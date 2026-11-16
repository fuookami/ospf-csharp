// VRPTW 实例一致性校验 / VRPTW instance consistency validation.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Infrastructure;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Policy;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Service;

/// <summary>
/// VRPTW 实例一致性校验器 / VRPTW instance consistency validator.
/// </summary>
/// <remarks>
/// **只报第一个失败，而不是收集全部**：实例构造是"要么全对、要么不造"，逐个报告会让调用方以为修完一处就能通过，
/// 实际上还要再跑十几轮。第一个失败已经足够定位问题，剩下的一次修完即可。
/// **Only the first failure is reported rather than collecting them all**: instance construction is all-or-nothing, and
/// reporting them one at a time would suggest fixing a single item suffices when a dozen more rounds remain. The first
/// failure is enough to locate the problem.
/// </remarks>
public static class VrptwValidator {
    /// <summary>
    /// 校验输入并创建不可变实例 / Validate inputs and create an immutable instance.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="name">实例名称 / Instance name.</param>
    /// <param name="startDepot">起始仓库 / Start depot.</param>
    /// <param name="endDepot">结束仓库 / End depot.</param>
    /// <param name="customers">客户 / Customers.</param>
    /// <param name="vehicleTypes">车辆类型 / Vehicle types.</param>
    /// <param name="schedulingWindow">业务绝对时间轴 / Business absolute timeline.</param>
    /// <param name="units">归一化单位 / Normalized units.</param>
    /// <param name="tolerances">算法容差 / Algorithm tolerances.</param>
    /// <returns>实例或校验失败 / The instance, or a validation failure.</returns>
    public static Result<VrptwInstance<V>, ErrorCode, Error<ErrorCode>> Create<V>(
        string name,
        Depot<V> startDepot,
        Depot<V> endDepot,
        IReadOnlyList<Customer<V>> customers,
        IReadOnlyList<VehicleType<V>> vehicleTypes,
        TimeWindow<V> schedulingWindow,
        VrptwUnits units,
        VrptwTolerances tolerances) where V : struct, IRealNumber<V> {
        string? failure = FirstFailure(
            name, startDepot, endDepot, customers, vehicleTypes, schedulingWindow, units);
        if (failure is not null) {
            return NetworkSchedulingErrors.Failure<VrptwInstance<V>>(failure);
        }
        return Results.Ok(VrptwInstance<V>.Validated(
            name, startDepot, endDepot, customers, vehicleTypes, schedulingWindow, units, tolerances));
    }

    /// <summary>
    /// 重新校验已有实例 / Revalidate an existing instance.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="instance">VRPTW 实例 / VRPTW instance.</param>
    /// <returns>成功或校验失败 / Success, or a validation failure.</returns>
    public static Try Validate<V>(VrptwInstance<V> instance) where V : struct, IRealNumber<V> {
        ArgumentNullException.ThrowIfNull(instance);

        string? failure = FirstFailure(
            instance.Name, instance.StartDepot, instance.EndDepot, instance.Customers,
            instance.VehicleTypes, instance.SchedulingWindow, instance.Units);
        return failure is null ? Results.OkInstance : NetworkSchedulingErrors.TryFailure(failure);
    }

    /// <summary>
    /// 返回第一个校验失败说明，全部通过时为 null / Return the first validation failure message, or null when everything passes.
    /// </summary>
    /// <remarks>
    /// 校验顺序按"越基础越靠前"排列：名称 → 非空集合 → 仓库不同 → 标识唯一 → 坐标轴 → 坐标单位 → 时间轴 →
    /// 服务窗在时间轴内 → 需求可被某车型承载 → 车辆单位兼容。
    /// **容量可行性排在单位检查之前**：客户需求是否能被某车型装下，是实例是否可解的核心判断；先报出来比先报单位问题更有用，
    /// 因为单位问题往往一处改动就能全好，而容量不可行意味着数据本身需要重新设计。
    /// The order runs from the most fundamental outward: name → non-empty collections → distinct depots → unique identities →
    /// coordinate axes → coordinate units → timeline → service windows inside the timeline → demand carried by some vehicle →
    /// vehicle units compatible.
    /// **Capacity feasibility precedes the unit checks**: whether every demand fits some vehicle is the core question of
    /// solvability, and reporting it first is more useful — a unit problem is usually one fix, whereas infeasible capacity
    /// means the data needs redesign.
    /// </remarks>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="name">实例名称 / Instance name.</param>
    /// <param name="startDepot">起始仓库 / Start depot.</param>
    /// <param name="endDepot">结束仓库 / End depot.</param>
    /// <param name="customers">客户 / Customers.</param>
    /// <param name="vehicleTypes">车辆类型 / Vehicle types.</param>
    /// <param name="schedulingWindow">业务绝对时间轴 / Business absolute timeline.</param>
    /// <param name="units">归一化单位 / Normalized units.</param>
    /// <returns>失败说明或 null / The failure message, or null.</returns>
    private static string? FirstFailure<V>(
        string name,
        Depot<V> startDepot,
        Depot<V> endDepot,
        IReadOnlyList<Customer<V>> customers,
        IReadOnlyList<VehicleType<V>> vehicleTypes,
        TimeWindow<V> schedulingWindow,
        VrptwUnits units) where V : struct, IRealNumber<V> {
        ArgumentNullException.ThrowIfNull(startDepot);
        ArgumentNullException.ThrowIfNull(endDepot);
        ArgumentNullException.ThrowIfNull(customers);
        ArgumentNullException.ThrowIfNull(vehicleTypes);
        ArgumentNullException.ThrowIfNull(schedulingWindow);
        ArgumentNullException.ThrowIfNull(units);

        if (string.IsNullOrWhiteSpace(name)) {
            return "创建 VRPTW 实例失败：实例名称不能为空 / Failed to create VRPTW instance: instance name cannot be blank";
        }
        if (customers.Count == 0) {
            return "创建 VRPTW 实例失败：至少需要一个客户 / Failed to create VRPTW instance: at least one customer is "
                + "required";
        }
        if (vehicleTypes.Count == 0) {
            return "创建 VRPTW 实例失败：至少需要一种车辆类型 / Failed to create VRPTW instance: at least one vehicle type "
                + "is required";
        }
        if (startDepot.Node.Id.Equals(endDepot.Node.Id)) {
            return "创建 VRPTW 实例失败：起止仓库必须使用不同节点 ID / Failed to create VRPTW instance: start and end depots "
                + "must use different node IDs";
        }

        var customerIds = customers.Select(customer => customer.Id).ToList();
        if (customerIds.Distinct().Count() != customerIds.Count) {
            return "创建 VRPTW 实例失败：客户 ID 重复 / Failed to create VRPTW instance: duplicate customer ID";
        }
        var vehicleTypeIds = vehicleTypes.Select(vehicleType => vehicleType.Id).ToList();
        if (vehicleTypeIds.Distinct().Count() != vehicleTypeIds.Count) {
            return "创建 VRPTW 实例失败：车辆类型 ID 重复 / Failed to create VRPTW instance: duplicate vehicle-type ID";
        }

        var allNodeIds = new List<NetworkNodeId> { startDepot.Node.Id, endDepot.Node.Id };
        allNodeIds.AddRange(customers.Select(customer => customer.Node.Id));
        if (allNodeIds.Distinct().Count() != allNodeIds.Count) {
            return "创建 VRPTW 实例失败：网络节点 ID 重复 / Failed to create VRPTW instance: duplicate network node ID";
        }

        var nodes = new List<NetworkNode<V>> { startDepot.Node, endDepot.Node };
        nodes.AddRange(customers.Select(customer => customer.Node));
        var coordinateAxes = startDepot.Node.VrpCoordinateAttributes()
            .Select(pair => pair.Axis).ToList();
        if (coordinateAxes.Count == 0
            || nodes.Any(node => !node.VrpCoordinateAttributes().Select(pair => pair.Axis).SequenceEqual(coordinateAxes))) {
            return "创建 VRPTW 实例失败：所有节点必须具有相同且非空的坐标轴 / Failed to create VRPTW instance: all nodes "
                + "must have the same non-empty coordinate axes";
        }
        if (nodes.Any(node => node.VrpCoordinates().Any(quantity => !quantity.ConvertTo(units.DistanceUnit).IsOk))) {
            return "创建 VRPTW 实例失败：节点坐标无法转换到距离单位 / Failed to create VRPTW instance: node coordinates "
                + "cannot be converted to the distance unit";
        }

        if (schedulingWindow.Empty) {
            return "创建 VRPTW 实例失败：业务时间轴不能为空 / Failed to create VRPTW instance: business scheduling window "
                + "cannot be empty";
        }
        var serviceWindows = new List<ServiceTimeWindow> { startDepot.TimeWindow, endDepot.TimeWindow };
        serviceWindows.AddRange(customers.Select(customer => customer.TimeWindow));
        if (serviceWindows.Any(window =>
                window.ReadyTime < schedulingWindow.Window.Start || window.DueTime > schedulingWindow.Window.End)) {
            return "创建 VRPTW 实例失败：服务时间窗超出业务时间轴 / Failed to create VRPTW instance: service time window is "
                + "outside the business scheduling window";
        }

        foreach (Customer<V> customer in customers) {
            Result<Quantity<V>, ErrorCode, Error<ErrorCode>> demand = customer.Demand.ConvertTo(units.LoadUnit);
            if (demand is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>>) {
                return $"创建 VRPTW 实例失败：客户 {customer.Id.Value} 的需求单位不兼容 / Failed to create VRPTW instance: "
                    + $"demand unit of customer {customer.Id.Value} is incompatible";
            }
            bool supported = vehicleTypes.Any(vehicle => {
                Result<Quantity<V>, ErrorCode, Error<ErrorCode>> capacity = vehicle.Capacity.ConvertTo(units.LoadUnit);
                return capacity.IsOk && demand.Value.Value.CompareTo(capacity.Value.Value) <= 0;
            });
            if (!supported) {
                return $"创建 VRPTW 实例失败：客户 {customer.Id.Value} 的需求超过所有车辆容量 / Failed to create VRPTW "
                    + $"instance: demand of customer {customer.Id.Value} exceeds every vehicle capacity";
            }
        }

        foreach (VehicleType<V> vehicleType in vehicleTypes) {
            if (!vehicleType.Capacity.ConvertTo(units.LoadUnit).IsOk) {
                return $"创建 VRPTW 实例失败：车辆类型 {vehicleType.Id.Value} 的容量单位不兼容 / Failed to create VRPTW "
                    + $"instance: capacity unit of vehicle type {vehicleType.Id.Value} is incompatible";
            }
            if (!vehicleType.FixedCost.ConvertTo(units.CostUnit).IsOk) {
                return $"创建 VRPTW 实例失败：车辆类型 {vehicleType.Id.Value} 的成本单位不兼容 / Failed to create VRPTW "
                    + $"instance: cost unit of vehicle type {vehicleType.Id.Value} is incompatible";
            }
        }
        return null;
    }
}
