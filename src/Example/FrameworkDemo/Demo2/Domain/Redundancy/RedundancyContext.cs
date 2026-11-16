#nullable enable

using System;
using System.Collections.Generic;

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Redundancy.Service;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Redundancy;

/// <summary>
/// 冗余上下文 / The redundancy context.
/// </summary>
/// <remarks>
/// 与对齐：**初始化幂等**，`Init` **不收 `stowageMode`**（冗余的聚合与模式无关，
/// 模式只在生成管线时用）。
///
/// 这一点与装载效能上下文**不同**：那边 `Init` 必须收模式，因为聚合要按模式二选一地建子模型；这边两个子模型无条件都建。
///
/// **initialisation is idempotent** and `Init` **does not take the `stowageMode`** (the redundancy aggregation is mode-independent; the mode only matters when
/// generating pipelines).
///
/// This differs from the loading-effectiveness context, whose `Init` must take the mode because its aggregation builds one of two sub-models by mode, while here both sub-models are
/// always built.
/// </remarks>
public sealed class RedundancyContext
{
    /// <summary>
    /// 用空上下文构造 / Construct an empty context.
    /// </summary>
    public RedundancyContext()
    {
    }

    /// <summary>
    /// 用现成的聚合构造上下文 / Construct a context around an existing aggregation.
    /// </summary>
    /// <param name="aggregation">冗余聚合 / The redundancy aggregation.</param>
    /// <remarks>
    /// 与其它子域同一形态：依赖注入用。给了聚合之后 <see cref="Init"/> 直接返回成功，不会重建。
    ///
    /// The same shape as the other sub-domains, for dependency injection: with an aggregation supplied, <see cref="Init"/> succeeds without rebuilding.
    /// </remarks>
    public RedundancyContext(RedundancyAggregation aggregation)
    {
        ArgumentNullException.ThrowIfNull(aggregation);
        Aggregation = aggregation;
    }

    /// <summary>聚合 / The aggregation</summary>
    public RedundancyAggregation Aggregation { get; private set; } = null!;

    /// <summary>
    /// 初始化 / Initialize.
    /// </summary>
    /// <param name="stowageContext">配载上下文 / The stowage context.</param>
    /// <param name="input">请求 DTO / The request DTO.</param>
    /// <returns>执行结果 / Execution result.</returns>
    public Try Init(StowageContext stowageContext, RequestDTO input)
    {
        ArgumentNullException.ThrowIfNull(stowageContext);

        // **幂等**：已初始化就直接成功。
        // **Idempotent**: already initialised means immediate success.
        if (Aggregation is not null)
        {
            return Results.Ok<Success>(Results.SuccessInstance);
        }

        Result<RedundancyAggregation, ErrorCode, Error<ErrorCode>> initialized =
            AggregationInitializer.Initialize(stowageContext.Aggregation, input);
        if (initialized is Failed<RedundancyAggregation, ErrorCode, Error<ErrorCode>> failed)
        {
            return new Failed<Success, ErrorCode, Error<ErrorCode>>(failed.Error);
        }
        Aggregation = ((Ok<RedundancyAggregation, ErrorCode, Error<ErrorCode>>)initialized).Value!;
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
                "冗余上下文尚未初始化 / the redundancy context is not initialised"));
        }

        // **先注册子模型（冗余 → 纵向平衡），再走生成器（纵向平衡 → 冗余）**：两侧顺序相反是基线自带的。
        // **Register the sub-models (redundancy, then balance), then run the generator (balance, then redundancy)**: the two opposite orders are its own.
        Try registered = Aggregation.Register(model);
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
    /// <param name="parameter">参数 / The parameter.</param>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>执行结果 / Execution result.</returns>
    public Try RegisterForBendersMP(StowageMode stowageMode, Parameter parameter, LinearMetaModel<Flt64> model) =>
        Register(stowageMode, parameter, model);

    /// <summary>
    /// 注册 Benders 子问题约束 / Register the Benders sub-problem constraints.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>执行结果 / Execution result.</returns>
    /// <remarks>
    /// 子问题**没有额外约束**（冗余的目标全在主问题），返回成功如实。
    ///
    /// The code adds **no extra constraints** to the sub-problem (every redundancy objective lives in the master problem), so success is accurate.
    /// </remarks>
    public Try RegisterForBendersSP(LinearMetaModel<Flt64> model) =>
        Results.Ok<Success>(Results.SuccessInstance);

    /// <summary>
    /// 刷新 Benders 子问题 / Flush the Benders sub-problem.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <param name="solution">子问题解 / The sub-problem solution.</param>
    /// <returns>执行结果 / Execution result.</returns>
    /// <remarks>
    /// 同上：没有需要回填的量。
    ///
    /// As above: nothing to back-fill.
    /// </remarks>
    public Try FlushForBendersSP(LinearMetaModel<Flt64> model, IReadOnlyList<Flt64> solution) =>
        Results.Ok<Success>(Results.SuccessInstance);
}
