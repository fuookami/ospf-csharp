#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.Packing.Model;
/// <summary>
/// 物料属性 / Material attribute.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type</typeparam>
public sealed record MaterialAttribute<V>(
    string MaterialNo,
    Quantity<V> Weight,
    ulong Amount)
    where V : struct, IFloatingNumber<V>;
