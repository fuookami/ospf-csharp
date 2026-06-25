#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Quantity;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain;
/// <summary>
/// 已规划的矩形放置
/// Planned rectangle placement.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type</typeparam>
public sealed record PlannedRectangle2<V>(
    /// <summary>矩形物料项 / Rectangle item</summary>
    RectangleItem2<V> Item,
    /// <summary>X 坐标 / X coordinate</summary>
    Quantity<V> X,
    /// <summary>Y 坐标 / Y coordinate</summary>
    Quantity<V> Y,
    /// <summary>是否已旋转 / Whether rotated</summary>
    bool Rotated = false
) where V : struct, IFloatingNumber<V> {
    /// <summary>
    /// 转换为放置需求 / Convert to placement need.
    /// </summary>
    public Placement2Need<V> ToPlacement2Need() {
        Projection2Need<V> projection = Rotated && Item.AllowRotate
            ? new Projection2Need<V>(
                Width: Item.Height,
                Height: Item.Width)
            : new Projection2Need<V>(
                Width: Item.Width,
                Height: Item.Height);

        return new Placement2Need<V>(
            X: X,
            Y: Y,
            Projection: projection);
    }

    /// <summary>
    /// 转换为盒体需求 / Convert to box need.
    /// </summary>
    public Box2Need<V> ToBox2Need() => ToPlacement2Need().ToBox2Need();
}
