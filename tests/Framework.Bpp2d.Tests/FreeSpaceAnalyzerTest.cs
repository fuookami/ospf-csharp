// 余料分析测试 / Tests for remnant analysis.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using FluentAssertions;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp2d.Tests;

/// <summary>
/// 余料分析 / Remnant analysis.
/// </summary>
public sealed class FreeSpaceAnalyzerTest {
    /// <summary>
    /// 空板材的余料就是整板：把"没有件"报成"没有余料"会让板材永远进不了库存。
    /// An empty sheet yields the whole sheet: reporting "no remnants" for "no items" would keep the sheet out of stock
    /// forever.
    /// </summary>
    [Fact]
    public void EmptySheet_ShouldReportWholeSheet() {
        IReadOnlyList<Remnant2> remnants = FreeSpaceAnalyzer.AnalyzeSheet(
            "s", Sheet(10, 10), Array.Empty<Placement2<FltX>>());

        remnants.Should().ContainSingle();
        D(remnants[0].Area).Should().BeApproximately(100.0, 1e-9);
    }

    /// <summary>余料面积之和等于板材面积（无件时）/ The single remnant covers the sheet.</summary>
    [Fact]
    public void EmptySheet_Remnant_ShouldCoverSheet() {
        Remnant2 remnant = FreeSpaceAnalyzer
            .AnalyzeSheet("s", Sheet(10, 10), Array.Empty<Placement2<FltX>>())[0];

        D(remnant.Rectangle.Width).Should().BeApproximately(10.0, 1e-9);
        D(remnant.Rectangle.Height).Should().BeApproximately(10.0, 1e-9);
    }

    /// <summary>
    /// **余料之间不得重叠**：重叠的余料会让下游把同一块区域当成两块来用，切出冲突的工单。
    /// **Remnants must not overlap**: overlapping remnants would let downstream treat one region as two and cut
    /// conflicting jobs.
    /// </summary>
    [Fact]
    public void Remnants_ShouldBePairwiseDisjoint() {
        IReadOnlyList<Remnant2> remnants = FreeSpaceAnalyzer.AnalyzeSheet(
            "s",
            Sheet(10, 10),
            new[] { Placed(0, 0, 4, 4), Placed(5, 0, 4, 4), Placed(0, 5, 3, 3) });

        // 非空断言：空集会让下面的双循环真空通过，等于什么都没测。
        // Non-vacuity: an empty result would make the double loop below pass vacuously, testing nothing.
        remnants.Should().NotBeEmpty();
        for (int i = 0; i < remnants.Count; i++) {
            for (int j = i + 1; j < remnants.Count; j++) {
                remnants[i].Rectangle.Overlapped(remnants[j].Rectangle).Value
                    .Should().BeFalse($"余料 {i} 与 {j} 不得重叠 / remnants {i} and {j} must not overlap");
            }
        }
    }

    /// <summary>
    /// **余料不得覆盖已放置的件**：报出一块压在件上的"余料"，下游会据此下料并撞上已排好的件。
    /// **No remnant may cover a placed item**: reporting a remnant on top of a placed item would make downstream cut
    /// into an already scheduled piece.
    /// </summary>
    [Fact]
    public void Remnants_ShouldNotCoverPlacements() {
        Placement2<FltX> placed = Placed(2, 2, 4, 4);
        IReadOnlyList<Remnant2> remnants = FreeSpaceAnalyzer.AnalyzeSheet(
            "s", Sheet(10, 10), new[] { placed, Placed(7, 7, 2, 2) });

        remnants.Should().NotBeEmpty("空集会让下面的循环真空通过 / an empty result would loop vacuously");
        foreach (Remnant2 remnant in remnants) {
            remnant.Rectangle.Overlapped(placed.Box()).Value
                .Should().BeFalse("余料不得压在件上 / a remnant must not sit on an item");
        }
    }

    /// <summary>余料按面积降序输出，便于下游先用大块 / Remnants are largest-first for downstream reuse.</summary>
    [Fact]
    public void Remnants_ShouldBeSortedByAreaDescending() {
        IReadOnlyList<Remnant2> remnants = FreeSpaceAnalyzer.AnalyzeSheet(
            "s",
            Sheet(20, 10),
            new[] { Placed(0, 0, 15, 3), Placed(0, 5, 2, 2) });

        remnants.Should().NotBeEmpty();
        double[] areas = remnants.Select(remnant => D(remnant.Area)).ToArray();
        areas.Should().BeInDescendingOrder();
    }

    /// <summary>退化矩形（零面积）必须被丢弃 / Degenerate zero-area rectangles are dropped.</summary>
    [Fact]
    public void Remnants_ShouldDropDegenerate() {
        IReadOnlyList<Remnant2> remnants = FreeSpaceAnalyzer.AnalyzeSheet(
            "s", Sheet(10, 10), new[] { Placed(0, 0, 10, 10) });

        remnants.Should().BeEmpty("整板被占满时没有余料 / a fully occupied sheet has no remnants");
    }

    /// <summary>多块板材各自得到独立的余料列表，名称按序号 / Each sheet gets its own list with an indexed name.</summary>
    [Fact]
    public void Analyze_ShouldNameSheetsByIndex() {
        IReadOnlyList<Remnant2> remnants = FreeSpaceAnalyzer.Analyze(new[] {
            (Sheet(10, 10), (IReadOnlyList<Placement2<FltX>>)Array.Empty<Placement2<FltX>>()),
            (Sheet(5, 5), (IReadOnlyList<Placement2<FltX>>)Array.Empty<Placement2<FltX>>()),
        });

        remnants.Should().HaveCount(2);
        remnants.Select(remnant => remnant.SheetName).Should().Equal("sheet-1", "sheet-2");
    }

    /// <summary>取出数值 / Read the number.</summary>
    private static double D(Quantity<FltX> quantity) => quantity.Value.ToFlt64().ToDouble();

    /// <summary>构造板材 / Build a sheet.</summary>
    private static Sheet2Shape<FltX> Sheet(double w, double h) => new(Meters(w), Meters(h));

    /// <summary>构造放置 / Build a placement.</summary>
    private static Placement2<FltX> Placed(double x, double y, double w, double h) => new(
        new RectanglePackingShape2<FltX>(Meters(w), Meters(h)),
        new QuantityPoint2<FltX>(Meters(x), Meters(y)));

    /// <summary>构造以米为单位的量 / Build a quantity in metres.</summary>
    private static Quantity<FltX> Meters(double value) => new(new FltX(value), SIBaseUnits.Meter);
}
