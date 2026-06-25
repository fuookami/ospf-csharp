#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.LayerAssignment.Model
{
    /// <summary>
    /// 容量接口 / Capacity interface.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type</typeparam>
    public interface ICapacity<V> where V : struct, IFloatingNumber<V>
    {
        /// <summary>容量值 / Capacity value.</summary>
        Quantity<V> Value { get; }
    }

    /// <summary>
    /// 容量模型组件 / Capacity model component.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type</typeparam>
    public sealed record Capacity<V>(Quantity<V> Value) : ICapacity<V>
        where V : struct, IFloatingNumber<V>;
}
