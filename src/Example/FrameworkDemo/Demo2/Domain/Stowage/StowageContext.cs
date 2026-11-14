#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage;

/// <summary>
/// 配载上下文。Context for managing stowage decisions.
/// </summary>
public sealed class StowageContext
{
    /// <summary>配载聚合；未初始化时为 `null` / The stowage aggregation, `null` before initialisation</summary>
    /// <remarks>
    /// **可空**。B606 之前是 `null!`，调用方在未初始化时拿到 `null` 却看不到任何提示，`Init` 也永远返回 `Ok`——
    /// 一个"成功"但什么都没做的上下文。现在未初始化就是 `null`，下游必须显式处理。
    ///
    /// **Nullable.** Before B606 this was `null!`: a caller that had not initialised saw `null` with no hint, and `Init` always returned `Ok` — a context that "succeeded"
    /// while doing nothing. Now an uninitialised context is simply `null`, which downstream must handle explicitly.
    /// </remarks>
    public StowageAggregation? Aggregation { get; private set; }

    /// <summary>
    /// 从飞机上下文和请求初始化配载上下文 / Initialise the stowage context from the aircraft context and the request.
    /// </summary>
    /// <param name="aircraftContext">飞机上下文 / The aircraft context.</param>
    /// <param name="input">请求 DTO / The request DTO.</param>
    /// <param name="stowageMode">装载模式 / The stowage mode.</param>
    /// <returns>执行结果 / Execution result.</returns>
    /// <remarks>
    /// 与基线一致：**已初始化过就不再重建**。失败只可能来自校验或内核构造，不会静默成功。
    ///
    /// As in the baseline, **an existing aggregation is not rebuilt**. A failure can only come from validation or kernel construction, never a silent success.
    /// </remarks>
    public Try Init(AircraftContext aircraftContext, RequestDTO input, StowageMode stowageMode = StowageMode.FullLoad)
    {
        ArgumentNullException.ThrowIfNull(aircraftContext);
        ArgumentNullException.ThrowIfNull(input);

        if (Aggregation is not null)
        {
            return Results.Ok<Success>(Results.SuccessInstance);
        }

        // 上游未初始化时返回失败，不让 `null` 穿进初始化器变成 `ArgumentNullException`。守卫放在共同入口上。
        // When the upstream is uninitialised this returns a failure instead of letting `null` reach the initialiser and become an `ArgumentNullException`; the guard sits on the
        // common entry.
        if (aircraftContext.Aggregation is not { } aircraftAggregation)
        {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationFailed,
                "飞机聚合未初始化 / the aircraft aggregation is not initialised"));
        }

        Result<StowageAggregation, ErrorCode, Error<ErrorCode>> result =
            Service.AggregationInitializer.Initialize(aircraftAggregation, input, stowageMode);
        if (result is Failed<StowageAggregation, ErrorCode, Error<ErrorCode>> failed)
        {
            return new Failed<Success, ErrorCode, Error<ErrorCode>>(failed.Error);
        }
        Aggregation = ((Ok<StowageAggregation, ErrorCode, Error<ErrorCode>>)result).Value;
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 注册配载聚合与全部约束管线 / Register the stowage aggregation and every constraint pipeline.
    /// </summary>
    /// <param name="stowageMode">装载模式 / The stowage mode.</param>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>执行结果 / Execution result.</returns>
    /// <remarks>
    /// 与基线一致：**先聚合、再生成管线、再逐条执行**。三步都按顺序短路——聚合失败就不生成，生成失败就不执行。
    ///
    /// As in the baseline: **aggregate first, then generate the pipelines, then run each**. All three short-circuit in order — a failed aggregation skips generation, and a
    /// failed generation skips execution.
    /// </remarks>
    public Try Register(StowageMode stowageMode, LinearMetaModel<Flt64> model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (Aggregation is not { } aggregation)
        {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationFailed,
                "配载上下文尚未初始化 / the stowage context is not initialised"));
        }

        Try registered = aggregation.Register(stowageMode, model);
        if (!registered.IsOk)
        {
            return registered;
        }

        Result<IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>>, ErrorCode, Error<ErrorCode>> generated =
            Service.PipelineListGenerator.Invoke(aggregation, stowageMode);
        if (generated is Failed<IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>>, ErrorCode, Error<ErrorCode>> failed)
        {
            return new Failed<Success, ErrorCode, Error<ErrorCode>>(failed.Error);
        }
        IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>> pipelines =
            ((Ok<IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>>, ErrorCode, Error<ErrorCode>>)generated).Value;

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
    /// <summary>Benders 主问题注册（转发到聚合）/ Benders master-problem registration, forwarded to the aggregation.</summary>
    /// <param name="stowageMode">配载模式 / Stowage mode</param>
    /// <param name="model">线性元模型 / Linear meta model</param>
    /// <returns>执行结果 / Execution result</returns>
    /// <remarks>
    /// **基线这一层是纯转发**（B825 实测）：`StowageContext.registerForBendersMP` 只调 `aggregation.registerForBendersMP`，没有任何自己的逻辑。
    /// 本仓此前没有它，于是 `StowageContext` 在 Benders 覆盖表上算"缺一套"。
    ///
    /// **This layer is a pure forwarder in the baseline** (measured by B825): the context only delegates to the aggregation. It was missing here, which is why
    /// `StowageContext` counted as "one set short" in the Benders coverage table.
    /// </remarks>
    public Try RegisterForBendersMP(StowageMode stowageMode, LinearMetaModel<Flt64> model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (Aggregation is not { } aggregation)
        {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationFailed,
                "配载上下文尚未初始化 / the stowage context is not initialised"));
        }
        return aggregation.RegisterForBendersMP(stowageMode, model);
    }

    /// <summary>Benders 子问题注册（转发到聚合）/ Benders sub-problem registration, forwarded to the aggregation.</summary>
    /// <param name="stowageMode">配载模式 / Stowage mode</param>
    /// <param name="model">线性元模型 / Linear meta model</param>
    /// <param name="solution">主问题解 / The master-problem solution</param>
    /// <returns>执行结果 / Execution result</returns>
    public Try RegisterForBendersSP(StowageMode stowageMode, LinearMetaModel<Flt64> model, IReadOnlyList<double> solution)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (Aggregation is not { } aggregation)
        {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationFailed,
                "配载上下文尚未初始化 / the stowage context is not initialised"));
        }
        return aggregation.RegisterForBendersSP(stowageMode, model);
    }

    /// <summary>
    /// Benders **单模型**路径的追索注册（转发到聚合，不带业载）。
    /// The recourse registration for the **single-model** Benders path (forwarded to the aggregation, without the payload).
    /// </summary>
    /// <param name="model">线性元模型 / Linear meta model</param>
    /// <returns>执行结果 / Execution result</returns>
    /// <remarks>
    /// **前提是同一个模型上已经跑过 `RegisterForBendersMP`**：业载只能注册一次，重复会失败（判据实测过）。
    /// 这里不接 `solution` 参数，因为该聚合的注册根本不读它（B833 钉住的前提）。
    ///
    /// **The premise is that `RegisterForBendersMP` already ran on the same model**: the payload can be registered only once and a repeat fails (measured by a
    /// criterion). No `solution` parameter here, because that aggregation's registration never reads it (a premise pinned in B833).
    /// </remarks>
    public Try RegisterForBendersSPWithoutPayload(LinearMetaModel<Flt64> model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (Aggregation is not { } aggregation)
        {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationFailed,
                "配载上下文尚未初始化 / the stowage context is not initialised"));
        }
        return aggregation.RegisterForBendersSPWithoutPayload(model);
    }

    /// <summary>Benders 子问题刷新 / Benders sub-problem flush.</summary>
    /// <param name="stowageMode">配载模式 / Stowage mode</param>
    /// <param name="model">线性元模型 / Linear meta model</param>
    /// <param name="solution">主问题解 / The master-problem solution</param>
    /// <returns>执行结果 / Execution result</returns>
    /// <remarks>
    /// **基线这里是个怪处，照抄并记下来**：`flushForBendersSP` 的转发目标是 `aggregation.registerForBendersSP`，**不是某个 flush**。
    /// 换句话说"刷新"在配载域等同于"再注册一次子问题"。看起来像笔误，但**没有证据支持改写它**（B785→B786 的规矩），所以照抄。
    ///
    /// **This is the baseline quirk, copied and named**: the flush forwards to `aggregation.registerForBendersSP`, **not to any flush** — "flush" here means
    /// "register the sub-problem again". It looks like a typo, but **there is no evidence to rewrite it** (the B785→B786 rule), so it is copied.
    /// </remarks>
    public Try FlushForBendersSP(StowageMode stowageMode, LinearMetaModel<Flt64> model, IReadOnlyList<double> solution)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (Aggregation is not { } aggregation)
        {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationFailed,
                "配载上下文尚未初始化 / the stowage context is not initialised"));
        }
        return aggregation.RegisterForBendersSP(stowageMode, model);
    }
}
