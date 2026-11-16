#nullable enable

using Fuookami.Ospf.Framework.Csp1d.Application.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Service;
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

        // **三个生成策略扩展点在这里也必须生效。** 基线 `Csp1dMilp.initialPlans` 接收并使用了
        // `candidateFilters` / `canonicalKeyOverrides` / `dominanceAcceptOverrides` 三者
        // （见基线 `Csp1dMilp.kt:139-176`），而 C# 这条 MILP 直连路径**一个都没接**——
        // 于是同一个策略经由列生成生效、经由 MILP 直连**失效**。
        //
        // **All three generation-strategy extension points must take effect here too.** The baseline's `Csp1dMilp.initialPlans` accepts and uses
        // `candidateFilters` / `canonicalKeyOverrides` / `dominanceAcceptOverrides` (see baseline `Csp1dMilp.kt:139-176`), while C#'s MILP-direct path wired **none of them** —
        // so the same strategy works through column generation and **silently fails** through the MILP path.
        // **与列生成路径共用同一份投影**（`Csp1dGenerationStrategyProjection`）。
        // — 两条路径各写一份，结果一条接了一条没接——**复制粘贴的代价**。
        //
        // **Shares one projection with the column-generation path** (`Csp1dGenerationStrategyProjection`).  wrote two copies, so one got wired and the other
        // did not — **the cost of copy-paste**.
        IReadOnlyList<ICsp1dGenerationStrategy<V>> strategies = resolvedConfig.ExtensionSet.GenerationStrategies;
        IReadOnlyList<global::System.Func<CuttingPlan<V>, IReadOnlyList<CuttingPlan<V>>, bool>> candidateFilters =
            Csp1dGenerationStrategyProjection.CandidateFilters(strategies);
        IReadOnlyList<global::System.Func<CuttingPlan<V>, string?>> canonicalKeyOverrides =
            Csp1dGenerationStrategyProjection.CanonicalKeyOverrides(strategies);
        IReadOnlyList<global::System.Func<CuttingPlan<V>, IReadOnlyList<CuttingPlan<V>>, bool>> dominanceAcceptOverrides =
            Csp1dGenerationStrategyProjection.DominanceAcceptOverrides(strategies);

        IReadOnlyList<CuttingPlan<V>> generatedPlans = await InitialPlansAsync(
            problem,
            resolvedConfig.ColumnGeneration,
            domainPolicies,
            resolvedConfig.ExtensionSet.FlowPolicies,
            widthCheck,
            candidateFilters,
            canonicalKeyOverrides,
            dominanceAcceptOverrides);

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
        Func<Material<V>, Product<V>, Quantity<V>, bool>? widthFeasibilityCheck,
        IReadOnlyList<global::System.Func<CuttingPlan<V>, IReadOnlyList<CuttingPlan<V>>, bool>> candidateFilters,
        IReadOnlyList<global::System.Func<CuttingPlan<V>, string?>> canonicalKeyOverrides,
        IReadOnlyList<global::System.Func<CuttingPlan<V>, IReadOnlyList<CuttingPlan<V>>, bool>> dominanceAcceptOverrides) {
        if (configuration.MaxInitialPlans <= Int64.Zero) {
            return Task.FromResult<IReadOnlyList<CuttingPlan<V>>>(Array.Empty<CuttingPlan<V>>());
        }

        var generationInput = new GenerationInput<V> {
            Materials = problem.Materials,
            Demands = problem.Demands,
            Costars = problem.Costars,
            CandidateFilters = candidateFilters,
            DominanceAcceptOverrides = dominanceAcceptOverrides,
        };

        CuttingPlanGenerationReport<CuttingPlan<V>> report = _initialGenerator.GenerateWithReport(generationInput);

        // Deduplicate by canonical key, honouring strategy overrides (baseline `distinctBy { resolveCanonicalKey(it) }`).
        var generatedPlans = report.Plans
            .DistinctByCanonicalKey(plan => Csp1dGenerationStrategyProjection.Resolve(plan, canonicalKeyOverrides))
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
                // **候选过滤**：基线 `SimpleInitialCuttingPlanGenerator` 也消费 `candidateFilters`，
                // 语义是 `candidateFilters.all { it(plan, input.existingPlans) }`。
                // **Candidate filter**: the baseline's `SimpleInitialCuttingPlanGenerator` also consumes `candidateFilters`, with `all { it(plan, input.existingPlans) }`.
                if (!PassesCandidateFilters(plan, input)) {
                    continue;
                }
                if (material.Enabled(plan)) {
                    plans.Add(plan);
                }
            }
        }
        return plans;
    }

    /// <summary>候选过滤是否全部通过 / Whether every candidate filter passes.</summary>
    /// <remarks>
    /// 语义与基线 `candidateFilters.all { ... }` 一致：**任一返回 false 就拒**。
    ///
    /// Same semantics as the baseline's `candidateFilters.all { ... }`: **any false rejects**.
    /// </remarks>
    /// <param name="plan">候选方案 / Candidate plan.</param>
    /// <param name="input">生成输入 / Generation input.</param>
    /// <returns>true 表示通过 / true if it passes.</returns>
    private static bool PassesCandidateFilters(CuttingPlan<TV> plan, GenerationInput<TV> input) {
        foreach (global::System.Func<CuttingPlan<TV>, IReadOnlyList<CuttingPlan<TV>>, bool> filter in input.CandidateFilters) {
            if (!filter(plan, input.ExistingPlans)) {
                return false;
            }
        }
        return true;
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

    /// <summary>
    /// 按对偶价格生成定价列 / Generate priced columns driven by shadow (dual) prices.
    /// </summary>
    /// <param name="input">定价输入 / Pricing input.</param>
    /// <returns>定价出的新列 / The priced columns.</returns>
    /// <remarks>
    /// **实现前这个方法只做了 `_initialGenerator.Generate(input.GenerationInput)`**——
    /// 也就是说"减成本定价"**根本没有读对偶价格**，每一轮产出的都是初始方案，于是
    /// **第一轮必然全部重复、以 `AllDuplicates` 收敛**。名字与行为不一致，且流程看起来"能跑"。
    ///
    /// 现在按基线 `SimplePricingGenerator` 的语义实现：
    /// 1. 取第一条物料；没有则返回空。
    /// 2. 逐条需求：达到 `MaxGeneratedPlans` 就停。
    /// 3. 查 `ProductDemandShadowPriceKey(产品 id, 单位符号)` 的对偶价格；**缺失或非正就跳过**。
    /// 4. 在产品宽度里挑一条物料能切的；没有则跳过。
    /// 5. 生成"单条切片 + 需求贡献"的方案。
    ///
    /// **第 3 步是定价的全部意义**：对偶价格非正意味着这条需求在当前 LP 解下**不值得再进列**。
    /// 不查它就等于"每轮把初始方案再报一遍"。
    ///
    /// **Before this, the method only did `_initialGenerator.Generate(input.GenerationInput)`** — i.e. "reduced-cost pricing" **never read the dual prices**, so every
    /// round produced the initial plans, and **the first round was necessarily all duplicates and terminated with `AllDuplicates`**. The name and the behaviour
    /// disagreed while the flow still "ran".
    ///
    /// It now follows the baseline `SimplePricingGenerator` (steps above).
    ///
    /// **Step 3 is the entire point of pricing**: a non-positive dual price means the demand is **not worth adding a column for** under the current LP solution.
    /// Skipping that check is the same as "report the initial plans again every round".
    /// </remarks>
    public IReadOnlyList<CuttingPlan<V>> Generate(Csp1dPricingInput<V> input) {
        // 基线 `ReducedCostPricingGenerator` 是**包装器**：先让 enumerator 枚举候选，再按对偶收益筛选。
        // 基线 `ReducedCostPricingGenerator` is a **wrapper**: the enumerator produces candidates, which are then filtered by dual benefit.
        CuttingPlanGenerationReport<CuttingPlan<V>> report = _initialGenerator.GenerateWithReport(input.GenerationInput);
        if (report.Plans.Count == 0) {
            return report.Plans;
        }

        ulong maxGeneratedPlans = input.MaxGeneratedPlans.ToULong();
        // **顺序：候选内部去重 → 收益/成本筛选 → 排序 → 截断。**
        //
        // **去重必须在截断之前**：先截断再去重会把名额浪费在重复列上——
        // 上限 2、候选 `[A, A, B]` 时，先截断只剩 `[A, A]`，去重后**只剩 1 条**。
        // 由 `Csp1dPricingOrderTests` 钉住。
        //
        // **只做"候选内部去重"，不过滤池里已有的方案**：基线把"池里已有的"过滤放在
        // `deduplicatePlans`（**列生成侧**）。放在定价器里的话，由于 C# 的枚举器与初始池
        // 共用同一个生成器，第一轮就会被滤空 ⇒ 报 0 条 ⇒ `PricingConverged`；
        // 交给列生成侧 ⇒ 报 N 条、去重后 0 条 ⇒ `AllDuplicates`（基线行为，
        // `Csp1dApplicationAcceptanceTest.kt:726`）。**这是 的结论。**
        //
        // **Order: dedupe within the candidates → benefit/cost filter → sort → truncate.**
        //
        // **Deduplication must precede truncation**: truncating first wastes slots on duplicates — with a limit of 2 and candidates `[A, A, B]`, truncation leaves `[A, A]`
        // and deduping then yields **only 1**. Pinned by `Csp1dPricingOrderTests` .
        //
        // **Only dedupe within the candidates; do not filter plans already in the pool**: the baseline puts that filter in `deduplicatePlans` (**the column-generation
        // side**). Doing it here would empty the first round (C#'s enumerator shares the initial pool's generator) ⇒ 0 reported ⇒ `PricingConverged`; leaving it downstream
        // ⇒ N reported, 0 after dedup ⇒ `AllDuplicates` (the baseline behaviour, `Csp1dApplicationAcceptanceTest.kt:726`). **That is the earlier conclusion.**
        //
        // **This paragraph used to claim "dedupe, then filter the pool, then truncate"** while the code only deduped — a comment describing behaviour the code no longer
        // had. Corrected.
        //
        var seenKeys = new HashSet<CuttingPlanCanonicalKey>();
        var candidates = new List<PricedCandidate<V>>();
        foreach (CuttingPlan<V> plan in report.Plans) {
            var planKey = CuttingPlanCanonicalKey.From(plan);
            if (!seenKeys.Add(planKey)) {
                continue;
            }
            // **这一步是"减成本"的全部意义**：对偶收益必须**大于目标成本**，否则这条列改善不了主问题。
            // **This step is all of "reduced cost"**: the dual benefit must **exceed the objective cost**, or the column cannot improve the master problem.
            V benefit = ComputeDualBenefit(plan, input.ShadowPrices);
            if (input.BenefitModifiers is { } benefitModifiers) {
                foreach (global::System.Func<CuttingPlan<V>, V, V> modifier in benefitModifiers) {
                    benefit = modifier(plan, benefit);
                }
            }
            V objectiveCost = ComputeObjectiveCost(plan, input.ObjectiveConfig);
            if (input.CostModifiers is { } costModifiers) {
                foreach (global::System.Func<CuttingPlan<V>, V, V> modifier in costModifiers) {
                    objectiveCost = modifier(plan, objectiveCost);
                }
            }
            if (!IsImproving(plan, benefit, objectiveCost, input.ImprovingJudges)) {
                continue;
            }
            candidates.Add(new PricedCandidate<V>(plan, benefit, objectiveCost));
        }

        // **基线是"先排序、再取上限"**，不是"按枚举顺序取上限"。
        // 上限截断时，**排序决定了哪几条被留下**——不排序就会留下枚举顺序里的前几条，
        // 而那几条可能恰恰是最差的。
        //
        // **The baseline sorts first, then takes the limit** — it does not take the limit in enumeration order. When the limit truncates, **the sort decides which
        // columns survive**; without it you keep the first few in enumeration order, which may be exactly the worst ones.
        candidates.Sort(CompareByScore);
        var pricedPlans = new List<CuttingPlan<V>>();
        foreach (PricedCandidate<V> candidate in candidates) {
            if ((ulong)pricedPlans.Count >= maxGeneratedPlans) {
                break;
            }
            pricedPlans.Add(candidate.Plan);
        }
        return pricedPlans;
    }

    /// <summary>
    /// 定价候选 / A priced candidate.
    /// </summary>
    /// <param name="Plan">切割方案 / Cutting plan.</param>
    /// <param name="Benefit">对偶收益 / Dual benefit.</param>
    /// <param name="ObjectiveCost">目标成本 / Objective cost.</param>
    /// <typeparam name="TP">数值类型 / Numeric value type.</typeparam>
    private sealed record PricedCandidate<TP>(
        CuttingPlan<TP> Plan,
        TP Benefit,
        TP ObjectiveCost) where TP : struct;

    /// <summary>
    /// 按分数比较两个候选 / Compare two candidates by score.
    /// </summary>
    /// <param name="left">左候选 / Left candidate.</param>
    /// <param name="right">右候选 / Right candidate.</param>
    /// <typeparam name="TP">数值类型 / Numeric value type.</typeparam>
    /// <returns>比较结果，负数表示 left 优先 / Comparison result; negative means left ranks first.</returns>
    /// <remarks>
    /// 基线：
    ///
    /// ```kotlin
    /// val leftScoreSide = left.benefit + right.objectiveCost
    /// val rightScoreSide = right.benefit + left.objectiveCost
    /// when (leftScoreSide partialOrd rightScoreSide) {
    ///     is Order.Greater -> -1
    ///     is Order.Less -> 1
    ///     else -> 0
    /// }
    /// ```
    ///
    /// **交叉求和与"按净收益 `benefit − cost` 降序"是数学等价的**：
    /// `left.b + right.c > right.b + left.c` ⟺ `left.b − left.c > right.b − right.c`。
    /// 所以**这个比较器本身没有独立语义**——它钉住的是"**排序这一步存在**"。
    ///
    /// **真正有语义的是"排序后再截断"**：不排序时截断留下的是**枚举顺序里的前几条**，
    /// 而那几条可能是净收益最差的。**这才是判据要钉住的东西。**
    ///
    /// The cross-sum is **mathematically equivalent to "descending net benefit `benefit − cost`"**: `left.b + right.c > right.b + left.c` ⟺
    /// `left.b − left.c > right.b − right.c`. So **this comparator has no independent semantics** — what it pins is that "**the sort step exists**".
    ///
    /// **What actually carries meaning is "sort, then truncate"**: without sorting, truncation keeps **the first few in enumeration order**, which may be the worst by net
    /// benefit. **That is what the criterion pins.**
    /// </remarks>
    private static int CompareByScore<TP>(PricedCandidate<TP> left, PricedCandidate<TP> right) where TP : struct {
        if (typeof(TP) != typeof(Flt64)) {
            return 0;
        }
        double leftScore = ((Flt64)(object)left.Benefit).ToDouble()
            + ((Flt64)(object)right.ObjectiveCost).ToDouble();
        double rightScore = ((Flt64)(object)right.Benefit).ToDouble()
            + ((Flt64)(object)left.ObjectiveCost).ToDouble();
        if (leftScore > rightScore) {
            return -1;
        }
        if (leftScore < rightScore) {
            return 1;
        }
        return 0;
    }

    /// <summary>
    /// 计算方案的对偶收益 / Compute a plan's dual benefit.
    /// </summary>
    /// <param name="plan">切割方案 / Cutting plan.</param>
    /// <param name="shadowPrices">影子价格映射 / Shadow-price map.</param>
    /// <returns>对偶收益 / Dual benefit.</returns>
    /// <remarks>
    /// 基线公式：`benefit = Σ(贡献量 × 需求对偶价) + 物料对偶价 + 设备对偶价 + 产能消耗 × 设备产能对偶价`。
    ///
    /// **时这里只做了需求那一项**，理由是那三个键"只有生产方、没有消费方"。这里把消费方补上了——
    /// **现在四项齐全**，公式与基线逐项对齐。
    ///
 /// **In only the demand term was implemented**, on the grounds that those three keys had "only producers, no consumers". adds the consumers —
    /// **all four terms are now present**, matching the baseline term by term.
    /// </remarks>
    internal static double ComputeDualBenefitForTest(CuttingPlan<V> plan, ShadowPriceMap<V> shadowPrices) {
        V benefit = ComputeDualBenefit(plan, shadowPrices);
        return ((Flt64)(object)benefit).ToDouble();
    }

    private static V ComputeDualBenefit(CuttingPlan<V> plan, ShadowPriceMap<V> shadowPrices) {
        if (typeof(V) != typeof(Flt64)) {
            // `ponytail: 非 Flt64 时无法通用地做乘加，返回"总是改善"；出现别的数值类型时补 IComparable 约束。`
            return default;
        }
        double benefit = 0.0;
        foreach (CuttingPlanDemandContribution<V> contribution in plan.DemandContributions) {
            string unitSymbol = GenerationQuantityMath.UnitKey(contribution.Quantity.Unit);
            V? price = shadowPrices[
                new ProductDemandShadowPriceKey(contribution.Product.Id, unitSymbol)];
            if (price is null) {
                continue;
            }
            benefit += ((Flt64)(object)contribution.Quantity.Value).ToDouble()
                * ((Flt64)(object)price.Value).ToDouble();
        }

        // 物料用量项 / Material-usage term.
        V? materialPrice = shadowPrices[new MaterialUsageShadowPriceKey(plan.Material.Id)];
        if (materialPrice is not null) {
            benefit += ((Flt64)(object)materialPrice.Value).ToDouble();
        }

        // 设备两项 / The two machine terms.
        if (plan.MachineId is { } machineId) {
            V? machineBatchPrice = shadowPrices[new MachineBatchShadowPriceKey(machineId)];
            if (machineBatchPrice is not null) {
                benefit += ((Flt64)(object)machineBatchPrice.Value).ToDouble();
            }
            V? machineCapacityPrice = shadowPrices[new MachineCapacityShadowPriceKey(machineId)];
            // **产能消耗与设备对偶价必须同时存在**：缺任一项这一项就加不进来。
            // **Both the capacity consumption and the machine dual must be present**; missing either means this term cannot be added.
            if (machineCapacityPrice is not null && plan.CapacityConsumption is { } capacity) {
                benefit += ((Flt64)(object)capacity.Value).ToDouble()
                    * ((Flt64)(object)machineCapacityPrice.Value).ToDouble();
            }
        }

        return (V)(object)new Flt64(benefit);
    }

    /// <summary>
    /// 计算方案的目标成本 / Compute a plan's objective cost.
    /// </summary>
    /// <param name="plan">切割方案 / Cutting plan.</param>
    /// <param name="config">定价目标配置；null 时只算基础值 / Pricing objective config; base value only when null.</param>
    /// <typeparam name="TC">数值类型 / Numeric value type.</typeparam>
    /// <returns>目标成本 / Objective cost.</returns>
    /// <remarks>
    /// 基线公式（基础成本是 `1`，即"方案用一次"）：
    ///
    /// ```
    /// cost = 1
    ///      + planUsagePenalty                                   （若配置）
    ///      + restWidth × trimWidthPenalty                       （若配置且 restWidth > 0）
    ///      + restWidth × materialLength × restMaterialPenalty   （若三者都配置且均 > 0）
    ///      + materialCostPenalty[plan.material.id]              （若该物料配置了）
    /// ```
    ///
    /// **每一项都有前置条件**，基线是逐个 `if` 判断的。这里**照抄这些前置条件**——
    /// 漏掉 `isPositive` 那类守卫会让"零宽余料也加惩罚"，从而把成本抬高、把该出列的列挡掉。
    ///
    /// The baseline formula is above (base cost `1`, i.e. "one use of the plan"). **Every term has preconditions**, checked one `if` at a time in the baseline; those
    /// are copied here — **dropping an `isPositive` guard makes a zero-width remainder add a penalty**, raising the cost and blocking columns that should emit.
    /// </remarks>
    private static TC ComputeObjectiveCost<TC>(
        CuttingPlan<TC> plan,
        Csp1dPricingObjectiveConfig<TC>? config) where TC : struct {
        if (typeof(TC) != typeof(Flt64)) {
            // `ponytail: 非 Flt64 时无法通用地做乘加，返回"永不出列"的成本；出现别的数值类型时补约束。`
            return default;
        }
        double cost = 1.0;
        if (config is null) {
            return (TC)(object)new Flt64(cost);
        }
        if (config.PlanUsagePenalty is { } planUsage) {
            cost += ((Flt64)(object)planUsage).ToDouble();
        }
        double restWidth = plan.RestWidth is { } rest
            ? ((Flt64)(object)rest.Value).ToDouble()
            : 0.0;
        if (config.TrimWidthPenalty is { } trimWidth && restWidth > 0.0) {
            cost += restWidth * ((Flt64)(object)trimWidth).ToDouble();
        }
        if (config.RestMaterialPenalty is { } restMaterial
            && plan.Material.Length is { } materialLength
            && restWidth > 0.0) {
            double length = ((Flt64)(object)materialLength.Value).ToDouble();
            if (length > 0.0) {
                cost += restWidth * length * ((Flt64)(object)restMaterial).ToDouble();
            }
        }
        if (config.MaterialCostPenalty is { } materialCosts
            && materialCosts.TryGetValue(plan.Material.Id, out TC materialCost)) {
            cost += ((Flt64)(object)materialCost).ToDouble();
        }
        return (TC)(object)new Flt64(cost);
    }

    /// <summary>
    /// 判断一条候选是否改善主问题 / Check whether a candidate improves the master problem.
    /// </summary>
    /// <param name="plan">切割方案 / Cutting plan.</param>
    /// <param name="benefit">对偶收益 / Dual benefit.</param>
    /// <param name="objectiveCost">目标成本 / Objective cost.</param>
    /// <param name="judges">自定义判据列表；可为 null / Custom judges; may be null.</param>
    /// <typeparam name="TN">数值类型 / Numeric value type.</typeparam>
    /// <returns>是否改善 / Whether it improves.</returns>
    /// <remarks>
    /// **先问自定义判据，全部不表态才回退默认**——基线的顺序正是如此：
    ///
    /// ```kotlin
    /// for (judge in judges) {
    ///     val result = judge(candidate.plan, candidate.benefit, candidate.objectiveCost)
    ///     if (result != null) return result
    /// }
    /// return when (candidate.benefit partialOrd candidate.objectiveCost) {
    ///     is Order.Greater -> true
    ///     else -> false
    /// }
    /// ```
    ///
    /// **三值语义是这里的全部要害**：`null` 表示"这条判据不表态"，**继续问下一条**。
    /// 若把 `null` 当成 `false`，一条"只管某类方案"的判据会把**其余所有方案**判成不改善，
    /// **列生成立刻枯竭**——而且症状是"迅速收敛"，**看起来像正常结束**。
    ///
    /// **默认判据严格大于**：等于时不出列（`Order.Greater` 才 `true`，`Order.Equal` 落 `else`）。
    ///
    /// **Custom judges are asked first; only when all abstain does the default apply** — exactly the baseline order (snippet above).
    ///
    /// **The three-valued semantics is the whole crux**: `null` means "this judge abstains" and **the next one is asked**. Treating `null` as `false` would make a judge
    /// that only handles one kind of plan **reject every other plan**, so **column generation dries up immediately** — and the symptom is "converged quickly",
    /// **which looks like a normal finish**.
    ///
    /// **The default is strictly greater**: equality yields no column (`Order.Greater` is `true`, `Order.Equal` falls to `else`).
    /// </remarks>
    private static bool IsImproving<TN>(
        CuttingPlan<TN> plan,
        TN benefit,
        TN objectiveCost,
        IReadOnlyList<global::System.Func<CuttingPlan<TN>, TN, TN, bool?>>? judges) where TN : struct {
        if (judges is not null) {
            foreach (global::System.Func<CuttingPlan<TN>, TN, TN, bool?> judge in judges) {
                bool? verdict = judge(plan, benefit, objectiveCost);
                if (verdict is not null) {
                    return verdict.Value;
                }
            }
        }
        if (typeof(TN) != typeof(Flt64)) {
            return true;
        }
        return ((Flt64)(object)benefit).ToDouble() > ((Flt64)(object)objectiveCost).ToDouble();
    }

    /// <summary>
    /// 判断对偶价格是否严格为正 / Check whether a dual price is strictly positive.
    /// </summary>
    /// <param name="price">对偶价格 / Dual price.</param>
    /// <typeparam name="N">数值类型 / Numeric value type.</typeparam>
    /// <returns>是否严格为正 / Whether strictly positive.</returns>
    /// <remarks>
    /// **`N` 只约束 `struct`**，所以没有通用的比较方式。这里对 `Flt64` 精确比较（`Flt64` 是实际使用的类型），
    /// 其它类型**按"存在即接受"处理**——这是与基线的**已知偏离**：基线用 `RealNumber` 的 `isPositive`，对所有数值类型都成立。
    ///
    /// `ponytail: 非 Flt64 时跳过正负过滤；若将来出现别的数值类型，改为给 V 加 IComparable 约束。`
    ///
    /// **`N` is only constrained to `struct`**, so there is no generic comparison. `Flt64` is compared exactly (it is the type actually used); other types are
    /// **treated as accepted when present** — a **known divergence** from the baseline, which uses `RealNumber.isPositive` and works for all numeric types.
    /// </remarks>
    private static bool IsPositiveShadowPrice<N>(N price) where N : struct {
        if (typeof(N) == typeof(Flt64)) {
            return ((Flt64)(object)price).ToDouble() > 0.0;
        }
        return true;
    }
}
