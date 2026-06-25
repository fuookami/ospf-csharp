#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Quantity;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain
{
    /// <summary>
    /// 二维板材
    /// 2D sheet.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type</typeparam>
    public sealed record Sheet2<V>(
        /// <summary>板材标识 / Sheet identifier</summary>
        string Id,
        /// <summary>宽度 / Width</summary>
        Quantity<V> Width,
        /// <summary>高度 / Height</summary>
        Quantity<V> Height
    ) where V : struct, IFloatingNumber<V>;
}
