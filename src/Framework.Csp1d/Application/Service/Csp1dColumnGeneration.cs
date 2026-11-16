#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Framework.Csp1d.Application.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.LengthAssignment.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Error;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Yield.Model;
using Fuookami.Ospf.Core.Solver.Progress;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Framework.Csp1d.Application.Service;
/// <summary>
/// CSP1D 列生成求解器 / CSP1D column generation solver.
///
/// 实现列生成主循环：初始方案生成 -> LP 松弛求解 -> pricing 定价 -> 加列迭代 -> 最终 MILP 整数求解。
/// 支持 flow policy 自定义终止/去重/早停逻辑、warm start 初始方案注入、以及多种 pricing 生成器。
///
/// Implements the column generation main loop: initial plan generation -> LP relaxation solve ->
/// pricing -> column addition iteration -> final MILP integer solve.
/// Supports flow policy custom termination/deduplication/early-stop logic, warm start initial
/// plan injection, and multiple pricing generators.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public sealed class Csp1dColumnGeneration<V> where V : struct {
    private readonly IColumnGenerationSolver _solver;
    private readonly ICsp1dInitialCuttingPlanGenerator<CuttingPlan<V>, GenerationInput<V>> _initialGenerator;
    private readonly ICsp1dPricingGenerator<CuttingPlan<V>, Csp1dPricingInput<V>> _pricingGenerator;
    private readonly ICsp1dSolutionAnalyzer<V> _analyzer;
    private readonly YieldModelingConfig<V>? _yieldConfig;
    private readonly WasteMinimizationConfig<V>? _wasteConfig;
    private readonly LengthAssignmentModelingConfig<V>? _lengthConfig;
    private readonly IReadOnlyList<CuttingPlanUsage<V>> _warmStartPlanUsages;

    /// <summary>
    /// 列生成追踪信息 / Column generation trace.
    /// </summary>
    public sealed record Csp1dColumnGenerationTrace(
        UInt64 InitialPlanCount,
        UInt64 FinalPlanCount,
        IReadOnlyList<UInt64> PricedPlanCount,
        IReadOnlyList<Csp1dIterationRecord> Iterations,
        Csp1dTerminationReason TerminationReason = Csp1dTerminationReason.PricingConverged,
        CuttingPlanGenerationStatistics? InitialGenerationStatistics = null,
        Csp1dFinalMilpStatus FinalMilpStatus = Csp1dFinalMilpStatus.NotAttempted,
        bool PartialSolutionAvailable = false,
        string? FailureMessage = null,
        CuttingPlanGenerationStatistics? PricingGenerationStatistics = null,
        string? LpFailureMessage = null
    );

    /// <summary>
    /// 列生成结果 / Column generation result.
    /// </summary>
    public sealed record Csp1dColumnGenerationResult(
        Csp1dSolution<V> Solution,
        Csp1dColumnGenerationTrace Trace
    );

    private sealed record LpMaster(
        LinearMetaModel<Flt64> Model,
        Csp1dProduceContext<V> Context,
        V DomainValueSample
    );

    private sealed record InitialPlanPool(
        IReadOnlyList<CuttingPlan<V>> Plans,
        CuttingPlanGenerationStatistics? Statistics
    );

    private sealed record FinalMilpSolveResult(
        Csp1dFinalMilpStatus Status,
        Csp1dMilpSolver.MilpResult<V>? MilpResult,
        string? FailureMessage
    );

    private sealed record FlowContextImpl : ICsp1dFlowContext<V> {
        public long Iteration { get; init; }
        public IReadOnlyList<CuttingPlan<V>> CurrentPlans { get; init; } = Array.Empty<CuttingPlan<V>>();
        public long IterationLimit { get; init; }
        public bool AllowPartialSolution { get; init; }
        public IReadOnlyList<CuttingPlan<V>> NewPlans { get; init; } = Array.Empty<CuttingPlan<V>>();
        public bool HasValidLpResult { get; init; }
        public CuttingPlanGenerationStatistics? PricingStatistics { get; init; }
        public long WarmStartPlanCount => 0;
        public bool WarmStartRequiresFallback => false;
    }

    /// <summary>
    /// 构造列生成求解器 / Construct column generation solver.
    /// </summary>
    /// <param name="solver">列生成求解器 / Column generation solver.</param>
    /// <param name="initialGenerator">初始方案生成器 / Initial cutting plan generator.</param>
    /// <param name="pricingGenerator">定价方案生成器 / Pricing cutting plan generator.</param>
    /// <param name="analyzer">解分析器 / Solution analyzer.</param>
    /// <param name="yieldConfig">默认 yield 建模配置 / Default yield modeling config.</param>
    /// <param name="wasteConfig">默认 waste 建模配置 / Default waste modeling config.</param>
    /// <param name="lengthConfig">默认 length 建模配置 / Default length modeling config.</param>
    /// <param name="warmStartPlanUsages">warm start 方案使用量 / Warm start plan usages.</param>
    public Csp1dColumnGeneration(
        IColumnGenerationSolver solver,
        ICsp1dInitialCuttingPlanGenerator<CuttingPlan<V>, GenerationInput<V>>? initialGenerator = null,
        ICsp1dPricingGenerator<CuttingPlan<V>, Csp1dPricingInput<V>>? pricingGenerator = null,
        ICsp1dSolutionAnalyzer<V>? analyzer = null,
        YieldModelingConfig<V>? yieldConfig = null,
        WasteMinimizationConfig<V>? wasteConfig = null,
        LengthAssignmentModelingConfig<V>? lengthConfig = null,
        IReadOnlyList<CuttingPlanUsage<V>>? warmStartPlanUsages = null) {
        _solver = solver;
        _initialGenerator = initialGenerator ?? new SimpleInitialGenerator<V>();
        _analyzer = analyzer ?? new DefaultCsp1dSolutionAnalyzer<V>();
        _yieldConfig = yieldConfig;
        _wasteConfig = wasteConfig;
        _lengthConfig = lengthConfig;
        _warmStartPlanUsages = warmStartPlanUsages ?? Array.Empty<CuttingPlanUsage<V>>();
        _pricingGenerator = pricingGenerator ?? new ReducedCostPricingGenerator<V>(_initialGenerator);
    }

    /// <summary>
    /// 列生成求解（仅返回解）/ Column generation solve (returns solution only).
    /// </summary>
    /// <param name="problem">问题定义 / Problem definition.</param>
    /// <param name="solveConfig">显式求解配置 / Explicit solve configuration.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <param name="progressContext">进度上报上下文；为 null 时不启用 / Progress reporting context; off when null.</param>
    /// <returns>CSP1D 解 / CSP1D solution.</returns>
    public async Task<Csp1dSolution<V>> SolveAsync(
        Csp1dProblem<V> problem,
        Csp1dSolveConfig<V>? solveConfig = null,
        CancellationToken cancellationToken = default,
        SolverProgressContext? progressContext = null) =>
        (await SolveWithTraceAsync(problem, solveConfig, cancellationToken, progressContext)).Solution;

    /// <summary>
    /// 带追踪信息的列生成求解 / Column generation solve with trace.
    /// </summary>
    /// <param name="problem">问题定义 / Problem definition.</param>
    /// <param name="solveConfig">显式求解配置 / Explicit solve configuration.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <param name="progressContext">进度上报上下文；为 null 时不启用 / Progress reporting context; off when null.</param>
    /// <returns>列生成结果（含追踪信息）/ Column generation result with trace.</returns>
    /// <remarks>
    /// **取消有两套机制，必须一起生效。** `cancellationToken` 是平台原生的协作式取消（接入），
    /// `progressContext.IsCancelled` 是取消判定：每轮迭代开头检查一次。
    /// 两者**语义相同、来源不同**：调用方可能只给了其中之一。
    ///
    /// 所以这里**两条都查**，并在每轮迭代开头检查 `progressContext` 的阶段快照与取消。
    ///
    /// **There are two cancellation mechanisms and both must take effect.** `cancellationToken` is the platform's cooperative cancellation (wired);
    /// `progressContext.IsCancelled` is the cancellation predicate, checked once at the start of every iteration. The two are **semantically identical but sourced
    /// differently**: a caller may supply only one of them.
    ///
    /// So **both are checked here**, and each iteration start checks the `progressContext`'s stage snapshot and cancellation.
    /// </remarks>
    public async Task<Csp1dColumnGenerationResult> SolveWithTraceAsync(
        Csp1dProblem<V> problem,
        Csp1dSolveConfig<V>? solveConfig = null,
        CancellationToken cancellationToken = default,
        SolverProgressContext? progressContext = null) {
        cancellationToken.ThrowIfCancellationRequested();
        progressContext?.ThrowIfCancelled();
        Csp1dSolveConfig<V> resolvedConfig = ResolveSolveConfig(problem, solveConfig);
        Csp1dConfiguration<V> columnConfig = resolvedConfig.ColumnGeneration;
        IReadOnlyList<ICsp1dDomainPolicy<V>> domainPolicies = resolvedConfig.ExtensionSet.DomainPolicies;
        V? domainValueSample = problem.Demands.FirstOrDefault()?.Quantity.Value;
        if (domainValueSample is null) {
            Material<V>? firstMaterial = problem.Materials.FirstOrDefault();
            if (firstMaterial is not null) {
                domainValueSample = firstMaterial.WidthRange.UpperBound.Value;
            }
        }
        Func<Material<V>, Product<V>, Quantity<V>, bool>? widthCheck = domainValueSample is not null
            ? Csp1dDomainPolicyHelpers.WidthFeasibilityCheckFromPolicies(domainPolicies, domainValueSample.Value)
            : null;

        // **把 `ICsp1dGenerationStrategy.CanonicalKeyFor` 解析成回调列表。**
        //
        // 三个生成策略扩展点（`acceptCandidate` / `canonicalKeyFor` / `acceptDominance`）在 C# 里
        // **接口都定义了，却一个调用点都没有**——这是"接口在、连接件是空壳"的形态。
        // 这里只接 `CanonicalKeyFor`（缺口清单里点名的那一项），另两个仍未接线。
        //
        // **Resolve `ICsp1dGenerationStrategy.CanonicalKeyFor` into callback lists.**
        //
        // The three generation-strategy extension points (`acceptCandidate` / `canonicalKeyFor` / `acceptDominance`) are **all defined with zero call
 // sites** — the "interfaces present, connecting piece is a shell" shape. wires only `CanonicalKeyFor` (the gap named in the ledger); the other two
        // remain unwired.
        IReadOnlyList<global::System.Func<CuttingPlan<V>, string?>> canonicalKeyOverrides =
            Csp1dGenerationStrategyProjection.CanonicalKeyOverrides(resolvedConfig.ExtensionSet.GenerationStrategies);

        // **`AcceptDominance` 的投影，语义是"全部通过"。**
        //
        // 收集器里的 `dominanceAcceptOverride` 钩子**早就实现了**（`GenerationCollector.cs:187`），
        // 但四个生成器构造时**一个都没传**——又一处"钩子在、没人喂"。
        // 这里把它接上，同时把 `AcceptCandidate` 留给后续。
        //
        // **The `AcceptDominance` projection; the semantics are "all must pass".**
        //
        // The collector's `dominanceAcceptOverride` hook **already existed** (`GenerationCollector.cs:187`), but **none of the four generators passed it** — another
 // "hook present, nothing feeding it". wires it and leaves `AcceptCandidate` for the next one.
        IReadOnlyList<global::System.Func<CuttingPlan<V>, IReadOnlyList<CuttingPlan<V>>, bool>> dominanceAcceptOverrides =
            Csp1dGenerationStrategyProjection.DominanceAcceptOverrides(resolvedConfig.ExtensionSet.GenerationStrategies);

        // **`AcceptCandidate` 的投影。** 这是三个扩展点里最后一个，也是唯一一个
        // **第二个参数有实际差别**的：初始池传空列表、定价传当前列池（见 `ExistingPlans`）。
        //
        // **The `AcceptCandidate` projection.** The last of the three and the only one whose **second argument really differs**: the initial pool passes an empty list
        // and pricing passes the current column pool (see `ExistingPlans`).
        IReadOnlyList<global::System.Func<CuttingPlan<V>, IReadOnlyList<CuttingPlan<V>>, bool>> candidateFilters =
            Csp1dGenerationStrategyProjection.CandidateFilters(resolvedConfig.ExtensionSet.GenerationStrategies);

        InitialPlanPool initialPlanPool = await InitialPlanPoolAsync(
            problem,
            columnConfig,
            domainPolicies,
            widthCheck,
            resolvedConfig.ExtensionSet.FlowPolicies,
            canonicalKeyOverrides,
            dominanceAcceptOverrides,
            candidateFilters);
        cancellationToken.ThrowIfCancellationRequested();
        progressContext?.ThrowIfCancelled();
        IReadOnlyList<CuttingPlan<V>> initialPlans = initialPlanPool.Plans;
        int initialCount = initialPlans.Count;
        ReportProgress(
            context: progressContext,
            stage: SolverStages.ColumnGeneration,
            subStage: new SolverSubStage(
                Key: "initial_columns",
                DefaultTemplate: "初始列生成（{count} 列）",
                MessageKey: "i18n.ospf.substage.initial_columns",
                Args: new Dictionary<string, string> { ["count"] = initialCount.ToString() }),
            progressInStage: 0,
            overallProgress: 15,
            diagnostics: new Dictionary<string, string> { ["initialPlans"] = initialCount.ToString() });

        if (initialPlans.Count == 0) {
            return BuildNoInitialPlansResult(problem, initialPlanPool, resolvedConfig);
        }

        var currentPlans = initialPlans.ToList();
        var pricedPlanCounts = new List<UInt64>();
        var iterationRecords = new List<Csp1dIterationRecord>();
        Csp1dTerminationReason terminationReason = Csp1dTerminationReason.PricingConverged;
        bool hasValidLpResult = false;
        // 是否处于"只最小化人工短缺"的可行性阶段 / Whether the "minimise artificial deficits only" feasibility phase is active.
        bool feasibilityPhase = false;
        string? lpFailureMessage = null;
        CuttingPlanGenerationStatistics? pricingGenerationStatistics = null;
        IReadOnlyList<ICsp1dFlowPolicy<V>> flowPolicies = resolvedConfig.ExtensionSet.FlowPolicies;
        Int64 iterationLimit = columnConfig.IterationLimit;
        int iterationLimitBound = (int)iterationLimit.ToLong();

        V lpDomainValueSample = domainValueSample
            ?? currentPlans.FirstOrDefault()?.RestWidth?.Value
            ?? currentPlans.FirstOrDefault()?.DemandContributions.FirstOrDefault()?.Quantity.Value
            ?? default;

        if (EqualityComparer<V>.Default.Equals(lpDomainValueSample, default)) {
            return BuildNoInitialPlansResult(problem, initialPlanPool, resolvedConfig, "Cannot derive domain value sample for CSP1D LP master");
        }

        LpMaster? lpMaster = null;
        if (iterationLimitBound > 0) {
            lpMaster = await BuildLpMasterAsync(problem, currentPlans, resolvedConfig.AllExtensions, lpDomainValueSample);
            if (lpMaster is null) {
                pricedPlanCounts.Add(UInt64.Zero);
                iterationRecords.Add(new Csp1dIterationRecord(
                    Int64.Zero, Flt64.Zero, new Int64(currentPlans.Count),
                    UInt64.Zero, new Int64(currentPlans.Count)));
                terminationReason = Csp1dTerminationReason.LpInfeasible;
                lpFailureMessage = "LP master build failed";
            }
        }

        if (lpMaster is not null) {
            ReportProgress(
                context: progressContext,
                stage: SolverStages.ModelBuilding,
                subStage: new SolverSubStage(
                    Key: "lp_master_ready",
                    DefaultTemplate: "LP 主问题已就绪",
                    MessageKey: "i18n.ospf.substage.lp_master_ready"),
                progressInStage: 100,
                overallProgress: 20,
                diagnostics: new Dictionary<string, string> { ["model"] = problem.Products.FirstOrDefault()?.Id ?? "" });

            for (int iteration = 0; iteration < iterationLimitBound; iteration++) {
                // 每轮迭代开头检查取消。
                // Cancellation is checked at the start of each iteration.
                cancellationToken.ThrowIfCancellationRequested();
                progressContext?.ThrowIfCancelled();
                var iterationNumber = new Int64(iteration);
                int planCountBefore = currentPlans.Count;
                // 迭代区间 20—75，在这一段里按比例上报进度。
                // Iteration band 20—75, with progress reported proportionally across it.
                int iterationStart = RangedProgress(20, 75, iteration, iterationLimitBound);
                int iterationEnd = RangedProgress(20, 75, iteration + 1, iterationLimitBound);

                (Csp1dMilpSolver.LpResult<V>? Result, ErrorCode? ErrorCode) lpSolve = await SolveLpMasterAsync(
                    lpMaster,
                    iterationNumber,
                    // 迭代区间 20—75 由 `phase` 重映射到全局进度带上。
                    // The 20—75 band is remapped by `phase` onto the global progress band.
                    progressContext?.Phase(iterationStart, iterationEnd));
                Csp1dMilpSolver.LpResult<V>? lpResult = lpSolve.Result;

                // ===== 第一阶段：可行性回退 / Phase one: feasibility fallback =====
                // **从未解出可行解**、当前不是可行性阶段、且 LP 以「模型不可行」失败时，
                // 重建一个**只最小化人工短缺**的主问题再解一次。C# 原本**完全没有这个分支**——
                // 一旦 LP 不可行就直接终止，哪怕"加人工短缺就能解"。
                //
                // **注意判据里的 `!hasValidLpResult`**：可行性阶段只是**重建**，不能挽救已经正常跑起来的流程。
                //
                // When **no feasible solution has ever been found**, we are not already in the feasibility phase, and the LP failed with
                // "model infeasible", rebuild a master that **only minimises artificial deficits** and solve again. C# had **no such branch at all** — an infeasible
                // LP terminated immediately even when "an artificial deficit would make it solvable".
                //
                // **Note the `!hasValidLpResult` guard**: the feasibility phase only **rebuilds**; it must not rescue a run that already worked.
                // **必须确认错误码是「模型不可行」**：别的失败（例如求解器未找到）重建可行性主问题也没用，
                // 只会掩盖真正的错误。
                // **The error code must be "model infeasible"**: rebuilding a feasibility master for any other failure (say solver-not-found) is useless and only masks
                // the real problem.
                if (lpResult is null && lpSolve.ErrorCode == ErrorCode.ORModelInfeasible
                    && !hasValidLpResult && !feasibilityPhase) {
                    LpMaster? phaseOne = await BuildLpMasterAsync(
                        problem,
                        currentPlans,
                        resolvedConfig.AllExtensions,
                        lpDomainValueSample,
                        feasibilityPhase: true);
                    if (phaseOne is not null) {
                        lpMaster = phaseOne;
                        feasibilityPhase = true;
                        lpResult = (await SolveLpMasterAsync(
                            lpMaster,
                            iterationNumber,
                            progressContext?.Phase(iterationStart, iterationEnd))).Result;
                    }
                }

                // ===== 第二阶段：可行性达成后恢复原始主问题 / Phase two: restore the original master once feasible =====
                // 目标值 ≤ 1e-7（人工短缺归零）时，**丢弃人工短缺、重建原始硬约束主问题**并重新求解。
                // 不恢复的话，最终解会带着人工短缺——**看起来可行，实际不可行**。
                //
                // Once the objective is ≤ 1e-7 (the artificial deficit is zero), **discard the deficits, rebuild the original hard-constrained master**
                // and solve again. Without the restore the final solution carries artificial deficits — **apparently feasible, actually infeasible**.
                if (feasibilityPhase && lpResult is not null
                    && lpResult.LpOutput.Obj.ToDouble() <= 1e-7) {
                    LpMaster? restored = await BuildLpMasterAsync(
                        problem,
                        currentPlans,
                        resolvedConfig.AllExtensions,
                        lpDomainValueSample);
                    if (restored is not null) {
                        lpMaster = restored;
                        feasibilityPhase = false;
                        lpResult = (await SolveLpMasterAsync(
                            lpMaster,
                            iterationNumber,
                            progressContext?.Phase(iterationStart, iterationEnd))).Result;
                    }
                }

                if (lpResult is null) {
                    pricedPlanCounts.Add(UInt64.Zero);
                    iterationRecords.Add(new Csp1dIterationRecord(
                        iterationNumber, Flt64.Zero, new Int64(planCountBefore),
                        UInt64.Zero, new Int64(planCountBefore)));
                    terminationReason = hasValidLpResult
                        ? Csp1dTerminationReason.LpSolveFailed
                        : Csp1dTerminationReason.LpInfeasible;
                    lpFailureMessage = $"LP solve returned null at iteration {iteration}";
                    break;
                }

                hasValidLpResult = true;
                Flt64 lpObjective = lpResult.LpOutput.Obj;
                ShadowPriceMap<V> shadowPrices = lpResult.ShadowPrices;

                ReportProgress(
                    context: progressContext,
                    stage: SolverStages.ColumnGeneration,
                    subStage: PricingIterationSubStage(iterationNumber),
                    progressInStage: 40,
                    overallProgress: iterationStart + ((iterationEnd - iterationStart) * 40 / 100),
                    diagnostics: new Dictionary<string, string> {
                        ["iteration"] = iterationNumber.ToString(),
                        ["planCount"] = currentPlans.Count.ToString(),
                    });

                // **把配置里的定价策略接进来**：从 `resolvedConfig.extensionSet.pricingPolicies` 取三个回调，
                // 而 时这三个参数在**生产路径上恒为 null**——`ICsp1dPricingPolicy` 存在、能从 builder 配到
                // `SolveConfig`，但**从没到达定价器**。
                //
                // **Wire the config's pricing policies in**: the three callbacks come from
                // `resolvedConfig.extensionSet.pricingPolicies`, and as of they were **always null on the production path** —
                // `ICsp1dPricingPolicy` existed and flowed from the builder into `SolveConfig`, but **never reached the pricing generator**.
                IReadOnlyList<ICsp1dPricingPolicy<V>> pricingPolicies = resolvedConfig.ExtensionSet.PricingPolicies;
                // **从配置构造目标成本**：`pricingObjectiveConfig(resolvedConfig)` 取
                // `lengthConfig.batchMinPenalty` 与 `wasteConfig` 的三个惩罚项。
                // **不接的话 `ObjectiveConfig` 恒为 null，成本永远只有基础值 1**——
                // 与 修的是同一类缺陷（配置项存在、没到达消费者）。
                //
                // **The objective cost is built from config**: `pricingObjectiveConfig(resolvedConfig)` reads
                // `lengthConfig.batchMinPenalty` and the three waste penalties. **Without this, `ObjectiveConfig` is always null and
                // the cost is always the base 1** — the same defect class as (the knob exists but never reaches its consumer).
                var pricingObjectiveConfig = new Csp1dPricingObjectiveConfig<V>(
                    PlanUsagePenalty: resolvedConfig.LengthConfig?.BatchMinPenalty,
                    TrimWidthPenalty: resolvedConfig.WasteConfig?.TrimWidthPenalty,
                    RestMaterialPenalty: resolvedConfig.WasteConfig?.RestMaterialPenalty,
                    MaterialCostPenalty: resolvedConfig.WasteConfig?.MaterialCostPenalty);
                var pricingInput = new Csp1dPricingInput<V>(
                    GenerationInput: new GenerationInput<V> {
                        Materials = problem.Materials,
                        Demands = problem.Demands,
                        Costars = problem.Costars,
                        // **：把池里已有的方案传进来。**
                        //
                        // **曾判定"接上去比不接更糟"，那是错的。** 当时看到"接上后定价一条列都不产"就下了结论，
                        // 但验收测试（`Csp1dApplicationAcceptanceTest` :726）断言的是另一件事：
                        // **同样的场景下也断言 `AllDuplicates`**——第一轮全是重复列**是预期行为**，不是缺陷。
                        //
                        // 原因是定价用的枚举器与初始方案池**是同一个**（`SimpleInitialCuttingPlanGenerator`），
                        // 所以它会重新枚举出池里已有的方案，去重后自然为空。**这条行为是有意的。**
                        //
                        // **Pass the plans already in the pool.**
                        //
                        // **concluded "wiring it is worse than not wiring it", which was wrong.** That conclusion came from seeing "pricing produces no columns"
                        // the acceptance test (`Csp1dApplicationAcceptanceTest` :726) **also asserts `AllDuplicates`** in the same
                        // situation — an all-duplicate first round **is expected behaviour**, not a defect.
                        //
                        // The reason is that pricing and the initial pool **share the same enumerator** (`SimpleInitialCuttingPlanGenerator`), so it re-enumerates plans
                        // already in the pool and deduplication naturally empties the result. **That behaviour is deliberate.**
                        ExistingPlans = currentPlans,
                        DominanceAcceptOverrides = dominanceAcceptOverrides,
                        CandidateFilters = candidateFilters,
                    },
                    ShadowPrices: shadowPrices,
                    MaxGeneratedPlans: new UInt64((ulong)System.Math.Max(0L, columnConfig.MaxPricingPlans.ToLong())),
                    ObjectiveConfig: pricingObjectiveConfig,
                    BenefitModifiers: pricingPolicies.Count == 0
                        ? null
                        : pricingPolicies.Select(policy => (global::System.Func<CuttingPlan<V>, V, V>)policy.ModifyBenefit).ToList(),
                    CostModifiers: pricingPolicies.Count == 0
                        ? null
                        : pricingPolicies.Select(policy => (global::System.Func<CuttingPlan<V>, V, V>)policy.ModifyCost).ToList(),
                    ImprovingJudges: pricingPolicies.Count == 0
                        ? null
                        : pricingPolicies.Select(policy => (global::System.Func<CuttingPlan<V>, V, V, bool?>)policy.IsImproving).ToList());

                CuttingPlanGenerationReport<CuttingPlan<V>> pricingReport = _pricingGenerator.GenerateWithReport(pricingInput);
                pricingGenerationStatistics = MergeGenerationStatistics(pricingGenerationStatistics, pricingReport.Statistics);
                IReadOnlyList<CuttingPlan<V>> newPlans = pricingReport.Plans;

                if (newPlans.Count == 0) {
                    pricedPlanCounts.Add(UInt64.Zero);
                    iterationRecords.Add(new Csp1dIterationRecord(
                        iterationNumber, lpObjective, new Int64(planCountBefore),
                        UInt64.Zero, new Int64(planCountBefore)));
                    terminationReason = Csp1dTerminationReason.PricingConverged;
                    break;
                }

                var flowContext = new FlowContextImpl {
                    Iteration = iterationNumber.ToLong(),
                    CurrentPlans = currentPlans,
                    IterationLimit = iterationLimit.ToLong(),
                    AllowPartialSolution = resolvedConfig.AllowPartialSolution,
                    NewPlans = newPlans,
                    HasValidLpResult = hasValidLpResult,
                    PricingStatistics = pricingGenerationStatistics
                };

                List<CuttingPlan<V>> addedPlans = DeduplicatePlans(
                    currentPlans, newPlans, flowPolicies, flowContext, canonicalKeyOverrides);
                if (addedPlans.Count == 0) {
                    pricedPlanCounts.Add(UInt64.Zero);
                    iterationRecords.Add(new Csp1dIterationRecord(
                        iterationNumber, lpObjective, new Int64(planCountBefore),
                        UInt64.Zero, new Int64(planCountBefore)));
                    terminationReason = Csp1dTerminationReason.AllDuplicates;
                    break;
                }

                Result<IReadOnlyList<CuttingPlan<V>>, ErrorCode, Error<ErrorCode>> addColumnsResult = lpMaster.Context.AddColumns(
                    new UInt64((ulong)(iteration + 1)), addedPlans, lpMaster.Model);

                if (addColumnsResult.IsFailed || addColumnsResult.Value.Count == 0) {
                    pricedPlanCounts.Add(UInt64.Zero);
                    iterationRecords.Add(new Csp1dIterationRecord(
                        iterationNumber, lpObjective, new Int64(planCountBefore),
                        UInt64.Zero, new Int64(planCountBefore)));
                    terminationReason = Csp1dTerminationReason.AllDuplicates;
                    break;
                }

                IReadOnlyList<CuttingPlan<V>> addedColumns = addColumnsResult.Value;
                currentPlans.AddRange(addedColumns);
                pricedPlanCounts.Add(new UInt64((ulong)addedColumns.Count));
                iterationRecords.Add(new Csp1dIterationRecord(
                    iterationNumber, lpObjective, new Int64(planCountBefore),
                    new UInt64((ulong)addedColumns.Count), new Int64(currentPlans.Count)));

                if (iteration == iterationLimitBound - 1) {
                    terminationReason = Csp1dTerminationReason.IterationLimitReached;
                }

                if (iteration < iterationLimitBound - 1 && flowPolicies.Count > 0) {
                    var stopContext = new FlowContextImpl {
                        Iteration = iterationNumber.ToLong(),
                        CurrentPlans = currentPlans,
                        IterationLimit = iterationLimit.ToLong(),
                        AllowPartialSolution = resolvedConfig.AllowPartialSolution,
                        HasValidLpResult = hasValidLpResult,
                        PricingStatistics = pricingGenerationStatistics
                    };
                    if (Csp1dFlowPolicyHelpers.ShouldStopByPolicies(flowPolicies, stopContext)) {
                        terminationReason = Csp1dTerminationReason.PricingConverged;
                        break;
                    }
                }
            }
        }

        // 最终 MILP 拥有总体进度的 75—99 带。
        // The final MILP owns the 75—99 band.
        FinalMilpSolveResult finalMilp = await SolveFinalMilpAsync(
            problem, currentPlans, resolvedConfig, progressContext?.Phase(75, 99));
        Produce<V> produce = finalMilp.MilpResult?.Produce ?? EmptyProduce(problem);
        Csp1dSolution<V> baseSolution = _analyzer.Analyze(problem, produce, currentPlans);
        List<CuttingPlan<V>> topPlans = Csp1dSolutionEnrichmentHelper.TopCuttingPlans<V>(currentPlans, resolvedConfig.TopKPlanLimit);

        Csp1dSolutionStatus solutionStatus = finalMilp.Status switch {
            Csp1dFinalMilpStatus.Solved => Csp1dSolutionStatus.Feasible,
            Csp1dFinalMilpStatus.Failed => resolvedConfig.AllowPartialSolution
                ? Csp1dSolutionStatus.Partial
                : Csp1dSolutionStatus.Failed,
            _ => Csp1dSolutionStatus.Partial
        };

        string? combinedFailureMessage = (lpFailureMessage, finalMilp.FailureMessage) switch {
            (not null, not null) => $"{lpFailureMessage}; {finalMilp.FailureMessage}",
            (not null, null) => lpFailureMessage,
            _ => finalMilp.FailureMessage
        };

        Csp1dSolution<V> enrichedSolution = Csp1dSolutionEnrichmentHelper.EnrichSolution(
            solution: baseSolution with {
                YieldResult = (YieldModelingResult<V>?)(object?)finalMilp.MilpResult?.YieldResult,
                WasteResult = finalMilp.MilpResult?.WasteResult,
                LengthResult = (LengthAssignmentModelingResult<V>?)(object?)finalMilp.MilpResult?.LengthResult
            },
            topPlans: topPlans,
            status: solutionStatus,
            failureMessage: combinedFailureMessage,
            terminationReason: terminationReason,
            finalMilpStatus: finalMilp.Status,
            partialSolutionAvailable: finalMilp.Status == Csp1dFinalMilpStatus.Failed,
            initialGenerationStatistics: initialPlanPool.Statistics,
            pricingGenerationStatistics: pricingGenerationStatistics,
            lpFailureMessage: lpFailureMessage,
            iterationRecords: iterationRecords,
            extractionPolicies: resolvedConfig.ExtensionSet.ExtractionPolicies,
            demands: problem.Demands,
            materials: problem.Materials,
            machines: problem.Machines);

        // 收尾快照：没有它进度条会**停在 60**（记录的缺口）。
        // Closing snapshot: without it the progress bar **freezes at 60** (the gap recorded).
        ReportProgress(
            context: progressContext,
            stage: SolverStages.ColumnGeneration,
            subStage: PricingIterationSubStage(new Int64(global::System.Math.Max(0, iterationRecords.Count - 1))),
            progressInStage: 100,
            overallProgress: 99,
            diagnostics: new Dictionary<string, string> {
                ["iterations"] = iterationRecords.Count.ToString(),
                ["finalPlans"] = currentPlans.Count.ToString(),
            });

        return new Csp1dColumnGenerationResult(
            Solution: enrichedSolution,
            Trace: new Csp1dColumnGenerationTrace(
                InitialPlanCount: new UInt64((ulong)initialCount),
                FinalPlanCount: new UInt64((ulong)currentPlans.Count),
                PricedPlanCount: pricedPlanCounts,
                TerminationReason: terminationReason,
                Iterations: iterationRecords,
                InitialGenerationStatistics: initialPlanPool.Statistics,
                FinalMilpStatus: finalMilp.Status,
                PartialSolutionAvailable: finalMilp.Status == Csp1dFinalMilpStatus.Failed,
                FailureMessage: finalMilp.FailureMessage,
                PricingGenerationStatistics: pricingGenerationStatistics,
                LpFailureMessage: lpFailureMessage));
    }

    private async Task<InitialPlanPool> InitialPlanPoolAsync(
        Csp1dProblem<V> problem,
        Csp1dConfiguration<V> configuration,
        IReadOnlyList<ICsp1dDomainPolicy<V>> domainPolicies,
        Func<Material<V>, Product<V>, Quantity<V>, bool>? widthFeasibilityCheck,
        IReadOnlyList<ICsp1dFlowPolicy<V>> flowPolicies,
        IReadOnlyList<global::System.Func<CuttingPlan<V>, string?>> canonicalKeyOverrides,
        IReadOnlyList<global::System.Func<CuttingPlan<V>, IReadOnlyList<CuttingPlan<V>>, bool>> dominanceAcceptOverrides,
        IReadOnlyList<global::System.Func<CuttingPlan<V>, IReadOnlyList<CuttingPlan<V>>, bool>> candidateFilters) {
        if (configuration.MaxInitialPlans.ToLong() <= 0L) {
            return new InitialPlanPool(Array.Empty<CuttingPlan<V>>(), null);
        }

        var generationInput = new GenerationInput<V> {
            Materials = problem.Materials,
            Demands = problem.Demands,
            Costars = problem.Costars,
            DominanceAcceptOverrides = dominanceAcceptOverrides,
            CandidateFilters = candidateFilters,
        };

        CuttingPlanGenerationReport<CuttingPlan<V>> report = _initialGenerator.GenerateWithReport(generationInput);
        var generatedPlans = report.Plans
            .GroupBy(p => Csp1dGenerationStrategyProjection.Resolve(p, canonicalKeyOverrides))
            .Select(g => g.First())
            .Take((int)configuration.MaxInitialPlans.ToLong())
            .ToList();

        if (flowPolicies.Count > 0) {
            var flowContext = new FlowContextImpl {
                Iteration = Int64.Zero.ToLong(),
                CurrentPlans = generatedPlans,
                IterationLimit = configuration.IterationLimit.ToLong(),
                AllowPartialSolution = true
            };
            generatedPlans = Csp1dFlowPolicyHelpers.FilterInitialPlansByPolicies(flowPolicies, flowContext, generatedPlans).ToList();
        }

        return new InitialPlanPool(generatedPlans, report.Statistics);
    }

    private Csp1dColumnGenerationResult BuildNoInitialPlansResult(
        Csp1dProblem<V> problem,
        InitialPlanPool initialPlanPool,
        Csp1dSolveConfig<V> resolvedConfig,
        string? customMessage = null) {
        string failureMessage = customMessage ?? "No initial cutting plans generated";
        Produce<V> emptyProduce = EmptyProduce(problem);
        Csp1dSolution<V> baseSolution = _analyzer.Analyze(problem, emptyProduce, Array.Empty<CuttingPlan<V>>());
        Csp1dSolution<V> enriched = Csp1dSolutionEnrichmentHelper.EnrichSolution(
            solution: baseSolution,
            topPlans: Array.Empty<CuttingPlan<V>>(),
            status: Csp1dSolutionStatus.NoInitialPlans,
            failureMessage: failureMessage,
            terminationReason: Csp1dTerminationReason.NoInitialPlans,
            finalMilpStatus: Csp1dFinalMilpStatus.NotAttempted,
            partialSolutionAvailable: false,
            initialGenerationStatistics: initialPlanPool.Statistics,
            iterationRecords: Array.Empty<Csp1dIterationRecord>(),
            extractionPolicies: resolvedConfig.ExtensionSet.ExtractionPolicies,
            demands: problem.Demands,
            materials: problem.Materials,
            machines: problem.Machines);

        return new Csp1dColumnGenerationResult(
            Solution: enriched,
            Trace: new Csp1dColumnGenerationTrace(
                InitialPlanCount: UInt64.Zero,
                FinalPlanCount: UInt64.Zero,
                PricedPlanCount: Array.Empty<UInt64>(),
                TerminationReason: Csp1dTerminationReason.NoInitialPlans,
                Iterations: Array.Empty<Csp1dIterationRecord>(),
                InitialGenerationStatistics: initialPlanPool.Statistics,
                FinalMilpStatus: Csp1dFinalMilpStatus.NotAttempted,
                PartialSolutionAvailable: false,
                FailureMessage: failureMessage));
    }

    /// <summary>
    /// 构建 LP 主问题 / Build the LP master problem.
    /// </summary>
    /// <param name="problem">问题定义 / Problem definition.</param>
    /// <param name="cuttingPlans">当前方案列表 / Current cutting plans.</param>
    /// <param name="extensions">建模扩展 / Modeling extensions.</param>
    /// <param name="domainValueSample">领域数值样本 / Domain value sample.</param>
    /// <param name="feasibilityPhase">
    /// 是否为"只最小化人工短缺"的可行性阶段 / Whether this is the "minimise artificial deficits only" feasibility phase.
    /// </param>
    /// <returns>LP 主问题；注册失败时返回 null / The LP master; null when registration fails.</returns>
    /// <remarks>
    /// **可行性阶段额外做两件事**（如此 `buildLpMaster(..., feasibilityPhase = true)`）：
    ///
    /// 1. `context.FeasibilityPhase` —— 让 LP 模式下**也构建 yield 聚合**（`_mode == MILP || _feasibilityPhase`）。
    /// 2. 用一个**只惩罚欠产、超产惩罚为零**的 `YieldModelingConfig`——这样目标函数就退化成
    ///    "最小化人工需求短缺"，解出来的就是**让硬约束可以被满足的最小短缺量**。
    ///
    /// **欠产惩罚用的是 `domainValueSample.constants.one`**（单位 1），超产是 `zero`。
    /// 这不是随手取的值：系数必须**严格为正**才能让人工短缺进入目标；
    /// 超产为 `zero` 是让**多产不获奖励**，否则求解器会用超产去抵欠产，**短缺量就失真了**。
    ///
    /// **The feasibility phase does two extra things**: the two steps above. The
    /// under-production penalty of **one** and over-production of **zero** are not arbitrary: the coefficient must be **strictly positive** for the artificial deficit to
    /// enter the objective, and over-production must be **zero** so the solver cannot offset deficits with surplus and **distort the deficit amount**.
    /// </remarks>
    private async Task<LpMaster?> BuildLpMasterAsync(
        Csp1dProblem<V> problem,
        IReadOnlyList<CuttingPlan<V>> cuttingPlans,
        IReadOnlyList<Csp1dModelingExtension<V>> extensions,
        V domainValueSample,
        bool feasibilityPhase = false) {
        var model = new LinearMetaModel<Flt64>(name: "csp1d_produce_lp");
        var input = new ProduceInput<V>(
            cuttingPlans: cuttingPlans,
            demands: problem.Demands,
            materials: problem.Materials,
            machines: problem.Machines);

        Csp1dProduceContextBuilder<V> builder = new Csp1dProduceContextBuilder<V>(input)
            .Mode(Csp1dModelingMode.LP)
            .Extensions(extensions);
        if (feasibilityPhase) {
            builder = builder.FeasibilityPhase();
            builder = builder.YieldConfig(BuildFeasibilityYieldConfig(problem, domainValueSample));
        }
        Csp1dProduceContext<V> context = builder.Build();

        Result<Success, ErrorCode, Error<ErrorCode>> registerResult = context.Register(model);
        if (registerResult.IsFailed) {
            return null;
        }

        return new LpMaster(model, context, domainValueSample);
    }

    /// <summary>
    /// 构建可行性阶段的人工短缺惩罚配置 / Build the artificial-deficit penalty config for the feasibility phase.
    /// </summary>
    /// <param name="problem">问题定义 / Problem definition.</param>
    /// <param name="domainValueSample">领域数值样本 / Domain value sample.</param>
    /// <returns>yield 建模配置 / The yield modeling config.</returns>
    /// <remarks>
    /// 每条需求一个键：`ProductDemandShadowPriceKey(产品 id, 单位键)`，单位键走唯一实现
    /// `GenerationQuantityMath.UnitKey`——**与影子价格的键必须一致**，否则这里建的惩罚和
    /// 后续读的对偶价格**对不上**（修过同形的分叉）。
    ///
    /// One key per demand, with the unit key from the single implementation `GenerationQuantityMath.UnitKey` — **it must match the shadow-price key**, or these penalties
    /// and the duals read later **will not line up** (the same divergence fixed).
    /// </remarks>
    private static YieldModelingConfig<V> BuildFeasibilityYieldConfig(
        Csp1dProblem<V> problem,
        V domainValueSample) {
        var underPenalty = new Dictionary<ProductDemandShadowPriceKey, V>();
        var overPenalty = new Dictionary<ProductDemandShadowPriceKey, V>();
        // `domainValueSample` 只在这里用作"能否转换"的样本；实际值取 solver 侧的 1 与 0。
        // **`Flt64` 是唯一受支持的领域数值类型**，其它类型直接跳过该项（与定价器里的早退一致）。
        // `domainValueSample` is only the sample used to check convertibility; the actual values are solver-side 1 and 0.
        // **`Flt64` is the only supported domain numeric type**; others skip this term (matching the early exits in the pricing generator).
        if (typeof(V) != typeof(Flt64)) {
            return new YieldModelingConfig<V>();
        }
        var one = (V)(object)new Flt64(1.0);
        var zero = (V)(object)Flt64.Zero;
        foreach (ProductDemand<V> demand in problem.Demands) {
            var key = new ProductDemandShadowPriceKey(
                demand.Product.Id,
                global::Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Service.GenerationQuantityMath.UnitKey(demand.Quantity.Unit));
            underPenalty[key] = one;
            overPenalty[key] = zero;
        }
        return new YieldModelingConfig<V>(
            UnderProductionPenalty: underPenalty,
            OverProductionPenalty: overPenalty);
    }

    /// <summary>
    /// 求解 LP 主问题，并保留失败的错误码 / Solve the LP master, keeping the failure's error code.
    /// </summary>
    /// <param name="master">LP 主问题 / LP master.</param>
    /// <param name="iteration">迭代号 / Iteration number.</param>
    /// <param name="progressContext">进度上下文 / Progress context.</param>
    /// <returns>
    /// 求解结果；失败时 <c>Result</c> 为 null 且 <c>ErrorCode</c> 带出具体原因 /
    /// The solve result; on failure <c>Result</c> is null and <c>ErrorCode</c> carries the reason.
    /// </returns>
    /// <remarks>
    /// **为什么必须保留错误码**：两阶段可行性只在**「模型不可行」这个具体错误码**上才触发。
    /// 原实现失败时直接 `return null`，**把错误码丢掉了**——所以即便调用方想区分"不可行"与"别的失败"，
    /// **也拿不到依据**。
    ///
    /// **Why the error code must be kept**: the two-phase feasibility only triggers on **the specific "model infeasible" code**. The original returned `null` on any
 /// failure, **discarding the implementation** — so even a caller wanting to tell "infeasible" from "some other failure" **had no basis**.
    /// </remarks>
    private async Task<(Csp1dMilpSolver.LpResult<V>? Result, ErrorCode? ErrorCode)> SolveLpMasterAsync(
        LpMaster master,
        Int64 iteration,
        SolverProgressContext? progressContext) {
        // 走带 options 的重载，把进度上下文传进框架层——`phase(...)` 的子上下文由此到达 `solver.solveLP`。
        // Use the options overload so the progress context reaches the framework layer — `phase(...)`'s sub-context then reaches `solver.solveLP`.
        Result<IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>> lpSolveResult = await _solver.SolveLPAsync(
            master.Model,
            new FrameworkSolveOptions {
                Name = $"csp1d-produce-lp-{iteration}",
                ProgressContext = progressContext,
            });
        if (lpSolveResult.IsFailed) {
            return (null, lpSolveResult is Failed<IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>> failed
                ? failed.Error.Code
                : null);
        }

        try {
            // ****：`Csp1dShadowPriceLifecycle<V>(domainValueSample, context.cgPipelines)`。
            // 不传 pipeline 时提取器是空转的，影子价格表永远为空，定价因此永远不产列。
            // **Lifecycle construction**: `Csp1dShadowPriceLifecycle<V>(domainValueSample, context.cgPipelines)`.
            // Without pipelines the extractor is a no-op, the shadow-price map stays empty, and pricing never emits a column.
            var lifecycle = new Csp1dShadowPriceLifecycle<V>(master.DomainValueSample, master.Context.CgPipelines);
            Result<ShadowPriceMap<V>, ErrorCode, Error<ErrorCode>> shadowPriceResult = lifecycle.ExtractFromDualSolution(master.Model, lpSolveResult.Value.DualSolution);
            if (shadowPriceResult.IsFailed) {
                return (null, null);
            }

            var solved = new Csp1dMilpSolver.LpResult<V>(
                ShadowPrices: shadowPriceResult.Value,
                Model: master.Model,
                LpOutput: lpSolveResult.Value,
                FrameworkShadowPriceMap: lifecycle.FrameworkShadowPriceMap);
            return (solved, null);
        }
        catch {
            return (null, null);
        }
    }

    private async Task<FinalMilpSolveResult> SolveFinalMilpAsync(
        Csp1dProblem<V> problem,
        IReadOnlyList<CuttingPlan<V>> cuttingPlans,
        Csp1dSolveConfig<V> solveConfig,
        SolverProgressContext? progressContext) {
        Result<Csp1dMilpSolver.MilpResult<V>?, ErrorCode, Error<ErrorCode>> solveResult = await new Csp1dMilpSolver(_solver).SolveAsync(
            input: new ProduceInput<V>(
                CuttingPlans: cuttingPlans,
                Demands: problem.Demands,
                Materials: problem.Materials,
                Machines: problem.Machines,
                WarmStartPlanUsages: _warmStartPlanUsages),
            yieldConfig: solveConfig.YieldConfig,
            wasteConfig: solveConfig.WasteConfig,
            lengthConfig: solveConfig.LengthConfig,
            extensions: solveConfig.AllExtensions,
            objectivePolicies: solveConfig.ExtensionSet.ObjectivePolicies,
            isFinalMilp: true,
            progressContext: progressContext);

        if (solveResult.IsFailed) {
            string message = solveResult is Fatal<Csp1dMilpSolver.MilpResult<V>?, ErrorCode, Error<ErrorCode>> fatal
                ? string.Join(", ", fatal.Errors.Select(e => e.Message))
                : ((Failed<Csp1dMilpSolver.MilpResult<V>?, ErrorCode, Error<ErrorCode>>)solveResult).Error.Message;
            return new FinalMilpSolveResult(Csp1dFinalMilpStatus.Failed, null, message);
        }

        Csp1dMilpSolver.MilpResult<V>? result = solveResult.Value;
        if (result is not null) {
            return new FinalMilpSolveResult(Csp1dFinalMilpStatus.Solved, result, null);
        }

        return new FinalMilpSolveResult(Csp1dFinalMilpStatus.Failed, null, "Final MILP returned no solution");
    }

    private Csp1dSolveConfig<V> ResolveSolveConfig(Csp1dProblem<V> problem, Csp1dSolveConfig<V>? solveConfig) {
        Csp1dSolveConfig<V> baseConfig = solveConfig ?? problem.SolveConfig ?? new Csp1dSolveConfig<V> { ColumnGeneration = problem.Configuration };
        return baseConfig with {
            YieldConfig = baseConfig.YieldConfig ?? _yieldConfig,
            WasteConfig = baseConfig.WasteConfig ?? _wasteConfig,
            LengthConfig = baseConfig.LengthConfig ?? _lengthConfig
        };
    }

    private Produce<V> EmptyProduce(Csp1dProblem<V> problem) => new(
        CuttingPlans: Array.Empty<CuttingPlanUsage<V>>(),
        MaterialUsages: Array.Empty<MaterialUsage<V>>(),
        MachineUsages: Array.Empty<MachineCapacityUsage<V>>(),
        UnmetDemands: problem.Demands);

    private static List<CuttingPlan<V>> DeduplicatePlans(
        IReadOnlyList<CuttingPlan<V>> existing,
        IReadOnlyList<CuttingPlan<V>> candidates,
        IReadOnlyList<ICsp1dFlowPolicy<V>> flowPolicies,
        ICsp1dFlowContext<V> flowContext,
        IReadOnlyList<global::System.Func<CuttingPlan<V>, string?>> canonicalKeyOverrides) {
        var existingKeys = new HashSet<CuttingPlanCanonicalKey>(
            existing.Select(p => Csp1dGenerationStrategyProjection.Resolve(p, canonicalKeyOverrides)));
        return candidates.Where(candidate => {
            CuttingPlanCanonicalKey key = Csp1dGenerationStrategyProjection.Resolve(candidate, canonicalKeyOverrides);
            if (existingKeys.Contains(key)) {
                return false;
            }

            if (flowPolicies.Count > 0) {
                return !existing.Any(ep => Csp1dFlowPolicyHelpers.IsEquivalentByPolicies(flowPolicies, flowContext, ep, candidate));
            }
            return true;
        }).ToList();
    }

    private static CuttingPlanGenerationStatistics? MergeGenerationStatistics(
        CuttingPlanGenerationStatistics? left,
        CuttingPlanGenerationStatistics? right) {
        if (left is null) {
            return right;
        }

        if (right is null) {
            return left;
        }

        return new CuttingPlanGenerationStatistics(
            VisitedNodes: left.VisitedNodes + right.VisitedNodes,
            GeneratedCandidates: left.GeneratedCandidates + right.GeneratedCandidates,
            AcceptedPlans: left.AcceptedPlans + right.AcceptedPlans,
            InfeasibleCandidates: left.InfeasibleCandidates + right.InfeasibleCandidates,
            DuplicateCandidates: left.DuplicateCandidates + right.DuplicateCandidates,
            DominatedCandidates: left.DominatedCandidates + right.DominatedCandidates,
            WidthBoundPrunedNodes: left.WidthBoundPrunedNodes + right.WidthBoundPrunedNodes,
            KnifeBoundPrunedNodes: left.KnifeBoundPrunedNodes + right.KnifeBoundPrunedNodes,
            LengthBoundPrunedEntries: left.LengthBoundPrunedEntries + right.LengthBoundPrunedEntries,
            MaterialWidthIndexCacheHits: left.MaterialWidthIndexCacheHits + right.MaterialWidthIndexCacheHits,
            MaterialSliceTemplateCacheHits: left.MaterialSliceTemplateCacheHits + right.MaterialSliceTemplateCacheHits,
            QuantityCacheHits: left.QuantityCacheHits + right.QuantityCacheHits,
            QuantityCacheMisses: left.QuantityCacheMisses + right.QuantityCacheMisses,
            MaterialSliceTemplateCacheMisses: left.MaterialSliceTemplateCacheMisses + right.MaterialSliceTemplateCacheMisses,
            CrossWorkerDuplicateCandidates: left.CrossWorkerDuplicateCandidates + right.CrossWorkerDuplicateCandidates,
            CrossContributionDominated: left.CrossContributionDominated + right.CrossContributionDominated,
            ElapsedMilliseconds: left.ElapsedMilliseconds + right.ElapsedMilliseconds,
            StopReason: right.StopReason);
    }

    /// <summary>
    /// 定价迭代子阶段 / The pricing-iteration sub-stage.
    /// </summary>
    /// <param name="iterationNumber">迭代号 / Iteration number.</param>
    /// <returns>子阶段 / The sub-stage.</returns>
    private static SolverSubStage PricingIterationSubStage(Int64 iterationNumber) =>
        new(
            Key: "pricing_iteration",
            DefaultTemplate: "定价迭代 {iter}",
            MessageKey: "i18n.ospf.substage.pricing_iteration",
            Args: new Dictionary<string, string> { ["iter"] = iterationNumber.ToString() });

    /// <summary>
    /// 上报进度 / Report progress.
    /// </summary>
    /// <param name="context">进度上下文 / Progress context.</param>
    /// <param name="stage">阶段 / Stage.</param>
    /// <param name="subStage">子阶段 / Sub-stage.</param>
    /// <param name="progressInStage">阶段内进度 / In-stage progress.</param>
    /// <param name="overallProgress">总体进度 / Overall progress.</param>
    /// <param name="diagnostics">诊断信息 / Diagnostics.</param>
    private static void ReportProgress(
        SolverProgressContext? context,
        SolverStage stage,
        SolverSubStage subStage,
        int progressInStage,
        int overallProgress,
        Dictionary<string, string> diagnostics) {
        context?.Report(new SolverProgressSnapshot(
            Stage: stage,
            ProgressInStage: progressInStage,
            OverallProgress: overallProgress,
            SubStage: subStage,
            Diagnostics: diagnostics));
    }

    /// <summary>
    /// 按比例计算区间内进度 / Compute ranged progress.
    /// </summary>
    /// <param name="start">区间起点 / Range start.</param>
    /// <param name="end">区间终点 / Range end.</param>
    /// <param name="index">当前序号 / Current index.</param>
    /// <param name="total">总数 / Total.</param>
    /// <returns>区间内进度 / Progress within the range.</returns>
    /// <remarks>
    /// **`total` 不大于 0 时返回 `end`**——总数为零意味着"已经走完"，
    /// 而不是"进度为零"。**区间夹取用 `end - start`**，防止 `index` 超出 `total` 时越界。
    ///
    /// **Returns `end` when `total` is not positive** — a zero total means "already done", not "zero progress". The clamp uses
    /// `end - start` so an `index` beyond `total` cannot overshoot.
    /// </remarks>
    private static int RangedProgress(int start, int end, int index, int total) {
        if (total <= 0) {
            return end;
        }
        return start + global::System.Math.Clamp((end - start) * index / total, 0, end - start);
    }
}
