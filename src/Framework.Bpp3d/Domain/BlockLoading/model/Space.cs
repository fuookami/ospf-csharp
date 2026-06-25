#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.BlockLoading.Model
{
    /// <summary>
    /// 空间模型 / Space model.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type</typeparam>
    public sealed record Space<V>(
        Quantity<V> Width,
        Quantity<V> Height,
        Quantity<V> Depth)
        where V : struct, IFloatingNumber<V>
    {
        /// <summary>体积 / Volume.</summary>
        public Quantity<V> Volume => Width.Multiply(Height).Multiply(Depth);
    }
}
