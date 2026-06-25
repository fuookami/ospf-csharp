#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Quantity;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain;
/// <summary>
/// 二维放置需求
/// 2D placement need.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type</typeparam>
/// <param name="X">X 坐标 / X coordinate</param>
/// <param name="Y">Y 坐标 / Y coordinate</param>
/// <param name="Projection">投影需求 / Projection need</param>
public sealed record Placement2Need<V>(
    Quantity<V> X,
    Quantity<V> Y,
    Projection2Need<V> Projection
) where V : struct, IFloatingNumber<V> {
    /// <summary>最大 X 坐标 / Maximum X coordinate</summary>
    public Quantity<V> MaxX => QuantityArithmetic.Plus(X, Projection.Width);

    /// <summary>最大 Y 坐标 / Maximum Y coordinate</summary>
    public Quantity<V> MaxY => QuantityArithmetic.Plus(Y, Projection.Height);

    /// <summary>
    /// 转换为盒体需求 / Convert to box need.
    /// </summary>
    public Box2Need<V> ToBox2Need() {
        return new Box2Need<V>(
            MinX: X,
            MinY: Y,
            MaxX: MaxX,
            MaxY: MaxY
        );
    }
}
