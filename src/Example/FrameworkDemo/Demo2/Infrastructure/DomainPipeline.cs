#nullable enable

using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Mac;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.PayloadMaximization;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.RecommendedWeightEqualization;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Redundancy;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.SoftSecurity;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.MacOptimization;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.ExpressEffectiveness;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.LoadingEffectiveness;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage;
using System;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;

/// <summary>
/// FullLoad 和 Predistribution 模式的通用域管线上下文。
/// Common domain pipeline context for FullLoad and Predistribution modes.
/// Encapsulates the 3-domain init/register chain to reduce duplication.
/// </summary>
public sealed class DomainPipelineContext
{
    /// <summary>飞机上下文 / Aircraft context</summary>
    public AircraftContext AircraftContext { get; } = new();
    /// <summary>配载上下文 / Stowage context</summary>
    public StowageContext StowageContext { get; } = new();
    /// <summary>MAC 上下文 / MAC context</summary>
    public MacContext MacContext { get; } = new();
    /// <summary>适航安全上下文 / Airworthiness-security context</summary>
    public AirworthinessSecurityContext AirworthinessSecurityContext { get; } = new();
    /// <summary>软安全上下文 / Soft-security context</summary>
    public SoftSecurityContext SoftSecurityContext { get; } = new();
    /// <summary>MAC 优化上下文 / MAC-optimization context</summary>
    public MacOptimizationContext MacOptimizationContext { get; } = new();
    /// <summary>时效效能上下文 / Express-effectiveness context</summary>
    public ExpressEffectivenessContext ExpressEffectivenessContext { get; } = new();
    /// <summary>装载效能上下文 / Loading-effectiveness context</summary>
    public LoadingEffectivenessContext LoadingEffectivenessContext { get; } = new();
    /// <summary>
    /// 冗余上下文 / The redundancy context.
    /// </summary>
    /// <remarks>
    /// **只在预配载模式下参与**：`PredistributionPipelineContext` 是"全配载七域 + 余度"，
    /// 全配载与建议打板都没有它。这个域在本仓早就写完了（799 行，带自己的判据），却**从来没有消费者**——
    /// 于是预配载少了一整组约束，而所有判据都是绿的。
    ///
    /// **Only participates in predistribution mode**: its `PredistributionPipelineContext` is "full load's seven domains + redundancy"; neither full load
    /// nor weight recommendation has it. The domain has long been finished here (799 lines with its own criteria) but **never had a consumer** — so predistribution was missing
    /// a whole family of constraints while every criterion stayed green.
    /// </remarks>
    public RedundancyContext RedundancyContext { get; } = new();

    /// <summary>
    /// 推荐重量均衡上下文 / The recommended-weight-equalization context.
    /// </summary>
    /// <remarks>
    /// **建议打板独有**：`WeightRecommendationPipelineContext` 用它替换掉全配载的软安全、重心优化、装卸效率三域。
    /// 本仓的通用上下文一直没有它，于是"建议打板模式"在这个上下文里**仍按七域建模**——形状与真实的建议打板路径不同。
    ///
    /// **Weight recommendation only**: this context swaps it in for full load's soft security, mac optimization and loading effectiveness. The generic
    /// context here never had it, so "weight recommendation mode" was **still modelled as seven domains** — a different shape from the real weight-recommendation path.
    /// </remarks>
    public RecommendedWeightEqualizationContext RecommendedWeightEqualizationContext { get; } = new();

    /// <summary>
    /// 载荷最大化上下文 / The payload-maximization context.
    /// </summary>
    /// <remarks>**建议打板独有**，与均衡域一起构成那条六域管线。 / **Weight recommendation only** , forming that six-domain pipeline with equalization.</remarks>
    public PayloadMaximizationContext PayloadMaximizationContext { get; } = new();

    /// <summary>
    /// 初始化所有域上下文。Initialize all domain contexts from request.
    /// </summary>
    /// <param name="request">请求 DTO / Request DTO</param>
    /// <param name="stowageMode">装载模式 / The stowage mode</param>
    /// <returns>执行结果 / Execution result</returns>
    /// <remarks>
    /// **模式必须一路传到装配器**：`PositionStatus.From` 用模式决定谓词/推荐重量旗标，而 `Load` 靠那两个旗标决定要不要建变量。
    /// 这个参数以前不存在，于是三次 `Init` 都把 `FullLoad` 写死——**加的模式判断在生产路径上是死的**：
    /// 预配载装配出来的仍是全配载的模型，三种模式的指纹完全一样。
    ///
    /// **The mode has to travel all the way to the initialiser**: `PositionStatus.From` uses it to decide the predicate/recommended weight flags, and `Load` uses those
    /// flags to decide whether to create variables. This parameter did not exist before, so all three `Init` calls hard-coded `FullLoad` — **the mode check added was
    /// dead on the production path**: predistribution still assembled the full-load model and the three modes had identical fingerprints.
    /// </remarks>
    public Try Init(RequestDTO request, StowageMode stowageMode = StowageMode.FullLoad)
    {
        Try r1 = AircraftContext.Init(request);
        if (r1.IsFailed)
        {
            return r1;
        }

        Try r2 = StowageContext.Init(AircraftContext, request, stowageMode);
        if (r2.IsFailed)
        {
            return r2;
        }

        Try r3 = MacContext.Init(AircraftContext, StowageContext, request);
        if (r3.IsFailed)
        {
            return r3;
        }

        // **后四个域的先后不是随便排的**：`MacOptimization` 要 MAC 聚合，`Express`/`Loading` 效能要配载聚合，
        // 而效能域的参数里还引用适航那层的符号。顺序错了不会在这里报错，只会在注册阶段变成"引用了不存在的 token"。
        // **The order of the last four is not arbitrary**: `MacOptimization` needs the MAC aggregation, the two effectiveness domains need the stowage one, and their
        // parameters reference symbols the airworthiness layer registers. A wrong order raises nothing here and shows up at registration as "a referenced token does not exist".
        Try r4 = AirworthinessSecurityContext.Init(AircraftContext, StowageContext, MacContext, request);
        if (r4.IsFailed)
        {
            return r4;
        }

        Try r5 = SoftSecurityContext.Init(AircraftContext, StowageContext, request);
        if (r5.IsFailed)
        {
            return r5;
        }

        Try r6 = MacOptimizationContext.Init(AircraftContext, StowageContext, MacContext, request);
        if (r6.IsFailed)
        {
            return r6;
        }

        Try r7 = ExpressEffectivenessContext.Init(StowageContext, request, stowageMode);
        if (r7.IsFailed)
        {
            return r7;
        }

        Try r8 = LoadingEffectivenessContext.Init(StowageContext, request, stowageMode);
        if (r8.IsFailed)
        {
            return r8;
        }

                // 余度只在预配载下初始化，与`PredistributionPipelineContext.init` 一致。
 // Redundancy is initialised under predistribution only.
        if (stowageMode == StowageMode.Predistribution)
        {
            Try r9 = RedundancyContext.Init(StowageContext, request);
            if (r9.IsFailed)
            {
                return r9;
            }
        }

        // 建议打板独有的两域：它们替换掉软安全、重心优化、装卸效率。
 // The two weight-recommendation-only domains : the implementation swaps them in for soft security, mac optimization and loading effectiveness.
        if (stowageMode == StowageMode.WeightRecommendation)
        {
            Try r10 = RecommendedWeightEqualizationContext.Init(AircraftContext, StowageContext, request);
            if (r10.IsFailed)
            {
                return r10;
            }

            Try r11 = PayloadMaximizationContext.Init(AircraftContext, StowageContext, request);
            if (r11.IsFailed)
            {
                return r11;
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 把七个域注册进模型 / Register all seven domains with the model.
    /// </summary>
    /// <param name="stowageMode">配载模式 / The stowage mode.</param>
    /// <param name="parameter">求解参数 / The solve parameters.</param>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>执行结果 / Execution result.</returns>
    /// <remarks>
    /// **走各上下文的 `Register` 而不是直接注册子模型**：每个上下文的 `Register` 除了子模型还会把该域的管线表跑一遍——
    /// 约束加了，**目标也加了**。只注册子模型会得到"有变量、有约束、没有目标"的模型，求解器会拿一个默认目标解出一个看似合理的答案。
    ///
    /// **Go through each context's `Register` rather than registering sub-models directly**: besides the sub-models, each context's `Register` runs that domain's pipeline list,
    /// which adds constraints **and objectives**. Registering only the sub-models yields "variables, rows, no objective", and the solver then picks a default objective and returns
    /// a plausible-looking answer.
    /// </remarks>
    public Try Register(StowageMode stowageMode, Parameter parameter, LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(parameter);
        ArgumentNullException.ThrowIfNull(model);

        // **三条管线是三种不同的组成，不是一个集合加加减减**：
        // 全配载/预配载七域（软安全、重心优化、装卸效率），建议打板六域（时效、均衡、载荷最大化）。
        // 以前这里只有七域一套，于是"建议打板模式"建出来的模型**和真正的建议打板路径不同形状**——
        // 指纹表里那个 `54/71/53/2` 就是这种"七域的建议打板"，而真实的建议打板路径从来不建它。
        //
        // **Three pipelines, three different compositions, not one set plus or minus members**: full load and predistribution take seven domains (soft security,
        // mac optimization, loading effectiveness); weight recommendation takes six (express effectiveness, equalization, payload maximization). Only the seven-domain set
        // used to exist here, so "weight recommendation mode" produced **a different shape from the real path** — the `54/71/53/2` in the fingerprint table was exactly that
        // "seven-domain weight recommendation", which the real path never builds.
        Try[] registrations = stowageMode == StowageMode.WeightRecommendation
            ? [
                StowageContext.Register(stowageMode, model),
                MacContext.Register(stowageMode, model),
                AirworthinessSecurityContext.Register(stowageMode, model),
                ExpressEffectivenessContext.Register(stowageMode, parameter, model),
                RecommendedWeightEqualizationContext.Register(stowageMode, parameter, model),
                PayloadMaximizationContext.Register(stowageMode, parameter, model),
            ]
            : [
                StowageContext.Register(stowageMode, model),
                MacContext.Register(stowageMode, model),
                AirworthinessSecurityContext.Register(stowageMode, model),
                SoftSecurityContext.Register(stowageMode, parameter, model),
                MacOptimizationContext.Register(stowageMode, parameter, model),
                ExpressEffectivenessContext.Register(stowageMode, parameter, model),
                LoadingEffectivenessContext.Register(stowageMode, parameter, model),
            ];
        if (stowageMode == StowageMode.Predistribution)
        {
            // 余度在预配载下多注册一层；全配载与建议打板都没有它。
            // Redundancy adds one more layer under predistribution ; full load and weight recommendation do not have it.
            registrations = [
                ..registrations,
                RedundancyContext.Register(stowageMode, parameter, model),
            ];
        }
        foreach (Try registration in registrations) {
            if (registration is Failed<Success, ErrorCode, Error<ErrorCode>> failed) {
                return new Failed<Success, ErrorCode, Error<ErrorCode>>(failed.Error);
            }
        }

        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
