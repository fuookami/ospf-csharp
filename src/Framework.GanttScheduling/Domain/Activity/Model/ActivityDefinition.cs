#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Quantity;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.Activity.Model;

/// <summary>
/// 一段可用产能 / A capacity available over a time range.
/// </summary>
/// <param name="Time">时段 / The time range.</param>
/// <param name="Capacity">产能值 / The capacity value.</param>
public sealed record ActivityCapacity<V>(TimeRange Time, Quantity<V> Capacity)
    where V : struct, IFloatingNumber<V>;

/// <summary>
/// 执行者（资源）及其产能 / An executor with its capacity.
/// </summary>
/// <param name="Id">执行者标识 / The executor id.</param>
/// <param name="Capacity">总产能 / The overall capacity.</param>
/// <param name="UnavailableTimes">不可用时段 / The unavailable time ranges.</param>
/// <param name="Capacities">分时段产能 / The per-period capacities.</param>
/// <remarks>
/// 与基线 `ActivityResource` 对齐。**三层产能同时存在**：总量、分时段、以及不可用时段——
/// 不可用不是"产能为零的时段"，它是**禁止排入**；两者混成一体会让"排进去但产能为 0"看起来合法。
///
/// Aligned with the baseline's `ActivityResource`. **Three layers of capacity coexist**: the total, the per-period values and the unavailable ranges. Unavailability is not "a period with zero capacity"
/// but a **prohibition**, and merging the two would make "scheduled with zero capacity" look legitimate.
/// </remarks>
public sealed record ActivityResource<V>(
    string Id,
    Quantity<V> Capacity,
    IReadOnlyList<TimeRange>? UnavailableTimes = null,
    IReadOnlyList<ActivityCapacity<V>>? Capacities = null)
    where V : struct, IFloatingNumber<V> {
    /// <summary>不可用时段（未给则空）/ The unavailable ranges, empty when unset</summary>
    public IReadOnlyList<TimeRange> UnavailableTimesValue =>
        UnavailableTimes ?? Array.Empty<TimeRange>();

    /// <summary>分时段产能（未给则空）/ The per-period capacities, empty when unset</summary>
    public IReadOnlyList<ActivityCapacity<V>> CapacitiesValue =>
        Capacities ?? Array.Empty<ActivityCapacity<V>>();
}

/// <summary>
/// 一次资源占用 / One resource use.
/// </summary>
/// <param name="Resource">执行者 / The executor.</param>
/// <param name="Quantity">占用强度 / The amount used.</param>
/// <param name="From">从活动开始算起的偏移 / The offset from the activity's start.</param>
/// <param name="Until">到活动开始算起的偏移，null 表示持续到结束 / The offset to the end, null meaning through to the finish.</param>
/// <remarks>
/// 与基线 `ActivityResourceUse` 对齐。**`Until` 为 null 不是"零长度"**，而是"持续到活动结束"——
/// 活动时长在不同模式下不同，占用若写死一个长度就会在其它模式下错位。
///
/// Aligned with the baseline's `ActivityResourceUse`. **A null `Until` means "through to the finish", not zero length**: an activity's duration differs between modes, so a fixed length would be
/// misaligned in the others.
/// </remarks>
public sealed record ActivityResourceUse<V>(
    string Resource,
    Quantity<V> Quantity,
    TimeSpan? From = null,
    TimeSpan? Until = null)
    where V : struct, IFloatingNumber<V> {
    /// <summary>起始偏移（未给则为零）/ The start offset, zero when unset</summary>
    public TimeSpan FromValue => From ?? TimeSpan.Zero;
}

/// <summary>
/// 活动的一种执行模式 / One execution mode of an activity.
/// </summary>
/// <param name="Id">模式标识 / The mode id.</param>
/// <param name="Duration">时长 / The duration.</param>
/// <param name="Starts">允许的开始时刻 / The permitted start instants.</param>
/// <param name="Resources">资源占用 / The resource uses.</param>
/// <param name="Cost">成本 / The cost.</param>
/// <param name="MaterialFlows">物料流 / The material flows.</param>
/// <remarks>
/// 与基线 `ActivityMode` 对齐。**模式自带时长与成本**：同一活动换模式等于换了一整套参数，
/// 所以它们挂在模式上而不是活动上。
///
/// Aligned with the baseline's `ActivityMode`. **A mode carries its own duration and cost**: switching an activity's mode swaps the whole parameter set, so they hang off the mode rather than the
/// activity.
/// </remarks>
public sealed record ActivityMode<V>(
    string Id,
    TimeSpan Duration,
    IReadOnlyList<DateTimeOffset> Starts,
    IReadOnlyList<ActivityResourceUse<V>> Resources,
    Quantity<V> Cost,
    IReadOnlyList<ActivityMaterialFlow<V>>? MaterialFlows = null)
    where V : struct, IFloatingNumber<V> {
    /// <summary>物料流（未给则空）/ The material flows, empty when unset</summary>
    public IReadOnlyList<ActivityMaterialFlow<V>> MaterialFlowsValue =>
        MaterialFlows ?? Array.Empty<ActivityMaterialFlow<V>>();
}

/// <summary>
/// 活动：一组可选模式 / An activity: a set of alternative modes.
/// </summary>
/// <param name="Id">活动标识 / The activity id.</param>
/// <param name="Modes">可选模式 / The alternative modes.</param>
/// <param name="CancellationCost">取消成本 / The cancellation cost.</param>
/// <remarks>
/// 与基线 `Activity` 对齐。**取消成本可空**：null 表示该活动不允许取消——
/// 用 0 表示"免费取消"与"不能取消"是两件事，不能合并。
///
/// Aligned with the baseline's `Activity`. **The cancellation cost is nullable**: null means the activity may not be cancelled, since "free to cancel" and "cannot be cancelled" are different
/// things and must not be merged.
/// </remarks>
public sealed record Activity<V>(
    string Id,
    IReadOnlyList<ActivityMode<V>> Modes,
    Quantity<V>? CancellationCost = null)
    where V : struct, IFloatingNumber<V>;
