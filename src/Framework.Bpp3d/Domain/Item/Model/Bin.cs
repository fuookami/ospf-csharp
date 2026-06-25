#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
#nullable enable

using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;
/// <summary>
/// 箱型 / Bin type.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type</typeparam>
public sealed record BinType<V>(
    string TypeCode,
    Quantity<V> Width,
    Quantity<V> Height,
    Quantity<V> Depth,
    Quantity<V> Capacity,
    bool IsMain = false)
    where V : struct, IFloatingNumber<V> {
    /// <summary>体积 / Volume.</summary>
    public Quantity<V> Volume => Width.Multiply(Height).Multiply(Depth);
}

/// <summary>
/// 块模型 / Block model.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type</typeparam>
public sealed record Block<V>(
    string Id,
    Quantity<V> Width,
    Quantity<V> Height,
    Quantity<V> Depth,
    Quantity<V> Weight)
    where V : struct, IFloatingNumber<V>;

/// <summary>
/// 层模型 / Layer model.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type</typeparam>
public sealed record Layer<V>(
    string Id,
    Quantity<V> Depth,
    Quantity<V> Weight)
    where V : struct, IFloatingNumber<V>;
