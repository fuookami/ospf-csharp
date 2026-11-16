#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.ExpressEffectiveness;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.LoadingEffectiveness;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Mac;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.MacOptimization;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Redundancy;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.SoftSecurity;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Service;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Application;

/// <summary>
/// 预配载应用。Predistribution application: orchestrates the predistribution stowage optimization pipeline.
/// </summary>
public sealed class PredistributionApplication
{
    private readonly AircraftContext _aircraftContext = new();
    private readonly StowageContext _stowageContext = new();
    private readonly MacContext _macContext = new();
    private readonly AirworthinessSecurityContext _airworthinessSecurityContext = new();
    private readonly SoftSecurityContext _softSecurityContext = new();
    private readonly MacOptimizationContext _macOptimizationContext = new();
    // **预配载里还注册这两个域**：漏掉它们不会报错，只会让模型少几条约束——
    // 实测后果是整条路径不可行（49 行 vs 54 行）。
    //
    // **The code registers these two more domains for predistribution** : omitting them never errors, it just leaves the model with fewer
    // constraints — the measured consequence was that the whole path became infeasible (49 rows vs 54).
    private readonly ExpressEffectivenessContext _expressEffectivenessContext = new();
    private readonly LoadingEffectivenessContext _loadingEffectivenessContext = new();
    /// <summary>
    /// 冗余上下文 / The redundancy context.
    /// </summary>
    /// <remarks>
    /// **预配载独有的第八个域**：在全配载七域之后再注册它。
    /// 这个域在本仓早就写完了却从没接线，于是预配载少了六个符号、两个目标。
    ///
    /// **The eighth domain, predistribution only**: it is registered after full load's seven. The domain had long been finished here but never wired, so
    /// predistribution was missing six symbols and two objectives.
    /// </remarks>
    private readonly RedundancyContext _redundancyContext = new();

    /// <summary>
    /// 执行预配载算法。Execute the predistribution algorithm.
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
        // **不再丢弃它**：算出来的模式必须决定走向，否则就是"算了等于没算"的局部变量。

        // 4. **Benders 模式在这条路径上没有接线 → 明确拒绝**
        //
        // 这一行以前是 `SolveMode solveMode = …` 之后**什么都不做**：模式算出来、然后被丢掉，永远走 MILP。
        // 于是"要了 Benders"的请求会拿到一个**看起来成功**的 MILP 答案，而备注里还留着 `ResolveSolveMode` 写下的
        // `Benders requested; adaptive Benders path enabled in application layer` —— **说一套、做一套**。
        //
        // 本仓只在全配载路径接了 Benders，另外两条没有。**没有就说不没有**，不要静默回落。
        //
        // 4. **There is no Benders wiring on this path, so it is refused explicitly** . This used to resolve the mode and then discard it, always
        // running MILP — a request that asked for Benders got **a MILP answer that looked successful**, with the note `Benders requested; adaptive Benders
        // path enabled in application layer` left in the diagnostics: **saying one thing and doing another.** Only the full-load path has Benders wired
        // When a path lacks it, say so instead of silently falling back.
        if (solveMode is SolveMode.Benders bendersMode)
        {
            notes.Add($"benders_mode_not_wired={bendersMode.Config.MaxIterations};mode=Predistribution");
            return (ResponseDTO.NoSolution("BendersNotWiredForMode", notes), null);
        }

        // TODO: Implement solveWithMILP and solveWithBendersAlgorithm

        // **求解器由调用方带进来**：默认全量测试不能依赖可选后端；没带就明确说"未接线"，
        // **不是"未实现"**——装配与求解链路在本仓早已可用（先接线了全配载）。
        // **The solver comes from the caller**: the default suite must not depend on an optional backend. When none is supplied it says "not wired",
        // **not "not implemented"** — assembly and solving already exist here ( wired the full-load path first).
        return milpSolver is null
            ? (ResponseDTO.NoSolution("MilpSolverNotSupplied", notes), null)
            : (DirectMilpRunner.Run(
                model => {
                    // **参数从请求派生**：`new Parameter` 会把请求里的两个推荐优先级丢掉。
                // **The parameters come from the request**: `new Parameter` drops both recommendation priorities carried by the request.
                Parameter parameter = request.ResolvedParameter;
                    Try[] registrations = [
                        _stowageContext.Register(StowageMode.Predistribution, model),
                        _macContext.Register(StowageMode.Predistribution, model),
                        _airworthinessSecurityContext.Register(StowageMode.Predistribution, model),
                _softSecurityContext.Register(StowageMode.Predistribution, parameter, model),
                    // **这里还注册三个域**：`macOptimization`、`expressEffectiveness`、`loadingEffectiveness`。
                    // 以前只注册四个，**预配载路径因此不可行**（49 行，SCIP 报 `ORModelInfeasible`），而七个域全注册时可行（54 行，目标 1）。
                    // 缺域不会报错，只会让模型"少几条约束"——这类缺陷最难发现，因为装配看起来是成功的。
                    //
                    // **The code registers three more domains here** : `macOptimization`, `expressEffectiveness`, `loadingEffectiveness`.
                    // Registering only four made the predistribution path **infeasible** (49 rows, SCIP reports `ORModelInfeasible`), while all seven are feasible
                    // (54 rows, objective 1). A missing domain never errors — it just leaves the model with fewer constraints, which is the hardest kind to notice.
                    _macOptimizationContext.Register(StowageMode.Predistribution, parameter, model),
                    _expressEffectivenessContext.Register(StowageMode.Predistribution, parameter, model),
                    _loadingEffectivenessContext.Register(StowageMode.Predistribution, parameter, model),
                    _redundancyContext.Register(StowageMode.Predistribution, parameter, model),
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

        // **模式必须传下去**：`StowageContext.Init` 的 `stowageMode` 默认是 `FullLoad`，不传就**静默地按全配载建模**，
        // 而请求里的谓词规格照样被注册——于是预配载得到"带着谓词变量的全配载模型"，不可行且不报错。 在流水线上修过同一处，
        // 三个应用各存了一份自己的 `Init`，所以这里**各修一遍**（它们没有共用那条路径）。
        //
        // **The mode has to be passed down**: `StowageContext.Init`'s `stowageMode` defaults to `FullLoad`, so omitting it **silently models full load** while
        // the request's predicate specs are still registered — predistribution ends up as "a full-load model carrying predicate variables", infeasible and silent.
        // fixed exactly this in the pipeline; each of the three applications keeps its own `Init`, so it is fixed **in each** (they share no path).
        Try r2 = _stowageContext.Init(_aircraftContext, request, StowageMode.Predistribution);
        if (r2.IsFailed) return r2;

        Try r3 = _macContext.Init(_aircraftContext, _stowageContext, request);
        if (r3.IsFailed) return r3;

        Try r4 = _airworthinessSecurityContext.Init(_aircraftContext, _stowageContext, _macContext, request);
        if (r4.IsFailed) return r4;

        Try r5 = _softSecurityContext.Init(_aircraftContext, _stowageContext, request);
        if (r5.IsFailed) return r5;

        Try r6 = _macOptimizationContext.Init(_aircraftContext, _stowageContext, _macContext, request);
        if (r6.IsFailed) return r6;

        // TODO: ExpressEffectivenessContext, LoadingEffectivenessContext, RedundancyContext

                Try r7 = _expressEffectivenessContext.Init(_stowageContext, request, StowageMode.Predistribution);
        if (r7.IsFailed)
        {
            return r7;
        }

        Try r8 = _loadingEffectivenessContext.Init(_stowageContext, request, StowageMode.Predistribution);
        if (r8.IsFailed)
        {
            return r8;
        }
        // 余度只依赖配载聚合，放在最后初始化。
        // Redundancy depends only on the stowage aggregation, so it is initialised last.
        Try r9 = _redundancyContext.Init(_stowageContext, request);
        if (r9.IsFailed)
        {
            return r9;
        }
return Results.Ok<Success>(Results.SuccessInstance);
    }
}
