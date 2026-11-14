// 二维包围盒几何测试 / Tests for 2D bounding box geometry.

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
/// 二维包围盒几何 / 2D bounding box geometry.
/// </summary>
public sealed class Box2GeometryTest {
    /// <summary>分离的两个盒子不重叠 / Disjoint boxes do not overlap.</summary>
    [Fact]
    public void Overlapped_ShouldBeFalseWhenDisjoint() =>
        Box(0, 0, 2, 2).Overlapped(Box(3, 3, 2, 2)).Value.Should().BeFalse();

    /// <summary>内部相交的两个盒子重叠 / Interior-overlapping boxes overlap.</summary>
    [Fact]
    public void Overlapped_ShouldBeTrueWhenInteriorOverlap() =>
        Box(0, 0, 2, 2).Overlapped(Box(1, 1, 2, 2)).Value.Should().BeTrue();

    /// <summary>
    /// **贴边相切不算重叠**：相切是排样中最常见的相邻关系，判成重叠会让任何规则网格都无法成立。
    /// **Edge tangency is not an overlap**: tangency is the most common adjacency in packing, and counting it as
    /// overlap would invalidate every regular grid.
    /// </summary>
    [Fact]
    public void Overlapped_ShouldExcludeTangency() {
        Box(0, 0, 2, 2).Overlapped(Box(2, 0, 2, 2)).Value
            .Should().BeFalse("右侧贴边 / touching on the right edge");
        Box(0, 0, 2, 2).Overlapped(Box(0, 2, 2, 2)).Value
            .Should().BeFalse("上侧贴边 / touching on the top edge");
    }

    /// <summary>圆与圆相切不算重叠 / Tangent circles do not overlap.</summary>
    [Fact]
    public void Overlapped_ShouldExcludeTangentCircles() {
        // 半径 1 的两圆，圆心相距 2（相切）/ two radius-1 circles with centres 2 apart (tangent).
        Box2Geometry<FltX> left = CircleBox(0, 1);
        Box2Geometry<FltX> right = CircleBox(2, 1);

        left.Overlapped(right).Value.Should().BeFalse();
    }

    /// <summary>圆与圆内部相交算重叠 / Interior-overlapping circles do overlap.</summary>
    [Fact]
    public void Overlapped_ShouldDetectCircleInterpenetration() =>
        CircleBox(0, 1).Overlapped(CircleBox(1, 1)).Value.Should().BeTrue();

    /// <summary>完全包含在内部的盒子不算"重叠"以外的情况，但必须判定为 overlap / A contained box overlaps.</summary>
    [Fact]
    public void Overlapped_ShouldBeTrueWhenContained() =>
        Box(0, 0, 10, 10).Overlapped(Box(2, 2, 1, 1)).Value.Should().BeTrue();

    /// <summary>完全位于内部时 <c>Inside</c> 为真 / A box fully inside reports inside.</summary>
    [Fact]
    public void Inside_ShouldBeTrueWhenFullyContained() =>
        Box(1, 1, 2, 2).Inside(Box(0, 0, 10, 10)).Value.Should().BeTrue();

    /// <summary>
    /// **贴边仍算位于内部**：贴在板材边上的件是合法放置，判成越界会让板材边缘无法使用。
    /// **Touching still counts as inside**: a piece flush with the sheet edge is a legal placement, and calling it
    /// out-of-bounds would make the sheet border unusable.
    /// </summary>
    [Fact]
    public void Inside_ShouldAllowTouchingBorder() =>
        Box(0, 0, 10, 10).Inside(Box(0, 0, 10, 10)).Value.Should().BeTrue();

    /// <summary>越界的盒子不位于内部 / An out-of-bounds box is not inside.</summary>
    [Fact]
    public void Inside_ShouldBeFalseWhenCrossingBorder() =>
        Box(1, 1, 10, 2).Inside(Box(0, 0, 10, 10)).Value.Should().BeFalse();

    /// <summary>交集面积等于重叠区域 / The intersection area equals the overlap region.</summary>
    [Fact]
    public void OverlapArea_ShouldComputeIntersection() =>
        D(Box(0, 0, 4, 4).OverlapArea(Box(2, 2, 4, 4)))
            .Should().BeApproximately(4.0, 1e-9, "重叠区域是 2×2 / the overlap is 2 by 2");

    /// <summary>无交集时交集面积为零 / A disjoint pair has zero intersection area.</summary>
    [Fact]
    public void OverlapArea_ShouldBeZeroWhenDisjoint() =>
        D(Box(0, 0, 1, 1).OverlapArea(Box(5, 5, 1, 1))).Should().BeApproximately(0.0, 1e-9);

    /// <summary>零尺寸盒子判定为退化 / A zero-extent box is degenerate.</summary>
    [Fact]
    public void IsDegenerate_ShouldDetectZeroExtent() {
        Box(0, 0, 0, 2).IsDegenerate().Should().BeTrue();
        Box(0, 0, 2, 2).IsDegenerate().Should().BeFalse();
    }

    /// <summary>矩形中心必须是几何中心 / A rectangle's centre must be its geometric centre.</summary>
    [Fact]
    public void Center_ShouldBeGeometricCentre() {
        Box2Geometry<FltX> box = Box(2, 4, 10, 20);

        D(box.CenterX).Should().BeApproximately(7.0, 1e-9);
        D(box.CenterY).Should().BeApproximately(14.0, 1e-9);
    }

    /// <summary>圆形盒的中心位于圆心 / A circle box centre is the circle centre.</summary>
    [Fact]
    public void CircleCenter_ShouldBeCircleCentre() {
        Box2Geometry<FltX> circle = CircleBox(3, 2);

        // 盒子左下角 (3, 0)，半径 2，因此圆心是 (5, 2)。/ origin (3, 0) with radius 2 gives centre (5, 2).
        D(circle.CenterX).Should().BeApproximately(5.0, 1e-9);
        D(circle.CenterY).Should().BeApproximately(2.0, 1e-9);
    }

    /// <summary>构造矩形盒子 / Build a rectangle box.</summary>
    private static Box2Geometry<FltX> Box(double x, double y, double w, double h) =>
        Box2Geometry<FltX>.Of(Meters(x), Meters(y), Meters(w), Meters(h));

    /// <summary>构造圆形盒子，半径 r / Build a circle box with radius r.</summary>
    private static Box2Geometry<FltX> CircleBox(double x, double r) =>
        new(Meters(x), Meters(0.0), new QuantityCircle2<FltX>(Meters(r)));

    /// <summary>取出数值 / Read the number.</summary>
    private static double D(Quantity<FltX> quantity) => quantity.Value.ToFlt64().ToDouble();

    /// <summary>构造以米为单位的量 / Build a quantity in metres.</summary>
    private static Quantity<FltX> Meters(double value) => new(new FltX(value), SIBaseUnits.Meter);
}
