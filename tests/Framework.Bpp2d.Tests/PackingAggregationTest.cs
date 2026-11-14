// 装箱聚合测试 / Tests for packing aggregation.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp2d.Tests;

/// <summary>
/// 装箱聚合 / Packing aggregation.
/// </summary>
public sealed class PackingAggregationTest {
    /// <summary>无物品的板材占用面积为零 / An empty sheet has zero occupied area.</summary>
    [Fact]
    public void EmptySheet_ShouldHaveZeroActualArea() {
        var sheet = new PackedSheet("s", Sheet(10, 10));

        D(sheet.ActualArea).Should().BeApproximately(0.0, 1e-9);
        sheet.LoadingRate.ToFlt64().ToDouble().Should().BeApproximately(0.0, 1e-9);
    }

    /// <summary>占用面积是各放置实际面积之和 / Occupied area sums the actual placement areas.</summary>
    [Fact]
    public void ActualArea_ShouldSumPlacements() {
        var sheet = new PackedSheet("s", Sheet(10, 10), Items: new[] {
            Packed(0, 0, 2, 3),
            Packed(4, 0, 2, 2),
        });

        D(sheet.ActualArea).Should().BeApproximately(10.0, 1e-9);
    }

    /// <summary>装载率是占用面积与板材面积之比 / The loading rate is occupied over sheet area.</summary>
    [Fact]
    public void LoadingRate_ShouldBeOccupiedOverSheetArea() {
        var sheet = new PackedSheet("s", Sheet(10, 10), Items: new[] { Packed(0, 0, 5, 5) });

        sheet.LoadingRate.ToFlt64().ToDouble().Should().BeApproximately(0.25, 1e-9);
    }

    /// <summary>
    /// **圆形件按 πr² 计入，不按外接矩形**：用外接面积会把装载率算高，让还能再排一件的板材看起来已满。
    /// **Circles count by πr², not their enclosing rectangle**: the enclosing area inflates the loading rate and makes
    /// a sheet that could still take one more item look full.
    /// </summary>
    [Fact]
    public void ActualArea_ShouldUseCircleAreaNotBoundingBox() {
        var sheet = new PackedSheet("s", Sheet(10, 10), Items: new[] {
            new PackedItem(
                new Placement2<FltX>(
                    new CirclePackingShape2<FltX>(Meters(1)),
                    new QuantityPoint2<FltX>(Meters(0), Meters(0))),
                1),
        });

        D(sheet.ActualArea).Should().BeApproximately(System.Math.PI, 1e-6);
        D(sheet.ActualArea).Should().NotBeApproximately(4.0, 0.1);
    }

    /// <summary>板材面积非正时装载率为零，而不是除零 / A non-positive sheet area yields a zero rate, not division.</summary>
    [Fact]
    public void LoadingRate_ShouldBeZeroForDegenerateSheet() {
        var sheet = new PackedSheet(
            "s",
            new Sheet2Shape<FltX>(Meters(0), Meters(10)),
            Items: new[] { Packed(0, 0, 1, 1) });

        sheet.LoadingRate.ToFlt64().ToDouble().Should().BeApproximately(0.0, 1e-9);
    }

    /// <summary>可选集合缺省时为空集合而不是 null / Optional collections default to empty, not null.</summary>
    [Fact]
    public void OptionalCollections_ShouldDefaultToEmpty() {
        var sheet = new PackedSheet("s", Sheet(10, 10));
        var result = new PackingResult(new PackingAggregation(new[] { sheet }));

        sheet.PackedItems.Should().BeEmpty();
        result.MaterialSummaries.Should().BeEmpty();
        result.RemnantList.Should().BeEmpty();
        result.ExtraInfo.Should().BeEmpty();
    }

    /// <summary>聚合保留板材顺序 / The aggregation keeps sheet order.</summary>
    [Fact]
    public void Aggregation_ShouldKeepSheetOrder() {
        var aggregation = new PackingAggregation(new[] {
            new PackedSheet("a", Sheet(10, 10)),
            new PackedSheet("b", Sheet(5, 5)),
        });

        aggregation.Bins.Should().HaveCount(2);
        aggregation.Bins[0].Name.Should().Be("a");
        aggregation.Bins[1].Name.Should().Be("b");
    }

    /// <summary>物料汇总保留物料编号与件数 / The material summary keeps number and amount.</summary>
    [Fact]
    public void MaterialSummary_ShouldCarryNumberAndAmount() {
        var result = new PackingResult(
            new PackingAggregation(Array.Empty<PackedSheet>()),
            MaterialSummary: new[] { new MaterialSummary(RawKey("M-1"), 42) });

        result.MaterialSummaries.Should().ContainSingle();
        result.MaterialSummaries[0].Material.No.Value.Should().Be("M-1");
        result.MaterialSummaries[0].Amount.Should().Be(42UL);
    }

    /// <summary>批次号可缺省 / The batch number is optional.</summary>
    [Fact]
    public void BatchNo_ShouldBeOptional() =>
        new PackedSheet("s", Sheet(10, 10)).BatchNo.Should().BeNull();

    /// <summary>构造原材料键 / Build a raw-material key.</summary>
    private static MaterialKey RawKey(string no) =>
        new(new MaterialNo(no), MaterialType.RawMaterial);

    /// <summary>构造板材 / Build a sheet.</summary>
    private static Sheet2Shape<FltX> Sheet(double w, double h) => new(Meters(w), Meters(h));

    /// <summary>构造已装箱物品 / Build a packed item.</summary>
    private static PackedItem Packed(double x, double y, double w, double h) => new(
        new Placement2<FltX>(
            new RectanglePackingShape2<FltX>(Meters(w), Meters(h)),
            new QuantityPoint2<FltX>(Meters(x), Meters(y))),
        1);

    /// <summary>取出数值 / Read the number.</summary>
    private static double D(Quantity<FltX> quantity) => quantity.Value.ToFlt64().ToDouble();

    /// <summary>构造以米为单位的量 / Build a quantity in metres.</summary>
    private static Quantity<FltX> Meters(double value) => new(new FltX(value), SIBaseUnits.Meter);
}
