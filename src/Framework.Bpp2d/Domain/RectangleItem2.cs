#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Quantity;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain;
/// <summary>
/// 二维矩形物料项
/// 2D rectangle item.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type</typeparam>
/// <param name="Id">物料标识 / Item identifier</param>
/// <param name="Width">宽度 / Width</param>
/// <param name="Height">高度 / Height</param>
/// <param name="AllowRotate">是否允许旋转 / Whether rotation is allowed</param>
public sealed record RectangleItem2<V>(
    string Id,
    Quantity<V> Width,
    Quantity<V> Height,
    bool AllowRotate = false
) where V : struct, IFloatingNumber<V>;
