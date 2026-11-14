#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Service;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Mac;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity;

/// <summary>
/// 适航安全上下文 / Context for managing airworthiness and safety constraints across aircraft, stowage, and MAC domains.
/// </summary>
/// <remarks>
/// 与基线 `AirworthinessSecurityContext.kt` 对齐。它把"建聚合"与"往模型里注册"分成两步：
///
/// 1. <see cref="Init"/> 建聚合（B605 起可用，见 <see cref="AggregationInitializer"/>）。
/// 2. <see cref="Register"/> 用聚合注册约束——**已实现**，顺序是 聚合注册 → 生成管线 → 逐条执行。
///
/// 三处容易做错：
///
/// 1. **`Register` 是两步，不是一步**。只调 `aggregation.Register` 不会加任何约束行（聚合只注册符号），真正成行的是后面那 14 条管线。
///    漏掉第二步会得到一个"注册成功、一行约束都没有"的模型。
/// 2. **`RegisterForBendersMP` 是空操作**，基线就是 `return ok`，且**不转发**给聚合的同名方法。转发与不转发在基线里等价（聚合那个也是空操作），照抄不转发。
/// 3. **`RegisterForBendersSP` 就是 `Register`**，忽略 `solution`；`FlushForBendersSP` 是空操作。
///
/// Aligned with the baseline. It splits "build the aggregation" from "register into the model": `Init` is **still missing here** (see
/// <see cref="AggregationInitializer"/>), and `Register` is implemented as aggregate-register then generate pipelines then run each. Three easy
/// mistakes: (1) **`Register` is two steps** — calling only `aggregation.Register` adds no constraint row (the aggregation registers symbols) and the fourteen pipelines are what
/// become rows, so skipping the second step yields a model that registered "successfully" with not one constraint in it; (2) **`RegisterForBendersMP` is a no-op** (`return ok`) and
/// does **not** forward to the aggregation's same-named method — forwarding and not forwarding are equivalent because that one is a no-op too, so the baseline's form is copied;
/// (3) **`RegisterForBendersSP` is just `Register`**, ignoring `solution`, and `FlushForBendersSP` is a no-op.
/// </remarks>
public sealed class AirworthinessSecurityContext
{
    private AirworthinessSecurityAggregation? _aggregation;

    /// <summary>
    /// 构造未初始化的上下文 / Construct an uninitialised context.
    /// </summary>
    /// <remarks>
    /// 基线是 `lateinit var aggregation`，正常路径是 <see cref="Init"/>。这个无参构造保留给该路径。
    ///
    /// The baseline uses `lateinit var aggregation` and the normal path is <see cref="Init"/>; this constructor is kept for it.
    /// </remarks>
    public AirworthinessSecurityContext() {
    }

    /// <summary>
    /// 用现成的聚合构造上下文 / Construct a context around an existing aggregation.
    /// </summary>
    /// <param name="aggregation">适航安全聚合 / The airworthiness security aggregation.</param>
    /// <remarks>
    /// **两条入口并存：`Init` 从上游聚合建，这个构造直接收现成的聚合**（依赖注入、或调用方已经建好了）。基线只有 `init` 一条，
    /// 多出来的这条不是"等移植的临时通道"——**`Init` 早就在了**（B605 起 `AggregationInitializer` 真正构造聚合），
    /// 所以这里不再有"唯一入口"这种说法。
    ///
    /// **Two entry points coexist: `Init` builds from the upstream aggregations, this constructor takes a ready-made one** (dependency injection, or a caller that already
    /// built it). The baseline has only `init`; the extra one is not a "temporary channel waiting for the port" — **`Init` has been there since B605**, so there is no
    /// "only entry point" left to speak of.
    /// </remarks>
    public AirworthinessSecurityContext(AirworthinessSecurityAggregation aggregation) {
        ArgumentNullException.ThrowIfNull(aggregation);
        _aggregation = aggregation;
        Aggregation = aggregation;
    }

    /// <summary>适航安全聚合；未初始化时为 `null` / The aggregation, `null` before initialisation.</summary>
    public AirworthinessSecurityAggregation? Aggregation { get; private set; }

    /// <summary>
    /// 从飞机、配载和 MAC 上下文初始化 / Initialise from the aircraft, stowage and MAC contexts.
    /// </summary>
    /// <param name="aircraftContext">飞机上下文 / Aircraft context</param>
    /// <param name="stowageContext">配载上下文 / Stowage context</param>
    /// <param name="macContext">MAC 上下文 / MAC context</param>
    /// <param name="input">请求 DTO / Request DTO</param>
    /// <returns>执行结果 / Execution result</returns>
    /// <remarks>
    /// 与基线一致：**已初始化过就不再重建**。B605 起 `AggregationInitializer` 真正构造聚合，失败只可能来自 DTO 校验
    /// （没有舱位、上限为负、包线区间反了等），而不是"尚未移植"。
    ///
    /// As in the baseline, **an existing aggregation is not rebuilt**. Since B605 `AggregationInitializer` really builds the aggregation, so a failure can only come from DTO
    /// validation (no positions, a negative limit, an inverted envelope interval) rather than "not ported yet".
    /// </remarks>
    public Try Init(AircraftContext aircraftContext, StowageContext stowageContext, MacContext macContext, RequestDTO input) {
        ArgumentNullException.ThrowIfNull(aircraftContext);
        ArgumentNullException.ThrowIfNull(stowageContext);
        ArgumentNullException.ThrowIfNull(macContext);
        ArgumentNullException.ThrowIfNull(input);

        if (_aggregation is not null) {
            return Results.Ok<Success>(Results.SuccessInstance);
        }

        // 上游上下文未初始化时**返回失败**，不让 `null` 穿到初始化器里变成 `ArgumentNullException`。
        // 守卫放在这里而不是初始化器里：`Init` 是所有调用方的共同入口，一处挡住即可，且异常类型对调用方毫无用处。
        // When an upstream context is uninitialised this **returns a failure** instead of letting `null` reach the initialiser and become an `ArgumentNullException`. The guard
        // sits here rather than in the initialiser because `Init` is every caller's common entry, one guard covers them all, and the exception type tells a caller nothing.
        if (aircraftContext.Aggregation is not { } aircraftAggregation
            || stowageContext.Aggregation is not { } stowageAggregation
            || macContext.Aggregation is not { } macAggregation) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationFailed,
                "上游聚合未初始化 / an upstream aggregation is not initialised"));
        }

        Result<AirworthinessSecurityAggregation, ErrorCode, Error<ErrorCode>> result = AggregationInitializer.Initialize(
            aircraftAggregation,
            stowageAggregation,
            macAggregation,
            input);
        if (result is Failed<AirworthinessSecurityAggregation, ErrorCode, Error<ErrorCode>> failed) {
            return new Failed<Success, ErrorCode, Error<ErrorCode>>(failed.Error);
        }
        AirworthinessSecurityAggregation built = ((Ok<AirworthinessSecurityAggregation, ErrorCode, Error<ErrorCode>>)result).Value!;
        // **两处都要写**：短路读的是私有字段 `_aggregation`，公开属性只是给外部看。只写属性时短路永远不成立，
        // 每次 `Init` 都重建一份聚合——**而旧引用还指着上一份，注册出去的约束来自两张不同的聚合**。
        // **Both must be written**: the short-circuit reads the private `_aggregation`, the public property is for outside callers. Writing only the property leaves the short-circuit
        // permanently false, rebuilding an aggregation on every `Init` — **while old references still point at the previous one, so registered constraints come from two different
        // aggregations**.
        _aggregation = built;
        Aggregation = built;
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 注册适航安全约束到模型 / Register the airworthiness security constraints with the model.
    /// </summary>
    /// <param name="stowageMode">配载模式 / Stowage mode</param>
    /// <param name="model">线性元模型 / Linear meta model</param>
    /// <returns>执行结果 / Execution result</returns>
    public Try Register(StowageMode stowageMode, LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        if (Aggregation is not { } aggregation) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationFailed,
                "适航安全上下文尚未初始化 / the airworthiness security context is not initialised"));
        }

        Try registration = aggregation.Register(stowageMode, model);
        if (!registration.IsOk) {
            return registration;
        }

        Result<IReadOnlyList<SecurityPipeline>, ErrorCode, Error<ErrorCode>> generated = new PipelineListGenerator(aggregation).Invoke(stowageMode);
        if (generated is Failed<IReadOnlyList<SecurityPipeline>, ErrorCode, Error<ErrorCode>> generationFailed) {
            return new Failed<Success, ErrorCode, Error<ErrorCode>>(generationFailed.Error);
        }
        foreach (SecurityPipeline pipeline in ((Ok<IReadOnlyList<SecurityPipeline>, ErrorCode, Error<ErrorCode>>)generated).Value!) {
            Try invoked = pipeline.Invoke(model);
            if (!invoked.IsOk) {
                return invoked;
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// Benders 主问题注册（空操作）/ Benders master-problem registration (a no-op).
    /// </summary>
    /// <param name="model">线性元模型 / Linear meta model</param>
    /// <returns>执行结果 / Execution result</returns>
    /// <remarks>
    /// 基线是 `return ok`，而且**不转发**给聚合的 `registerForBendersMP`。两者在基线里都是空操作，所以"转发"看不出区别——但转发会让将来聚合那边一改就悄悄改变这里的行为。
    /// 不转发是更保守的读法。
    ///
    /// The baseline is `return ok` and does **not** forward to the aggregation's `registerForBendersMP`. Both are no-ops there, so forwarding looks identical — but forwarding
    /// would silently change this behaviour the day the aggregation's version grows. Not forwarding is the more conservative reading.
    /// </remarks>
    public Try RegisterForBendersMP(LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// Benders 子问题注册（等价于 <see cref="Register"/>）/ Benders sub-problem registration (equivalent to <see cref="Register"/>).
    /// </summary>
    /// <param name="stowageMode">配载模式 / Stowage mode</param>
    /// <param name="model">线性元模型 / Linear meta model</param>
    /// <param name="solution">主问题的解 / The solution from the master problem.</param>
    /// <returns>执行结果 / Execution result</returns>
    public Try RegisterForBendersSP(StowageMode stowageMode, LinearMetaModel<Flt64> model, IReadOnlyList<double> solution) {
        ArgumentNullException.ThrowIfNull(solution);
        return Register(stowageMode, model);
    }

    /// <summary>
    /// Benders 子问题刷新（空操作）/ Benders sub-problem flush (a no-op).
    /// </summary>
    /// <param name="model">线性元模型 / Linear meta model</param>
    /// <param name="solution">主问题的解 / The solution from the master problem.</param>
    /// <returns>执行结果 / Execution result</returns>
    public Try FlushForBendersSP(LinearMetaModel<Flt64> model, IReadOnlyList<double> solution) {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(solution);
        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
