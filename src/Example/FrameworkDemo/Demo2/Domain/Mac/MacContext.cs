#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Mac;

/// <summary>
/// MAC 上下文。Context for managing MAC (Mean Aerodynamic Chord) computations.
/// </summary>
public sealed class MacContext
{
    /// <summary>MAC 聚合；未初始化时为 `null` / The MAC aggregation, `null` before initialisation</summary>
    /// <remarks>
    /// **可空**。 之前是 `null!` 且 `Init` 永远返回 `Ok`——调用方拿到一个"成功"但什么都没做的上下文。
    ///
    /// **Nullable.** Before this was `null!` with `Init` always returning `Ok` — a context that "succeeded" while doing nothing.
    /// </remarks>
    public MacAggregation? Aggregation { get; private set; }

    /// <summary>
    /// 从飞机和配载上下文初始化 MAC 上下文 / Initialise the MAC context from the aircraft and stowage contexts.
    /// </summary>
    /// <param name="aircraftContext">飞机上下文 / The aircraft context.</param>
    /// <param name="stowageContext">配载上下文 / The stowage context.</param>
    /// <param name="input">请求 DTO / The request DTO.</param>
    /// <returns>执行结果 / Execution result.</returns>
    public Try Init(AircraftContext aircraftContext, StowageContext stowageContext, RequestDTO input)
    {
        ArgumentNullException.ThrowIfNull(aircraftContext);
        ArgumentNullException.ThrowIfNull(stowageContext);
        ArgumentNullException.ThrowIfNull(input);

        if (Aggregation is not null)
        {
            return Results.Ok<Success>(Results.SuccessInstance);
        }

        // 上游未初始化时返回失败，不让 `null` 穿进初始化器。守卫放在共同入口上。
        // When an upstream is uninitialised this returns a failure instead of letting `null` reach the initialiser; the guard sits on the common entry.
        if (aircraftContext.Aggregation is not { } aircraftAggregation)
        {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationFailed,
                "飞机聚合未初始化 / the aircraft aggregation is not initialised"));
        }
        if (stowageContext.Aggregation is not { } stowageAggregation)
        {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationFailed,
                "配载聚合未初始化 / the stowage aggregation is not initialised"));
        }

        Result<MacAggregation, ErrorCode, Error<ErrorCode>> result =
            Service.AggregationInitializer.Initialize(aircraftAggregation, stowageAggregation, input);
        if (result is Failed<MacAggregation, ErrorCode, Error<ErrorCode>> failed)
        {
            return new Failed<Success, ErrorCode, Error<ErrorCode>>(failed.Error);
        }
        Aggregation = ((Ok<MacAggregation, ErrorCode, Error<ErrorCode>>)result).Value;
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 注册到模型 / Register with the model.
    /// </summary>
    /// <param name="stowageMode">配载模式 / The stowage mode.</param>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>执行结果 / Execution result.</returns>
    /// <remarks>
    /// **只委托聚合**，不再自己判断模式。 之前这个方法缺失，
    /// 所以跨域编排里 `macContext.register` 那一步无处可调。
    ///
    /// **It only delegates to the aggregation** and does not re-decide the mode. Before this the method was missing, so the cross-domain orchestration's
    /// `macContext.register` step had nothing to call.
    /// </remarks>
    public Try Register(StowageMode stowageMode, LinearMetaModel<Flt64> model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (Aggregation is not { } aggregation)
        {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationFailed,
                "MAC 上下文尚未初始化 / the MAC context is not initialised"));
        }
        return aggregation.Register(stowageMode, model);
    }
    /// <summary>Benders 主问题注册（转发到聚合）/ Benders master-problem registration, forwarded to the aggregation.</summary>
    /// <param name="stowageMode">配载模式 / Stowage mode</param>
    /// <param name="model">线性元模型 / Linear meta model</param>
    /// <returns>执行结果 / Execution result</returns>
    public Try RegisterForBendersMP(StowageMode stowageMode, LinearMetaModel<Flt64> model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (Aggregation is not { } aggregation)
        {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationFailed,
                "MAC 上下文尚未初始化 / the MAC context is not initialised"));
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
                "MAC 上下文尚未初始化 / the MAC context is not initialised"));
        }
        return aggregation.RegisterForBendersSP(stowageMode, model);
    }

    /// <summary>Benders 子问题刷新（空操作）/ Benders sub-problem flush (a no-op).</summary>
    /// <param name="model">线性元模型 / Linear meta model</param>
    /// <param name="solution">主问题解 / The master-problem solution</param>
    /// <returns>执行结果 / Execution result</returns>
    /// <remarks>
    /// **这一层与配载域不同**：MAC 的 `flushForBendersSP` 是**空实现**，而且**不收模式**（签名就是 `(model, solution)`）。
    /// 两处不一致是既有的，沿用而不是统一——**统一需要证据，这里没有。**
    ///
    /// **Unlike the stowage domain**: MAC flush is a **no-op** and **takes no stowage mode** — the signature is `(model, solution)`. The two
    /// domains disagree as written; it is kept rather than unified, because **unifying needs evidence and there is none.**
    /// </remarks>
    public Try FlushForBendersSP(LinearMetaModel<Flt64> model, IReadOnlyList<double> solution) =>
        Results.Ok<Success>(Results.SuccessInstance);
}
