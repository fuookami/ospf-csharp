#nullable enable

using System;
using System.Collections.Generic;

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.LoadingEffectiveness.Service;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.LoadingEffectiveness;

/// <summary>
/// 装载效能上下文 / The loading-effectiveness context.
/// </summary>
/// <remarks>
/// 与基线 `loading_effectiveness/LoadingEffectivenessContext.kt` 对齐：**初始化幂等**，**`Init` 收 `stowageMode`**，
/// 注册时先派生四个子模型再生成十二条管线。
///
/// **一处刻意的参数删减**：基线的 `init` 还收 `aircraftContext`，但本仓的 `AggregationInitializer` 用不到飞机的任何量
/// （重量单位那条路径在 B624 已改成"调用方用同一单位"），所以这里**不接一个没有消费者的参数**——接了就只是把
/// "这个参数必须有"变成一句空话。
///
/// Aligned with the baseline: **initialisation is idempotent**, **`Init` takes the `stowageMode`**, and registration derives the four sub-models before generating the twelve
/// pipelines.
///
/// **One deliberate parameter omission**: the baseline's `init` also takes an `aircraftContext`, but this port's `AggregationInitializer` reads nothing from the aircraft (the weight-unit
/// path became "the caller uses one unit" in B624), so this port **does not accept a parameter with no consumer** — accepting it would only turn "this parameter must exist" into an empty
/// promise.
/// </remarks>
public sealed class LoadingEffectivenessContext
{
    /// <summary>
    /// 用空上下文构造 / Construct an empty context.
    /// </summary>
    public LoadingEffectivenessContext()
    {
    }

    /// <summary>
    /// 用现成的聚合构造上下文 / Construct a context around an existing aggregation.
    /// </summary>
    /// <param name="aggregation">装载效能聚合 / The loading-effectiveness aggregation.</param>
    /// <remarks>
    /// 与其它子域同一形态：依赖注入用。给了聚合之后 <see cref="Init"/> 直接返回成功，不会重建。
    ///
    /// The same shape as the other sub-domains, for dependency injection: with an aggregation supplied, <see cref="Init"/> succeeds without rebuilding.
    /// </remarks>
    public LoadingEffectivenessContext(LoadingEffectivenessAggregation aggregation)
    {
        ArgumentNullException.ThrowIfNull(aggregation);
        Aggregation = aggregation;
    }

    /// <summary>聚合 / The aggregation</summary>
    public LoadingEffectivenessAggregation Aggregation { get; private set; } = null!;

    /// <summary>
    /// 初始化 / Initialize.
    /// </summary>
    /// <param name="stowageContext">配载上下文 / The stowage context.</param>
    /// <param name="input">请求 DTO / The request DTO.</param>
    /// <param name="stowageMode">装载模式 / The stowage mode.</param>
    /// <returns>执行结果 / Execution result.</returns>
    public Try Init(StowageContext stowageContext, RequestDTO input, StowageMode stowageMode)
    {
        ArgumentNullException.ThrowIfNull(stowageContext);

        // **幂等**：已初始化就直接成功。
        // **Idempotent**: already initialised means immediate success.
        if (Aggregation is not null)
        {
            return Results.Ok<Success>(Results.SuccessInstance);
        }

        Result<LoadingEffectivenessAggregation, ErrorCode, Error<ErrorCode>> initialized =
            AggregationInitializer.Initialize(stowageContext.Aggregation, input, stowageMode);
        if (initialized is Failed<LoadingEffectivenessAggregation, ErrorCode, Error<ErrorCode>> failed)
        {
            return new Failed<Success, ErrorCode, Error<ErrorCode>>(failed.Error);
        }
        Aggregation = ((Ok<LoadingEffectivenessAggregation, ErrorCode, Error<ErrorCode>>)initialized).Value!;
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
                "装载效能上下文尚未初始化 / the loading-effectiveness context is not initialised"));
        }

        // **必须走聚合的 `Register`，不能直接调生成器**：聚合的 `Register` 先注册 `Load` 再派生四个子模型，
        // 跳过它会让依赖子模型的那些管线全部消失，而模型看上去"注册成功"。
        // **Must go through the aggregation's `Register`, not the generator directly**: the aggregation registers `Load` and then derives the four sub-models; skipping it makes every
        // sub-model-dependent pipeline vanish while registration still reports success.
        return Aggregation.Register(stowageMode, parameter, model);
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
    /// <remarks>
    /// 基线的子问题在这里**没有额外约束**（`loading_effectiveness` 的目标全在主问题），所以返回成功是**如实**的，
    /// 不是占位壳。
    ///
    /// The baseline adds **no extra constraints** to the sub-problem here (every `loading_effectiveness` objective lives in the master problem), so returning success is **accurate**
    /// rather than a placeholder shell.
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
    /// 同上：没有需要从子问题解里回填的量，返回成功如实。
    ///
    /// As above: there is nothing to back-fill from the sub-problem solution, so success is accurate.
    /// </remarks>
    public Try FlushForBendersSP(LinearMetaModel<Flt64> model, IReadOnlyList<Flt64> solution) =>
        Results.Ok<Success>(Results.SuccessInstance);
}
