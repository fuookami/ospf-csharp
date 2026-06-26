#nullable enable

using Fuookami.Ospf.Framework.Bpp3d.Infra;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;

/// <summary>
/// 货物合并单元接口 / Item merge unit interface.
/// 标记可参与合并的货物类型。
/// </summary>
public interface IItemMergeUnit { }

/// <summary>
/// 块基类 / Block base class.
/// 一组货物的组合放置。
/// A combined placement of a group of items.
/// </summary>
public abstract class Block : IItemMergeUnit {
    /// <summary>放置单元列表 / Placement units.</summary>
    public IReadOnlyList<QuantityPlacement3<ActualItem, FltX>> Units { get; }

    protected Block(IReadOnlyList<QuantityPlacement3<ActualItem, FltX>> units) {
        Units = units;
    }

    /// <summary>形状宽度 / Shape width.</summary>
    public Quantity<FltX> ShapeWidth { get; protected set; } = null!;
    /// <summary>形状高度 / Shape height.</summary>
    public Quantity<FltX> ShapeHeight { get; protected set; } = null!;
    /// <summary>形状深度 / Shape depth.</summary>
    public Quantity<FltX> ShapeDepth { get; protected set; } = null!;

    /// <summary>包装类型 / Package type.</summary>
    public PackageType PackageType => Units.Count > 0 ? Units[0].Unit.PackageType : PackageType.Box;
}

/// <summary>
/// 通用块 / Common block.
/// 任意货物组合。
/// </summary>
public sealed class CommonBlock : Block {
    public CommonBlock(IReadOnlyList<QuantityPlacement3<ActualItem, FltX>> units) : base(units) {
        ComputeShape();
    }

    private void ComputeShape() {
        if (Units.Count == 0) {
            var zero = new Quantity<FltX>(FltX.Zero, Fuookami.Ospf.Quantities.Unit.SIBaseUnits.Meter);
            ShapeWidth = ShapeHeight = ShapeDepth = zero;
            return;
        }
        // Compute bounding box from placements
        double maxX = 0, maxY = 0, maxZ = 0;
        foreach (QuantityPlacement3<ActualItem, FltX> u in Units) {
            double x = u.Position.X.Value.ToFlt64().ToDouble() + u.Unit.Width.Value.ToFlt64().ToDouble();
            double y = u.Position.Y.Value.ToFlt64().ToDouble() + u.Unit.Height.Value.ToFlt64().ToDouble();
            double z = u.Position.Z.Value.ToFlt64().ToDouble() + u.Unit.Depth.Value.ToFlt64().ToDouble();
            if (x > maxX) maxX = x;
            if (y > maxY) maxY = y;
            if (z > maxZ) maxZ = z;
        }
        var meter = Fuookami.Ospf.Quantities.Unit.SIBaseUnits.Meter;
        ShapeWidth = new Quantity<FltX>(new FltX(maxX), meter);
        ShapeHeight = new Quantity<FltX>(new FltX(maxY), meter);
        ShapeDepth = new Quantity<FltX>(new FltX(maxZ), meter);
    }
}

/// <summary>
/// 简单块 / Simple block.
/// 同种货物的网格排列。
/// Grid arrangement of identical items.
/// </summary>
public sealed class SimpleBlock : Block {
    public ActualItem Item { get; }
    public ItemView ItemView { get; }
    public Orientation ItemOrientation { get; }
    public int Layer { get; }
    public int XCount { get; }
    public int YCount { get; }
    public int ZCount { get; }

    public SimpleBlock(
        ActualItem item,
        ItemView itemView,
        Orientation orientation,
        int xCount,
        int yCount,
        int zCount,
        IReadOnlyList<QuantityPlacement3<ActualItem, FltX>> units)
        : base(units) {
        Item = item;
        ItemView = itemView;
        ItemOrientation = orientation;
        XCount = xCount;
        YCount = yCount;
        ZCount = zCount;
        Layer = zCount;
        var meter = Fuookami.Ospf.Quantities.Unit.SIBaseUnits.Meter;
        ShapeWidth = new Quantity<FltX>(new FltX(itemView.Width.Value.ToFlt64().ToDouble() * xCount), meter);
        ShapeHeight = new Quantity<FltX>(new FltX(itemView.Height.Value.ToFlt64().ToDouble() * yCount), meter);
        ShapeDepth = new Quantity<FltX>(new FltX(itemView.Depth.Value.ToFlt64().ToDouble() * zCount), meter);
    }

    /// <summary>仅底部放置 / Bottom only.</summary>
    public bool BottomOnly => Item.BottomOnly;
    /// <summary>顶部平整 / Top flat.</summary>
    public bool TopFlat => Item.TopFlat;
}

/// <summary>
/// 堆叠块 / Pile.
/// 不同货物的垂直堆叠。
/// Vertical stacking of different items.
/// </summary>
public sealed class Pile : Block {
    public ActualItem BottomItem { get; }
    public ActualItem TopItem { get; }
    public int BottomLayer { get; }
    public int TopLayer { get; }

    public Pile(
        ActualItem bottomItem,
        ActualItem topItem,
        int bottomLayer,
        int topLayer,
        IReadOnlyList<QuantityPlacement3<ActualItem, FltX>> units)
        : base(units) {
        BottomItem = bottomItem;
        TopItem = topItem;
        BottomLayer = bottomLayer;
        TopLayer = topLayer;
        ComputeShape();
    }

    private void ComputeShape() {
        if (Units.Count == 0) {
            var zero = new Quantity<FltX>(FltX.Zero, Fuookami.Ospf.Quantities.Unit.SIBaseUnits.Meter);
            ShapeWidth = ShapeHeight = ShapeDepth = zero;
            return;
        }
        double maxX = 0, maxY = 0, maxZ = 0;
        foreach (QuantityPlacement3<ActualItem, FltX> u in Units) {
            double x = u.Position.X.Value.ToFlt64().ToDouble() + u.Unit.Width.Value.ToFlt64().ToDouble();
            double y = u.Position.Y.Value.ToFlt64().ToDouble() + u.Unit.Height.Value.ToFlt64().ToDouble();
            double z = u.Position.Z.Value.ToFlt64().ToDouble() + u.Unit.Depth.Value.ToFlt64().ToDouble();
            if (x > maxX) maxX = x;
            if (y > maxY) maxY = y;
            if (z > maxZ) maxZ = z;
        }
        var meter = Fuookami.Ospf.Quantities.Unit.SIBaseUnits.Meter;
        ShapeWidth = new Quantity<FltX>(new FltX(maxX), meter);
        ShapeHeight = new Quantity<FltX>(new FltX(maxY), meter);
        ShapeDepth = new Quantity<FltX>(new FltX(maxZ), meter);
    }
}

/// <summary>
/// 分层块 / Layered block.
/// 多个简单块的垂直堆叠。
/// Vertical stacking of multiple simple blocks.
/// </summary>
public sealed class LayeredBlock : Block {
    public IReadOnlyList<SimpleBlock> Layers { get; }
    public ActualItem BottomItem => Layers[0].Item;
    public ActualItem TopItem => Layers[^1].Item;

    public LayeredBlock(IReadOnlyList<SimpleBlock> layers, IReadOnlyList<QuantityPlacement3<ActualItem, FltX>> units)
        : base(units) {
        Layers = layers;
        ComputeShape();
    }

    private void ComputeShape() {
        if (Layers.Count == 0) {
            var zero = new Quantity<FltX>(FltX.Zero, Fuookami.Ospf.Quantities.Unit.SIBaseUnits.Meter);
            ShapeWidth = ShapeHeight = ShapeDepth = zero;
            return;
        }
        double maxW = 0, totalH = 0, maxD = 0;
        foreach (SimpleBlock layer in Layers) {
            double w = layer.ShapeWidth.Value.ToFlt64().ToDouble();
            double h = layer.ShapeHeight.Value.ToFlt64().ToDouble();
            double d = layer.ShapeDepth.Value.ToFlt64().ToDouble();
            if (w > maxW) maxW = w;
            totalH += h;
            if (d > maxD) maxD = d;
        }
        var meter = Fuookami.Ospf.Quantities.Unit.SIBaseUnits.Meter;
        ShapeWidth = new Quantity<FltX>(new FltX(maxW), meter);
        ShapeHeight = new Quantity<FltX>(new FltX(totalH), meter);
        ShapeDepth = new Quantity<FltX>(new FltX(maxD), meter);
    }
}

/// <summary>
/// 复杂块 / Complex block.
/// 块的相对放置组合。
/// Combined placement of blocks relative to each other.
/// </summary>
public sealed class ComplexBlock : Block {
    public IReadOnlyList<QuantityPlacement3<Block, FltX>> BlockPlacements { get; }

    public ComplexBlock(
        IReadOnlyList<QuantityPlacement3<Block, FltX>> blockPlacements,
        IReadOnlyList<QuantityPlacement3<ActualItem, FltX>> units)
        : base(units) {
        BlockPlacements = blockPlacements;
        ComputeShape();
    }

    private void ComputeShape() {
        if (BlockPlacements.Count == 0) {
            var zero = new Quantity<FltX>(FltX.Zero, Fuookami.Ospf.Quantities.Unit.SIBaseUnits.Meter);
            ShapeWidth = ShapeHeight = ShapeDepth = zero;
            return;
        }
        double maxX = 0, maxY = 0, maxZ = 0;
        foreach (QuantityPlacement3<Block, FltX> bp in BlockPlacements) {
            double x = bp.Position.X.Value.ToFlt64().ToDouble() + bp.Unit.ShapeWidth.Value.ToFlt64().ToDouble();
            double y = bp.Position.Y.Value.ToFlt64().ToDouble() + bp.Unit.ShapeHeight.Value.ToFlt64().ToDouble();
            double z = bp.Position.Z.Value.ToFlt64().ToDouble() + bp.Unit.ShapeDepth.Value.ToFlt64().ToDouble();
            if (x > maxX) maxX = x;
            if (y > maxY) maxY = y;
            if (z > maxZ) maxZ = z;
        }
        var meter = Fuookami.Ospf.Quantities.Unit.SIBaseUnits.Meter;
        ShapeWidth = new Quantity<FltX>(new FltX(maxX), meter);
        ShapeHeight = new Quantity<FltX>(new FltX(maxY), meter);
        ShapeDepth = new Quantity<FltX>(new FltX(maxZ), meter);
    }
}
