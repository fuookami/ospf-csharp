#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.Intermediate;
using System;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Model.Intermediate;

/// <summary>
/// 使用位置：被引用优先于"目标与约束都有" / Referenced outranks "both".
/// </summary>
/// <remarks>
/// B1179 按基线 `FunctionExpansion.kt` 的 `FunctionUsageSummary`、`FunctionUsageLocation`、
/// `FunctionInputShape`、`PiecewiseContinuity` 补。
///
/// **四个布尔是事实，三个派生属性是决定——分开它们让"何时保留结果变量"只有一处规则。**
///
/// B1179 adds the baseline's `FunctionUsageSummary` and its three companions.
///
/// **Four booleans are facts, three derived members are decisions — keeping them apart puts "when to retain the result
/// variable" in one place.**
/// </remarks>
public class FunctionUsageSummaryTests
{
    /// <summary>
    /// **被引用优先于"目标与约束都有"** / Referenced outranks "both".
    /// </summary>
    /// <remarks>
    /// **这是本批的核心判据，也是唯一能被写错的地方。**
    ///
    /// **一个既出现在目标、又出现在约束、同时被别的结构引用的函数，
    /// 位置应当报 `Nested`——因为"被引用"是最强的保留理由。**
    ///
    /// **把顺序调换（先判 `ObjectiveAndConstraint`），这个函数会报"目标与约束都有"；
    /// 调用方据此认为它不需要额外保留，模型就错了。**
    ///
    /// **This is the batch's core criterion and the only place it can be written wrong.**
    ///
    /// **A function appearing in both the objective and a constraint while also being referenced by another structure
    /// must report `Nested` — being referenced is the strongest reason to retain it.**
    ///
    /// **Reordering (testing `ObjectiveAndConstraint` first) reports "both", the caller concludes no extra retention is
    /// needed, and the model is wrong.**
    /// </remarks>
    [Fact]
    public void TheNestedUsage_ShouldOutrankBothObjectiveAndConstraint()
    {
        var nested = new FunctionUsageSummary(
            InObjective: true,
            InConstraint: true,
            NestedAsInput: true);

        nested.Location.Should().Be(
            FunctionUsageLocation.Nested,
            "being referenced is the strongest reason to retain the result");

        var external = new FunctionUsageSummary(
            InObjective: true,
            InConstraint: true,
            ExternallyReferenced: true);

        external.Location.Should().Be(
            FunctionUsageLocation.External,
            "an explicit external reference outranks both as well");
    }

    /// <summary>**其余四种位置** / The remaining four locations.</summary>
    [Fact]
    public void TheOtherLocations_ShouldFollowTheFacts()
    {
        new FunctionUsageSummary(InObjective: true, InConstraint: true).Location.Should()
            .Be(FunctionUsageLocation.ObjectiveAndConstraint);
        new FunctionUsageSummary(InObjective: true).Location.Should()
            .Be(FunctionUsageLocation.ObjectiveOnly);
        new FunctionUsageSummary(InConstraint: true).Location.Should()
            .Be(FunctionUsageLocation.ConstraintOnly);
        new FunctionUsageSummary().Location.Should()
            .Be(FunctionUsageLocation.Unused);
    }

    /// <summary>
    /// **`Nested` 优先于 `External`** / `Nested` outranks `External`.
    /// </summary>
    /// <remarks>
    /// **两个都成立时基线报 `Nested`。**
    /// **这条单独立一个判据，是因为"先判 `Nested` 还是先判 `External`"是
    /// 一个只有在两者<b>同时</b>为真时才看得出的差别。**
    /// **只测单独情形的话，两种顺序都能通过。**
    ///
    /// **The baseline reports `Nested` when both hold.**
    /// **This gets its own criterion because "check `Nested` first or `External` first" is a difference visible only when
    /// <b>both</b> are true.** **Testing the single cases alone passes under either order.**
    /// </remarks>
    [Fact]
    public void TheNestedAndExternal_ShouldReportNested()
    {
        new FunctionUsageSummary(NestedAsInput: true, ExternallyReferenced: true).Location.Should()
            .Be(FunctionUsageLocation.Nested, "the baseline tests nested first");
    }

    /// <summary>**保留结果变量的四种理由** / The four reasons to retain the result.</summary>
    /// <remarks>
    /// **`InObjective &amp;&amp; InConstraint` 那一项是冗余的**（**`InConstraint` 已覆盖**），
    /// **保留它只为让这个情形在表达式里可见。**
    /// **判据因此断言"仅目标时不保留"——那是唯一一个 <b>false</b> 的非平凡情形。**
    ///
    /// **The `InObjective &amp;&amp; InConstraint` term is redundant** (**`InConstraint` covers it**), **kept only so the case
    /// stays visible.**
    /// **The criterion therefore asserts "objective only does not retain" — the one non-trivial <b>false</b> case.**
    /// </remarks>
    [Fact]
    public void TheRequiresResultVariable_ShouldCoverFourReasons()
    {
        new FunctionUsageSummary(InConstraint: true).RequiresResultVariable.Should().BeTrue();
        new FunctionUsageSummary(NestedAsInput: true).RequiresResultVariable.Should().BeTrue();
        new FunctionUsageSummary(ExternallyReferenced: true).RequiresResultVariable.Should().BeTrue();
        new FunctionUsageSummary(InObjective: true, InConstraint: true).RequiresResultVariable.Should().BeTrue();

        new FunctionUsageSummary(InObjective: true).RequiresResultVariable.Should().BeFalse(
            "an objective-only usage can be lowered without a retained column");
        new FunctionUsageSummary().RequiresResultVariable.Should().BeFalse();
    }

    /// <summary>
    /// **仅目标原生要求单变量** / Objective-only native needs a single variable.
    /// </summary>
    /// <remarks>
    /// **一个多变量的线性表达式无法被"仅目标"的原生接口表达——那个接口只接受单变量分段函数。**
    ///
    /// **A multi-variable linear expression cannot be expressed by the "objective only" native interface — that
    /// interface takes a single-variable piecewise function.**
    /// </remarks>
    [Fact]
    public void TheObjectiveOnlyNative_ShouldDemandASingleVariable()
    {
        new FunctionUsageSummary(
            InObjective: true,
            InputShape: FunctionInputShape.SingleVariable).SupportsObjectiveOnlyNative().Should().BeTrue();

        new FunctionUsageSummary(
            InObjective: true,
            InputShape: FunctionInputShape.LinearExpression).SupportsObjectiveOnlyNative().Should().BeFalse(
                "a linear expression is not a single-variable piecewise function");

        new FunctionUsageSummary(
            InObjective: true,
            InputShape: FunctionInputShape.Unknown).SupportsObjectiveOnlyNative().Should().BeFalse(
                "unknown takes the conservative path");
    }

    /// <summary>**目标与约束都有时不走仅目标原生** / Both in objective and constraint excludes it.</summary>
    [Fact]
    public void TheBothUsage_ShouldNotUseObjectiveOnlyNative()
    {
        new FunctionUsageSummary(
            InObjective: true,
            InConstraint: true,
            InputShape: FunctionInputShape.SingleVariable).SupportsObjectiveOnlyNative().Should().BeFalse(
                "the constraint side needs the retained column");
    }

    /// <summary>**`Unknown` 与 `Discontinuous` 不是同一件事** / `Unknown` and `Discontinuous` differ.</summary>
    /// <remarks>
    /// **前者是"没查过"，后者是"知道有断点"。**
    /// **这条判据只断言两者是<b>不同的枚举值</b>——它们的处理差异在别处，
    /// 而这里要钉住的是"别把两者混成一个"。**
    ///
    /// **The former means "nobody looked", the latter "a break is known".**
    /// **The criterion only asserts they are <b>distinct</b> values — how they are handled lives elsewhere, and what
    /// matters here is not conflating them.**
    /// </remarks>
    [Fact]
    public void TheUnknownAndDiscontinuous_ShouldBeDistinct()
    {
        PiecewiseContinuity.Unknown.Should().NotBe(PiecewiseContinuity.Discontinuous);
        FunctionInputShape.Unknown.Should().NotBe(FunctionInputShape.LinearExpression);

        new FunctionUsageSummary(Continuity: PiecewiseContinuity.Discontinuous).Continuity.Should()
            .Be(PiecewiseContinuity.Discontinuous);
    }

    /// <summary>**默认值全是"未收集"** / Every default is "not collected".</summary>
    [Fact]
    public void TheDefaults_ShouldBeUncollected()
    {
        var empty = new FunctionUsageSummary();

        empty.InObjective.Should().BeFalse();
        empty.InConstraint.Should().BeFalse();
        empty.NestedAsInput.Should().BeFalse();
        empty.ExternallyReferenced.Should().BeFalse();
        empty.InputShape.Should().Be(FunctionInputShape.Unknown);
        empty.Continuity.Should().Be(PiecewiseContinuity.Unknown);
    }
}
