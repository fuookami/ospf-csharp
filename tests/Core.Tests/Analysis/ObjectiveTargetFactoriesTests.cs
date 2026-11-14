#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Math.Algebra.Number;
using System;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// 相对目标与预算单位目标 / Relative and business-unit targets.
/// </summary>
/// <remarks>
/// **这两个的差别只有一处，而那一处决定了能不能用**：
/// **相对按比例缩放**，**预算单位按绝对量平移**。
/// **把预算单位当成相对，基线一大就会放大十倍。**
///
/// **They differ in one point, and that point decides usability**: **relative scales**, **business-unit translates**. **Treating a delta as a fraction multiplies it tenfold as soon as the baseline is
/// large.**
/// </remarks>
public class ObjectiveTargetFactoriesTests
{
    private static ObjectiveId Id() => new("objective:main");

    /// <summary>**相对目标按比例缩放** / A relative target scales.</summary>
    [Fact]
    public void TheRelativeTarget_ShouldScaleWithTheBaseline() {
        ObjectiveTarget target = ObjectiveTarget.CreateRelativeAtLeast(Id(), new Flt64(100.0), new Flt64(0.05));

        target.Value.ToDouble().Should().BeApproximately(105.0, 1e-9, "five percent of 100 is 5");
        target.Relation.Should().Be(ObjectiveTargetRelation.AtLeast);
    }

    /// <summary>**预算单位目标按绝对量平移** / A business-unit target translates.</summary>
    /// <remarks>
    /// **反向对照**：只测相对会让"两个都写成乘法"通过上一条，
    /// **而那样 100 的预算单位增量会变成 100 × (1 + 5) = 600。**
    ///
    /// **The reverse control**: testing only relative lets "both multiply" pass it, **and then a business-unit delta of 5 against a baseline of 100 becomes 100 × (1 + 5) = 600.**
    /// </remarks>
    [Fact]
    public void TheBusinessUnitTarget_ShouldTranslateInsteadOfScale() {
        ObjectiveTarget target = ObjectiveTarget.CreateBusinessUnitAtLeast(Id(), new Flt64(100.0), new Flt64(5.0));

        target.Value.ToDouble().Should().BeApproximately(105.0, 1e-9, "a delta of five adds five, whatever the baseline");
    }

    /// <summary>**同一个数在两种语义下给出不同结果** / The same number means different things.</summary>
    /// <remarks>
    /// **这是一条"差异"判据**：如果某天有人把两个工厂写成同一份实现，
    /// 上面两条各自仍能通过各自的用例，**而这一条会立刻红。**
    ///
    /// **A difference criterion**: if someone ever collapses the two factories into one implementation, the two cases above still pass individually, **and this one goes red immediately.**
    /// </remarks>
    [Fact]
    public void TheTwoSemantics_ShouldDisagreeOnTheSameNumber() {
        double relative = ObjectiveTarget.CreateRelativeAtLeast(Id(), new Flt64(100.0), new Flt64(5.0))
            .Value.ToDouble();
        double businessUnit = ObjectiveTarget.CreateBusinessUnitAtLeast(Id(), new Flt64(100.0), new Flt64(5.0))
            .Value.ToDouble();

        relative.Should().Be(600.0, "a fivefold fraction against 100 is 600");
        businessUnit.Should().Be(105.0, "a delta of five is 105");
        relative.Should().NotBe(businessUnit, "the same 5 must not mean the same thing in both");
    }

    /// <summary>**非有限输入必须在构造时拒绝** / Non-finite input has to be rejected at construction.</summary>
    /// <remarks>
    /// 一个 NaN 比例会算出一个 NaN 目标值，**而那个值此后一路合法地传下去**：
    /// `ObjectiveBound` 返回 `null`，分析报 `Unsupported`——**"这个目标无法表达成整数边界"。**
    /// **那是一个错误的诊断**：真正的问题是调用方给了 NaN。
    ///
    /// A NaN fraction yields a NaN value that **travels onward looking legitimate**: the bound comes back `null` and the analysis reports `Unsupported`. **The diagnosis is wrong**: the caller passed a NaN.
    /// </remarks>
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void TheNonFiniteInput_ShouldBeRejectedEarly(double bad) {
        Action relative = () => ObjectiveTarget.CreateRelativeAtLeast(Id(), new Flt64(100.0), new Flt64(bad));
        Action businessUnit = () => ObjectiveTarget.CreateBusinessUnitAtLeast(Id(), new Flt64(100.0), new Flt64(bad));
        Action baseline = () => ObjectiveTarget.CreateRelativeAtLeast(Id(), new Flt64(bad), new Flt64(0.05));

        relative.Should().Throw<ArgumentOutOfRangeException>();
        businessUnit.Should().Throw<ArgumentOutOfRangeException>();
        baseline.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>**两个命名顺序给同一个目标** / Both naming orders give the same target.</summary>
    /// <remarks>
    /// 基线同时提供 `relativeAtLeast` 与 `atLeastRelative`（预算单位同理），
    /// **两套名字必须落到同一个值上**，否则调用方按哪套写会得到不同结果。
    ///
    /// The baseline offers both `relativeAtLeast` and `atLeastRelative` (and the same for business units). **The two names must land on the same value**, or the caller's choice of name changes the answer.
    /// </remarks>
    [Fact]
    public void TheTwoNamingOrders_ShouldAgree() {
        ObjectiveTarget.CreateAtLeastRelative(Id(), new Flt64(100.0), new Flt64(0.05))
            .Should().Be(ObjectiveTarget.CreateRelativeAtLeast(Id(), new Flt64(100.0), new Flt64(0.05)),
                "the attribute-order name is an alias, not a second semantic");

        ObjectiveTarget.CreateAtMostBusinessUnit(Id(), new Flt64(100.0), new Flt64(5.0))
            .Should().Be(ObjectiveTarget.CreateBusinessUnitAtMost(Id(), new Flt64(100.0), new Flt64(5.0)));
    }
}
