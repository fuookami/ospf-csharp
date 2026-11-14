#nullable enable

using System;
using System.Collections.Generic;

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.PayloadMaximization.Service;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.PayloadMaximization;

/// <summary>
/// 业载最大化上下文 / The payload-maximization context.
/// </summary>
/// <remarks>
/// 与基线 `payload_maximization/PayloadMaximizationContext.kt` 对齐：**初始化只做一次**（`aggregation` 已初始化就跳过），
/// **注册先建管线再逐条执行**。
///
/// 一处必须照抄：**初始化是幂等的**。基线的 `if (!::aggregation.isInitialized)` 意味着第二次 `init` 不会重建聚合——
/// 重建会把上一次的符号引用丢掉，而模型里还挂着那些符号。
///
/// Aligned with the baseline: **initialisation happens once** (skipped when the aggregation already exists) and **registration builds the pipelines then runs each**. One thing must be
/// copied: **initialisation is idempotent** — the baseline's `if (!::aggregation.isInitialized)` means a second `init` does not rebuild the aggregation, which would drop the previous
/// symbol references while the model still holds them.
/// </remarks>
public sealed class PayloadMaximizationContext
{
    /// <summary>业载最大化聚合 / The payload-maximization aggregation</summary>
    public PayloadMaximizationAggregation Aggregation { get; private set; } = null!;

    /// <summary>
    /// 从飞机和配载上下文初始化业载最大化上下文 / Initialize the payload-maximization context from the aircraft and stowage contexts.
    /// </summary>
    /// <param name="aircraftContext">飞机上下文 / The aircraft context.</param>
    /// <param name="stowageContext">配载上下文 / The stowage context.</param>
    /// <param name="input">请求 DTO / The request DTO.</param>
    /// <returns>执行结果 / Execution result.</returns>
    public Try Init(AircraftContext aircraftContext, StowageContext stowageContext, RequestDTO input)
    {
        ArgumentNullException.ThrowIfNull(aircraftContext);
        ArgumentNullException.ThrowIfNull(stowageContext);

        if (Aggregation is not null)
        {
            return Results.Ok<Success>(Results.SuccessInstance);
        }

        // **上游可能未初始化**（B606 起配载聚合可空）。取非空会抛 `NullReferenceException`，那是崩溃不是失败。
        // **The upstream may be uninitialised** (nullable since B606). Taking non-null throws a `NullReferenceException`, which is a crash rather than a failure.
        if (aircraftContext.Aggregation is not { } aircraftAggregation)
        {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationFailed,
                "飞机上下文尚未初始化 / the aircraft context is not initialised"));
        }
        if (stowageContext.Aggregation is not { } stowageAggregation)
        {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationFailed,
                "配载上下文尚未初始化 / the stowage context is not initialised"));
        }

        Aggregation = new PayloadMaximizationAggregation(
            aircraftAggregation.AircraftModel,
            stowageAggregation.Payload);
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 注册业载最大化目标到模型 / Register the payload-maximization objective with the model.
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
                "业载最大化上下文尚未初始化 / the payload-maximization context is not initialised"));
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
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>执行结果 / Execution result.</returns>
    public Try RegisterForBendersMP(LinearMetaModel<Flt64> model)
    {
        return Register(StowageMode.FullLoad, new Parameter(), model);
    }

    /// <summary>
    /// 注册 Benders 子问题约束 / Register the Benders sub-problem constraints.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>执行结果 / Execution result.</returns>
    public Try RegisterForBendersSP(LinearMetaModel<Flt64> model)
    {
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 刷新 Benders 子问题 / Flush the Benders sub-problem.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <param name="solution">子问题解值 / The sub-problem solution.</param>
    /// <returns>执行结果 / Execution result.</returns>
    public Try FlushForBendersSP(LinearMetaModel<Flt64> model, IReadOnlyList<Flt64> solution)
    {
        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
