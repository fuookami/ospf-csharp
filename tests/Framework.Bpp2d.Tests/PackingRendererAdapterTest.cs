// 渲染适配测试 / Tests for the packing render adapter.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp2d.Tests;

/// <summary>
/// 渲染适配 / Packing render adapter.
/// </summary>
public sealed class PackingRendererAdapterTest {
    /// <summary>每块板材产出一个装载方案 / Each sheet becomes one loading plan.</summary>
    [Fact]
    public void ToSchema_ShouldProduceOnePlanPerSheet() {
        RenderSchema2Dto schema = Schema(PackingResultOf(
            new PackedSheet("s1", Sheet(10, 10), Items: new[] { Packed(0, 0, 4, 4, "a") }),
            new PackedSheet("s2", Sheet(5, 5), Items: new[] { Packed(0, 0, 2, 2, "b") })));

        schema.Plans.Should().HaveCount(2);
        schema.Plans.Select(plan => plan.Name).Should().Equal("s1", "s2");
    }

    /// <summary>
    /// **圆形件必须额外输出半径与直径**，而矩形件两者为空：渲染器靠这两个值画圆，缺失就只能画方。
    /// **A circle must carry radius and diameter** while a rectangle leaves both empty: the renderer draws a circle
    /// from those values and can only draw a box without them.
    /// </summary>
    [Fact]
    public void ToSchema_ShouldCarryCircleRadiusAndDiameter() {
        var sheet = new PackedSheet("s", Sheet(10, 10), Items: new[] {
            new PackedItem(
                new Placement2<FltX>(
                    new CirclePackingShape2<FltX>(Meters(2)),
                    // 放在 (5,5)：外接方形 [5,9]² 与下方矩形 [0,2]² 不相交，否则守卫会（正确地）拒绝。
                    // Placed at (5,5): the enclosing square [5,9]² does not touch the rectangle [0,2]² below,
                    // which the guard would (correctly) reject.
                    new QuantityPoint2<FltX>(Meters(5), Meters(5))),
                1,
                "c"),
            Packed(0, 0, 2, 2, "r"),
        });

        RenderItem2Dto circle = Schema(PackingResultOf(sheet)).Plans[0].Items[0];
        RenderItem2Dto rectangle = Schema(PackingResultOf(sheet)).Plans[0].Items[1];

        circle.ShapeType.Should().Be(RenderShape2TypeDto.Circle);
        circle.Radius.Should().BeApproximately(2.0, 1e-9);
        circle.Diameter.Should().BeApproximately(4.0, 1e-9);
        rectangle.ShapeType.Should().Be(RenderShape2TypeDto.Rectangle);
        rectangle.Radius.Should().BeNull();
        rectangle.Diameter.Should().BeNull();
    }

    /// <summary>
    /// **`actualArea` 是实际占用面积（圆为 πr²），`boundingArea` 是外接矩形面积**——两者对圆形件不同，
    /// 渲染器若混用会把圆形件画得比实际占位更大或更小。
    /// **`actualArea` is the actual occupied area (πr² for circles) while `boundingArea` is the enclosing rectangle**;
    /// they differ for circles, and mixing them makes the circle render larger or smaller than it occupies.
    /// </summary>
    [Fact]
    public void ToSchema_ShouldDistinguishActualAndBoundingArea() {
        var sheet = new PackedSheet("s", Sheet(10, 10), Items: new[] {
            new PackedItem(
                new Placement2<FltX>(
                    new CirclePackingShape2<FltX>(Meters(1)),
                    new QuantityPoint2<FltX>(Meters(0), Meters(0))),
                1),
        });

        RenderItem2Dto item = Schema(PackingResultOf(sheet)).Plans[0].Items[0];

        item.ActualArea.Should().BeApproximately(System.Math.PI, 1e-6);
        item.BoundingArea.Should().BeApproximately(4.0, 1e-9);
    }

    /// <summary>装载率与板材尺寸进入 DTO / The loading rate and sheet size reach the DTO.</summary>
    [Fact]
    public void ToSchema_ShouldCarrySheetMetrics() {
        RenderLoadingPlan2Dto plan = Schema(PackingResultOf(
            new PackedSheet(
                "s",
                Sheet(10, 10),
                Items: new[] { Packed(0, 0, 5, 5, "a") },
                TypeCode: new SheetTypeId("STEEL-2")))).Plans[0];

        plan.Sheet.TypeCode.Should().Be("STEEL-2");
        plan.Sheet.Width.Should().BeApproximately(10.0, 1e-9);
        plan.Sheet.Area.Should().BeApproximately(100.0, 1e-9);
        plan.Sheet.LoadingRate.Should().BeApproximately(0.25, 1e-9);
    }

    /// <summary>物品无编号时按板材名与序号生成名称 / An item without an id is named from sheet and index.</summary>
    [Fact]
    public void ToSchema_ShouldFallbackItemName() {
        RenderItem2Dto item = Schema(PackingResultOf(
            new PackedSheet("s1", Sheet(10, 10), Items: new[] { Packed(0, 0, 2, 2, null) })))
            .Plans[0].Items[0];

        item.Name.Should().Be("s1-item-1");
    }

    /// <summary>汇总包含板材数、物品数、物料数与附加信息 / The summary carries counts and caller info.</summary>
    [Fact]
    public void ToSchema_ShouldSummarise() {
        var result = new PackingResult(
            new PackingAggregation(new[] {
                new PackedSheet("s1", Sheet(10, 10), Items: new[] { Packed(0, 0, 2, 2, "a") }),
            }),
            MaterialSummary: new[] {
                new MaterialSummary(new MaterialKey(new MaterialNo("M-1"), MaterialType.RawMaterial), 3),
            },
            Info: new Dictionary<string, string> { ["job"] = "J-9" });

        IReadOnlyDictionary<string, string> summary = Schema(result).Summary!;

        summary["sheet_count"].Should().Be("1");
        summary["item_count"].Should().Be("1");
        summary["material_count"].Should().Be("1");
        summary["material:M-1-RawMaterial"].Should().Be("3");
        summary["job"].Should().Be("J-9");
    }

    /// <summary>
    /// 调用方附加信息**最后合并并可覆盖统计键**：调用方比框架更清楚本次渲染要强调什么。
    /// Caller info is merged **last and may override statistic keys**: the caller knows better what to emphasise.
    /// </summary>
    [Fact]
    public void ToSchema_ShouldLetCallerOverrideSummary() {
        var result = new PackingResult(
            new PackingAggregation(new[] {
                new PackedSheet("s1", Sheet(10, 10), Items: new[] { Packed(0, 0, 2, 2, "a") }),
            }),
            Info: new Dictionary<string, string> { ["item_count"] = "999" });

        Schema(result).Summary!["item_count"].Should().Be("999");
    }

    /// <summary>
    /// **几何不自洽的装箱结果必须拒绝渲染**，而不是画出一张看着正常、实际与几何不符的图。
    /// **A geometrically inconsistent result must be rejected** rather than rendered into a plausible picture that
    /// contradicts the geometry.
    /// </summary>
    [Fact]
    public void ToSchema_ShouldRejectInvalidGeometry() {
        var sheet = new PackedSheet("s", Sheet(10, 10), Items: new[] { Packed(8, 8, 5, 5, "a") });

        Result<RenderSchema2Dto, ErrorCode, Error<ErrorCode>> result =
            PackingRendererAdapter.ToSchema(PackingResultOf(sheet));

        result.IsOk.Should().BeFalse();
        ((Failed<RenderSchema2Dto, ErrorCode, Error<ErrorCode>>)result).Error.Message
            .Should().Contain("type=outside_sheet");
    }

    /// <summary>装载顺序与旋转标记进入 DTO / Loading order and rotation reach the DTO.</summary>
    [Fact]
    public void ToSchema_ShouldCarryOrderAndRotation() {
        var sheet = new PackedSheet("s", Sheet(10, 10), Items: new[] {
            new PackedItem(
                new Placement2<FltX>(
                    new RectanglePackingShape2<FltX>(Meters(2), Meters(5)),
                    new QuantityPoint2<FltX>(Meters(0), Meters(0)),
                    Rotated: true,
                    ItemId: "r"),
                7,
                "r"),
        });

        RenderItem2Dto item = Schema(PackingResultOf(sheet)).Plans[0].Items[0];

        item.Rotated.Should().BeTrue();
        item.LoadingOrder.Should().Be(7L);
        item.Info!["sheet"].Should().Be("s");
    }

    /// <summary>转换渲染方案 / Convert to the render schema.</summary>
    private static RenderSchema2Dto Schema(PackingResult result) =>
        PackingRendererAdapter.ToSchema(result).Value;

    /// <summary>构造仅含聚合的装箱结果 / Build a result carrying only the aggregation.</summary>
    private static PackingResult PackingResultOf(params PackedSheet[] sheets) =>
        new(new PackingAggregation(sheets));

    /// <summary>构造板材 / Build a sheet.</summary>
    private static Sheet2Shape<FltX> Sheet(double w, double h) => new(Meters(w), Meters(h));

    /// <summary>构造已装箱物品 / Build a packed item.</summary>
    private static PackedItem Packed(double x, double y, double w, double h, string? itemId) => new(
        new Placement2<FltX>(
            new RectanglePackingShape2<FltX>(Meters(w), Meters(h)),
            new QuantityPoint2<FltX>(Meters(x), Meters(y)),
            false,
            itemId),
        1,
        itemId);

    /// <summary>构造以米为单位的量 / Build a quantity in metres.</summary>
    private static Quantity<FltX> Meters(double value) => new(new FltX(value), SIBaseUnits.Meter);
}
