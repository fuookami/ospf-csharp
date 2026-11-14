// 二维板材与放置测试 / Tests for 2D sheets and placements.

using System;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp2d.Tests;

/// <summary>
/// 二维板材与放置 / 2D sheets and placements.
/// </summary>
public sealed class SheetPlacement2Test {
    /// <summary>板材面积等于宽乘高 / A sheet's area is width times height.</summary>
    [Fact]
    public void Sheet_ShouldComputeArea() =>
        D(Sheet(10, 20).Area).Should().BeApproximately(200.0, 1e-9);

    /// <summary>恰好等大的件可以放入 / An exactly-sized item fits.</summary>
    [Fact]
    public void Sheet_ShouldAcceptExactFit() =>
        Sheet(10, 20).Enabled(Meters(10), Meters(20)).Value.Should().BeTrue();

    /// <summary>超宽或超高的件放不下 / Too wide or too tall does not fit.</summary>
    [Fact]
    public void Sheet_ShouldRejectOversize() {
        Sheet(10, 20).Enabled(Meters(11), Meters(20)).Value.Should().BeFalse();
        Sheet(10, 20).Enabled(Meters(10), Meters(21)).Value.Should().BeFalse();
    }

    /// <summary>
    /// **圆形件按外接正方形判定**：直径等于板材宽高能放下，更大的即使面积更小也不行——排样关心能否摆下。
    /// **Circles are judged by their enclosing square**: a diameter equal to the sheet fits, and a larger one does
    /// not even with smaller area, because packing cares about physical fit.
    /// </summary>
    [Fact]
    public void Sheet_ShouldJudgeCircleByBoundingBox() {
        var circle = new CirclePackingShape2<FltX>(Meters(5));

        Sheet(10, 10).Enabled(circle).Value.Should().BeTrue("直径 10 = 板材边长 / diameter 10 equals the side");
        Sheet(10, 10).Enabled(new CirclePackingShape2<FltX>(Meters(6))).Value.Should().BeFalse();
    }

    /// <summary>板材包围盒锚定在原点 / The sheet's origin box is anchored at the origin.</summary>
    [Fact]
    public void Sheet_ShouldAnchorOriginBox() {
        Box2Geometry<FltX> origin = Sheet(10, 20).OriginBox();

        D(origin.X).Should().BeApproximately(0.0, 1e-9);
        D(origin.Y).Should().BeApproximately(0.0, 1e-9);
        D(origin.MaxX().Value).Should().BeApproximately(10.0, 1e-9);
        D(origin.MaxY().Value).Should().BeApproximately(20.0, 1e-9);
    }

    /// <summary>非正尺寸的板材必须被拒绝 / A non-positive sheet size must be rejected.</summary>
    [Fact]
    public void SheetFactory_ShouldRejectNonPositive() {
        Sheet2ShapeFactory.Sheet2Shape<FltX>(Meters(0), Meters(10)).IsOk.Should().BeFalse();
        Sheet2ShapeFactory.Sheet2Shape<FltX>(Meters(10), Meters(-1)).IsOk.Should().BeFalse();
    }

    /// <summary>正尺寸板材必须通过 / A positive sheet size must pass.</summary>
    [Fact]
    public void SheetFactory_ShouldAcceptPositive() =>
        Sheet2ShapeFactory.Sheet2Shape<FltX>(Meters(10), Meters(10)).IsOk.Should().BeTrue();

    /// <summary>放置的包围盒与右上角由形状与位置决定 / A placement's box follows shape and position.</summary>
    [Fact]
    public void Placement_ShouldDeriveCorners() {
        Placement2<FltX> placement = Placed(2, 3, 4, 5);

        D(placement.MaxX().Value).Should().BeApproximately(6.0, 1e-9);
        D(placement.MaxY().Value).Should().BeApproximately(8.0, 1e-9);
        D(placement.Area).Should().BeApproximately(20.0, 1e-9);
    }

    /// <summary>相交的两个放置判定为重叠 / Two interpenetrating placements overlap.</summary>
    [Fact]
    public void Placement_ShouldDetectOverlap() =>
        Placed(0, 0, 4, 4).Overlapped(Placed(2, 2, 4, 4)).Value.Should().BeTrue();

    /// <summary>贴边的两个放置不算重叠 / Two flush placements do not overlap.</summary>
    [Fact]
    public void Placement_ShouldExcludeTangency() =>
        Placed(0, 0, 4, 4).Overlapped(Placed(4, 0, 4, 4)).Value.Should().BeFalse();

    /// <summary>位于板材内部的放置判定为在板材内 / A placement within the sheet is inside.</summary>
    [Fact]
    public void Placement_ShouldBeInsideSheet() =>
        Placed(1, 1, 4, 4).InsideSheet(Sheet(10, 10)).Value.Should().BeTrue();

    /// <summary>越界的放置不在板材内 / An out-of-bounds placement is not inside.</summary>
    [Fact]
    public void Placement_ShouldBeOutsideSheet() =>
        Placed(8, 8, 4, 4).InsideSheet(Sheet(10, 10)).Value.Should().BeFalse();

    /// <summary>贴板材边仍算在内部 / Flush with the sheet edge is still inside.</summary>
    [Fact]
    public void Placement_ShouldAllowFlushWithSheetEdge() =>
        Placed(6, 0, 4, 10).InsideSheet(Sheet(10, 10)).Value.Should().BeTrue();

    /// <summary>平移只改变位置 / Translation changes only the position.</summary>
    [Fact]
    public void Placement_ShouldTranslate() {
        Placement2<FltX> moved = Placed(1, 2, 3, 4)
            .Translate(new QuantityVector2<FltX>(Meters(10), Meters(20))).Value;

        D(moved.X).Should().BeApproximately(11.0, 1e-9);
        D(moved.Y).Should().BeApproximately(22.0, 1e-9);
        D(moved.Width).Should().BeApproximately(3.0, 1e-9);
    }

    /// <summary>
    /// **排序先 y 后 x**：BLA 排样按"更低优先、同高更左优先"，顺序反了会得到更差的放置序列。
    /// **Ordering is y then x**: BLA packing prefers lower then leftmost, and reversing it yields worse placements.
    /// </summary>
    [Fact]
    public void Point_ShouldOrderByYThenX() {
        QuantityPoint2<FltX> low = Point(100, 1);
        QuantityPoint2<FltX> high = Point(0, 2);

        low.OrdOrNull(high).Should().BeOfType<Order.Less>("y 是主键 / y is the primary key");

        QuantityPoint2<FltX> left = Point(1, 5);
        QuantityPoint2<FltX> right = Point(2, 5);
        left.OrdOrNull(right).Should().BeOfType<Order.Less>("同高时比 x / equal height falls back to x");

        left.OrdOrNull(Point(1, 5)).Should().BeOfType<Order.Equal>();
    }

    /// <summary>量纲不可比时返回 null 而不是猜一个顺序 / An incomparable pair yields null rather than a guess.</summary>
    [Fact]
    public void Point_ShouldReturnNullWhenIncomparable() {
        var length = new QuantityPoint2<FltX>(Meters(1), Meters(1));
        var mass = new QuantityPoint2<FltX>(
            new Quantity<FltX>(new FltX(1), SIBaseUnits.Kilogram), Meters(1));

        length.OrdOrNull(mass).Should().BeNull();
    }

    /// <summary>构造板材 / Build a sheet.</summary>
    private static Sheet2Shape<FltX> Sheet(double w, double h) =>
        new(Meters(w), Meters(h));

    /// <summary>构造放置 / Build a placement.</summary>
    private static Placement2<FltX> Placed(double x, double y, double w, double h) => new(
        new RectanglePackingShape2<FltX>(Meters(w), Meters(h)),
        new QuantityPoint2<FltX>(Meters(x), Meters(y)));

    /// <summary>构造点 / Build a point.</summary>
    private static QuantityPoint2<FltX> Point(double x, double y) =>
        new(Meters(x), Meters(y));

    /// <summary>取出数值 / Read the number.</summary>
    private static double D(Quantity<FltX> quantity) => quantity.Value.ToFlt64().ToDouble();

    /// <summary>构造以米为单位的量 / Build a quantity in metres.</summary>
    private static Quantity<FltX> Meters(double value) => new(new FltX(value), SIBaseUnits.Meter);
}
