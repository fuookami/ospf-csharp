#nullable enable

using Fuookami.Ospf.Framework.Bpp3d.Infra;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;

/// <summary>
/// 泛型物料模型 / Generic material model.
/// </summary>
public sealed record QuantityMaterial<V>(
    string No,
    MaterialType Type,
    string Name,
    Quantity<V> Weight,
    string? Manufacturer = null,
    string? Supplier = null,
    string? Warehouse = null) where V : struct, IFloatingNumber<V> {
    /// <summary>转换为 FltX 模型 / Convert to FltX model.</summary>
    public Material<FltX> ToModel() => new(No, Type, Name,
        new Quantity<FltX>(new FltX(Weight.Value.ToFlt64().ToDouble()), Weight.Unit),
        Manufacturer, Supplier, Warehouse);
}

/// <summary>
/// 泛型货物放置模型 / Generic item placement model.
/// </summary>
public sealed record QuantityItemPlacement<V>(
    string ItemId,
    Quantity<V> X,
    Quantity<V> Y,
    Quantity<V> Z,
    Orientation Orientation) where V : struct, IFloatingNumber<V>;

/// <summary>
/// 泛型 BinLayer 模型 / Generic BinLayer model.
/// </summary>
public sealed record QuantityBinLayer<V>(
    int Iteration,
    Quantity<V> Width,
    Quantity<V> Height,
    Quantity<V> Depth,
    IReadOnlyList<QuantityItemPlacement<V>> Units) where V : struct, IFloatingNumber<V>;
