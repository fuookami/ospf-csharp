#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Quantity;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain;
/// <summary>
/// 二维板材
/// 2D sheet.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type</typeparam>
/// <param name="Id">板材标识 / Sheet identifier</param>
/// <param name="Width">宽度 / Width</param>
/// <param name="Height">高度 / Height</param>
public sealed record Sheet2<V>(
    string Id,
    Quantity<V> Width,
    Quantity<V> Height
) where V : struct, IFloatingNumber<V>;
