#nullable enable
using Fuookami.Ospf.Math.Algebra.Concept;

namespace Fuookami.Ospf.Math.Geometry;

/// <summary>
/// 三维形状接口 / 3D shape interface.
/// </summary>
/// <typeparam name="V">数值类型 / Number type.</typeparam>
public interface IShape3<V> where V : struct, IFloatingNumber<V>
{
    /// <summary>包围长方体 / Bounding cuboid.</summary>
    Cuboid3<V> BoundingCuboid { get; }
}
