// 余料汇总与超产上界判据 / Criteria for remnant summarisation and over-production bounds.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Framework.Csp2d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp2d.Domain.Produce;
using Fuookami.Ospf.Framework.Csp2d.Domain.WastingMinimization;
using Fuookami.Ospf.Framework.Csp2d.Domain.Yield;
using Fuookami.Ospf.Framework.Csp2d.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.Csp2d.Tests.Domain;

/// <summary>
/// **余料汇总与超产上界** / Remnant summarisation and over-production bounds.
/// </summary>
/// <remarks>
/// 对应基线 `WasteContext.kt` 与 `YieldContext.kt`。两者都只差 `produce` 层的类型，B456—B457 之后才可落地。
///
/// Mirrors the baseline `WasteContext.kt` and `YieldContext.kt`. Both only needed the `produce` layer, which landed in B456—B457.
/// </remarks>
public class WasteAndYieldTests {
    /// <summary>**阈值以物料目录为准** / The catalogue threshold wins over the pattern's own material.</summary>
    /// <remarks>
    /// 汇总器收物料目录就是为了**统一口径**：同一编号在不同图案里带着不同阈值时，必须以目录为准，
    /// 否则同一块料在两张报表里会有两种"能不能复用"。
    ///
    /// The summariser takes a catalogue precisely to **fix one threshold**: when the same identifier carries different thresholds across patterns, the catalogue must win,
    /// or the same remnant reports two different "reusable" answers.
    /// </remarks>
    [Fact]
    public void Summarize_ShouldUseTheCatalogueThreshold() {
        Pattern2<Flt64> pattern = PatternOf("MAT1", minRemnantArea: null);
        Material2<Flt64> strict = new(
            Csp2dIds.MaterialId2Of("MAT1"), "steel", Metres(4.0), Metres(2.0),
            MinRemnantArea: SquareMetres(100.0));
        Csp2dRemnantSummarizer summarizer = new(new List<Material2<Flt64>> { strict });

        Csp2dRemnantSummary summary = summarizer.Summarize(new List<Pattern2<Flt64>> { pattern })[0];

        summary.Usable.Should().BeFalse("the catalogue demands 100 square metres of remnant");
        summary.PatternId.Should().Be("PAT1");
        summary.MaterialId.Should().Be("MAT1");
        summary.WasteArea.Value.ToDouble().Should().BeApproximately(7.0, 1e-12,
            "an 8 square metre sheet with one 1 square metre piece leaves 7");
    }

    /// <summary>**目录缺条目时回退到图案自带物料** / A missing catalogue entry falls back to the pattern's material.</summary>
    [Fact]
    public void Summarize_ShouldFallBackToThePatternMaterial() {
        Pattern2<Flt64> pattern = PatternOf("MAT9", minRemnantArea: SquareMetres(1.0));
        Csp2dRemnantSummarizer summarizer = new(new List<Material2<Flt64>>());

        Csp2dRemnantSummary summary = summarizer.Summarize(new List<Pattern2<Flt64>> { pattern })[0];

        summary.Usable.Should().BeTrue("7 square metres of remnant exceeds the pattern's own 1 square metre threshold");
    }

    /// <summary>**不可算的余料不进汇总** / A remnant that cannot be computed is left out.</summary>
    /// <remarks>
    /// 记一条零面积会让报表声称"这块料没剩什么"，而真实情况是**这笔账算不成**。
    ///
    /// Recording a zero area would claim "nothing is left over", while the truth is that **the arithmetic failed**.
    /// </remarks>
    [Fact]
    public void Summarize_ShouldSkipPatternsWithIncomparableAreas() {
        Pattern2<Flt64> broken = new(
            Csp2dIds.PatternId2Of("PAT-BAD"),
            MaterialOf("MAT1", null),
            Region2Factory.Region2Of(Metres(0.0), Metres(0.0), Metres(4.0), Metres(2.0)),
            new List<Placement2<Flt64>> {
                new(new RectanglePackingShape2<Flt64>(Metres(1.0), Kilograms(1.0)),
                    new QuantityPoint2<Flt64>(Metres(0.0), Metres(0.0)), false, "P1"),
            },
            new List<Placement2<Flt64>>());
        Csp2dRemnantSummarizer summarizer = new(new List<Material2<Flt64>>());

        summarizer.Summarize(new List<Pattern2<Flt64>> { broken }).Should().BeEmpty(
            "a metre-kilogram placement has no area to subtract");
    }

    /// <summary>**可复用余料只累加可用项** / Only usable remnants are summed.</summary>
    [Fact]
    public void ReusableArea_ShouldOnlySumUsableRemnants() {
        Csp2dRemnantSummarizer summarizer = new(new List<Material2<Flt64>>());
        IReadOnlyList<Csp2dRemnantSummary> summaries = new List<Csp2dRemnantSummary> {
            new("P1", "M1", SquareMetres(3.0), true, 0.5),
            new("P2", "M1", SquareMetres(4.0), false, 0.5),
            new("P3", "M1", SquareMetres(2.0), true, 0.5),
        };

        summarizer.ReusableArea(summaries, SquareMetres(0.0)).Value.ToDouble()
            .Should().BeApproximately(5.0, 1e-12, "the unusable four square metres must not be counted");
    }

    /// <summary>**余料最小化目标可注册，零权重等同不优化** / The waste objective registers, and a zero weight optimises nothing.</summary>
    [Fact]
    public void WasteMinimization_ShouldRegisterAnObjective() {
        using var model = new LinearMetaModel<Flt64>();
        IReadOnlyList<CuttingPlan2<Flt64>> plans = PlansOf();
        Csp2dPatternAssignment assignment = new(plans);
        assignment.Register(model, 10UL).IsOk.Should().BeTrue();

        new Csp2dWasteMinimization(plans, assignment).Invoke(model).IsOk.Should().BeTrue();
        new Csp2dWasteMinimization(plans, assignment, Flt64.Zero).Invoke(model).IsOk.Should().BeTrue(
            "a zero-weight objective is legal, it just optimises nothing");
    }

    /// <summary>**超产上界：空配置不注册、无覆盖不注册、有覆盖才注册** / Over-production bounds register only where configured and covered.</summary>
    [Fact]
    public void OverProductionBound_ShouldRegisterOnlyCoveredAndBoundedDemands() {
        using var model = new LinearMetaModel<Flt64>();
        IReadOnlyList<CuttingPlan2<Flt64>> plans = PlansOf();
        Csp2dPatternAssignment assignment = new(plans);
        assignment.Register(model, 10UL).IsOk.Should().BeTrue();
        var demands = new List<ProductDemand2<Flt64>> { Demand("P1"), Demand("P2") };

        int before = model.Constraints.Count;
        Csp2dOverProductionBoundConstraint none = new(plans, assignment, demands, new Csp2dYieldConfig());
        none.Invoke(model).IsOk.Should().BeTrue();
        model.Constraints.Count.Should().Be(before, "an empty bound table adds nothing");

        // **有界但无覆盖、有覆盖但无界，两种都不该注册**：一次只配一种，才能把两条守卫分开验。
        // **Bounded-but-uncovered and covered-but-unbounded must both skip**: configuring one case at a time keeps the two guards distinguishable.
        var boundsOnlyUncovered = new Dictionary<string, Flt64> { ["P2"] = new Flt64(2.0) };
        Csp2dOverProductionBoundConstraint uncovered =
            new(plans, assignment, demands, new Csp2dYieldConfig(boundsOnlyUncovered));
        uncovered.Invoke(model).IsOk.Should().BeTrue();
        model.Constraints.Count.Should().Be(before, "P2 is bounded but no plan covers it");

        var bounds = new Dictionary<string, Flt64> {
            ["P1"] = new Flt64(2.0),
            ["P2"] = new Flt64(2.0),
        };
        Csp2dOverProductionBoundConstraint configured =
            new(plans, assignment, demands, new Csp2dYieldConfig(bounds));
        configured.Invoke(model).IsOk.Should().BeTrue();
        model.Constraints.Count.Should().Be(before + 1,
            "only P1 is covered by the plans, so P2 gets no row");
    }

    /// <summary>一个需求 / One demand.</summary>
    /// <param name="productId">产品编号 / The product identifier.</param>
    /// <returns>需求 / The demand.</returns>
    private static ProductDemand2<Flt64> Demand(string productId) =>
        new(new Product2<Flt64>(Csp2dIds.ProductId2Of(productId), "part", Metres(1.0), Metres(1.0)),
            new DemandValue2<Flt64>.Amount(4UL));

    /// <summary>两个候选方案 / Two candidate plans.</summary>
    /// <returns>方案列表 / The plan list.</returns>
    private static IReadOnlyList<CuttingPlan2<Flt64>> PlansOf() =>
        new List<CuttingPlan2<Flt64>> {
            PlanOf(PatternOf("MAT1", null, pieces: 2)),
            PlanOf(PatternOf("MAT1", null, pieces: 1)),
        };

    /// <summary>由图案构造方案 / Build a plan from a pattern.</summary>
    /// <param name="pattern">图案 / The pattern.</param>
    /// <returns>方案 / The plan.</returns>
    private static CuttingPlan2<Flt64> PlanOf(Pattern2<Flt64> pattern) {
        Product2<Flt64> product = new(Csp2dIds.ProductId2Of("P1"), "part", Metres(1.0), Metres(1.0));
        ulong amount = pattern.ProductAmounts().TryGetValue("P1", out ulong count) ? count : 0UL;
        var contributions = new List<CuttingPlanDemandContribution2<Flt64>>();
        if (amount > 0UL) {
            contributions.Add(new CuttingPlanDemandContribution2<Flt64>(product, amount));
        }
        return new(Csp2dIds.PlanId2Of("PLAN-" + pattern.Id.Value), pattern.Material, pattern, contributions, null);
    }

    /// <summary>构造图案 / Build a pattern.</summary>
    /// <param name="materialId">物料编号 / The material identifier.</param>
    /// <param name="minRemnantArea">最小可用余料面积 / The minimum usable remnant area.</param>
    /// <param name="pieces">件数 / The piece count.</param>
    /// <returns>图案 / The pattern.</returns>
    private static Pattern2<Flt64> PatternOf(
        string materialId,
        Quantity<Flt64>? minRemnantArea,
        int pieces = 1) {
        var placements = new List<Placement2<Flt64>>();
        for (int index = 0; index < pieces; index++) {
            placements.Add(new Placement2<Flt64>(
                new RectanglePackingShape2<Flt64>(Metres(1.0), Metres(1.0)),
                new QuantityPoint2<Flt64>(Metres(index), Metres(0.0)),
                false,
                "P1"));
        }
        return new(
            Csp2dIds.PatternId2Of("PAT1"),
            MaterialOf(materialId, minRemnantArea),
            Region2Factory.Region2Of(Metres(0.0), Metres(0.0), Metres(4.0), Metres(2.0)),
            placements,
            new List<Placement2<Flt64>>());
    }

    /// <summary>构造物料 / Build a material.</summary>
    /// <param name="materialId">物料编号 / The material identifier.</param>
    /// <param name="minRemnantArea">最小可用余料面积 / The minimum usable remnant area.</param>
    /// <returns>物料 / The material.</returns>
    private static Material2<Flt64> MaterialOf(string materialId, Quantity<Flt64>? minRemnantArea) =>
        new(Csp2dIds.MaterialId2Of(materialId), "steel", Metres(4.0), Metres(2.0),
            MinRemnantArea: minRemnantArea);

    /// <summary>构造米量 / Build a length in metres.</summary>
    /// <param name="value">数值 / The value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> Metres(double value) => new(new Flt64(value), SIBaseUnits.Meter);

    /// <summary>构造平方米量 / Build an area in square metres.</summary>
    /// <param name="value">数值 / The value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> SquareMetres(double value) =>
        new(new Flt64(value), AreaUnits.SquareMeter);

    /// <summary>构造千克量 / Build a mass in kilograms.</summary>
    /// <param name="value">数值 / The value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> Kilograms(double value) => new(new Flt64(value), SIBaseUnits.Kilogram);
}
