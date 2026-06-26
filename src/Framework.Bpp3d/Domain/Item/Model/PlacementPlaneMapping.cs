#nullable enable

using Fuookami.Ospf.Framework.Bpp3d.Infra;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;

/// <summary>
/// 放置平面映射 / Placement plane mapping.
/// 在不同投影平面之间转换放置。
/// Converts placements between different projective planes.
/// </summary>
public static class PlacementPlaneMapping {
    /// <summary>
    /// 计算侧面剩余空间 / Compute side remaining space.
    /// </summary>
    public static QuantityContainer2Shape<FltX>? RestSideSpace(
        Container3Shape<FltX> container,
        IReadOnlyList<QuantityPlacement3<ActualItem, FltX>> placements) {
        if (placements.Count == 0) return null;

        double maxX = 0, maxY = 0;
        foreach (QuantityPlacement3<ActualItem, FltX> p in placements) {
            double x = p.Position.X.Value.ToFlt64().ToDouble() + p.Unit.Width.Value.ToFlt64().ToDouble();
            double y = p.Position.Y.Value.ToFlt64().ToDouble() + p.Unit.Height.Value.ToFlt64().ToDouble();
            if (x > maxX) maxX = x;
            if (y > maxY) maxY = y;
        }

        double remainingLength = container.Width.Value.ToFlt64().ToDouble() - maxX;
        double remainingWidth = container.Height.Value.ToFlt64().ToDouble() - maxY;

        if (remainingLength <= 0 || remainingWidth <= 0) return null;

        return new QuantityContainer2Shape<FltX>(
            new Quantity<FltX>(new FltX(remainingLength), Fuookami.Ospf.Quantities.Unit.SIBaseUnits.Meter),
            new Quantity<FltX>(new FltX(remainingWidth), Fuookami.Ospf.Quantities.Unit.SIBaseUnits.Meter),
            new ProjectivePlane.Side());
    }

    /// <summary>
    /// 计算正面剩余空间 / Compute front remaining space.
    /// </summary>
    public static QuantityContainer2Shape<FltX>? RestFrontSpace(
        Container3Shape<FltX> container,
        IReadOnlyList<QuantityPlacement3<ActualItem, FltX>> placements) {
        if (placements.Count == 0) return null;

        double maxZ = 0, maxY = 0;
        foreach (QuantityPlacement3<ActualItem, FltX> p in placements) {
            double z = p.Position.Z.Value.ToFlt64().ToDouble() + p.Unit.Depth.Value.ToFlt64().ToDouble();
            double y = p.Position.Y.Value.ToFlt64().ToDouble() + p.Unit.Height.Value.ToFlt64().ToDouble();
            if (z > maxZ) maxZ = z;
            if (y > maxY) maxY = y;
        }

        double remainingLength = container.Depth.Value.ToFlt64().ToDouble() - maxZ;
        double remainingWidth = container.Height.Value.ToFlt64().ToDouble() - maxY;

        if (remainingLength <= 0 || remainingWidth <= 0) return null;

        return new QuantityContainer2Shape<FltX>(
            new Quantity<FltX>(new FltX(remainingLength), Fuookami.Ospf.Quantities.Unit.SIBaseUnits.Meter),
            new Quantity<FltX>(new FltX(remainingWidth), Fuookami.Ospf.Quantities.Unit.SIBaseUnits.Meter),
            new ProjectivePlane.Front());
    }
}
