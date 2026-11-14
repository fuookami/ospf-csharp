// 二维装载形状抽象 / 2D packing shape abstractions.
// 与固定 Kotlin 基线 `geometry2d/PackingShape2.kt` 对应。对应 BPP3D 的 `PackingShape3`（Cuboid + Cylinder），
// 二维侧提供矩形与圆形两种形状。
// Mirrors the pinned Kotlin baseline's `geometry2d/PackingShape2.kt`: BPP3D's `PackingShape3` (Cuboid + Cylinder)
// counterpart for 2D, with rectangle and circle shapes.
//
// 面积口径：`Area` 是**实际占用面积**（圆为 πr²，矩形为 w·h），`BoundingArea` 是**外接矩形面积**。两者在矩形上相等、
// 在圆上不等——混用会把圆形件的利用率算高。
// The area convention: `Area` is the **actual occupied area** (πr² for circles, w·h for rectangles) while
// `BoundingArea` is the **enclosing rectangle area**. They coincide for rectangles and differ for circles; mixing
// them up inflates utilization for circular items.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Geometry;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain;

/// <summary>
/// 装载形状类型 / Packing shape type.
/// </summary>
public enum PackingShape2Type {
    /// <summary>矩形件 / Rectangle item.</summary>
    Rectangle,

    /// <summary>圆形件 / Circular item.</summary>
    Circle,
}

/// <summary>
/// 二维装载形状 / 2D packing shape.
/// </summary>
/// <remarks>
/// 声明为抽象 record 而非接口 + 实现，使调用方可以穷尽分支而不需要 <c>else</c>——与基线的 sealed interface 同义。
/// Declared as an abstract record rather than an interface plus implementations so callers can exhaust the branches
/// without an <c>else</c>, matching the baseline's sealed interface.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public abstract record PackingShape2<V>
    where V : struct, IFloatingNumber<V> {
    /// <summary>形状类型 / Shape type.</summary>
    public abstract PackingShape2Type ShapeType { get; }

    /// <summary>包围宽度 / Bounding width.</summary>
    public abstract Quantity<V> BoundingWidth { get; }

    /// <summary>包围高度 / Bounding height.</summary>
    public abstract Quantity<V> BoundingHeight { get; }

    /// <summary>实际占用面积 / Actual occupied area.</summary>
    public abstract Quantity<V> Area { get; }

    /// <summary>外接矩形面积 / Enclosing rectangle area.</summary>
    public abstract Quantity<V> BoundingArea { get; }

    /// <summary>
    /// 转换为基础二维几何投影 / Convert to the base 2D geometry projection.
    /// </summary>
    /// <returns>二维几何投影 / 2D geometry projection.</returns>
    public abstract IQuantityProjection2<V> Geometry();

    /// <summary>
    /// 生成旋转 90 度后的形状；圆形件旋转后不变 / Rotate by 90 degrees; circle shapes are unchanged.
    /// </summary>
    /// <returns>旋转后的形状 / Rotated shape.</returns>
    public abstract PackingShape2<V> Rotated();
}

/// <summary>
/// 矩形装载形状 / Rectangle packing shape.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="Width">宽度 / Width.</param>
/// <param name="Height">高度 / Height.</param>
public sealed record RectanglePackingShape2<V>(Quantity<V> Width, Quantity<V> Height)
    : PackingShape2<V>
    where V : struct, IFloatingNumber<V> {
    /// <inheritdoc/>
    public override PackingShape2Type ShapeType => PackingShape2Type.Rectangle;

    /// <inheritdoc/>
    public override Quantity<V> BoundingWidth => Width;

    /// <inheritdoc/>
    public override Quantity<V> BoundingHeight => Height;

    /// <inheritdoc/>
    public override Quantity<V> Area => QuantityOps.QuantityProduct(Width, Height);

    /// <inheritdoc/>
    public override Quantity<V> BoundingArea => Area;

    /// <inheritdoc/>
    public override IQuantityProjection2<V> Geometry() => new QuantityRectangle2<V>(Width, Height);

    /// <inheritdoc/>
    public override PackingShape2<V> Rotated() => new RectanglePackingShape2<V>(Height, Width);

    /// <summary>
    /// 判断旋转 90 度是否得到不同形状 / Check whether a 90-degree rotation produces a different shape.
    /// </summary>
    /// <returns>宽与高是否不等 / Whether width and height differ.</returns>
    public bool IsRotatable() =>
        QuantityOps.OrdSafe(Width, Height, "width").Value is not Order.Equal;
}

/// <summary>
/// 圆形装载形状 / Circle packing shape.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="Radius">半径 / Radius.</param>
public sealed record CirclePackingShape2<V>(Quantity<V> Radius)
    : PackingShape2<V>
    where V : struct, IFloatingNumber<V> {
    /// <inheritdoc/>
    public override PackingShape2Type ShapeType => PackingShape2Type.Circle;

    /// <summary>直径 / Diameter.</summary>
    public Quantity<V> Diameter => new(Radius.Value.Plus(Radius.Value), Radius.Unit);

    /// <inheritdoc/>
    public override Quantity<V> BoundingWidth => Diameter;

    /// <inheritdoc/>
    public override Quantity<V> BoundingHeight => Diameter;

    /// <inheritdoc/>
    public override Quantity<V> Area => QuantityOps.QuantityProduct(
        QuantityOps.QuantityProduct(Radius, Radius),
        Radius.Value.Constants.Pi);

    /// <inheritdoc/>
    public override Quantity<V> BoundingArea =>
        QuantityOps.QuantityProduct(Diameter, Diameter);

    /// <inheritdoc/>
    public override IQuantityProjection2<V> Geometry() => new QuantityCircle2<V>(Radius);

    /// <inheritdoc/>
    public override PackingShape2<V> Rotated() => this;
}

/// <summary>
/// 带旋转标记的候选形状 / Candidate shape carrying its rotation flag.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="Shape">候选形状 / Candidate shape.</param>
/// <param name="Rotated">是否旋转 90 度 / Whether rotated by 90 degrees.</param>
public sealed record Shape2Candidate<V>(PackingShape2<V> Shape, bool Rotated)
    where V : struct, IFloatingNumber<V>;

/// <summary>
/// 二维装载形状扩展 / 2D packing shape extensions.
/// </summary>
public static class PackingShape2Extensions {
    /// <summary>
    /// 判断形状是否为圆形件 / Check whether a shape is a circle.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="shape">装载形状 / Packing shape.</param>
    /// <returns>是否圆形件 / Whether it is a circle.</returns>
    public static bool IsCircle<V>(this PackingShape2<V> shape)
        where V : struct, IFloatingNumber<V> {
        ArgumentNullException.ThrowIfNull(shape);
        return shape.ShapeType == PackingShape2Type.Circle;
    }

    /// <summary>
    /// 判断形状是否为矩形件 / Check whether a shape is a rectangle.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="shape">装载形状 / Packing shape.</param>
    /// <returns>是否矩形件 / Whether it is a rectangle.</returns>
    public static bool IsRectangle<V>(this PackingShape2<V> shape)
        where V : struct, IFloatingNumber<V> {
        ArgumentNullException.ThrowIfNull(shape);
        return shape.ShapeType == PackingShape2Type.Rectangle;
    }

    /// <summary>
    /// 按是否允许旋转返回候选形状列表 / Candidate shapes honouring the rotation allowance.
    /// </summary>
    /// <remarks>
    /// 圆形件与**正方形**矩形只返回自身：旋转正方形会得到一个坐标不同但几何等价的形状，多出来的候选只会
    /// 让搜索空间无谓翻倍。
    /// Circle shapes and **square** rectangles return only themselves: rotating a square yields a differently
    /// parameterized but geometrically identical shape, and the extra candidate would only double the search space.
    /// </remarks>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="shape">装载形状 / Packing shape.</param>
    /// <param name="allowRotate">是否允许旋转 / Whether rotation is allowed.</param>
    /// <returns>候选形状列表 / Candidate shape list.</returns>
    public static IReadOnlyList<Shape2Candidate<V>> CandidateShapes<V>(
        this PackingShape2<V> shape,
        bool allowRotate = true)
        where V : struct, IFloatingNumber<V> {
        ArgumentNullException.ThrowIfNull(shape);
        var upright = new Shape2Candidate<V>(shape, false);
        if (!allowRotate) {
            return new[] { upright };
        }
        if (shape is not RectanglePackingShape2<V> rectangle || !rectangle.IsRotatable()) {
            return new[] { upright };
        }
        return new[] {
            upright,
            new Shape2Candidate<V>(rectangle.Rotated(), true),
        };
    }
}

/// <summary>
/// 二维装载形状工厂 / 2D packing shape factories.
/// </summary>
public static class PackingShape2Factory {
    /// <summary>
    /// 生成矩形形状，宽高必须为正 / Build a rectangle shape; width and height must be positive.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="width">宽度 / Width.</param>
    /// <param name="height">高度 / Height.</param>
    /// <returns>矩形形状或失败 / The rectangle, or a failure.</returns>
    public static Result<RectanglePackingShape2<V>, ErrorCode, Error<ErrorCode>>
        RectangleShape2<V>(Quantity<V> width, Quantity<V> height)
        where V : struct, IFloatingNumber<V> {
        if (!IsPositive(width, "width") || !IsPositive(height, "height")) {
            return Results.Failed<RectanglePackingShape2<V>>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "宽高必须为正 / Width and height must be positive"));
        }
        return Results.Ok(new RectanglePackingShape2<V>(width, height));
    }

    /// <summary>
    /// 生成圆形形状，半径必须为正 / Build a circle shape; the radius must be positive.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="radius">半径 / Radius.</param>
    /// <returns>圆形形状或失败 / The circle, or a failure.</returns>
    public static Result<CirclePackingShape2<V>, ErrorCode, Error<ErrorCode>>
        CircleShape2<V>(Quantity<V> radius)
        where V : struct, IFloatingNumber<V> {
        if (!IsPositive(radius, "radius")) {
            return Results.Failed<CirclePackingShape2<V>>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "半径必须为正 / Radius must be positive"));
        }
        return Results.Ok(new CirclePackingShape2<V>(radius));
    }

    /// <summary>
    /// 判断量值是否严格为正 / Check whether a quantity is strictly positive.
    /// </summary>
    /// <remarks>
    /// 量纲不可比时 `OrdSafe` 会失败，此处**按非正处理**：拿一个无法比较的尺寸去装箱没有意义，
    /// 让它通过只会把错误推迟到排样阶段。
    /// An incomparable dimension makes `OrdSafe` fail and is treated as non-positive: packing an incomparable size
    /// is meaningless, and letting it through only defers the error to the layout stage.
    /// </remarks>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="value">量值 / Quantity.</param>
    /// <param name="axis">轴名，用于错误信息 / Axis name, for the error message.</param>
    /// <returns>是否严格为正 / Whether it is strictly positive.</returns>
    private static bool IsPositive<V>(Quantity<V> value, string axis)
        where V : struct, IFloatingNumber<V> {
        Result<Order, ErrorCode, Error<ErrorCode>> comparison =
            QuantityOps.OrdSafe(value, QuantityOps.QuantityZeroOf(value), axis);
        return comparison.IsOk && comparison.Value is Order.Greater;
    }
}
