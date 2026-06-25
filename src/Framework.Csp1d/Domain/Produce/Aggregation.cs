#nullable enable

using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce.Model;
using Fuookami.Ospf.Math.Algebra.Concept;
using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.Produce;
/// <summary>
/// 产出输入 / Produce input.
///
/// 聚合 CSP1D 主问题建模所需的全部领域数据。
/// Aggregates all domain data required for CSP1D master problem modeling.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
/// <param name="CuttingPlans">切割方案列表 / Cutting plan list.</param>
/// <param name="Demands">需求列表 / Demand list.</param>
/// <param name="Materials">物料列表 / Material list.</param>
/// <param name="Machines">设备列表 / Machine list.</param>
/// <param name="WarmStartPlanUsages">热启动方案使用量 / Warm start plan usages.</param>
public sealed record ProduceInput<V>(
    IReadOnlyList<CuttingPlan<V>> CuttingPlans,
    IReadOnlyList<ProductDemand<V>> Demands,
    IReadOnlyList<Material<V>> Materials,
    IReadOnlyList<Machine<V>> Machines,
    IReadOnlyList<CuttingPlanUsage<V>> WarmStartPlanUsages
)
    where V : struct {
    /// <summary>
    /// 创建不含热启动的输入 / Create input without warm start.
    /// </summary>
    /// <param name="cuttingPlans">切割方案列表 / Cutting plan list.</param>
    /// <param name="demands">需求列表 / Demand list.</param>
    /// <param name="materials">物料列表 / Material list.</param>
    /// <param name="machines">设备列表 / Machine list.</param>
    public ProduceInput(
        IReadOnlyList<CuttingPlan<V>> cuttingPlans,
        IReadOnlyList<ProductDemand<V>> demands,
        IReadOnlyList<Material<V>> materials,
        IReadOnlyList<Machine<V>> machines)
        : this(cuttingPlans, demands, materials, machines, System.Array.Empty<CuttingPlanUsage<V>>()) {
    }
}
