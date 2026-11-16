// 物料库存审计 / Material inventory audit.
// 与固定 Kotlin 
// 的 `levels` / `violations` 对应；`extract` 依赖 `ActivityValueBoundary`，见文末说明。
// .

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Activity.Model;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.Activity.Service;

/// <summary>
/// 物料库存审计 / The material inventory audit.
/// </summary>
/// <remarks>
/// **先按时间分组、再逐时点结算**，不是逐事件累加。这不是性能优化：同一时刻的一笔消耗与一笔到货如果分开判，
/// 中间态库存是 `−5`（消耗先）或 `+5`（到货先），取决于事件的排列顺序；而按净额判始终是 `0`。
/// 也就是说**逐事件判会报出一个并不存在的缺料**，而它是否报出取决于输入顺序。
///
/// **Group by time first, then settle instant by instant** — not event by event. That is not a performance choice: judged separately, a consumption and a receipt at the same
/// instant leave an intermediate stock of `−5` or `+5` depending on their order, while the net is always `0`. In other words **per-event checking reports a shortage that does
/// not exist**, and whether it reports it depends on the input order.
/// </remarks>
public static class ActivityMaterialAudit {
    /// <summary>
    /// 计算每种物料的库存轨迹 / Compute the inventory trajectory of every material.
    /// </summary>
    /// <param name="materials">物料清单 / The materials.</param>
    /// <param name="events">固定事件 / The fixed events.</param>
    /// <param name="horizonStart">优化区间起点 / The horizon start.</param>
    /// <param name="horizonEnd">优化区间终点 / The horizon end.</param>
    /// <returns>物料身份到库存轨迹 / Material identity to inventory trajectory.</returns>
    /// <remarks>
    /// **区间的两端都必须出现在轨迹里**，即使那两点没有任何事件：最低库存通常就落在端点之间什么都没发生的长时间段里，
    /// 只报"有事件的时刻"会让"整整三天库存为零"完全不可见。
    ///
    /// **Both horizon endpoints appear in the trajectory** even when nothing happens there: the minimum stock usually falls in a long quiet stretch between endpoints, and
    /// reporting only the instants that carry events makes "stock sat at zero for three days" entirely invisible.
    /// </remarks>
    public static Dictionary<string, IReadOnlyList<ActivityInventoryLevel<Flt64>>> Levels(
        IReadOnlyList<ActivityMaterial<Flt64>> materials,
        IReadOnlyList<ActivityMaterialEvent<Flt64>> events,
        DateTimeOffset horizonStart,
        DateTimeOffset horizonEnd) {
        System.ArgumentNullException.ThrowIfNull(materials);
        System.ArgumentNullException.ThrowIfNull(events);

        var byMaterial = new Dictionary<string, List<ActivityMaterialEvent<Flt64>>>(StringComparer.Ordinal);
        foreach (ActivityMaterialEvent<Flt64> materialEvent in events) {
            if (!byMaterial.TryGetValue(materialEvent.Material, out List<ActivityMaterialEvent<Flt64>>? list)) {
                list = new List<ActivityMaterialEvent<Flt64>>();
                byMaterial[materialEvent.Material] = list;
            }
            list.Add(materialEvent);
        }

        var result = new Dictionary<string, IReadOnlyList<ActivityInventoryLevel<Flt64>>>(StringComparer.Ordinal);
        foreach (ActivityMaterial<Flt64> material in materials) {
            List<ActivityMaterialEvent<Flt64>> owned = byMaterial.TryGetValue(material.Id, out List<ActivityMaterialEvent<Flt64>>? found)
                ? found
                : new List<ActivityMaterialEvent<Flt64>>();

            var times = new SortedSet<DateTimeOffset> { horizonStart, horizonEnd };
            foreach (ActivityMaterialEvent<Flt64> materialEvent in owned) {
                times.Add(materialEvent.Time);
            }

            var levels = new List<ActivityInventoryLevel<Flt64>>(times.Count);
            double stock = material.Initial.Value.ToDouble();
            foreach (DateTimeOffset time in times) {
                // 同一时刻的所有事件先合并成净额，再一次性加到库存上。
                // All events sharing an instant are merged into a net amount before being applied once.
                double net = 0.0;
                foreach (ActivityMaterialEvent<Flt64> materialEvent in owned) {
                    if (materialEvent.Time == time) {
                        net += materialEvent.Quantity.Value.ToDouble();
                    }
                }
                stock += net;
                levels.Add(new ActivityInventoryLevel<Flt64>(time, new Quantity<Flt64>(
                    new Flt64(stock), material.Initial.Unit)));
            }
            result[material.Id] = levels;
        }
        return result;
    }

    /// <summary>
    /// 找出库存违规 / Find the inventory violations.
    /// </summary>
    /// <param name="materials">物料清单 / The materials.</param>
    /// <param name="levels">库存轨迹 / The inventory trajectories.</param>
    /// <returns>违规项 / The violations.</returns>
    /// <remarks>
    /// **期初量本身也要判**：一个期初就低于下限的库存，其第一条"事件后库存"可能已经因为到货而回到正常区间，
    /// 于是只看事件后库存会漏掉"一开始就不够"这件事。
    ///
    /// **The opening stock is checked too**: a stock that starts below its minimum may have its first post-event level back in range thanks to a receipt, so checking only
    /// post-event levels misses "it was never enough to begin with".
    ///
    /// **非有限值单独算违规**：`NaN` 与任何上下界比较都为假，于是它会**静默通过所有检查**。必须显式判。
    ///
    /// **A non-finite value is its own violation**: `NaN` compares false against every bound, so it **silently passes every check**. It has to be tested explicitly.
    /// </remarks>
    public static IReadOnlyList<string> Violations(
        IReadOnlyList<ActivityMaterial<Flt64>> materials,
        IReadOnlyDictionary<string, IReadOnlyList<ActivityInventoryLevel<Flt64>>> levels) {
        System.ArgumentNullException.ThrowIfNull(materials);
        System.ArgumentNullException.ThrowIfNull(levels);

        var violations = new List<string>();
        foreach (ActivityMaterial<Flt64> material in materials) {
            double minimum = material.ResolvedMinimum.Value.ToDouble();
            double? maximum = material.Maximum?.Value.ToDouble();

            if (IsInvalid(material.Initial.Value.ToDouble(), minimum, maximum)) {
                violations.Add("stock_opening:" + material.Id);
            }

            if (!levels.TryGetValue(material.Id, out IReadOnlyList<ActivityInventoryLevel<Flt64>>? trajectory)) {
                continue;
            }
            foreach (ActivityInventoryLevel<Flt64> level in trajectory) {
                if (IsInvalid(level.Quantity.Value.ToDouble(), minimum, maximum)) {
                    violations.Add("stock:" + material.Id + ":" + level.Time.ToString("O"));
                }
            }
        }
        return violations;
    }

    /// <summary>判断一个库存值是否违规 / Whether a stock value is invalid.</summary>
    /// <param name="stock">库存 / The stock.</param>
    /// <param name="minimum">下限 / The lower bound.</param>
    /// <param name="maximum">上限 / The upper bound.</param>
    /// <returns>是否违规 / Whether it is invalid.</returns>
    private static bool IsInvalid(double stock, double minimum, double? maximum) =>
        !double.IsFinite(stock) || stock < minimum || (maximum is { } upper && stock > upper);
}
