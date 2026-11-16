// 装箱分析器的判据 / Criteria for the packing analyser.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.ColumnGeneration;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp2d.Tests.LayerAssignment;

/// <summary>
/// **装箱分析器** / The packing analyser.
/// </summary>
/// <remarks>
/// 判据的重点：**实际面积与外接面积是两个数**（圆形件上差别最大）、**装载率是各板材的平均**（不是总面积比）、
/// **剩余面积钳到零**（重叠摆放不返回负数）。
///
/// The criteria focus on **actual and bounding area being two numbers** (the gap is largest for circles), **the loading rate being a per-sheet average** rather than a ratio of totals, and
/// **the remaining area being clamped at zero** so overlapping placements never report a negative number.
/// </remarks>
public class ColumnGenerationPackingAnalyzerTests {
    private static Quantity<FltX> Q(double value) => new(new FltX(value), LengthUnits.Millimeter);

    private static ColumnGenerationResult EmptyResult() =>
        new(Array.Empty<Layer<FltX>>(), Array.Empty<Sheet<FltX>>(), 0, false, false, 0, false,
            Array.Empty<FltX?>(), null, TimeSpan.Zero);

    private static PackedItem Rect(double width, double height, string id) =>
        new(new Placement2<FltX>(
            Shape: new RectanglePackingShape2<FltX>(Q(width), Q(height)),
            Position: new QuantityPoint2<FltX>(Q(0.0), Q(0.0)),
            Rotated: false,
            ItemId: id), 0UL, id);

    private static PackedItem Circle(double radius, string id) =>
        new(new Placement2<FltX>(
            Shape: new CirclePackingShape2<FltX>(Q(radius)),
            Position: new QuantityPoint2<FltX>(Q(0.0), Q(0.0)),
            Rotated: false,
            ItemId: id), 0UL, id);

    private static PackedSheet Sheet(double width, double height, params PackedItem[] items) =>
        new("S", new Sheet2Shape<FltX>(Q(width), Q(height)), Items: items);

    private static PackingResult Packing(params PackedSheet[] sheets) =>
        new(new PackingAggregation(sheets));

    /// <summary>
    /// **实际面积按 πr²、外接面积按 2r×2r，两个数都留** / Actual area counts πr² and bounding area 2r×2r, and both numbers survive.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 一个半径 10 的圆 → 实际面积 ≈ 314.16、外接面积 400，**两者不等**。
    /// 2. **只留一个数的实现会在这里露馅**：断言两个值都必须对。
    /// 3. 矩形件数 / 圆形件数分别计数，不合并。
    /// 4. 板材面积取板材形状的面积（100×100 = 10000），不是件数乘某常数。
    ///
    /// Four paired points: a circle of radius 10 gives an actual area near 314.16 and a bounding area of 400, so **the two differ**; **an implementation keeping only one number fails here**,
    /// since both are asserted; rectangle and circle counts are tracked separately rather than merged; and the sheet area comes from the sheet shape (100×100 = 10000), not from a count times
    /// some constant.
    /// </remarks>
    [Fact]
    public void Analyze_ShouldKeepActualAndBoundingAreaApart() {
        PackingResult packing = Packing(Sheet(100.0, 100.0, Circle(10.0, "c1"), Rect(20.0, 10.0, "r1")));
        ColumnGenerationPackingSnapshot snapshot =
            new ColumnGenerationPackingAnalyzer().Analyze(EmptyResult(), packing);

        snapshot.SheetCount.Should().Be(1);
        snapshot.ItemCount.Should().Be(2);
        snapshot.CircleCount.Should().Be(1);
        snapshot.RectangleCount.Should().Be(1);
        snapshot.SheetArea.ToFlt64().ToDouble().Should().BeApproximately(10000.0, 1e-6);

        snapshot.ActualArea.ToFlt64().ToDouble()
            .Should().BeApproximately(global::System.Math.PI * 100.0 + 200.0, 0.01, "circles count pi r squared");
        snapshot.BoundingArea.ToFlt64().ToDouble()
            .Should().BeApproximately(400.0 + 200.0, 1e-6, "circles count their enclosing square here");
        snapshot.BoundingArea.ToFlt64().ToDouble()
            .Should().NotBe(snapshot.ActualArea.ToFlt64().ToDouble(), "the two numbers must differ");
    }

    /// <summary>
    /// **装载率是各板材的平均，不是总面积比** / The loading rate is a per-sheet average, not a ratio of totals.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 两块大小不同的板材 → **平均装载率不等于总面积比**（这是判据的关键：两块板材装载率不同时两者才分得开）。
    /// 2. 剩余面积 = 板材面积 − 实际面积。
    /// 3. 面积利用率 = 实际面积 / 板材面积。
    /// 4. **重叠摆放时剩余面积钳到零**，不返回负数。
    ///
    /// Four paired points: two differently sized sheets make **the average loading rate differ from the ratio of totals**, which is what separates the two definitions; the remaining area is
    /// sheet area minus actual area; the area utilisation is actual over sheet area; and **an overfilled sheet clamps the remaining area to zero** instead of reporting a negative number.
    /// </remarks>
    [Fact]
    public void Analyze_ShouldAverageLoadingRateAcrossSheets() {
        PackingResult packing = Packing(
            Sheet(100.0, 100.0, Rect(50.0, 100.0, "a")),
            Sheet(200.0, 100.0, Rect(50.0, 100.0, "b")));
        ColumnGenerationPackingSnapshot snapshot =
            new ColumnGenerationPackingAnalyzer().Analyze(EmptyResult(), packing);

        double average = (0.5 + 0.25) / 2.0;
        snapshot.LoadingRate.ToFlt64().ToDouble().Should().BeApproximately(average, 1e-9);
        double ratioOfTotals = 10000.0 / 30000.0;
        snapshot.LoadingRate.ToFlt64().ToDouble()
            .Should().NotBeApproximately(ratioOfTotals, 1e-9, "an average of rates is not a ratio of totals");

        snapshot.RemainingArea.ToFlt64().ToDouble().Should().BeApproximately(20000.0, 1e-6);
        snapshot.AreaUtilization.ToFlt64().ToDouble().Should().BeApproximately(1.0 / 3.0, 1e-9);

        PackingResult overfilled = Packing(Sheet(10.0, 10.0, Rect(20.0, 20.0, "big")));
        new ColumnGenerationPackingAnalyzer().Analyze(EmptyResult(), overfilled)
            .RemainingArea.ToFlt64().ToDouble()
            .Should().Be(0.0, "an overfilled sheet must not report a negative remaining area");
    }

    /// <summary>
    /// **空装箱不除零，键名照做** / An empty packing divides by nothing, and the key names match the code.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 空装箱 → 全部为零，**不抛除零异常**。
    /// 2. `ToInfo` 的**键名是蛇形**（`sheet_count` 等）：下游按契约读，改成 PascalCase 会静默丢字段。
    /// 3. **数值文本不用区域设置的小数逗号**（`100` 本身没有小数点，所以断言"不含逗号"而不是"含小数点"）。
    ///
    /// Three paired points: an empty packing yields zeros **without a divide-by-zero**; `ToInfo`'s **key names are snake case**, read downstream as a contract, so PascalCase would silently
    /// drop fields; and **numbers use a decimal point** rather than a locale-dependent comma.
    /// </remarks>
    [Fact]
    public void Analyze_ShouldHandleEmptyPackingAndKeepTheKeyNames() {
        PackingResult empty = Packing();
        ColumnGenerationPackingSnapshot snapshot =
            new ColumnGenerationPackingAnalyzer().Analyze(EmptyResult(), empty);

        snapshot.SheetCount.Should().Be(0);
        snapshot.ItemCount.Should().Be(0);
        snapshot.AreaUtilization.ToFlt64().ToDouble().Should().Be(0.0);
        snapshot.LoadingRate.ToFlt64().ToDouble().Should().Be(0.0);
        snapshot.RemainingArea.ToFlt64().ToDouble().Should().Be(0.0);

        PackingResult withRemnant = new(
            new PackingAggregation(new[] { Sheet(100.0, 100.0, Rect(10.0, 10.0, "a")) }),
            Remnants: new[] {
                new Remnant2("S", Box2Geometry<FltX>.Of(Q(0.0), Q(0.0), Q(5.0), Q(5.0)), Q(25.0)),
            });
        ColumnGenerationPackingSnapshot withArea =
            new ColumnGenerationPackingAnalyzer().Analyze(EmptyResult(), withRemnant);
        withArea.RemnantCount.Should().Be(1);
        withArea.RemnantAreaValue.ToFlt64().ToDouble().Should().BeApproximately(25.0, 1e-9);

        IReadOnlyDictionary<string, string> info = withArea.ToInfo();
        info.Keys.Should().BeEquivalentTo(new[] {
            "sheet_count", "item_count", "actual_area", "bounding_area", "sheet_area", "remaining_area",
            "area_utilization", "loading_rate", "rectangle_count", "circle_count", "remnant_count", "remnant_area",
        });
        info["sheet_count"].Should().Be("1");
        info["actual_area"].Should().NotContain(",", "the decimal separator must not follow the locale");
    }
}
