#nullable enable

using Fuookami.Ospf.Framework.Bpp3d.Infra;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;

/// <summary>
/// 二维货物放置 / 2D item placement.
/// 用于平面层的货物放置信息。
/// </summary>
public sealed record ItemPlacement2(
    ActualItem Unit,
    Quantity<FltX> X,
    Quantity<FltX> Y,
    Orientation Orientation = Orientation.Upright);

/// <summary>
/// 平面层 / Plane layer.
/// 二维平面投影层，用于层生成。
/// 2D plane projection layer for layer generation.
/// </summary>
public sealed class PlaneLayer {
    public ProjectivePlane Plane { get; }
    public Quantity<FltX> Length { get; }
    public Quantity<FltX> Width { get; }
    public IReadOnlyList<ItemPlacement2> Units { get; }

    public PlaneLayer(
        ProjectivePlane plane,
        Quantity<FltX> length,
        Quantity<FltX> width,
        IReadOnlyList<ItemPlacement2> units) {
        Plane = plane;
        Length = length;
        Width = width;
        Units = units;
    }

    /// <summary>货物列表 / Item list.</summary>
    public IReadOnlyList<ActualItem> Items => Units.Select(u => u.Unit).ToList();
}

/// <summary>
/// 托盘层 / Pallet layer.
/// 货物的水平排列层。
/// Horizontal arrangement layer of items.
/// </summary>
public sealed class PalletLayer : ItemContainerBase {
    public int Iteration { get; }
    public System.Type? From { get; }
    public Container3Shape<FltX> Shape { get; }
    public Quantity<FltX> Height => Shape.Height;

    public PalletLayer(
        int iteration,
        System.Type? from,
        Container3Shape<FltX> shape,
        IReadOnlyList<QuantityPlacement3<ActualItem, FltX>> units)
        : base(units) {
        Iteration = iteration;
        From = from;
        Shape = shape;
    }

    /// <inheritdoc/>
    public override bool TopFlat => true;
}
