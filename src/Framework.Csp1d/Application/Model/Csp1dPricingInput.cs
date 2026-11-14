#nullable enable

using System.Collections.Generic;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Framework.Csp1d.Application.Model;
/// <summary>
/// CSP1D 定价输入 / CSP1D pricing input.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
/// <param name="GenerationInput">方案生成输入 / Cutting-plan generation input.</param>
/// <param name="ShadowPrices">对偶价格 / Dual prices.</param>
/// <param name="MaxGeneratedPlans">最多产出多少列 / Maximum number of columns to emit.</param>
/// <param name="ObjectiveConfig">定价目标配置；为 null 时成本只算基础值 1 / Pricing objective config; null means base cost 1 only.</param>
/// <param name="BenefitModifiers">对偶收益修正器；按顺序折叠 / Dual-benefit modifiers, folded in order.</param>
/// <param name="CostModifiers">目标成本修正器；按顺序折叠 / Objective-cost modifiers, folded in order.</param>
/// <param name="ImprovingJudges">
/// 自定义"是否改善"判断器；**返回 null 表示跳过**，第一个返回非 null 的判据生效，
/// 全部返回 null 时回退到默认的"收益 &gt; 成本" / Custom "is improving" judges; **null means skip**, the first non-null judge wins, and all-null falls back to the
/// default "benefit &gt; cost".
/// </param>
/// <remarks>
/// **这三个扩展点照抄基线 `Csp1dPricingInput`**。基线签名：
///
/// ```kotlin
/// val pricingBenefitModifiers: List&lt;(CuttingPlan&lt;V&gt;, V) -&gt; V&gt; = emptyList(),
/// val pricingCostModifiers: List&lt;(CuttingPlan&lt;V&gt;, V) -&gt; V&gt; = emptyList(),
/// val isImprovingJudges: List&lt;(CuttingPlan&lt;V&gt;, V, V) -&gt; Boolean?&gt; = emptyList(),
/// ```
///
/// **`isImprovingJudges` 的三值语义是关键**：返回 `null` 表示"这条判据不表态"，继续问下一条；
/// **只有全都不表态时才回退默认逻辑**。若实现把 `null` 当成 `false`，
/// 一条"只管某类方案"的判据会把**其余所有方案**都判成不改善——**列生成立刻枯竭**。
///
/// **These three extension points copy the baseline `Csp1dPricingInput`** (signatures above).
///
/// **The three-valued semantics of `isImprovingJudges` is the crux**: `null` means "this judge abstains", so the next one is asked; **only when all abstain does the
/// default logic apply**. Treating `null` as `false` would make a judge that only handles one kind of plan **reject every other plan** — **column generation would dry
/// up immediately**.
/// </remarks>
public sealed record Csp1dPricingInput<V>(
    Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.GenerationInput<V> GenerationInput,
    Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model.ShadowPriceMap<V> ShadowPrices,
    UInt64 MaxGeneratedPlans,
    Csp1dPricingObjectiveConfig<V>? ObjectiveConfig = null,
    IReadOnlyList<global::System.Func<Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model.CuttingPlan<V>, V, V>>? BenefitModifiers = null,
    IReadOnlyList<global::System.Func<Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model.CuttingPlan<V>, V, V>>? CostModifiers = null,
    IReadOnlyList<global::System.Func<Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model.CuttingPlan<V>, V, V, bool?>>? ImprovingJudges = null
) where V : struct;
