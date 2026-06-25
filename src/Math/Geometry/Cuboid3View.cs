#nullable enable
using Fuookami.Ospf.Math.Algebra.Concept;

namespace Fuookami.Ospf.Math.Geometry;

/// <summary>
/// 长方体轴置换视图 / Axis-permuted view of a cuboid.
/// </summary>
public sealed record Cuboid3View<V> where V : struct, IFloatingNumber<V>
{
    /// <summary>原始长方体 / Origin cuboid.</summary>
    public Cuboid3<V> Origin { get; }

    /// <summary>置换 / Permutation.</summary>
    public AxisPermutation3 Permutation { get; }

    public Cuboid3View(Cuboid3<V> origin, AxisPermutation3? permutation = null)
    {
        Origin = origin;
        Permutation = permutation ?? AxisPermutation3.XYZ;
    }

    /// <summary>置换后的长方体 / Permuted cuboid.</summary>
    public Cuboid3<V> Cuboid => Permutation.Apply(Origin);

    /// <summary>宽度 / Width.</summary>
    public V Width => Cuboid.Width;

    /// <summary>高度 / Height.</summary>
    public V Height => Cuboid.Height;

    /// <summary>深度 / Depth.</summary>
    public V Depth => Cuboid.Depth;
}
