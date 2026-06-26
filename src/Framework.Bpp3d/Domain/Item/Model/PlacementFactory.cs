#nullable enable

using Fuookami.Ospf.Framework.Bpp3d.Infra;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;

/// <summary>
/// 放置工厂 / Placement factory.
/// 创建各种类型的放置对象。
/// </summary>
public static class PlacementFactory {
    /// <summary>创建货物三维放置 / Create item 3D placement.</summary>
    public static QuantityPlacement3<ActualItem, FltX> ItemPlacement3(
        ActualItem item,
        QuantityPoint3<FltX> position,
        Orientation orientation = Orientation.Upright) =>
        new(item, position, orientation);

    /// <summary>从视图创建货物三维放置 / Create item 3D placement from view.</summary>
    public static QuantityPlacement3<ActualItem, FltX> ItemPlacement3(
        ItemView view,
        QuantityPoint3<FltX> position) =>
        new(view.Item, position, view.Orientation);

    /// <summary>创建 BinLayer 三维放置 / Create BinLayer 3D placement.</summary>
    public static QuantityPlacement3<BinLayer, FltX> BinLayerPlacement(
        BinLayer layer,
        QuantityPoint3<FltX> position,
        Orientation orientation = Orientation.Upright) =>
        new(layer, position, orientation);

    /// <summary>创建块三维放置 / Create block 3D placement.</summary>
    public static QuantityPlacement3<Block, FltX> BlockPlacement(
        Block block,
        QuantityPoint3<FltX> position,
        Orientation orientation = Orientation.Upright) =>
        new(block, position, orientation);

    /// <summary>检查两个三维放置是否在底部重叠 / Check if two 3D placements overlap on bottom.</summary>
    public static bool OverlappedOnBottom(
        QuantityPlacement3<ActualItem, FltX> a,
        QuantityPlacement3<ActualItem, FltX> b) {
        double aMinX = a.Position.X.Value.ToFlt64().ToDouble();
        double aMaxX = aMinX + a.Unit.Width.Value.ToFlt64().ToDouble();
        double aMinZ = a.Position.Z.Value.ToFlt64().ToDouble();
        double aMaxZ = aMinZ + a.Unit.Depth.Value.ToFlt64().ToDouble();

        double bMinX = b.Position.X.Value.ToFlt64().ToDouble();
        double bMaxX = bMinX + b.Unit.Width.Value.ToFlt64().ToDouble();
        double bMinZ = b.Position.Z.Value.ToFlt64().ToDouble();
        double bMaxZ = bMinZ + b.Unit.Depth.Value.ToFlt64().ToDouble();

        return aMinX < bMaxX && aMaxX > bMinX && aMinZ < bMaxZ && aMaxZ > bMinZ;
    }
}
