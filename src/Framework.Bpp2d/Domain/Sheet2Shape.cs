// 二维板材形状与容器几何 / 2D sheet shape and container geometry.
// 与固定 Kotlin 基线 `geometry2d/Sheet2Shape.kt` 对应。板材是二维装箱的容器，对应 BPP3D 的 `Container3Shape`；
// 二维容器只需宽、高与可用面积。
// Mirrors the pinned Kotlin baseline's `geometry2d/Sheet2Shape.kt`: a sheet is the 2D packing container mirroring
// BPP3D `Container3Shape`, needing only width, height and usable area.

using System;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Geometry;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain;

/// <summary>
/// 二维板材几何 / 2D sheet geometry.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public interface ISheet2Geometry<V>
    where V : struct, IFloatingNumber<V> {
    /// <summary>板材宽度 / Sheet width.</summary>
    Quantity<V> Width { get; }

    /// <summary>板材高度 / Sheet height.</summary>
    Quantity<V> Height { get; }

    /// <summary>板材面积 / Sheet area.</summary>
    Quantity<V> Area { get; }

    /// <summary>
    /// 判断尺寸为 <c>boxWidth × boxHeight</c> 的包围盒能否放入板材 / Check whether a box of that size fits.
    /// </summary>
    /// <param name="boxWidth">包围盒宽度 / Bounding-box width.</param>
    /// <param name="boxHeight">包围盒高度 / Bounding-box height.</param>
    /// <returns>是否可容纳或量纲错误 / Whether it fits, or a dimension error.</returns>
    Result<bool, ErrorCode, Error<ErrorCode>> Enabled(
        Quantity<V> boxWidth,
        Quantity<V> boxHeight);

    /// <summary>
    /// 判断给定装载形状能否放入板材（按包围尺寸） / Check whether a packing shape fits by bounding size.
    /// </summary>
    /// <param name="shape">装载形状 / Packing shape.</param>
    /// <returns>是否可容纳或量纲错误 / Whether it fits, or a dimension error.</returns>
    Result<bool, ErrorCode, Error<ErrorCode>> Enabled(PackingShape2<V> shape);

    /// <summary>
    /// 生成以原点为左下角的板材包围盒 / Build the sheet bounding box anchored at the origin.
    /// </summary>
    /// <returns>板材包围盒 / Sheet bounding box.</returns>
    Box2Geometry<V> OriginBox();
}

/// <summary>
/// 二维板材形状实现 / 2D sheet shape implementation.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="Width">板材宽度 / Sheet width.</param>
/// <param name="Height">板材高度 / Sheet height.</param>
public sealed record Sheet2Shape<V>(Quantity<V> Width, Quantity<V> Height)
    : ISheet2Geometry<V>
    where V : struct, IFloatingNumber<V> {
    /// <summary>
    /// 板材面积 / Sheet area.
    /// </summary>
    /// <remarks>
    /// 基线与 C# 常用写法都允许直接在实现类型上取 `area`／`originBox`。C# 若用"默认接口成员 + 同名公开成员"
    /// 的写法，接口调用会分派到类成员从而**无限递归**，因此这里把两者都声明为接口成员、由实现类型给出唯一实现。
    ///
    /// Both the baseline and ordinary C# usage read `area`/`originBox` on the concrete type. Combining a
    /// default interface member with a same-named public member makes the interface call dispatch back to the class
    /// member and **recurse forever**, so both are plain interface members with a single implementation here.
    /// </remarks>
    public Quantity<V> Area => QuantityOps.QuantityProduct(Width, Height);

    /// <summary>
    /// 生成以原点为左下角的板材包围盒 / Build the sheet bounding box anchored at the origin.
    /// </summary>
    /// <returns>板材包围盒 / Sheet bounding box.</returns>
    public Box2Geometry<V> OriginBox() => new(
        QuantityCompare.ZeroOf(Width),
        QuantityCompare.ZeroOf(Height),
        new QuantityRectangle2<V>(Width, Height));

    /// <summary>
    /// 判断尺寸能否放入 / Check whether a size fits.
    /// </summary>
    /// <remarks>
    /// **按包围尺寸判定**：圆形件用其外接正方形判定，因此一个直径恰好等于板材宽高的圆可以放入，而直径更大的圆
    /// 即使"面积更小"也不行——排样关心的是能否摆下，不是面积比较。
    ///
    /// **Judged by bounding size**: a circle is measured by its enclosing square, so a circle whose diameter equals
    /// the sheet dimensions fits while a larger one does not even if its area is smaller — packing cares about
    /// whether it physically fits, not about comparing areas.
    /// </remarks>
    /// <param name="boxWidth">包围盒宽度 / Bounding-box width.</param>
    /// <param name="boxHeight">包围盒高度 / Bounding-box height.</param>
    /// <returns>是否可容纳或量纲错误 / Whether it fits, or a dimension error.</returns>
    public Result<bool, ErrorCode, Error<ErrorCode>> Enabled(
        Quantity<V> boxWidth,
        Quantity<V> boxHeight) =>
        Results.Ok(
            QuantityCompare.AtMost(boxWidth, Width, "boxWidth") &&
            QuantityCompare.AtMost(boxHeight, Height, "boxHeight"));

    /// <summary>
    /// 判断装载形状能否放入 / Check whether a packing shape fits.
    /// </summary>
    /// <param name="shape">装载形状 / Packing shape.</param>
    /// <returns>是否可容纳或量纲错误 / Whether it fits, or a dimension error.</returns>
    public Result<bool, ErrorCode, Error<ErrorCode>> Enabled(PackingShape2<V> shape) {
        ArgumentNullException.ThrowIfNull(shape);
        return Enabled(shape.BoundingWidth, shape.BoundingHeight);
    }
}

/// <summary>
/// 二维板材形状工厂 / 2D sheet shape factory.
/// </summary>
public static class Sheet2ShapeFactory {
    /// <summary>
    /// 由宽高构造板材形状，宽高必须为正 / Build a sheet shape; width and height must be positive.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="width">板材宽度 / Sheet width.</param>
    /// <param name="height">板材高度 / Sheet height.</param>
    /// <returns>板材形状或失败 / The sheet, or a failure.</returns>
    public static Result<Sheet2Shape<V>, ErrorCode, Error<ErrorCode>> Sheet2Shape<V>(
        Quantity<V> width,
        Quantity<V> height)
        where V : struct, IFloatingNumber<V> {
        Quantity<V> zero = QuantityCompare.ZeroOf(width);
        if (!QuantityCompare.StrictlyGreater(width, zero, "width") ||
            !QuantityCompare.StrictlyGreater(height, QuantityCompare.ZeroOf(height), "height")) {
            return Results.Failed<Sheet2Shape<V>>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "板材宽高必须为正 / Sheet width and height must be positive"));
        }
        return Results.Ok(new Sheet2Shape<V>(width, height));
    }
}
