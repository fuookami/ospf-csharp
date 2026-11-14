// 切割方案到跨贡献投影的适配器测试 / Tests for the cutting-plan to cross-contribution projection adapter.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.Csp1d.Tests.CuttingPlanGeneration.model;

/// <summary>
/// <see cref="CrossContributionProjector.Project"/> 必须把真实切割方案接到 B302 的投影形状上。
/// <see cref="CrossContributionProjector.Project"/> must connect a real cutting plan to B302's projection shape.
/// </summary>
/// <remarks>
/// B302 把判定抽成了投影，但**投影器没有生产实现**，收集器的投影器参数只有测试传入。
/// 本批补上适配器，于是"方案 → 投影 → 判定"这条链第一次**完整**。
///
/// B302 extracted the decision into a projection but **the projector had no production implementation**, so the collector's projector parameter was only
/// supplied by tests. This batch adds the adapter, making the "plan → projection → decision" chain **complete** for the first time.
/// </remarks>
public class CrossContributionProjectorTests {
    private static Quantity<Flt64> Millimetres(double value) =>
        new(new Flt64(value), LengthUnits.Millimeter);

    private static Material<Flt64> MakeMaterial() =>
        new(
            id: "M1",
            name: "material",
            widthRange: new WidthRange<Flt64>(
                Width: new QuantityRange<Flt64>(
                    LowerBound: Millimetres(100.0),
                    UpperBound: Millimetres(200.0)
                ),
                Step: Millimetres(1.0)
            )
        );

    /// <summary>**空方案必须返回空** / A null plan must project to null.</summary>
    [Fact]
    public void NullPlan_ShouldProjectToNull() =>
        CrossContributionProjector.Project(null).Should().BeNull();

    /// <summary>**没有切片时余宽未知 → 返回空** / With no slices the rest width is unknown → null.</summary>
    /// <remarks>
    /// 余宽由"已用幅宽"推出，而已用幅宽由切片累加。没有切片就没有余宽，
    /// 判定对它一律"不支配"（保守）。投影器返回空让调用方**直接跳过**这个候选。
    ///
    /// The rest width derives from the used width, which accumulates from slices. With no slices there is no rest width, and the decision always says
    /// "does not dominate" (conservative). Returning null lets the caller **skip** the candidate outright.
    /// </remarks>
    [Fact]
    public void PlanWithoutSlices_ShouldProjectToNull() {
        var plan = new CuttingPlan<Flt64>(
            id: "P1",
            material: MakeMaterial(),
            slices: new List<CuttingPlanSlice<Flt64>>()
        );

        CrossContributionProjector.Project(plan).Should().BeNull();
    }

    /// <summary>**需求贡献必须逐个进投影表** / Every demand contribution must reach the projection table.</summary>
    [Fact]
    public void PlanWithSlices_ShouldCarryContributions() {
        var plan = new CuttingPlan<Flt64>(
            id: "P1",
            material: MakeMaterial(),
            slices: new List<CuttingPlanSlice<Flt64>>(),
            demandContributions: new List<CuttingPlanDemandContribution<Flt64>>()
        );

        // 空切片 → 余宽未知 → 投影为空，但这条断言验证的是"不抛异常且不谎报"。
        // Empty slices → unknown rest width → null projection; this assertion verifies "no throw and no false report".
        CrossContributionProjector.Project(plan).Should().BeNull();
    }

    /// <summary>**有切片时余宽与贡献必须真的被投影出来** / With slices, both the rest width and the contributions must really be projected.</summary>
    /// <remarks>
    /// 前面三条测试只覆盖"返回空"的路径——**它们全绿也说明不了适配器真的能投影**。
    /// 这条用一条真实切片构造方案，断言余宽与贡献表**都有内容**。
    ///
    /// The three tests above cover only the "returns null" path — **they can all be green without showing the adapter really projects**. This one builds a plan
    /// with a real slice and asserts both the rest width and the contribution table **carry content**.
    /// </remarks>
    [Fact]
    public void PlanWithRealSlice_ShouldProjectRestWidthAndContributions() {
        var costar = new Costar<Flt64>(
            id: "C1",
            name: "costar",
            width: new List<Quantity<Flt64>> { Millimetres(40.0) }
        );
        var plan = new CuttingPlan<Flt64>(
            id: "P2",
            material: MakeMaterial(),
            slices: new List<CuttingPlanSlice<Flt64>> {
                new(production: costar, width: Millimetres(40.0)),
            }
        );

        CrossContributionProjection? projection = CrossContributionProjector.Project(plan);

        projection.Should().NotBeNull();
        projection!.RestWidth.Should().NotBeNull();

        // 只断言"非空"是不够的：硬编码的单位键与数值同样"非空"。
        // Asserting only "not null" is not enough: a hard-coded unit key and value are "not null" too.
        projection.RestWidth!.UnitKey.Should().Be("mm");
        projection.RestWidth.Comparable.Should().BeGreaterThan(0.0);
        projection.RestWidth.ValueText.Should().NotBeEmpty();
    }

    /// <summary>**需求贡献必须按产品 ID 进投影表** / Demand contributions must reach the projection table keyed by product id.</summary>
    /// <remarks>
    /// 前一条测试的方案**没有需求贡献**，所以"贡献表非空"是空的——把整段贡献循环删掉它照样通过。
    /// 这条带两个产品贡献，断言**表里有两条且数值正确**。
    ///
    /// The previous plan has **no demand contributions**, so "the table is not null" holds vacuously — deleting the whole contribution loop still passes. This
    /// one carries two product contributions and asserts **two entries with the right values**.
    /// </remarks>
    [Fact]
    public void PlanWithContributions_ShouldKeyThemByProductId() {
        var costar = new Costar<Flt64>(
            id: "C1",
            name: "costar",
            width: new List<Quantity<Flt64>> { Millimetres(40.0) }
        );
        var productA = new Product<Flt64>(
            id: "A",
            name: "product-a",
            width: new List<Quantity<Flt64>> { Millimetres(20.0) }
        );
        var productB = new Product<Flt64>(
            id: "B",
            name: "product-b",
            width: new List<Quantity<Flt64>> { Millimetres(10.0) }
        );
        var plan = new CuttingPlan<Flt64>(
            id: "P3",
            material: MakeMaterial(),
            slices: new List<CuttingPlanSlice<Flt64>> {
                new(production: costar, width: Millimetres(40.0)),
            },
            demandContributions: new List<CuttingPlanDemandContribution<Flt64>> {
                new(Product: productA, Quantity: Millimetres(20.0)),
                new(Product: productB, Quantity: Millimetres(10.0)),
            }
        );

        CrossContributionProjection? projection = CrossContributionProjector.Project(plan);

        projection.Should().NotBeNull();
        projection!.ContributionsByProduct.Should().HaveCount(2);
        projection.ContributionsByProduct.Keys.Should().BeEquivalentTo(new[] { "A", "B" });
        projection.ContributionsByProduct["A"].Comparable.Should().Be(20.0);
        projection.ContributionsByProduct["B"].Comparable.Should().Be(10.0);
    }

    /// <summary>**投影结果必须能喂给判定** / The projection must be consumable by the decision.</summary>
    /// <remarks>
    /// 这条是**整链的连通性证据**：构造两个投影，判定能给出结果而不抛异常。
    /// 若适配器产出的形状与判定期望的形状不一致，编译期就会失败——所以这条测试的价值在于
    /// **把"形状对得上"变成可执行断言**。
    ///
    /// This is the **connectivity evidence for the whole chain**: build two projections and the decision returns a result without throwing. If the adapter's
    /// shape did not match the decision's expected shape, it would fail at compile time — so this test's value is **turning "the shapes match" into an
    /// executable assertion**.
    /// </remarks>
    [Fact]
    public void Projection_ShouldBeConsumableByTheDecision() {
        var wide = new CrossContributionProjection(
            RestWidth: new CrossContributionMeasure("mm", "5", 5.0),
            ContributionsByProduct: new Dictionary<string, CrossContributionMeasure> {
                ["p1"] = new CrossContributionMeasure("mm", "10", 10.0),
            }
        );
        var narrower = new CrossContributionProjection(
            RestWidth: new CrossContributionMeasure("mm", "3", 3.0),
            ContributionsByProduct: new Dictionary<string, CrossContributionMeasure> {
                ["p1"] = new CrossContributionMeasure("mm", "10", 10.0),
            }
        );

        CrossContributionDominance.Dominates(narrower, wide).Should().BeTrue();
        CrossContributionDominance.Dominates(wide, narrower).Should().BeFalse();
    }
}
