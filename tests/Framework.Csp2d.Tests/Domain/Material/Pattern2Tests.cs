// 裁切图案判据 / Criteria for cutting patterns.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Framework.Csp2d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp2d.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.Csp2d.Tests.Domain.Material;

/// <summary>
/// **面积账、重叠与工艺一致性** / Area arithmetic, overlaps and process consistency.
/// </summary>
/// <remarks>
/// 对应。放置类型来自几何层（当前住在 `Bpp2d` 里，见 `Framework.Csp2d.csproj` 的 ponytail 记档）。
///
///. Placements come from the geometry layer, which currently lives in `Bpp2d` (see the ponytail note in
/// `Framework.Csp2d.csproj`).
/// </remarks>
public class Pattern2Tests {
    /// <summary>**余料 = 料域 − 成品 − 副产物** / The remnant is the region minus finished parts minus by-products.</summary>
    [Fact]
    public void WasteArea_ShouldSubtractBothKindsOfPlacement() {
        Pattern2<Flt64> pattern = PatternOf(
            Region(4.0, 2.0),
            new List<Placement2<Flt64>> { Placement(0.0, 0.0, 2.0, 1.0, "P1") },
            new List<Placement2<Flt64>> { Placement(2.0, 0.0, 1.0, 1.0, null) });

        pattern.UsedArea().ValueOrThrow().Value.ToDouble().Should().BeApproximately(2.0, 1e-12);
        pattern.CostarArea().ValueOrThrow().Value.ToDouble().Should().BeApproximately(1.0, 1e-12);
        pattern.WasteArea().ValueOrThrow().Value.ToDouble().Should().BeApproximately(5.0, 1e-12);
    }

    /// <summary>**余料下限为零，绝不返回负数** / The remnant floors at zero and never goes negative.</summary>
    /// <remarks>
    /// 负余料在业务上不存在，而它会让**复用阈值判定反过来**（负值永远小于阈值，却也可能被当成"有料"）。
    ///
    /// A negative remnant does not exist in the domain, and it would **invert the reusable-threshold comparison**.
    /// </remarks>
    [Fact]
    public void WasteArea_ShouldFloorAtZeroWhenTheRegionIsFull() {
        Pattern2<Flt64> pattern = PatternOf(
            Region(2.0, 2.0),
            new List<Placement2<Flt64>> {
                Placement(0.0, 0.0, 2.0, 1.0, "P1"),
                Placement(0.0, 1.0, 2.0, 1.0, "P1"),
            },
            new List<Placement2<Flt64>>());

        pattern.WasteArea().ValueOrThrow().Value.ToDouble().Should().BeApproximately(0.0, 1e-12);

        // **超额占用**才真正区分"下限为零"与"直接相减"：等量占用扣出来也是 0，看不出区别。
        // Only an **over-full** placement distinguishes "floored at zero" from "plain subtraction": an exact fit yields 0 either way.
        Pattern2<Flt64> overfull = PatternOf(
            Region(2.0, 2.0),
            new List<Placement2<Flt64>> {
                Placement(0.0, 0.0, 2.0, 2.0, "P1"),
                Placement(0.0, 0.0, 1.0, 1.0, "P1"),
            },
            new List<Placement2<Flt64>>());
        overfull.WasteArea().ValueOrThrow().Value.ToDouble().Should().BeApproximately(0.0, 1e-12,
            "5 square metres on a 4 square metre sheet must not produce a negative remnant");
    }

    /// <summary>**单位不一致时必须报错，而不是静默把面积算成 0** / A unit mismatch must be reported, not silently zeroed.</summary>
    /// <remarks>
    /// `quantitySum(...) ?: quantityZeroOf(...)` 在这一格会给出 `0`，于是"利用率 0%"看起来像一个合法结果，
    /// 而真实情况是**这笔账根本算不成**。这里用一个**量纲根本不是面积**的放置制造这一格：
    /// 一个宽为米、高为千克的"件"，它的 `area` 是米·千克。
    ///
    /// Here a placement whose **dimension is not an area at all** produces that case: a "piece" one metre wide and one kilogram tall has an area of metre-kilogram.
    ///
    /// The code's `quantitySum(...) ?: quantityZeroOf(...)` yields `0` here, so "0% utilization" looks like a legal result while the real situation is that
    /// **the arithmetic cannot be done at all**.
    /// </remarks>
    [Fact]
    public void UsedArea_ShouldFailOnMixedUnits() {
        Pattern2<Flt64> pattern = PatternOf(
            Region(4.0, 2.0),
            Placements("P1", Metres(2.0), Kilograms(1.0)),
            new List<Placement2<Flt64>>());

        Result<Quantity<Flt64>, ErrorCode, Error<ErrorCode>> used = pattern.UsedArea();
        used.IsFailed.Should().BeTrue("a length and an area cannot be summed");
        ((Failed<Quantity<Flt64>, ErrorCode, Error<ErrorCode>>)used).Error.Code
            .Should().Be(ErrorCode.IllegalArgument);
        pattern.Utilization().IsFailed.Should().BeTrue("utilization depends on the sum");
    }

    /// <summary>**件数按产品编号统计，没有编号的放置不计入** / Counts are keyed by product identifier and anonymous placements are skipped.</summary>
    [Fact]
    public void ProductAmounts_ShouldCountByIdentifierAndSkipAnonymousPlacements() {
        Pattern2<Flt64> pattern = PatternOf(
            Region(4.0, 2.0),
            new List<Placement2<Flt64>> {
                Placement(0.0, 0.0, 1.0, 1.0, "P1"),
                Placement(1.0, 0.0, 1.0, 1.0, "P1"),
                Placement(2.0, 0.0, 1.0, 1.0, null),
            },
            new List<Placement2<Flt64>>());

        pattern.ProductAmounts().Should().ContainKey("P1").WhoseValue.Should().Be(2UL);
        pattern.ProductAmounts().Should().HaveCount(1,
            "an anonymous placement is a remnant, not a piece, so it must not add a key at all");
    }

    /// <summary>**利用率只算成品面积** / Utilization counts finished parts only.</summary>
    [Fact]
    public void Utilization_ShouldIgnoreByproducts() {
        Pattern2<Flt64> pattern = PatternOf(
            Region(4.0, 1.0),
            new List<Placement2<Flt64>> { Placement(0.0, 0.0, 2.0, 1.0, "P1") },
            new List<Placement2<Flt64>> { Placement(2.0, 0.0, 2.0, 1.0, null) });

        pattern.Utilization().ValueOrThrow().ToDouble().Should().BeApproximately(0.5, 1e-12,
            "by-products are not demand-driven output and must not inflate utilization");
    }

    /// <summary>**校验拦住四类错误** / Validation stops four kinds of mistake.</summary>
    [Fact]
    public void Validate_ShouldRejectEmptyOverlappingOutOfBoundsAndTreeLessGuillotinePatterns() {
        Pattern2<Flt64> empty = PatternOf(
            Region(4.0, 2.0), new List<Placement2<Flt64>>(), new List<Placement2<Flt64>>());
        empty.Validate(Metres(0.0)).IsFailed.Should().BeTrue("a pattern without any placement is meaningless");

        Pattern2<Flt64> overlapping = PatternOf(
            Region(4.0, 2.0),
            new List<Placement2<Flt64>> {
                Placement(0.0, 0.0, 1.0, 1.0, "P1"),
                Placement(0.5, 0.0, 1.0, 1.0, "P1"),
            },
            new List<Placement2<Flt64>>());
        Result<Success, ErrorCode, Error<ErrorCode>> overlapResult = overlapping.Validate(Metres(0.0));
        overlapResult.IsFailed.Should().BeTrue();
        ((Failed<Success, ErrorCode, Error<ErrorCode>>)overlapResult).Error.Message.Should().Contain("重叠");

        Pattern2<Flt64> outside = PatternOf(
            Region(4.0, 2.0),
            new List<Placement2<Flt64>> { Placement(3.5, 0.0, 1.0, 1.0, "P1") },
            new List<Placement2<Flt64>>());
        result(outside.Validate(Metres(0.0))).Should().Contain("超出料域");

        Pattern2<Flt64> guillotine = new(
            Id: Csp2dIds.PatternId2Of("PAT-G"),
            Material: MaterialOf(),
            Region: Region(4.0, 2.0),
            Placements: new List<Placement2<Flt64>> { Placement(0.0, 0.0, 1.0, 1.0, "P1") },
            CostarPlacements: new List<Placement2<Flt64>>(),
            CutTree: null,
            Process: CuttingProcess2.GuillotineTwoStage);
        result(guillotine.Validate(Metres(0.0))).Should().Contain("切分树");

        Pattern2<Flt64> legal = PatternOf(
            Region(4.0, 2.0),
            new List<Placement2<Flt64>> { Placement(0.0, 0.0, 1.0, 1.0, "P1") },
            new List<Placement2<Flt64>>());
        legal.Validate(Metres(0.0)).IsOk.Should().BeTrue("a legal freeform pattern must pass");
    }

    /// <summary>**余料可复用的判定** / The removable-remnant decision.</summary>
    [Fact]
    public void HasUsableRemnant_ShouldRespectTheMaterialThreshold() {
        Pattern2<Flt64> plain = PatternOf(
            Region(4.0, 2.0),
            new List<Placement2<Flt64>> { Placement(0.0, 0.0, 3.0, 2.0, "P1") },
            new List<Placement2<Flt64>>());
        plain.HasUsableRemnant().Should().BeTrue("2 square metres of remnant is usable");

        Pattern2<Flt64> full = PatternOf(
            Region(4.0, 2.0),
            new List<Placement2<Flt64>> { Placement(0.0, 0.0, 4.0, 2.0, "P1") },
            new List<Placement2<Flt64>>());
        full.HasUsableRemnant().Should().BeFalse("a fully used sheet leaves no remnant");
    }

    /// <summary>取出失败消息 / Extract the failure message.</summary>
    /// <param name="result">校验结果 / The validation result.</param>
    /// <returns>消息 / The message.</returns>
    private static string result(Result<Success, ErrorCode, Error<ErrorCode>> result) =>
        result is Failed<Success, ErrorCode, Error<ErrorCode>> failed ? failed.Error.Message : string.Empty;

    /// <summary>构造图案 / Build a pattern.</summary>
    /// <param name="region">料域 / The region.</param>
    /// <param name="placements">成品件放置 / The finished part placements.</param>
    /// <param name="costars">副产物放置 / The by-product placements.</param>
    /// <returns>图案 / The pattern.</returns>
    private static Pattern2<Flt64> PatternOf(
        Region2<Flt64> region,
        IReadOnlyList<Placement2<Flt64>> placements,
        IReadOnlyList<Placement2<Flt64>> costars) =>
        new(Csp2dIds.PatternId2Of("PAT1"), MaterialOf(), region, placements, costars);

    /// <summary>构造带有给定尺寸放置的列表 / Build a list holding one placement of the given size.</summary>
    /// <param name="itemId">产品编号 / The product identifier.</param>
    /// <param name="width">宽度 / The width.</param>
    /// <param name="height">高度 / The height.</param>
    /// <returns>放置列表 / The placement list.</returns>
    private static IReadOnlyList<Placement2<Flt64>> Placements(
        string itemId, Quantity<Flt64> width, Quantity<Flt64> height) =>
        new List<Placement2<Flt64>> { new(new RectanglePackingShape2<Flt64>(width, height),
            new QuantityPoint2<Flt64>(Metres(0.0), Metres(0.0)), false, itemId) };

    /// <summary>构造放置 / Build a placement.</summary>
    /// <param name="x">左下角 x / The lower-left x.</param>
    /// <param name="y">左下角 y / The lower-left y.</param>
    /// <param name="width">宽度 / The width.</param>
    /// <param name="height">高度 / The height.</param>
    /// <param name="itemId">产品编号，空为余料 / The product identifier; null for a remnant.</param>
    /// <returns>放置 / The placement.</returns>
    private static Placement2<Flt64> Placement(double x, double y, double width, double height, string? itemId) =>
        new(new RectanglePackingShape2<Flt64>(Metres(width), Metres(height)),
            new QuantityPoint2<Flt64>(Metres(x), Metres(y)), false, itemId);

    /// <summary>构造物料 / Build a material.</summary>
    /// <returns>物料 / The material.</returns>
    private static Material2<Flt64> MaterialOf() =>
        new(Csp2dIds.MaterialId2Of("MAT1"), "steel", Metres(4.0), Metres(2.0));

    /// <summary>以原点构造区域 / Build a region anchored at the origin.</summary>
    /// <param name="width">宽度 / The width.</param>
    /// <param name="height">高度 / The height.</param>
    /// <returns>区域 / The region.</returns>
    private static Region2<Flt64> Region(double width, double height) =>
        Region2Factory.Region2Of(Metres(0.0), Metres(0.0), Metres(width), Metres(height));

    /// <summary>构造千克量 / Build a mass in kilograms.</summary>
    /// <param name="value">数值 / The value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> Kilograms(double value) => new(new Flt64(value), SIBaseUnits.Kilogram);

    /// <summary>构造米量 / Build a length in metres.</summary>
    /// <param name="value">数值 / The value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> Metres(double value) => new(new Flt64(value), SIBaseUnits.Meter);
}
