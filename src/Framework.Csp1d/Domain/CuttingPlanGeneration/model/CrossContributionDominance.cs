#nullable enable

using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;

/// <summary>
/// 跨贡献 dominance 判定：新方案支配旧方案当且仅当**余宽不劣于旧方案**且**对每个产品的贡献不劣于旧方案**。
/// Cross-contribution dominance: the new plan dominates the old one iff **its rest width is no worse** and **its contribution to every product is no worse**.
/// </summary>
/// <remarks>
/// 逐条判定：
///
/// | 判据 | 这里 |
/// | --- | --- |
/// | 单位不一致时拒绝 | 单位键不同则返回 false |
/// | 新余宽大于旧余宽时拒绝 | 数值大于则返回 false |
/// | 新产品表缺该产品时拒绝 | 找不到该产品则返回 false |
/// | 新贡献量小于旧贡献量时拒绝 | 数值小于则返回 false |
/// | 全部通过则支配 | 全部通过返回 true |
///
/// **三条规则里最容易写漏的是单位检查**：两个余宽数值可比但单位不同（米 vs 厘米）时，
/// 按数值比会得出错误结论。这里专门检查它。
///
/// **The easiest rule to omit is the unit check**: when two
/// rest widths are numerically comparable but in different units (metres vs centimetres), comparing numbers alone gives the wrong answer. This checks
/// it explicitly.
/// </remarks>
public static class CrossContributionDominance {
    /// <summary>
    /// 新方案是否支配旧方案 / Whether the new plan dominates the existing one.
    /// </summary>
    /// <param name="newPlan">待比较的新方案 / The new plan to compare.</param>
    /// <param name="existingPlan">待比较的已有方案 / The existing plan to compare against.</param>
    /// <returns>新方案是否支配已有方案 / Whether the new plan dominates the existing one.</returns>
    /// <remarks>
    /// **余宽为空时一律返回 false**：空值短路，意思是"不知道余宽就无法判定支配"。
    /// 这是**保守**选择——宁可接受一个可能重复的候选，也不错误剪掉一个更优的候选。
    ///
    /// **A null rest width always yields false**: the code short-circuits on null, meaning "without knowing the rest width, dominance cannot be decided".
    /// That is the **conservative** choice — better to accept a possibly-redundant candidate than to wrongly prune a better one.
    /// </remarks>
    public static bool Dominates(
        CrossContributionProjection newPlan,
        CrossContributionProjection existingPlan
    ) {
        if (newPlan.RestWidth is null || existingPlan.RestWidth is null) {
            return false;
        }

        if (!string.Equals(newPlan.RestWidth.UnitKey, existingPlan.RestWidth.UnitKey, System.StringComparison.Ordinal)) {
            return false;
        }

        if (newPlan.RestWidth.Comparable > existingPlan.RestWidth.Comparable) {
            return false;
        }

        foreach (KeyValuePair<string, CrossContributionMeasure> entry in existingPlan.ContributionsByProduct) {
            if (!newPlan.ContributionsByProduct.TryGetValue(entry.Key, out CrossContributionMeasure? newContribution)) {
                return false;
            }
            if (newContribution.Comparable < entry.Value.Comparable) {
                return false;
            }
        }

        return true;
    }
}
