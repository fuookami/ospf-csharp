// 活动物料库存模型 / Activity material inventory model.
// 与固定 Kotlin 对应。
// The pinned Kotlin: .

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Quantity;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.Activity.Model;

/// <summary>
/// 具名物料库存 / Named material inventory.
/// </summary>
/// <param name="Id">稳定物料身份 / The stable material identity.</param>
/// <param name="Initial">期初库存与统一计量单位 / The opening stock and its canonical unit.</param>
/// <param name="Minimum">全时域最低库存，空表示零 / The minimum stock throughout the horizon; null means zero.</param>
/// <param name="Maximum">可选库存上限 / The optional storage limit.</param>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <remarks>
/// **不同物料不混加**：这条看起来是废话，但把库存塞进一个"按单位分组"的字典里就会混——
/// `千克` 的原料和 `千克` 的成品共用一个键，于是生产 1 千克成品会"补充"掉 1 千克原料的缺口。
/// 所以身份是 `Id`，单位只用于量纲校验。
///
/// **Different materials never add together**: that sounds obvious, yet bucketing stock by unit does exactly that — a kilogram of input and a kilogram of output share a
/// key, so producing one kilogram of output "replenishes" a one-kilogram input shortfall. The identity is therefore `Id`; the unit is only for dimension checking.
///
/// **`Minimum` 为空表示零，而不是"无下限"**：库存不允许为负是默认的物理事实，把它当成"无限制"会让缺料永远不报。
///
/// **A null `Minimum` means zero, not "no lower bound"**: non-negative stock is the default physical fact, and treating it as unlimited would mean a material shortage is
/// never reported.
/// </remarks>
public sealed record ActivityMaterial<V>(
    string Id,
    Quantity<V> Initial,
    Quantity<V>? Minimum = null,
    Quantity<V>? Maximum = null)
    where V : struct, IFloatingNumber<V> {
    /// <summary>解析后的最低库存，未给出时为零 / The resolved minimum stock, zero when omitted.</summary>
    /// <returns>最低库存 / The minimum stock.</returns>
    public Quantity<V> ResolvedMinimum => Minimum ?? new Quantity<V>(default, Initial.Unit);
}

/// <summary>
/// 模式内部物料事件 / An intra-mode material event.
/// </summary>
/// <param name="Material">物料身份 / The material identity.</param>
/// <param name="Quantity">有符号库存变化量 / The signed inventory change.</param>
/// <param name="At">相对活动起点的事件时间 / The offset from the activity start.</param>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <remarks>
/// **同一时点的事件原子结算**：`消耗 −5` 与 `产出 +5` 落在同一偏移量时，中间态库存是 `−5` 还是 `0` 取决于结算顺序，
/// 而按顺序判下限会得出"缺料"，按净额判则不会。所以同偏移量的事件必须先合并再判。
///
/// **Simultaneous events settle atomically**: a `-5` consumption and a `+5` supply at the same offset leave an intermediate stock of `-5` or `0` depending on the order, so a
/// per-event lower-bound check reports a shortage while a net check does not. Events sharing an offset must therefore be merged before the bound is checked.
/// </remarks>
public sealed record ActivityMaterialFlow<V>(
    string Material,
    Quantity<V> Quantity,
    TimeSpan? At = null)
    where V : struct, IFloatingNumber<V> {
    /// <summary>解析后的事件偏移，未给出时为零 / The resolved offset, zero when omitted.</summary>
    public TimeSpan Offset => At ?? TimeSpan.Zero;
}

/// <summary>
/// 与活动选择无关的未来到货或需求 / A fixed future receipt or demand, independent of the choices.
/// </summary>
/// <param name="Material">物料身份 / The material identity.</param>
/// <param name="Time">事件时间 / The event time.</param>
/// <param name="Quantity">有符号库存变化量 / The signed inventory change.</param>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <remarks>
/// **不自动从执行历史推导**：已执行的事实是事实，未来的到货是承诺。把两者混在一起，
/// "计划里多了一批料"与"仓库里已经多了一批料"就无法区分，而后者会让排程低估缺料风险。
///
/// **Never inferred from execution history**: what already ran is fact, a future receipt is a promise. Mixing them makes "the plan assumes a delivery" indistinguishable from
/// "the warehouse already received it", and the latter makes the schedule understate the shortage risk.
/// </remarks>
public sealed record ActivityMaterialEvent<V>(
    string Material,
    DateTimeOffset Time,
    Quantity<V> Quantity)
    where V : struct, IFloatingNumber<V>;

/// <summary>
/// 同时点所有事件结算后的库存 / The stock after all events at one instant have settled.
/// </summary>
/// <param name="Time">事件时间 / The event time.</param>
/// <param name="Quantity">该时点之后的库存 / The stock after the event.</param>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed record ActivityInventoryLevel<V>(DateTimeOffset Time, Quantity<V> Quantity)
    where V : struct, IFloatingNumber<V>;

/// <summary>
/// 库存复核轨迹，含原始期初量与事件后库存 / An inventory audit trail retaining the opening stock and the post-event stock.
/// </summary>
/// <param name="Material">物料身份 / The material identity.</param>
/// <param name="Initial">事件发生前的期初库存 / The opening stock before any event.</param>
/// <param name="Levels">事件后库存 / The post-event levels.</param>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <remarks>
/// **期初量与第一条事件后库存都要留**：只有 `Levels` 时，"期初 100、消耗 100、剩 0"与"期初 0、到货 100、消耗 100、剩 0"
/// 看起来一样，而前者说明这批料是本区间内到位的、后者说明一开始就有——两者的风险完全不同。
///
/// **Both the opening stock and the first post-event level are kept**: with only `Levels`, "opening 100, consumed 100, zero left" looks the same as "opening 0, receipt 100,
/// consumed 100, zero left", yet the first means the material arrived within the horizon and the second means it was there from the start — very different risks.
/// </remarks>
public sealed record ActivityMaterialInventory<V>(
    string Material,
    Quantity<V> Initial,
    IReadOnlyList<ActivityInventoryLevel<V>> Levels)
    where V : struct, IFloatingNumber<V>;
