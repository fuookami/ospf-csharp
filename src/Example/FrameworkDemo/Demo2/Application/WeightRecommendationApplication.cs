#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.ExpressEffectiveness;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Mac;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.PayloadMaximization;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.RecommendedWeightEqualization;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Service;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Application;

/// <summary>
/// 建议打板应用。Weight recommendation application: orchestrates the weight recommendation optimization pipeline.
/// Port of Kotlin WeightRecommendationApplication / WeightRecommendationAlgorithmImpl.
/// </summary>
public sealed class WeightRecommendationApplication
{
    private readonly AircraftContext _aircraftContext = new();
    private readonly StowageContext _stowageContext = new();
    private readonly MacContext _macContext = new();
    private readonly AirworthinessSecurityContext _airworthinessSecurityContext = new();
    // **基线这一条管线不是"全配载的七域"**（B816）：它注册的是配载、重心、适航、时效、**推荐重量均衡**、载荷最大化——
    // 没有软安全、没有重心优化、没有装卸效率。均衡那个域**是这条模式的全部意义**（推荐的重量要摊平），
    // 以前它带着 `Init` 里的 `TODO` 一直没接上，于是"建议打板"其实只是"最大化载荷"。
    //
    // **This pipeline is not "full load's seven domains"** (B816): it registers stowage, mac, airworthiness, express effectiveness, **recommended-weight
    // equalization** and payload maximization — no soft security, no mac optimization, no loading effectiveness. The equalization domain **is the whole point of this
    // mode** (recommended weights must be levelled), and it sat behind a `TODO` in `Init`, so "weight recommendation" was really just "maximise payload".
    private readonly ExpressEffectivenessContext _expressEffectivenessContext = new();
    private readonly RecommendedWeightEqualizationContext _recommendedWeightEqualizationContext = new();
    private readonly PayloadMaximizationContext _payloadMaximizationContext = new();

    /// <summary>
    /// 执行建议打板算法。Execute the weight recommendation algorithm.
    /// </summary>
    /// <param name="request">请求 DTO / Request DTO</param>
    /// <param name="milpSolver">直连 MILP 求解器（可选，为 null 时明确报"未带求解器"）/ Direct MILP solver (optional; null reports "no solver supplied" explicitly)</param>
    /// <returns>响应 DTO 和渲染 DTO / Response DTO and render DTO</returns>
    public (ResponseDTO Response, RenderDTO? Render) Execute(
        RequestDTO request,
        ILinearSolver? milpSolver = null)
    {
        var notes = new List<string>();

        // 1. Initialize contexts
        Try r1 = Init(request);
        if (r1 is Failed<Success, ErrorCode, Error<ErrorCode>> failed)
        {
            return (ResponseDTO.NoSolution("InitFailed", notes), null);
        }

        // 2. Feasibility diagnostics
        FeasibilityDiagnostics.AppendCoreFeasibilityDiagnostics(request, notes);
        if (notes.Count > 0)
        {
            return (ResponseDTO.NoSolution("NoSolution", notes), null);
        }

        // 3. Resolve solve mode
        SolveMode solveMode = BendersStrategy.ResolveSolveMode(request, notes);
        // **不再丢弃它**（B831）：算出来的模式必须决定走向，否则就是"算了等于没算"的局部变量。

        // 4. **Benders 模式在这条路径上没有接线 → 明确拒绝**（B831）
        //
        // 这一行以前是 `SolveMode solveMode = …` 之后**什么都不做**：模式算出来、然后被丢掉，永远走 MILP。
        // 于是"要了 Benders"的请求会拿到一个**看起来成功**的 MILP 答案，而备注里还留着 `ResolveSolveMode` 写下的
        // `Benders requested; adaptive Benders path enabled in application layer` —— **说一套、做一套**。
        //
        // 本仓只在全配载路径接了 Benders（B790），另外两条没有。**没有就说不没有**，不要静默回落。
        //
        // 4. **There is no Benders wiring on this path, so it is refused explicitly** (B831). This used to resolve the mode and then discard it, always
        // running MILP — a request that asked for Benders got **a MILP answer that looked successful**, with the note `Benders requested; adaptive Benders
        // path enabled in application layer` left in the diagnostics: **saying one thing and doing another.** Only the full-load path has Benders wired
        // (B790). When a path lacks it, say so instead of silently falling back.
        if (solveMode is SolveMode.Benders bendersMode)
        {
            notes.Add($"benders_mode_not_wired={bendersMode.Config.MaxIterations};mode=WeightRecommendation");
            return (ResponseDTO.NoSolution("BendersNotWiredForMode", notes), null);
        }

        // TODO: Port solveWithMILP and solveWithBendersAlgorithm from Kotlin

        // **求解器由调用方带进来**：默认全量测试不能依赖可选后端；没带就明确说"未接线"，
        // **不是"未移植"**——装配与求解链路在本仓早已可用（B790 先接线了全配载）。
        // **The solver comes from the caller**: the default suite must not depend on an optional backend. When none is supplied it says "not wired",
        // **not "not ported"** — assembly and solving already exist in this port (B790 wired the full-load path first).
        return milpSolver is null
            ? (ResponseDTO.NoSolution("MilpSolverNotSupplied", notes), null)
            : (DirectMilpRunner.Run(
                model => {
                    // **参数从请求派生**（B808）：`new Parameter()` 会把请求里的两个推荐优先级丢掉。
                // **The parameters come from the request** (B808): `new Parameter()` drops both recommendation priorities carried by the request.
                Parameter parameter = request.ResolvedParameter;
                    Try[] registrations = [
                        _stowageContext.Register(StowageMode.WeightRecommendation, model),
                        _macContext.Register(StowageMode.WeightRecommendation, model),
                        _airworthinessSecurityContext.Register(StowageMode.WeightRecommendation, model),
                _expressEffectivenessContext.Register(StowageMode.WeightRecommendation, parameter, model),
                _recommendedWeightEqualizationContext.Register(StowageMode.WeightRecommendation, parameter, model),
                _payloadMaximizationContext.Register(StowageMode.WeightRecommendation, parameter, model),
                    ];
                    foreach (Try registration in registrations) {
                        if (registration is Failed<Success, ErrorCode, Error<ErrorCode>> failed) {
                            return failed;
                        }
                    }
                    return Results.Ok<Success>(Results.SuccessInstance);
                },
                milpSolver,
                notes,
                (values, model) => SolutionRenderer.RenderSolution(values, model, _stowageContext.Aggregation)));
    }

    private Try Init(RequestDTO request)
    {
        Try r1 = _aircraftContext.Init(request);
        if (r1.IsFailed) return r1;

        // **模式必须传下去**（B815）：`StowageContext.Init` 的 `stowageMode` 默认是 `FullLoad`，不传就**静默地按全配载建模**，
        // 而请求里的谓词规格照样被注册——于是预配载得到"带着谓词变量的全配载模型"，不可行且不报错。B800 在流水线上修过同一处，
        // 三个应用各存了一份自己的 `Init`，所以这里**各修一遍**（它们没有共用那条路径）。
        //
        // **The mode has to be passed down** (B815): `StowageContext.Init`'s `stowageMode` defaults to `FullLoad`, so omitting it **silently models full load** while
        // the request's predicate specs are still registered — predistribution ends up as "a full-load model carrying predicate variables", infeasible and silent.
        // B800 fixed exactly this in the pipeline; each of the three applications keeps its own `Init`, so it is fixed **in each** (they share no path).
        Try r2 = _stowageContext.Init(_aircraftContext, request, StowageMode.WeightRecommendation);
        if (r2.IsFailed) return r2;

        Try r3 = _macContext.Init(_aircraftContext, _stowageContext, request);
        if (r3.IsFailed) return r3;

        Try r4 = _airworthinessSecurityContext.Init(_aircraftContext, _stowageContext, _macContext, request);
        if (r4.IsFailed) return r4;

        Try r5 = _expressEffectivenessContext.Init(_stowageContext, request, StowageMode.WeightRecommendation);
        if (r5.IsFailed)
        {
            return r5;
        }

        Try r6 = _recommendedWeightEqualizationContext.Init(_aircraftContext, _stowageContext, request);
        if (r6.IsFailed)
        {
            return r6;
        }

        Try r7 = _payloadMaximizationContext.Init(_aircraftContext, _stowageContext, request);
        if (r7.IsFailed) return r7;

        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
