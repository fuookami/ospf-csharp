#nullable enable

using System;

using System.Collections.Generic;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Service;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;

/// <summary>
/// 把切割方案投影成跨贡献判定所需的形状。
/// Projects a cutting plan into the shape the cross-contribution decision needs.
/// </summary>
/// <remarks>
/// 把判定抽成了"余宽 + 贡献表"的投影，但**没有把真实方案接到那个投影上**，
/// 所以生成收集器的投影器参数**只有测试传入**。本类型补上这一环。
///
/// **为什么只对 Flt64 生效**：物理量数值类型是泛型，而"转成可比较数值"需要一个数值转换器实例；
/// 那些转换器访问器都是**实例成员**，泛型方法里拿不到实例（具体常量类是 internal）。
///
/// 而 Flt64 是**本项目全部求解路径的收敛点**：废料聚合与生产上下文都在切割方案的 Flt64 形式上工作。
/// 只支持 Flt64 覆盖了真实使用，且**不需要发明一个转换器注入协议**。若将来需要别的数值类型，再引入注入。
///
/// extracted the decision into a "rest width + contribution table" projection but **never connected a real plan to it**, so the generation collector's
/// projector parameter was **only supplied by tests**. This type closes that link.
///
/// **Why Flt64 only**: the physical quantity's value type is generic, and "convert to a comparable number" needs a converter instance; those converter accessors
/// are **instance members**, and a generic method cannot obtain an instance (the concrete constants class is internal).
///
/// Flt64 is **the convergence point of every solving path in this project**: the waste aggregation and produce context all work on the Flt64 form of the
/// cutting plan. Supporting only Flt64 covers real usage **without inventing a converter-injection protocol**. If another numeric type is needed later, injection
/// can be introduced then.
/// </remarks>
public static class CrossContributionProjector {
    /// <summary>把切割方案投影成跨贡献判定形状 / Project a cutting plan into the cross-contribution shape.</summary>
    /// <param name="plan">切割方案 / The cutting plan.</param>
    /// <returns>投影；余宽未知时为空 / The projection; null when the rest width is unknown.</returns>
    /// <remarks>
    /// **余宽为空时返回空，而不是返回一个余宽为空的投影**：判定对空余宽的处理是"不支配"（保守），
    /// 但投影器返回空能让调用方**完全跳过这个候选**，省掉一次无意义的比较。
    /// 两者最终都导致"不剪枝"，但空值让意图更清楚。
    ///
    /// **When the rest width is unknown this returns null rather than a projection with a null rest width**: the decision treats a null rest width as
    /// "does not dominate" (conservative), but returning null lets the caller **skip the candidate entirely**, saving a pointless comparison. Both end up
    /// "no pruning", but null states the intent more clearly.
    /// </remarks>
    public static CrossContributionProjection? Project(CuttingPlan<Flt64>? plan) {
        if (plan is null) {
            return null;
        }

        Quantity<Flt64>? restWidth = plan.RestWidth;
        if (restWidth is null) {
            return null;
        }

        var contributions = new Dictionary<string, CrossContributionMeasure>(System.StringComparer.Ordinal);
        foreach (CuttingPlanDemandContribution<Flt64> contribution in plan.DemandContributions) {
            contributions[contribution.Product.Id] = Measure(contribution.Quantity);
        }

        return new CrossContributionProjection(
            RestWidth: Measure(restWidth),
            ContributionsByProduct: contributions
        );
    }

    /// <summary>
    /// 为生成器提供投影委托；数值类型不是 `Flt64` 时返回空。
    /// / Provide the projection delegate to a generator; returns null when the value type is not `Flt64`.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
    /// <returns>投影委托，或空 / The projection delegate, or null.</returns>
    /// <remarks>
    /// **这是把投影器接进生成器的唯一入口。** 在 之前 `GenerationCollector` 虽然接受
    /// `crossContributionProjector`，**但没有任何生成器传过它**——跨贡献剪枝在真实运行中从不发生。
    ///
    /// 只对 `Flt64` 生效，原因见 `Project`：数值转换器的访问器是实例成员，泛型方法里拿不到。
    ///
    /// **This is the single entry point wiring the projector into the generators.** Before `GenerationCollector` accepted a
    /// `crossContributionProjector`, **but no generator ever passed one** — cross-contribution pruning never happened in a real run.
    ///
    /// It applies only to `Flt64` for the reason stated on `Project`: the numeric converter's accessors are instance members, unavailable from a generic method.
    /// </remarks>
    public static Func<CuttingPlan<V>, CrossContributionProjection?>? ForGenerator<V>() where V : struct =>
        typeof(V) == typeof(Flt64)
            ? plan => Project((CuttingPlan<Flt64>)(object)plan)
            : null;

    /// <summary>
    /// 把物理量压成可比较的度量 / Reduce a quantity to a comparable measure.
    /// </summary>
    /// <param name="quantity">物理量 / Quantity.</param>
    /// <returns>度量 / The measure.</returns>
    /// <remarks>
    /// **单位键走唯一实现 `GenerationQuantityMath.UnitKey`，与基线逐字一致。**
    ///
    /// 之前这里落到空串，改成 `类型全名|ToString`——**那一步是错的**：基线
    /// `canonicalUnitKey` 是 `symbol ?: name ?: toString`，**不看类型**。加类型名会让 C# 与基线
    /// 对同一份输入给出不同的键。
    ///
    /// 同时撤回那一步并收敛实现：本处与 `GenerationWidthIndex.CanonicalUnitKey` 都委托到
    /// `GenerationQuantityMath.UnitKey`，与基线"只有一个 private fun"一致。
    ///
    /// **The unit key routes through the single implementation `GenerationQuantityMath.UnitKey`, byte-for-byte with the baseline.** Before this fell back to
    /// the empty string, and changed it to `full type name|ToString` — **that step was wrong**: the baseline's `canonicalUnitKey` is
    /// `symbol ?: name ?: toString` and **never looks at the type**. Adding the type name makes C# and the baseline disagree on the same input.
    ///
    /// both reverts that step and collapses the implementation: this and `GenerationWidthIndex.CanonicalUnitKey` delegate to `GenerationQuantityMath.UnitKey`,
    /// matching the baseline's "one private fun".
    ///
    /// **The unit key routes through the single implementation `GenerationQuantityMath.UnitKey`.** Before this fell back to the empty string and changed
    /// it to `full type name|ToString` — but **three copies each writing it out** was the real problem. collapses it into one: both this and
    /// `GenerationWidthIndex.CanonicalUnitKey` delegate there.
    ///
    /// `CrossContributionDominance.Dominates` conservatively returns "does not dominate" on **mismatched** unit keys; collapsing them turns "incomparable" into
    /// "comparable" and **can prune a plan that should have been kept**.
    ///
    /// Built-in units all have both `Symbol` and `Name`, so this is **currently unreachable** — but it is a real latent trap, not worth betting that nobody will
    /// write a custom unit.
    ///
    /// </remarks>
    private static CrossContributionMeasure Measure(Quantity<Flt64> quantity) {
        // 单位键走唯一实现，不再各写一份。
        // The unit key routes through the single implementation instead of its own copy.
        string unitKey = GenerationQuantityMath.UnitKey(quantity.Unit);
        return new CrossContributionMeasure(
            UnitKey: unitKey,
            ValueText: quantity.Value.ToString() ?? "",
            Comparable: quantity.Value.ToDouble()
        );
    }
}
