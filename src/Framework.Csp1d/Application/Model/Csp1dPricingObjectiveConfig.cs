#nullable enable

using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Framework.Csp1d.Application.Model;

/// <summary>
/// 定价目标配置 / Pricing objective configuration.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
/// <remarks>
/// 它决定**定价时一条列的目标成本**，而减成本判据是 `对偶收益 > 目标成本`——
/// **成本算错，出列的判断就错**。
///
/// 四项可选惩罚：方案使用 `PlanUsagePenalty`、切边 `TrimWidthPenalty`、
/// 余料 `RestMaterialPenalty`、物料成本 `MaterialCostPenalty`（按物料编号的表）。
///
/// **四项惩罚全部可选**，默认 `null`/空表，此时成本就是基础值 `1`——与 的实现一致。
/// 所以移植它**不会改变默认行为**，只是把"可配置"这条能力补上。
///
/// Ported from its `Csp1dPricingObjectiveConfig`. It determines **the objective cost of a column during pricing**, and the reduced-cost test is
/// `dual benefit > objective cost` — **get the cost wrong and the emit decision is wrong**.
///
/// **All four penalties are optional**, defaulting to `null`/empty, in which case the cost is just the base `1` — matching the earlier implementation. So implementation it
/// **does not change default behaviour**; it only adds the "configurable" capability.
/// </remarks>
/// <param name="PlanUsagePenalty">方案使用惩罚 / Plan-usage penalty.</param>
/// <param name="TrimWidthPenalty">修边宽度惩罚 / Trim-width penalty.</param>
/// <param name="RestMaterialPenalty">余料惩罚 / Rest-material penalty.</param>
/// <param name="MaterialCostPenalty">按物料 id 的成本惩罚 / Per-material-id cost penalty.</param>
public sealed record Csp1dPricingObjectiveConfig<V>(
    V? PlanUsagePenalty = null,
    V? TrimWidthPenalty = null,
    V? RestMaterialPenalty = null,
    IReadOnlyDictionary<string, V>? MaterialCostPenalty = null
) where V : struct;
