// 初始路线生成器 / Initial route generator.
//
// 为 RMP 提供初始可行列。启发式：每个客户 × 每种车辆类型生成一条单客户路线，经 `RouteValidator` 独立复核后返回，并过滤与分支
// 遮罩不兼容的路线。
// Supplies initial feasible columns to the RMP. Heuristic: one single-customer route per customer × vehicle-type pair,
// independently re-validated through `RouteValidator`, with branch-mask-incompatible routes filtered out.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
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

namespace Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteGeneration.Service;

/// <summary>
/// 初始路线生成器 / Initial route generator.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class InitialRouteGenerator<V>
    where V : struct, IRealNumber<V> {
    private readonly VrptwInstance<V> _instance;
    private readonly INetworkSchedulingSolverValueAdapter<V> _valueAdapter;
    private readonly IDistanceCalculator<V> _distanceCalculator;
    private readonly ITravelTimeCalculator<V> _travelTimeCalculator;
    private readonly IArcCostCalculator<V> _arcCostCalculator;
    private readonly IRouteCostPolicy<V> _routeCostPolicy;
    private readonly IArcFeasibilityPolicy<V> _arcFeasibilityPolicy;
    private readonly ResourceGraph<VehicleTypeId>? _resourceGraph;

    /// <summary>
    /// 构造生成器 / Construct the generator.
    /// </summary>
    /// <param name="instance">VRPTW 实例 / VRPTW instance.</param>
    /// <param name="valueAdapter">solver 数值适配器 / Solver value adapter.</param>
    /// <param name="distanceCalculator">距离策略 / Distance policy.</param>
    /// <param name="travelTimeCalculator">时间策略 / Travel-time policy.</param>
    /// <param name="arcCostCalculator">弧成本策略 / Arc-cost policy.</param>
    /// <param name="routeCostPolicy">路线成本策略 / Route-cost policy.</param>
    /// <param name="arcFeasibilityPolicy">弧可行性策略 / Arc-feasibility policy.</param>
    /// <param name="resourceGraph">资源图 / Resource graph.</param>
    public InitialRouteGenerator(
        VrptwInstance<V> instance,
        INetworkSchedulingSolverValueAdapter<V> valueAdapter,
        IDistanceCalculator<V> distanceCalculator,
        ITravelTimeCalculator<V> travelTimeCalculator,
        IArcCostCalculator<V> arcCostCalculator,
        IRouteCostPolicy<V> routeCostPolicy,
        IArcFeasibilityPolicy<V>? arcFeasibilityPolicy = null,
        ResourceGraph<VehicleTypeId>? resourceGraph = null) {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(valueAdapter);
        ArgumentNullException.ThrowIfNull(distanceCalculator);
        ArgumentNullException.ThrowIfNull(travelTimeCalculator);
        ArgumentNullException.ThrowIfNull(arcCostCalculator);
        ArgumentNullException.ThrowIfNull(routeCostPolicy);
        _instance = instance;
        _valueAdapter = valueAdapter;
        _distanceCalculator = distanceCalculator;
        _travelTimeCalculator = travelTimeCalculator;
        _arcCostCalculator = arcCostCalculator;
        _routeCostPolicy = routeCostPolicy;
        _arcFeasibilityPolicy = arcFeasibilityPolicy ?? new DefaultArcFeasibilityPolicy<V>();
        _resourceGraph = resourceGraph;
    }

    /// <summary>
    /// 生成初始路线 / Generate initial routes.
    /// </summary>
    /// <remarks>
    /// **启发式失败不是致命错误**，返回空列表即可——RMP 拿不到初始列只会退化为别的初始化方式，不会算错。因此这里的每条
    /// `return null` 都是"放弃这一候选"，而不是报错。
    /// **Heuristic failure is not fatal**; returning an empty list is acceptable — a master without initial columns merely falls
    /// back to another initialisation, it does not compute a wrong answer. Every `return null` below means "drop this candidate",
    /// not "error".
    /// </remarks>
    /// <param name="branchMask">分支遮罩 / Branch mask.</param>
    /// <returns>初始可行路线 / Initial feasible routes.</returns>
    public IReadOnlyList<Route<V>> Generate(BranchMask<VehicleTypeId>? branchMask = null) {
        var routes = new List<Route<V>>();

        foreach (Customer<V> customer in _instance.Customers) {
            foreach (VehicleType<V> vehicleType in _instance.VehicleTypes) {
                if (branchMask is not null && !branchMask.AllowsNode(vehicleType.Id, customer.Node.Id)) {
                    continue;
                }
                IReadOnlyList<ResourceArc<VehicleTypeId>> startArcs = _resourceGraph is null
                    ? new[] {
                        new ResourceArc<VehicleTypeId>(
                            vehicleType.Id, _instance.StartDepot.Node.Id, customer.Node.Id),
                    }
                    : _resourceGraph.Between(vehicleType.Id, _instance.StartDepot.Node.Id, customer.Node.Id);
                IReadOnlyList<ResourceArc<VehicleTypeId>> endArcs = _resourceGraph is null
                    ? new[] {
                        new ResourceArc<VehicleTypeId>(
                            vehicleType.Id, customer.Node.Id, _instance.EndDepot.Node.Id),
                    }
                    : _resourceGraph.Between(vehicleType.Id, customer.Node.Id, _instance.EndDepot.Node.Id);

                foreach (ResourceArc<VehicleTypeId> startArc in startArcs) {
                    if (branchMask is not null && !branchMask.AllowsArc(startArc)) {
                        continue;
                    }
                    foreach (ResourceArc<VehicleTypeId> endArc in endArcs) {
                        if (branchMask is not null && !branchMask.AllowsArc(endArc)) {
                            continue;
                        }
                        Route<V>? route = BuildSingleCustomerRoute(
                            customer, vehicleType, startArc.Id, endArc.Id);
                        if (route is null) {
                            continue;
                        }
                        if (branchMask is not null
                            && !branchMask.IsRouteCompatible(
                                vehicleType.Id,
                                route.Stops.Select(stop => stop.NodeId).ToList(),
                                route.ArcIds)) {
                            continue;
                        }

                        Try validation = RouteValidator.Validate(
                            _instance, route, _distanceCalculator, _travelTimeCalculator,
                            _arcCostCalculator, _routeCostPolicy, _valueAdapter);
                        if (validation.IsOk) {
                            routes.Add(route);
                        }
                    }
                }
            }
        }

        return routes;
    }

    /// <summary>
    /// 构建单客户路线：起始仓库 → 客户 → 结束仓库 / Build a single-customer route.
    /// </summary>
    /// <param name="customer">客户 / The customer.</param>
    /// <param name="vehicleType">车辆类型 / Vehicle type.</param>
    /// <param name="startArcId">起始弧标识 / Start-arc identity.</param>
    /// <param name="endArcId">结束弧标识 / End-arc identity.</param>
    /// <returns>路线，不可行时为 null / The route, or null when infeasible.</returns>
    private Route<V>? BuildSingleCustomerRoute(
        Customer<V> customer,
        VehicleType<V> vehicleType,
        NetworkArcId startArcId,
        NetworkArcId endArcId) {
        TimeWindow<V> schedulingWindow = _instance.SchedulingWindow;
        V zero = vehicleType.Capacity.Value.Constants.Zero;

        Depot<V> startDepot = _instance.StartDepot;
        Depot<V> endDepot = _instance.EndDepot;
        var startArc = new ResourceArc<VehicleTypeId>(
            vehicleType.Id, startDepot.Node.Id, customer.Node.Id, startArcId);
        var endArc = new ResourceArc<VehicleTypeId>(
            vehicleType.Id, customer.Node.Id, endDepot.Node.Id, endArcId);

        Result<bool, ErrorCode, Error<ErrorCode>> startFeasible =
            _arcFeasibilityPolicy.IsFeasible(startArc, startDepot.Node, customer.Node, vehicleType);
        if (startFeasible is Failed<bool, ErrorCode, Error<ErrorCode>> || !startFeasible.Value) {
            return null;
        }
        Result<bool, ErrorCode, Error<ErrorCode>> endFeasible =
            _arcFeasibilityPolicy.IsFeasible(endArc, customer.Node, endDepot.Node, vehicleType);
        if (endFeasible is Failed<bool, ErrorCode, Error<ErrorCode>> || !endFeasible.Value) {
            return null;
        }

        if (TimeValue(schedulingWindow, startDepot.TimeWindow.ReadyTime) is not { } currentTime) {
            return null;
        }
        V currentLoad = zero;
        V totalDistance = zero;
        var arcCosts = new List<Quantity<V>>();
        var stops = new List<RouteStop<V>>();

        // 起始仓库 / Start depot
        stops.Add(new RouteStop<V>(
            startDepot.Node.Id, null,
            schedulingWindow.InstantOf(currentTime.ToFlt64()),
            schedulingWindow.InstantOf(currentTime.ToFlt64()),
            schedulingWindow.InstantOf(currentTime.ToFlt64()),
            new Quantity<V>(zero, _instance.Units.LoadUnit)));

        // 起始仓库 → 客户 / Start depot → customer
        Result<TimeSpan, ErrorCode, Error<ErrorCode>> travelTime1 =
            _travelTimeCalculator.TravelTime(startArc, startDepot.Node, customer.Node, vehicleType);
        if (travelTime1 is Failed<TimeSpan, ErrorCode, Error<ErrorCode>>) {
            return null;
        }
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> distance1 =
            _distanceCalculator.Distance(startArc, startDepot.Node, customer.Node);
        if (distance1 is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>>) {
            return null;
        }
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> arcCost1 = _arcCostCalculator.Cost(
            startArc, startDepot.Node, customer.Node, distance1.Value, travelTime1.Value, vehicleType);
        if (arcCost1 is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>>) {
            return null;
        }
        arcCosts.Add(arcCost1.Value);
        if (Normalized(distance1.Value, _instance.Units.DistanceUnit) is not { } normalizedDistance1) {
            return null;
        }
        totalDistance = totalDistance.Plus(normalizedDistance1);

        if (TimeValue(schedulingWindow, travelTime1.Value) is not { } travel1) {
            return null;
        }
        V arrival1 = currentTime.Plus(travel1);
        if (TimeValue(schedulingWindow, customer.TimeWindow.ReadyTime) is not { } customerReady) {
            return null;
        }
        V serviceStart1 = MaxOf(arrival1, customerReady);
        if (TimeValue(schedulingWindow, customer.TimeWindow.DueTime) is not { } customerDue
            || serviceStart1.CompareTo(customerDue) > 0) {
            return null;
        }

        if (Normalized(customer.Demand, _instance.Units.LoadUnit) is not { } demand) {
            return null;
        }
        currentLoad = currentLoad.Plus(demand);
        if (Normalized(vehicleType.Capacity, _instance.Units.LoadUnit) is not { } vehicleCapacity
            || currentLoad.CompareTo(vehicleCapacity) > 0) {
            return null;
        }

        if (TimeValue(schedulingWindow, customer.ServiceTime) is not { } serviceDuration) {
            return null;
        }
        V departure1 = serviceStart1.Plus(serviceDuration);
        currentTime = departure1;

        stops.Add(new RouteStop<V>(
            customer.Node.Id, customer.Id,
            schedulingWindow.InstantOf(arrival1.ToFlt64()),
            schedulingWindow.InstantOf(serviceStart1.ToFlt64()),
            schedulingWindow.InstantOf(departure1.ToFlt64()),
            new Quantity<V>(currentLoad, _instance.Units.LoadUnit)));

        // 客户 → 结束仓库 / Customer → end depot
        Result<TimeSpan, ErrorCode, Error<ErrorCode>> travelTime2 =
            _travelTimeCalculator.TravelTime(endArc, customer.Node, endDepot.Node, vehicleType);
        if (travelTime2 is Failed<TimeSpan, ErrorCode, Error<ErrorCode>>) {
            return null;
        }
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> distance2 =
            _distanceCalculator.Distance(endArc, customer.Node, endDepot.Node);
        if (distance2 is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>>) {
            return null;
        }
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> arcCost2 = _arcCostCalculator.Cost(
            endArc, customer.Node, endDepot.Node, distance2.Value, travelTime2.Value, vehicleType);
        if (arcCost2 is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>>) {
            return null;
        }
        arcCosts.Add(arcCost2.Value);
        if (Normalized(distance2.Value, _instance.Units.DistanceUnit) is not { } normalizedDistance2) {
            return null;
        }
        totalDistance = totalDistance.Plus(normalizedDistance2);

        if (TimeValue(schedulingWindow, travelTime2.Value) is not { } travel2) {
            return null;
        }
        V arrival2 = currentTime.Plus(travel2);
        if (TimeValue(schedulingWindow, endDepot.TimeWindow.ReadyTime) is not { } endReady) {
            return null;
        }
        V serviceStart2 = MaxOf(arrival2, endReady);
        if (TimeValue(schedulingWindow, endDepot.TimeWindow.DueTime) is not { } endDue
            || serviceStart2.CompareTo(endDue) > 0) {
            return null;
        }

        stops.Add(new RouteStop<V>(
            endDepot.Node.Id, null,
            schedulingWindow.InstantOf(arrival2.ToFlt64()),
            schedulingWindow.InstantOf(serviceStart2.ToFlt64()),
            schedulingWindow.InstantOf(serviceStart2.ToFlt64()),
            new Quantity<V>(currentLoad, _instance.Units.LoadUnit)));

        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> cost =
            _routeCostPolicy.Cost(vehicleType, arcCosts);
        if (cost is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>>) {
            return null;
        }

        Result<Route<V>, ErrorCode, Error<ErrorCode>> route = Route<V>.Invoke(
            vehicleType.Id,
            stops,
            new Quantity<V>(totalDistance, _instance.Units.DistanceUnit),
            cost.Value,
            new[] { startArcId, endArcId });
        return route is Failed<Route<V>, ErrorCode, Error<ErrorCode>> ? null : route.Value;
    }

    /// <summary>
    /// 用 solver 适配器归一化物理量 / Normalise a quantity through the solver adapter.
    /// </summary>
    /// <param name="quantity">物理量 / The quantity.</param>
    /// <param name="unit">目标单位 / The target unit.</param>
    /// <returns>归一化数值，失败时为 null / The normalised value, or null on failure.</returns>
    private V? Normalized(Quantity<V> quantity, PhysicalUnit unit) {
        Result<V, ErrorCode, Error<ErrorCode>> result = _valueAdapter.NormalizeValue(quantity, unit);
        return result is Failed<V, ErrorCode, Error<ErrorCode>> ? null : result.Value;
    }

    /// <summary>
    /// 把时刻换算到业务时间轴坐标 / Map an instant into business-timeline coordinates.
    /// </summary>
    /// <param name="schedulingWindow">业务时间轴 / Business timeline.</param>
    /// <param name="instant">时间点 / The instant.</param>
    /// <returns>时间轴坐标值，失败时为 null / The value in timeline coordinates, or null on failure.</returns>
    private V? TimeValue(TimeWindow<V> schedulingWindow, DateTimeOffset instant) {
        Result<V, ErrorCode, Error<ErrorCode>> result =
            _valueAdapter.FromSolverValue(schedulingWindow.ValueOf(instant));
        return result is Failed<V, ErrorCode, Error<ErrorCode>> ? null : result.Value;
    }

    /// <summary>
    /// 把时长换算到业务时间轴坐标 / Map a duration into business-timeline coordinates.
    /// </summary>
    /// <param name="schedulingWindow">业务时间轴 / Business timeline.</param>
    /// <param name="duration">时长 / The duration.</param>
    /// <returns>时间轴坐标值，失败时为 null / The value in timeline coordinates, or null on failure.</returns>
    private V? TimeValue(TimeWindow<V> schedulingWindow, TimeSpan duration) {
        Result<V, ErrorCode, Error<ErrorCode>> result =
            _valueAdapter.FromSolverValue(schedulingWindow.ValueOf(duration));
        return result is Failed<V, ErrorCode, Error<ErrorCode>> ? null : result.Value;
    }

    /// <summary>
    /// 取两个值的较大者 / Take the larger of two values.
    /// </summary>
    /// <param name="left">左值 / Left value.</param>
    /// <param name="right">右值 / Right value.</param>
    /// <returns>较大者 / The larger value.</returns>
    private static V MaxOf(V left, V right) => left.CompareTo(right) >= 0 ? left : right;
}
