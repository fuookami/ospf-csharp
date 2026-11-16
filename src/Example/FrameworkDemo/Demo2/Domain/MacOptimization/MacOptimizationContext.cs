#nullable enable

using System;
using System.Collections.Generic;

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Mac;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.MacOptimization.Service;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.MacOptimization;

/// <summary>
/// 重心优化上下文 / The MAC-optimisation context.
/// </summary>
/// <remarks>
/// 与对齐：**初始化幂等**（已初始化就跳过），**注册先建管线再逐条执行**。
///
/// 与 `SoftSecurityContext` 不同，这里的 `Register` **接收 `stowageMode`**——因为纵/横松弛的注册集合随模式变化。
/// 少传一个模式会让建议打板下也注册两个纵向松弛，而那里一个都不注册。
///
/// **initialisation is idempotent** (skipped once initialised) and **registration builds the pipelines then runs each**. Unlike `SoftSecurityContext`, this
/// `Register` **takes the `stowageMode`**, because the longitudinal and lateral slack sets depend on it; dropping it would register two longitudinal slacks under weight
/// recommendation, where the code registers none.
/// </remarks>
public sealed class MacOptimizationContext
{
    /// <summary>重心优化聚合 / The MAC-optimisation aggregation</summary>
    public MacOptimizationAggregation Aggregation { get; private set; } = null!;

    /// <summary>
    /// 从飞机、配载与重心上下文初始化 / Initialize from the aircraft, stowage and MAC contexts.
    /// </summary>
    /// <param name="aircraftContext">飞机上下文 / The aircraft context.</param>
    /// <param name="stowageContext">配载上下文 / The stowage context.</param>
    /// <param name="macContext">重心上下文 / The MAC context.</param>
    /// <param name="input">请求 DTO / The request DTO.</param>
    /// <returns>执行结果 / Execution result.</returns>
    public Try Init(
        AircraftContext aircraftContext,
        StowageContext stowageContext,
        MacContext macContext,
        RequestDTO input)
    {
        ArgumentNullException.ThrowIfNull(aircraftContext);
        ArgumentNullException.ThrowIfNull(stowageContext);
        ArgumentNullException.ThrowIfNull(macContext);

        if (Aggregation is not null)
        {
            return Results.Ok<Success>(Results.SuccessInstance);
        }

        Result<MacOptimizationAggregation, ErrorCode, Error<ErrorCode>> initialized =
            Service.AggregationInitializer.Initialize(
                aircraftContext.Aggregation,
                stowageContext.Aggregation,
                macContext.Aggregation,
                input);
        if (initialized is Failed<MacOptimizationAggregation, ErrorCode, Error<ErrorCode>> failed)
        {
            return new Failed<Success, ErrorCode, Error<ErrorCode>>(failed.Error);
        }
        Aggregation = ((Ok<MacOptimizationAggregation, ErrorCode, Error<ErrorCode>>)initialized).Value!;
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 注册重心优化目标到模型 / Register the MAC-optimisation objectives with the model.
    /// </summary>
    /// <param name="stowageMode">配载模式 / The stowage mode.</param>
    /// <param name="parameter">参数 / The parameter.</param>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>执行结果 / Execution result.</returns>
    public Try Register(StowageMode stowageMode, Parameter parameter, LinearMetaModel<Flt64> model)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        ArgumentNullException.ThrowIfNull(model);
        if (Aggregation is null)
        {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationFailed,
                "重心优化上下文尚未初始化 / the MAC-optimisation context is not initialised"));
        }

        // **必须先走聚合的 `Register`**：聚合的 `Register` 才建出松弛符号等子模型，而管线表里有一半是"读这些子模型"的。
        // 跳过它，模型不会报错——管线会在读到空属性时以"未注册"失败，**而失败点指向了错的那个上下文**。
        // **Go through the aggregation's `Register` first**: that is what builds the slack symbols and the rest of the sub-models, and half the pipeline list reads them.
        // Skipping it raises nothing in the model — the pipelines fail on an empty property with "not registered", **pointing at the wrong context**.
        Try registered = Aggregation.Register(stowageMode, model);
        if (!registered.IsOk)
        {
            return registered;
        }

        Result<IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>>, ErrorCode, Error<ErrorCode>> generated =
            new PipelineListGenerator(Aggregation).Invoke(stowageMode, parameter);
        if (generated is Failed<IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>>, ErrorCode, Error<ErrorCode>> failed)
        {
            return new Failed<Success, ErrorCode, Error<ErrorCode>>(failed.Error);
        }
        IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>> pipelines =
            ((Ok<IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>>, ErrorCode, Error<ErrorCode>>)generated).Value!;

        foreach (Func<LinearMetaModel<Flt64>, Try> pipeline in pipelines)
        {
            Try applied = pipeline(model);
            if (!applied.IsOk)
            {
                return applied;
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 注册 Benders 主问题约束 / Register the Benders master-problem constraints.
    /// </summary>
    /// <param name="stowageMode">配载模式 / The stowage mode.</param>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>执行结果 / Execution result.</returns>
    public Try RegisterForBendersMP(StowageMode stowageMode, LinearMetaModel<Flt64> model) =>
        Register(stowageMode, new Parameter(), model);

    /// <summary>
    /// 注册 Benders 子问题约束 / Register the Benders sub-problem constraints.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>执行结果 / Execution result.</returns>
    public Try RegisterForBendersSP(LinearMetaModel<Flt64> model) =>
        Results.Ok<Success>(Results.SuccessInstance);

    /// <summary>
    /// 刷新 Benders 子问题 / Flush the Benders sub-problem.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <param name="solution">子问题解 / The sub-problem solution.</param>
    /// <returns>执行结果 / Execution result.</returns>
    public Try FlushForBendersSP(LinearMetaModel<Flt64> model, IReadOnlyList<Flt64> solution) =>
        Results.Ok<Success>(Results.SuccessInstance);
}
