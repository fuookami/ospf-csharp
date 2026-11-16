#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Example.CoreDemo.Demo17;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Infrastructure;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo5;

/// <summary>
/// 把仓库现有 Demo17 数据转换为可复现的 VRPTW 夹具 / Convert repository Demo17 data into reproducible VRPTW fixtures.
/// </summary>
/// <remarks>
///
/// **客户数量必须落在 `1..可用客户数` 内**。越界时返回失败而不是截断，因为截断会让"要 100 个客户"悄悄变成
/// "5 个客户"，**而实例名仍然写着 `demo17-100`**。
///
/// **The customer count must fall inside `1..available`.** An out-of-range count fails instead of truncating, because truncation turns "give me 100
/// customers" into "5 customers" silently, **while the instance name still says `demo17-100`**.
///
/// **与数据规模偏离**：标准 `Demo17` 有 100 个需求节点与 2 个不同的车辆规格；仓库内的 C# `Demo17` 是简化版
/// （5 个需求节点、3 辆同规格车）。适配逻辑一致，规模不同——这里不为凑数字去编造 100 个节点。
///
/// **Scale divergence**: the standard `Demo17` has 100 demand nodes and two distinct vehicle specifications; the in-repo C# `Demo17`
/// is the simplified version (5 demand nodes, 3 identically specified vehicles). The adaptation logic matches, the scale does not — not
/// invent 100 nodes just to match a count.
/// </remarks>
public static class Demo17InstanceAdapter {
    /// <summary>
    /// 从 Demo17 的前缀客户集创建实例 / Create an instance from a prefix of Demo17 customers.
    /// </summary>
    /// <param name="customerCount">客户数量 / Number of customers.</param>
    /// <param name="schedulingWindow">业务绝对时间轴与 solver 数值转换 / Business absolute timeline and solver numeric conversion.</param>
    /// <returns>对应实例或输入失败 / The matching instance, or an input failure.</returns>
    public static Result<VrptwInstance<Flt64>, ErrorCode, Error<ErrorCode>> Create(
        int customerCount,
        TimeWindow<Flt64> schedulingWindow) {
        ArgumentNullException.ThrowIfNull(schedulingWindow);

        IReadOnlyList<CustomerNode> demandNodes = VrptwDemo.CustomerNodes;
        if (customerCount < 1 || customerCount > demandNodes.Count) {
            return AdapterFailure<VrptwInstance<Flt64>>(
                $"客户数量 {customerCount} 超出 1..{demandNodes.Count}",
                $"customer count {customerCount} is outside 1..{demandNodes.Count}");
        }

        Result<VrptwUnits, ErrorCode, Error<ErrorCode>> units = VrptwUnits.Invoke(
            SIBaseUnits.Meter, SIBaseUnits.Kilogram, NoneUnit.Instance);
        if (units is not Ok<VrptwUnits, ErrorCode, Error<ErrorCode>> unitsOk) {
            return AdapterFailure<VrptwInstance<Flt64>>(
                "单位配置非法", "unit configuration is invalid");
        }

        Result<NetworkNode<Flt64>, ErrorCode, Error<ErrorCode>> startNode =
            NetworkNodeOf(VrptwDemo.OriginNode, "demo17-start");
        if (startNode is not Ok<NetworkNode<Flt64>, ErrorCode, Error<ErrorCode>> startOk) {
            return AdapterFailure<VrptwInstance<Flt64>>(
                "起始仓库节点创建失败", "origin node creation failed");
        }
        Result<NetworkNode<Flt64>, ErrorCode, Error<ErrorCode>> endNode =
            NetworkNodeOf(VrptwDemo.EndNode, "demo17-end");
        if (endNode is not Ok<NetworkNode<Flt64>, ErrorCode, Error<ErrorCode>> endOk) {
            return AdapterFailure<VrptwInstance<Flt64>>(
                "结束仓库节点创建失败", "end-depot node creation failed");
        }
        Result<ServiceTimeWindow, ErrorCode, Error<ErrorCode>> startWindow =
            TimeWindowOf(VrptwDemo.OriginNode, schedulingWindow);
        if (startWindow is not Ok<ServiceTimeWindow, ErrorCode, Error<ErrorCode>> startWindowOk) {
            return AdapterFailure<VrptwInstance<Flt64>>(
                "起始仓库时间窗创建失败", "origin time-window creation failed");
        }
        Result<ServiceTimeWindow, ErrorCode, Error<ErrorCode>> endWindow =
            TimeWindowOf(VrptwDemo.EndNode, schedulingWindow);
        if (endWindow is not Ok<ServiceTimeWindow, ErrorCode, Error<ErrorCode>> endWindowOk) {
            return AdapterFailure<VrptwInstance<Flt64>>(
                "结束仓库时间窗创建失败", "end-depot time-window creation failed");
        }

        var customers = new List<Customer<Flt64>>(customerCount);
        for (int index = 0; index < customerCount; ++index) {
            CustomerNode source = demandNodes[index];
            int number = index + 1;
            Result<NetworkNode<Flt64>, ErrorCode, Error<ErrorCode>> node =
                NetworkNodeOf(source, $"demo17-customer-{number}");
            if (node is not Ok<NetworkNode<Flt64>, ErrorCode, Error<ErrorCode>> nodeOk) {
                return AdapterFailure<VrptwInstance<Flt64>>(
                    $"客户 {number} 节点创建失败", $"node creation failed for customer {number}");
            }
            Result<ServiceTimeWindow, ErrorCode, Error<ErrorCode>> window =
                TimeWindowOf(source, schedulingWindow);
            if (window is not Ok<ServiceTimeWindow, ErrorCode, Error<ErrorCode>> windowOk) {
                return AdapterFailure<VrptwInstance<Flt64>>(
                    $"客户 {number} 时间窗创建失败",
                    $"time-window creation failed for customer {number}");
            }
            // **服务时长取 0**：仓库内的简化 Demo17 没有服务时长字段，而`serviceTime` 在原始数据里也普遍为 0。
            // 编一个非零值会让时间窗推演与原始数据不一致，**而这种不一致只在跨实例比对时才会暴露**。
            // **Service time is zero**: the in-repo simplified Demo17 has no service-time field, and its `serviceTime` is zero throughout the
            // raw data too. Inventing a non-zero value would desynchronise the time-window propagation from the raw data, **and that desynchronisation
            // only shows up when comparing across instances**.
            Result<Customer<Flt64>, ErrorCode, Error<ErrorCode>> customer = Customer<Flt64>.Invoke(
                id: new CustomerId($"demo17-customer-{number}"),
                node: nodeOk.Value,
                demand: new Quantity<Flt64>(
                    new Flt64(source.Demand), SIBaseUnits.Kilogram),
                timeWindow: windowOk.Value,
                serviceTime: TimeSpan.Zero);
            if (customer is not Ok<Customer<Flt64>, ErrorCode, Error<ErrorCode>> customerOk) {
                return AdapterFailure<VrptwInstance<Flt64>>(
                    $"客户 {number} 校验失败", $"validation failed for customer {number}");
            }
            customers.Add(customerOk.Value);
        }

        // **车辆按 (容量, 固定成本) 归组**：同规格车辆合并成一个车辆类型，`amount` 是组内数量。
        // 不归组会让每种车各占一个类型，而**分支定价的车辆类型数量会直接乘进分支维度**。
        // **Vehicles group by (capacity, fixed cost)**: identically specified vehicles collapse into one vehicle type whose `amount` is the group size.
        // Not grouping gives each vehicle its own type, and **the vehicle-type count multiplies straight into the branching dimension**.
        var vehicleTypes = new List<VehicleType<Flt64>>();
        foreach (IGrouping<(int Capacity, Flt64 FixedCost), Vehicle> group
                 in VrptwDemo.VehicleList.GroupBy(vehicle => (vehicle.Capacity, vehicle.FixedCost))) {
            Result<VehicleType<Flt64>, ErrorCode, Error<ErrorCode>> vehicleType =
                VehicleType<Flt64>.Invoke(
                    id: new VehicleTypeId($"demo17-vehicle-type-{vehicleTypes.Count + 1}"),
                    capacity: new Quantity<Flt64>(
                        new Flt64(group.Key.Capacity), SIBaseUnits.Kilogram),
                    fixedCost: new Quantity<Flt64>(
                        group.Key.FixedCost, NoneUnit.Instance),
                    amount: group.Count());
            if (vehicleType is not Ok<VehicleType<Flt64>, ErrorCode, Error<ErrorCode>> typeOk) {
                return AdapterFailure<VrptwInstance<Flt64>>(
                    $"车辆类型 {vehicleTypes.Count + 1} 校验失败",
                    $"validation failed for vehicle type {vehicleTypes.Count + 1}");
            }
            vehicleTypes.Add(typeOk.Value);
        }

        return VrptwInstance<Flt64>.Invoke(
            name: $"demo17-{customerCount}",
            startDepot: new Depot<Flt64>(startOk.Value, startWindowOk.Value),
            endDepot: new Depot<Flt64>(endOk.Value, endWindowOk.Value),
            customers: customers,
            vehicleTypes: vehicleTypes,
            schedulingWindow: schedulingWindow,
            units: unitsOk.Value,
            tolerances: VrptwTolerances.Default);
    }

    private static Result<NetworkNode<Flt64>, ErrorCode, Error<ErrorCode>> NetworkNodeOf(
        CustomerNode source,
        string id) =>
        NetworkNode<Flt64>.Invoke(
            id: new NetworkNodeId(id),
            attributes: new Dictionary<string, Quantity<Flt64>>(StringComparer.Ordinal) {
                ["x"] = new Quantity<Flt64>(source.PosX, SIBaseUnits.Meter),
                ["y"] = new Quantity<Flt64>(source.PosY, SIBaseUnits.Meter),
            });

    private static Result<ServiceTimeWindow, ErrorCode, Error<ErrorCode>> TimeWindowOf(
        CustomerNode source,
        TimeWindow<Flt64> schedulingWindow) =>
        ServiceTimeWindow.Invoke(
            readyTime: schedulingWindow.InstantOf(source.TimeWindowStart),
            dueTime: schedulingWindow.InstantOf(source.TimeWindowEnd));

    private static Result<T, ErrorCode, Error<ErrorCode>> AdapterFailure<T>(
        string chinese,
        string english) =>
        NetworkSchedulingErrors.Failure<T>(
            $"转换 Demo17 数据失败：{chinese} / Failed to adapt Demo17 data: {english}");
}
