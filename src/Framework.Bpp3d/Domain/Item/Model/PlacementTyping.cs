#nullable enable

using Fuookami.Ospf.Framework.Bpp3d.Infra;
using Fuookami.Ospf.Math.Algebra.Number;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;

/// <summary>
/// 放置类型工具 / Placement typing utilities.
/// </summary>
public static class PlacementTyping {
    /// <summary>
    /// 尝试将通用放置转换为货物放置 / Try converting generic placement to item placement.
    /// </summary>
    public static QuantityPlacement3<ActualItem, FltX>? ToItemPlacementOrNull<T>(
        QuantityPlacement3<T, FltX> placement) where T : class {
        if (placement.Unit is ActualItem item) {
            return new QuantityPlacement3<ActualItem, FltX>(item, placement.Position, placement.Orientation);
        }
        return null;
    }

    /// <summary>获取顶部货物放置 / Get top item placements.</summary>
    public static IReadOnlyList<QuantityPlacement3<ActualItem, FltX>> TopItemPlacements(
        IReadOnlyList<QuantityPlacement3<ActualItem, FltX>> placements) {
        if (placements.Count == 0) return System.Array.Empty<QuantityPlacement3<ActualItem, FltX>>();
        double maxY = placements.Max(p =>
            p.Position.Y.Value.ToFlt64().ToDouble() + p.Unit.Height.Value.ToFlt64().ToDouble());
        return placements.Where(p =>
            global::System.Math.Abs(
                p.Position.Y.Value.ToFlt64().ToDouble() + p.Unit.Height.Value.ToFlt64().ToDouble() - maxY) < 1e-7
        ).ToList();
    }

    /// <summary>获取底部货物放置 / Get bottom item placements.</summary>
    public static IReadOnlyList<QuantityPlacement3<ActualItem, FltX>> BottomItemPlacements(
        IReadOnlyList<QuantityPlacement3<ActualItem, FltX>> placements) {
        if (placements.Count == 0) return System.Array.Empty<QuantityPlacement3<ActualItem, FltX>>();
        double minY = placements.Min(p => p.Position.Y.Value.ToFlt64().ToDouble());
        return placements.Where(p =>
            global::System.Math.Abs(p.Position.Y.Value.ToFlt64().ToDouble() - minY) < 1e-7
        ).ToList();
    }
}
