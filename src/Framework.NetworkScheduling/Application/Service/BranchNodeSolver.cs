// 单分支节点求解器 / The single branch-node solver.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Framework.NetworkScheduling.Application.Model;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteCompilation;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteCompilation.Shadow;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteGeneration.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteGeneration.Policy;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteGeneration.Service;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Infrastructure;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Policy;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Pricing;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Application.Service;

/// <summary>
/// 单分支节点求解结果 / A single node solve result.
/// </summary>
/// <remarks>
/// **`PricingComplete` 与 `SolverStatus` 是两套独立信号**。定价未收敛但求解器给出了可行解时，`LowerBound` 只是继承的父下界，
/// 不是本节点的真实下界；把它当下界会剪掉本来可能有更好解的分支。
/// **`PricingComplete` and `SolverStatus` are two independent signals.** When pricing has not converged but the solver returned a
/// feasible solution, `LowerBound` is only the inherited parent bound, not this node's true bound; treating it as a bound would
/// prune branches that might still hold a better solution.
/// **`Iterations` 统计 Phase I + Phase II 的总轮数**，供全局预算判断使用。
/// **`Iterations` counts Phase I plus Phase II**, feeding the global budget.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="LowerBound">节点 LP 下界（定价收敛后的 LP 目标值）/ Node LP lower bound after pricing convergence.</param>
/// <param name="RouteValues">路线变量值 / Route variable values.</param>
/// <param name="IsInteger">解是否满足整数容差 / Whether the solution satisfies the integrality tolerance.</param>
/// <param name="IsFeasible">节点是否可行 / Whether the node is feasible.</param>
/// <param name="PricingComplete">定价是否完整收敛 / Whether pricing fully converged.</param>
/// <param name="LpObjective">LP 目标值 / The LP objective value.</param>
/// <param name="Iterations">本节点消耗的 CG 迭代次数 / The CG iterations consumed by this node.</param>
/// <param name="TimeLimitReached">是否因时间上限中断 / Whether the time limit interrupted the node.</param>
/// <param name="SolverStatus">最后一次 LP 的求解状态 / The last LP solver status.</param>
/// <param name="Columns">本节点生成的完整列池快照 / The column-pool snapshot generated at this node.</param>
public sealed record NodeSolveResult<V>(
    Flt64 LowerBound,
    IReadOnlyList<(Route<V> Route, Flt64 Value)> RouteValues,
    bool IsInteger,
    bool IsFeasible,
    bool PricingComplete,
    Flt64 LpObjective,
    int Iterations,
    bool TimeLimitReached = false,
    SolverStatus SolverStatus = SolverStatus.Optimal,
    IReadOnlyList<Route<V>>? Columns = null) where V : struct, IRealNumber<V> {
    /// <summary>本节点生成的完整列池快照 / The column-pool snapshot generated at this node.</summary>
    public IReadOnlyList<Route<V>> Columns { get; init; } = Columns ?? Array.Empty<Route<V>>();
}

/// <summary>
/// 单分支节点求解器 / The single branch-node solver.
/// </summary>
/// <remarks>
/// **每个节点都建全新的模型与上下文**。可变建模状态一旦跨节点复用，父节点的列、对偶与阶段标记会渗进子节点，而这种渗漏只表现为
/// "下界莫名其妙地高"，不会报错。
/// **Every node gets a fresh model and context.** Reusing mutable modeling state across nodes would leak the parent's columns, duals
/// and phase markers into the child, and that leak only shows up as "the bound is inexplicably high", never as an error.
/// **Phase I 的提前终止是正确性优化而非近似**。第一阶段目标是非负人工变量之和，达到零即证明不存在更低目标的列，无需继续定价。
/// **Early termination in Phase I is a correctness-preserving optimisation, not an approximation.** The phase-one objective is a sum
/// of non-negative artificial variables; reaching zero proves no lower-objective column exists, so pricing can stop.
/// **列数截断不等于收敛**。`ExactPricingComplete` 为假时，即使本轮没拿到新列也不能判定收敛。
/// **Truncating columns is not convergence.** When `ExactPricingComplete` is false, an empty batch still does not prove
/// convergence.
/// **与有意偏离**：C# 的 `IColumnGenerationSolver.SolveLPAsync` 只返回可行解，无法表达"不可行证书"或非最优状态，
/// 因此的 `LPResultWithStatus.Infeasible` 与 `status != Optimal` 分支在这里统一落到失败路径。调用方拿到的失败结果
/// 携带原始求解错误，不会被伪装成"节点不可行"。
/// **Deliberate divergence from the code**: C#'s `IColumnGenerationSolver.SolveLPAsync` returns only a feasible output and
/// cannot express an infeasibility certificate or a non-optimal status, so its `LPResultWithStatus.Infeasible` and
/// `status != Optimal` branches collapse onto the failure path here. The caller receives the original solver error, never a
/// disguised "node infeasible".
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class BranchNodeSolver<V> where V : struct, IFloatingNumber<V> {
    private readonly VrptwInstance<V> _instance;
    private readonly IColumnGenerationSolver _solver;
    private readonly INetworkSchedulingSolverValueAdapter<V> _valueAdapter;
    private readonly IDistanceCalculator<V> _distanceCalculator;
    private readonly ITravelTimeCalculator<V> _travelTimeCalculator;
    private readonly IArcCostCalculator<V> _arcCostCalculator;
    private readonly IRouteCostPolicy<V> _routeCostPolicy;
    private readonly IArcFeasibilityPolicy<V> _arcFeasibilityPolicy;
    private readonly ILabelDominancePolicy _labelDominancePolicy;
    private readonly IPricingColumnSelector<V> _pricingColumnSelector;
    private readonly IReadOnlyList<ICGPipeline<VrpShadowPriceArguments, LinearMetaModel<Flt64>, VrpShadowPriceMap>>
        _extraPipelines;
    private readonly int _maxColumnsPerPricing;
    private readonly int _maxCGIterations;
    private readonly IPricingInterruptionChecker _interruptionChecker;
    private readonly IReadOnlyList<Route<V>> _inheritedColumns;
    private readonly ResourceGraph<VehicleTypeId>? _resourceGraph;
    private readonly Func<VrptwInstance<V>, LinearMetaModel<Flt64>, IReadOnlyList<IRouteCompilationContribution<V>>>
        _extraContributionFactory;

    /// <summary>
    /// 构造单分支节点求解器 / Construct the single branch-node solver.
    /// </summary>
    /// <param name="instance">实例 / The instance.</param>
    /// <param name="solver">列生成求解器 / The column-generation solver.</param>
    /// <param name="valueAdapter">数值边界 / The numeric boundary.</param>
    /// <param name="distanceCalculator">距离计算 / The distance calculator.</param>
    /// <param name="travelTimeCalculator">行程时间计算 / The travel-time calculator.</param>
    /// <param name="arcCostCalculator">弧成本计算 / The arc-cost calculator.</param>
    /// <param name="routeCostPolicy">路线成本策略 / The route-cost policy.</param>
    /// <param name="arcFeasibilityPolicy">弧可行性策略 / The arc-feasibility policy.</param>
    /// <param name="labelDominancePolicy">标签支配策略 / The label-dominance policy.</param>
    /// <param name="pricingColumnSelector">定价列选择器 / The pricing column selector.</param>
    /// <param name="extraPipelines">额外管线 / Extra pipelines.</param>
    /// <param name="maxColumnsPerPricing">单次定价的列数上限 / The per-pricing column cap.</param>
    /// <param name="maxCGIterations">列生成迭代上限 / The CG iteration cap.</param>
    /// <param name="interruptionChecker">定价中断检查器 / The pricing interruption checker.</param>
    /// <param name="inheritedColumns">继承的路线列 / Inherited route columns.</param>
    /// <param name="resourceGraph">资源图 / The resource graph.</param>
    /// <param name="extraContributionFactory">额外贡献工厂 / The extra-contribution factory.</param>
    public BranchNodeSolver(
        VrptwInstance<V> instance,
        IColumnGenerationSolver solver,
        INetworkSchedulingSolverValueAdapter<V> valueAdapter,
        IDistanceCalculator<V> distanceCalculator,
        ITravelTimeCalculator<V> travelTimeCalculator,
        IArcCostCalculator<V> arcCostCalculator,
        IRouteCostPolicy<V> routeCostPolicy,
        IArcFeasibilityPolicy<V>? arcFeasibilityPolicy = null,
        ILabelDominancePolicy? labelDominancePolicy = null,
        IPricingColumnSelector<V>? pricingColumnSelector = null,
        IReadOnlyList<ICGPipeline<VrpShadowPriceArguments, LinearMetaModel<Flt64>, VrpShadowPriceMap>>?
            extraPipelines = null,
        int maxColumnsPerPricing = int.MaxValue,
        int maxCGIterations = 1000,
        IPricingInterruptionChecker? interruptionChecker = null,
        IReadOnlyList<Route<V>>? inheritedColumns = null,
        ResourceGraph<VehicleTypeId>? resourceGraph = null,
        Func<VrptwInstance<V>, LinearMetaModel<Flt64>, IReadOnlyList<IRouteCompilationContribution<V>>>?
            extraContributionFactory = null) {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(solver);
        ArgumentNullException.ThrowIfNull(valueAdapter);
        ArgumentNullException.ThrowIfNull(distanceCalculator);
        ArgumentNullException.ThrowIfNull(travelTimeCalculator);
        ArgumentNullException.ThrowIfNull(arcCostCalculator);
        ArgumentNullException.ThrowIfNull(routeCostPolicy);
        _instance = instance;
        _solver = solver;
        _valueAdapter = valueAdapter;
        _distanceCalculator = distanceCalculator;
        _travelTimeCalculator = travelTimeCalculator;
        _arcCostCalculator = arcCostCalculator;
        _routeCostPolicy = routeCostPolicy;
        _arcFeasibilityPolicy = arcFeasibilityPolicy ?? new DefaultArcFeasibilityPolicy<V>();
        _labelDominancePolicy = labelDominancePolicy ?? DefaultLabelDominancePolicy.Instance;
        _pricingColumnSelector = pricingColumnSelector ?? new KeepAllColumnSelector<V>();
        _extraPipelines = extraPipelines
            ?? new List<ICGPipeline<VrpShadowPriceArguments, LinearMetaModel<Flt64>, VrpShadowPriceMap>>();
        _maxColumnsPerPricing = maxColumnsPerPricing;
        _maxCGIterations = maxCGIterations;
        _interruptionChecker = interruptionChecker ?? NeverInterruptChecker.Instance;
        _inheritedColumns = inheritedColumns ?? Array.Empty<Route<V>>();
        _resourceGraph = resourceGraph;
        _extraContributionFactory = extraContributionFactory
            ?? ((_, _) => Array.Empty<IRouteCompilationContribution<V>>());
    }

    /// <summary>
    /// 过滤与分支遮罩兼容的既有路线列 / Filter existing route columns compatible with a branch mask.
    /// </summary>
    /// <remarks>
    /// **既有列也必须过遮罩与资源图两道检查**。只用遮罩会放过资源图里不存在的弧；只用资源图会放过本分支禁止的节点。
    /// 两者漏一条，子节点就会带着父分支不允许的列进场。
    /// **Inherited columns must pass both the mask and the resource graph.** The mask alone lets through arcs absent from the
    /// resource graph; the graph alone lets through nodes this branch forbids. Missing either lets a child node enter carrying
    /// columns its branch disallows.
    /// </remarks>
    /// <param name="branchMask">分支遮罩 / The branch mask.</param>
    /// <param name="columns">既有路线列 / The existing route columns.</param>
    /// <param name="resourceGraph">资源图 / The resource graph.</param>
    /// <returns>兼容的路线列 / The compatible route columns.</returns>
    public static IReadOnlyList<Route<V>> FilterRouteColumnsForBranch(
        BranchMask<VehicleTypeId> branchMask,
        IReadOnlyList<Route<V>> columns,
        ResourceGraph<VehicleTypeId>? resourceGraph = null) {
        ArgumentNullException.ThrowIfNull(branchMask);
        ArgumentNullException.ThrowIfNull(columns);
        return columns.Where(route => {
            IReadOnlyList<NetworkNodeId> path = route.Stops.Select(stop => stop.NodeId).ToList();
            if (!branchMask.IsRouteCompatible(route.VehicleTypeId, path)) {
                return false;
            }
            for (int index = 0; index < route.ArcIds.Count; index++) {
                if (!branchMask.AllowsArc(
                        route.VehicleTypeId,
                        route.Stops[index].NodeId,
                        route.Stops[index + 1].NodeId,
                        route.ArcIds[index])) {
                    return false;
                }
            }
            if (resourceGraph is null) {
                return true;
            }
            for (int index = 0; index < route.ArcIds.Count; index++) {
                var arc = new ResourceArc<VehicleTypeId>(
                    route.VehicleTypeId,
                    route.Stops[index].NodeId,
                    route.Stops[index + 1].NodeId,
                    route.ArcIds[index]);
                if (!resourceGraph.Arcs.Contains(arc)) {
                    return false;
                }
            }
            return true;
        }).ToList();
    }

    /// <summary>
    /// 求解分支节点 / Solve a branch node.
    /// </summary>
    /// <param name="node">分支节点 / The branch node.</param>
    /// <returns>节点求解结果 / The node solve result.</returns>
    public async Task<Result<NodeSolveResult<V>, ErrorCode, Error<ErrorCode>>> Solve(BranchNode node) {
        ArgumentNullException.ThrowIfNull(node);
        var model = new LinearMetaModel<Flt64>($"node_{node.Id}");
        IReadOnlyList<IRouteCompilationContribution<V>> extraContributions =
            _extraContributionFactory(_instance, model);
        var context = new RouteCompilationContext<V>(
            _instance, _valueAdapter, _extraPipelines, extraContributions);

        Try registered = context.Register(model);
        if (registered is Failed<Success, ErrorCode, Error<ErrorCode>> registerFailure) {
            return Results.Failed<NodeSolveResult<V>>(registerFailure.Error);
        }

        var initialRouteGenerator = new InitialRouteGenerator<V>(
            _instance,
            _valueAdapter,
            _distanceCalculator,
            _travelTimeCalculator,
            _arcCostCalculator,
            _routeCostPolicy,
            _arcFeasibilityPolicy,
            _resourceGraph);
        IReadOnlyList<Route<V>> compatibleInherited = FilterRouteColumnsForBranch(
            node.BranchMask, _inheritedColumns, _resourceGraph);
        IReadOnlyList<Route<V>> initialRoutes = initialRouteGenerator.Generate(node.BranchMask);
        var routesToAdd = compatibleInherited.Concat(initialRoutes).ToList();
        if (routesToAdd.Count > 0) {
            Result<IReadOnlyList<Route<V>>, ErrorCode, Error<ErrorCode>> added =
                context.AddColumns(0UL, routesToAdd, model);
            if (added is Failed<IReadOnlyList<Route<V>>, ErrorCode, Error<ErrorCode>> addFailure) {
                return Results.Failed<NodeSolveResult<V>>(addFailure.Error);
            }
        }

        Result<ColumnGenerationResult, ErrorCode, Error<ErrorCode>> phaseOne =
            await RunColumnGeneration(node, context, model, PricingPhase.PhaseOne);
        if (phaseOne is Failed<ColumnGenerationResult, ErrorCode, Error<ErrorCode>> phaseOneFailure) {
            return Results.Failed<NodeSolveResult<V>>(phaseOneFailure.Error);
        }
        ColumnGenerationResult phaseOneResult =
            ((Ok<ColumnGenerationResult, ErrorCode, Error<ErrorCode>>)phaseOne).Value;
        if (phaseOneResult.SolverStatus is not null) {
            return Results.Ok(new NodeSolveResult<V>(
                node.InheritedLowerBound,
                Array.Empty<(Route<V>, Flt64)>(),
                false,
                phaseOneResult.SolverStatus != SolverStatus.Infeasible
                    && phaseOneResult.SolverStatus != SolverStatus.Unbounded
                    && phaseOneResult.SolverStatus != SolverStatus.InfeasibleOrUnbounded
                    && phaseOneResult.SolverStatus != SolverStatus.SolvingException,
                false,
                new Flt64(double.PositiveInfinity),
                phaseOneResult.Iterations,
                SolverStatus: phaseOneResult.SolverStatus.Value,
                Columns: context.Compilation.Routes));
        }
        if (!phaseOneResult.PricingComplete) {
            if (!phaseOneResult.Interrupted) {
                return Failure<NodeSolveResult<V>>(
                    $"节点 {node.Id} Phase I 定价未在 {_maxCGIterations} 轮内收敛 / "
                    + $"Phase I pricing at node {node.Id} did not converge within {_maxCGIterations} iterations");
            }
            return Results.Ok(new NodeSolveResult<V>(
                node.InheritedLowerBound,
                Array.Empty<(Route<V>, Flt64)>(),
                false,
                true,
                false,
                new Flt64(double.PositiveInfinity),
                phaseOneResult.Iterations,
                TimeLimitReached: true,
                Columns: context.Compilation.Routes));
        }

        Result<bool, ErrorCode, Error<ErrorCode>> converged =
            context.IsPhaseOneConverged(model, _instance.Tolerances.Feasibility);
        if (converged is Failed<bool, ErrorCode, Error<ErrorCode>> convergedFailure) {
            return Results.Failed<NodeSolveResult<V>>(convergedFailure.Error);
        }
        if (!((Ok<bool, ErrorCode, Error<ErrorCode>>)converged).Value) {
            node.MarkInfeasible();
            return Results.Ok(new NodeSolveResult<V>(
                new Flt64(double.PositiveInfinity),
                Array.Empty<(Route<V>, Flt64)>(),
                false,
                false,
                true,
                new Flt64(double.PositiveInfinity),
                phaseOneResult.Iterations,
                Columns: context.Compilation.Routes));
        }

        Try switched = context.SwitchToPhaseTwo(model);
        if (switched is Failed<Success, ErrorCode, Error<ErrorCode>> switchFailure) {
            return Results.Failed<NodeSolveResult<V>>(switchFailure.Error);
        }

        Result<ColumnGenerationResult, ErrorCode, Error<ErrorCode>> phaseTwo =
            await RunColumnGeneration(node, context, model, PricingPhase.PhaseTwo);
        if (phaseTwo is Failed<ColumnGenerationResult, ErrorCode, Error<ErrorCode>> phaseTwoFailure) {
            return Results.Failed<NodeSolveResult<V>>(phaseTwoFailure.Error);
        }
        ColumnGenerationResult phaseTwoResult =
            ((Ok<ColumnGenerationResult, ErrorCode, Error<ErrorCode>>)phaseTwo).Value;
        int totalIterations = phaseOneResult.Iterations + phaseTwoResult.Iterations;
        if (phaseTwoResult.SolverStatus is not null) {
            return Results.Ok(new NodeSolveResult<V>(
                node.InheritedLowerBound,
                Array.Empty<(Route<V>, Flt64)>(),
                false,
                phaseTwoResult.SolverStatus != SolverStatus.Infeasible
                    && phaseTwoResult.SolverStatus != SolverStatus.Unbounded
                    && phaseTwoResult.SolverStatus != SolverStatus.InfeasibleOrUnbounded
                    && phaseTwoResult.SolverStatus != SolverStatus.SolvingException,
                false,
                new Flt64(double.PositiveInfinity),
                totalIterations,
                SolverStatus: phaseTwoResult.SolverStatus.Value,
                Columns: context.Compilation.Routes));
        }
        if (!phaseTwoResult.PricingComplete) {
            if (!phaseTwoResult.Interrupted) {
                return Failure<NodeSolveResult<V>>(
                    $"节点 {node.Id} Phase II 定价未在 {_maxCGIterations} 轮内收敛 / "
                    + $"Phase II pricing at node {node.Id} did not converge within {_maxCGIterations} iterations");
            }
            return Results.Ok(new NodeSolveResult<V>(
                node.InheritedLowerBound,
                Array.Empty<(Route<V>, Flt64)>(),
                false,
                true,
                false,
                new Flt64(double.PositiveInfinity),
                totalIterations,
                TimeLimitReached: true,
                Columns: context.Compilation.Routes));
        }

        // 最终 LP 求解以取得下界。 / The final LP solve yields the lower bound.
        Result<IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>> finalLp =
            await _solver.SolveLPAsync($"node_{node.Id}_final", model);
        if (finalLp is Failed<IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>> finalFailure) {
            return Results.Failed<NodeSolveResult<V>>(finalFailure.Error);
        }
        IColumnGenerationSolver.LpResult finalResult =
            ((Ok<IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>>)finalLp).Value;
        model.SetSolution(finalResult.Solution);
        Flt64 lpObjective = finalResult.Obj;
        node.Solve(lpObjective);

        Result<IReadOnlyList<(Route<V> Route, Flt64 Value)>, ErrorCode, Error<ErrorCode>> routeValues =
            context.ExtractRouteValues(model, _instance.Tolerances.Integrality);
        if (routeValues is
            Failed<IReadOnlyList<(Route<V> Route, Flt64 Value)>, ErrorCode, Error<ErrorCode>> valueFailure) {
            return Results.Failed<NodeSolveResult<V>>(valueFailure.Error);
        }
        IReadOnlyList<(Route<V> Route, Flt64 Value)> extracted =
            ((Ok<IReadOnlyList<(Route<V> Route, Flt64 Value)>, ErrorCode, Error<ErrorCode>>)routeValues).Value;
        // 整数性检查用容差而不是精确比较：LP 解几乎不会给出干净的 0/1。
        // The integrality check uses the tolerance rather than exact comparison: an LP solution almost never yields clean 0/1.
        bool isInteger = extracted.All(pair =>
            pair.Value.CompareTo(_instance.Tolerances.Integrality) <= 0
            || pair.Value.CompareTo(Flt64.One.Minus(_instance.Tolerances.Integrality)) >= 0);

        return Results.Ok(new NodeSolveResult<V>(
            lpObjective,
            extracted,
            isInteger,
            true,
            true,
            lpObjective,
            totalIterations,
            Columns: context.Compilation.Routes));
    }

    /// <summary>
    /// 执行列生成循环 / Run the column-generation loop.
    /// </summary>
    /// <param name="node">分支节点 / The branch node.</param>
    /// <param name="context">路线编译上下文 / The route compilation context.</param>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <param name="phase">当前阶段 / The current phase.</param>
    /// <returns>列生成结果 / The column-generation result.</returns>
    private async Task<Result<ColumnGenerationResult, ErrorCode, Error<ErrorCode>>> RunColumnGeneration(
        BranchNode node,
        RouteCompilationContext<V> context,
        LinearMetaModel<Flt64> model,
        PricingPhase phase) {
        var graphBuilder = new RouteGraphBuilder<V>(
            _instance,
            _valueAdapter,
            _distanceCalculator,
            _travelTimeCalculator,
            _arcCostCalculator,
            _arcFeasibilityPolicy,
            _resourceGraph,
            context.ExtraContributions);
        var pricer = new EspprcPricer<V>(
            _instance, _valueAdapter, _labelDominancePolicy, _routeCostPolicy);

        ulong iteration = 1UL;
        string phaseLabel = phase == PricingPhase.PhaseOne ? "phase1" : "phase2";
        while (iteration <= (ulong)_maxCGIterations) {
            if (_interruptionChecker.ShouldStop()) {
                return Results.Ok(new ColumnGenerationResult(false, (int)iteration - 1, true));
            }
            Result<IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>> lp =
                await _solver.SolveLPAsync($"node_{node.Id}_{phaseLabel}_{iteration}", model);
            if (lp is Failed<IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>> lpFailure) {
                return Results.Failed<ColumnGenerationResult>(lpFailure.Error);
            }
            IColumnGenerationSolver.LpResult lpResult =
                ((Ok<IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>>)lp).Value;
            model.SetSolution(lpResult.Solution);

            // Phase I 目标是非负人工变量之和：达到零即取得全局最优，无需继续证明不存在更低目标的列。
            // The phase-one objective is a sum of non-negative artificial variables: reaching zero proves global optimality
            // without further pricing.
            if (phase == PricingPhase.PhaseOne
                && lpResult.Obj.CompareTo(_instance.Tolerances.Feasibility) <= 0) {
                return Results.Ok(new ColumnGenerationResult(true, (int)iteration, false));
            }

            Result<PricingDuals, ErrorCode, Error<ErrorCode>> duals =
                context.ExtractPricingDuals(model, lpResult.DualSolution.ToMeta());
            if (duals is Failed<PricingDuals, ErrorCode, Error<ErrorCode>> dualFailure) {
                return Results.Failed<ColumnGenerationResult>(dualFailure.Error);
            }
            PricingDuals extractedDuals = ((Ok<PricingDuals, ErrorCode, Error<ErrorCode>>)duals).Value;

            var newRoutes = new List<Route<V>>();
            bool allComplete = true;
            foreach (VehicleType<V> vehicleType in _instance.VehicleTypes) {
                Result<PricingGraph<V>, ErrorCode, Error<ErrorCode>> graph = graphBuilder.Build(
                    vehicleType.Id, extractedDuals, node.BranchMask);
                if (graph is Failed<PricingGraph<V>, ErrorCode, Error<ErrorCode>> graphFailure) {
                    return Results.Failed<ColumnGenerationResult>(graphFailure.Error);
                }
                var request = new PricingRequest<V>(
                    _instance,
                    extractedDuals,
                    node.BranchMask,
                    _instance.Tolerances.Pricing,
                    vehicleType.Id,
                    _maxColumnsPerPricing,
                    _interruptionChecker);
                Result<PricingResult<V>, ErrorCode, Error<ErrorCode>> priced = pricer.Price(
                    ((Ok<PricingGraph<V>, ErrorCode, Error<ErrorCode>>)graph).Value, request);
                if (priced is Failed<PricingResult<V>, ErrorCode, Error<ErrorCode>> priceFailure) {
                    return Results.Failed<ColumnGenerationResult>(priceFailure.Error);
                }
                PricingResult<V> pricingResult =
                    ((Ok<PricingResult<V>, ErrorCode, Error<ErrorCode>>)priced).Value;
                newRoutes.AddRange(_pricingColumnSelector.Select(
                    pricingResult.Routes, _maxColumnsPerPricing));
                if (pricingResult.Interrupted) {
                    return Results.Ok(new ColumnGenerationResult(false, (int)iteration, true));
                }
                if (!pricingResult.ExactPricingComplete) {
                    allComplete = false;
                }
            }

            if (newRoutes.Count == 0 && allComplete) {
                return Results.Ok(new ColumnGenerationResult(true, (int)iteration, false));
            }
            if (newRoutes.Count > 0) {
                Result<IReadOnlyList<Route<V>>, ErrorCode, Error<ErrorCode>> added =
                    context.AddColumns(iteration, newRoutes, model);
                if (added is Failed<IReadOnlyList<Route<V>>, ErrorCode, Error<ErrorCode>> addFailure) {
                    return Results.Failed<ColumnGenerationResult>(addFailure.Error);
                }
            }
            iteration++;
        }
        return Results.Ok(new ColumnGenerationResult(false, _maxCGIterations, false));
    }

    /// <summary>构造失败结果 / Build a failure result.</summary>
    /// <typeparam name="T">结果类型 / The result type.</typeparam>
    /// <param name="message">错误信息 / The error message.</param>
    /// <returns>失败结果 / The failure result.</returns>
    private static Result<T, ErrorCode, Error<ErrorCode>> Failure<T>(string message) =>
        Results.Failed<T>(new Err<ErrorCode>(ErrorCode.IllegalArgument, message));

    /// <summary>
    /// 列生成循环的结果 / The outcome of one column-generation loop.
    /// </summary>
    /// <param name="PricingComplete">定价是否收敛 / Whether pricing converged.</param>
    /// <param name="Iterations">消耗的轮数 / The iterations consumed.</param>
    /// <param name="Interrupted">是否被中断 / Whether it was interrupted.</param>
    /// <param name="SolverStatus">非最优的求解状态 / A non-optimal solver status.</param>
    private sealed record ColumnGenerationResult(
        bool PricingComplete,
        int Iterations,
        bool Interrupted,
        SolverStatus? SolverStatus = null);
}
