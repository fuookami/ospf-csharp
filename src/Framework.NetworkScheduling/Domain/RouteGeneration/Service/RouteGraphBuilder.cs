// Branch-aware 定价图构建 / Branch-aware pricing graph construction.
// 与固定 Kotlin 对应。
//
// 定价图把 VRPTW 实例 + 对偶 + 分支遮罩压成一张**只含节点索引与数值**的图：节点坐标、服务时长、需求、弧的 reduced cost 全部
// 预计算成 `V`，定价标签扩展时只做加减比较，不再碰业务对象。构建一次、扩展百万次的场景下，这个取舍是必须的。
// The pricing graph flattens VrptwInstance + duals + branch mask into a graph of **indices and numbers only**: node ready/due
// times, service durations, demands and arc reduced costs are all precomputed as `V`, so label extension only adds and compares
// and never touches business objects. Build once, extend a million times — that trade is mandatory here.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteGeneration.Policy;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Infrastructure;
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
/// 定价图节点 / Pricing graph node.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="NodeId">网络节点标识 / Network node identity.</param>
/// <param name="IsDepot">是否为仓库 / Whether this is a depot.</param>
/// <param name="CustomerIndex">客户索引，仓库为 -1 / Customer index, -1 at depots.</param>
/// <param name="ReadyTime">最早服务时刻（窗口坐标）/ Earliest service instant in window coordinates.</param>
/// <param name="DueTime">最晚服务时刻（窗口坐标）/ Latest service instant in window coordinates.</param>
/// <param name="ServiceTime">服务时长（窗口坐标）/ Service duration in window coordinates.</param>
/// <param name="Demand">需求 / Demand.</param>
public sealed record PricingNode<V>(
    NetworkNodeId NodeId,
    bool IsDepot,
    int CustomerIndex,
    V ReadyTime,
    V DueTime,
    V ServiceTime,
    V Demand) where V : struct, IFloatingNumber<V>;

/// <summary>
/// 定价图弧 / Pricing graph arc.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="FromIndex">起点索引 / Origin index.</param>
/// <param name="ToIndex">终点索引 / Destination index.</param>
/// <param name="ObjectiveCost">目标成本（含固定成本）/ Objective cost including the fixed cost.</param>
/// <param name="ArcCost">弧业务成本 / Arc business cost.</param>
/// <param name="ReducedCost">弧 reduced cost / Arc reduced cost.</param>
/// <param name="TravelTime">行驶时间（窗口坐标）/ Travel time in window coordinates.</param>
/// <param name="Distance">距离 / Distance.</param>
/// <param name="FromNodeId">起点标识 / Origin identity.</param>
/// <param name="ToNodeId">终点标识 / Destination identity.</param>
/// <param name="Id">独立稳定弧标识 / Independent stable arc identity.</param>
public sealed record PricingArc<V>(
    int FromIndex,
    int ToIndex,
    V ObjectiveCost,
    Quantity<V> ArcCost,
    V ReducedCost,
    V TravelTime,
    V Distance,
    NetworkNodeId FromNodeId,
    NetworkNodeId ToNodeId,
    NetworkArcId Id) where V : struct, IFloatingNumber<V>;

/// <summary>
/// 定价图 / Pricing graph.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class PricingGraph<V>(
    IReadOnlyList<PricingNode<V>> Nodes,
    IReadOnlyList<PricingArc<V>> Arcs,
    IReadOnlyDictionary<NetworkNodeId, int> NodeIndexMap,
    int CustomerCount,
    int StartDepotIndex,
    int EndDepotIndex,
    V VehicleCapacity) where V : struct, IFloatingNumber<V> {
    private readonly Dictionary<int, List<PricingArc<V>>> _outgoingCache = new();

    /// <summary>定价图节点 / The graph nodes.</summary>
    public IReadOnlyList<PricingNode<V>> Nodes { get; } = Nodes;

    /// <summary>定价图弧 / The graph arcs.</summary>
    public IReadOnlyList<PricingArc<V>> Arcs { get; } = Arcs;

    /// <summary>节点标识到节点索引的映射 / Node identity to node-index map.</summary>
    public IReadOnlyDictionary<NetworkNodeId, int> NodeIndexMap { get; } = NodeIndexMap;

    /// <summary>客户总数（实例口径，含被遮罩排除者）/ Total customers in the instance, including mask-excluded ones.</summary>
    public int CustomerCount { get; } = CustomerCount;

    /// <summary>起始仓库索引 / Start depot index.</summary>
    public int StartDepotIndex { get; } = StartDepotIndex;

    /// <summary>结束仓库索引 / End depot index.</summary>
    public int EndDepotIndex { get; } = EndDepotIndex;

    /// <summary>车辆容量 / Vehicle capacity.</summary>
    public V VehicleCapacity { get; } = VehicleCapacity;

    /// <summary>
    /// 从指定节点出发的弧 / Arcs outgoing from the specified node.
    /// </summary>
    /// <remarks>
    /// 结果被缓存：定价扩展会在同一个节点上反复枚举出弧，线性扫描整张弧表会让复杂度多乘一个弧数。
    /// Results are cached: label extension enumerates a node's outgoing arcs over and over, and a linear scan of the whole arc
    /// list would multiply the complexity by the arc count.
    /// </remarks>
    /// <param name="nodeIndex">节点索引 / Node index.</param>
    /// <returns>出弧 / The outgoing arcs.</returns>
    public IReadOnlyList<PricingArc<V>> OutgoingFrom(int nodeIndex) {
        if (_outgoingCache.TryGetValue(nodeIndex, out List<PricingArc<V>>? cached)) {
            return cached;
        }
        var built = Arcs.Where(arc => arc.FromIndex == nodeIndex).ToList();
        _outgoingCache[nodeIndex] = built;
        return built;
    }
}

/// <summary>
/// Branch-aware 定价图构建器 / Branch-aware pricing graph builder.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class RouteGraphBuilder<V>(
    VrptwInstance<V> instance,
    INetworkSchedulingSolverValueAdapter<V> valueAdapter,
    IDistanceCalculator<V> distanceCalculator,
    ITravelTimeCalculator<V> travelTimeCalculator,
    IArcCostCalculator<V> arcCostCalculator,
    IArcFeasibilityPolicy<V>? arcFeasibilityPolicy = null,
    ResourceGraph<VehicleTypeId>? resourceGraph = null,
    IReadOnlyList<IRouteArcCoefficientProvider<V>>? coefficientProviders = null)
    where V : struct, IFloatingNumber<V> {
    private readonly IArcFeasibilityPolicy<V> _arcFeasibilityPolicy =
        arcFeasibilityPolicy ?? new DefaultArcFeasibilityPolicy<V>();
    private readonly IReadOnlyList<IRouteArcCoefficientProvider<V>> _coefficientProviders =
        coefficientProviders ?? Array.Empty<IRouteArcCoefficientProvider<V>>();

    /// <summary>
    /// 构建定价图 / Build the pricing graph.
    /// </summary>
    /// <remarks>
    /// **节点顺序固定为「起始仓库 → 客户 → 结束仓库」**：定价状态与位集索引都依赖这个顺序，一旦顺序变化，已缓存的标签与
    /// 支配判定会静默失配。
    /// **阶段决定目标成本**：第一阶段把目标成本压成零，只用对偶做"是否存在负列"的判定；第二阶段才用真实目标成本。这是
    /// 两阶段单纯形在定价侧的对应做法。
    /// **The node order is fixed to「start depot → customers → end depot」**: pricing state and bitset indices depend on it, and
    /// a reordering would silently invalidate cached labels and dominance results.
    /// **The phase determines the objective cost**: phase one collapses it to zero and decides "does a negative column exist"
    /// from duals alone; phase two uses the real objective cost. That mirrors two-phase simplex on the pricing side.
    /// </remarks>
    /// <param name="vehicleTypeId">车辆类型 / Vehicle type.</param>
    /// <param name="duals">定价对偶 / Pricing duals.</param>
    /// <param name="branchMask">分支遮罩 / Branch mask.</param>
    /// <returns>定价图或构建失败 / The pricing graph, or a build failure.</returns>
    public Result<PricingGraph<V>, ErrorCode, Error<ErrorCode>> Build(
        VehicleTypeId vehicleTypeId,
        PricingDuals duals,
        BranchMask<VehicleTypeId>? branchMask = null) {
        ArgumentNullException.ThrowIfNull(vehicleTypeId);
        ArgumentNullException.ThrowIfNull(duals);

        if (!instance.VehicleTypeById.TryGetValue(vehicleTypeId, out VehicleType<V>? vehicleType)) {
            return NetworkSchedulingErrors.Failure<PricingGraph<V>>(
                $"构建定价图失败：车辆类型 {vehicleTypeId.Value} 不存在 / Failed to build pricing graph: vehicle type "
                + $"{vehicleTypeId.Value} does not exist");
        }
        var instanceNodeIds = new HashSet<NetworkNodeId> {
            instance.StartDepot.Node.Id, instance.EndDepot.Node.Id,
        };
        foreach (Customer<V> customer in instance.Customers) {
            instanceNodeIds.Add(customer.Node.Id);
        }
        if (resourceGraph is not null && resourceGraph.Nodes.Any(node => !instanceNodeIds.Contains(node))) {
            return NetworkSchedulingErrors.Failure<PricingGraph<V>>(
                "构建定价图失败：资源图包含实例外节点 / Failed to build pricing graph: resource graph contains a node "
                + "outside the instance");
        }

        TimeWindow<V> schedulingWindow = instance.SchedulingWindow;
        V zero = vehicleType.Capacity.Value.Constants.Zero;

        var nodes = new List<PricingNode<V>>();
        var nodeIndexMap = new Dictionary<NetworkNodeId, int>();

        Result<PricingNode<V>, ErrorCode, Error<ErrorCode>> startDepotNode = DepotNode(
            instance.StartDepot, schedulingWindow, zero);
        if (startDepotNode is Failed<PricingNode<V>, ErrorCode, Error<ErrorCode>> startFailure) {
            return Results.Failed<PricingGraph<V>>(startFailure.Error);
        }
        nodes.Add(startDepotNode.Value);
        nodeIndexMap[instance.StartDepot.Node.Id] = 0;

        for (int customerIndex = 0; customerIndex < instance.Customers.Count; customerIndex++) {
            Customer<V> customer = instance.Customers[customerIndex];
            bool forbiddenByMask = branchMask is not null
                && !branchMask.AllowsNode(vehicleTypeId, customer.Node.Id);
            if (forbiddenByMask) {
                continue;
            }
            Result<V, ErrorCode, Error<ErrorCode>> demand =
                valueAdapter.NormalizeValue(customer.Demand, instance.Units.LoadUnit);
            if (demand is Failed<V, ErrorCode, Error<ErrorCode>> demandFailure) {
                return Results.Failed<PricingGraph<V>>(demandFailure.Error);
            }
            Result<V, ErrorCode, Error<ErrorCode>> readyTime =
                TimeValue(schedulingWindow, customer.TimeWindow.ReadyTime);
            if (readyTime is Failed<V, ErrorCode, Error<ErrorCode>> readyFailure) {
                return Results.Failed<PricingGraph<V>>(readyFailure.Error);
            }
            Result<V, ErrorCode, Error<ErrorCode>> dueTime =
                TimeValue(schedulingWindow, customer.TimeWindow.DueTime);
            if (dueTime is Failed<V, ErrorCode, Error<ErrorCode>> dueFailure) {
                return Results.Failed<PricingGraph<V>>(dueFailure.Error);
            }
            Result<V, ErrorCode, Error<ErrorCode>> serviceTime =
                TimeValue(schedulingWindow, customer.ServiceTime);
            if (serviceTime is Failed<V, ErrorCode, Error<ErrorCode>> serviceFailure) {
                return Results.Failed<PricingGraph<V>>(serviceFailure.Error);
            }
            int nodeIndex = nodes.Count;
            nodes.Add(new PricingNode<V>(
                customer.Node.Id, false, customerIndex,
                readyTime.Value, dueTime.Value, serviceTime.Value, demand.Value));
            nodeIndexMap[customer.Node.Id] = nodeIndex;
        }

        Result<PricingNode<V>, ErrorCode, Error<ErrorCode>> endDepotNode = DepotNode(
            instance.EndDepot, schedulingWindow, zero);
        if (endDepotNode is Failed<PricingNode<V>, ErrorCode, Error<ErrorCode>> endFailure) {
            return Results.Failed<PricingGraph<V>>(endFailure.Error);
        }
        nodes.Add(endDepotNode.Value);
        nodeIndexMap[instance.EndDepot.Node.Id] = nodes.Count - 1;

        Result<V, ErrorCode, Error<ErrorCode>> capacity =
            valueAdapter.NormalizeValue(vehicleType.Capacity, instance.Units.LoadUnit);
        if (capacity is Failed<V, ErrorCode, Error<ErrorCode>> capacityFailure) {
            return Results.Failed<PricingGraph<V>>(capacityFailure.Error);
        }

        var arcs = new List<PricingArc<V>>();
        for (int fromIndex = 0; fromIndex < nodes.Count; fromIndex++) {
            PricingNode<V> fromNode = nodes[fromIndex];
            if (fromNode.NodeId.Equals(instance.EndDepot.Node.Id)) {
                continue;
            }
            for (int toIndex = 0; toIndex < nodes.Count; toIndex++) {
                PricingNode<V> toNode = nodes[toIndex];
                if (fromIndex == toIndex || toNode.NodeId.Equals(instance.StartDepot.Node.Id)) {
                    continue;
                }
                Result<bool, ErrorCode, Error<ErrorCode>> added = AddArcs(
                    fromNode, toNode, fromIndex, toIndex, vehicleTypeId, vehicleType, duals, branchMask,
                    schedulingWindow, zero, arcs);
                if (added is Failed<bool, ErrorCode, Error<ErrorCode>> addedFailure) {
                    return Results.Failed<PricingGraph<V>>(addedFailure.Error);
                }
            }
        }

        return Results.Ok(new PricingGraph<V>(
            nodes, arcs, nodeIndexMap, instance.Customers.Count, 0, nodes.Count - 1, capacity.Value));
    }

    /// <summary>
    /// 为一条节点对枚举资源弧并写入定价弧 / Enumerate resource arcs for one node pair and append pricing arcs.
    /// </summary>
    /// <remarks>
    /// 拆成独立方法只是为了避免双层循环里嵌套六层缩进；逻辑与基线逐句对应。
    /// Split out only to avoid six levels of nesting inside the double loop; the logic mirrors the code line by line.
    /// </remarks>
    /// <param name="fromNode">起点定价节点 / Origin pricing node.</param>
    /// <param name="toNode">终点定价节点 / Destination pricing node.</param>
    /// <param name="fromIndex">起点索引 / Origin index.</param>
    /// <param name="toIndex">终点索引 / Destination index.</param>
    /// <param name="vehicleTypeId">车辆类型 / Vehicle type.</param>
    /// <param name="vehicleType">车辆类型对象 / The vehicle type object.</param>
    /// <param name="duals">定价对偶 / Pricing duals.</param>
    /// <param name="branchMask">分支遮罩 / Branch mask.</param>
    /// <param name="schedulingWindow">业务时间轴 / Business timeline.</param>
    /// <param name="zero">数值零 / Numeric zero.</param>
    /// <param name="arcs">输出弧集合 / The output arc list.</param>
    /// <returns>成功或构建失败 / Success, or a build failure.</returns>
    private Result<bool, ErrorCode, Error<ErrorCode>> AddArcs(
        PricingNode<V> fromNode,
        PricingNode<V> toNode,
        int fromIndex,
        int toIndex,
        VehicleTypeId vehicleTypeId,
        VehicleType<V> vehicleType,
        PricingDuals duals,
        BranchMask<VehicleTypeId>? branchMask,
        TimeWindow<V> schedulingWindow,
        V zero,
        List<PricingArc<V>> arcs) {
        NetworkNode<V>? fromObject = fromNode.IsDepot
            && fromNode.NodeId.Equals(instance.StartDepot.Node.Id)
                ? instance.StartDepot.Node
                : instance.CustomerByNodeId.TryGetValue(fromNode.NodeId, out Customer<V>? fromCustomer)
                    ? fromCustomer.Node
                    : null;
        if (fromObject is null) {
            return Results.Ok(true);
        }
        NetworkNode<V>? toObject = toNode.IsDepot && toNode.NodeId.Equals(instance.EndDepot.Node.Id)
            ? instance.EndDepot.Node
            : instance.CustomerByNodeId.TryGetValue(toNode.NodeId, out Customer<V>? toCustomer)
                ? toCustomer.Node
                : null;
        if (toObject is null) {
            return Results.Ok(true);
        }

        IReadOnlyList<ResourceArc<VehicleTypeId>> resourceArcs = resourceGraph is null
            ? new[] { new ResourceArc<VehicleTypeId>(vehicleTypeId, fromNode.NodeId, toNode.NodeId) }
            : resourceGraph.Between(vehicleTypeId, fromNode.NodeId, toNode.NodeId);

        foreach (ResourceArc<VehicleTypeId> resourceArc in resourceArcs) {
            if (branchMask is not null && !branchMask.AllowsArc(resourceArc)) {
                continue;
            }
            Result<bool, ErrorCode, Error<ErrorCode>> feasible =
                _arcFeasibilityPolicy.IsFeasible(resourceArc, fromObject, toObject, vehicleType);
            if (feasible is Failed<bool, ErrorCode, Error<ErrorCode>> feasibleFailure) {
                return Results.Failed<bool>(feasibleFailure.Error);
            }
            if (!feasible.Value) {
                continue;
            }

            Result<Quantity<V>, ErrorCode, Error<ErrorCode>> distance =
                distanceCalculator.Distance(resourceArc, fromObject, toObject);
            if (distance is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> distanceFailure) {
                return Results.Failed<bool>(distanceFailure.Error);
            }
            Result<TimeSpan, ErrorCode, Error<ErrorCode>> travelTime =
                travelTimeCalculator.TravelTime(resourceArc, fromObject, toObject, vehicleType);
            if (travelTime is Failed<TimeSpan, ErrorCode, Error<ErrorCode>> travelFailure) {
                return Results.Failed<bool>(travelFailure.Error);
            }
            if (travelTime.Value < TimeSpan.Zero) {
                return NetworkSchedulingErrors.Failure<bool>(
                    "构建定价图失败：弧行驶时间不能为负 / Failed to build pricing graph: arc travel time cannot be negative");
            }
            Result<Quantity<V>, ErrorCode, Error<ErrorCode>> arcCost =
                arcCostCalculator.Cost(resourceArc, fromObject, toObject, distance.Value, travelTime.Value, vehicleType);
            if (arcCost is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> arcCostFailure) {
                return Results.Failed<bool>(arcCostFailure.Error);
            }
            Result<V, ErrorCode, Error<ErrorCode>> normalizedArcCost =
                valueAdapter.NormalizeValue(arcCost.Value, instance.Units.CostUnit);
            if (normalizedArcCost is Failed<V, ErrorCode, Error<ErrorCode>> normalizedCostFailure) {
                return Results.Failed<bool>(normalizedCostFailure.Error);
            }
            Result<V, ErrorCode, Error<ErrorCode>> normalizedDistance =
                valueAdapter.NormalizeValue(distance.Value, instance.Units.DistanceUnit);
            if (normalizedDistance is Failed<V, ErrorCode, Error<ErrorCode>> normalizedDistanceFailure) {
                return Results.Failed<bool>(normalizedDistanceFailure.Error);
            }

            bool fromStartDepot = fromNode.IsDepot && fromNode.NodeId.Equals(instance.StartDepot.Node.Id);
            V routeObjectiveCost = normalizedArcCost.Value;
            if (fromStartDepot) {
                Result<V, ErrorCode, Error<ErrorCode>> fixedCost =
                    valueAdapter.NormalizeValue(vehicleType.FixedCost, instance.Units.CostUnit);
                if (fixedCost is Failed<V, ErrorCode, Error<ErrorCode>> fixedCostFailure) {
                    return Results.Failed<bool>(fixedCostFailure.Error);
                }
                routeObjectiveCost = fixedCost.Value.Plus(normalizedArcCost.Value);
            }

            V customerDual = zero;
            if (!toNode.IsDepot) {
                CustomerId customerId = instance.Customers[toNode.CustomerIndex].Id;
                Flt64 solverDual = duals.Customer.TryGetValue(customerId, out Flt64 found)
                    ? found
                    : Flt64.Zero;
                Result<V, ErrorCode, Error<ErrorCode>> converted = valueAdapter.FromSolverValue(solverDual);
                if (converted is Failed<V, ErrorCode, Error<ErrorCode>>) {
                    return NetworkSchedulingErrors.Failure<bool>(
                        "构建定价图失败：客户对偶转换失败 / Failed to build pricing graph: customer dual conversion "
                        + "failed");
                }
                customerDual = converted.Value;
            }

            V fleetDual = zero;
            if (fromStartDepot) {
                Flt64 solverDual = duals.Fleet.TryGetValue(vehicleTypeId, out Flt64 found)
                    ? found
                    : Flt64.Zero;
                Result<V, ErrorCode, Error<ErrorCode>> converted = valueAdapter.FromSolverValue(solverDual);
                if (converted is Failed<V, ErrorCode, Error<ErrorCode>>) {
                    return NetworkSchedulingErrors.Failure<bool>(
                        "构建定价图失败：车队对偶转换失败 / Failed to build pricing graph: fleet dual conversion failed");
                }
                fleetDual = converted.Value;
            }

            V phaseObjectiveCost = duals.Phase == PricingPhase.PhaseOne ? zero : routeObjectiveCost;
            V reducedCost = phaseObjectiveCost.Minus(customerDual).Minus(fleetDual);
            foreach (IRouteArcCoefficientProvider<V> provider in _coefficientProviders) {
                foreach (KeyValuePair<string, V> coefficient in provider.Coefficients(resourceArc)) {
                    if (!duals.Additional.TryGetValue(coefficient.Key, out Flt64 extraDual)) {
                        return NetworkSchedulingErrors.Failure<bool>(
                            $"构建定价图失败：扩展行 {coefficient.Key} 缺少当前对偶 / Failed to build pricing graph: missing "
                            + $"current dual for extra row {coefficient.Key}");
                    }
                    Result<V, ErrorCode, Error<ErrorCode>> converted =
                        valueAdapter.FromSolverValue(extraDual);
                    if (converted is Failed<V, ErrorCode, Error<ErrorCode>>) {
                        return NetworkSchedulingErrors.Failure<bool>(
                            "构建定价图失败：扩展对偶转换失败 / Failed to build pricing graph: extra dual conversion "
                            + "failed");
                    }
                    reducedCost = reducedCost.Minus(coefficient.Value.Times(converted.Value));
                }
            }

            Result<V, ErrorCode, Error<ErrorCode>> travelTimeValue =
                TimeValue(schedulingWindow, travelTime.Value);
            if (travelTimeValue is Failed<V, ErrorCode, Error<ErrorCode>> travelValueFailure) {
                return Results.Failed<bool>(travelValueFailure.Error);
            }
            arcs.Add(new PricingArc<V>(
                fromIndex, toIndex, routeObjectiveCost,
                new Quantity<V>(normalizedArcCost.Value, instance.Units.CostUnit),
                reducedCost,
                travelTimeValue.Value,
                normalizedDistance.Value,
                fromNode.NodeId, toNode.NodeId, resourceArc.Id));
        }
        return Results.Ok(true);
    }

    /// <summary>
    /// 构造仓库定价节点 / Build a depot pricing node.
    /// </summary>
    /// <param name="depot">仓库 / The depot.</param>
    /// <param name="schedulingWindow">业务时间轴 / Business timeline.</param>
    /// <param name="zero">数值零 / Numeric zero.</param>
    /// <returns>仓库节点或换算失败 / The depot node, or a conversion failure.</returns>
    private Result<PricingNode<V>, ErrorCode, Error<ErrorCode>> DepotNode(
        Depot<V> depot,
        TimeWindow<V> schedulingWindow,
        V zero) {
        Result<V, ErrorCode, Error<ErrorCode>> readyTime =
            TimeValue(schedulingWindow, depot.TimeWindow.ReadyTime);
        if (readyTime is Failed<V, ErrorCode, Error<ErrorCode>> readyFailure) {
            return Results.Failed<PricingNode<V>>(readyFailure.Error);
        }
        Result<V, ErrorCode, Error<ErrorCode>> dueTime =
            TimeValue(schedulingWindow, depot.TimeWindow.DueTime);
        if (dueTime is Failed<V, ErrorCode, Error<ErrorCode>> dueFailure) {
            return Results.Failed<PricingNode<V>>(dueFailure.Error);
        }
        return Results.Ok(new PricingNode<V>(
            depot.Node.Id, true, -1, readyTime.Value, dueTime.Value, zero, zero));
    }

    /// <summary>
    /// 把时刻换算到业务时间轴坐标 / Map an instant into business-timeline coordinates.
    /// </summary>
    /// <remarks>
    /// **换算失败必须向上传播，不能取 `default`**：`default(V)` 会把时间窗压成零，让所有时间可行性检查失去意义，而定价
    /// 仍会跑出一批"可行"的列。返回失败比返回一个看起来正常的零安全得多。
    /// **A failed conversion must propagate rather than yield `default`**: `default(V)` collapses the time window to zero so
    /// every time-feasibility check becomes meaningless, yet pricing still emits a batch of "feasible" columns. Failing is far
    /// safer than returning a plausible-looking zero.
    /// </remarks>
    /// <param name="schedulingWindow">业务时间轴 / Business timeline.</param>
    /// <param name="instant">时间点 / The instant.</param>
    /// <returns>时间轴坐标值或换算失败 / The value in timeline coordinates, or a conversion failure.</returns>
    private Result<V, ErrorCode, Error<ErrorCode>> TimeValue(TimeWindow<V> schedulingWindow, DateTimeOffset instant) =>
        valueAdapter.FromSolverValue(schedulingWindow.ValueOf(instant));

    /// <summary>
    /// 把时长换算到业务时间轴坐标 / Map a duration into business-timeline coordinates.
    /// </summary>
    /// <param name="schedulingWindow">业务时间轴 / Business timeline.</param>
    /// <param name="duration">时长 / The duration.</param>
    /// <returns>时间轴坐标值或换算失败 / The value in timeline coordinates, or a conversion failure.</returns>
    private Result<V, ErrorCode, Error<ErrorCode>> TimeValue(TimeWindow<V> schedulingWindow, TimeSpan duration) =>
        valueAdapter.FromSolverValue(schedulingWindow.ValueOf(duration));
}
