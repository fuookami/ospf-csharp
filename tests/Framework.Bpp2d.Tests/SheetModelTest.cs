// 板材与层模型测试 / Tests for the sheet and layer model.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp2d.Tests;

/// <summary>
/// 板材与层模型 / Sheet and layer model.
/// </summary>
public sealed class SheetModelTest {
    /// <summary>板材面积是宽高之积 / A sheet type's area is width times height.</summary>
    [Fact]
    public void SheetType_ShouldComputeArea() =>
        SheetTypeOf(10, 10).Area.Value.ToFlt64().ToDouble().Should().BeApproximately(100.0, 1e-9);

    /// <summary>
    /// **相等性必须包含类型编码**：同尺寸不同编码是两种板材，不带编码会让多板材类型被错误去重。
    /// **Equality must include the type code**: equal sizes with different codes are different sheet types, and
 /// omitting the implementation would wrongly deduplicate across sheet types.
    /// </summary>
    [Fact]
    public void SheetType_ShouldDistinguishTypeCode() {
        SheetTypeOf(10, 10, "A").Equals(SheetTypeOf(10, 10, "B")).Should().BeFalse();
        SheetTypeOf(10, 10, "A").Equals(SheetTypeOf(10, 10, "A")).Should().BeTrue();
    }

    /// <summary>主板材标记参与相等性比较 / The main-sheet flag participates in equality.</summary>
    [Fact]
    public void SheetType_ShouldDistinguishMainFlag() =>
        SheetTypeOf(10, 10, "A", main: true).Equals(SheetTypeOf(10, 10, "A")).Should().BeFalse();

    /// <summary>`New` 只覆盖指定属性 / `New` overrides only the given property.</summary>
    [Fact]
    public void SheetType_New_ShouldOverrideOnlyGiven() {
        SheetType<FltX> copy = SheetTypeOf(10, 10, "A").New(width: Meters(20));

        copy.Width.Value.ToFlt64().ToDouble().Should().BeApproximately(20.0, 1e-9);
        copy.Height.Value.ToFlt64().ToDouble().Should().BeApproximately(10.0, 1e-9);
        copy.TypeCode.Value.Should().Be("A");
    }

    /// <summary>层件数为放置个数 / A layer's amount is its placement count.</summary>
    [Fact]
    public void Layer_ShouldCountUnits() =>
        LayerOf(new[] { Placed(0, 0, 2, 2, "a"), Placed(3, 0, 2, 2, "b") }).Amount.Should().Be(2UL);

    /// <summary>层占用面积为各放置实际面积之和 / A layer's area sums the actual placement areas.</summary>
    [Fact]
    public void Layer_ShouldSumArea() =>
        LayerOf(new[] { Placed(0, 0, 2, 3, "a"), Placed(3, 0, 2, 2, "b") })
            .Area.Value.ToFlt64().ToDouble().Should().BeApproximately(10.0, 1e-9);

    /// <summary>层利用率为占用面积比外接面积 / Utilisation is occupied over bounding area.</summary>
    [Fact]
    public void Layer_ShouldComputeUtilization() =>
        LayerOf(new[] { Placed(0, 0, 4, 5, "a") })
            .Utilization().ToFlt64().ToDouble().Should().BeApproximately(1.0, 1e-9);

    /// <summary>退化外接形状时利用率为零，而不是除零 / A degenerate bounding shape yields zero, not division.</summary>
    [Fact]
    public void Layer_ShouldReturnZeroUtilizationForDegenerateShape() {
        var layer = new Layer<FltX>(
            0, null, null, new Sheet2Shape<FltX>(Meters(0), Meters(0)),
            new[] { Placed(0, 0, 0, 0, "a") });

        layer.Utilization().ToFlt64().ToDouble().Should().BeApproximately(0.0, 1e-9);
    }

    /// <summary>件数映射只统计有编号的放置 / The item map counts only placements carrying an id.</summary>
    [Fact]
    public void Layer_ShouldGroupItemAmounts() {
        IReadOnlyDictionary<ItemId, ulong> amounts = LayerOf(new[] {
            Placed(0, 0, 2, 2, "a"),
            Placed(3, 0, 2, 2, "a"),
            Placed(6, 0, 2, 2, null),
        }).ItemAmounts;

        amounts.Should().HaveCount(1);
        amounts[new ItemId("a")].Should().Be(2UL);
    }

    /// <summary>形状计数区分矩形与圆形 / Shape counts separate rectangles from circles.</summary>
    [Fact]
    public void Layer_ShouldCountShapes() {
        var layer = new Layer<FltX>(
            0, null, null, new Sheet2Shape<FltX>(Meters(10), Meters(10)),
            new[] {
                Placed(0, 0, 2, 2, "r"),
                new Placement2<FltX>(
                    new CirclePackingShape2<FltX>(Meters(1)),
                    new QuantityPoint2<FltX>(Meters(5), Meters(5)),
                    false,
                    "c"),
            });

        layer.ShapeCounts[PackingShape2Type.Rectangle].Should().Be(1UL);
        layer.ShapeCounts[PackingShape2Type.Circle].Should().Be(1UL);
    }

    /// <summary>
    /// **层的相等性包含板材类型编码**：同一排布放在不同板材上是不同的列，不带编码会把它们错误去重。
    /// **Layer equality includes the sheet type code**: the same layout on different sheets is a different column, and
 /// leaving the implementation out deduplicates them incorrectly.
    /// </summary>
    [Fact]
    public void Layer_Equality_ShouldIncludeSheetType() {
        Placement2<FltX>[] units = { Placed(0, 0, 2, 2, "a") };

        Layer<FltX> onA = LayerWithSheet(units, SheetTypeOf(10, 10, "A"));
        Layer<FltX> onB = LayerWithSheet(units, SheetTypeOf(10, 10, "B"));
        Layer<FltX> onA2 = LayerWithSheet(units, SheetTypeOf(10, 10, "A"));

        onA.Equals(onB).Should().BeFalse();
        onA.Equals(onA2).Should().BeTrue();
    }

    /// <summary>板材层组合的件数是各层之和 / A sheet layer's amount sums its layers.</summary>
    [Fact]
    public void SheetLayer_ShouldSumAmounts() =>
        SheetLayerOf().Amount.Should().Be(3UL);

    /// <summary>
    /// 板材层装载率为占用面积比板材面积：占用 2×2 + 2×2 + 3×1 = 11，板材 10×10 = 100，故 0.11。
    /// Loading rate is occupied over sheet area: 2×2 + 2×2 + 3×1 = 11 occupied out of 10×10 = 100, hence 0.11.
    /// </summary>
    [Fact]
    public void SheetLayer_ShouldComputeLoadingRate() =>
        SheetLayerOf().LoadingRate.ToFlt64().ToDouble().Should().BeApproximately(0.11, 1e-9);

    /// <summary>板材层件数映射合并各层 / The sheet layer's item map merges its layers.</summary>
    [Fact]
    public void SheetLayer_ShouldMergeItemAmounts() {
        IReadOnlyDictionary<ItemId, ulong> amounts = SheetLayerOf().ItemAmounts;

        amounts[new ItemId("a")].Should().Be(2UL);
        amounts[new ItemId("b")].Should().Be(1UL);
    }

    /// <summary>板材实例能展开出全部放置 / A sheet flattens every placement.</summary>
    [Fact]
    public void Sheet_ShouldFlattenPlacements() =>
        SheetOf().Placements.Should().HaveCount(3);

    /// <summary>板材件数映射合并全部层组合 / A sheet's item map merges every sheet layer.</summary>
    [Fact]
    public void Sheet_ShouldMergeItemAmounts() {
        IReadOnlyDictionary<ItemId, ulong> amounts = SheetOf().ItemAmounts;

        amounts[new ItemId("a")].Should().Be(2UL);
        amounts[new ItemId("b")].Should().Be(1UL);
    }

    /// <summary>空板材的层与放置都是空集合而非 null / An empty sheet yields empty collections, not null.</summary>
    [Fact]
    public void Sheet_ShouldDefaultToEmpty() {
        var sheet = new Sheet<FltX>(SheetTypeOf(10, 10));

        sheet.SheetLayers.Should().BeEmpty();
        sheet.Placements.Should().BeEmpty();
        sheet.ItemAmounts.Should().BeEmpty();
    }


    /// <summary>
    /// **接 `Sheet` 的正规入口**：装箱与余料分析都能直接吃板材实例，不必调用方先手动展开成放置列表。
    /// **The proper `Sheet` entry point**: packing and remnant analysis both accept a sheet instance directly, so the
    /// caller need not flatten it by hand first.
    /// </summary>
    [Fact]
    public void Sheet_ShouldFeedPackerAndAnalyzer() {
        Sheet<FltX> sheet = SheetOf();

        Utils.Functional.Result<PackingResult, Utils.Error.ErrorCode, Utils.Error.Error<Utils.Error.ErrorCode>> packed =
            new Packer().InvokeSheets(new[] { sheet });
        packed.IsOk.Should().BeTrue();
        packed.Value.Aggregation.Bins[0].PackedItems.Should().HaveCount(3);
        packed.Value.Aggregation.Bins[0].TypeCode!.Value.Should().Be("T");

        IReadOnlyList<Remnant2> remnants = FreeSpaceAnalyzer.AnalyzeSheets(new[] { sheet });
        remnants.Should().NotBeEmpty("板材未排满仍有余料 / the sheet is not full");
    }

    /// <summary>接 `Sheet` 的装箱入口同样执行几何校验 / The sheet entry point validates geometry too.</summary>
    [Fact]
    public void Sheet_PackerEntry_ShouldRejectInvalidGeometry() {
        SheetType<FltX> type = SheetTypeOf(10, 10);
        var oversize = new Sheet<FltX>(type, new[] {
            new SheetLayer<FltX>(type, new Sheet2Shape<FltX>(Meters(10), Meters(10)), new[] {
                new Layer<FltX>(0, null, type, new Sheet2Shape<FltX>(Meters(8), Meters(8)), new[] {
                    Placed(8, 8, 5, 5, "a"),
                }),
            }),
        });

        new Packer().InvokeSheets(new[] { oversize }).IsOk.Should().BeFalse();
    }

    /// <summary>构造板材类型 / Build a sheet type.</summary>
    private static SheetType<FltX> SheetTypeOf(
        double w, double h, string code = "T", bool main = false) =>
        new(Meters(w), Meters(h), Meters(0), new SheetTypeId(code), main);

    /// <summary>构造层 / Build a layer.</summary>
    private static Layer<FltX> LayerOf(IReadOnlyList<Placement2<FltX>> units) =>
        new(0, null, SheetTypeOf(10, 10), new Sheet2Shape<FltX>(Meters(4), Meters(5)), units);

    /// <summary>构造带指定板材类型的层 / Build a layer on the given sheet type.</summary>
    private static Layer<FltX> LayerWithSheet(
        IReadOnlyList<Placement2<FltX>> units, SheetType<FltX> sheet) =>
        new(0, null, sheet, new Sheet2Shape<FltX>(Meters(10), Meters(10)), units);

    /// <summary>构造板材层组合 / Build a sheet layer.</summary>
    private static SheetLayer<FltX> SheetLayerOf() {
        SheetType<FltX> type = SheetTypeOf(10, 10);
        return new SheetLayer<FltX>(
            type,
            new Sheet2Shape<FltX>(Meters(10), Meters(10)),
            new[] {
                new Layer<FltX>(0, null, type, new Sheet2Shape<FltX>(Meters(4), Meters(5)), new[] {
                    Placed(0, 0, 2, 2, "a"),
                    Placed(3, 0, 2, 2, "a"),
                }),
                new Layer<FltX>(0, null, type, new Sheet2Shape<FltX>(Meters(3), Meters(1)), new[] {
                    Placed(0, 5, 3, 1, "b"),
                }),
            });
    }

    /// <summary>构造板材实例 / Build a sheet.</summary>
    private static Sheet<FltX> SheetOf() =>
        new(SheetTypeOf(10, 10), new[] { SheetLayerOf() });

    /// <summary>构造放置 / Build a placement.</summary>
    private static Placement2<FltX> Placed(
        double x, double y, double w, double h, string? itemId) =>
        new(
            new RectanglePackingShape2<FltX>(Meters(w), Meters(h)),
            new QuantityPoint2<FltX>(Meters(x), Meters(y)),
            false,
            itemId);

    /// <summary>构造以米为单位的量 / Build a quantity in metres.</summary>
    private static Quantity<FltX> Meters(double value) => new(new FltX(value), SIBaseUnits.Meter);
}
