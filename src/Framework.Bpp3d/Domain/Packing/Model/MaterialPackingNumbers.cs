#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
#nullable enable

using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.Packing.Model;
/// <summary>
/// 物料装箱数量 / Material packing numbers.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type</typeparam>
public sealed record MaterialPackingNumbers<V>(
    string MaterialNo,
    ulong PackedAmount,
    ulong TotalAmount)
    where V : struct, IFloatingNumber<V>;
