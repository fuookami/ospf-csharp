#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;
/// <summary>
/// 物料类型枚举 / Material type enumeration.
/// </summary>
public enum MaterialType {
    /// <summary>原材料 / Raw material</summary>
    RawMaterial,
    /// <summary>半成品 / Semi-finished product</summary>
    SemiFinishedProduct,
    /// <summary>成品 / Finished product</summary>
    FinishedProduct
}

/// <summary>
/// 物料键 / Material key (identity).
/// </summary>
public sealed record MaterialKey(
    string No,
    MaterialType Type,
    string? Manufacturer = null,
    string? Supplier = null);

/// <summary>
/// 物料模型 / Material model.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type</typeparam>
public sealed record Material<V>(
    string No,
    MaterialType Type,
    string Name,
    Quantity<V> Weight,
    string? Manufacturer = null,
    string? Supplier = null,
    string? Warehouse = null)
    where V : struct, IFloatingNumber<V> {
    /// <summary>物料键 / Material key.</summary>
    public MaterialKey Key => new(No, Type, Manufacturer, Supplier);
}
