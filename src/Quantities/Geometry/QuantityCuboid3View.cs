#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Geometry;
using Fuookami.Ospf.Quantities.Quantity;

namespace Fuookami.Ospf.Quantities.Geometry;

/// <summary>
/// 三维长方体视图 / 3D cuboid view.
/// 通过轴置换对原始长方体进行视角变换，提供变换后的尺寸。
/// Transforms an original cuboid through axis permutation, providing transformed dimensions.
/// </summary>
public sealed record QuantityCuboid3View<V>(
    QuantityCuboid3<V> Origin,
    AxisPermutation3? Permutation = null
) where V : struct, IFloatingNumber<V> {
    /// <summary>实际置换 / Effective permutation.</summary>
    public AxisPermutation3 EffectivePermutation => Permutation ?? AxisPermutation3.XYZ;

    /// <summary>置换后的长方体 / Permuted cuboid.</summary>
    public QuantityCuboid3<V> Cuboid => Origin.Permute(EffectivePermutation);

    /// <summary>视图宽度 / View width.</summary>
    public Quantity<V> Width => Cuboid.Width;

    /// <summary>视图高度 / View height.</summary>
    public Quantity<V> Height => Cuboid.Height;

    /// <summary>视图深度 / View depth.</summary>
    public Quantity<V> Depth => Cuboid.Depth;
}
