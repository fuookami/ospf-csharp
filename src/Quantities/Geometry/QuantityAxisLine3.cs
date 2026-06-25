#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Geometry;
using Fuookami.Ospf.Quantities.Quantity;

namespace Fuookami.Ospf.Quantities.Geometry;

/// <summary>
/// 三维轴对齐线段 / 3D axis-aligned line segment.
/// 沿某一坐标轴方向的线段。
/// A line segment along a coordinate axis direction.
/// </summary>
public sealed record QuantityAxisLine3<V>(
    Axis3 Axis,
    Quantity<V> From,
    Quantity<V> To
) where V : struct, IFloatingNumber<V>;
