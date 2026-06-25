#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Quantity;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain;
/// <summary>
/// 二维矩形物料项
/// 2D rectangle item.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type</typeparam>
public sealed record RectangleItem2<V>(
    /// <summary>物料标识 / Item identifier</summary>
    string Id,
    /// <summary>宽度 / Width</summary>
    Quantity<V> Width,
    /// <summary>高度 / Height</summary>
    Quantity<V> Height,
    /// <summary>是否允许旋转 / Whether rotation is allowed</summary>
    bool AllowRotate = false
) where V : struct, IFloatingNumber<V>;
