#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
#nullable enable

using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;
/// <summary>
/// 方案（批次 + 货物需求）/ Schema (batch + item demands).
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type</typeparam>
public sealed record Schema<V>(
    string BatchNo,
    IReadOnlyList<(Item<V> Item, UInt64 Amount)> PatternedItems)
    where V : struct, IFloatingNumber<V>;

/// <summary>
/// 货物模型 / Item model.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type</typeparam>
public sealed record Item<V>(
    string Id,
    string Name,
    Quantity<V> Width,
    Quantity<V> Height,
    Quantity<V> Depth,
    Quantity<V> Weight,
    PackageAttribute PackageAttribute,
    string? BatchNo = null,
    string? Warehouse = null)
    where V : struct, IFloatingNumber<V> {
    /// <summary>体积 / Volume.</summary>
    public Quantity<V> Volume => Width.Multiply(Height).Multiply(Depth);
}
