// 刀缝与几何扩展测试 / Tests for kerf handling and geometry extensions.

using System;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Geometry;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp2d.Tests;

/// <summary>
/// 刀缝与几何扩展 / Kerf handling and geometry extensions.
/// </summary>
public sealed class KerfTest {
    /// <summary>矩形按间距在宽高各膨胀一次 / A rectangle grows by the spacing in both dimensions.</summary>
    [Fact]
    public void Rectangle_ShouldInflateBothDimensions() {
        var shape = new RectanglePackingShape2<FltX>(Meters(10), Meters(20));

        var inflated = (RectanglePackingShape2<FltX>)shape.InflatedBy(Meters(2));

        D(inflated.Width).Should().BeApproximately(12.0, 1e-9);
        D(inflated.Height).Should().BeApproximately(22.0, 1e-9);
    }

    /// <summary>
    /// **圆形按直径膨胀，即半径加一半**：圆之间不区分方向，半径加整个间距会让间隙翻倍。
    /// **A circle grows in diameter, i.e. by half in radius**: circles have no preferred direction, and adding the
    /// full spacing to the radius would double the gap.
    /// </summary>
    [Fact]
    public void Circle_ShouldInflateRadiusByHalf() {
        var circle = new CirclePackingShape2<FltX>(Meters(5));

        var inflated = (CirclePackingShape2<FltX>)circle.InflatedBy(Meters(4));

        D(inflated.Radius).Should().BeApproximately(7.0, 1e-9, "半径 + 间距/2 / radius plus half the spacing");
        D(inflated.BoundingWidth).Should().BeApproximately(14.0, 1e-9);
    }

    /// <summary>
    /// **非正间距返回原形状**：负刀缝没有物理意义，按"负膨胀"处理会让相邻件互相嵌入。
    /// **A non-positive spacing leaves the shape unchanged**: a negative kerf is meaningless, and negative
    /// inflation would let neighbours interpenetrate.
    /// </summary>
    [Fact]
    public void NonPositiveSpacing_ShouldLeaveShapeUnchanged() {
        var shape = new RectanglePackingShape2<FltX>(Meters(10), Meters(20));

        shape.InflatedBy(Meters(0)).Should().BeSameAs(shape);
        shape.InflatedBy(Meters(-3)).Should().BeSameAs(shape);
    }

    /// <summary>膨胀后的放置位置不变 / Inflating a placement keeps its position.</summary>
    [Fact]
    public void Placement_ShouldKeepPositionWhenInflated() {
        var placement = new Placement2<FltX>(
            new RectanglePackingShape2<FltX>(Meters(10), Meters(10)),
            new QuantityPoint2<FltX>(Meters(3), Meters(4)));

        Placement2<FltX> inflated = placement.InflatedBy(Meters(2));

        D(inflated.X).Should().BeApproximately(3.0, 1e-9);
        D(inflated.Y).Should().BeApproximately(4.0, 1e-9);
        D(inflated.Width).Should().BeApproximately(12.0, 1e-9);
    }

    /// <summary>
    /// **节距 = 包围尺寸 + kerf**，使真实间隙恰为 kerf。
    /// **The pitch is the bounding size plus the kerf**, making the real gap exactly the kerf.
    /// </summary>
    [Fact]
    public void Pitch_ShouldIncludeKerf() {
        var shape = new RectanglePackingShape2<FltX>(Meters(10), Meters(20));

        QuantityVector2<FltX> pitch = Kerf.PitchOf(shape, Meters(2));

        D(pitch.X).Should().BeApproximately(12.0, 1e-9);
        D(pitch.Y).Should().BeApproximately(22.0, 1e-9);
    }

    /// <summary>零间距时节距退化为包围尺寸 / A zero spacing degrades the pitch to the bounding size.</summary>
    [Fact]
    public void Pitch_ShouldDegradeToBoundingSize() {
        var shape = new RectanglePackingShape2<FltX>(Meters(10), Meters(20));

        QuantityVector2<FltX> pitch = Kerf.PitchOf(shape, Meters(0));

        D(pitch.X).Should().BeApproximately(10.0, 1e-9);
        D(pitch.Y).Should().BeApproximately(20.0, 1e-9);
    }

    /// <summary>
    /// **膨胀后的间距必须真的阻止相邻件靠得太近**：这是 kerf 的全部意义，只断言宽高变大不足以证明它有效。
    /// **Inflation must actually keep neighbours apart**: that is the whole point of a kerf, and asserting only that
    /// the dimensions grew does not show it works.
    /// </summary>
    [Fact]
    public void InflatedShape_ShouldRejectTooCloseNeighbour() {
        var shape = new RectanglePackingShape2<FltX>(Meters(10), Meters(10));
        // 两件相距 1 米，小于 kerf 2 米 / two pieces 1 m apart, less than the 2 m kerf.
        Placement2<FltX> left = new(shape, new QuantityPoint2<FltX>(Meters(0), Meters(0)));
        Placement2<FltX> right = new(shape, new QuantityPoint2<FltX>(Meters(11), Meters(0)));

        left.Overlapped(right).Value.Should().BeFalse("真实形状并不相交 / the real shapes do not intersect");
        left.InflatedBy(Meters(2)).Overlapped(right).Value
            .Should().BeTrue("间隙小于刀缝，膨胀后相交 / the gap is under the kerf, so inflated shapes intersect");
    }

    /// <summary>间隙足够时膨胀后仍不相交 / With enough clearance inflation still reports no overlap.</summary>
    [Fact]
    public void InflatedShape_ShouldAcceptSufficientClearance() {
        var shape = new RectanglePackingShape2<FltX>(Meters(10), Meters(10));
        Placement2<FltX> left = new(shape, new QuantityPoint2<FltX>(Meters(0), Meters(0)));
        Placement2<FltX> right = new(shape, new QuantityPoint2<FltX>(Meters(20), Meters(0)));

        left.InflatedBy(Meters(2)).Overlapped(right).Value.Should().BeFalse();
    }

    /// <summary>圆形面积用数值类型自身的 π / Circle area uses the numeric type's own π.</summary>
    [Fact]
    public void CircleArea_ShouldUseOwnPi() =>
        D(new QuantityCircle2<FltX>(Meters(2)).AreaOf())
            .Should().BeApproximately(4.0 * System.Math.PI, 1e-6);

    /// <summary>
    /// **包围盒面积是外接矩形面积**，圆形件时等于 4r² 而不是 πr²——利用率误用会高估占用。
    /// **The bounding area is the enclosing rectangle**, 4r² for a circle rather than πr²; using it for utilization
    /// would overstate occupancy.
    /// </summary>
    [Fact]
    public void BoundingArea_ShouldBeEnclosingRectangle() {
        var box = new QuantityBox2<FltX>(
            Meters(0), Meters(0), new QuantityCircle2<FltX>(Meters(1)));

        D(box.BoundingArea()).Should().BeApproximately(4.0, 1e-9);
    }

    /// <summary>贴板材边仍算在范围内 / Flush with the sheet edge is inside.</summary>
    [Fact]
    public void InsideSheet_ShouldAllowTouchingBorder() {
        var box = new QuantityBox2<FltX>(
            Meters(0), Meters(0), new QuantityRectangle2<FltX>(Meters(10), Meters(10)));

        box.InsideSheet(Meters(10), Meters(10)).Value.Should().BeTrue();
    }

    /// <summary>越界不算在范围内 / Out of bounds is not inside.</summary>
    [Fact]
    public void InsideSheet_ShouldRejectOversize() {
        var box = new QuantityBox2<FltX>(
            Meters(0), Meters(0), new QuantityRectangle2<FltX>(Meters(11), Meters(10)));

        box.InsideSheet(Meters(10), Meters(10)).Value.Should().BeFalse();
    }

    /// <summary>取出数值 / Read the number.</summary>
    private static double D(Quantity<FltX> quantity) => quantity.Value.ToFlt64().ToDouble();

    /// <summary>构造以米为单位的量 / Build a quantity in metres.</summary>
    private static Quantity<FltX> Meters(double value) => new(new FltX(value), SIBaseUnits.Meter);
}
