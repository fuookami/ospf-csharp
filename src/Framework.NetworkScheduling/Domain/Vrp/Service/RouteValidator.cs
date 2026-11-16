// 路线资源递推与成本独立校验 / Independent route resource-recursion and cost validation.
//
// 这个校验器**不信任路线里的任何数值**：到达时刻、服务开始、离开时刻、累计负载、总距离、总成本，全部按策略与实例重新递推
// 一遍再比对。它是整条求解流水线的最后一道防线——定价算错了、合并算错了、启发式写错了，都会在这里暴露成一个可读的失败消息，
// 而不是一个"看起来能用但成本心虚"的方案。
// This validator **trusts no number in the route**: arrival, service start, departure, accumulated load, total distance and
// total cost are all recomputed from the instance and the policies, then compared. It is the last line of defence for the whole
// pipeline — a pricing bug, a merge bug or a heuristic bug surfaces here as a readable failure instead of a plan that "looks
// usable but whose cost nobody trusts".

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Infrastructure;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Policy;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Service;

/// <summary>
/// 路线资源递推与成本独立校验器 / Independent route resource-recursion and cost validator.
/// </summary>
public static class RouteValidator {
    /// <summary>
    /// 重放并校验完整路线 / Replay and validate a complete route.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="instance">VRPTW 实例 / VRPTW instance.</param>
    /// <param name="route">待校验路线 / Route to validate.</param>
    /// <param name="distanceCalculator">距离策略 / Distance policy.</param>
    /// <param name="travelTimeCalculator">时间策略 / Travel-time policy.</param>
    /// <param name="arcCostCalculator">弧成本策略 / Arc-cost policy.</param>
    /// <param name="routeCostPolicy">路线成本策略 / Route-cost policy.</param>
    /// <param name="valueAdapter">solver 数值适配器 / Solver value adapter.</param>
    /// <returns>成功或带上下文的失败 / Success, or a contextual failure.</returns>
    public static Try Validate<V>(
        VrptwInstance<V> instance,
        Route<V> route,
        IDistanceCalculator<V> distanceCalculator,
        ITravelTimeCalculator<V> travelTimeCalculator,
        IArcCostCalculator<V> arcCostCalculator,
        IRouteCostPolicy<V> routeCostPolicy,
        INetworkSchedulingSolverValueAdapter<V> valueAdapter) where V : struct, IRealNumber<V> {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(distanceCalculator);
        ArgumentNullException.ThrowIfNull(travelTimeCalculator);
        ArgumentNullException.ThrowIfNull(arcCostCalculator);
        ArgumentNullException.ThrowIfNull(routeCostPolicy);
        ArgumentNullException.ThrowIfNull(valueAdapter);

        if (!instance.VehicleTypeById.TryGetValue(route.VehicleTypeId, out VehicleType<V>? vehicleType)) {
            return RouteFailure(route, "车辆类型不存在", "vehicle type does not exist");
        }

        IReadOnlyList<RouteStop<V>> stops = route.Stops;
        if (!stops[0].NodeId.Equals(instance.StartDepot.Node.Id) || stops[0].CustomerId is not null) {
            return RouteFailure(route, "首个停靠点不是起始仓库", "first stop is not the start depot");
        }
        if (!stops[^1].NodeId.Equals(instance.EndDepot.Node.Id) || stops[^1].CustomerId is not null) {
            return RouteFailure(route, "最后停靠点不是结束仓库", "last stop is not the end depot");
        }

        var customerStops = stops.Skip(1).Take(stops.Count - 2).ToList();
        var customerIds = customerStops.Where(stop => stop.CustomerId is not null)
            .Select(stop => stop.CustomerId!).ToList();
        if (customerIds.Count != customerStops.Count || customerIds.Distinct().Count() != customerIds.Count) {
            return RouteFailure(route, "客户缺失或重复访问", "customer is missing or visited repeatedly");
        }
        foreach (RouteStop<V> stop in customerStops) {
            if (stop.CustomerId is null
                || !instance.CustomerById.TryGetValue(stop.CustomerId, out Customer<V>? customer)) {
                return StopFailure(route, stop, "客户不存在", "customer does not exist");
            }
            if (!customer.Node.Id.Equals(stop.NodeId)) {
                return StopFailure(route, stop, "客户与节点 ID 不匹配", "customer and node IDs do not match");
            }
        }

        RouteStop<V> start = stops[0];
        if (!ValidLoadField(start, instance)) {
            return StopFailure(route, start, "负载单位不兼容", "load unit is incompatible");
        }
        if (!instance.StartDepot.TimeWindow.Contains(start.ServiceStart)) {
            return StopFailure(route, start, "出发时刻不在仓库时间窗内", "departure time is outside the depot time window");
        }
        if (!start.ServiceStart.Equals(start.Departure) || start.Arrival > start.ServiceStart) {
            return StopFailure(route, start, "起始仓库的到达/出发时刻非法",
                "start-depot arrival or departure time is invalid");
        }
        var zeroLoad = new Quantity<V>(start.AccumulatedLoad.Value.Constants.Zero, instance.Units.LoadUnit);
        if (!Same(start.AccumulatedLoad, zeroLoad)) {
            return StopFailure(route, start, "起始负载必须为 0", "initial load must be zero");
        }

        var totalDistance = new Quantity<V>(route.Distance.Value.Constants.Zero, instance.Units.DistanceUnit);
        var arcCosts = new List<Quantity<V>>();
        for (int index = 1; index < stops.Count; index++) {
            RouteStop<V> previous = stops[index - 1];
            RouteStop<V> current = stops[index];
            if (!ValidLoadField(current, instance)) {
                return StopFailure(route, current, "负载单位不兼容", "load unit is incompatible");
            }
            NetworkNode<V>? fromNode = NodeOf(instance, previous);
            if (fromNode is null) {
                return StopFailure(route, previous, "节点不存在", "node does not exist");
            }
            NetworkNode<V>? toNode = NodeOf(instance, current);
            if (toNode is null) {
                return StopFailure(route, current, "节点不存在", "node does not exist");
            }

            var resourceArc = new ResourceArc<VehicleTypeId>(
                route.VehicleTypeId, fromNode.Id, toNode.Id, route.ArcIds[index - 1]);

            Result<Quantity<V>, ErrorCode, Error<ErrorCode>> distance = distanceCalculator
                .Distance(resourceArc, fromNode, toNode);
            if (distance is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>>) {
                return ArcFailure(route, fromNode, toNode, "距离计算失败", "distance calculation failed");
            }
            Result<TimeSpan, ErrorCode, Error<ErrorCode>> travelTime = travelTimeCalculator
                .TravelTime(resourceArc, fromNode, toNode, vehicleType);
            if (travelTime is Failed<TimeSpan, ErrorCode, Error<ErrorCode>>) {
                return ArcFailure(route, fromNode, toNode, "行驶时间计算失败", "travel-time calculation failed");
            }

            DateTimeOffset expectedArrival = previous.Departure + travelTime.Value;
            if (!current.Arrival.Equals(expectedArrival)) {
                return StopFailure(route, current, "到达时刻与递推结果不一致",
                    "arrival time does not match the recurrence");
            }

            ServiceTimeWindow? window = TimeWindowOf(instance, current);
            if (window is null) {
                return StopFailure(route, current, "时间窗不存在", "time window does not exist");
            }
            DateTimeOffset expectedServiceStart = current.Arrival > window.ReadyTime
                ? current.Arrival
                : window.ReadyTime;
            if (!current.ServiceStart.Equals(expectedServiceStart) || !window.Contains(current.ServiceStart)) {
                return StopFailure(route, current, "等待或服务开始时刻非法", "waiting or service-start time is invalid");
            }
            TimeSpan serviceTime = ServiceTimeOf(instance, current);
            DateTimeOffset expectedDeparture = current.ServiceStart + serviceTime;
            if (!current.Departure.Equals(expectedDeparture)) {
                return StopFailure(route, current, "离开时刻与递推结果不一致",
                    "departure time does not match the recurrence");
            }

            Quantity<V> demand = DemandOf(instance, current);
            Quantity<V>? expectedLoad = Add(previous.AccumulatedLoad, demand);
            if (expectedLoad is null) {
                return StopFailure(route, current, "负载单位不兼容", "load unit is incompatible");
            }
            if (!Same(current.AccumulatedLoad, expectedLoad)) {
                return StopFailure(route, current, "累计负载与递推结果不一致",
                    "accumulated load does not match the recurrence");
            }

            Result<Quantity<V>, ErrorCode, Error<ErrorCode>> capacity = vehicleType.Capacity.ConvertTo(instance.Units.LoadUnit);
            if (capacity is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>>) {
                return RouteFailure(route, "车辆容量单位不兼容", "vehicle-capacity unit is incompatible");
            }
            Result<Quantity<V>, ErrorCode, Error<ErrorCode>> normalizedLoad =
                current.AccumulatedLoad.ConvertTo(instance.Units.LoadUnit);
            if (normalizedLoad is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>>) {
                return StopFailure(route, current, "负载单位不兼容", "load unit is incompatible");
            }
            if (normalizedLoad.Value.Value.CompareTo(capacity.Value.Value) > 0) {
                return StopFailure(route, current, "累计负载超过车辆容量",
                    "accumulated load exceeds vehicle capacity");
            }

            Quantity<V>? accumulated = Add(totalDistance, distance.Value);
            if (accumulated is null) {
                return ArcFailure(route, fromNode, toNode, "距离单位不兼容", "distance units are incompatible");
            }
            totalDistance = accumulated;

            Result<Quantity<V>, ErrorCode, Error<ErrorCode>> arcCost = arcCostCalculator.Cost(
                resourceArc, fromNode, toNode, distance.Value, travelTime.Value, vehicleType);
            if (arcCost is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>>) {
                return ArcFailure(route, fromNode, toNode, "弧成本计算失败", "arc-cost calculation failed");
            }
            arcCosts.Add(arcCost.Value);
        }

        if (!WithinTolerance(route.Distance, totalDistance, instance, valueAdapter, true)) {
            return RouteFailure(route, "总距离与重算结果不一致", "total distance does not match the recomputed value");
        }
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> expectedCost = routeCostPolicy.Cost(vehicleType, arcCosts);
        if (expectedCost is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>>) {
            return RouteFailure(route, "路线成本计算失败", "route-cost calculation failed");
        }
        if (!WithinTolerance(route.Cost, expectedCost.Value, instance, valueAdapter, false)) {
            return RouteFailure(route, "总成本与重算结果不一致", "total cost does not match the recomputed value");
        }
        return Results.OkInstance;
    }

    /// <summary>按停靠点找到对应节点 / Find the node for a stop.</summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="instance">实例 / Instance.</param>
    /// <param name="stop">停靠点 / Stop.</param>
    /// <returns>节点或 null / The node, or null.</returns>
    private static NetworkNode<V>? NodeOf<V>(VrptwInstance<V> instance, RouteStop<V> stop)
        where V : struct, IRealNumber<V> {
        if (stop.NodeId.Equals(instance.StartDepot.Node.Id)) {
            return instance.StartDepot.Node;
        }
        if (stop.NodeId.Equals(instance.EndDepot.Node.Id)) {
            return instance.EndDepot.Node;
        }
        return instance.CustomerByNodeId.TryGetValue(stop.NodeId, out Customer<V>? customer)
            ? customer.Node
            : null;
    }

    /// <summary>按停靠点找到服务时间窗 / Find the service time window for a stop.</summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="instance">实例 / Instance.</param>
    /// <param name="stop">停靠点 / Stop.</param>
    /// <returns>时间窗或 null / The window, or null.</returns>
    private static ServiceTimeWindow? TimeWindowOf<V>(VrptwInstance<V> instance, RouteStop<V> stop)
        where V : struct, IRealNumber<V> {
        if (stop.NodeId.Equals(instance.StartDepot.Node.Id)) {
            return instance.StartDepot.TimeWindow;
        }
        if (stop.NodeId.Equals(instance.EndDepot.Node.Id)) {
            return instance.EndDepot.TimeWindow;
        }
        return instance.CustomerByNodeId.TryGetValue(stop.NodeId, out Customer<V>? customer)
            ? customer.TimeWindow
            : null;
    }

    /// <summary>服务时长；仓库为零 / Service duration; zero at depots.</summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="instance">实例 / Instance.</param>
    /// <param name="stop">停靠点 / Stop.</param>
    /// <returns>服务时长 / The service duration.</returns>
    private static TimeSpan ServiceTimeOf<V>(VrptwInstance<V> instance, RouteStop<V> stop)
        where V : struct, IRealNumber<V> =>
        instance.CustomerByNodeId.TryGetValue(stop.NodeId, out Customer<V>? customer)
            ? customer.ServiceTime
            : TimeSpan.Zero;

    /// <summary>需求；仓库为零 / Demand; zero at depots.</summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="instance">实例 / Instance.</param>
    /// <param name="stop">停靠点 / Stop.</param>
    /// <returns>需求 / The demand.</returns>
    private static Quantity<V> DemandOf<V>(VrptwInstance<V> instance, RouteStop<V> stop)
        where V : struct, IRealNumber<V> =>
        instance.CustomerByNodeId.TryGetValue(stop.NodeId, out Customer<V>? customer)
            ? customer.Demand
            : new Quantity<V>(stop.AccumulatedLoad.Value.Constants.Zero, instance.Units.LoadUnit);

    /// <summary>负载量能否换算到实例负载单位 / Whether the load quantity converts to the instance load unit.</summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="stop">停靠点 / Stop.</param>
    /// <param name="instance">实例 / Instance.</param>
    /// <returns>是否可换算 / Whether it converts.</returns>
    private static bool ValidLoadField<V>(RouteStop<V> stop, VrptwInstance<V> instance)
        where V : struct, IRealNumber<V> =>
        stop.AccumulatedLoad.ConvertTo(instance.Units.LoadUnit).IsOk;

    /// <summary>按左侧单位相加 / Add in the left operand's unit.</summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="lhs">左操作数 / Left operand.</param>
    /// <param name="rhs">右操作数 / Right operand.</param>
    /// <returns>和，单位不可换算时为 null / The sum, or null when units are incompatible.</returns>
    private static Quantity<V>? Add<V>(Quantity<V> lhs, Quantity<V> rhs) where V : struct, IRealNumber<V> {
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> normalized = rhs.ConvertTo(lhs.Unit);
        return normalized is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>>
            ? null
            : new Quantity<V>(lhs.Value.Plus(normalized.Value.Value), lhs.Unit);
    }

    /// <summary>按左侧单位判断相等 / Compare for equality in the left operand's unit.</summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="lhs">左操作数 / Left operand.</param>
    /// <param name="rhs">右操作数 / Right operand.</param>
    /// <returns>是否相等 / Whether they are equal.</returns>
    private static bool Same<V>(Quantity<V> lhs, Quantity<V> rhs) where V : struct, IRealNumber<V> {
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> normalized = rhs.ConvertTo(lhs.Unit);
        return normalized.IsOk && lhs.Value.CompareTo(normalized.Value.Value) == 0;
    }

    /// <summary>
    /// 按容差比较两个量 / Compare two quantities within tolerance.
    /// </summary>
    /// <remarks>
    /// **用对称边界而不是减法取绝对值**：`V` 只保证排序与加法，不保证减法存在（无符号值就没有），因此
    /// `actual ≤ expected + tol` 且 `expected ≤ actual + tol` 是等价且类型安全的绝对差检查。
    /// **Symmetric bounds rather than a subtraction and absolute value**: `V` guarantees ordering and addition but not
    /// subtraction (unsigned values have none), so `actual ≤ expected + tol` and `expected ≤ actual + tol` is an equivalent,
    /// type-safe absolute-difference check.
    /// </remarks>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="actual">实际值 / Actual value.</param>
    /// <param name="expected">期望值 / Expected value.</param>
    /// <param name="instance">实例 / Instance.</param>
    /// <param name="adapter">solver 数值适配器 / Solver value adapter.</param>
    /// <param name="isDistance">是否比较距离而非成本 / Whether comparing distance instead of cost.</param>
    /// <returns>是否在容差内 / Whether within tolerance.</returns>
    private static bool WithinTolerance<V>(
        Quantity<V> actual,
        Quantity<V> expected,
        VrptwInstance<V> instance,
        INetworkSchedulingSolverValueAdapter<V> adapter,
        bool isDistance) where V : struct, IRealNumber<V> {
        PhysicalUnit unit = isDistance ? instance.Units.DistanceUnit : instance.Units.CostUnit;
        Result<V, ErrorCode, Error<ErrorCode>> actualValue = adapter.NormalizeValue(actual, unit);
        if (actualValue is Failed<V, ErrorCode, Error<ErrorCode>>) {
            return false;
        }
        Result<V, ErrorCode, Error<ErrorCode>> expectedValue = adapter.NormalizeValue(expected, unit);
        if (expectedValue is Failed<V, ErrorCode, Error<ErrorCode>>) {
            return false;
        }
        Result<V, ErrorCode, Error<ErrorCode>> tolerance = adapter.FromSolverValue(instance.Tolerances.CostValidation);
        if (tolerance is Failed<V, ErrorCode, Error<ErrorCode>>) {
            return false;
        }
        V upperActual = expectedValue.Value.Plus(tolerance.Value);
        V upperExpected = actualValue.Value.Plus(tolerance.Value);
        return actualValue.Value.CompareTo(upperActual) <= 0
            && expectedValue.Value.CompareTo(upperExpected) <= 0;
    }

    /// <summary>构造路线级失败 / Build a route-level failure.</summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="route">路线 / Route.</param>
    /// <param name="chinese">中文说明 / Chinese description.</param>
    /// <param name="english">英文说明 / English description.</param>
    /// <returns>失败结果 / The failure.</returns>
    private static Try RouteFailure<V>(Route<V> route, string chinese, string english)
        where V : struct, IRealNumber<V> =>
        NetworkSchedulingErrors.TryFailure(
            $"路线校验失败：路线 {route.Signature} {chinese} / Route validation failed: route {route.Signature} {english}");

    /// <summary>构造停靠点级失败 / Build a stop-level failure.</summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="route">路线 / Route.</param>
    /// <param name="stop">停靠点 / Stop.</param>
    /// <param name="chinese">中文说明 / Chinese description.</param>
    /// <param name="english">英文说明 / English description.</param>
    /// <returns>失败结果 / The failure.</returns>
    private static Try StopFailure<V>(Route<V> route, RouteStop<V> stop, string chinese, string english)
        where V : struct, IRealNumber<V> =>
        NetworkSchedulingErrors.TryFailure(
            $"路线校验失败：路线 {route.Signature} 节点 {stop.NodeId.Value} {chinese} / Route validation failed: route "
            + $"{route.Signature}, node {stop.NodeId.Value} {english}");

    /// <summary>构造弧级失败 / Build an arc-level failure.</summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="route">路线 / Route.</param>
    /// <param name="from">起点 / Origin.</param>
    /// <param name="to">终点 / Destination.</param>
    /// <param name="chinese">中文说明 / Chinese description.</param>
    /// <param name="english">英文说明 / English description.</param>
    /// <returns>失败结果 / The failure.</returns>
    private static Try ArcFailure<V>(
        Route<V> route,
        NetworkNode<V> from,
        NetworkNode<V> to,
        string chinese,
        string english) where V : struct, IRealNumber<V> =>
        NetworkSchedulingErrors.TryFailure(
            $"路线校验失败：路线 {route.Signature} 弧 {from.Id.Value}->{to.Id.Value} {chinese} / Route validation failed: "
            + $"route {route.Signature}, arc {from.Id.Value}->{to.Id.Value} {english}");
}
