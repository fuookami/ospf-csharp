// 生成策略扩展点的唯一投影处 / The single projection point for generation-strategy hooks.

using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce.Model;

namespace Fuookami.Ospf.Framework.Csp1d.Application.Service;

/// <summary>
/// 把 `ICsp1dGenerationStrategy` 的三个扩展点投影成回调列表 /
/// Project the three `ICsp1dGenerationStrategy` hooks into callback lists.
/// </summary>
/// <remarks>
/// **为什么要有这个类**： 把三个扩展点接进了**两条**路径
/// （`Csp1dColumnGeneration` 的列生成路径、`Csp1dMilp` 的直连路径），
/// 而两次都是**各写一份**投影代码。 只接了一条、另一条静默失效，
/// **正是复制粘贴造成的**。
///
/// 投影逻辑收在这里之后，**"两条路径投影不一致"在结构上不可能**——
/// 除非有人绕过这个类重新手写一遍。
///
/// **Why this class exists**:  wired the three hooks into **two** paths (column generation in `Csp1dColumnGeneration` and the direct path in `Csp1dMilp`),
/// **writing the projection twice**. Wired only one and the other silently failed — **exactly what copy-paste causes**.
///
/// With the projection here, **"the two paths project differently" is structurally impossible** unless someone bypasses this class and hand-writes it again.
/// </remarks>
internal static class Csp1dGenerationStrategyProjection {
    /// <summary>
    /// 投影 `AcceptCandidate` / Project `AcceptCandidate`.
    /// </summary>
    /// <remarks>
    /// 语义由生成侧折叠：基线 `candidateFilters.all { it(plan, input.existingPlans) }`。
    /// The semantics are folded on the generation side: the baseline's `candidateFilters.all { it(plan, input.existingPlans) }`.
    /// </remarks>
    /// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
    /// <param name="strategies">生成策略列表 / Generation strategy list.</param>
    /// <returns>候选过滤回调列表 / Candidate filter callbacks.</returns>
    public static IReadOnlyList<global::System.Func<CuttingPlan<V>, IReadOnlyList<CuttingPlan<V>>, bool>>
        CandidateFilters<V>(IReadOnlyList<ICsp1dGenerationStrategy<V>> strategies)
        where V : struct =>
        strategies.Count == 0
            ? global::System.Array.Empty<global::System.Func<CuttingPlan<V>, IReadOnlyList<CuttingPlan<V>>, bool>>()
            : strategies
                .Select(strategy => (global::System.Func<CuttingPlan<V>, IReadOnlyList<CuttingPlan<V>>, bool>)(
                    (plan, existing) => strategy.AcceptCandidate(plan, existing)))
                .ToList();

    /// <summary>
    /// 投影 `AcceptDominance` / Project `AcceptDominance`.
    /// </summary>
    /// <remarks>
    /// 语义由生成侧折叠：基线 `dominanceAcceptOverrides.all { it(plan, existing) }`。
    /// The semantics are folded on the generation side: the baseline's `dominanceAcceptOverrides.all { it(plan, existing) }`.
    /// </remarks>
    /// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
    /// <param name="strategies">生成策略列表 / Generation strategy list.</param>
    /// <returns>dominance 接受回调列表 / Dominance acceptance callbacks.</returns>
    public static IReadOnlyList<global::System.Func<CuttingPlan<V>, IReadOnlyList<CuttingPlan<V>>, bool>>
        DominanceAcceptOverrides<V>(IReadOnlyList<ICsp1dGenerationStrategy<V>> strategies)
        where V : struct =>
        strategies.Count == 0
            ? global::System.Array.Empty<global::System.Func<CuttingPlan<V>, IReadOnlyList<CuttingPlan<V>>, bool>>()
            : strategies
                .Select(strategy => (global::System.Func<CuttingPlan<V>, IReadOnlyList<CuttingPlan<V>>, bool>)(
                    (plan, existing) => strategy.AcceptDominance(plan, existing)))
                .ToList();

    /// <summary>
    /// 投影 `CanonicalKeyFor` / Project `CanonicalKeyFor`.
    /// </summary>
    /// <remarks>
    /// 返回 `string?`：**null 表示不覆盖**，由解析处回退到默认键。
    /// Returns `string?`: **null means no override**, and the resolver falls back to the default key.
    /// </remarks>
    /// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
    /// <param name="strategies">生成策略列表 / Generation strategy list.</param>
    /// <returns>规范键覆盖回调列表 / Canonical-key override callbacks.</returns>
    public static IReadOnlyList<global::System.Func<CuttingPlan<V>, string?>>
        CanonicalKeyOverrides<V>(IReadOnlyList<ICsp1dGenerationStrategy<V>> strategies)
        where V : struct =>
        strategies.Count == 0
            ? global::System.Array.Empty<global::System.Func<CuttingPlan<V>, string?>>()
            : strategies
                .Select(strategy => (global::System.Func<CuttingPlan<V>, string?>)(plan => strategy.CanonicalKeyFor(plan)))
                .ToList();

    /// <summary>
    /// 按规范键覆盖解析键 / Resolve a canonical key honouring the overrides.
    /// </summary>
    /// <remarks>
    /// **第一个返回非 null 的覆盖胜出**，与基线 `canonicalKeyOverrides.firstNotNullOfOrNull { it(plan) }` 一致。
    ///
    /// **The first override returning non-null wins**, matching the baseline's `canonicalKeyOverrides.firstNotNullOfOrNull { it(plan) }`.
    /// </remarks>
    /// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
    /// <param name="plan">切割方案 / Cutting plan.</param>
    /// <param name="overrides">规范键覆盖回调 / Canonical-key override callbacks.</param>
    /// <returns>解析后的规范键 / The resolved canonical key.</returns>
    public static CuttingPlanCanonicalKey Resolve<V>(
        CuttingPlan<V> plan,
        IReadOnlyList<global::System.Func<CuttingPlan<V>, string?>> overrides)
        where V : struct {
        foreach (global::System.Func<CuttingPlan<V>, string?> override_ in overrides) {
            if (override_(plan) is { } customKey) {
                return new CuttingPlanCanonicalKey(customKey);
            }
        }
        return CuttingPlanCanonicalKey.From(plan);
    }
}
