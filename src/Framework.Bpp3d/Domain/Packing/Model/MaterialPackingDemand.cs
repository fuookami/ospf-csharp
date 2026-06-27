#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.Packing.Model;

/// <summary>
/// 装箱需求 / Packing demand.
/// </summary>
/// <param name="Material">物料 / Material</param>
/// <param name="Amount">数量 / Amount</param>
/// <param name="Weight">重量 / Weight</param>
public sealed record MaterialPackingDemand(
    Material<FltX>? Material = null,
    ulong Amount = 0,
    Quantity<FltX>? Weight = null);
