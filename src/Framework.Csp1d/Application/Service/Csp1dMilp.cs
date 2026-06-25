#nullable enable

using Fuookami.Ospf.Framework.Csp1d.Application.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.LengthAssignment.Model;
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

namespace Fuookami.Ospf.Framework.Csp1d.Application.Service;
/// <summary>
/// CSP1D 普通 MILP 求解入口 / CSP1D plain MILP solve entry.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public sealed class Csp1dMilp<V> where V : struct {
    private readonly IColumnGenerationSolver _solver;
    private readonly ICsp1dInitialCuttingPlanGenerator<CuttingPlan<V>, GenerationInput<V>> _initialGenerator;
    private readonly ICsp1dSolutionAnalyzer<V> _analyzer;
    private readonly YieldModelingConfig<V>? _yieldConfig;
    private readonly WasteMinimizationConfig<V>? _wasteConfig;
    private readonly LengthAssignmentModelingConfig<V>? _lengthConfig;
    private readonly IReadOnlyList<CuttingPlanUsage<V>> _warmStartPlanUsages;

    /// <summary>
    /// 构造 CSP1D MILP 求解器 / Construct CSP1D MILP solver.
    /// </summary>
    /// <param name="solver">列生成求解器 / Column generation solver.</param>
    /// <param name="initialGenerator">初始方案生成器 / Initial plan generator.</param>
    /// <param name="analyzer">解分析器 / Solution analyzer.</param>
    /// <param name="yieldConfig">yield 建模配置 / Yield modeling config.</param>
    /// <param name="wasteConfig">waste 建模配置 / Waste modeling config.</param>
    /// <param name="lengthConfig">length 建模配置 / Length modeling config.</param>
    /// <param name="warmStartPlanUsages">热启动方案使用量 / Warm start plan usages.</param>
    public Csp1dMilp(
        IColumnGenerationSolver solver,
        ICsp1dInitialCuttingPlanGenerator<CuttingPlan<V>, GenerationInput<V>>? initialGenerator = null,
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
    }

    /// <summary>
    /// 生成初始方案并求解最终 MILP / Generate initial plans and solve final MILP.
    /// </summary>
    /// <param name="problem">问题定义 / Problem definition.</param>
    /// <param name="solveConfig">显式求解配置，优先级高于 problem.SolveConfig / Explicit solve config, higher priority than problem.SolveConfig.</param>
    /// <returns>CSP1D 解 / CSP1D solution.</returns>
    public async Task<Csp1dSolution<V>> SolveAsync(
        Csp1dProblem<V> problem,
        Csp1dSolveConfig<V>? solveConfig = null) {
        Csp1dSolveConfig<V> resolvedConfig = ResolveSolveConfig(problem, solveConfig);
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

        IReadOnlyList<CuttingPlan<V>> generatedPlans = await InitialPlansAsync(
            problem,
            resolvedConfig.ColumnGeneration,
            domainPolicies,
            resolvedConfig.ExtensionSet.FlowPolicies,
            widthCheck);

        if (generatedPlans.Count == 0) {
            string failureMessage = "No initial cutting plans generated";
            Csp1dSolution<V> baseSolution = _analyzer.Analyze(
                problem,
                EmptyProduce(problem),
                Array.Empty<CuttingPlan<V>>());
            return Csp1dSolutionEnrichmentHelper.EnrichSolution(
                solution: baseSolution,
                topPlans: Array.Empty<CuttingPlan<V>>(),
                status: Csp1dSolutionStatus.NoInitialPlans,
                failureMessage: failureMessage,
                finalMilpStatus: Csp1dFinalMilpStatus.NotAttempted,
                partialSolutionAvailable: false,
                extractionPolicies: resolvedConfig.ExtensionSet.ExtractionPolicies,
                demands: problem.Demands,
                materials: problem.Materials,
                machines: problem.Machines);
        }

        MilpSolveResult milpResult = await SolveMilpAsync(
            problem,
            generatedPlans,
            resolvedConfig,
            isFinalMilp: false);

        Produce<V> produce = milpResult.Result?.Produce ?? EmptyProduce(problem);
        List<CuttingPlan<V>> topPlans = Csp1dSolutionEnrichmentHelper.TopCuttingPlans(
            generatedPlans,
            resolvedConfig.TopKPlanLimit);

        Csp1dSolutionStatus solutionStatus = milpResult.Status switch {
            Csp1dFinalMilpStatus.Solved => Csp1dSolutionStatus.Feasible,
            Csp1dFinalMilpStatus.Failed => resolvedConfig.AllowPartialSolution
                ? Csp1dSolutionStatus.Partial
                : Csp1dSolutionStatus.Failed,
            Csp1dFinalMilpStatus.NotAttempted => Csp1dSolutionStatus.Partial,
            _ => Csp1dSolutionStatus.Failed
        };

        Csp1dSolution<V> baseSolution2 = _analyzer.Analyze(problem, produce, generatedPlans) with {
            WasteResult = milpResult.Result?.WasteResult,
        };

        return Csp1dSolutionEnrichmentHelper.EnrichSolution(
            solution: baseSolution2,
            topPlans: topPlans,
            status: solutionStatus,
            failureMessage: milpResult.FailureMessage,
            finalMilpStatus: milpResult.Status,
            partialSolutionAvailable: milpResult.Status == Csp1dFinalMilpStatus.Failed,
            extractionPolicies: resolvedConfig.ExtensionSet.ExtractionPolicies,
            demands: problem.Demands,
            materials: problem.Materials,
            machines: problem.Machines);
    }

    /// <summary>
    /// 生成初始切割方案 / Generate initial cutting plans.
    /// </summary>
    private Task<IReadOnlyList<CuttingPlan<V>>> InitialPlansAsync(
        Csp1dProblem<V> problem,
        Csp1dConfiguration<V> configuration,
        IReadOnlyList<ICsp1dDomainPolicy<V>> domainPolicies,
        IReadOnlyList<ICsp1dFlowPolicy<V>> flowPolicies,
        Func<Material<V>, Product<V>, Quantity<V>, bool>? widthFeasibilityCheck) {
        if (configuration.MaxInitialPlans <= Int64.Zero) {
            return Task.FromResult<IReadOnlyList<CuttingPlan<V>>>(Array.Empty<CuttingPlan<V>>());
        }

        var generationInput = new GenerationInput<V> {
            Materials = problem.Materials,
            Demands = problem.Demands,
            Costars = problem.Costars
        };

        CuttingPlanGenerationReport<CuttingPlan<V>> report = _initialGenerator.GenerateWithReport(generationInput);

        // Deduplicate by canonical key
        var generatedPlans = report.Plans
            .DistinctByCanonicalKey(plan => CuttingPlanCanonicalKey.From(plan))
            .Take((int)configuration.MaxInitialPlans.ToLong())
            .ToList();

        // Apply flow policy initial plan filter
        IReadOnlyList<CuttingPlan<V>> result;
        if (flowPolicies.Count > 0) {
            var flowContext = new Csp1dMilpFlowContext<V>(
                iteration: 0L,
                currentPlans: generatedPlans,
                iterationLimit: configuration.IterationLimit.ToLong(),
                allowPartialSolution: true);
            result = Csp1dFlowPolicyHelpers.FilterInitialPlansByPolicies(flowPolicies, flowContext, generatedPlans);
        }
        else {
            result = generatedPlans;
        }

        return Task.FromResult(result);
    }

    /// <summary>
    /// 解析求解配置 / Resolve solve configuration.
    /// </summary>
    private Csp1dSolveConfig<V> ResolveSolveConfig(
        Csp1dProblem<V> problem,
        Csp1dSolveConfig<V>? solveConfig) {
        Csp1dSolveConfig<V> baseConfig = solveConfig ?? problem.SolveConfig ?? new Csp1dSolveConfig<V> {
            ColumnGeneration = problem.Configuration
        };
        return baseConfig with {
            YieldConfig = baseConfig.YieldConfig ?? _yieldConfig,
            WasteConfig = baseConfig.WasteConfig ?? _wasteConfig,
            LengthConfig = baseConfig.LengthConfig ?? _lengthConfig
        };
    }

    /// <summary>
    /// 求解 MILP / Solve MILP.
    /// </summary>
    private async Task<MilpSolveResult> SolveMilpAsync(
        Csp1dProblem<V> problem,
        IReadOnlyList<CuttingPlan<V>> cuttingPlans,
        Csp1dSolveConfig<V> config,
        bool isFinalMilp = false) {
        var input = new ProduceInput<V>(
            CuttingPlans: cuttingPlans,
            Demands: problem.Demands,
            Materials: problem.Materials,
            Machines: problem.Machines,
            WarmStartPlanUsages: _warmStartPlanUsages);

        Result<Csp1dMilpSolver.MilpResult<V>?, ErrorCode, Error<ErrorCode>> solveResult = await new Csp1dMilpSolver(_solver).SolveAsync(
            input: input,
            yieldConfig: config.YieldConfig,
            wasteConfig: config.WasteConfig,
            lengthConfig: config.LengthConfig,
            extensions: config.AllExtensions,
            objectivePolicies: config.ExtensionSet.ObjectivePolicies,
            isFinalMilp: isFinalMilp);

        if (solveResult is Ok<Csp1dMilpSolver.MilpResult<V>?, ErrorCode, Error<ErrorCode>> ok) {
            if (ok.Value is not null) {
                return new MilpSolveResult(
                    Status: Csp1dFinalMilpStatus.Solved,
                    Result: ok.Value,
                    FailureMessage: null);
            }
            return new MilpSolveResult(
                Status: Csp1dFinalMilpStatus.Failed,
                Result: null,
                FailureMessage: "MILP returned no solution");
        }

        if (solveResult is Failed<Csp1dMilpSolver.MilpResult<V>?, ErrorCode, Error<ErrorCode>> failed) {
            return new MilpSolveResult(
                Status: Csp1dFinalMilpStatus.Failed,
                Result: null,
                FailureMessage: failed.Error.Message);
        }

        if (solveResult is Fatal<Csp1dMilpSolver.MilpResult<V>?, ErrorCode, Error<ErrorCode>> fatal) {
            return new MilpSolveResult(
                Status: Csp1dFinalMilpStatus.Failed,
                Result: null,
                FailureMessage: string.Join("; ", fatal.Errors.Select(e => e.Message)));
        }

        return new MilpSolveResult(
            Status: Csp1dFinalMilpStatus.Failed,
            Result: null,
            FailureMessage: "MILP returned no solution");
    }

    /// <summary>
    /// 创建空产出 / Create empty produce.
    /// </summary>
    private static Produce<V> EmptyProduce(Csp1dProblem<V> problem) {
        return new Produce<V>(
            CuttingPlans: Array.Empty<CuttingPlanUsage<V>>(),
            MaterialUsages: Array.Empty<MaterialUsage<V>>(),
            MachineUsages: Array.Empty<MachineCapacityUsage<V>>(),
            UnmetDemands: problem.Demands);
    }

    /// <summary>
    /// MILP 求解结果 / MILP solve result.
    /// </summary>
    private sealed record MilpSolveResult(
        Csp1dFinalMilpStatus Status,
        Csp1dMilpSolver.MilpResult<V>? Result,
        string? FailureMessage
    );
}

/// <summary>
/// MILP 流程上下文实现 / MILP flow context implementation.
/// </summary>
internal sealed class Csp1dMilpFlowContext<V> : ICsp1dFlowContext<V> where V : struct {
    public Csp1dMilpFlowContext(
        long iteration,
        IReadOnlyList<CuttingPlan<V>> currentPlans,
        long iterationLimit,
        bool allowPartialSolution) {
        Iteration = iteration;
        CurrentPlans = currentPlans;
        IterationLimit = iterationLimit;
        AllowPartialSolution = allowPartialSolution;
    }

    /// <inheritdoc/>
    public long Iteration { get; }

    /// <inheritdoc/>
    public IReadOnlyList<CuttingPlan<V>> CurrentPlans { get; }

    /// <inheritdoc/>
    public long IterationLimit { get; }

    /// <inheritdoc/>
    public bool AllowPartialSolution { get; }
}

/// <summary>
/// 简单初始方案生成器 / Simple initial cutting plan generator.
///
/// 为每个物料+需求组合生成单切片方案。
/// Generates single-slice plans for each material + demand combination.
/// </summary>
/// <typeparam name="TV">数值类型 / Numeric value type.</typeparam>
public sealed class SimpleInitialGenerator<TV>
    : ICsp1dInitialCuttingPlanGenerator<CuttingPlan<TV>, GenerationInput<TV>>
    where TV : struct {
    /// <inheritdoc/>
    public IReadOnlyList<CuttingPlan<TV>> Generate(GenerationInput<TV> input) {
        var plans = new List<CuttingPlan<TV>>();
        foreach (Material<TV> material in input.Materials) {
            foreach (ProductDemand<TV> demand in input.Demands) {
                Quantity<TV>? width = demand.Product.Width.FirstOrDefault(material.WidthRange.CanCut);
                if (width is null) {
                    continue;
                }

                var plan = new CuttingPlan<TV>(
                    id: $"init-{material.Id}-{demand.Product.Id}-{plans.Count}",
                    material: material,
                    slices: new[] { new CuttingPlanSlice<TV>(demand.Product, width) },
                    demandContributions: new[] { new CuttingPlanDemandContribution<TV>(demand.Product, demand.Quantity) });
                if (material.Enabled(plan)) {
                    plans.Add(plan);
                }
            }
        }
        return plans;
    }
}

/// <summary>
/// Reduced cost 定价方案生成器 / Reduced cost pricing cutting plan generator.
///
/// 作为默认定价生成器，委托初始生成器生成候选方案。
/// Serves as the default pricing generator, delegating candidate generation to the initial generator.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public sealed class ReducedCostPricingGenerator<V>
    : ICsp1dPricingGenerator<CuttingPlan<V>, Csp1dPricingInput<V>>
    where V : struct {
    private readonly ICsp1dInitialCuttingPlanGenerator<CuttingPlan<V>, GenerationInput<V>> _initialGenerator;

    /// <summary>
    /// 构造 reduced cost 定价生成器 / Construct reduced cost pricing generator.
    /// </summary>
    /// <param name="initialGenerator">初始方案生成器 / Initial cutting plan generator.</param>
    public ReducedCostPricingGenerator(
        ICsp1dInitialCuttingPlanGenerator<CuttingPlan<V>, GenerationInput<V>> initialGenerator) {
        _initialGenerator = initialGenerator;
    }

    /// <inheritdoc/>
    public IReadOnlyList<CuttingPlan<V>> Generate(Csp1dPricingInput<V> input) => _initialGenerator.Generate(input.GenerationInput);
}
