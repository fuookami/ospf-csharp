// 层（B635）的口径判据 / Criteria for the layer's conventions (B635).

using System;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Framework.Bpp2d.Domain.Item.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp2d.Tests.Item;

/// <summary>
/// **层的两个口径（B635）** / The layer's two conventions (B635).
/// </summary>
/// <remarks>
/// 层本身早已落地（`Domain/SheetModel.cs` 的 `Layer<V>`），`SheetModelTest` 也钉了 `ItemAmounts` 分组。这里补的是**另外两条口径**：
///
/// 1. `Area` 用**实际占用面积**——圆形件是 πr²，不是外接正方形；
/// 2. `Utilization()` 在**分母为零时返回零**而不是除零。
///
/// 两条都只在"层里有圆形件"或"层形状退化"时才会露出，而既有判据的夹具全是矩形，所以一直没被钉住。
///
/// The layer itself landed long ago (`Layer<V>` in `Domain/SheetModel.cs`) and `SheetModelTest` already pins `ItemAmounts` grouping. What is added here is **two other conventions**:
/// `Area` uses **actually occupied area** — πr² for a circle rather than its enclosing square — and `Utilization()` **returns zero when the denominator is zero** instead of dividing.
/// Both only surface with a circle in the layer or a degenerate layer shape, and the existing fixtures are all rectangles, so neither had ever been pinned.
/// </remarks>
public class LayerConventionTests {
    private static Quantity<FltX> Q(double value) => new(new FltX(value), LengthUnits.Millimeter);

    private static Placement2<FltX> Rect(double width, double height, string? itemId) => new(
        Shape: new RectanglePackingShape2<FltX>(Q(width), Q(height)),
        Position: new QuantityPoint2<FltX>(Q(0.0), Q(0.0)),
        Rotated: false,
        ItemId: itemId);

    private static Placement2<FltX> Circle(double radius, string? itemId) => new(
        Shape: new CirclePackingShape2<FltX>(Q(radius)),
        Position: new QuantityPoint2<FltX>(Q(100.0), Q(0.0)),
        Rotated: false,
        ItemId: itemId);

    private static Layer<FltX> Make(Sheet2Shape<FltX> shape, params Placement2<FltX>[] units) =>
        new(iteration: 1L, from: null, sheet: null, shape: shape, units: units);

    /// <summary>
    /// **圆形件的面积是 πr²，不是外接正方形** / A circle contributes pi r squared, not its bounding square.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 一个矩形加一个圆 → `Area` 是 `w·h + πr²`，**不是** `w·h + (2r)²`。
    /// 2. `BoundingArea` 仍是层的外接矩形面积（100 × 50），与 `Area` 是两个量。
    /// 3. 利用率 = `Area / BoundingArea`，跟着 `Area` 的口径走。
    ///
    /// Three paired points: one rectangle plus one circle gives `Area` as `w·h + πr²`, **not** `w·h + (2r)²`; `BoundingArea` remains the layer's enclosing rectangle (100 × 50) and is a
    /// separate quantity; and utilisation is `Area / BoundingArea`, following `Area`'s convention.
    /// </remarks>
    [Fact]
    public void LayerArea_ShouldUseOccupiedAreaForCircles() {
        Layer<FltX> layer = Make(
            new Sheet2Shape<FltX>(Q(100.0), Q(50.0)),
            Rect(20.0, 10.0, "I1"),
            Circle(10.0, "I2"));

        double expected = 200.0 + (System.Math.PI * 10.0 * 10.0);
        layer.Area.Value.ToFlt64().ToDouble().Should().BeApproximately(expected, 1e-6);
        layer.Area.Value.ToFlt64().ToDouble()
            .Should().NotBeApproximately(200.0 + 400.0, 1e-6, "the bounding square would overstate the circle");
        layer.BoundingArea.Value.ToFlt64().ToDouble().Should().Be(5000.0);
        layer.Utilization().ToFlt64().ToDouble().Should().BeApproximately(expected / 5000.0, 1e-6);
    }

    /// <summary>
    /// **分母为零时利用率返回零，不除** / Utilisation returns zero instead of dividing by zero.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 层形状宽为零 → **利用率为零**，不抛异常。
    /// 2. 空层的 `Area` 为零、件数为零。
    /// 3. **没有编号的放置不计件**：两个放置里一个有编号，`ItemAmounts` 只有一个条目。
    ///
    /// Three paired points: a zero-width layer shape gives **zero utilisation** without throwing; an empty layer has zero area and zero amount; and **placements without an id are not
    /// counted** — two placements with one id yield a single-entry `ItemAmounts`.
    /// </remarks>
    [Fact]
    public void LayerUtilization_ShouldGuardZeroAndSkipEmptyIds() {
        Layer<FltX> empty = Make(new Sheet2Shape<FltX>(Q(0.0), Q(50.0)));
        empty.Area.Value.ToFlt64().ToDouble().Should().Be(0.0);
        empty.Amount.Should().Be(0UL);
        empty.Utilization().ToFlt64().ToDouble().Should().Be(0.0, "a zero-area layer must not divide");

        Layer<FltX> mixed = Make(
            new Sheet2Shape<FltX>(Q(100.0), Q(50.0)),
            Rect(20.0, 10.0, "I1"),
            Rect(10.0, 10.0, null));
        mixed.Amount.Should().Be(2UL);
        mixed.ItemAmounts.Should().HaveCount(1, "the placement without an id is not an item");
        mixed.ItemAmounts[new ItemId("I1")].Should().Be(1UL);
    }
}
