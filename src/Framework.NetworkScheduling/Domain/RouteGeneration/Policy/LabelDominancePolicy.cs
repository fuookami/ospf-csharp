// 定价列选择与标签支配策略 / Pricing column selection and label dominance policies.
// And.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteGeneration.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Model;
using Fuookami.Ospf.Math.Algebra.Concept;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteGeneration.Policy;

/// <summary>
/// 定价列选择策略 / Pricing column selector policy.
/// </summary>
/// <remarks>
/// 负责挑出 reduced cost 小于 `-pricingTolerance` 的列，并截断到 `maxColumnsPerPricing`。
/// Responsible for picking columns whose reduced cost is below `-pricingTolerance` and truncating to `maxColumnsPerPricing`.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public interface IPricingColumnSelector<V> where V : struct, IRealNumber<V> {
    /// <summary>
    /// 选择要加入 RMP 的列 / Select the columns to add to the RMP.
    /// </summary>
    /// <param name="columns">候选列 / Candidate columns.</param>
    /// <param name="maxColumns">最大列数 / Maximum column count.</param>
    /// <returns>选中的列 / The selected columns.</returns>
    IReadOnlyList<Route<V>> Select(IReadOnlyList<Route<V>> columns, int maxColumns);
}

/// <summary>
/// 保留全部列的默认选择策略 / The default selector that keeps every column.
/// </summary>
/// <remarks>
/// 截断交给定价器处理。这样"选列"与"截断"的职责分离，定价器才能正确区分"没有更多负列"（收敛）与"找到太多被截断"（中断）。
/// Truncation is left to the pricer, keeping selection and truncation separate so the pricer can tell "no more negative columns"
/// (convergence) from "too many found, truncated" (interruption).
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class KeepAllColumnSelector<V> : IPricingColumnSelector<V> where V : struct, IRealNumber<V> {
    /// <summary>共享实例 / Shared instance.</summary>
    public static KeepAllColumnSelector<V> Instance { get; } = new KeepAllColumnSelector<V>();

    /// <inheritdoc/>
    public IReadOnlyList<Route<V>> Select(IReadOnlyList<Route<V>> columns, int maxColumns) {
        ArgumentNullException.ThrowIfNull(columns);
        return columns;
    }
}

/// <summary>
/// 标签支配策略 / Label dominance policy.
/// </summary>
public interface ILabelDominancePolicy {
    /// <summary>
    /// 只有经过证明安全的策略才可剪枝并认证精确定价；自定义策略默认不认证 / Only proven-safe policies may prune and certify
    /// exact pricing; custom policies default to uncertified.
    /// </summary>
    /// <remarks>
    /// **默认假，是安全默认**：支配判定写错时，被剪掉的标签可能原本能扩展出最优路线，而定价会报"精确定价完成"。
    /// 这种错误不会抛异常，只会让主问题收敛到一个更差的解。因此"能否认证精确性"必须由策略主动声明。
    /// **False by default, and that is the safe default**: with a wrong dominance test, a pruned label might have extended into
    /// the optimal route while pricing still reports "exact pricing complete". Nothing throws — the master simply converges on a
    /// worse solution. So a policy must opt in to certifying exactness.
    /// </remarks>
    bool IsExact => false;

    /// <summary>
    /// 判断标签 a 是否支配标签 b / Whether label a dominates label b.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="a">候选支配标签 / The potentially dominating label.</param>
    /// <param name="b">被支配标签 / The potentially dominated label.</param>
    /// <returns>是否支配 / Whether a dominates b.</returns>
    bool Dominates<V>(EspprcLabel<V> a, EspprcLabel<V> b) where V : struct, IRealNumber<V>;
}

/// <summary>
/// 默认支配策略：三资源支配 + elementarity + 不可达子集 / Default: three-resource dominance plus elementarity plus the
/// forbidden subset test.
/// </summary>
/// <remarks>
/// **最后一条"至少一个严格不等"不可省**：若把完全相同（各资源相等）的两个标签判为互相支配，两者都会被剪掉，
/// 该节点上就不剩任何标签，路径凭空消失。这条要求保证支配关系是严格偏序的一个方向。
/// **The final "at least one strict inequality" is not optional**: treating two identical labels (all resources equal) as
/// mutually dominating prunes both, leaving no label at that node and making routes vanish.
/// </remarks>
public sealed class DefaultLabelDominancePolicy : ILabelDominancePolicy {
    /// <summary>共享实例 / Shared instance.</summary>
    public static DefaultLabelDominancePolicy Instance { get; } = new DefaultLabelDominancePolicy();

    /// <inheritdoc/>
    public bool IsExact => true;

    /// <inheritdoc/>
    public bool Dominates<V>(EspprcLabel<V> a, EspprcLabel<V> b) where V : struct, IRealNumber<V> {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        if (!a.CurrentNode.Equals(b.CurrentNode)) {
            return false;
        }
        if (a.ReducedCost.CompareTo(b.ReducedCost) > 0
            || a.Time.CompareTo(b.Time) > 0
            || a.Load.CompareTo(b.Load) > 0) {
            return false;
        }
        if (!IsVisitedSubset(a, b)) {
            return false;
        }
        if (!a.Forbidden.IsSubsetOf(b.Forbidden)) {
            return false;
        }
        return a.ReducedCost.CompareTo(b.ReducedCost) < 0
            || a.Time.CompareTo(b.Time) < 0
            || a.Load.CompareTo(b.Load) < 0;
    }

    /// <summary>
    /// 判断 a 的已访问集合是否为 b 的子集 / Whether a's visited set is a subset of b's.
    /// </summary>
    /// <remarks>
    /// **客户总数不同直接返回假**：两个标签来自不同实例时比较位集毫无意义，而位集会因字长对齐看起来"差不多"。
    /// **Differing customer counts return false immediately**: comparing bitsets from different instances is meaningless, and
    /// word alignment can make them look compatible.
    /// </remarks>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="a">候选支配标签 / The potentially dominating label.</param>
    /// <param name="b">被支配标签 / The potentially dominated label.</param>
    /// <returns>是否为子集 / Whether it is a subset.</returns>
    private static bool IsVisitedSubset<V>(EspprcLabel<V> a, EspprcLabel<V> b)
        where V : struct, IRealNumber<V> {
        if (a.Visited.Size != b.Visited.Size) {
            return false;
        }
        for (int index = 0; index < a.Visited.Size; index++) {
            if (a.Visited.Contains(index) && !b.Visited.Contains(index)) {
                return false;
            }
        }
        return true;
    }
}
