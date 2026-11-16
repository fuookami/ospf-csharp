#nullable enable

using System;

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.ExpressEffectiveness;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Mac;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.PayloadMaximization;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.RecommendedWeightEqualization;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;

/// <summary>
/// 建议重量管线上下文 / The weight-recommendation pipeline context.
/// </summary>
/// <remarks>
/// **七个上下文、固定的初始化顺序、固定的注册顺序**。
///
/// 两条顺序都必须沿用，且**两者不一样**：
///
/// | 阶段 | 顺序 |
/// | --- | --- |
/// | `Init` | 飞机 → 配载 → MAC → 适航安全 → 货物时效 → 建议重量均衡 → 载荷最大化 |
/// | `Register` | 配载 → MAC → 适航安全 → 货物时效 → 建议重量均衡 → 载荷最大化 |
///
/// **`Register` 里没有飞机**——飞机聚合只提供机型参数，不往模型上写任何东西。把它顺手加进 `Register` 会多注册一批
/// 本不属于该管线的行。
///
/// 全套流程**只在 `StowageMode.WeightRecommendation` 下跑**，这个常量在每一处调用上都要传，包括 `Init` 里那些
/// 收模式的子域。
///
/// **seven contexts, a fixed initialisation order and a fixed registration order**, and the two differ. `Init` runs aircraft, stowage, MAC, airworthiness,
/// express effectiveness, recommended-weight equalisation and payload maximisation; `Register` runs stowage, MAC, airworthiness, express effectiveness, recommended-weight equalisation
/// and payload maximisation. **`Register` omits the aircraft** — that aggregation only supplies model parameters and writes nothing to the model, so adding it would register rows that do
/// not belong to this pipeline. The whole flow runs **only under `StowageMode.WeightRecommendation`**, which must be passed at every call site, including the sub-domains whose `Init`
/// takes a mode.
/// </remarks>
public sealed class WeightRecommendationPipelineContext
{
    /// <summary>
    /// 全套流程使用的装载模式 / The stowage mode the whole flow runs under.
    /// </summary>
    /// <remarks>
    /// 单点定义，避免六个子域各自写一遍常量。每一处调用都写字面量 `StowageMode.WeightRecommendation`，
    /// 本仓用一个常量收敛——语义相同，改成别的模式时只有一处要动。
    ///
    /// Defined once so six sub-domains do not each repeat the constant. The code writes the literal `StowageMode.WeightRecommendation` at every call site; this implementation converges it to one
    /// constant with identical semantics, so switching modes touches one place.
    /// </remarks>
    public const StowageMode Mode = StowageMode.WeightRecommendation;

    /// <summary>飞机上下文 / The aircraft context</summary>
    public AircraftContext AircraftContext { get; } = new();

    /// <summary>配载上下文 / The stowage context</summary>
    public StowageContext StowageContext { get; } = new();

    /// <summary>MAC 上下文 / The MAC context</summary>
    public MacContext MacContext { get; } = new();

    /// <summary>适航安全上下文 / The airworthiness-security context</summary>
    public AirworthinessSecurityContext AirworthinessSecurityContext { get; } = new();

    /// <summary>货物时效上下文 / The express-effectiveness context</summary>
    public ExpressEffectivenessContext ExpressEffectivenessContext { get; } = new();

    /// <summary>建议重量均衡上下文 / The recommended-weight-equalisation context</summary>
    public RecommendedWeightEqualizationContext RecommendedWeightEqualizationContext { get; } = new();

    /// <summary>载荷最大化上下文 / The payload-maximisation context</summary>
    public PayloadMaximizationContext PayloadMaximizationContext { get; } = new();

    /// <summary>
    /// 按顺序初始化七个上下文 / Initialise the seven contexts in order.
    /// </summary>
    /// <param name="request">请求 DTO / The request DTO.</param>
    /// <returns>执行结果 / Execution result.</returns>
    public Try Init(RequestDTO request)
    {
        ArgumentNullException.ThrowIfNull(request);

        // **必须逐步短路**：把它们塞进数组字面量会**先全部求值**再逐个检查，第一个失败时后面六步已经跑过，
        // 上下文被改了一半。每一次 `when` 都是当场返回。
        // **Must short-circuit step by step**: packing them into an array literal **evaluates all of them first** and only then checks, so a first-step failure would still leave the
        // remaining six contexts half-initialised. Every step returns on the spot.
        Try step = AircraftContext.Init(request);
        if (!step.IsOk)
        {
            return step;
        }
        step = StowageContext.Init(AircraftContext, request, Mode);
        if (!step.IsOk)
        {
            return step;
        }
        step = MacContext.Init(AircraftContext, StowageContext, request);
        if (!step.IsOk)
        {
            return step;
        }
        step = AirworthinessSecurityContext.Init(AircraftContext, StowageContext, MacContext, request);
        if (!step.IsOk)
        {
            return step;
        }
        step = ExpressEffectivenessContext.Init(StowageContext, request, Mode);
        if (!step.IsOk)
        {
            return step;
        }
        step = RecommendedWeightEqualizationContext.Init(AircraftContext, StowageContext, request);
        if (!step.IsOk)
        {
            return step;
        }
        return PayloadMaximizationContext.Init(AircraftContext, StowageContext, request);
    }

    /// <summary>
    /// 按顺序注册六个子域 / Register the six sub-domains in order.
    /// </summary>
    /// <param name="parameter">参数 / The parameter.</param>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>执行结果 / Execution result.</returns>
    public Try Register(Parameter parameter, LinearMetaModel<Flt64> model)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        ArgumentNullException.ThrowIfNull(model);

        // **没有飞机**：它不往模型上写任何东西。同上，逐步短路。
        // **No aircraft**: it writes nothing to the model. Same step-by-step short-circuit.
        Try step = StowageContext.Register(Mode, model);
        if (!step.IsOk)
        {
            return step;
        }
        step = MacContext.Register(Mode, model);
        if (!step.IsOk)
        {
            return step;
        }
        step = AirworthinessSecurityContext.Register(Mode, model);
        if (!step.IsOk)
        {
            return step;
        }
        step = ExpressEffectivenessContext.Register(Mode, parameter, model);
        if (!step.IsOk)
        {
            return step;
        }
        step = RecommendedWeightEqualizationContext.Register(Mode, parameter, model);
        if (!step.IsOk)
        {
            return step;
        }
        return PayloadMaximizationContext.Register(Mode, parameter, model);
    }
}
