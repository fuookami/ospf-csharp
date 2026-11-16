// VRPTW 分支定价算法 / The VRPTW branch-and-price algorithm.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Framework.NetworkScheduling.Application.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteCompilation;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteCompilation.Shadow;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteGeneration.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteGeneration.Policy;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Infrastructure;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Policy;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Service;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Application.Service;

/// <summary>
/// B&amp;P 求解结果 / The B&amp;P solve result.
/// </summary>
/// <remarks>
/// **`Status` 与两个界一起才说得清"求解到什么程度"**。单独看 `Status`，`Feasible` 既可能是"队列空了但 gap 没达标"，也可能是
/// "提前停止"；单独看 `UpperBound`，没有 `LowerBound` 就无法判断质量。
/// **`Status` only means something together with both bounds.** Seen alone, `Feasible` could mean "the queue emptied but the gap is
/// still open" or "stopped early"; seen alone, `UpperBound` says nothing about quality without `LowerBound`.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="Status">终态 / The terminal status.</param>
/// <param name="Solution">业务解 / The business solution.</param>
/// <param name="LowerBound">全局下界 / The global lower bound.</param>
/// <param name="UpperBound">全局上界 / The global upper bound.</param>
/// <param name="Trace">求解轨迹 / The solve trace.</param>
public sealed record BranchAndPriceResult<V>(
    BranchAndPriceStatus Status,
    VrptwSolution<V>? Solution,
    Flt64 LowerBound,
    Flt64 UpperBound,
    BranchAndPriceTrace Trace) where V : struct, IRealNumber<V>;

/// <summary>
/// VRPTW Branch-and-Price 算法 / The VRPTW branch-and-price algorithm.
/// </summary>
/// <remarks>
/// **用 best-bound 队列迭代处理节点，不递归**。递归会把节点深度和调用栈捆在一起，深分支直接爆栈，而失败的时机完全取决于数据规模。
/// **Nodes are processed iteratively with a best-bound queue, never recursively.** Recursion couples node depth to the call stack,
/// and deep branching blows the stack at a timing that depends entirely on the data size.
/// **未求解子节点继承父节点的 LP 下界作为有效下界**。若把未求解节点的下界当成 `-inf`，best-bound 顺序会退化成深度优先，剪枝也随之失效。
/// **An unsolved child inherits the parent's LP lower bound as its effective bound.** Treating it as `-inf` degenerates best-bound
/// ordering into depth-first and disables pruning.
/// **全局下界是所有活动节点有效下界的最小值**，不是"最近求解的那个节点"的下界。
/// **The global lower bound is the minimum effective bound over all active nodes**, not the bound of the most recently solved node.
/// **`TimeLimit` 与 `NodeLimit` 是可解释的正常终态**。即使还没有 incumbent，全局下界（可能是 `-inf`）依然是诚实的下界；把它们伪装成
/// `SolverStopped` 会让调用方误以为求解器异常停止。
/// **`TimeLimit` and `NodeLimit` are explainable normal terminal states.** Even without an incumbent the global bound (possibly
/// `-inf`) is still an honest bound; disguising them as `SolverStopped` would mislead the caller into thinking the solver crashed.
/// **非最优 LP 与未完成精确定价都必须失败**。它们没有最优性证书，用来更新下界会让分支定界在错误的下界上剪枝，而结果看起来完全正常。
/// **A non-optimal LP and incomplete exact pricing must both fail.** They carry no optimality certificate; using them to update the
/// bound would prune on a wrong bound while the result looks completely normal.
/// **assignment/edge 都是整数但路线变量仍是真分数 = 合同违例**，必须失败而不是随便选一个分支。那说明去重不变式失效或平行弧导致边聚合
/// 含糊，继续分下去得到的"最优解"没有意义。
/// **Integer assignment/edge with fractional route values is a contract violation**, which must fail rather than branch arbitrarily. It
/// means the deduplication invariant broke or parallel arcs made edge aggregation ambiguous, so any "optimum" reached by branching on
/// would be meaningless.
/// **与偏离**：C# 侧没有 `SolverProgressContext`，进度上报被省略；这不是能力缺口，而是本仓未实现进度上下文。
/// **Divergence**: there is no `SolverProgressContext` on the C# side, so progress reporting is omitted. That is
/// not a capability gap but an unimplemented progress context in this repository.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class BranchAndPriceAlgorithm<V> where V : struct, IFloatingNumber<V> {
    private readonly VrptwInstance<V> _instance;
    private readonly IColumnGenerationSolver _solver;
    private readonly Configuration _configuration;
    private readonly Policy _policy;

    /// <summary>
    /// 构造分支定价算法 / Construct the branch-and-price algorithm.
    /// </summary>
    /// <param name="instance">实例 / The instance.</param>
    /// <param name="solver">列生成求解器 / The column-generation solver.</param>
    /// <param name="configuration">算法配置 / The algorithm configuration.</param>
    /// <param name="policy">策略注入 / The policy injection.</param>
    public BranchAndPriceAlgorithm(
        VrptwInstance<V> instance,
        IColumnGenerationSolver solver,
        Configuration configuration,
        Policy policy) {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(solver);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(policy);
        _instance = instance;
        _solver = solver;
        _configuration = configuration;
        _policy = policy;
    }

    /// <summary>
    /// 求解 VRPTW / Solve the VRPTW.
    /// </summary>
    /// <returns>求解结果 / The solve result.</returns>
    public async Task<Result<BranchAndPriceResult<V>, ErrorCode, Error<ErrorCode>>> Solve() {
        var stopwatch = Stopwatch.StartNew();
        NetworkNodeId startDepot = _instance.StartDepot.Node.Id;
        NetworkNodeId endDepot = _instance.EndDepot.Node.Id;

        Result<BranchNode, ErrorCode, Error<ErrorCode>> root = BranchNode.Root(startDepot, endDepot);
        if (root is Failed<BranchNode, ErrorCode, Error<ErrorCode>> rootFailure) {
            return Results.Failed<BranchAndPriceResult<V>>(rootFailure.Error);
        }
        // best-bound 优先队列：有效下界最小者先处理。 / Best-bound queue: the smallest effective bound goes first.
        var queue = new List<BranchNode> { ((Ok<BranchNode, ErrorCode, Error<ErrorCode>>)root).Value };
        IReadOnlyList<Route<V>>? incumbent = null;
        Flt64? incumbentCost = null;
        var globalLowerBound = new Flt64(double.NegativeInfinity);
        int nodesExplored = 0;
        int nodesPruned = 0;
        int totalIterations = 0;
        int nodeIdCounter = 1;
        IReadOnlyList<Route<V>> columnPool = Array.Empty<Route<V>>();

        while (queue.Count > 0) {
            Flt64? queueMinimum = MinimumEffectiveLowerBound(queue);
            if (queueMinimum is { } minimum) {
                globalLowerBound = minimum;
            }
            TimeSpan elapsed = stopwatch.Elapsed;
            if (elapsed >= _configuration.TimeLimit) {
                return Results.Ok(AssembleResult(
                    BranchAndPriceStatus.TimeLimit,
                    incumbent,
                    incumbentCost,
                    globalLowerBound,
                    nodesExplored,
                    nodesPruned,
                    totalIterations,
                    elapsed));
            }
            if (nodesExplored >= _configuration.NodeLimit) {
                return Results.Ok(AssembleResult(
                    BranchAndPriceStatus.NodeLimit,
                    incumbent,
                    incumbentCost,
                    globalLowerBound,
                    nodesExplored,
                    nodesPruned,
                    totalIterations,
                    elapsed));
            }
            if (incumbentCost is { } incumbentValue
                && double.IsFinite(globalLowerBound.ToDouble())
                && !double.IsNegativeInfinity(globalLowerBound.ToDouble())) {
                Flt64 gap = RelativeGap(incumbentValue, globalLowerBound);
                if (gap.CompareTo(_configuration.GapTolerance) <= 0) {
                    return Results.Ok(AssembleResult(
                        BranchAndPriceStatus.Optimal,
                        incumbent,
                        incumbentCost,
                        globalLowerBound,
                        nodesExplored,
                        nodesPruned,
                        totalIterations,
                        elapsed));
                }
            }

            BranchNode node = PollBestBound(queue);
            if (!node.CanImprove(incumbentCost, _instance.Tolerances.Pricing)) {
                node.Prune();
                nodesPruned++;
                globalLowerBound = MinimumEffectiveLowerBound(queue) ?? incumbentCost ?? globalLowerBound;
                continue;
            }

            var nodeSolver = new BranchNodeSolver<V>(
                _instance,
                _solver,
                _policy.ValueAdapter,
                _policy.DistanceCalculator,
                _policy.TravelTimeCalculator,
                _policy.ArcCostCalculator,
                _policy.RouteCostPolicy,
                _policy.ArcFeasibilityPolicy,
                _policy.LabelDominancePolicy,
                _policy.PricingColumnSelector,
                _policy.ExtraPipelines,
                _configuration.MaxColumnsPerPricing,
                _configuration.MaxCGIterationsPerNode,
                new ElapsedInterruptionChecker(stopwatch, _configuration.TimeLimit),
                columnPool,
                _policy.ResourceGraph,
                _policy.ExtraContributionFactory);

            Result<NodeSolveResult<V>, ErrorCode, Error<ErrorCode>> solved =
                await nodeSolver.Solve(node);
            if (solved is Failed<NodeSolveResult<V>, ErrorCode, Error<ErrorCode>> solveFailure) {
                return Results.Failed<BranchAndPriceResult<V>>(solveFailure.Error);
            }
            NodeSolveResult<V> result = ((Ok<NodeSolveResult<V>, ErrorCode, Error<ErrorCode>>)solved).Value;
            nodesExplored++;
            totalIterations += result.Iterations;
            // 列池按签名去重：同一路线可能被多个节点重新发现。 / De-duplicate the pool by signature: several nodes may rediscover
            // the same route.
            var merged = new List<Route<V>>(columnPool);
            merged.AddRange(result.Columns);
            columnPool = merged
                .GroupBy(route => route.Signature, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToList();

            if (result.TimeLimitReached) {
                Flt64? queueBound = MinimumEffectiveLowerBound(queue);
                var candidates = new List<Flt64> { globalLowerBound, node.InheritedLowerBound };
                if (queueBound is { } bound) {
                    candidates.Add(bound);
                }
                return Results.Ok(AssembleResult(
                    BranchAndPriceStatus.TimeLimit,
                    incumbent,
                    incumbentCost,
                    candidates.Min(),
                    nodesExplored,
                    nodesPruned,
                    totalIterations,
                    stopwatch.Elapsed));
            }
            if (result.SolverStatus == SolverStatus.Infeasible) {
                // 外部 LP 只证明当前节点不可行；其它活动节点仍需继续处理。
                // The external LP only proves this node infeasible; other active nodes remain viable.
                node.MarkInfeasible();
                globalLowerBound = MinimumEffectiveLowerBound(queue)
                    ?? incumbentCost
                    ?? new Flt64(double.PositiveInfinity);
                continue;
            }
            if (result.SolverStatus != SolverStatus.Optimal) {
                return Failure<BranchAndPriceResult<V>>(
                    $"节点 {node.Id} 的 LP 状态为 {result.SolverStatus}，未提供精确最优性证书 / "
                    + $"LP at node {node.Id} ended with {result.SolverStatus} without an exact optimality certificate");
            }
            if (!result.PricingComplete) {
                return Failure<BranchAndPriceResult<V>>(
                    $"节点 {node.Id} 未完成精确定价，不能更新全局下界 / "
                    + $"Exact pricing at node {node.Id} is incomplete and cannot update the global lower bound");
            }
            if (!result.IsFeasible) {
                globalLowerBound = MinimumEffectiveLowerBound(queue)
                    ?? incumbentCost
                    ?? new Flt64(double.PositiveInfinity);
                continue;
            }

            Flt64? nextQueueMinimum = MinimumEffectiveLowerBound(queue);
            globalLowerBound = nextQueueMinimum is { } next ? Flt64Min(result.LowerBound, next) : result.LowerBound;

            Try traced = _policy.TraceListener(new BranchAndPriceTrace(
                nodesExplored,
                nodesPruned,
                globalLowerBound,
                incumbentCost ?? new Flt64(double.PositiveInfinity),
                incumbentCost is { } upperBoundValue
                    && double.IsFinite(globalLowerBound.ToDouble())
                        ? RelativeGap(upperBoundValue, globalLowerBound)
                        : new Flt64(double.PositiveInfinity),
                stopwatch.Elapsed,
                totalIterations));
            if (traced is Failed<Success, ErrorCode, Error<ErrorCode>> traceFailure) {
                return Results.Failed<BranchAndPriceResult<V>>(traceFailure.Error);
            }

            if (result.IsInteger) {
                IReadOnlyList<Route<V>> selected = result.RouteValues
                    .Where(pair => pair.Value.CompareTo(
                        Flt64.One.Minus(_instance.Tolerances.Integrality)) >= 0)
                    .Select(pair => pair.Route)
                    .ToList();
                if (selected.Count > 0) {
                    Flt64 cost = ComputeSolutionCost(selected);
                    if (incumbentCost is null || cost.CompareTo(incumbentCost.Value) < 0) {
                        // RouteValidator 复核：整数解也必须逐条合法。 / RouteValidator re-checks every route even for an integer
                        // solution.
                        bool validated = selected.All(route => RouteValidator.Validate(
                            _instance,
                            route,
                            _policy.DistanceCalculator,
                            _policy.TravelTimeCalculator,
                            _policy.ArcCostCalculator,
                            _policy.RouteCostPolicy,
                            _policy.ValueAdapter).IsOk);
                        if (validated) {
                            incumbent = selected;
                            incumbentCost = cost;
                            var removable = queue
                                .Where(candidate => !candidate.CanImprove(
                                    incumbentCost, _instance.Tolerances.Pricing))
                                .ToList();
                            foreach (BranchNode candidate in removable) {
                                queue.Remove(candidate);
                            }
                            nodesPruned += removable.Count;
                        }
                    }
                }
            } else if (incumbentCost is null || result.LowerBound.CompareTo(incumbentCost.Value) < 0) {
                IBranchDecision? decision = SelectBranchDecision(
                    result.RouteValues, _instance, _instance.Tolerances.Integrality);
                if (decision is null) {
                    return Failure<BranchAndPriceResult<V>>(
                        $"Contract violation at node {node.Id}: assignment and edge values are integer "
                        + "but route variables remain fractional. This indicates a dedup invariant failure "
                        + "or parallel arcs causing ambiguous edge aggregation.");
                }
                IBranchDecision complementary = ComplementaryDecision(decision);
                Result<BranchNode, ErrorCode, Error<ErrorCode>> leftChild =
                    BranchNode.Child(nodeIdCounter++, node, decision, startDepot, endDepot);
                if (leftChild is Failed<BranchNode, ErrorCode, Error<ErrorCode>> leftFailure) {
                    return Results.Failed<BranchAndPriceResult<V>>(leftFailure.Error);
                }
                Result<BranchNode, ErrorCode, Error<ErrorCode>> rightChild =
                    BranchNode.Child(nodeIdCounter++, node, complementary, startDepot, endDepot);
                if (rightChild is Failed<BranchNode, ErrorCode, Error<ErrorCode>> rightFailure) {
                    return Results.Failed<BranchAndPriceResult<V>>(rightFailure.Error);
                }
                queue.Add(((Ok<BranchNode, ErrorCode, Error<ErrorCode>>)leftChild).Value);
                queue.Add(((Ok<BranchNode, ErrorCode, Error<ErrorCode>>)rightChild).Value);
            }

            Flt64? activeBound = MinimumEffectiveLowerBound(queue);
            Flt64 closedBound = incumbentCost ?? result.LowerBound;
            globalLowerBound = activeBound is { } active ? Flt64Min(active, closedBound) : closedBound;
        }

        TimeSpan finalElapsed = stopwatch.Elapsed;
        BranchAndPriceStatus finalStatus;
        if (incumbent is null) {
            finalStatus = BranchAndPriceStatus.Infeasible;
        } else if (double.IsFinite(globalLowerBound.ToDouble())
            && !double.IsNegativeInfinity(globalLowerBound.ToDouble())) {
            Flt64 finalGap = RelativeGap(incumbentCost!.Value, globalLowerBound);
            finalStatus = finalGap.CompareTo(_configuration.GapTolerance) <= 0
                ? BranchAndPriceStatus.Optimal
                : BranchAndPriceStatus.Feasible;
        } else {
            finalStatus = BranchAndPriceStatus.Feasible;
        }
        return Results.Ok(AssembleResult(
            finalStatus,
            incumbent,
            incumbentCost,
            globalLowerBound,
            nodesExplored,
            nodesPruned,
            totalIterations,
            finalElapsed));
    }

    /// <summary>
    /// 选择分支决策 / Select the branch decision.
    /// </summary>
    /// <remarks>
    /// **先看 assignment，再看 edge**。先看 edge 会在同一客户可从多个车型服务时分到一条毫无意义的弧上，而 assignment 层面的分支恰好
    /// 覆盖了这种歧义。
    /// **assignment is checked before edge.** Checking edges first would branch on a meaningless arc whenever the same customer can be
    /// served by several vehicle types, while an assignment-level branch covers exactly that ambiguity.
    /// **返回值 null 有两种含义**：完全整数（无需分支），或合同违例（assignment/edge 整数但路线变量仍是真分数）。调用方必须能区分，
    /// 因此违例在 `Solve` 里以失败返回，而不是由这里返回 null。
    /// **A null return has two meanings**: fully integral (nothing to branch on) or a contract violation (integer assignment/edge with
    /// fractional route values). The caller must distinguish them, so the violation is surfaced as a failure in `Solve` rather than
    /// as another null here.
    /// </remarks>
    /// <param name="routeValues">路线变量值 / The route variable values.</param>
    /// <param name="instance">实例 / The instance.</param>
    /// <param name="integralityTolerance">整数容差 / The integrality tolerance.</param>
    /// <returns>分支决策；完全整数时为 null / The branch decision, or null when fully integral.</returns>
    public IBranchDecision? SelectBranchDecision(
        IReadOnlyList<(Route<V> Route, Flt64 Value)> routeValues,
        VrptwInstance<V> instance,
        Flt64 integralityTolerance) {
        ArgumentNullException.ThrowIfNull(routeValues);
        ArgumentNullException.ThrowIfNull(instance);
        var assignmentValue = new Dictionary<(VehicleTypeId Vehicle, CustomerId Customer), Flt64>();
        foreach ((Route<V> route, Flt64 value) in routeValues) {
            foreach (RouteStop<V> stop in route.Stops) {
                if (stop.CustomerId is not { } customerId) {
                    continue;
                }
                (VehicleTypeId Vehicle, CustomerId Customer) key = (route.VehicleTypeId, customerId);
                assignmentValue[key] = assignmentValue.TryGetValue(key, out Flt64 existing)
                    ? existing.Plus(value)
                    : value;
            }
        }
        (VehicleTypeId Vehicle, CustomerId Customer, NetworkNodeId Node)? bestAssignment = null;
        Flt64 bestFractionality = Flt64.One;
        foreach (((VehicleTypeId vehicle, CustomerId customer) key, Flt64 value) in assignmentValue) {
            if (value.CompareTo(integralityTolerance) <= 0
                || value.CompareTo(Flt64.One.Minus(integralityTolerance)) >= 0) {
                continue;
            }
            Flt64 fractionality = Fractionality(value);
            if (fractionality.CompareTo(bestFractionality) < 0) {
                bestFractionality = fractionality;
                if (!instance.CustomerById.TryGetValue(key.customer, out Customer<V>? customer)) {
                    continue;
                }
                bestAssignment = (key.vehicle, key.customer, customer.Node.Id);
            }
        }
        if (bestAssignment is { } assignment) {
            return new ForbidVehicleType(
                assignment.Vehicle, assignment.Customer, assignment.Node);
        }

        var edgeValue = new Dictionary<(VehicleTypeId, NetworkArcId, NetworkNodeId, NetworkNodeId), Flt64>();
        foreach ((Route<V> route, Flt64 value) in routeValues) {
            IReadOnlyList<NetworkNodeId> path = route.Stops.Select(stop => stop.NodeId).ToList();
            for (int index = 0; index < path.Count - 1; index++) {
                (VehicleTypeId, NetworkArcId, NetworkNodeId, NetworkNodeId) key =
                    (route.VehicleTypeId, route.ArcIds[index], path[index], path[index + 1]);
                edgeValue[key] = edgeValue.TryGetValue(key, out Flt64 existing)
                    ? existing.Plus(value)
                    : value;
            }
        }
        (VehicleTypeId, NetworkArcId, NetworkNodeId, NetworkNodeId)? bestArc = null;
        Flt64 bestArcFractionality = Flt64.One;
        foreach (((VehicleTypeId, NetworkArcId, NetworkNodeId, NetworkNodeId) key, Flt64 value) in edgeValue) {
            if (value.CompareTo(integralityTolerance) <= 0
                || value.CompareTo(Flt64.One.Minus(integralityTolerance)) >= 0) {
                continue;
            }
            Flt64 fractionality = Fractionality(value);
            if (fractionality.CompareTo(bestArcFractionality) < 0) {
                bestArcFractionality = fractionality;
                bestArc = key;
            }
        }
        if (bestArc is { } arc) {
            return new ForbidArc(arc.Item1, arc.Item3, arc.Item4, arc.Item2);
        }
        return null;
    }

    /// <summary>
    /// 获取互补分支决策 / Get the complementary branch decision.
    /// </summary>
    /// <param name="decision">分支决策 / The branch decision.</param>
    /// <returns>互补决策 / The complementary decision.</returns>
    public static IBranchDecision ComplementaryDecision(IBranchDecision decision) {
        ArgumentNullException.ThrowIfNull(decision);
        return decision switch {
            ForbidVehicleType forbid => new RequireVehicleType(
                forbid.VehicleTypeId, forbid.CustomerId, forbid.CustomerNodeId),
            RequireVehicleType require => new ForbidVehicleType(
                require.VehicleTypeId, require.CustomerId, require.CustomerNodeId),
            ForbidArc forbid => new RequireArc(
                forbid.VehicleTypeId, forbid.From, forbid.To, forbid.ArcId),
            RequireArc require => new ForbidArc(
                require.VehicleTypeId, require.From, require.To, require.ArcId),
            _ => throw new ArgumentOutOfRangeException(
                nameof(decision), decision, "未知的分支决策类型 / Unknown branch decision kind"),
        };
    }

    /// <summary>
    /// B&amp;P 配置 / The B&amp;P configuration.
    /// </summary>
    /// <param name="TimeLimit">时间上限 / The time limit.</param>
    /// <param name="NodeLimit">节点上限 / The node limit.</param>
    /// <param name="RelativeGapTolerance">相对间隙容差 / The relative-gap tolerance.</param>
    /// <param name="MaxColumnsPerPricing">单次定价的列数上限 / The per-pricing column cap.</param>
    /// <param name="MaxCGIterationsPerNode">单节点列生成迭代上限 / The per-node CG iteration cap.</param>
    public sealed record Configuration(
        TimeSpan TimeLimit,
        int NodeLimit = int.MaxValue,
        Flt64? RelativeGapTolerance = null,
        int MaxColumnsPerPricing = int.MaxValue,
        int MaxCGIterationsPerNode = 1000) {
        /// <summary>实际使用的相对间隙容差 / The effective relative-gap tolerance.</summary>
        public Flt64 GapTolerance => RelativeGapTolerance ?? new Flt64(1e-4);
    }

    /// <summary>
    /// B&amp;P 策略注入 / The B&amp;P policy injection.
    /// </summary>
    /// <remarks>
    /// **`ExtraContributionFactory` 必须为每个分支节点模型返回不共享的实例**。共享实例会把一个节点的约束结构带进另一个节点，而这种污染
    /// 只表现为"下界偏高"，不会报错。
    /// **`ExtraContributionFactory` must return unshared instances for each branch-node model.** A shared instance would carry one
    /// node's constraint structure into another, and that contamination only shows up as "the bound is high", never as an error.
    /// </remarks>
    /// <param name="ValueAdapter">数值边界 / The numeric boundary.</param>
    /// <param name="DistanceCalculator">距离计算 / The distance calculator.</param>
    /// <param name="TravelTimeCalculator">行程时间计算 / The travel-time calculator.</param>
    /// <param name="ArcCostCalculator">弧成本计算 / The arc-cost calculator.</param>
    /// <param name="RouteCostPolicy">路线成本策略 / The route-cost policy.</param>
    /// <param name="ArcFeasibilityPolicy">弧可行性策略 / The arc-feasibility policy.</param>
    /// <param name="LabelDominancePolicy">标签支配策略 / The label-dominance policy.</param>
    /// <param name="PricingColumnSelector">定价列选择器 / The pricing column selector.</param>
    /// <param name="TraceListener">轨迹监听器 / The trace listener.</param>
    /// <param name="ExtraPipelines">额外管线 / Extra pipelines.</param>
    /// <param name="ExtraContributionFactory">额外贡献工厂 / The extra-contribution factory.</param>
    /// <param name="ResourceGraph">资源图 / The resource graph.</param>
    public sealed record Policy(
        INetworkSchedulingSolverValueAdapter<V> ValueAdapter,
        IDistanceCalculator<V> DistanceCalculator,
        ITravelTimeCalculator<V> TravelTimeCalculator,
        IArcCostCalculator<V> ArcCostCalculator,
        IRouteCostPolicy<V> RouteCostPolicy,
        IArcFeasibilityPolicy<V>? ArcFeasibilityPolicy = null,
        ILabelDominancePolicy? LabelDominancePolicy = null,
        IPricingColumnSelector<V>? PricingColumnSelector = null,
        BranchAndPriceTraceListener? TraceListener = null,
        IReadOnlyList<ICGPipeline<VrpShadowPriceArguments, LinearMetaModel<Flt64>, VrpShadowPriceMap>>?
            ExtraPipelines = null,
        Func<VrptwInstance<V>, LinearMetaModel<Flt64>, IReadOnlyList<IRouteCompilationContribution<V>>>?
            ExtraContributionFactory = null,
        ResourceGraph<VehicleTypeId>? ResourceGraph = null) {
        /// <summary>轨迹监听器 / The trace listener.</summary>
        public BranchAndPriceTraceListener TraceListener { get; init; } =
            TraceListener ?? BranchAndPriceTraceListeners.None;

        /// <summary>额外管线 / Extra pipelines.</summary>
        public IReadOnlyList<ICGPipeline<VrpShadowPriceArguments, LinearMetaModel<Flt64>, VrpShadowPriceMap>>
            ExtraPipelines { get; init; } =
            ExtraPipelines
            ?? Array.Empty<ICGPipeline<VrpShadowPriceArguments, LinearMetaModel<Flt64>, VrpShadowPriceMap>>();

        /// <summary>额外贡献工厂 / The extra-contribution factory.</summary>
        public Func<VrptwInstance<V>, LinearMetaModel<Flt64>, IReadOnlyList<IRouteCompilationContribution<V>>>
            ExtraContributionFactory { get; init; } =
            ExtraContributionFactory
            ?? ((_, _) => Array.Empty<IRouteCompilationContribution<V>>());

        /// <summary>弧可行性策略 / The arc-feasibility policy.</summary>
        public IArcFeasibilityPolicy<V> ArcFeasibilityPolicy { get; init; } =
            ArcFeasibilityPolicy ?? new DefaultArcFeasibilityPolicy<V>();

        /// <summary>标签支配策略 / The label-dominance policy.</summary>
        public ILabelDominancePolicy LabelDominancePolicy { get; init; } =
            LabelDominancePolicy ?? DefaultLabelDominancePolicy.Instance;

        /// <summary>定价列选择器 / The pricing column selector.</summary>
        public IPricingColumnSelector<V> PricingColumnSelector { get; init; } =
            PricingColumnSelector ?? new KeepAllColumnSelector<V>();
    }

    /// <summary>
    /// 计算解的总成本 / Compute the total solution cost.
    /// </summary>
    /// <param name="routes">路线 / The routes.</param>
    /// <returns>总成本 / The total cost.</returns>
    private Flt64 ComputeSolutionCost(IReadOnlyList<Route<V>> routes) {
        Flt64 total = Flt64.Zero;
        foreach (Route<V> route in routes) {
            Result<Flt64, ErrorCode, Error<ErrorCode>> cost =
                _policy.ValueAdapter.Normalize(route.Cost, _instance.Units.CostUnit);
            // 单条路线换算失败按零计——这，但总成本因此只是一个下估计。
 // A failed per-route conversion counts as zero.
            total = total.Plus(cost.IsOk ? ((Ok<Flt64, ErrorCode, Error<ErrorCode>>)cost).Value : Flt64.Zero);
        }
        return total;
    }

    /// <summary>
    /// 组装最终结果 / Assemble the final result.
    /// </summary>
    /// <param name="status">终态 / The terminal status.</param>
    /// <param name="incumbent">当前最好解 / The incumbent.</param>
    /// <param name="incumbentCost">当前最好解费用 / The incumbent cost.</param>
    /// <param name="globalLowerBound">全局下界 / The global lower bound.</param>
    /// <param name="nodesExplored">已处理节点数 / Nodes explored.</param>
    /// <param name="nodesPruned">已剪枝节点数 / Nodes pruned.</param>
    /// <param name="totalIterations">总迭代数 / Total iterations.</param>
    /// <param name="elapsed">耗时 / The elapsed time.</param>
    /// <returns>求解结果 / The solve result.</returns>
    private BranchAndPriceResult<V> AssembleResult(
        BranchAndPriceStatus status,
        IReadOnlyList<Route<V>>? incumbent,
        Flt64? incumbentCost,
        Flt64 globalLowerBound,
        int nodesExplored,
        int nodesPruned,
        int totalIterations,
        TimeSpan elapsed) {
        Flt64 upperBound = incumbentCost ?? new Flt64(double.PositiveInfinity);
        Flt64 relativeGap = incumbentCost is { } cost
            && double.IsFinite(globalLowerBound.ToDouble())
            && !double.IsNegativeInfinity(globalLowerBound.ToDouble())
                ? RelativeGap(cost, globalLowerBound)
                : new Flt64(double.PositiveInfinity);
        VrptwSolution<V>? solution = null;
        if (incumbent is { Count: > 0 }) {
            PhysicalUnit distanceUnit = _instance.Units.DistanceUnit;
            PhysicalUnit costUnit = _instance.Units.CostUnit;
            var totalDistance = new Quantity<V>(SumValues(incumbent, route => route.Distance, distanceUnit), distanceUnit);
            var totalCost = new Quantity<V>(SumValues(incumbent, route => route.Cost, costUnit), costUnit);
            solution = new VrptwSolution<V>(incumbent, totalDistance, totalCost);
        }
        return new BranchAndPriceResult<V>(
            status,
            solution,
            globalLowerBound,
            upperBound,
            new BranchAndPriceTrace(
                nodesExplored,
                nodesPruned,
                globalLowerBound,
                upperBound,
                relativeGap,
                elapsed,
                totalIterations));
    }

    /// <summary>
    /// 累加路线上的一个物理量 / Sum one quantity over the routes.
    /// </summary>
    /// <param name="routes">路线 / The routes.</param>
    /// <param name="selector">取值器 / The selector.</param>
    /// <param name="unit">目标单位 / The target unit.</param>
    /// <returns>累加值 / The summed value.</returns>
    private static V SumValues(
        IReadOnlyList<Route<V>> routes,
        Func<Route<V>, Quantity<V>> selector,
        PhysicalUnit unit) {
        V total = default;
        bool seeded = false;
        foreach (Route<V> route in routes) {
            Quantity<V> quantity = selector(route);
            if (!quantity.Unit.SameDimension(unit)) {
                continue;
            }
            total = seeded ? total.Plus(quantity.Value) : quantity.Value;
            seeded = true;
        }
        return total;
    }

    /// <summary>
    /// 取队列中最小有效下界 / The minimum effective bound in the queue.
    /// </summary>
    /// <param name="queue">节点队列 / The node queue.</param>
    /// <returns>最小有效下界，或队列为空时的 null / The minimum effective bound, or null when empty.</returns>
    private static Flt64? MinimumEffectiveLowerBound(IReadOnlyList<BranchNode> queue) {
        if (queue.Count == 0) {
            return null;
        }
        Flt64 minimum = queue[0].EffectiveLowerBound;
        for (int index = 1; index < queue.Count; index++) {
            if (queue[index].EffectiveLowerBound.CompareTo(minimum) < 0) {
                minimum = queue[index].EffectiveLowerBound;
            }
        }
        return minimum;
    }

    /// <summary>
    /// 取出有效下界最小的节点 / Remove the node with the smallest effective bound.
    /// </summary>
    /// <param name="queue">节点队列 / The node queue.</param>
    /// <returns>该节点 / That node.</returns>
    private static BranchNode PollBestBound(List<BranchNode> queue) {
        int bestIndex = 0;
        for (int index = 1; index < queue.Count; index++) {
            if (queue[index].EffectiveLowerBound.CompareTo(queue[bestIndex].EffectiveLowerBound) < 0) {
                bestIndex = index;
            }
        }
        BranchNode node = queue[bestIndex];
        queue.RemoveAt(bestIndex);
        return node;
    }

    /// <summary>取两个下界中的较小者 / The smaller of two bounds.</summary>
    private static Flt64 Flt64Min(Flt64 lhs, Flt64 rhs) => lhs.CompareTo(rhs) <= 0 ? lhs : rhs;

    /// <summary>
    /// 相对间隙 / The relative gap.
    /// </summary>
    /// <param name="upperBound">上界 / The upper bound.</param>
    /// <param name="lowerBound">下界 / The lower bound.</param>
    /// <returns>相对间隙 / The relative gap.</returns>
    private static Flt64 RelativeGap(Flt64 upperBound, Flt64 lowerBound) {
        Flt64 denominator = Flt64.One.CompareTo(upperBound.Abs()) >= 0 ? Flt64.One : upperBound.Abs();
        return (upperBound.Minus(lowerBound)) / denominator;
    }

    /// <summary>
    /// 变量值到 0.5 的距离 / The distance from a variable value to 0.5.
    /// </summary>
    /// <param name="value">变量值 / The variable value.</param>
    /// <returns>分数性 / The fractionality.</returns>
    private static Flt64 Fractionality(Flt64 value) =>
        value.Minus(new Flt64(0.5)).Abs();

    /// <summary>构造失败结果 / Build a failure result.</summary>
    /// <typeparam name="T">结果类型 / The result type.</typeparam>
    /// <param name="message">错误信息 / The error message.</param>
    /// <returns>失败结果 / The failure result.</returns>
    private static Result<T, ErrorCode, Error<ErrorCode>> Failure<T>(string message) =>
        Results.Failed<T>(new Err<ErrorCode>(ErrorCode.IllegalArgument, message));

    /// <summary>
    /// 基于已耗时的中断检查器 / An interruption checker driven by elapsed time.
    /// </summary>
    private sealed class ElapsedInterruptionChecker : IPricingInterruptionChecker {
        private readonly Stopwatch _stopwatch;
        private readonly TimeSpan _timeLimit;

        /// <summary>
        /// 构造中断检查器 / Construct the interruption checker.
        /// </summary>
        /// <param name="stopwatch">计时器 / The stopwatch.</param>
        /// <param name="timeLimit">时间上限 / The time limit.</param>
        public ElapsedInterruptionChecker(Stopwatch stopwatch, TimeSpan timeLimit) {
            _stopwatch = stopwatch;
            _timeLimit = timeLimit;
        }

        /// <inheritdoc/>
        public bool ShouldStop() => _stopwatch.Elapsed >= _timeLimit;
    }
}
