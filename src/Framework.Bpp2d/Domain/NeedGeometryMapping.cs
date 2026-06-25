#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Geometry;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain;
/// <summary>
/// bpp2d 业务模型到 quantity-geometry 的稳定适配层；不依赖 BPP3D 包。
/// Stable adapter from bpp2d domain models to quantity-geometry; no dependency on BPP3D packages.
/// </summary>
internal static class NeedGeometryMapping {
    /// <summary>
    /// 将投影需求转换为几何投影 / Convert projection need to geometry projection.
    /// </summary>
    public static QuantityRectangle2<V> ToGeometryProjection2<V>(this Projection2Need<V> projection)
        where V : struct, IFloatingNumber<V> {
        return new QuantityRectangle2<V>(
            Width: projection.Width,
            Height: projection.Height);
    }

    /// <summary>
    /// 将放置需求转换为几何放置 / Convert placement need to geometry placement.
    /// </summary>
    public static QuantityPlacement2<V> ToGeometryPlacement2<V>(this Placement2Need<V> placement)
        where V : struct, IFloatingNumber<V> {
        return new QuantityPlacement2<V>(
            X: placement.X,
            Y: placement.Y,
            Shape: placement.Projection.ToGeometryProjection2());
    }

    /// <summary>
    /// 将盒体需求转换为几何盒体 / Convert box need to geometry box.
    /// </summary>
    public static QuantityBox2<V> ToGeometryBox2<V>(this Box2Need<V> box)
        where V : struct, IFloatingNumber<V> {
        return new QuantityBox2<V>(
            X: box.MinX,
            Y: box.MinY,
            Shape: new QuantityRectangle2<V>(
                Width: box.Width,
                Height: box.Height));
    }
}
