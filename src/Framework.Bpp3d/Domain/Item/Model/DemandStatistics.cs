#nullable enable

using Fuookami.Ospf.Framework.Bpp3d.Infra;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;

/// <summary>
/// BPP3D 需求值（密封接口）/ BPP3D demand value (sealed interface).
/// </summary>
public abstract record Bpp3dDemandValue {
    /// <summary>数量需求 / Amount demand.</summary>
    public sealed record Amount(UInt64 Value) : Bpp3dDemandValue;

    /// <summary>重量需求 / Weight demand.</summary>
    public sealed record Weight(Quantity<FltX> Value) : Bpp3dDemandValue;
}

/// <summary>
/// BPP3D 需求统计扩展 / BPP3D demand statistics extensions.
/// </summary>
public static class DemandStatisticsExtensions {
    /// <summary>创建零重量需求 / Create zero weight demand.</summary>
    public static Bpp3dDemandValue NoWeightDemandValue() =>
        new Bpp3dDemandValue.Weight(new Quantity<FltX>(FltX.Zero, SIBaseUnits.Kilogram));

    /// <summary>合并两个需求值 / Merge two demand values.</summary>
    public static Bpp3dDemandValue Merge(Bpp3dDemandValue left, Bpp3dDemandValue right) {
        if (left is Bpp3dDemandValue.Amount la && right is Bpp3dDemandValue.Amount ra) {
            return new Bpp3dDemandValue.Amount(new UInt64((ulong)(la.Value.ToFlt64().ToDouble() + ra.Value.ToFlt64().ToDouble())));
        }
        if (left is Bpp3dDemandValue.Weight lw && right is Bpp3dDemandValue.Weight rw) {
            return new Bpp3dDemandValue.Weight(new Quantity<FltX>(
                new FltX(lw.Value.Value.ToFlt64().ToDouble() + rw.Value.Value.ToFlt64().ToDouble()),
                lw.Value.Unit));
        }
        return left;
    }

    /// <summary>缩放需求值 / Scale demand value.</summary>
    public static Bpp3dDemandValue Scale(Bpp3dDemandValue demand, ulong factor) {
        if (demand is Bpp3dDemandValue.Amount a) {
            return new Bpp3dDemandValue.Amount(new UInt64((ulong)(a.Value.ToFlt64().ToDouble() * factor)));
        }
        if (demand is Bpp3dDemandValue.Weight w) {
            return new Bpp3dDemandValue.Weight(new Quantity<FltX>(
                new FltX(w.Value.Value.ToFlt64().ToDouble() * factor),
                w.Value.Unit));
        }
        return demand;
    }
}
