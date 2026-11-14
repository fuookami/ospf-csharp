#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Framework.Bpp3d.Infra;

/// <summary>
/// 容器三维几何接口 / Container 3D geometry interface.
/// 通用三维容器形状，与具体货物类型无关。
/// Generic 3D container shape, independent of concrete item types.
/// </summary>
public interface IContainer3Geometry<V> where V : struct, IFloatingNumber<V> {
    /// <summary>宽度 / Width.</summary>
    Quantity<V> Width { get; }
    /// <summary>高度 / Height.</summary>
    Quantity<V> Height { get; }
    /// <summary>深度 / Depth.</summary>
    Quantity<V> Depth { get; }
    /// <summary>体积 / Volume.</summary>
    Quantity<V> Volume { get; }

    /// <summary>
    /// 给定的包围尺寸能否放进这个容器 / Whether the given extents fit inside this container.
    /// </summary>
    /// <param name="unitWidth">待放入的宽度 / The width to fit.</param>
    /// <param name="unitHeight">待放入的高度 / The height to fit.</param>
    /// <param name="unitDepth">待放入的深度 / The depth to fit.</param>
    /// <returns>是否放得下 / Whether it fits.</returns>
    /// <remarks>
    /// **量纲不符算"放不下"，不是"不知道"**：比较在量纲不同时返回 `null`，而 `== true` 把它归入 `false`。
    /// 这是**保守**的选择——若把 `null` 当成"可能放得下"，一次单位配错就会让一个更大的货物混进小容器，
    /// 而错误要到后续的几何校验才暴露，那时已经离配置处很远。
    ///
    /// **A dimension mismatch means "does not fit", not "unknown"**: the comparison returns `null` when the dimensions differ, and `== true` folds that into `false`.
    /// This is the **conservative** choice — treating `null` as "might fit" would let an oversized item into a small container on any unit mix-up, and the error would
    /// only surface in a later geometric check, far from where it was configured.
    /// </remarks>
    bool Enabled(Quantity<V> unitWidth, Quantity<V> unitHeight, Quantity<V> unitDepth) =>
        unitWidth.LessThanOrEqual(Width) == true
        && unitHeight.LessThanOrEqual(Height) == true
        && unitDepth.LessThanOrEqual(Depth) == true;
}

/// <summary>
/// 容量三维形状实现 / Quantity container 3D shape implementation.
/// </summary>
public sealed record QuantityContainer3Shape<V>(
    Quantity<V> Width,
    Quantity<V> Height,
    Quantity<V> Depth) : IContainer3Geometry<V>
    where V : struct, IFloatingNumber<V> {
    /// <summary>体积 / Volume.</summary>
    public Quantity<V> Volume => Width.Multiply(Height).Multiply(Depth);
}

/// <summary>
/// 容器二维几何接口 / Container 2D geometry interface.
/// </summary>
public interface IContainer2Geometry<V> where V : struct, IFloatingNumber<V> {
    /// <summary>长度 / Length.</summary>
    Quantity<V> Length { get; }
    /// <summary>宽度 / Width.</summary>
    Quantity<V> Width { get; }
    /// <summary>关联投影平面 / Associated projective plane.</summary>
    ProjectivePlane Plane { get; }
}

/// <summary>
/// 容量二维形状实现 / Quantity container 2D shape implementation.
/// </summary>
public sealed record QuantityContainer2Shape<V>(
    Quantity<V> Length,
    Quantity<V> Width,
    ProjectivePlane Plane) : IContainer2Geometry<V>
    where V : struct, IFloatingNumber<V>;
