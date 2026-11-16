// 层生成请求与圆形网格生成测试 / Tests for the layer generation request and circle grid generator.

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
/// 层生成请求与圆形网格生成 / Layer generation request and circle grid generator.
/// </summary>
public sealed class LayerGenerationTest {
    /// <summary>无板材类型时参与集为空 / Without any sheet type the participant set is empty.</summary>
    [Fact]
    public void Request_Participants_ShouldBeEmptyWithoutSheet() =>
        new Bpp2dLayerGenerationRequest<FltX>(0).Participants.Should().BeEmpty();

    /// <summary>仅给主板材时参与集退化为它 / With only the primary sheet the participants degrade to it.</summary>
    [Fact]
    public void Request_Participants_ShouldFallBackToPrimary() =>
        new Bpp2dLayerGenerationRequest<FltX>(0, Sheet: SheetTypeOf(10, 10))
            .Participants.Should().ContainSingle();

    /// <summary>给了板材列表时以列表为准，主板材不重复参与 / An explicit list wins and the primary is not re-added.</summary>
    [Fact]
    public void Request_Participants_ShouldPreferExplicitList() {
        IReadOnlyList<SheetType<FltX>> participants = new Bpp2dLayerGenerationRequest<FltX>(
            0,
            Sheet: SheetTypeOf(10, 10, "A"),
            Sheets: new[] { SheetTypeOf(10, 10, "B"), SheetTypeOf(10, 10, "C") }).Participants;

        participants.Should().HaveCount(2);
        participants.Select(entry => entry.TypeCode.Value).Should().Equal("B", "C");
    }

    /// <summary>含圆形件与含矩形件分开判断 / Circle and rectangle presence are judged separately.</summary>
    [Fact]
    public void Request_ShouldDetectShapePresence() {
        var circleOnly = new Bpp2dLayerGenerationRequest<FltX>(
            0, Items: new[] { (CircleItem("c", 2), 1) });
        var rectangleOnly = new Bpp2dLayerGenerationRequest<FltX>(
            0, Items: new[] { (RectItem("r", 2, 2), 1) });

        circleOnly.HasCircleItems().Should().BeTrue();
        circleOnly.HasRectangleItems().Should().BeFalse();
        rectangleOnly.HasRectangleItems().Should().BeTrue();
        rectangleOnly.HasCircleItems().Should().BeFalse();
    }

    /// <summary>
    /// **未设刀缝时返回零间距而不是 null**：调用方总是持有形状，零值一定可构造，让下游少一层判空。
    /// **An unset kerf yields a zero spacing, not null**: callers always hold a shape, so a zero value is always
    /// constructible and downstream needs no null check.
    /// </summary>
    [Fact]
    public void Request_KerfSpacing_ShouldDefaultToZero() {
        Quantity<FltX> spacing = new Bpp2dLayerGenerationRequest<FltX>(0).KerfSpacing(Meters(5));

        spacing.Value.ToFlt64().ToDouble().Should().BeApproximately(0.0, 1e-9);
        spacing.Unit.Should().Be(SIBaseUnits.Meter);
    }

    /// <summary>显式正刀缝被采用 / A positive explicit kerf is used.</summary>
    [Fact]
    public void Request_KerfSpacing_ShouldUseExplicit() =>
        new Bpp2dLayerGenerationRequest<FltX>(0, Kerf: Meters(2))
            .KerfSpacing(Meters(5)).Value.ToFlt64().ToDouble()
            .Should().BeApproximately(2.0, 1e-9);

    /// <summary>非正刀缝视为无刀缝 / A non-positive kerf behaves as no kerf.</summary>
    [Fact]
    public void Request_KerfSpacing_ShouldIgnoreNonPositive() =>
        new Bpp2dLayerGenerationRequest<FltX>(0, Kerf: Meters(0))
            .KerfSpacing(Meters(5)).Value.ToFlt64().ToDouble()
            .Should().BeApproximately(0.0, 1e-9);

    /// <summary>矩形件不产生圆形网格 / A rectangle item yields no circle grid.</summary>
    [Fact]
    public void Generator_ShouldRejectRectangle() =>
        CircleGridGenerator.Invoke(RectItem("r", 2, 2), SheetTypeOf(10, 10))
            .Should().BeEmpty();

    /// <summary>直角网格件数等于列数乘行数 / The square grid count is columns times rows.</summary>
    [Fact]
    public void Generator_ShouldFillSquareGrid() {
        CircleGridLayout<FltX> square = CircleGridGenerator
            .Invoke(CircleItem("c", 2), SheetTypeOf(10, 10))
            .Single(layout => layout.Mode == CircleGridMode.SquareGrid);

        // 10 / 2 = 5 列，5 行。
        // 10 / 2 = 5 columns, 5 rows.
        square.Placements.Should().HaveCount(25);
    }

    /// <summary>
    /// **六角网格比直角网格更密**：这是六角布局存在的唯一理由，若反而更少就该回退到直角网格。
    /// **The hexagonal grid is denser than the square grid**: that is its only reason to exist, and being sparser would
    /// mean falling back to the square grid.
    /// </summary>
    [Fact]
    public void Generator_HexGridShouldBeatSquareGrid() {
        IReadOnlyList<CircleGridLayout<FltX>> layouts =
            CircleGridGenerator.Invoke(CircleItem("c", 2), SheetTypeOf(20, 20));

        int square = layouts.Single(layout => layout.Mode == CircleGridMode.SquareGrid).Placements.Count;
        int hex = layouts.Single(layout => layout.Mode == CircleGridMode.HexGrid).Placements.Count;

        hex.Should().BeGreaterThan(square);
    }

    /// <summary>结果按件数降序，最密的排法在最前 / Results are densest-first.</summary>
    [Fact]
    public void Generator_ShouldSortByCountDescending() {
        IReadOnlyList<CircleGridLayout<FltX>> layouts =
            CircleGridGenerator.Invoke(CircleItem("c", 2), SheetTypeOf(20, 20));

        layouts.Select(layout => layout.Placements.Count).Should().BeInDescendingOrder();
    }

    /// <summary>
    /// **六角行距必须是 0.8667·d 而不是 0.8667**：这里记录过把「26d / 30d」写成无量纲比值的真实缺陷，在 10×8 板材、
    /// 直径 2 的圆形件上相邻圆心距会只有 1.32（小于直径 2），实际产出重叠的圆。这里直接断言相邻行间距。
    ///
    /// **The hexagonal row pitch must be 0.8667·d, not 0.8667**: a real defect was recorded where "26d / 30d"
    /// was written as a dimensionless ratio, leaving adjacent centres only 1.32 apart (below the diameter 2) on a 10×8
    /// sheet with diameter-2 circles, producing actually overlapping circles. This asserts the row spacing directly.
    /// </summary>
    [Fact]
    public void Generator_HexRowPitchShouldScaleByDiameter() {
        CircleGridLayout<FltX> hex = CircleGridGenerator
            .Invoke(CircleItem("c", 2), SheetTypeOf(10, 8))
            .Single(layout => layout.Mode == CircleGridMode.HexGrid);

        double[] distinctRows = hex.Placements
            .Select(placement => placement.Y.Value.ToFlt64().ToDouble())
            .Distinct()
            .OrderBy(value => value)
            .ToArray();
        double rowPitch = distinctRows[1] - distinctRows[0];

        // 26/30 × 直径 2 = 1.7333；若退化成无量纲比值，行距会变成 0.8667，圆必然重叠。
        // 26/30 × diameter 2 = 1.7333; degraded to a dimensionless ratio the pitch would be 0.8667 and circles must
        // overlap.
        rowPitch.Should().BeApproximately(26.0 / 30.0 * 2.0, 1e-6);
        rowPitch.Should().BeGreaterThan(2.0 * 0.8, "行距必须与直径同量级 / the pitch must be on the diameter's scale");
    }

    /// <summary>
    /// **六角网格相邻圆不得重叠**：这是该缺陷真正的后果，用几何判定而非数值猜测来固定。
    /// **Hexagonal neighbours must not overlap**: that is the real consequence of the defect, pinned by geometry rather
    /// than by guessing at numbers.
    /// </summary>
    [Fact]
    public void Generator_HexGridShouldNotOverlap() {
        CircleGridLayout<FltX> hex = CircleGridGenerator
            .Invoke(CircleItem("c", 2), SheetTypeOf(10, 8))
            .Single(layout => layout.Mode == CircleGridMode.HexGrid);

        // 只比较相邻行上的水平邻域，避免 O(n²) 全比较。
        // Compare only horizontal neighbours in adjacent rows, avoiding a full O(n²) sweep.
        var grouped = hex.Placements
            .GroupBy(placement => placement.Y.Value.ToFlt64().ToDouble())
            .OrderBy(group => group.Key)
            .ToList();
        for (int row = 0; row + 1 < grouped.Count; row++) {
            foreach (Placement2<FltX> lower in grouped[row]) {
                foreach (Placement2<FltX> upper in grouped[row + 1]) {
                    double dx = System.Math.Abs(
                        lower.X.Value.ToFlt64().ToDouble() - upper.X.Value.ToFlt64().ToDouble());
                    double dy = System.Math.Abs(
                        lower.Y.Value.ToFlt64().ToDouble() - upper.Y.Value.ToFlt64().ToDouble());
                    System.Math.Sqrt(dx * dx + dy * dy).Should().BeGreaterThanOrEqualTo(
                        2.0 - 1e-9,
                        "圆心距不得小于直径 / centre distance must not fall below the diameter");
                }
            }
        }
    }

    /// <summary>刀缝使节距增大，件数不增加 / A kerf enlarges the pitch and never increases the count.</summary>
    [Fact]
    public void Generator_KerfShouldReduceCount() {
        int without = CircleGridGenerator
            .Invoke(CircleItem("c", 2), SheetTypeOf(20, 20))
            .Single(layout => layout.Mode == CircleGridMode.SquareGrid)
            .Placements.Count;
        int with = CircleGridGenerator
            .Invoke(CircleItem("c", 2), SheetTypeOf(20, 20), Meters(1))
            .Single(layout => layout.Mode == CircleGridMode.SquareGrid)
            .Placements.Count;

        with.Should().BeLessThan(without);
    }

    /// <summary>零直径的圆形件不产生候选 / A zero-diameter circle yields no candidates.</summary>
    [Fact]
    public void Generator_ShouldRejectDegenerateCircle() =>
        CircleGridGenerator.Invoke(CircleItem("c", 0), SheetTypeOf(10, 10)).Should().BeEmpty();

    /// <summary>板材装不下任何一件时返回空 / A sheet too small for one piece yields nothing.</summary>
    [Fact]
    public void Generator_ShouldRejectUndersizedSheet() =>
        CircleGridGenerator.Invoke(CircleItem("c", 5), SheetTypeOf(1, 1)).Should().BeEmpty();

    /// <summary>构造板材类型 / Build a sheet type.</summary>
    private static SheetType<FltX> SheetTypeOf(double w, double h, string code = "T") =>
        new(Meters(w), Meters(h), Meters(0), new SheetTypeId(code));

    /// <summary>
    /// 构造圆形件。入参是**直径**，但 `CirclePackingShape2` 的构造参数是**半径**，因此在此折半——这类单位假设
    /// 写在辅助函数里比散落在每个测试里可靠。
    /// Build a circle item. The argument is a **diameter** while `CirclePackingShape2` takes a **radius**, so it is
    /// halved here: keeping this unit assumption in one helper is safer than scattering it across tests.
    /// </summary>
    private static ActualItem<FltX> CircleItem(string id, double diameter) =>
        new(new ItemId(id), id, new CirclePackingShape2<FltX>(Meters(diameter / 2.0)), true, Meters(0));

    /// <summary>构造矩形件 / Build a rectangle item.</summary>
    private static ActualItem<FltX> RectItem(string id, double w, double h) =>
        new(new ItemId(id), id, new RectanglePackingShape2<FltX>(Meters(w), Meters(h)), true, Meters(0));

    /// <summary>构造以米为单位的量 / Build a quantity in metres.</summary>
    private static Quantity<FltX> Meters(double value) => new(new FltX(value), SIBaseUnits.Meter);
}
