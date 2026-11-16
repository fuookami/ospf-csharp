// ESPPRC 精确定价器 / ESPPRC exact pricer.
//
// 定价 = 在定价图上做带资源约束的标签扩展，把"是否存在负 reduced cost 的列"这个问题回答到底。本实现支持：单路线 elementarity、
// reduced cost／时间／负载三资源支配、基于 elementarity 与容量单调性的保守客户排除、前驱链回溯生成完整 `Route`，
// 以及分支遮罩（在构图与扩展两处都生效）。
// Pricing = resource-constrained label extension on the pricing graph, answering "does a column with negative reduced cost exist"
// completely. This implementation supports single-route elementarity, three-resource dominance on reduced cost / time / load,
// conservative customer exclusion driven by elementarity and monotone capacity, predecessor-chain backtracking into a complete
// `Route`, and branch masks (applied both at graph construction and during extension).

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Path;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteGeneration.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteGeneration.Policy;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Policy;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Pricing;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteGeneration.Service;

/// <summary>
/// ESPPRC 精确定价器 / ESPPRC exact pricer.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class EspprcPricer<V>
    where V : struct, IFloatingNumber<V> {
    private readonly VrptwInstance<V> _instance;
    private readonly INetworkSchedulingSolverValueAdapter<V> _valueAdapter;
    private readonly ILabelDominancePolicy _dominancePolicy;
    private readonly IRouteCostPolicy<V> _routeCostPolicy;

    /// <summary>
    /// 构造定价器 / Construct the pricer.
    /// </summary>
    /// <param name="instance">VRPTW 实例 / VRPTW instance.</param>
    /// <param name="valueAdapter">solver 数值适配器 / Solver value adapter.</param>
    /// <param name="dominancePolicy">标签支配策略 / Label dominance policy.</param>
    /// <param name="routeCostPolicy">路线成本策略 / Route-cost policy.</param>
    public EspprcPricer(
        VrptwInstance<V> instance,
        INetworkSchedulingSolverValueAdapter<V> valueAdapter,
        ILabelDominancePolicy? dominancePolicy = null,
        IRouteCostPolicy<V>? routeCostPolicy = null) {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(valueAdapter);
        _instance = instance;
        _valueAdapter = valueAdapter;
        _dominancePolicy = dominancePolicy ?? DefaultLabelDominancePolicy.Instance;
        _routeCostPolicy = routeCostPolicy ?? new FixedPlusArcCostPolicy<V>(instance.Units.CostUnit);
    }

    /// <summary>
    /// 执行精确定价 / Execute exact pricing.
    /// </summary>
    /// <remarks>
    /// **支配剪枝只在可加成本 + 已认证支配时才启用**。非可加的路线成本策略下，reduced cost 不能逐弧累加，中途的支配比较会用
    /// 错误的数值剪掉本可通向最优解的标签；未认证的支配策略同样不可信。两者任一不满足，就退化为不剪枝的穷尽搜索——慢，但结论
    /// 可信。
    /// **Dominance pruning is enabled only with an additive cost policy and certified dominance.** Under a non-additive route-cost
    /// policy the reduced cost is not arc-decomposable, and mid-route dominance would prune labels that could still reach the
    /// optimum using wrong numbers; an uncertified dominance policy is no more trustworthy. If either condition fails, pricing
    /// degrades to exhaustive search — slower, but trustworthy.
    /// </remarks>
    /// <param name="graph">定价图 / Pricing graph.</param>
    /// <param name="request">定价请求 / Pricing request.</param>
    /// <returns>定价结果 / The pricing result.</returns>
    public Result<PricingResult<V>, ErrorCode, Error<ErrorCode>> Price(
        PricingGraph<V> graph,
        PricingRequest<V> request) {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(request);

        Result<V, ErrorCode, Error<ErrorCode>> toleranceResult =
            _valueAdapter.FromSolverValue(request.PricingTolerance);
        if (toleranceResult is Failed<V, ErrorCode, Error<ErrorCode>> toleranceFailure) {
            return NetworkSchedulingErrors.Failure<PricingResult<V>>(
                "执行定价失败：定价容差转换失败 / Failed to execute pricing: pricing tolerance conversion failed");
        }
        V pricingTolerance = toleranceResult.Value;
        int customerCount = graph.CustomerCount;
        int endDepotIndex = graph.EndDepotIndex;
        V zero = graph.VehicleCapacity.Constants.Zero;
        V negativePricingTolerance = zero.Minus(pricingTolerance);

        Result<NetworkGraph<PricingNode<V>, PricingArc<V>>, ErrorCode, Error<ErrorCode>> graphResult =
            NetworkGraph<PricingNode<V>, PricingArc<V>>.Invoke(
                graph.Nodes,
                graph.Arcs,
                new NetworkGraphAccess<PricingNode<V>, PricingArc<V>>(
                    NodeId: node => node.NodeId,
                    ArcId: arc => arc.Id,
                    From: arc => arc.FromNodeId,
                    To: arc => arc.ToNodeId));
        if (graphResult is Failed<NetworkGraph<PricingNode<V>, PricingArc<V>>, ErrorCode, Error<ErrorCode>> graphFailure) {
            return Results.Failed<PricingResult<V>>(graphFailure.Error);
        }

        PricingNode<V> start = graph.Nodes[graph.StartDepotIndex];
        var root = new EspprcLabel<V>(
            zero, start.ReadyTime, zero, start.NodeId,
            VisitedCustomers.Empty(customerCount),
            ForbiddenCustomers.Empty(customerCount),
            -1,
            -1,
            Array.Empty<Quantity<V>>(),
            zero);

        Func<PathLabel<EspprcLabel<V>, PricingArc<V>>,
            PathLabel<EspprcLabel<V>, PricingArc<V>>, bool>? exactDominance =
            _routeCostPolicy.IsAdditive && _dominancePolicy.IsExact
                ? (left, right) => _dominancePolicy.Dominates(left.State, right.State)
                : null;

        var problem = new PathProblem<PricingNode<V>, PricingArc<V>, EspprcLabel<V>>(
            Graph: graphResult.Value,
            Source: start.NodeId,
            Initial: root,
            Transition: (previous, arc) => Transition(graph, previous, arc),
            Terminal: (node, state) => node.Equals(graph.Nodes[endDepotIndex].NodeId),
            Accept: state => state.ReducedCost.CompareTo(negativePricingTolerance) < 0,
            Elementary: true,
            Dominates: exactDominance,
            DominanceIsExact: exactDominance is not null,
            Order: Comparer<EspprcLabel<V>>.Create(
                (left, right) => left.ReducedCost.CompareTo(right.ReducedCost)),
            TerminalContribution: terminal => TerminalContribution(terminal, request, zero));

        Result<PathSearchResult<EspprcLabel<V>, PricingArc<V>>, ErrorCode, Error<ErrorCode>> searchResult =
            new PathSearchContext().Search(
                problem,
                new PathSearchBudget(ShouldStop: () => request.InterruptionChecker.ShouldStop()));
        if (searchResult is Failed<PathSearchResult<EspprcLabel<V>, PricingArc<V>>, ErrorCode, Error<ErrorCode>>
            searchFailure) {
            return Results.Failed<PricingResult<V>>(searchFailure.Error);
        }
        PathSearchResult<EspprcLabel<V>, PricingArc<V>> search = searchResult.Value;

        V minReducedCost = zero;
        foreach (PathLabel<EspprcLabel<V>, PricingArc<V>> found in search.Paths) {
            if (found.State.ReducedCost.CompareTo(minReducedCost) < 0) {
                minReducedCost = found.State.ReducedCost;
            }
        }
        bool interrupted = search.Completion == PathSearchCompletion.Interrupted;
        bool exactPricingComplete = search.Complete
            && minReducedCost.CompareTo(negativePricingTolerance) >= 0;

        var routes = new List<Route<V>>();
        foreach (PathLabel<EspprcLabel<V>, PricingArc<V>> pathLabel in search.Paths) {
            if (routes.Count >= request.MaxColumnsPerPricing) {
                break;
            }
            Result<Route<V>, ErrorCode, Error<ErrorCode>> route =
                BacktrackRoute(search.Trace(pathLabel), graph, request);
            if (route is Failed<Route<V>, ErrorCode, Error<ErrorCode>> routeFailure) {
                return Results.Failed<PricingResult<V>>(routeFailure.Error);
            }
            routes.Add(route.Value);
        }

        return Results.Ok(new PricingResult<V>(
            routes,
            minReducedCost,
            exactPricingComplete,
            interrupted));
    }

    /// <summary>
    /// 单步资源转移 / A single resource transition.
    /// </summary>
    /// <param name="graph">定价图 / Pricing graph.</param>
    /// <param name="previous">前驱标签 / The previous label.</param>
    /// <param name="arc">到达弧 / The incoming arc.</param>
    /// <returns>新状态，不可行时为 null / The new state, or null when infeasible.</returns>
    private static Result<EspprcLabel<V>?, ErrorCode, Error<ErrorCode>> Transition(
        PricingGraph<V> graph,
        PathLabel<EspprcLabel<V>, PricingArc<V>> previous,
        PricingArc<V> arc) {
        EspprcLabel<V> current = previous.State;
        PricingNode<V> next = graph.Nodes[arc.ToIndex];

        if (!next.IsDepot
            && (current.Visited.Contains(next.CustomerIndex) || current.Forbidden.Contains(next.CustomerIndex))) {
            return Results.Ok<EspprcLabel<V>?>(null);
        }

        V serviceStart = MaxOf(current.Time.Plus(arc.TravelTime), next.ReadyTime);
        V load = current.Load.Plus(next.Demand);
        if (serviceStart.CompareTo(next.DueTime) > 0 || load.CompareTo(graph.VehicleCapacity) > 0) {
            return Results.Ok<EspprcLabel<V>?>(null);
        }

        VisitedCustomers visited = next.IsDepot ? current.Visited : current.Visited.Add(next.CustomerIndex);
        V departure = serviceStart.Plus(next.ServiceTime);
        var arcCosts = new List<Quantity<V>>(current.ArcCosts) { arc.ArcCost };
        V additive = (current.AdditiveObjectiveCost ?? current.Time.Constants.Zero).Plus(arc.ObjectiveCost);

        return Results.Ok<EspprcLabel<V>?>(new EspprcLabel<V>(
            current.ReducedCost.Plus(arc.ReducedCost),
            departure,
            load,
            next.NodeId,
            visited,
            UpdateForbiddenCustomers(current.Forbidden, next, visited, graph, load),
            previous.Index,
            -1,
            arcCosts,
            additive,
            null));
    }

    /// <summary>
    /// 终点处按路线成本策略修正 reduced cost / Correct the reduced cost at the terminal using the route-cost policy.
    /// </summary>
    /// <param name="terminal">终止标签 / The terminal label.</param>
    /// <param name="request">定价请求 / Pricing request.</param>
    /// <param name="zero">数值零 / Numeric zero.</param>
    /// <returns>修正后的状态 / The corrected state.</returns>
    private Result<EspprcLabel<V>?, ErrorCode, Error<ErrorCode>> TerminalContribution(
        PathLabel<EspprcLabel<V>, PricingArc<V>> terminal,
        PricingRequest<V> request,
        V zero) {
        EspprcLabel<V> state = terminal.State;
        if (!_instance.VehicleTypeById.TryGetValue(request.VehicleTypeId, out VehicleType<V>? vehicleType)) {
            return NetworkSchedulingErrors.Failure<EspprcLabel<V>?>(
                "执行定价失败：车辆类型不存在 / Failed to execute pricing: vehicle type does not exist");
        }
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> cost = _routeCostPolicy.Cost(vehicleType, state.ArcCosts);
        if (cost is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> costFailure) {
            return Results.Failed<EspprcLabel<V>?>(costFailure.Error);
        }
        Result<V, ErrorCode, Error<ErrorCode>> normalized =
            _valueAdapter.NormalizeValue(cost.Value, _instance.Units.CostUnit);
        if (normalized is Failed<V, ErrorCode, Error<ErrorCode>> normalizedFailure) {
            return Results.Failed<EspprcLabel<V>?>(normalizedFailure.Error);
        }
        if (request.Duals.Phase == PricingPhase.PhaseTwo && state.AdditiveObjectiveCost is null) {
            return NetworkSchedulingErrors.Failure<EspprcLabel<V>?>(
                "执行定价失败：缺少增量目标成本 / Failed to execute pricing: accumulated additive objective cost is "
                + "missing");
        }
        V reducedCost = request.Duals.Phase == PricingPhase.PhaseTwo
            ? state.ReducedCost.Minus(state.AdditiveObjectiveCost ?? zero).Plus(normalized.Value)
            : state.ReducedCost;
        return Results.Ok<EspprcLabel<V>?>(state with {
            ReducedCost = reducedCost,
            RouteCost = normalized.Value,
        });
    }

    /// <summary>
    /// 保守的不可扩展客户标记 / Conservative marking of customers that cannot be extended.
    /// </summary>
    /// <remarks>
    /// 仅标记由 elementarity 和容量单调性**必然**排除的客户：客户需求非负，因此当前负载超出容量后不可能通过后续访问恢复。
    /// **不能根据单条 outgoing 弧的时间窗结果永久标记客户**：非 metric 图中可能存在经其他客户到达的替代路径，平行弧也可能
    /// 有不同的可行性。错误标记会永久排除一个本可达的客户，让定价漏掉真实存在的负列，而且不会有任何报错。
    /// Only customers **necessarily** excluded by elementarity or monotone capacity are marked: demands are non-negative, so an
    /// over-capacity candidate can never recover later. **A single outgoing arc's time window must never permanently forbid a
    /// customer**: non-metric graphs may offer an alternative path via another customer, and parallel arcs may differ. A wrong
    /// mark would permanently exclude a reachable customer, making pricing miss a genuinely existing negative column with no error.
    /// </remarks>
    /// <param name="currentForbidden">当前不可达集合 / The current forbidden set.</param>
    /// <param name="toNode">到达节点 / The destination node.</param>
    /// <param name="visited">已访问集合 / The visited set.</param>
    /// <param name="graph">定价图 / Pricing graph.</param>
    /// <param name="currentLoad">当前负载 / The current load.</param>
    /// <returns>更新后的不可达集合 / The updated forbidden set.</returns>
    private static ForbiddenCustomers UpdateForbiddenCustomers(
        ForbiddenCustomers currentForbidden,
        PricingNode<V> toNode,
        VisitedCustomers visited,
        PricingGraph<V> graph,
        V currentLoad) {
        if (toNode.IsDepot) {
            return currentForbidden;
        }
        ForbiddenCustomers forbidden = currentForbidden.Add(toNode.CustomerIndex);
        foreach (PricingNode<V> candidate in graph.Nodes) {
            if (candidate.IsDepot
                || visited.Contains(candidate.CustomerIndex)
                || forbidden.Contains(candidate.CustomerIndex)) {
                continue;
            }
            V loadAtCandidate = currentLoad.Plus(candidate.Demand);
            if (loadAtCandidate.CompareTo(graph.VehicleCapacity) > 0) {
                forbidden = forbidden.Add(candidate.CustomerIndex);
            }
        }
        return forbidden;
    }

    /// <summary>
    /// 从标签回溯生成完整路线 / Backtrack from labels into a complete route.
    /// </summary>
    /// <param name="pathLabels">有序标签 / Ordered labels.</param>
    /// <param name="graph">定价图 / Pricing graph.</param>
    /// <param name="request">定价请求 / Pricing request.</param>
    /// <returns>路线或回溯失败 / The route, or a backtracking failure.</returns>
    private Result<Route<V>, ErrorCode, Error<ErrorCode>> BacktrackRoute(
        IReadOnlyList<PathLabel<EspprcLabel<V>, PricingArc<V>>> pathLabels,
        PricingGraph<V> graph,
        PricingRequest<V> request) {
        GanttScheduling.Infrastructure.TimeWindow<V> schedulingWindow = _instance.SchedulingWindow;
        if (!_instance.VehicleTypeById.TryGetValue(request.VehicleTypeId, out VehicleType<V>? _)) {
            return NetworkSchedulingErrors.Failure<Route<V>>(
                "回溯路线失败：车辆类型不存在 / Failed to backtrack route: vehicle type does not exist");
        }

        var path = pathLabels.Select(label => label.State).ToList();
        var pathArcs = new List<PricingArc<V>>();
        for (int index = 1; index < pathLabels.Count; index++) {
            if (pathLabels[index].Incoming is { } incoming) {
                pathArcs.Add(incoming);
            }
        }
        if (pathArcs.Count != path.Count - 1) {
            return NetworkSchedulingErrors.Failure<Route<V>>(
                "回溯路线失败：前驱链缺少弧 / Failed to backtrack route: predecessor chain is missing an arc");
        }
        if (path[^1].RouteCost is not { } totalCost) {
            return NetworkSchedulingErrors.Failure<Route<V>>(
                "回溯路线失败：终点路线成本缺失 / Failed to backtrack route: terminal route cost is missing");
        }

        var stops = new List<RouteStop<V>>();
        V zero = graph.VehicleCapacity.Constants.Zero;
        var accumulatedLoad = new Quantity<V>(zero, _instance.Units.LoadUnit);
        V totalDistance = zero;

        for (int index = 0; index < path.Count; index++) {
            EspprcLabel<V> nodeLabel = path[index];
            if (!graph.NodeIndexMap.TryGetValue(nodeLabel.CurrentNode, out int nodeIndex)) {
                continue;
            }
            PricingNode<V> node = graph.Nodes[nodeIndex];

            V arrivalTime;
            if (index == 0) {
                arrivalTime = nodeLabel.Time;
            } else {
                PricingArc<V>? arc = index - 1 < pathArcs.Count ? pathArcs[index - 1] : null;
                arrivalTime = arc is not null
                    ? path[index - 1].Time.Plus(arc.TravelTime)
                    : nodeLabel.Time.Minus(node.ServiceTime);
            }
            V serviceStart = MaxOf(arrivalTime, node.ReadyTime);
            V departure = nodeLabel.Time;

            DateTimeOffset arrivalInstant = InstantOf(schedulingWindow, arrivalTime);
            DateTimeOffset serviceStartInstant = InstantOf(schedulingWindow, serviceStart);
            DateTimeOffset departureInstant = InstantOf(schedulingWindow, departure);

            CustomerId? customerId = node.IsDepot ? null : _instance.Customers[node.CustomerIndex].Id;
            if (!node.IsDepot) {
                accumulatedLoad = new Quantity<V>(
                    accumulatedLoad.Value.Plus(node.Demand), _instance.Units.LoadUnit);
            }
            if (index > 0 && index - 1 < pathArcs.Count) {
                totalDistance = totalDistance.Plus(pathArcs[index - 1].Distance);
            }

            stops.Add(new RouteStop<V>(
                node.NodeId, customerId, arrivalInstant, serviceStartInstant, departureInstant, accumulatedLoad));
        }

        return Route<V>.Invoke(
            request.VehicleTypeId,
            stops,
            new Quantity<V>(totalDistance, _instance.Units.DistanceUnit),
            new Quantity<V>(totalCost, _instance.Units.CostUnit),
            pathArcs.Select(arc => arc.Id).ToList());
    }

    /// <summary>
    /// 把时间轴坐标还原为绝对时刻 / Turn a timeline coordinate back into an absolute instant.
    /// </summary>
    /// <param name="schedulingWindow">业务时间轴 / Business timeline.</param>
    /// <param name="value">时间轴坐标 / The timeline coordinate.</param>
    /// <returns>绝对时刻 / The absolute instant.</returns>
    private static DateTimeOffset InstantOf(
        GanttScheduling.Infrastructure.TimeWindow<V> schedulingWindow, V value) =>
        schedulingWindow.InstantOf(value.ToFlt64());

    /// <summary>
    /// 取两个值的较大者 / Take the larger of two values.
    /// </summary>
    /// <param name="left">左值 / Left value.</param>
    /// <param name="right">右值 / Right value.</param>
    /// <returns>较大者 / The larger value.</returns>
    private static V MaxOf(V left, V right) => left.CompareTo(right) >= 0 ? left : right;
}
