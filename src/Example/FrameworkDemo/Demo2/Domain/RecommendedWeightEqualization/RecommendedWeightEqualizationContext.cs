#nullable enable

using System;
using System.Collections.Generic;

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.RecommendedWeightEqualization.Service;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.RecommendedWeightEqualization;

/// <summary>
/// 推荐重量均衡上下文 / The recommended-weight-equalisation context.
/// </summary>
/// <remarks>
/// 与基线 `recommended_weight_equalization/RecommendedWeightEqualizationContext.kt` 对齐：**初始化幂等**，**注册先建管线再逐条执行**。
///
/// 与 `MacOptimizationContext` 一样，`Register` **接收 `stowageMode`**——生成器只认建议打板模式，其他模式返回空列表。
///
/// Aligned with the baseline: **initialisation is idempotent** and **registration builds the pipelines then runs each**. Like `MacOptimizationContext`, `Register` **takes the
/// `stowageMode`**, since the generator honours weight recommendation only and returns an empty list otherwise.
/// </remarks>
public sealed class RecommendedWeightEqualizationContext
{
    /// <summary>聚合 / The aggregation</summary>
    public RecommendedWeightEqualizationAggregation Aggregation { get; private set; } = null!;

    /// <summary>
    /// 初始化 / Initialize.
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

        Result<RecommendedWeightEqualizationAggregation, ErrorCode, Error<ErrorCode>> initialized =
            Service.AggregationInitializer.Initialize(
                aircraftContext.Aggregation, stowageContext.Aggregation, input);
        if (initialized is Failed<RecommendedWeightEqualizationAggregation, ErrorCode, Error<ErrorCode>> failed)
        {
            return new Failed<Success, ErrorCode, Error<ErrorCode>>(failed.Error);
        }
        Aggregation = ((Ok<RecommendedWeightEqualizationAggregation, ErrorCode, Error<ErrorCode>>)initialized).Value!;
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 注册到模型 / Register with the model.
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
                "推荐重量均衡上下文尚未初始化 / the recommended-weight-equalisation context is not initialised"));
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
