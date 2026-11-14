// 二维装载形状测试 / Tests for 2D packing shapes.

using System;
using System.Collections.Generic;
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
/// 二维装载形状 / 2D packing shapes.
/// </summary>
public sealed class PackingShape2Test {
    /// <summary>矩形面积等于宽乘高 / A rectangle's area is width times height.</summary>
    [Fact]
    public void Rectangle_ShouldComputeArea() {
        var rectangle = new RectanglePackingShape2<FltX>(Meters(2.0), Meters(3.0));

        D(rectangle.Area).Should().BeApproximately(6.0, 1e-9);
        D(rectangle.BoundingArea).Should().BeApproximately(6.0, 1e-9);
    }

    /// <summary>
    /// **圆形件的实际面积与外接面积必须不同**：混用会把圆形件的利用率算高。
    /// **A circle's actual and enclosing areas must differ**: mixing them inflates utilization.
    /// </summary>
    [Fact]
    public void Circle_ShouldDistinguishAreaFromBoundingArea() {
        var circle = new CirclePackingShape2<FltX>(Meters(1.0));

        D(circle.Area).Should().BeApproximately(System.Math.PI, 1e-6);
        D(circle.BoundingArea).Should().BeApproximately(4.0, 1e-9, "外接矩形是 2r × 2r / enclosing box is 2r by 2r");
        D(circle.BoundingWidth).Should().BeApproximately(2.0, 1e-9);
    }

    /// <summary>矩形旋转后宽高互换 / Rotating a rectangle swaps width and height.</summary>
    [Fact]
    public void Rectangle_ShouldSwapOnRotation() {
        var rectangle = new RectanglePackingShape2<FltX>(Meters(2.0), Meters(5.0));

        var rotated = (RectanglePackingShape2<FltX>)rectangle.Rotated();

        D(rotated.Width).Should().BeApproximately(5.0, 1e-9);
        D(rotated.Height).Should().BeApproximately(2.0, 1e-9);
    }

    /// <summary>圆形旋转后不变 / A circle is unchanged by rotation.</summary>
    [Fact]
    public void Circle_ShouldBeInvariantUnderRotation() {
        var circle = new CirclePackingShape2<FltX>(Meters(1.0));

        circle.Rotated().Should().BeSameAs(circle);
    }

    /// <summary>
    /// **正方形不产生旋转候选**：旋转正方形只是换参数化，几何等价，多出的候选会让搜索空间无谓翻倍。
    /// **A square yields no rotation candidate**: rotating it only reparameterizes an identical shape, and the
    /// extra candidate would double the search space for nothing.
    /// </summary>
    [Fact]
    public void Square_ShouldHaveSingleCandidate() {
        var square = new RectanglePackingShape2<FltX>(Meters(2.0), Meters(2.0));

        square.IsRotatable().Should().BeFalse();
        square.CandidateShapes(allowRotate: true).Should().HaveCount(1);
    }

    /// <summary>非正方形矩形在允许旋转时产生两个候选 / A non-square rectangle yields two candidates.</summary>
    [Fact]
    public void Rectangle_ShouldHaveTwoCandidatesWhenRotationAllowed() {
        var rectangle = new RectanglePackingShape2<FltX>(Meters(2.0), Meters(5.0));

        IReadOnlyList<Shape2Candidate<FltX>> candidates = rectangle.CandidateShapes(allowRotate: true);

        candidates.Should().HaveCount(2);
        candidates[1].Rotated.Should().BeTrue();
    }

    /// <summary>禁止旋转时只返回自身 / With rotation disallowed only the upright shape is returned.</summary>
    [Fact]
    public void Rectangle_ShouldHaveSingleCandidateWhenRotationDisallowed() {
        var rectangle = new RectanglePackingShape2<FltX>(Meters(2.0), Meters(5.0));

        rectangle.CandidateShapes(allowRotate: false).Should().HaveCount(1);
    }

    /// <summary>圆形件只返回自身 / A circle yields only itself.</summary>
    [Fact]
    public void Circle_ShouldHaveSingleCandidate() {
        var circle = new CirclePackingShape2<FltX>(Meters(1.0));

        circle.CandidateShapes(allowRotate: true).Should().HaveCount(1);
    }

    /// <summary>形状类型判定必须与具体类型一致 / The type predicate must agree with the concrete type.</summary>
    [Fact]
    public void Shapes_ShouldReportTypeConsistently() {
        var rectangle = new RectanglePackingShape2<FltX>(Meters(2.0), Meters(3.0));
        var circle = new CirclePackingShape2<FltX>(Meters(1.0));

        rectangle.IsRectangle().Should().BeTrue();
        rectangle.IsCircle().Should().BeFalse();
        circle.IsCircle().Should().BeTrue();
        circle.IsRectangle().Should().BeFalse();
        rectangle.ShapeType.Should().Be(PackingShape2Type.Rectangle);
        circle.ShapeType.Should().Be(PackingShape2Type.Circle);
    }

    /// <summary>
    /// **非正尺寸必须被拒绝**：宽为 0 或负的件在排样里只会产生退化放置，越早拦截越容易定位。
    /// **A non-positive size must be rejected**: a zero or negative-width item only produces degenerate placements
    /// in the layout, and catching it early localizes the mistake.
    /// </summary>
    [Fact]
    public void Factory_ShouldRejectNonPositiveSize() {
        PackingShape2Factory.RectangleShape2<FltX>(Meters(0.0), Meters(3.0)).IsOk
            .Should().BeFalse();
        PackingShape2Factory.RectangleShape2<FltX>(Meters(2.0), Meters(-1.0)).IsOk
            .Should().BeFalse();
        PackingShape2Factory.CircleShape2<FltX>(Meters(0.0)).IsOk
            .Should().BeFalse();
    }

    /// <summary>
    /// 正尺寸必须**通过**——只测拒绝会让"拒绝一切"也算实现。
    /// A positive size must **pass**; a rejection-only suite would let "reject everything" count.
    /// </summary>
    [Fact]
    public void Factory_ShouldAcceptPositiveSize() {
        PackingShape2Factory.RectangleShape2<FltX>(Meters(2.0), Meters(3.0)).IsOk
            .Should().BeTrue();
        PackingShape2Factory.CircleShape2<FltX>(Meters(1.0)).IsOk
            .Should().BeTrue();
    }

    /// <summary>几何投影必须保留原始尺寸 / The geometry projection keeps the original dimensions.</summary>
    [Fact]
    public void Rectangle_ShouldProjectToGeometry() {
        var rectangle = new RectanglePackingShape2<FltX>(Meters(2.0), Meters(3.0));

        Fuookami.Ospf.Quantities.Geometry.QuantityRectangle2<FltX> projection = rectangle.Geometry()
            .Should().BeOfType<Fuookami.Ospf.Quantities.Geometry.QuantityRectangle2<FltX>>().Subject;

        D(projection.Width).Should().BeApproximately(2.0, 1e-9);
        D(projection.Height).Should().BeApproximately(3.0, 1e-9);
    }

    /// <summary>取出数值 / Read the number.</summary>
    private static double D(Quantity<FltX> quantity) =>
        quantity.Value.ToFlt64().ToDouble();

    /// <summary>构造以米为单位的量 / Build a quantity in metres.</summary>
    private static Quantity<FltX> Meters(double value) =>
        new(new FltX(value), SIBaseUnits.Meter);
}
