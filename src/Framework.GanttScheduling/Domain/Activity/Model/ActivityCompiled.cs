#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Quantities.Quantity;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.Activity.Model;

/// <summary>
/// 编译后的选择变量与成本多项式 / The compiled choice variables and cost polynomial.
/// </summary>
/// <remarks>
/// 与基线 `ActivityCompilation` 对齐。**变量索引就是选择顺序**：第 `i` 个选择对应第 `i` 个变量，
/// 取解时按同一个索引回读——两处若各建一套映射，就会出现"求解成功但排程错位"。
/// 成本多项式与变量**同源构造**：第 `i` 个变量的系数就是第 `i` 个选择的成本。
///
/// Aligned with the baseline's `ActivityCompilation`. **The variable index is the choice order**: the i-th choice maps to the i-th variable and extraction reads back by the same index, whereas two
/// separate mappings would produce "solved successfully, scheduled wrongly". The cost polynomial is built from the **same source**: the i-th variable's coefficient is the i-th choice's cost.
/// </remarks>
public sealed class ActivityCompilation
{
    /// <summary>
    /// 构造 / Construct.
    /// </summary>
    /// <param name="size">选择数 / The number of choices.</param>
    /// <param name="costs">每个选择的成本 / The cost of each choice.</param>
    /// <param name="name">命名空间 / The namespace.</param>
    public ActivityCompilation(int size, IReadOnlyList<Flt64> costs, string name) {
        ArgumentNullException.ThrowIfNull(costs);
        if (size < 0) {
            throw new ArgumentOutOfRangeException(nameof(size), "size must be non-negative");
        }
        if (costs.Count != size) {
            throw new ArgumentException("costs must have one entry per choice", nameof(costs));
        }

        var variables = new List<BinVar>(size);
        var monomials = new List<LinearMonomial<Flt64>>(size);
        for (int index = 0; index < size; index++) {
            var variable = new BinVar($"{name}_choice_{index}");
            variables.Add(variable);
            monomials.Add(new LinearMonomial<Flt64>(costs[index], variable));
        }
        Variables = variables;
        Cost = new LinearPolynomial<Flt64>(monomials, new Flt64(0.0));
    }

    /// <summary>选择变量 / The choice variables</summary>
    public IReadOnlyList<BinVar> Variables { get; }

    /// <summary>成本多项式 / The cost polynomial</summary>
    public LinearPolynomial<Flt64> Cost { get; }
}

/// <summary>
/// 编译后的资源占用 / A compiled resource reservation.
/// </summary>
/// <param name="Resource">执行者 / The executor.</param>
/// <param name="Time">时段 / The time range.</param>
/// <param name="Quantity">占用强度 / The amount.</param>
public sealed record CompiledActivityReservation(string Resource, TimeRange Time, Flt64 Quantity);

/// <summary>
/// 编译后的物料事件 / A compiled material event.
/// </summary>
/// <param name="Material">物料 / The material.</param>
/// <param name="Time">时刻 / The instant.</param>
/// <param name="Quantity">数量 / The amount.</param>
public sealed record CompiledActivityMaterialEvent(string Material, DateTimeOffset Time, Flt64 Quantity);

/// <summary>
/// 编译后的物料 / A compiled material.
/// </summary>
/// <param name="Source">原始物料 / The source material.</param>
/// <param name="Initial">初始库存 / The initial inventory.</param>
/// <param name="Minimum">下限 / The lower bound.</param>
/// <param name="Maximum">上限 / The upper bound.</param>
public sealed record CompiledActivityMaterial<V>(
    ActivityMaterial<V> Source,
    Flt64 Initial,
    Flt64 Minimum,
    Flt64? Maximum)
    where V : struct, IFloatingNumber<V>;

/// <summary>
/// 编译后的选择 / A compiled choice.
/// </summary>
/// <param name="Task">活动 / The activity.</param>
/// <param name="Placement">已有排程 / The existing placement.</param>
/// <param name="Reservations">资源占用 / The reservations.</param>
/// <param name="Cost">成本 / The cost.</param>
/// <param name="MaterialEvents">物料事件 / The material events.</param>
public sealed record CompiledActivityChoice<V>(
    string Task,
    ActivityPlacement<V>? Placement,
    IReadOnlyList<CompiledActivityReservation> Reservations,
    Flt64 Cost,
    IReadOnlyList<CompiledActivityMaterialEvent>? MaterialEvents = null)
    where V : struct, IFloatingNumber<V> {
    /// <summary>物料事件（未给则空）/ The material events, empty when unset</summary>
    public IReadOnlyList<CompiledActivityMaterialEvent> MaterialEventsValue =>
        MaterialEvents ?? Array.Empty<CompiledActivityMaterialEvent>();
}

/// <summary>
/// 编译后的执行者产能 / A compiled executor capacity.
/// </summary>
/// <param name="Source">原始执行者 / The source executor.</param>
/// <param name="Capacity">总产能 / The overall capacity.</param>
/// <param name="Capacities">分时段产能 / The per-period capacities.</param>
/// <remarks>
/// 与基线 `CompiledActivityResource` 对齐，重点是 <see cref="CapacityAt"/> 的**优先级**：
/// 不可用时段 → **0**（禁止排入），其次分时段产能，最后才回落到总产能。
/// 三条的顺序不能换：把总产能放在前面，就等于允许在被禁止的时段里排程。
///
/// Aligned with the baseline's `CompiledActivityResource`, the point being **the precedence** in <see cref="CapacityAt"/>: an unavailable range yields **zero** (a prohibition), then the per-period
/// capacity applies, and only then the overall one. The order is not interchangeable: putting the overall capacity first would permit scheduling inside a forbidden range.
/// </remarks>
public sealed record CompiledActivityResource<V>(
    ActivityResource<V> Source,
    Flt64 Capacity,
    IReadOnlyList<KeyValuePair<TimeRange, Flt64>> Capacities)
    where V : struct, IFloatingNumber<V> {
    /// <summary>
    /// 某时刻的可用产能 / The capacity available at an instant.
    /// </summary>
    /// <param name="time">时刻 / The instant.</param>
    /// <returns>产能 / The capacity.</returns>
    public Flt64 CapacityAt(DateTimeOffset time) {
        foreach (TimeRange unavailable in Source.UnavailableTimesValue) {
            if (time >= unavailable.Start && time < unavailable.End) {
                return new Flt64(0.0);
            }
        }
        foreach (KeyValuePair<TimeRange, Flt64> capacity in Capacities) {
            if (time >= capacity.Key.Start && time < capacity.Key.End) {
                return capacity.Value;
            }
        }
        return Capacity;
    }
}
