#nullable enable

using Fuookami.Ospf.Framework.Bpp3d.Infra;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;

/// <summary>
/// 优先级属性 / Priority attribute.
/// </summary>
public sealed record PriorityAttribute(string Key, object? Value);

/// <summary>
/// 货物类型 / Item type.
/// </summary>
public sealed record ItemType(PackageType PackageType, Orientation Orientation);

/// <summary>
/// 货物模式 / Item pattern.
/// </summary>
public sealed record ItemPattern(
    string? BatchNo = null,
    IReadOnlyList<PriorityAttribute>? Priorities = null,
    string? Warehouse = null,
    PackageAttribute? PackageAttribute = null);

/// <summary>
/// 实际货物 / Actual item.
/// 具体的、已实例化的货物，具有完整尺寸和重量。
/// A concrete, instantiated item with full dimensions and weight.
/// </summary>
public sealed class ActualItem : IItemMergeUnit, ICargoView {
    public string Id { get; }
    public string Name { get; }
    public Quantity<FltX> Width { get; }
    public Quantity<FltX> Height { get; }
    public Quantity<FltX> Depth { get; }
    public Quantity<FltX> Weight { get; }
    public IReadOnlyList<Orientation> Orientations { get; }
    public string? BatchNo { get; }
    public string? Warehouse { get; }
    public PackageAttribute PackageAttribute { get; }
    public IReadOnlyList<PriorityAttribute> Priorities { get; }

    public ActualItem(
        string id,
        string name,
        Quantity<FltX> width,
        Quantity<FltX> height,
        Quantity<FltX> depth,
        Quantity<FltX> weight,
        PackageAttribute? packageAttribute = null,
        IReadOnlyList<Orientation>? orientations = null,
        string? batchNo = null,
        string? warehouse = null,
        IReadOnlyList<PriorityAttribute>? priorities = null) {
        Id = id;
        Name = name;
        Width = width;
        Height = height;
        Depth = depth;
        Weight = weight;
        PackageAttribute = packageAttribute ?? new PackageAttribute(PackageType.Box, new UInt64(10), new UInt64(10), new UInt64(1), new UInt64(100));
        Orientations = orientations ?? new[] { Orientation.Upright };
        BatchNo = batchNo;
        Warehouse = warehouse;
        Priorities = priorities ?? System.Array.Empty<PriorityAttribute>();
    }

    /// <summary>体积 / Volume.</summary>
    public Quantity<FltX> Volume => Width.Multiply(Height).Multiply(Depth);

    /// <summary>可以堆在上方的包装类型 / Package types allowed on top.</summary>
    public IReadOnlySet<PackageType> OverPackageTypes => PackageAttribute.OverPackageTypes;

    /// <summary>
    /// 底部投影面积 / The bottom footprint area.
    /// </summary>
    /// <remarks>
    /// 以"宽 × 深"作为矩形近似。圆柱形货物的圆形投影要经过 `ShapeFootprint2.Circle`，那层还没接到物品视图上——
    /// 这是这里明确的局限，写在代码里而不是让悬挂策略悄悄按矩形算。
    ///
    /// width times depth as a rectangular approximation. A cylindrical item's circular footprint would go through `ShapeFootprint2.Circle`,
    /// which is not wired to the item view yet — an explicit limitation, stated here rather than letting the hanging policies silently assume a
    /// rectangle.
    /// </remarks>
    public Quantity<FltX>? BottomFootprintArea => Width.Multiply(Depth);

    /// <summary>
    /// 允许的最大高度（长度）：目前未知 / The allowed maximum height as a length: currently unknown.
    /// </summary>
    /// <remarks>
    /// C# 的物品模型把 `MaxHeight` 存成层数（`UInt64`），而基线是长度（`Quantity&lt;FltX&gt;`）。
    /// 这里如实返回 null，让堆叠策略的高度门限判否——而不是把层数当长度，让门限静默失效。
    ///
    /// the C# item model keeps `MaxHeight` as a layer count while the baseline keeps a length. Returning null here is the honest answer and
    /// makes the policies' height gate refuse, rather than treating a layer count as a length and letting the gate silently pass.
    /// </remarks>
    public Quantity<FltX>? MaxHeightLength => null;

    /// <summary>底部投影的最小跨度 / The minimum span of the bottom footprint.</summary>
    /// <remarks>矩形近似下取宽与深之中较小的那个。/ The smaller of width and depth under the rectangular approximation.</remarks>
    public Quantity<FltX>? BottomFootprintMinSpan =>
        Width.Value.ToFlt64().CompareTo(Depth.Value.ToFlt64()) <= 0 ? Width : Depth;

    /// <summary>包装类型 / Package type.</summary>
    public PackageType PackageType => PackageAttribute.PackageType;

    /// <summary>包装类别 / Package category.</summary>
    public PackageCategory PackageCategory => PackageAttribute.Category;

    /// <summary>最大层数 / Max layer.</summary>
    public UInt64 MaxLayer => PackageAttribute.MaxLayer;

    /// <summary>最大高度 / Max height.</summary>
    public UInt64 MaxHeight => PackageAttribute.MaxHeight;

    /// <summary>仅底部放置 / Bottom only.</summary>
    public bool BottomOnly => PackageAttribute.BottomOnly;

    /// <summary>顶部平整 / Top flat.</summary>
    public bool TopFlat => PackageAttribute.TopFlat;
}

/// <summary>
/// 货物视图 / Item view.
/// 将货物以特定方向观察时的视图。
/// An item viewed in a specific orientation.
/// </summary>
public sealed class ItemView {
    public ActualItem Item { get; }
    public Orientation Orientation { get; }
    public Quantity<FltX> Width { get; }
    public Quantity<FltX> Height { get; }
    public Quantity<FltX> Depth { get; }

    public ItemView(ActualItem item, Orientation orientation) {
        Item = item;
        Orientation = orientation;
        (Width, Height, Depth) = orientation switch {
            Orientation.Upright => (item.Width, item.Height, item.Depth),
            Orientation.Side => (item.Height, item.Width, item.Depth),
            Orientation.Lie => (item.Width, item.Depth, item.Height),
            _ => (item.Width, item.Height, item.Depth)
        };
    }

    /// <summary>体积 / Volume.</summary>
    public Quantity<FltX> Volume => Width.Multiply(Height).Multiply(Depth);

    /// <summary>重量 / Weight.</summary>
    public Quantity<FltX> Weight => Item.Weight;

    /// <summary>包装类型 / Package type.</summary>
    public PackageType PackageType => Item.PackageType;

    /// <summary>仅底部放置 / Bottom only.</summary>
    public bool BottomOnly => Item.BottomOnly;

    /// <summary>顶部平整 / Top flat.</summary>
    public bool TopFlat => Item.TopFlat;

    /// <summary>旋转视图 / Rotated view.</summary>
    public ItemView Rotation => new(Item, Orientation switch {
        Orientation.Upright => Orientation.Lie,
        Orientation.Side => Orientation.Lie,
        Orientation.Lie => Orientation.Upright,
        _ => Orientation.Upright
    });
}

/// <summary>
/// 货物模式化视图 / Patterned item.
/// 将多个实际货物聚合为一个模式化货物。
/// Aggregates multiple actual items into a patterned item.
/// </summary>
public sealed class PatternedItem {
    public IReadOnlyList<(ActualItem Item, ulong Amount)> ActualItems { get; }
    public Quantity<FltX> Width { get; }
    public Quantity<FltX> Height { get; }
    public Quantity<FltX> Depth { get; }
    public Quantity<FltX> Weight { get; }
    public Quantity<FltX> Volume { get; }

    public PatternedItem(IReadOnlyList<(ActualItem Item, ulong Amount)> actualItems) {
        ActualItems = actualItems;
        if (actualItems.Count == 0) {
            var zero = new Quantity<FltX>(FltX.Zero, Fuookami.Ospf.Quantities.Unit.SIBaseUnits.Meter);
            Width = Height = Depth = Weight = Volume = zero;
            return;
        }
        Width = actualItems[0].Item.Width;
        Height = actualItems[0].Item.Height;
        Depth = actualItems[0].Item.Depth;
        Weight = actualItems[0].Item.Weight;
        Volume = Width.Multiply(Height).Multiply(Depth);
    }
}
