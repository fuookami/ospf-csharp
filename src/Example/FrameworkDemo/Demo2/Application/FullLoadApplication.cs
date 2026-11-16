#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.ExpressEffectiveness;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.LoadingEffectiveness;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Mac;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.MacOptimization;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.SoftSecurity;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Service;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Application;

/// <summary>
/// 全装载应用。Full load application: orchestrates the full-load stowage optimization pipeline.
///
/// Port of Kotlin FullLoadApplication / FullLoadAlgorithm.
/// Supports both direct MILP and Benders decomposition paths with quality-guarded fallback.
/// </summary>
public sealed class FullLoadApplication
{
    private readonly AircraftContext _aircraftContext = new();
    private readonly StowageContext _stowageContext = new();
    private readonly MacContext _macContext = new();
    private readonly AirworthinessSecurityContext _airworthinessSecurityContext = new();
    private readonly SoftSecurityContext _softSecurityContext = new();
    private readonly MacOptimizationContext _macOptimizationContext = new();
    /// <summary>
    /// 时效效能上下文 / The express-effectiveness context.
    /// </summary>
    /// <remarks>
    /// **全配载也是七域**：基线 `FullLoadPipelineContext` 的注册清单里有时效与装卸效率，
    /// 本仓的应用路径只有前五个，于是全配载的应用路径是 47 行、管线路径是 52 行——**同一份请求、两种形状**。
    /// 这与 （预配载漏三域）、 （建议打板漏两域）是同一类缺陷：**漏域不报错，只是少约束**。
    ///
    /// **Full load is seven domains too**: the baseline's `FullLoadPipelineContext` registers express effectiveness and loading effectiveness, while this port's
    /// application path had only the first five — 47 rows on the application path against 52 on the pipeline path, **one request, two shapes**. Same defect class as 
    /// (predistribution missing three) and (weight recommendation missing two): **a missing domain never errors, it just leaves fewer constraints**.
    /// </remarks>
    private readonly ExpressEffectivenessContext _expressEffectivenessContext = new();
    /// <summary>装卸效率上下文 / The loading-effectiveness context</summary>
    private readonly LoadingEffectivenessContext _loadingEffectivenessContext = new();

    /// <summary>
    /// 执行全装载算法。Execute the full load algorithm.
    /// </summary>
    /// <param name="request">请求 DTO / Request DTO</param>
    /// <param name="bendersSolver">Benders 分解求解器（可选，为 null 时跳过 Benders）/ Benders decomposition solver (optional, null skips Benders)</param>
    /// <param name="milpSolver">直连 MILP 求解器（可选，为 null 时明确报"未带求解器"）/ Direct MILP solver (optional; null reports "no solver supplied" explicitly)</param>
    /// <returns>响应 DTO 和渲染 DTO / Response DTO and render DTO</returns>
    public (ResponseDTO Response, RenderDTO? Render) Execute(
        RequestDTO request,
        Fuookami.Ospf.Framework.Solver.IBendersDecompositionSolver? bendersSolver = null,
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

        // 3. Check aircraft support
        if (!BendersStrategy.SupportedAircraft(request.AircraftType))
        {
            notes.Add($"unsupported aircraft type for full-load path: {request.AircraftType}");
            return (ResponseDTO.NoSolution("UnsupportedAircraft", notes), null);
        }

        if (notes.Count > 0)
        {
            return (ResponseDTO.NoSolution("NoSolution", notes), null);
        }

        // 4. Resolve solve mode
        SolveMode solveMode = BendersStrategy.ResolveSolveMode(request, notes);

        // **带了 Benders 求解器却没走 Benders，要说出来**：路线由**请求规模**决定（`ResolveSolveMode`），
        // **不由传进来的求解器决定**。调用方传了解释器却得到 MILP，静默跳过是最容易让人以为"Benders 跑了"的地方。
        //
        // **A supplied Benders solver that does not select Benders must say so** (measured): the route is decided by **request size** (`ResolveSolveMode`),
        // **not by the supplied solver**. A caller handing in a solver and silently getting MILP is exactly where people think "Benders ran".
        if (bendersSolver is not null && solveMode is not SolveMode.Benders)
        {
            notes.Add("benders_solver_supplied_but_route=milp_direct: the route is decided by request size, not by the supplied solver");
        }

        // 5. Solve
        ResponseDTO response;
        RenderDTO? render = null;
        switch (solveMode)
        {
            case SolveMode.Benders bendersMode:
                notes.Add("solver_path=benders");
                DiagnosticsHelper.PushGroupedNote(
                    notes, DiagnosticsHelper.LevelDiagnostic, DiagnosticsHelper.GroupSolver,
                    DiagnosticsHelper.CodeSolverPath, "benders");
                // **先建模型，再查求解器**（一条规矩，在这里守住它）：模型为空时，哪怕给了求解器也只会解出一个空模型，
                // 而"没求解器"这句话会掩盖"模型是空的"这个更根本的问题。所以求解器缺席时**仍然建一次模型并把形状写进备注**。
                //
                // **Build the model before checking the solver** (the earlier rule, upheld here): with an empty model even a supplied solver only solves nothing, and "no solver"
                // would mask the more fundamental "the model is empty". So when the solver is absent the model is still built once and its shape written into the notes.
                if (bendersSolver is null) {
                    using LinearMetaModel<Flt64>? shape = BuildSingleBendersModel(StowageMode.FullLoad, notes);
                    notes.Add("Benders solver not available, falling back to MILP");
                    response = ResponseDTO.NoSolution("BendersSolverUnavailable", notes);
                } else {
                    response = SolveSingleModelBendersAsync(
                        StowageMode.FullLoad, bendersSolver, milpSolver, notes).GetAwaiter().GetResult();
                }
                // **第二次尝试切分派，仍然被驳回**：真后端上单模型链收敛（记为 `Optimal`），但**默认门禁里的假脚本不收敛**
                // （它的取值满足下界却不满足约束， ），于是两条"不编造分配/目标值照报"的判据失去收敛前提。
                // 结论：**链路没问题，是默认门禁的夹具还不够**。切换要等夹具能在假脚本下收敛，或把那两条判据移到真后端门内。
                //
                // **tried the switch a second time and it was rejected again**: the real backend converges on the single-model chain ( `Optimal`), but **the fake
                // script in the default gate does not** (its values respect bounds but not constraints, ), so the two "no invented assignments / objective is real"
                // criteria lose their converging premise. Conclusion: **the chain is fine, the default-gate fixture is not enough.**
                // **试过把这里切到单模型路径，被实测驳回**：`Demo2BendersAssignmentFabricationTests` 的脚本求解器在旧算法上收敛（Optimal/7.5），
                // 在 `LinearBendersAlgorithm` 上直接失败。**"能建、能切、能跑到求解器"不等于"结果对得上"**，所以分派留在旧路径，
                // 直到两套算法的调用契约对齐（脚本求解器实现的是旧回调语义）。
                //
                // **tried switching this line to the single-model route and measurement rejected it**: the scripted solver in `Demo2BendersAssignmentFabricationTests`
                // converges on the old algorithm (Optimal / 7.5) but fails outright on `LinearBendersAlgorithm`. **"It builds, splits and reaches a solver" is not "the results
                // line up"**, so dispatch stays on the old route until the two algorithms' calling contracts agree (the scripted solver implements the old callback semantics).
                break;

            case SolveMode.Milp:
            default:
                notes.Add("solver_path=milp_direct");
                DiagnosticsHelper.PushGroupedNote(
                    notes, DiagnosticsHelper.LevelDiagnostic, DiagnosticsHelper.GroupSolver,
                    DiagnosticsHelper.CodeSolverPath, "milp_direct");
                // **求解器由调用方带进来**：直连路径需要后端，而默认全量测试不能依赖可选后端。
                // 没带就明确说"未接线"，**不是"未移植"**——模型装配与求解链路都已经在本仓可用。
                // **The solver comes from the caller**: the direct path needs a backend, and the default suite must not depend on an optional one. When none is
                // supplied it says "not wired", **not "not ported"** — both the model assembly and the solve chain already exist in this port.
                (response, render) = milpSolver is null
                    ? (ResponseDTO.NoSolution("MilpSolverNotSupplied", notes), null)
                    : SolveDirectMilp(milpSolver, notes, request.ResolvedParameter);
                break;
        }

        return (response, render);
    }

    /// <summary>
    /// 使用 Benders 分解求解。Solve with Benders decomposition, with quality guard and MILP fallback.
    /// </summary>
    // **`internal` 而不是 `private`**：分派将要切到单模型链，旧路径的对照判据必须能**直接调用**它——
    // 否则切换当天，那些判据只能被删掉或改到新链上，**对照关系就没了**。
    // **`internal` rather than `private`**: dispatch is about to switch to the single-model chain, and the comparison criteria for the old route must be able to **call
    // it directly** — otherwise they can only be deleted or rewritten on the new chain, and the comparison is lost.
    internal ResponseDTO SolveWithBenders(
        RequestDTO request,
        EffectiveBendersAdaptiveConfig config,
        Fuookami.Ospf.Framework.Solver.IBendersDecompositionSolver? solver,
        ILinearSolver? milpSolver,
        List<string> notes)
    {
        // **先查模型，再查求解器**：模型没注册时，哪怕给了求解器也只会解出一个空的 theta 模型，
        // 而那个已删掉的旧解码器（删除）会按"货 × 舱位"的布局去读那个长度 1 的解向量——**凭空造出分配**。
        // 顺序反过来（先查求解器）会让"没求解器"掩盖"模型是空的"这个更根本的问题。
        //
        // **Check the models before the solver**: with nothing registered, even a supplied solver only solves the empty theta model, and the old decoder
        // would read that one-element solution through the "cargo × position" layout — **inventing assignments**. Reversing the order (solver first) lets
        // "no solver" mask the more fundamental "the model is empty".
        var bendersModels = BuildBendersModels(StowageMode.FullLoad, notes);

        // **两个模型的数量都报出来**：判据要能看见"主问题 11 行 / 子问题 N 行"这个事实，
        // 否则"接上了"只能靠读代码相信。三个指标一起报，是因为已知软安全那种"只加符号不加约束"的域
        // **只看行数发现不了**（的教训）。
        //
        // **Both model shapes are reported**: a criterion must be able to see "master 11 rows, sub N rows" as a fact, otherwise "it is wired" can only be
        // believed by reading code. Three counts each, because a domain that only adds symbols without constraints **cannot be seen through rows alone** (the 
        // lesson).
        notes.Add($"benders_models=master_rows={bendersModels.MasterModel.Constraints.Count};master_symbols={bendersModels.MasterModel.Tokens.Symbols.Count};master_objectives={bendersModels.MasterModel.MetaSubObjects.Count};sub_rows={bendersModels.SubModel.Constraints.Count};sub_symbols={bendersModels.SubModel.Tokens.Symbols.Count};sub_objectives={bendersModels.SubModel.MetaSubObjects.Count}");
        // **两个模型分开报**：主问题在 接上之后有约束了，子问题还没有（它要一个主问题解才能注册）。
        // 合成一句"没注册"会让后来的人以为整个 Benders 都没接，**而实际进度是"主问题已接、子问题待接"**。
        //
        // **The two models are reported separately**: the master has constraints since then; the sub does not (it needs a master solution to register).
        // Merging them into one "not registered" makes the next reader think nothing is wired, **when the truth is "master wired, sub pending".**
        if (bendersModels.SubModel.Constraints.Count == 0)
        {
            // **三个数字一起报**：只看行数会漏掉"注册了但只加符号/目标"的域——软安全就是这样，
            // 去掉它行数不变，**行数这个指标根本发现不了**。
            //
            // **All three counts are reported**: rows alone miss domains that register symbols or objectives without constraints — soft security is exactly
            // that case, and rows alone cannot detect its absence.
            notes.Add($"benders_sub_model_not_registered: master_rows={bendersModels.MasterModel.Constraints.Count};master_symbols={bendersModels.MasterModel.Tokens.Symbols.Count};master_objectives={bendersModels.MasterModel.MetaSubObjects.Count}");
            DiagnosticsHelper.PushGroupedNote(
                notes, DiagnosticsHelper.LevelDiagnostic, DiagnosticsHelper.GroupSolver,
                DiagnosticsHelper.CodeBendersFailed, "benders sub-problem is not registered yet");
            return ResponseDTO.NoSolution("BendersSubModelNotRegistered", notes);
        }

        if (bendersModels.MasterModel.Constraints.Count == 0)
        {
            // **拒绝编造**：Benders 主/从模型的域注册仍是桩（见 `BuildBendersModels` 的注释），因此这里没有任何可解的模型。
            // 返回"最优解 + 造出来的分配"比返回失败**更有害**，因为调用方无从分辨。
            //
            // **Refuse to fabricate**: the Benders master/sub domain registration is still a stub (see the notes in `BuildBendersModels`), so there is no model to
            // solve. Returning "optimal plus invented assignments" is **more harmful** than returning a failure, because the caller cannot tell the difference.
            notes.Add("benders_models_not_registered: the master/sub domain registration is still a stub");
            DiagnosticsHelper.PushGroupedNote(
                notes, DiagnosticsHelper.LevelDiagnostic, DiagnosticsHelper.GroupSolver,
                DiagnosticsHelper.CodeBendersFailed, "benders models are not registered yet");
            return ResponseDTO.NoSolution("BendersModelsNotRegistered", notes);
        }

        if (solver == null)
        {
            notes.Add("Benders solver not available, falling back to MILP");
            return ResponseDTO.NoSolution("BendersSolverUnavailable", notes);
        }


        notes.Add($"benders_adaptive=max_iterations={config.MaxIterations},tolerance={config.Tolerance:F6}");
        DiagnosticsHelper.PushGroupedNote(
            notes, DiagnosticsHelper.LevelDiagnostic, DiagnosticsHelper.GroupSolver,
            DiagnosticsHelper.CodeBendersAdaptiveEffective,
            $"max_iterations={config.MaxIterations},tolerance={config.Tolerance:F6}");

        // Run Benders iteration loop
        // **一次取值，不用 `.Wait` + `.Result`**：那两个成员会把异常包成 `AggregateException`，
        // 于是 `catch (SomeSpecificException)` 接不到，日志里也只剩一层壳。`GetAwaiter.GetResult` 原样抛出，
        // 而且只读一次任务——`.Wait` 之后再 `.Result` 是第二次访问同一个任务。
        //
        // **阻塞仍然存在**：`Execute` 是同步入口，改 async 会波及所有调用方（demo2 的十几个测试与示例程序）。
        // 这不是"已经修好"，只是"把它变成一次可读的阻塞"。整链 async 化记在清单里。
        //
        // **Read once, no `.Wait` + `.Result`**: those two members wrap exceptions in `AggregateException`, so `catch (SomeSpecificException)` never matches
        // and the log keeps only a shell. `GetAwaiter.GetResult` rethrows the original, and reads the task once — `.Wait` followed by `.Result` is a second
        // access to the same task. **The blocking itself remains**: `Execute` is a sync entry, and making it async would ripple through every caller (a dozen demo2
        // tests and the sample program). This is not "fixed", only "one readable block"; the full async chain is recorded in the inventory.
        var bendersResult = BendersSolver.SolveAsync(
            solver: solver,
            masterModel: bendersModels.MasterModel,
            subModel: bendersModels.SubModel,
            fixedVariables: bendersModels.FixedVariables,
            objectVariable: bendersModels.ObjectVariable,
            config: config,
            notes: notes).GetAwaiter().GetResult();
        if (bendersResult is Failed<BendersResult, ErrorCode, Error<ErrorCode>> bendersFailed)
        {
            // Benders failed -- fallback to MILP if configured
            // **这里以前写的是 `BendersFailed_MilpFallback`——那是一句假话**：
            // 策略允许回退，但这条路径**没有 MILP 后端可用**，什么都没跑过。
            // 框架的 `DiscreteLoadingApplicationService` 用 `milp_fallback_after_benders` 表示"回退**确实**跑了"；
            // 两个字面值不能混用，否则调用方无法区分"跑过 MILP"和"只是允许回退"。
            //
            // **It used to say `BendersFailed_MilpFallback` — a lie**: the policy allows the fallback, but **no MILP backend is available on this path**
            // and nothing ran. The framework service uses `milp_fallback_after_benders` to mean "the fallback **did** run"; the two literals must not be merged,
            // otherwise a caller cannot tell "MILP ran" from "MILP was merely allowed".
            if (request.ResolvedSolvePolicy.BendersFallbackToMilp)
            {
                // **两种"没走回退"要说成两句不同的话**：基线要求回退**重建一个新模型与新上下文**
                // （`DiscreteLoadingApplicationService.kt`："回退必须创建新模型与上下文"）。而本仓的域上下文是**一次性**的
                // （`上下文已经注册`），所以就算调用方给了 MILP 求解器，这条路径也**建不出一个可用的新模型**——
                // 那是比"没给求解器"更根本的原因，混成一句会让后来的人以为"补个求解器就好了"。
                //
                // **Two different reasons, two different sentences**: the baseline requires the fallback to **rebuild a fresh model and context**
                // (`DiscreteLoadingApplicationService.kt`: "the fallback must create a new model and context"). This port's domain contexts are **single-use** (measured in
                // with `Context already registered`), so even with a MILP solver supplied this path **cannot build a usable fresh model** — a more fundamental reason
                // than "no solver was given", and merging the two would make the next reader think "just supply a solver".
                if (milpSolver is null)
                {
                    notes.Add("benders_failed: policy allows the MILP fallback, but no MILP solver was supplied on this path");
                }
                else
                {
                    notes.Add("benders_failed: a MILP solver was supplied, but the fallback needs a fresh model and the domain contexts are single-use");
                }
                DiagnosticsHelper.PushGroupedNote(
                    notes, DiagnosticsHelper.LevelDiagnostic, DiagnosticsHelper.GroupSolver,
                    DiagnosticsHelper.CodeBendersFailed, "benders failed, fallback to milp");
                return ResponseDTO.NoSolution("benders_failed_without_milp_fallback", notes);
            }
            return ResponseDTO.NoSolution("BendersFailed", notes);
        }

        var result = ((Ok<BendersResult, ErrorCode, Error<ErrorCode>>)bendersResult).Value;

        // **状态要报出来**：基线记 `benders_status=…`，本仓此前只在停滞时往备注里塞一句话。
        // 现在无论收敛与否都有这一句，**"没收敛"刚不再是件需要从空解向量里猜的事**。
        //
        // **The status is reported**: the baseline records `benders_status=…`; this port previously only added a sentence when the loop stalled. Now it is
        // there whether it converged or not, **so "it did not converge" is no longer something to guess from an empty solution vector.**
        notes.Add($"benders_status={result.Status}");

        // Quality guard check
        var qualityGuard = BendersStrategy.ResolveQualityGuardConfig(request.BendersQualityOverrides);
        string? qualityReason = BendersStrategy.ResolveQualityReason(
            adaptive: config,
            qualityGuard: qualityGuard,
            bendersIterations: result.BendersIterations,
            bendersGap: result.Gap,
            bendersTimeMs: result.TimeMs,
            executedIterations: result.RuntimeMetrics?.ExecutedIterations,
            totalCuts: result.RuntimeMetrics?.TotalCuts,
            iterationSnapshots: result.RuntimeMetrics != null
                ? GetSnapshotObjectives(result.RuntimeMetrics.IterationSnapshots)
                : null);

        if (qualityReason != null)
        {
            string qualityCode = qualityReason switch
            {
                "gap_guard_exceeded" => DiagnosticsHelper.CodeBendersGapGuardExceeded,
                "time_guard_exceeded" => DiagnosticsHelper.CodeBendersTimeGuardExceeded,
                "progress_guard_triggered" => DiagnosticsHelper.CodeBendersProgressGuardTriggered,
                "cut_efficiency_low" => DiagnosticsHelper.CodeBendersCutEfficiencyLow,
                "trajectory_weak" => DiagnosticsHelper.CodeBendersTrajectoryWeak,
                _ => DiagnosticsHelper.CodeBendersFailed
            };
            DiagnosticsHelper.PushGroupedNote(
                notes, DiagnosticsHelper.LevelDiagnostic, DiagnosticsHelper.GroupSolver,
                qualityCode, $"benders quality reason: {qualityReason}");

            double qualityScore = BendersStrategy.ResolveQualityScore(
                adaptive: config,
                qualityGuard: qualityGuard,
                bendersIterations: result.BendersIterations,
                bendersGap: result.Gap,
                bendersTimeMs: result.TimeMs,
                executedIterations: result.RuntimeMetrics?.ExecutedIterations,
                totalCuts: result.RuntimeMetrics?.TotalCuts,
                iterationSnapshots: result.RuntimeMetrics != null
                    ? GetSnapshotObjectives(result.RuntimeMetrics.IterationSnapshots)
                    : null);
            DiagnosticsHelper.PushGroupedNote(
                notes, DiagnosticsHelper.LevelDiagnostic, DiagnosticsHelper.GroupSolver,
                DiagnosticsHelper.CodeBendersQualityScore, $"quality_score={qualityScore:F2}");

            if (request.ResolvedSolvePolicy.BendersFallbackToMilp)
            {
                DiagnosticsHelper.PushGroupedNote(
                    notes, DiagnosticsHelper.LevelDiagnostic, DiagnosticsHelper.GroupSolver,
                    DiagnosticsHelper.CodeBendersQualityAction, $"action=fallback_to_milp reason={qualityReason}");
                return ResponseDTO.NoSolution("BendersQualityInsufficient_MilpFallback", notes);
            }
            else
            {
                DiagnosticsHelper.PushGroupedNote(
                    notes, DiagnosticsHelper.LevelDiagnostic, DiagnosticsHelper.GroupSolver,
                    DiagnosticsHelper.CodeBendersQualityAction, $"action=accept_benders reason={qualityReason}");
            }
        }

        // Report Benders diagnostics
        notes.Add($"benders_iterations={result.BendersIterations}");
        notes.Add($"benders_gap={result.Gap:F6}");
        notes.Add($"benders_time_ms={result.TimeMs}");
        DiagnosticsHelper.PushGroupedNote(
            notes, DiagnosticsHelper.LevelDiagnostic, DiagnosticsHelper.GroupSolver,
            DiagnosticsHelper.CodeBendersIterations, $"iterations={result.BendersIterations}");
        DiagnosticsHelper.PushGroupedNote(
            notes, DiagnosticsHelper.LevelDiagnostic, DiagnosticsHelper.GroupSolver,
            DiagnosticsHelper.CodeBendersGap, $"gap={result.Gap:F6}");
        DiagnosticsHelper.PushGroupedNote(
            notes, DiagnosticsHelper.LevelDiagnostic, DiagnosticsHelper.GroupSolver,
            DiagnosticsHelper.CodeBendersTimeMs, $"time_ms={result.TimeMs}");

        // **不拿主问题的解去造分配**：`result.Solution` 是主问题**模型 token 顺序**的值（theta 加二元变量），
        // 而那个旧解码器按"货 × 舱位"读它。 已经记录过这条隐患一次（那时候读出来是空分配），
        // 这里不再是隐患而是**确定的**：长度对不上就一定错位，对得上也只是碰巧。
        //
        // 所以这条路径交**目标值 + 状态**，分配留空并写明原因。等 demo2 换成框架的
        // `DiscreteLoadingApplicationService`（已确认那条才是基线路径）之后，分配由框架侧的
        // `MergeBendersValues` 按变量身份合并，**那时才有人负责解码**。
        //
        // **The master solution is not turned into assignments**: `result.Solution` is in master **model token order** (theta plus binaries), while
        // The old decoder read it as "cargo x position". recorded this hazard once (it read out empty assignments then); here it is no longer a hazard
        // but a certainty: a length mismatch always misplaces, and a match is a coincidence. This path therefore hands back **the objective and the status**,
        // leaves assignments empty and says why. Once demo2 moves to the framework `DiscreteLoadingApplicationService` ( established that as the baseline
        // path), assignments come from the framework's `MergeBendersValues`, merged by variable identity — **that is when someone decodes them**.
        notes.Add($"benders_assignments=not_decoded:master_solution_length={result.Solution.Length}");
        return ResponseDTO.Optimal(result.Obj, assignments: null, notes: notes);
    }

    /// <summary>
    /// **单模型 + 阶段表**：按基线形状建一个模型，交给 `LinearBendersPartition.BuildAsync` 拆分。
    /// **One model plus a stage table**: build a single model in the baseline's shape and let `LinearBendersPartition.BuildAsync` split it.
    /// </summary>
    /// <param name="stowageMode">装载模式 / The stowage mode.</param>
    /// <param name="notes">备注 / Notes.</param>
    /// <returns>一个模型（或失败）/ One model (or a failure).</returns>
    /// <remarks>
    /// 与 `BuildBendersModels`（两个模型）**并列存在**，不是替换：替换要等这条路径的门禁全绿（只建得起、还没接进分派）。
    /// 三个注册半在这里合成一个：主问题的四处 + 追索的重量两处。业载只注册一次——那正是 `RegisterForBendersSPWithoutPayload` 存在的理由。
    ///
    /// **`var` 的注册顺序不用猜**：主问题半先跑，业载于是已经在模型里；追索半只补重量。反过来会失败，判据里有实测。
    ///
    /// It **coexists** with `BuildBendersModels` (two models) rather than replacing it: replacing waits until this path's gates are green ( only builds it, it is not
    /// dispatched yet). Three registration halves meet in one model: the four master ones plus the two recourse weight ones. The payload is registered once — that is
    /// exactly why `RegisterForBendersSPWithoutPayload` exists. **The order is not a guess**: the master half runs first, so the payload is already in the model, and
    /// the recourse half adds only the weights. Reversed, it fails, and a criterion measures that.
    /// </remarks>
    internal LinearMetaModel<Flt64>? BuildSingleBendersModel(StowageMode stowageMode, List<string> notes) {
        notes.Add("benders_single_model=attempted");
        var model = new LinearMetaModel<Flt64>("demo2_full_load_single", ObjectCategory.Minimum);

        // **注册逐个跑、逐个记符号数**：数组初始化写法会让四处注册在**建数组时**就跑完，
        // 于是"每半各加了多少"根本量不到—— 的探针就是这么量出一个假的 `15->15`。
        // **Registrations run one at a time, each recording its symbol count**: with the array-initializer form the four registrations run **while the array is built**,
        // so "how much each half added" cannot be measured at all — that is how the earlier probe produced a meaningless `15->15`.
        var masterHalves = new (string Name, Func<Try> Run)[] {
            ("stowage", () => _stowageContext.RegisterForBendersMP(stowageMode, model)),
            ("mac", () => _macContext.RegisterForBendersMP(stowageMode, model)),
            ("soft_security", () => _softSecurityContext.RegisterForBendersMP(model)),
            ("mac_optimization", () => _macOptimizationContext.RegisterForBendersMP(stowageMode, model)),
        };
        int before = model.Tokens.Tokens.Count;
        int rowsBefore = model.Constraints.Count;
        int symbolsBefore = model.Tokens.Symbols.Count;
        foreach ((string name, Func<Try> run) in masterHalves) {
            Try registration = run();
            if (!registration.IsOk) {
                notes.Add($"benders_single_model_failed: the {name} half refused to register");
                model.Dispose();
                return null;
            }
            notes.Add($"benders_single_model=master_{name}:+{model.Tokens.Tokens.Count - before} tokens,+{model.Constraints.Count - rowsBefore} rows,+{model.Tokens.Symbols.Count - symbolsBefore} symbols");
            before = model.Tokens.Tokens.Count;
            rowsBefore = model.Constraints.Count;
            symbolsBefore = model.Tokens.Symbols.Count;
        }

        before = model.Tokens.Tokens.Count;
        symbolsBefore = model.Tokens.Symbols.Count;
        Try recourse = _stowageContext.RegisterForBendersSPWithoutPayload(model);
        if (!recourse.IsOk) {
            notes.Add("benders_single_model_failed: the recourse half refused to register");
            model.Dispose();
            return null;
        }
        notes.Add($"benders_single_model=recourse:+{model.Tokens.Tokens.Count - before} tokens,+{model.Constraints.Count - rowsBefore} rows,+{model.Tokens.Symbols.Count - symbolsBefore} symbols");

        notes.Add($"benders_single_model=built:tokens={model.Tokens.Tokens.Count},rows={model.Constraints.Count},symbols={model.Tokens.Symbols.Count},objectives={model.MetaSubObjects.Count}");
        return model;
    }

    // **`internal` 而不是 `private`**：两条路径的度量对比需要分别构建，
    // 若只能走 `Execute` 就得先造两个求解器——那会把"量数"变成"跑求解"。
    // **`internal` rather than `private`**: the comparison needs both paths built separately; going through `Execute` would require two solvers, turning a
    // measurement into a solve.
    /// <summary>
    /// **单模型 Benders 求解**（基线形状）：建一个模型 → 划分 → 交给 `LinearBendersAlgorithm`。
    /// **Single-model Benders** (the baseline shape): build one model, partition it, hand it to `LinearBendersAlgorithm`.
    /// </summary>
    /// <param name="stowageMode">装载模式 / The stowage mode.</param>
    /// <param name="solver">Benders 后端 / The Benders backend.</param>
    /// <param name="milpSolver">MILP 后端（回退用）/ The MILP backend (for the fallback).</param>
    /// <param name="notes">备注 / Notes.</param>
    /// <returns>响应 / The response.</returns>
    /// <remarks>
    /// **还没有接进分派**：`Execute` 仍走两模型那条路。这里只让"单模型 + 框架算法"这条链真的跑到求解器，
    /// 由 `Demo2SingleModelSolveTests` 量它、由那批的结论决定什么时候翻开关。
    ///
    /// **成功时不编造分配**（的教训）：`LinearBendersResult` 的取值映射还没做，返回最优值但不返回分配，
    /// 就像旧路径那样把"没解码"写进备注。
    ///
 /// **Not dispatched yet**: `Execute` still takes the two-model route. only makes the "single model plus framework algorithm" chain actually reach the
    /// solver; `Demo2SingleModelSolveTests` measures it and decides when to flip the switch. **No fabricated assignments on success** (the earlier lesson): the value mapping
    /// from `LinearBendersResult` is not done yet, so it returns the optimum without assignments and says "not decoded" in the notes, exactly like the old path.
    /// </remarks>
    internal async Task<ResponseDTO> SolveSingleModelBendersAsync(
        StowageMode stowageMode,
        Fuookami.Ospf.Framework.Solver.IBendersDecompositionSolver solver,
        ILinearSolver? milpSolver,
        List<string> notes) {
        ArgumentNullException.ThrowIfNull(solver);
        ArgumentNullException.ThrowIfNull(notes);

        LinearMetaModel<Flt64>? model = BuildSingleBendersModel(stowageMode, notes);
        if (model is null) {
            return ResponseDTO.NoSolution("BendersSingleModelNotBuilt", notes);
        }

        using (model) {
            // **阶段表按"整数 ⇒ 主问题"声明**： 按名字前缀挑决策变量，漏掉了 `empty_between_cargo_*` 里那些整数辅助变量，
            // 划分器直接拒绝（"整数变量不能作为连续追索"）。**正确规则是类型，不是名字**：`TypeKind.IsIntegerType` 为真就必须在主问题。
            //
            // **The stage table declares by type, not by name**: picked decisions by name prefix and missed the integer auxiliary variables inside
            // `empty_between_cargo_*`, so the partitioner refused. The rule is the type: `TypeKind.IsIntegerType` must mean `Master`.
            IReadOnlyDictionary<Fuookami.Ospf.Core.Variable.IVariableItem, LinearBendersStage> stages = BuildBendersStages(model);
            int integerCount = 0;
            foreach (LinearBendersStage stage in stages.Values) {
                if (stage == LinearBendersStage.Master) {
                    integerCount++;
                }
            }
            notes.Add($"benders_stages=declared:{stages.Count};integer={integerCount}");
            Result<LinearBendersModels, ErrorCode, Error<ErrorCode>> partitioned =
                await LinearBendersPartition.BuildAsync(model, stages);
            // **的历史**：：给 demo2 声明阶段表（`occupied_*`/`assignment_*` → `Master`）之后，**模型根本没到求解器**
            // （`MasterCalls == 0`，两条判据同时红）。 早量过"声明阶段不会收窄追索"，所以这次声明**没有收益、只有风险**，已回退。
            // 要接阶段表，得先弄清 `LinearBendersPartition` 对"与行规则不一致的声明"是什么反应——**那是后续的事**。
            //
            // **tried this and measurement rejected it**: declaring a stage table meant the **model never reached the solver** (`MasterCalls == 0`, two criteria red at
            // once). already measured that declaring stages **does not narrow the recourse**, so the declaration added risk without benefit; it is reverted. Wiring it needs
            // the answer to "how does the partitioner react to a declaration that disagrees with its row rule" — next batch.
            if (partitioned is not Ok<LinearBendersModels, ErrorCode, Error<ErrorCode>> partitionedOk) {
                notes.Add($"benders_partition_failed: {partitioned}");
                return ResponseDTO.NoSolution("BendersPartitionFailed", notes);
            }

            using LinearBendersModels models = partitionedOk.Value;
            notes.Add($"benders_split=master_rows={models.Master.Constraints.Count};sub_rows={models.Sub.Constraints.Count}");

            Result<LinearBendersResult, ErrorCode, Error<ErrorCode>> solved =
                await new LinearBendersAlgorithm(solver).SolveAsync(models);
            if (solved is Ok<LinearBendersResult, ErrorCode, Error<ErrorCode>> solvedOk) {
                // **缺口的形状要写清楚**：不是"忘了解码"，是**没有一条从 `Item` 到变量的映射**。
                // 没有映射时硬解只能靠位置猜，而猜错的分配看起来和真分配一模一样。
                //
                // **Name the gap exactly**: it is not "we forgot to decode", it is that **nothing maps an `Item` to its variable**. Decoding by position would be a
                // guess, and a wrong assignment looks exactly like a right one.
                // **按名字解码，最多 6 个 `assignment_*`**：名字对不上就报错，**绝不退回按位置取**——
                // 位置取错的分配看起来和真分配一模一样（的教训）。非二元值（如 `0.5`）同样拒。
                //
                // **Decode by name, at most the six `assignment_*` variables**: when a name does not match, say so — **never fall back to position**, because a
                // positionally misread assignment looks exactly like a real one . Non-binary values such as `0.5` are refused too.
                // **解码器只吃"名字 → 值"**：这样它在**默认门禁里也能被直接测**——不需要求解器，也不需要构造变量对象。
                // **The decoder takes "name to value" only**: that lets it be **tested in the default gate**, with no solver and no variable objects.
                var named = new Dictionary<string, double>(global::System.StringComparer.Ordinal);
                foreach ((IVariableItem variable, Flt64 value) in solvedOk.Value.Values) {
                    named[variable.Name] = value.ToDouble();
                }
                (IReadOnlyList<string>? decoded, string how) = DecodeAssignments(named);
                // **迭代与割的数也要写出来**： 量到"主问题全解成 0"，要判断是"下界还没被割修正"还是"目标没奖励装载"，
                // 先得有 `iterations`（一轮就停说明割从没起作用）。
                //
                // **Iterations and cut count go into the notes too**: measured "the master assigns nothing", and telling "the bound has not been corrected by cuts yet"
                // from "the objective does not reward loading" needs the iteration count (stopping after one round means the cuts never acted).
                notes.Add($"benders_iterations={solvedOk.Value.Iterations.Count};gap={solvedOk.Value.Gap}");
                // **绑定数是根因候选**：`Bindings` 是主问题决策变量到子问题的通道。**为 0 时子问题收不到主问题的决策**，
                // 于是它自己解自己的，主问题的零分配不受任何约束，间隙自然为零——这会让 Benders "收敛"到一个假的共识。
                //
                // **The binding count is the root-cause candidate**: `Bindings` is the channel from the master's decisions into the sub. **At zero the sub never
                // receives the master's decisions**, so it optimises on its own, the master's zero assignment constrains nothing, and the gap closes on a false agreement.
                notes.Add($"benders_bindings={models.Bindings.Count};stage_variables={models.Variables.Count}");
                // **子问题的行名要能看见**： 说装载约束在子问题，那就把**具体的行名**列出来——
                // "在子问题"这句话，只有看得见行名才算证据。
                //
                // **The sub's row names must be visible**: claims the loading constraints sit in the sub, so list **the actual row names** — "they are in the sub" is
                // evidence only when the names can be read.
                notes.Add($"benders_sub_rows={string.Join(",", System.Linq.Enumerable.ToList(models.Sub.Constraints).ConvertAll(constraint => constraint.Name))}");
                notes.Add($"benders_assignments={how}");
                return ResponseDTO.Optimal(
                    solvedOk.Value.Objective.ToDouble(), assignments: decoded, notes: notes);
            }

            // **失败原因要带原文**： 只量到"子问题之后失败"，说不清是哪一步。
            // 把框架算法返回的错误原文记进备注，下一个人不用再猜、也不用再跑一次探针。
            //
            // **The failure text itself goes into the notes**: only measured "it fails after the sub". Carrying the algorithm's own message means the next
            // reader does not have to guess or run another probe.
            notes.Add($"benders_failed_detail: {solved}");

            // **两种"没走回退"照旧分成两句**。
            if (milpSolver is null) {
                notes.Add("benders_failed: policy allows the MILP fallback, but no MILP solver was supplied on this path");
            }
            else {
                notes.Add("benders_failed: a MILP solver was supplied, but the fallback needs a fresh model and the domain contexts are single-use");
            }
            return ResponseDTO.NoSolution("benders_failed_without_milp_fallback", notes);
        }
    }
    /// <summary>
    /// 构建 Benders 分解模型。Build master and sub models for Benders decomposition.
    ///
    /// Master: stowage (binary loading decisions) + mac + soft_security + mac_optimization
    /// Sub: airworthiness constraints (weight/CG/MAC checks)
    /// </summary>
    internal BendersModels BuildBendersModels(StowageMode stowageMode, List<string> notes)
    {
        // **这个空列表只因为签名要求一个解**：两处子问题注册都不读它（见上）。它不是"解"，不要当解用。
        // **This empty list exists only because the signature demands a solution**: neither sub registration reads it (see above). It is not a solution; do not use it as one.
        IReadOnlyList<double> masterSolution = [];
        var masterModel = new LinearMetaModel<Flt64>("demo2_full_load_master", ObjectCategory.Minimum);
        var subModel = new LinearMetaModel<Flt64>("demo2_full_load_sub", ObjectCategory.Minimum);

        // **主问题： 接上了**。基线把「配载 + MAC + 软安全 + MAC 优化」放进主问题，这四处现在真的往里注册——
        // 在这之前这里是一段注释，唯一的实际动作是加一个 theta 变量，于是主问题**零约束**，`SolveWithBenders` 只能用"没注册"拒绝。
        //
        // 四个上下文的签名本来就不一致（软安全与适航安全不收模式），照各自的签名调，**不去统一它们**——统一需要证据。
        //
        // **The master problem is wired.** The baseline puts stowage, MAC, soft security and MAC optimisation into the master. Before this the body was
        // a comment and the only real action was adding a theta variable, so the master had **zero constraints** and `SolveWithBenders` could only refuse with
        // "not registered". The four contexts' signatures already differ (soft security and airworthiness take no mode); each is called with its own signature,
        // **and they are not unified** — unifying needs evidence.
        // **逐个跑、失败时点名是哪一半**：数组初始化写法会让四处注册在**建数组时**就跑完，
        // 于是失败只能报成"某一半不肯注册"——**一个不点名的诊断等于没有诊断**（在单模型路径上已经踩过）。
        // **One at a time, naming the half that fails**: with the array-initializer form the four registrations run **while the array is built**, so a failure
        // can only be reported as "some half refused to register" — **an unnamed diagnostic is no diagnostic** ( already hit this on the single-model path).
        (string Name, Func<Try> Run)[] masterRegistrations = [
            ("stowage", () => _stowageContext.RegisterForBendersMP(stowageMode, masterModel)),
            ("mac", () => _macContext.RegisterForBendersMP(stowageMode, masterModel)),
            ("soft_security", () => _softSecurityContext.RegisterForBendersMP(masterModel)),
            ("mac_optimization", () => _macOptimizationContext.RegisterForBendersMP(stowageMode, masterModel)),
        ];
        foreach ((string name, Func<Try> run) in masterRegistrations)
        {
            Try registration = run();
            if (!registration.IsOk)
            {
                // **不在返回 Result 的方法里抛异常**（本仓的错误处理规矩）：把失败写成备注，让下面的
                // `MasterModel.Constraints.Count == 0` 守卫接手——**失败的注册不会悄悄变成"注册成功但没内容"**。
                //
                // **No throwing in a method that returns a Result** (this port's error-handling rule): record the failure as a note and let the
                // `MasterModel.Constraints.Count == 0` guard take over — **a failed registration never quietly becomes "registered but empty".**
                notes.Add($"benders_master_registration_failed: the {name} half refused to register into the master model: {registration}");
            }
        }

        // **子问题也接上了**，而且**就放在建模型这一步、不进迭代循环**。原因是实测：
        //
        // - `StowageAggregation.RegisterForBendersSP(stowageMode, model)` **根本没有解这个参数**（的签名就只有两项）；
        // - `AirworthinessSecurityContext.RegisterForBendersSP(mode, model, solution)` 收到解之后转给 `Register(mode, model)`，**照样不读它**。
        //
        // 也就是说：基线的 `registerForBendersSP(mode, model, solution)` 里的 `solution` **在本仓当前实现下是死的**。
        // 既然如此，把注册按"每轮迭代"搬过来只会得到一个**每轮做同样事情的循环**。所以注册一次，位置在解出来之前。
        //
        // **这个"解是死的"结论用判据钉住了**（`Demo2BendersSubModelTests`）：两个不同的解注册出来的模型形状必须完全相同。
        // 哪天哪个域开始真的读解，那条判据就红，**那时才把它挪进循环**。
        //
        // **The sub-problem is wired as well**, and it is registered **while building the models, not inside the iteration loop**, because of a measurement:
        // `StowageAggregation.RegisterForBendersSP(stowageMode, model)` **has no solution parameter at all**, and `AirworthinessSecurityContext.RegisterForBendersSP(
        // mode, model, solution)` forwards to `Register(mode, model)` and **does not read it either**. So the baseline's `solution` argument is **dead in this port's
        // current implementation**: registering per iteration would be a loop that repeats the same work. Hence one registration, before any solution exists. **The
        // "the solution is dead" conclusion is pinned by a criterion** (`Demo2BendersSubModelTests`): two different solutions must yield identical model shapes. If a
        // domain ever starts reading it, that criterion reds, **and only then should this move into the loop.**
        Try[] subRegistrations = [
            _stowageContext.RegisterForBendersSP(stowageMode, subModel, masterSolution),
            _airworthinessSecurityContext.RegisterForBendersSP(stowageMode, subModel, masterSolution),
        ];
        foreach (Try registration in subRegistrations)
        {
            if (!registration.IsOk)
            {
                notes.Add("benders_sub_registration_failed: a domain refused to register into the sub model");
            }
        }

        // Create theta variable for cut generation
        var thetaVar = new URealVar("benders_theta");
        masterModel.Add(thetaVar);

        // Build fixedVariables from stowage x variables (binary loading decisions)
        // When stowage aggregation is fully ported, iterate over stowage items/positions
        var fixedVariables = new Dictionary<IVariableItem, Flt64>();

        return new BendersModels(masterModel, subModel, thetaVar, fixedVariables);
    }

    /// <summary>
    /// 提取迭代快照目标值。Extract objective values from iteration snapshots.
    /// </summary>
    private static List<double> GetSnapshotObjectives(IReadOnlyList<BendersIterationSnapshot> snapshots)
    {
        var result = new List<double>(snapshots.Count);
        foreach (var s in snapshots)
        {
            result.Add(s.MasterObj);
        }
        return result;
    }

    /// <summary>
    /// 初始化各域上下文 / Initialise the domain contexts.
    /// </summary>
    /// <param name="request">请求 / The request.</param>
    /// <returns>执行结果 / The result.</returns>
    /// <remarks>
    /// **`internal` 而不是 `private`**：单模型路径还没有接进分派，于是它的判据必须先自己构造一个初始化好的应用。
    /// 等分派切过去，这个可见性就该收回去——那时判据走 `Execute` 就够了。
    ///
    /// **`internal` rather than `private`**: the single-model path is not dispatched yet, so its criterion must build an initialised application itself. Once the
    /// dispatch switches over, this visibility should go back — the criterion will then go through `Execute`.
    /// </remarks>
    internal Try Init(RequestDTO request)
    {
        // 记下请求：列生成分派要从它读分区，而 `Init` 是这条链上唯一拿到请求的地方。
        // Keep the request: the CG dispatch reads partitions from it, and `Init` is the only place on this chain that sees it.
        _lastRequest = request;
        Try r1 = _aircraftContext.Init(request);
        if (r1.IsFailed) return r1;

        // **模式必须传下去**：`StowageContext.Init` 的 `stowageMode` 默认是 `FullLoad`，不传就**静默地按全配载建模**，
        // 而请求里的谓词规格照样被注册——于是预配载得到"带着谓词变量的全配载模型"，不可行且不报错。 在流水线上修过同一处，
        // 三个应用各存了一份自己的 `Init`，所以这里**各修一遍**（它们没有共用那条路径）。
        //
        // **The mode has to be passed down**: `StowageContext.Init`'s `stowageMode` defaults to `FullLoad`, so omitting it **silently models full load** while
        // the request's predicate specs are still registered — predistribution ends up as "a full-load model carrying predicate variables", infeasible and silent.
        // fixed exactly this in the pipeline; each of the three applications keeps its own `Init`, so it is fixed **in each** (they share no path).
        Try r2 = _stowageContext.Init(_aircraftContext, request, StowageMode.FullLoad);
        if (r2.IsFailed) return r2;

        Try r3 = _macContext.Init(_aircraftContext, _stowageContext, request);
        if (r3.IsFailed) return r3;

        Try r4 = _airworthinessSecurityContext.Init(_aircraftContext, _stowageContext, _macContext, request);
        if (r4.IsFailed) return r4;

        Try r5 = _softSecurityContext.Init(_aircraftContext, _stowageContext, request);
        if (r5.IsFailed) return r5;

        Try r6 = _macOptimizationContext.Init(_aircraftContext, _stowageContext, _macContext, request);
        if (r6.IsFailed) return r6;

        // 全配载的最后两域：顺序与管线一致，效能域要配载聚合。
        // The last two domains of full load , in the pipeline's order; the effectiveness domains need the stowage aggregation.
        Try r7 = _expressEffectivenessContext.Init(_stowageContext, request, StowageMode.FullLoad);
        if (r7.IsFailed)
        {
            return r7;
        }

        Try r8 = _loadingEffectivenessContext.Init(_stowageContext, request, StowageMode.FullLoad);
        if (r8.IsFailed)
        {
            return r8;
        }

        return Results.Ok<Success>(Results.SuccessInstance);
    }


    private (ResponseDTO Response, RenderDTO? Render) SolveDirectMilp(ILinearSolver solver, List<string> notes, Parameter parameter) =>
        DirectMilpRunner.Run(
            model => {
                Try[] registrations = [
                    _stowageContext.Register(StowageMode.FullLoad, model),
                    _macContext.Register(StowageMode.FullLoad, model),
                    _airworthinessSecurityContext.Register(StowageMode.FullLoad, model),
                    _softSecurityContext.Register(StowageMode.FullLoad, parameter, model),
                    _macOptimizationContext.Register(StowageMode.FullLoad, parameter, model),
                    _expressEffectivenessContext.Register(StowageMode.FullLoad, parameter, model),
                    _loadingEffectivenessContext.Register(StowageMode.FullLoad, parameter, model),
                ];
                foreach (Try registration in registrations) {
                    if (registration is Failed<Success, ErrorCode, Error<ErrorCode>> failed) {
                        return failed;
                    }
                }
                return Results.Ok<Success>(Results.SuccessInstance);
            },
            solver,
            notes,
            (values, model) => SolutionRenderer.RenderSolution(values, model, _stowageContext.Aggregation));

    /// <summary>
    /// 跑分区列生成 / Run partition column generation.
    /// </summary>
    /// <param name="solver">列生成求解器 / The column-generation solver.</param>
    /// <param name="notes">备注 / Notes.</param>
    /// <param name="parameter">求解参数 / Solve parameter.</param>
    /// <returns>响应与渲染 / The response and the render.</returns>
    /// <remarks>
    /// **与 `SolveDirectMilp` 同一份七域注册**，但注册进一张**调用方持有**的模型：列生成要在完整模型上做投影，
    /// 所以要拿到的不是"解完就扔"的那张，而是注册完还活着的这张。
    ///
    /// **注册清单与直连 MILP 那份必须一致**：两份清单会漂移，而漂移的症状是"列生成跑完、约束少了七行"——
    /// 这类差异在结果里看不出来。 记的就是同一类缺陷（漏域不报错，只是少约束）。
    ///
    /// **The same seven-domain registration as `SolveDirectMilp`**, into a model the **caller keeps**: column generation projects against the full model, so the model
    /// must still be alive after registration rather than discarded once solved. **The registration list must match the direct-MILP one** — the two would drift, and
    /// drift shows up as "column generation finished with seven rows missing", which no result reveals. recorded the same defect class: a missing domain never
    /// errors, it just leaves fewer constraints.
    /// </remarks>
    internal (ResponseDTO Response, RenderDTO? Render) SolvePartitionColumnGeneration(
        Fuookami.Ospf.Framework.Solver.IColumnGenerationSolver solver,
        List<string> notes,
        Parameter parameter) {
        ArgumentNullException.ThrowIfNull(notes);
        using var model = new LinearMetaModel<Flt64>("demo2_partition_column_generation", ObjectCategory.Minimum);
        Try[] registrations = [
            _stowageContext.Register(StowageMode.FullLoad, model),
            _macContext.Register(StowageMode.FullLoad, model),
            _airworthinessSecurityContext.Register(StowageMode.FullLoad, model),
            _softSecurityContext.Register(StowageMode.FullLoad, parameter, model),
            _macOptimizationContext.Register(StowageMode.FullLoad, parameter, model),
            _expressEffectivenessContext.Register(StowageMode.FullLoad, parameter, model),
            _loadingEffectivenessContext.Register(StowageMode.FullLoad, parameter, model),
        ];
        foreach (Try registration in registrations) {
            if (registration is Failed<Success, ErrorCode, Error<ErrorCode>> failed) {
                notes.Add("partition_column_generation_failed: a domain refused to register: " + failed.Error);
                // **基线这条路径是 `fail(Err(ApplicationFailed, "缺少列生成注册"))`**，
                // 而 `fail` 只把 `ORModelInfeasible` 映射成 `"Infeasible"`、`OREngineSolvingException` 映射成 `"NotConverged"`，
                // **其余一律 `"Error"`**——**`DomainRegisterFailed` 是本仓自造的。**
                //
                // **The baseline takes `fail(Err(ApplicationFailed, ...))` here**, and `fail` maps only
                // `ORModelInfeasible` to `"Infeasible"` and `OREngineSolvingException` to `"NotConverged"`; **everything else is `"Error"` —
                // `DomainRegisterFailed` was invented here.**
                return (new ResponseDTO(Succeed: false, Status: "Error", Notes: [.. notes, failed.Error.Message],
                    Diagnostics: DiagnosticsHelper.BuildStructured([.. notes, failed.Error.Message])), null);
            }
        }
        if (_stowageContext.Aggregation is not { } aggregation) {
            notes.Add("partition_column_generation_failed: the stowage aggregation is missing");
            return (ResponseDTO.NoSolution("InitFailed", notes), null);
        }
        notes.Add($"partition_model=rows{model.Constraints.Count},symbols{model.Tokens.Symbols.Count}");
        return PartitionColumnGenerationRunner.Run(_lastRequest!, aggregation, model, solver, notes);
    }

    /// <summary>**最近一次 `Init` 的请求**，列生成分派要它来读分区 / The request from the latest `Init`, which the CG dispatch reads partitions from.</summary>
    private RequestDTO? _lastRequest;

    /// <summary>
    /// Benders 模型容器。Container for Benders master/sub models and shared state.
    /// </summary>
    // **`internal` 与 `BuildBendersModels` 同步**：两个模型要能被度量对比拿出来。
    // **`internal` alongside `BuildBendersModels`**: the pair must be reachable for the measurement.
    internal sealed record BendersModels(
        LinearMetaModel<Flt64> MasterModel,
        LinearMetaModel<Flt64> SubModel,
        IVariableItem ObjectVariable,
        Dictionary<IVariableItem, Flt64> FixedVariables);
    /// <summary>**按名字解码分配** / Decode assignments by variable name.</summary>
    /// <param name="values">按变量身份的解 / Values by variable identity.</param>
    /// <returns>分配与一句说明 / The assignments and one sentence saying how it went.</returns>
    /// <remarks>
    /// 三种结局都有名字：`decoded:N`、`not_decoded:no_assignment_variables`、`not_decoded:non_binary:{name}`。
    /// **沉默地给一个空列表最坏**——它和"什么都没装"长得一模一样。
    ///
    /// Three outcomes, each named. **Silently returning an empty list is the worst outcome** — it looks exactly like "nothing was loaded".
    /// </remarks>
    internal static (IReadOnlyList<string>? Assignments, string How) DecodeAssignments(
        IReadOnlyDictionary<string, double> values) {
        const string prefix = "assignment_";
        const double tolerance = 1e-6;
        List<string> assignments = [];
        int seen = 0;
        foreach ((string name, double raw) in values) {
            if (!name.StartsWith(prefix, global::System.StringComparison.Ordinal)) {
                continue;
            }
            seen++;
            if (global::System.Math.Min(global::System.Math.Abs(raw), global::System.Math.Abs(raw - 1.0)) > tolerance) {
                return (null, $"not_decoded:non_binary:{name}");
            }
            if (raw > 0.5) {
                assignments.Add(name[prefix.Length..].Replace("_", "->", global::System.StringComparison.Ordinal));
            }
        }
        if (seen == 0) {
            return (null, "not_decoded:no_assignment_variables");
        }
        // **空分配不算解码**：真后端上主问题把 6 个 `assignment_*` **全解成 0**，于是"解出来 0 个分配"和
        // "什么都没装"**长得一模一样**——这正是不许沉默返回空列表的理由。不交给调用方，也不假装成功。
        //
        // **An empty assignment is not a decode** (measured): on the real backend the master solves all six `assignment_*` variables to **zero**, so "decoded zero
        // assignments" and "nothing was loaded" **look the same** — exactly why an empty list must never be returned silently . It is neither handed on nor
        // passed off as success.
        if (assignments.Count == 0) {
            return (null, "not_decoded:empty_master_assignment");
        }
        return (assignments, $"decoded:{assignments.Count}");
    }
    /// <summary>**按变量类型声明阶段表** / Declare the stage table by variable type.</summary>
    /// <param name="model">单模型 / The single model.</param>
    /// <returns>变量到阶段的映射 / Variable to stage.</returns>
    /// <remarks>
    /// **规则是类型**：整数（含二进制）变量必须在主问题，其余是连续追索。 按名字前缀挑，漏了 `empty_between_cargo_*` 的整数辅助变量，
    /// 划分器因此拒绝——**名字会变，类型不会**。
    ///
    /// **The rule is the type**: integer (including binary) variables belong in the master and everything else is continuous recourse. picked by name prefix, missed
    /// the integer auxiliary variables inside `empty_between_cargo_*` and the partitioner refused — **names change, types do not**.
    /// </remarks>
    private static Dictionary<Fuookami.Ospf.Core.Variable.IVariableItem, LinearBendersStage> BuildBendersStages(
        LinearMetaModel<Flt64> model) {
        var stages = new Dictionary<Fuookami.Ospf.Core.Variable.IVariableItem, LinearBendersStage>();
        foreach (Fuookami.Ospf.Core.Token.Token<Flt64> token in model.Tokens.Tokens) {
            Fuookami.Ospf.Core.Variable.IVariableItem variable = token.Variable;
            stages[variable] = variable.TypeKind.IsIntegerType
                ? LinearBendersStage.Master
                : LinearBendersStage.ContinuousRecourse;
        }
        return stages;
    }
}
