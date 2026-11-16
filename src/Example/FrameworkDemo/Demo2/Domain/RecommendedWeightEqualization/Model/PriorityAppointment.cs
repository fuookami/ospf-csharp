#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.RecommendedWeightEqualization.Model;

/// <summary>
/// 优先级预约 / The priority appointment.
/// </summary>
/// <remarks>
/// 把**每个货物优先级**映射到一组"可放置的装载顺序深度"。
///
/// 三处必须沿用：
///
/// 1. **回退到全部可用深度**。某个优先级没有任何已预约位置时，它拿到的是 `availableDepths`（全部可用舱位的深度），
///    不是空集。写成空集会让 `PriorityAppointmentLimit` 把该优先级的货物全部钉死在 0 处。
/// 2. **只统计"可用"舱位**。`Status.Available` 为假的舱位既不计入 `availableDepths`，也不计入已预约深度。
/// 3. **深度用 `LoadingOrder.PrecDepth`**，而那个值**恒为 0**（见 `LoadingOrder.PrecDepth` 的说明）。于是整个映射
///    实际上只有 `{0}` 这一个深度，`Invoke(priority, position)` 对所有可用舱位都为真。**这是行为**。
///
/// Each cargo priority maps to a set of loading-order depths it may occupy. Three things must be copied: **the fallback is all available depths** — a
/// priority with no appointed position gets `availableDepths`, not an empty set, which would pin every item of that priority at depth 0 via `PriorityAppointmentLimit`; **only
/// available positions count**, neither into `availableDepths` nor into the appointed depths; and **the depth is `LoadingOrder.PrecDepth`, which is always 0** (see that property),
/// so the whole mapping really holds only depth `{0}` and `Invoke(priority, position)` is true for every available position. That is its behaviour and is copied.
/// </remarks>
public sealed class PriorityAppointment
{
    private readonly IReadOnlyDictionary<CargoPriority, IReadOnlySet<byte>> _appointment;

    /// <summary>
    /// 构造优先级预约 / Construct the priority appointment.
    /// </summary>
    /// <param name="appointment">优先级到深度集合的映射 / The priority-to-depths mapping.</param>
    public PriorityAppointment(IReadOnlyDictionary<CargoPriority, IReadOnlySet<byte>> appointment)
    {
        ArgumentNullException.ThrowIfNull(appointment);
        _appointment = appointment;
    }

    /// <summary>优先级到深度集合的映射 / The priority-to-depths mapping</summary>
    public IReadOnlyDictionary<CargoPriority, IReadOnlySet<byte>> Appointment => _appointment;

    /// <summary>
    /// 从配载数据派生 / Derive it from the stowage data.
    /// </summary>
    /// <param name="items">货物 / The items.</param>
    /// <param name="positions">舱位 / The positions.</param>
    /// <param name="appointment">货物到舱位的预约 / The item-to-position appointments.</param>
    /// <returns>优先级预约 / The priority appointment.</returns>
    /// <remarks>
    /// `payload`、`totalWeight`、`ballast` 三个参数**在函数体里一次都没用到**——它们只是签名的一部分。这里不收，
    /// 因为收了却不读会让调用方以为有影响。
    ///
    /// The code's `payload`, `totalWeight` and `ballast` parameters are **never read in the body** — they are signature decoration. They are omitted here, since accepting and
    /// ignoring them would suggest they matter.
    /// </remarks>
    public static PriorityAppointment From(
        IReadOnlyList<Item> items,
        IReadOnlyList<StowagePosition> positions,
        IReadOnlyDictionary<Item, StowagePosition> appointment)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(appointment);

        var availableDepths = new HashSet<byte>();
        foreach (StowagePosition position in positions)
        {
            if (position.Status.Available)
            {
                availableDepths.Add(position.AircraftPosition.LoadingOrder?.PrecDepth ?? 0);
            }
        }

        var appointedDepths = new Dictionary<CargoPriority, HashSet<byte>>();
        foreach (KeyValuePair<Item, StowagePosition> entry in appointment)
        {
            if (!Contains(items, entry.Key) || !Contains(positions, entry.Value) || !entry.Value.Status.Available)
            {
                continue;
            }
            if (!appointedDepths.TryGetValue(entry.Key.Priority, out HashSet<byte>? depths))
            {
                depths = new HashSet<byte>();
                appointedDepths[entry.Key.Priority] = depths;
            }
            depths.Add(entry.Value.AircraftPosition.LoadingOrder?.PrecDepth ?? 0);
        }

        var byPriority = new Dictionary<CargoPriority, IReadOnlySet<byte>>();
        foreach (Item item in items)
        {
            CargoPriority priority = item.Priority;
            if (byPriority.ContainsKey(priority))
            {
                continue;
            }
            // **回退到全部可用深度**，不是空集。
            // **Fall back to all available depths**, not an empty set.
            byPriority[priority] = appointedDepths.TryGetValue(priority, out HashSet<byte>? found) && found.Count > 0
                ? new HashSet<byte>(found)
                : new HashSet<byte>(availableDepths);
        }
        return new PriorityAppointment(byPriority);
    }

    /// <summary>
    /// 该优先级能否放在该舱位 / Whether the priority may sit at the position.
    /// </summary>
    /// <param name="priority">优先级 / The priority.</param>
    /// <param name="position">舱位 / The position.</param>
    /// <returns>能否放置 / Whether it may be placed.</returns>
    public bool Invoke(CargoPriority priority, StowagePosition position)
    {
        ArgumentNullException.ThrowIfNull(position);
        return _appointment.TryGetValue(priority, out IReadOnlySet<byte>? depths)
            && depths.Contains(position.AircraftPosition.LoadingOrder?.PrecDepth ?? 0);
    }

    /// <summary>
    /// 按引用包含 / Reference containment.
    /// </summary>
    /// <typeparam name="T">元素类型 / The element type.</typeparam>
    /// <param name="source">集合 / The collection.</param>
    /// <param name="candidate">待查元素 / The candidate.</param>
    /// <returns>是否包含 / Whether contained.</returns>
    /// <remarks>
    /// 用 `ReferenceEquals` 而不是 `Contains`：`Item` 与 `StowagePosition` 的相等语义由各自的实现决定，而这里要判的是
    /// "这个对象在不在那个列表里"（`item in items` 走的是 `equals`，但两个列表来自同一个聚合，成员是同一批对象）。
    /// 按引用判更严格，且不会因为两个 `Id` 相同的不同对象而误判——那是本仓 `Position` 按 `Id` 判等埋下的坑。
    ///
    /// `ReferenceEquals` rather than `Contains`: the equality semantics of `Item` and `StowagePosition` are their own business, while this asks "is this object in that
    /// list" (`in` uses `equals`, but both lists come from one aggregation and hold the same instances). Reference identity is stricter and avoids the trap this implementation's
    /// `Position` sets by comparing on `Id`, where two distinct objects with one `Id` would pass.
    /// </remarks>
    private static bool Contains<T>(IReadOnlyList<T> source, T candidate)
        where T : class
    {
        foreach (T element in source)
        {
            if (ReferenceEquals(element, candidate))
            {
                return true;
            }
        }
        return false;
    }
}
