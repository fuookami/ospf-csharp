#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.RecommendedWeightEqualization.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
// **`Stowage` 既是命名空间又是类型**（`Domain.Stowage` 与 `Model.Stowage`），必须起别名。
// **`Stowage` is both a namespace and a type** (`Domain.Stowage` versus `Model.Stowage`), so an alias is mandatory.
using StowageModel = Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model.Stowage;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.RecommendedWeightEqualization;

/// <summary>
/// 推荐重量均衡聚合 / The recommended-weight-equalisation aggregation.
/// </summary>
/// <remarks>
/// **六个上游部件 + 一个自建模型**（`PriorityAppointment`）。
///
/// 三处必须沿用：
///
/// 1. **`PriorityAppointment` 在这里派生，不是从配载域传进来**。它依赖 `Item.Cargo.Priority` 与 `LoadingOrder.PrecDepth`，
///    而后者是飞机域的后置赋值结果——在配载域算会读到还没串好的装载顺序。
/// 2. **预约映射取 `StowageAggregation.Appointment.Appointment`**（内层字典），不是 `Appointment` 对象本身。
/// 3. **`Ballast` 可为 `null`**：预配载模式下没有压舱物。
///
/// **six upstream parts plus one self-built model** (`PriorityAppointment`). Three things must be copied: **`PriorityAppointment` is derived here**,
/// not passed in from the stowage domain — it depends on `Item.Cargo.Priority` and `LoadingOrder.PrecDepth`, the latter assigned after construction in the aircraft domain, so
/// computing it in the stowage domain would read loading orders that are not linked yet; **the appointment map is `StowageAggregation.Appointment.Appointment`** (the inner
/// dictionary), not the `Appointment` object; and **`Ballast` may be `null`**, since predistribution carries no ballast.
/// </remarks>
public sealed class RecommendedWeightEqualizationAggregation
{
    /// <summary>
    /// 构造聚合 / Construct the aggregation.
    /// </summary>
    /// <param name="aircraftModel">飞机型号 / The aircraft model.</param>
    /// <param name="items">货物 / The items.</param>
    /// <param name="positions">舱位 / The positions.</param>
    /// <param name="appointment">货物到舱位的预约 / The item-to-position appointments.</param>
    /// <param name="stowage">配载矩阵 / The stowage matrix.</param>
    /// <param name="load">载荷 / The load.</param>
    public RecommendedWeightEqualizationAggregation(
        AircraftModel aircraftModel,
        IReadOnlyList<Item> items,
        IReadOnlyList<StowagePosition> positions,
        IReadOnlyDictionary<Item, StowagePosition> appointment,
        StowageModel stowage,
        Load load)
    {
        ArgumentNullException.ThrowIfNull(aircraftModel);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(appointment);
        ArgumentNullException.ThrowIfNull(stowage);
        ArgumentNullException.ThrowIfNull(load);

        AircraftModel = aircraftModel;
        Items = items;
        Positions = positions;
        Appointment = appointment;
        Stowage = stowage;
        Load = load;
        PriorityAppointment = Model.PriorityAppointment.From(items, positions, appointment);
    }

    /// <summary>飞机型号 / The aircraft model</summary>
    public AircraftModel AircraftModel { get; }

    /// <summary>货物 / The items</summary>
    public IReadOnlyList<Item> Items { get; }

    /// <summary>舱位 / The positions</summary>
    public IReadOnlyList<StowagePosition> Positions { get; }

    /// <summary>货物到舱位的预约 / The item-to-position appointments</summary>
    public IReadOnlyDictionary<Item, StowagePosition> Appointment { get; }

    /// <summary>配载矩阵 / The stowage matrix</summary>
    public StowageModel Stowage { get; }

    /// <summary>载荷 / The load</summary>
    public Load Load { get; }

    /// <summary>优先级预约 / The priority appointment</summary>
    public PriorityAppointment PriorityAppointment { get; }
}
