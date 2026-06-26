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
