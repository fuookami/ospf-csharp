#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;

namespace Fuookami.Ospf.Quantities.Geometry;

/// <summary>
/// 三维形状接口 / 3D shape interface.
/// 所有三维形状的公共契约，提供最小包围长方体。
/// Common contract for all 3D shapes, providing the minimum bounding cuboid.
/// </summary>
/// <typeparam name="V">数值类型 / Number type.</typeparam>
public interface IQuantityShape3<V>
    where V : struct, IFloatingNumber<V> {
    /// <summary>最小包围长方体 / Minimum bounding cuboid.</summary>
    QuantityCuboid3<V> BoundingCuboid { get; }
}
