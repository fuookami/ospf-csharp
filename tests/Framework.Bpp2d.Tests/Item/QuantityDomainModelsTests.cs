// 二维量纲模型（B634）的判据 / Criteria for the quantity-based 2D models (B634).

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Framework.Bpp2d.Domain.Item.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp2d.Tests.Item;

/// <summary>
/// **二维量纲模型（B634）** / The quantity-based 2D models (B634).
/// </summary>
/// <remarks>
/// 判据的重点：两种形状的映射、**旋转要换掉形状本身**、以及物料目录里没有的物料**被丢掉而不是报错**。
///
/// The criteria focus on the two shape mappings, **rotation rewriting the shape itself**, and materials absent from the catalogue being **dropped rather than reported**.
/// </remarks>
public class QuantityDomainModelsTests {
    private static Quantity<FltX> Q(double value) => new(new FltX(value), LengthUnits.Millimeter);

    private static QuantityItemShape<FltX> Rect(double width, double height) =>
        new QuantityItemShape<FltX>.Rectangle<FltX>(Q(width), Q(height));

    private static QuantityItemShape<FltX> Circle(double radius) =>
        new QuantityItemShape<FltX>.Circle<FltX>(Q(radius));

    /// <summary>
    /// **两种形状各自映射到对应的解算器形状** / Each shape maps to its own solver shape.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 矩形 → `RectanglePackingShape2`，**宽高保持原样**（顺序不能交换）。
    /// 2. 圆 → `CirclePackingShape2`，半径保持原样。
    /// 3. **单位保留**：输入是毫米，输出也是毫米——量纲模型存在的意义就是不丢单位。
    ///
    /// Three paired points: a rectangle maps to `RectanglePackingShape2` with **width and height unchanged** (the order must not swap); a circle maps to `CirclePackingShape2` with its
    /// radius unchanged; and **the unit survives** — millimetres in, millimetres out, which is the whole reason the quantity models exist.
    /// </remarks>
    [Fact]
    public void Shape_ShouldMapToItsSolverShape() {
        PackingShape2<FltX> rectangle = Rect(30.0, 20.0).ToShape();
        RectanglePackingShape2<FltX> rectangleShape = rectangle.Should().BeOfType<RectanglePackingShape2<FltX>>().Subject;
        rectangleShape.Width.Value.ToFlt64().ToDouble().Should().Be(30.0);
        rectangleShape.Height.Value.ToFlt64().ToDouble().Should().Be(20.0);
        rectangleShape.Width.Unit.Should().Be(LengthUnits.Millimeter, "the unit must survive the conversion");

        PackingShape2<FltX> circle = Circle(7.5).ToShape();
        CirclePackingShape2<FltX> circleShape = circle.Should().BeOfType<CirclePackingShape2<FltX>>().Subject;
        circleShape.Radius.Value.ToFlt64().ToDouble().Should().Be(7.5);
    }

    /// <summary>
    /// **物料目录里没有的物料被丢掉，其余保留** / Materials absent from the catalogue are dropped and the rest kept.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 目录里有的物料**保留**，用量不变。
    /// 2. 目录里没有的**丢掉**，不报错——外部数据里多出来的物料编号是常态。
    /// 3. **没有目录时结果是空**，不是抛异常。
    ///
    /// Three paired points: a catalogued material is **kept** with its amount; an uncatalogued one is **dropped** without an error, since extra numbers in external data are normal; and
    /// **without a catalogue the result is empty** rather than throwing.
    /// </remarks>
    [Fact]
    public void QuantityItem_ShouldDropUncataloguedMaterials() {
        var known = new MaterialNo("M1");
        var unknown = new MaterialNo("M2");
        var item = new QuantityItem<FltX>(
            Id: new ItemId("I1"),
            Name: "item",
            Shape: Rect(30.0, 20.0),
            AllowRotate: true,
            Weight: Q(500.0),
            MaterialAmounts: new Dictionary<MaterialNo, ulong> { [known] = 3UL, [unknown] = 9UL });

        var catalog = new Dictionary<MaterialNo, MaterialKey> {
            [known] = new MaterialKey(known, MaterialType.RawMaterial),
        };
        ActualItem<FltX> model = item.ToModel(catalog);

        model.Id.Value.Should().Be("I1");
        model.Name.Should().Be("item");
        model.AllowRotate.Should().BeTrue();
        model.Weight.Unit.Should().Be(LengthUnits.Millimeter, "the weight keeps its unit through ToModel");
        model.MaterialAmounts.Should().HaveCount(1, "only the catalogued material survives");
        model.MaterialAmounts!.Values.Single().Should().Be(3UL);

        ActualItem<FltX> withoutCatalog = item.ToModel();
        withoutCatalog.MaterialAmounts.Should().BeEmpty("without a catalogue nothing can be resolved");
        withoutCatalog.Shape.Should().BeOfType<RectanglePackingShape2<FltX>>();
    }

    /// <summary>
    /// **旋转换掉形状本身，不只是记一个标记** / Rotation rewrites the shape itself rather than only recording a flag.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 未旋转：形状宽 30、高 20，`Rotated` 为假。
    /// 2. **旋转后：宽 20、高 30**——形状真的换了，而不是只把标记置真。
    /// 3. 坐标与货物编号照抄。
    ///
    /// Three paired points: unrotated gives width 30 and height 20 with `Rotated` false; **rotated gives width 20 and height 30** — the shape really changes rather than just the flag —
    /// and the coordinates and item id carry over.
    /// </remarks>
    [Fact]
    public void Placement_ShouldRotateTheShapeItself() {
        var placement = new QuantityLayerPlacement<FltX>(
            Shape: Rect(30.0, 20.0),
            X: Q(5.0),
            Y: Q(6.0),
            Rotated: false,
            ItemId: new ItemId("I9"));

        Placement2<FltX> upright = placement.ToModel();
        RectanglePackingShape2<FltX> uprightShape = upright.Shape.Should().BeOfType<RectanglePackingShape2<FltX>>().Subject;
        uprightShape.Width.Value.ToFlt64().ToDouble().Should().Be(30.0);
        uprightShape.Height.Value.ToFlt64().ToDouble().Should().Be(20.0);
        upright.Rotated.Should().BeFalse();
        upright.Position.X.Value.ToFlt64().ToDouble().Should().Be(5.0);
        upright.Position.X.Unit.Should().Be(LengthUnits.Millimeter, "coordinates keep their unit through ToModel");
        upright.ItemId.Should().Be("I9");

        Placement2<FltX> rotated = (placement with { Rotated = true }).ToModel();
        RectanglePackingShape2<FltX> rotatedShape = rotated.Shape.Should().BeOfType<RectanglePackingShape2<FltX>>().Subject;
        rotatedShape.Width.Value.ToFlt64().ToDouble().Should().Be(20.0, "rotation swaps the sides");
        rotatedShape.Height.Value.ToFlt64().ToDouble().Should().Be(30.0);
        rotated.Rotated.Should().BeTrue();
    }
}
