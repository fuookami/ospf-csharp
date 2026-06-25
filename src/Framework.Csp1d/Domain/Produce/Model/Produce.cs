#nullable enable

using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using System.Collections.Generic;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.Produce.Model;
/// <summary>
/// 切割方案使用量 / Cutting plan usage.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
/// <param name="Plan">切割方案 / Cutting plan.</param>
/// <param name="Amount">使用车次 / Usage amount.</param>
public sealed record CuttingPlanUsage<V>(
    CuttingPlan<V> Plan,
    UInt64 Amount
)
    where V : struct;

/// <summary>
/// 物料使用量 / Material usage.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
/// <param name="Material">物料 / Material.</param>
/// <param name="Amount">使用车次 / Used batches.</param>
public sealed record MaterialUsage<V>(
    Material<V> Material,
    UInt64 Amount
)
    where V : struct;

/// <summary>
/// 设备产能使用 / Machine capacity usage.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
/// <param name="Machine">设备 / Machine.</param>
/// <param name="Used">按方案产能消耗聚合的实际使用产能 / Actual used capacity aggregated from plan consumption.</param>
public sealed record MachineCapacityUsage<V>(
    Machine<V> Machine,
    Quantity<V>? Used
)
    where V : struct;

/// <summary>
/// 主问题求解产出 / Master problem output.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
/// <param name="CuttingPlans">选中切割方案 / Selected cutting plans.</param>
/// <param name="MaterialUsages">物料使用统计 / Material usage statistics.</param>
/// <param name="MachineUsages">设备产能统计 / Machine capacity statistics.</param>
/// <param name="UnmetDemands">未满足需求 / Unmet demands.</param>
public sealed record Produce<V>(
    IReadOnlyList<CuttingPlanUsage<V>> CuttingPlans,
    IReadOnlyList<MaterialUsage<V>> MaterialUsages,
    IReadOnlyList<MachineCapacityUsage<V>> MachineUsages,
    IReadOnlyList<ProductDemand<V>> UnmetDemands
)
    where V : struct {
    /// <summary>
    /// 创建无未满足需求的产出 / Create produce without unmet demands.
    /// </summary>
    public Produce(
        IReadOnlyList<CuttingPlanUsage<V>> cuttingPlans,
        IReadOnlyList<MaterialUsage<V>> materialUsages,
        IReadOnlyList<MachineCapacityUsage<V>> machineUsages)
        : this(cuttingPlans, materialUsages, machineUsages, System.Array.Empty<ProductDemand<V>>()) {
    }
}

/// <summary>
/// 需求贡献聚合键（产品ID + 单位），确保同产品不同单位贡献不混算 /
/// Contribution aggregation key (product ID + unit) ensuring same-product different-unit contributions are not mixed.
/// </summary>
/// <param name="ProductId">产品标识 / Product identifier.</param>
/// <param name="Unit">物理单位 / Physical unit.</param>
public sealed record ContributionKey(
    string ProductId,
    PhysicalUnit Unit
);

/// <summary>
/// 产出扩展方法 / Produce extension methods.
/// </summary>
public static class ProduceExtensions {
    /// <summary>
    /// 汇总切割方案贡献 / Aggregate cutting plan contributions.
    ///
    /// 按产品+单位聚合的需求贡献。
    /// Demand contribution grouped by product+unit.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
    /// <param name="produce">主问题产出 / Master problem output.</param>
    /// <returns>按产品+单位聚合的需求贡献 / Demand contribution grouped by product+unit.</returns>
    public static Dictionary<ContributionKey, List<CuttingPlanDemandContribution<V>>> Contributions<V>(
        this Produce<V> produce)
        where V : struct {
        var contributions = new Dictionary<ContributionKey, List<CuttingPlanDemandContribution<V>>>();
        foreach (CuttingPlanUsage<V> usage in produce.CuttingPlans) {
            foreach (CuttingPlanDemandContribution<V> contribution in usage.Plan.DemandContributions) {
                var key = new ContributionKey(contribution.Product.Id, contribution.Quantity.Unit);
                if (!contributions.TryGetValue(key, out List<CuttingPlanDemandContribution<V>>? list)) {
                    list = new List<CuttingPlanDemandContribution<V>>();
                    contributions[key] = list;
                }
                list.Add(contribution);
            }
        }
        return contributions;
    }
}
