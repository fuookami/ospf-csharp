// 跨贡献 dominance 判定的测试 / Tests for the cross-contribution dominance decision.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;
using Xunit;

namespace Fuookami.Ospf.Framework.Csp1d.Tests.CuttingPlanGeneration.model;

/// <summary>
/// <see cref="CrossContributionDominance.Dominates"/> 的判定规则如下，必须逐条成立。
/// <see cref="CrossContributionDominance.Dominates"/> follows the rule below, point for point.
/// </summary>
/// <remarks>
/// 判定：新方案支配旧方案当且仅当**余宽不劣**且**对每个产品的贡献不劣**，且**单位一致**。
/// 记录"跨贡献剪枝仍然不会发生"——这里把判定本身补上。
///
/// **每条规则单独一条测试**：合并断言时，某条规则写漏了也不会被看见。
///
/// Dominance holds iff **the rest width is no worse**, **every product contribution is no worse**, and **units agree**. The record that
/// "cross-contribution pruning still never happens" is about the decision itself, which is filled in here.
///
/// **One test per rule**: with merged assertions, an omitted rule goes unnoticed.
/// </remarks>
public class CrossContributionDominanceTests {
    private static CrossContributionMeasure Measure(string unit, double value) =>
        new(UnitKey: unit, ValueText: value.ToString(), Comparable: value);

    private static CrossContributionProjection Plan(
        double restWidth,
        params (string Product, double Quantity)[] contributions) {
        var table = new Dictionary<string, CrossContributionMeasure>();
        foreach ((string product, double quantity) in contributions) {
            table[product] = Measure("m", quantity);
        }
        return new CrossContributionProjection(
            RestWidth: Measure("m", restWidth),
            ContributionsByProduct: table
        );
    }

    /// <summary>**余宽更小、贡献相同 → 支配** / Smaller rest width, same contributions → dominates.</summary>
    [Fact]
    public void SmallerRestWidth_ShouldDominate() {
        CrossContributionProjection newPlan = Plan(restWidth: 3.0, ("p1", 5.0));
        CrossContributionProjection existing = Plan(restWidth: 5.0, ("p1", 5.0));

        CrossContributionDominance.Dominates(newPlan, existing).Should().BeTrue();
    }

    /// <summary>**余宽更大 → 不支配** / Larger rest width → does not dominate.</summary>
    [Fact]
    public void LargerRestWidth_ShouldNotDominate() {
        CrossContributionProjection newPlan = Plan(restWidth: 7.0, ("p1", 9.0));
        CrossContributionProjection existing = Plan(restWidth: 5.0, ("p1", 5.0));

        CrossContributionDominance.Dominates(newPlan, existing).Should().BeFalse();
    }

    /// <summary>**余宽相等 → 余宽这一条不否决** / Equal rest width → that rule does not veto.</summary>
    [Fact]
    public void EqualRestWidth_ShouldNotVeto() {
        CrossContributionProjection newPlan = Plan(restWidth: 5.0, ("p1", 5.0));
        CrossContributionProjection existing = Plan(restWidth: 5.0, ("p1", 5.0));

        CrossContributionDominance.Dominates(newPlan, existing).Should().BeTrue();
    }

    /// <summary>**某个产品贡献更少 → 不支配** / A smaller product contribution → does not dominate.</summary>
    [Fact]
    public void SmallerContribution_ShouldNotDominate() {
        CrossContributionProjection newPlan = Plan(restWidth: 3.0, ("p1", 3.0), ("p2", 5.0));
        CrossContributionProjection existing = Plan(restWidth: 5.0, ("p1", 5.0), ("p2", 5.0));

        CrossContributionDominance.Dominates(newPlan, existing).Should().BeFalse();
    }

    /// <summary>**新产品表缺某个产品 → 不支配** / The new table lacks a product → does not dominate.</summary>
    /// <remarks>
    /// `newContributions[productId] ?: return false`。**缺产品与"贡献为 0"是两件事**：
    /// 若用 `GetValueOrDefault` 默认 0，缺产品会被当成"贡献 0"，只要旧方案贡献也 ≥ 0 就误判支配。
    ///
 /// This implementation uses `newContributions[productId] ?: return false`. **A missing product and "contribution 0" are different things**: defaulting to 0
    /// would treat a missing product as "contribution 0", wrongly granting dominance whenever the existing contribution is also ≥ 0.
    /// </remarks>
    [Fact]
    public void MissingProduct_ShouldNotDominate() {
        CrossContributionProjection newPlan = Plan(restWidth: 3.0, ("p1", 9.0));
        CrossContributionProjection existing = Plan(restWidth: 5.0, ("p1", 5.0), ("p2", 1.0));

        CrossContributionDominance.Dominates(newPlan, existing).Should().BeFalse();
    }

    /// <summary>**余宽单位不同 → 不支配** / Different rest-width units → does not dominate.</summary>
    /// <remarks>
    /// 这是**最容易写漏的一条**：3 厘米与 5 米按数值比会得出"3 更小、支配"，而实际 3 厘米远小于 5 米但**单位不可比**。
    /// 本实现专门检查单位，本判据专门钉住它。
    ///
    /// This is **the easiest rule to omit**: comparing 3 centimetres against 5 metres numerically says "3 is smaller, dominates", yet the units are not
 /// comparable. The implementation checks units explicitly and this criterion pins it.
    /// </remarks>
    [Fact]
    public void DifferentUnits_ShouldNotDominate() {
        CrossContributionProjection newPlan = new(
            RestWidth: Measure("cm", 3.0),
            ContributionsByProduct: new Dictionary<string, CrossContributionMeasure> { ["p1"] = Measure("m", 5.0) }
        );
        CrossContributionProjection existing = new(
            RestWidth: Measure("m", 5.0),
            ContributionsByProduct: new Dictionary<string, CrossContributionMeasure> { ["p1"] = Measure("m", 5.0) }
        );

        CrossContributionDominance.Dominates(newPlan, existing).Should().BeFalse();
    }

    /// <summary>**余宽为空 → 不支配（保守）** / Null rest width → does not dominate (conservative).</summary>
    [Fact]
    public void NullRestWidth_ShouldNotDominate() {
        CrossContributionProjection newPlan = new(
            RestWidth: null,
            ContributionsByProduct: new Dictionary<string, CrossContributionMeasure> { ["p1"] = Measure("m", 9.0) }
        );
        CrossContributionProjection existing = Plan(restWidth: 5.0, ("p1", 5.0));

        CrossContributionDominance.Dominates(newPlan, existing).Should().BeFalse();
        CrossContributionDominance.Dominates(existing, newPlan).Should().BeFalse();
    }

    /// <summary>**旧方案没有产品时，只要余宽不劣就支配** / With no products on the existing plan, a no-worse rest width suffices.</summary>
    [Fact]
    public void NoProducts_ShouldDominateOnRestWidthAlone() {
        CrossContributionProjection newPlan = Plan(restWidth: 5.0);
        CrossContributionProjection existing = Plan(restWidth: 5.0);

        CrossContributionDominance.Dominates(newPlan, existing).Should().BeTrue();
    }
}
