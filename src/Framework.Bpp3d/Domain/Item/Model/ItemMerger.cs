#nullable enable

using Fuookami.Ospf.Framework.Bpp3d.Infra;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using System;
using System.Collections.Generic;
using System.Linq;
using Math = System.Math;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;

/// <summary>
/// 货物合并器 / Item merger.
/// 将货物合并为块或堆叠以提高装箱效率。
/// Merges items into blocks or piles for more efficient packing.
/// </summary>
public static class ItemMerger {
    /// <summary>
    /// 合并配置 / Merge configuration.
    /// </summary>
    public sealed record Config(
        bool MergeWithRotation = true,
        bool MergeAsPile = true,
        bool MergeAsBlock = true);

    /// <summary>
    /// 合并货物为简单块 / Merge items into simple blocks.
    /// </summary>
    public static IReadOnlyList<SimpleBlock> MergeBlocks(
        IReadOnlyList<(ActualItem Item, ulong Amount)> items,
        Container3Shape<FltX> space,
        Quantity<FltX> restWeight,
        Config? config = null) {
        config ??= new Config();
        var result = new List<SimpleBlock>();

        foreach ((ActualItem item, ulong amount) in items) {
            if (amount == 0) continue;
            if (item.PackageCategory == PackageCategory.Cylindrical) continue;

            foreach (Orientation orientation in item.Orientations) {
                var view = new ItemView(item, orientation);
                int maxX = global::System.Math.Max(1, (int)(space.Width.Value.ToFlt64().ToDouble() / view.Width.Value.ToFlt64().ToDouble()));
                int maxY = global::System.Math.Max(1, (int)(space.Height.Value.ToFlt64().ToDouble() / view.Height.Value.ToFlt64().ToDouble()));
                int maxZ = global::System.Math.Max(1, (int)(space.Depth.Value.ToFlt64().ToDouble() / view.Depth.Value.ToFlt64().ToDouble()));

                // Limit by available amount
                maxX = global::System.Math.Min(maxX, (int)amount);
                maxY = global::System.Math.Min(maxY, (int)item.MaxLayer.ToFlt64().ToDouble());

                if (maxX <= 0 || maxY <= 0 || maxZ <= 0) continue;

                var units = new List<QuantityPlacement3<ActualItem, FltX>>();
                for (int x = 0; x < maxX; x++) {
                    for (int y = 0; y < maxY; y++) {
                        for (int z = 0; z < maxZ; z++) {
                            var pos = new QuantityPoint3<FltX>(
                                new Quantity<FltX>(new FltX(x * view.Width.Value.ToFlt64().ToDouble()), SIBaseUnits.Meter),
                                new Quantity<FltX>(new FltX(y * view.Height.Value.ToFlt64().ToDouble()), SIBaseUnits.Meter),
                                new Quantity<FltX>(new FltX(z * view.Depth.Value.ToFlt64().ToDouble()), SIBaseUnits.Meter));
                            units.Add(new QuantityPlacement3<ActualItem, FltX>(item, pos, orientation));
                        }
                    }
                }

                if (units.Count > 0) {
                    result.Add(new SimpleBlock(item, view, orientation, maxX, maxY, maxZ, units));
                }
            }
        }

        return result;
    }

    /// <summary>
    /// 合并货物为堆叠 / Merge items into piles.
    /// </summary>
    public static IReadOnlyList<Pile> MergePiles(
        IReadOnlyList<(ActualItem Item, ulong Amount)> items,
        Container3Shape<FltX> space,
        Quantity<FltX> restWeight) {
        var result = new List<Pile>();
        if (items.Count < 2) return result;

        // Try to stack items vertically
        for (int i = 0; i < items.Count; i++) {
            for (int j = i + 1; j < items.Count; j++) {
                (ActualItem bottom, ulong bottomAmt) = items[i];
                (ActualItem top, ulong topAmt) = items[j];

                if (bottomAmt == 0 || topAmt == 0) continue;

                // Check dimension compatibility
                double bw = bottom.Width.Value.ToFlt64().ToDouble();
                double bd = bottom.Depth.Value.ToFlt64().ToDouble();
                double tw = top.Width.Value.ToFlt64().ToDouble();
                double td = top.Depth.Value.ToFlt64().ToDouble();

                if (global::System.Math.Abs(bw - tw) > 1e-7 || global::System.Math.Abs(bd - td) > 1e-7) continue;

                double totalHeight = bottom.Height.Value.ToFlt64().ToDouble() + top.Height.Value.ToFlt64().ToDouble();
                if (totalHeight > space.Height.Value.ToFlt64().ToDouble()) continue;

                var meter = SIBaseUnits.Meter;
                var units = new List<QuantityPlacement3<ActualItem, FltX>> {
                    new(bottom, new QuantityPoint3<FltX>(
                        new Quantity<FltX>(FltX.Zero, meter),
                        new Quantity<FltX>(FltX.Zero, meter),
                        new Quantity<FltX>(FltX.Zero, meter)), Orientation.Upright),
                    new(top, new QuantityPoint3<FltX>(
                        new Quantity<FltX>(FltX.Zero, meter),
                        new Quantity<FltX>(new FltX(bottom.Height.Value.ToFlt64().ToDouble()), meter),
                        new Quantity<FltX>(FltX.Zero, meter)), Orientation.Upright)
                };

                result.Add(new Pile(bottom, top, 1, 1, units));
            }
        }

        return result;
    }

    /// <summary>展平合并单元为货物列表 / Flatten merge units to item list.</summary>
    public static IReadOnlyList<ActualItem> Dump(IReadOnlyList<IItemMergeUnit> units) {
        var result = new List<ActualItem>();
        foreach (IItemMergeUnit unit in units) {
            if (unit is ActualItem item) {
                result.Add(item);
            }
        }
        return result;
    }
}
