// 几何扩展与形状工厂测试 / Tests for the geometry extensions and the shape factory.
//
// 本批由**覆盖率审计**驱动：脚本扫出 Bpp2d 中没有被任何测试文件提及的公开类型，再人工筛掉接口与纯辅助类，
// 剩下真正有行为却零覆盖的部分补测。这不是凑数量——`AreaOf` 与 `BoundingArea` 的差别正是圆形件利用率被高估的
// 经典来源，而形状工厂的拒绝路径此前完全没被走过。
// This batch is driven by a **coverage audit**: a script listed every public type in Bpp2d that no test file mentions,
// then interfaces and pure helpers were filtered out by hand, leaving the parts that genuinely have behaviour but zero
// coverage. Not padding — the difference between `AreaOf` and `BoundingArea` is the classic source of overstated circular
// utilisation, and the shape factory's rejection paths had never been exercised.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Geometry;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp2d.Tests;

/// <summary>
/// 几何扩展与形状工厂 / Geometry extensions and the shape factory.
/// </summary>
public sealed class Geometry2ExtensionsTest {
    /// <summary>圆面积按 πr² 计算 / A circle's area is πr².</summary>
    [Fact]
    public void AreaOf_ShouldUsePiRSquared() {
        Quantity<FltX> area = new QuantityCircle2<FltX>(Meters(2)).AreaOf();

        double pi = System.Math.PI;
        area.Value.ToFlt64().ToDouble().Should().BeApproximately(4.0 * pi, 1e-6, "r = 2 → 4π");
    }

    /// <summary>
    /// **包围盒面积恒为外接矩形面积，不是圆的真实面积**：用错这一项会把圆形件的占用高估到 4/π ≈ 1.27 倍。
    /// **The bounding area is the enclosing rectangle, not the circle's true area**: using the wrong one overstates a
    /// circular item's footprint by 4/π ≈ 1.27×.
    /// </summary>
    [Fact]
    public void BoundingArea_ShouldBeEnclosingRectangle() {
        var box = QuantityBox2<FltX>.AtOrigin(new QuantityCircle2<FltX>(Meters(2)));
        Quantity<FltX> bounding = box.BoundingArea();
        Quantity<FltX> actual = new QuantityCircle2<FltX>(Meters(2)).AreaOf();

        double boundingValue = bounding.Value.ToFlt64().ToDouble();
        double actualValue = actual.Value.ToFlt64().ToDouble();
        boundingValue.Should().BeApproximately(16.0, 1e-6, "直径 4 → 16");
        boundingValue.Should().BeGreaterThan(actualValue, "外接矩形大于圆 / the rectangle exceeds the circle");
    }

    /// <summary>矩形包围盒面积等于宽高之积 / A rectangle bounding box's area is width times height.</summary>
    [Fact]
    public void BoundingArea_ShouldMultiplyRectangle() {
        var box = QuantityBox2<FltX>.AtOrigin(new QuantityRectangle2<FltX>(Meters(3), Meters(4)));

        box.BoundingArea().Value.ToFlt64().ToDouble().Should().BeApproximately(12.0, 1e-9);
    }

    /// <summary>完全在板材内的盒体通过 / A box well inside the sheet passes.</summary>
    [Fact]
    public void InsideSheet_ShouldAcceptInnerBox() =>
        Box(1, 1, 2, 2).InsideSheet(Meters(10), Meters(10)).Value.Should().BeTrue();

    /// <summary>
    /// **贴板材边是合法放置**：下界用 `>=`、上界用 `<=`。判成越界会让板材边缘一圈区域无法使用。
    /// **Flush with the edge is legal**: lower bound uses `>=` and upper `<=`. Rejecting it would make a border strip of the
    /// sheet unusable.
    /// </summary>
    [Fact]
    public void InsideSheet_ShouldAllowTouchingEdges() {
        Box(0, 0, 10, 10).InsideSheet(Meters(10), Meters(10)).Value.Should().BeTrue();
        Box(0, 0, 10, 5).InsideSheet(Meters(10), Meters(10)).Value.Should().BeTrue();
    }

    /// <summary>负坐标越界 / Negative coordinates are out of bounds.</summary>
    [Fact]
    public void InsideSheet_ShouldRejectNegativeOrigin() =>
        Box(-1, 0, 2, 2).InsideSheet(Meters(10), Meters(10)).Value.Should().BeFalse();

    /// <summary>超出右边界越界 / Exceeding the right edge is out of bounds.</summary>
    [Fact]
    public void InsideSheet_ShouldRejectOverflow() =>
        Box(9, 0, 11, 2).InsideSheet(Meters(10), Meters(10)).Value.Should().BeFalse();

    /// <summary>超出上边界越界 / Exceeding the top edge is out of bounds.</summary>
    [Fact]
    public void InsideSheet_ShouldRejectTopOverflow() =>
        Box(0, 9, 2, 11).InsideSheet(Meters(10), Meters(10)).Value.Should().BeFalse();

    /// <summary>合法的矩形形状工厂 / The rectangle shape factory succeeds.</summary>
    [Fact]
    public void Factory_ShouldBuildRectangle() =>
        PackingShape2Factory.RectangleShape2<FltX>(Meters(2), Meters(4)).IsOk.Should().BeTrue();

    /// <summary>合法的圆形形状工厂 / The circle shape factory succeeds.</summary>
    [Fact]
    public void Factory_ShouldBuildCircle() =>
        PackingShape2Factory.CircleShape2<FltX>(Meters(2)).IsOk.Should().BeTrue();

    /// <summary>
    /// **非正尺寸被拒绝**：零或负尺寸的形状无法排布，放过去会在切分空间时产生退化区域并让递归无法收敛。
    /// **Non-positive dimensions are rejected**: a zero or negative shape cannot be laid out, and letting it through
    /// produces degenerate regions while splitting space, so recursion cannot converge.
    /// </summary>
    [Fact]
    public void Factory_ShouldRejectNonPositiveRectangle() {
        PackingShape2Factory.RectangleShape2<FltX>(Meters(0), Meters(4)).IsOk.Should().BeFalse();
        PackingShape2Factory.RectangleShape2<FltX>(Meters(2), Meters(-1)).IsOk.Should().BeFalse();
    }

    /// <summary>非正半径被拒绝 / A non-positive radius is rejected.</summary>
    [Fact]
    public void Factory_ShouldRejectNonPositiveCircle() =>
        PackingShape2Factory.CircleShape2<FltX>(Meters(0)).IsOk.Should().BeFalse();

    /// <summary>矩形形状不被识别为圆 / A rectangle is not recognised as a circle.</summary>
    [Fact]
    public void Shape_ShouldDistinguishKinds() {
        PackingShape2<FltX> rectangle = PackingShape2Factory.RectangleShape2<FltX>(Meters(2), Meters(2)).Value;
        PackingShape2<FltX> circle = PackingShape2Factory.CircleShape2<FltX>(Meters(2)).Value;

        rectangle.IsRectangle().Should().BeTrue();
        rectangle.IsCircle().Should().BeFalse();
        circle.IsCircle().Should().BeTrue();
        circle.IsRectangle().Should().BeFalse();
    }

    /// <summary>不可旋转形状只给一个候选 / A non-rotatable shape yields one candidate.</summary>
    [Fact]
    public void CandidateShapes_ShouldYieldOneWithoutRotation() =>
        PackingShape2Factory.RectangleShape2<FltX>(Meters(2), Meters(4)).Value
            .CandidateShapes(allowRotate: false).Should().ContainSingle();

    /// <summary>
    /// **可旋转矩形给出两个候选**，且第二个的宽高互换；这是旋转改进的来源。
    /// **A rotatable rectangle yields two candidates** with the second's dimensions swapped; that is where rotation gains
    /// come from.
    /// </summary>
    [Fact]
    public void CandidateShapes_ShouldYieldTwoWithRotation() {
        IReadOnlyList<Shape2Candidate<FltX>> candidates =
            PackingShape2Factory.RectangleShape2<FltX>(Meters(2), Meters(4)).Value
                .CandidateShapes(allowRotate: true);

        candidates.Should().HaveCount(2);
        candidates[1].Rotated.Should().BeTrue();
        candidates[1].Shape.BoundingWidth.Value.ToFlt64().ToDouble().Should().BeApproximately(4.0, 1e-9);
    }

    /// <summary>
    /// **圆形件即使允许旋转也不给旋转候选**：旋转一个圆没有任何意义，给出第二个候选只会让候选集翻倍而毫无收益。
    /// **A circle yields no rotated candidate even when rotation is allowed**: rotating a circle is meaningless and a second
    /// candidate would only double the set for no gain.
    /// </summary>
    [Fact]
    public void CandidateShapes_ShouldNotRotateCircle() =>
        PackingShape2Factory.CircleShape2<FltX>(Meters(2)).Value
            .CandidateShapes(allowRotate: true).Should().ContainSingle();

    /// <summary>构造盒体 / Build a box.</summary>
    private static QuantityBox2<FltX> Box(double x, double y, double w, double h) =>
        new(Meters(x), Meters(y), new QuantityRectangle2<FltX>(Meters(w), Meters(h)));

    /// <summary>构造以米为单位的量 / Build a quantity in metres.</summary>
    private static Quantity<FltX> Meters(double value) => new(new FltX(value), SIBaseUnits.Meter);
}
