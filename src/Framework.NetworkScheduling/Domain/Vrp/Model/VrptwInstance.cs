// VRPTW 实例 / VRPTW instance.
// 与固定 Kotlin 基线 `VrptwInstance.kt`（96 行）对应。
// Mirrors the pinned baseline's `VrptwInstance.kt`.
//
// 实例是**已通过校验的不可变快照**：构造入口只有 `VrptwValidator`（公开的 `Invoke`）与 `Validated`（内部，给校验器用）。
// 因此下游拿到的 `VrptwInstance` 一定满足"客户唯一、节点唯一、坐标轴一致、时间窗在业务时间轴内、需求不超过某车型容量"
// 等一整套约束，不必在每个使用点重查。
// An instance is an **immutable snapshot that already passed validation**: the only construction routes are
// `VrptwValidator` (the public `Invoke`) and `Validated` (internal, for the validator). So anything downstream receives an
// instance that already satisfies the whole constraint set — unique customers, unique nodes, consistent coordinate axes,
// time windows inside the business timeline, demands within some vehicle capacity — with no need to re-check at each use site.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Service;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Model;

/// <summary>
/// 已完成单位与引用校验的 VRPTW 实例 / VRPTW instance with validated units and references.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class VrptwInstance<V> where V : struct, IRealNumber<V> {
    private VrptwInstance(
        string name,
        Depot<V> startDepot,
        Depot<V> endDepot,
        List<Customer<V>> customers,
        List<VehicleType<V>> vehicleTypes,
        TimeWindow<V> schedulingWindow,
        VrptwUnits units,
        VrptwTolerances tolerances) {
        Name = name;
        StartDepot = startDepot;
        EndDepot = endDepot;
        Customers = customers;
        VehicleTypes = vehicleTypes;
        SchedulingWindow = schedulingWindow;
        Units = units;
        Tolerances = tolerances;
        CustomerById = customers.ToDictionary(customer => customer.Id);
        CustomerByNodeId = customers.ToDictionary(customer => customer.Node.Id);
        VehicleTypeById = vehicleTypes.ToDictionary(vehicleType => vehicleType.Id);
    }

    /// <summary>实例名称 / Instance name.</summary>
    public string Name { get; }

    /// <summary>起始仓库 / Start depot.</summary>
    public Depot<V> StartDepot { get; }

    /// <summary>结束仓库 / End depot.</summary>
    public Depot<V> EndDepot { get; }

    /// <summary>客户快照 / Customer snapshot.</summary>
    public IReadOnlyList<Customer<V>> Customers { get; }

    /// <summary>车辆类型快照 / Vehicle-type snapshot.</summary>
    public IReadOnlyList<VehicleType<V>> VehicleTypes { get; }

    /// <summary>业务绝对时间轴与 solver 数值转换 / Business absolute timeline and solver numeric conversion.</summary>
    public TimeWindow<V> SchedulingWindow { get; }

    /// <summary>归一化单位 / Normalized units.</summary>
    public VrptwUnits Units { get; }

    /// <summary>算法容差 / Algorithm tolerances.</summary>
    public VrptwTolerances Tolerances { get; }

    /// <summary>按标识索引的客户 / Customers indexed by identity.</summary>
    public IReadOnlyDictionary<CustomerId, Customer<V>> CustomerById { get; }

    /// <summary>按节点索引的客户 / Customers indexed by node.</summary>
    public IReadOnlyDictionary<NetworkNodeId, Customer<V>> CustomerByNodeId { get; }

    /// <summary>按标识索引的车辆类型 / Vehicle types indexed by identity.</summary>
    public IReadOnlyDictionary<VehicleTypeId, VehicleType<V>> VehicleTypeById { get; }

    /// <summary>
    /// 由校验器创建已验证实例 / Create an already validated instance from the validator.
    /// </summary>
    /// <remarks>
    /// `internal` 是关键：这条旁路**只给 `VrptwValidator` 用**，外部无法绕过校验直接造实例。若把它公开，下游就可能持有
    /// 一个"看起来是实例、实际没校验过"的对象，而所有依赖实例不变式的代码都会在远处出错。
    /// `internal` is the point: this bypass is **for `VrptwValidator` only**, so external callers cannot mint an instance that
    /// skipped validation. Making it public would let downstream hold something that looks like an instance but never passed
    /// validation, and every piece of code trusting the invariants would fail far away from the cause.
    /// </remarks>
    /// <param name="name">实例名称 / Instance name.</param>
    /// <param name="startDepot">起始仓库 / Start depot.</param>
    /// <param name="endDepot">结束仓库 / End depot.</param>
    /// <param name="customers">客户 / Customers.</param>
    /// <param name="vehicleTypes">车辆类型 / Vehicle types.</param>
    /// <param name="schedulingWindow">业务时间轴 / Business timeline.</param>
    /// <param name="units">归一化单位 / Normalized units.</param>
    /// <param name="tolerances">算法容差 / Algorithm tolerances.</param>
    /// <returns>实例 / The instance.</returns>
    internal static VrptwInstance<V> Validated(
        string name,
        Depot<V> startDepot,
        Depot<V> endDepot,
        IReadOnlyList<Customer<V>> customers,
        IReadOnlyList<VehicleType<V>> vehicleTypes,
        TimeWindow<V> schedulingWindow,
        VrptwUnits units,
        VrptwTolerances tolerances) =>
        new(
            name, startDepot, endDepot,
            customers.ToList(), vehicleTypes.ToList(),
            schedulingWindow, units, tolerances);

    /// <summary>
    /// 创建并完整校验实例 / Create and fully validate an instance.
    /// </summary>
    /// <param name="name">实例名称 / Instance name.</param>
    /// <param name="startDepot">起始仓库 / Start depot.</param>
    /// <param name="endDepot">结束仓库 / End depot.</param>
    /// <param name="customers">客户 / Customers.</param>
    /// <param name="vehicleTypes">车辆类型 / Vehicle types.</param>
    /// <param name="schedulingWindow">业务时间轴 / Business timeline.</param>
    /// <param name="units">归一化单位 / Normalized units.</param>
    /// <param name="tolerances">算法容差 / Algorithm tolerances.</param>
    /// <returns>实例或校验失败 / The instance, or a validation failure.</returns>
    public static Result<VrptwInstance<V>, ErrorCode, Error<ErrorCode>> Invoke(
        string name,
        Depot<V> startDepot,
        Depot<V> endDepot,
        IReadOnlyList<Customer<V>> customers,
        IReadOnlyList<VehicleType<V>> vehicleTypes,
        TimeWindow<V> schedulingWindow,
        VrptwUnits units,
        VrptwTolerances tolerances) =>
        VrptwValidator.Create(
            name, startDepot, endDepot, customers, vehicleTypes, schedulingWindow, units, tolerances);
}
