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
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
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
    /// <returns>CSP1D 解 / CSP1D solution.</returns>
    public async Task<Csp1dSolution<V>> SolveAsync(
        Csp1dProblem<V> problem,
        Csp1dSolveConfig<V>? solveConfig = null) => (await SolveWithTraceAsync(problem, solveConfig)).Solution;

    /// <summary>
    /// 带追踪信息的列生成求解 / Column generation solve with trace.
    /// </summary>
    /// <param name="problem">问题定义 / Problem definition.</param>
    /// <param name="solveConfig">显式求解配置 / Explicit solve configuration.</param>
    /// <returns>列生成结果（含追踪信息）/ Column generation result with trace.</returns>
    public async Task<Csp1dColumnGenerationResult> SolveWithTraceAsync(
        Csp1dProblem<V> problem,
        Csp1dSolveConfig<V>? solveConfig = null) {
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

        InitialPlanPool initialPlanPool = await InitialPlanPoolAsync(problem, columnConfig, domainPolicies, widthCheck, resolvedConfig.ExtensionSet.FlowPolicies);
        IReadOnlyList<CuttingPlan<V>> initialPlans = initialPlanPool.Plans;
        int initialCount = initialPlans.Count;

        if (initialPlans.Count == 0) {
            return BuildNoInitialPlansResult(problem, initialPlanPool, resolvedConfig);
        }

        var currentPlans = initialPlans.ToList();
        var pricedPlanCounts = new List<UInt64>();
        var iterationRecords = new List<Csp1dIterationRecord>();
        Csp1dTerminationReason terminationReason = Csp1dTerminationReason.PricingConverged;
        bool hasValidLpResult = false;
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
            for (int iteration = 0; iteration < iterationLimitBound; iteration++) {
                var iterationNumber = new Int64(iteration);
                int planCountBefore = currentPlans.Count;

                Csp1dMilpSolver.LpResult<V>? lpResult = await SolveLpMasterAsync(lpMaster, iterationNumber);
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

                var pricingInput = new Csp1dPricingInput<V>(
                    GenerationInput: new GenerationInput<V> {
                        Materials = problem.Materials,
                        Demands = problem.Demands,
                        Costars = problem.Costars
                    },
                    ShadowPrices: shadowPrices,
                    MaxGeneratedPlans: new UInt64((ulong)System.Math.Max(0L, columnConfig.MaxPricingPlans.ToLong())));

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

                List<CuttingPlan<V>> addedPlans = DeduplicatePlans(currentPlans, newPlans, flowPolicies, flowContext);
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

        FinalMilpSolveResult finalMilp = await SolveFinalMilpAsync(problem, currentPlans, resolvedConfig);
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
        IReadOnlyList<ICsp1dFlowPolicy<V>> flowPolicies) {
        if (configuration.MaxInitialPlans.ToLong() <= 0L) {
            return new InitialPlanPool(Array.Empty<CuttingPlan<V>>(), null);
        }

        var generationInput = new GenerationInput<V> {
            Materials = problem.Materials,
            Demands = problem.Demands,
            Costars = problem.Costars
        };

        CuttingPlanGenerationReport<CuttingPlan<V>> report = _initialGenerator.GenerateWithReport(generationInput);
        var generatedPlans = report.Plans
            .GroupBy(p => CuttingPlanCanonicalKey.From(p))
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

    private async Task<LpMaster?> BuildLpMasterAsync(
        Csp1dProblem<V> problem,
        IReadOnlyList<CuttingPlan<V>> cuttingPlans,
        IReadOnlyList<Csp1dModelingExtension<V>> extensions,
        V domainValueSample) {
        var model = new LinearMetaModel<Flt64>(name: "csp1d_produce_lp");
        var input = new ProduceInput<V>(
            cuttingPlans: cuttingPlans,
            demands: problem.Demands,
            materials: problem.Materials,
            machines: problem.Machines);

        Csp1dProduceContext<V> context = new Csp1dProduceContextBuilder<V>(input)
            .Mode(Csp1dModelingMode.LP)
            .Extensions(extensions)
            .Build();

        Result<Success, ErrorCode, Error<ErrorCode>> registerResult = context.Register(model);
        if (registerResult.IsFailed) {
            return null;
        }

        return new LpMaster(model, context, domainValueSample);
    }

    private async Task<Csp1dMilpSolver.LpResult<V>?> SolveLpMasterAsync(LpMaster master, Int64 iteration) {
        Result<IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>> lpSolveResult = await _solver.SolveLPAsync($"csp1d-produce-lp-{iteration}", master.Model);
        if (lpSolveResult.IsFailed) {
            return null;
        }

        try {
            var lifecycle = new Csp1dShadowPriceLifecycle<V>(master.DomainValueSample);
            Result<ShadowPriceMap<V>, ErrorCode, Error<ErrorCode>> shadowPriceResult = lifecycle.ExtractFromDualSolution(master.Model, lpSolveResult.Value.DualSolution);
            if (shadowPriceResult.IsFailed) {
                return null;
            }

            return new Csp1dMilpSolver.LpResult<V>(
                ShadowPrices: shadowPriceResult.Value,
                Model: master.Model,
                LpOutput: lpSolveResult.Value,
                FrameworkShadowPriceMap: lifecycle.FrameworkShadowPriceMap);
        }
        catch {
            return null;
        }
    }

    private async Task<FinalMilpSolveResult> SolveFinalMilpAsync(
        Csp1dProblem<V> problem,
        IReadOnlyList<CuttingPlan<V>> cuttingPlans,
        Csp1dSolveConfig<V> solveConfig) {
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
            isFinalMilp: true);

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
        ICsp1dFlowContext<V> flowContext) {
        var existingKeys = new HashSet<CuttingPlanCanonicalKey>(existing.Select(p => CuttingPlanCanonicalKey.From(p)));
        return candidates.Where(candidate => {
            var key = CuttingPlanCanonicalKey.From(candidate);
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
}
