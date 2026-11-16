// 物料模型 / Material model.
// 与固定 Kotlin 基线 `bpp2d/domain/item/model/Material.kt`（106 行）对应。
// Mirrors the pinned baseline's `Material.kt`.
//
// 这是解除"物料汇总"阻塞的一批：`IAbstractItem` 的 `materialAmounts` 需要 `MaterialKey`，`Packer` 的物料汇总
// 一直因此留空。
// unblocks the material summary: `IAbstractItem.materialAmounts` needs `MaterialKey`, which is why
// `Packer`'s material summary had been deliberately left empty.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Quantity;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain;

/// <summary>
/// 物料类型 / Material type.
/// </summary>
public enum MaterialType {
    /// <summary>原材料 / Raw material.</summary>
    RawMaterial,

    /// <summary>半成品 / Semi-finished product.</summary>
    SemiFinishedProduct,

    /// <summary>成品 / Finished product.</summary>
    FinishedProduct,
}

/// <summary>
/// 物料键：按编号、类型与来源唯一识别物料 / Material key uniquely identifying a material.
/// </summary>
/// <remarks>
/// 参与相等性的字段刻意包含**制造商与供应商**：同一编号的物料来自不同供应商时，成本与交期都不同，若只按编号
/// 合并，采购侧会拿到一份抹平来源的汇总。
/// The equality fields deliberately include **manufacturer and supplier**: the same material number from different
/// suppliers differs in cost and lead time, and merging by number alone would hand procurement a summary that erases
/// the source.
/// </remarks>
public class MaterialKey : IEquatable<MaterialKey> {
    /// <summary>
    /// 构造物料键 / Construct a material key.
    /// </summary>
    /// <param name="no">物料编号 / Material number.</param>
    /// <param name="type">物料类型 / Material type.</param>
    /// <param name="manufacturer">制造商 / Manufacturer.</param>
    /// <param name="supplier">供应商 / Supplier.</param>
    public MaterialKey(
        MaterialNo no,
        MaterialType type,
        string? manufacturer = null,
        string? supplier = null) {
        No = no;
        Type = type;
        Manufacturer = manufacturer;
        Supplier = supplier;
    }

    /// <summary>物料编号 / Material number.</summary>
    public MaterialNo No { get; }

    /// <summary>物料类型 / Material type.</summary>
    public MaterialType Type { get; }

    /// <summary>制造商 / Manufacturer.</summary>
    public string? Manufacturer { get; }

    /// <summary>供应商 / Supplier.</summary>
    public string? Supplier { get; }

    /// <inheritdoc/>
    public bool Equals(MaterialKey? other) =>
        other is not null
        && No.Equals(other.No)
        && Type == other.Type
        && string.Equals(Manufacturer, other.Manufacturer, StringComparison.Ordinal)
        && string.Equals(Supplier, other.Supplier, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? other) => Equals(other as MaterialKey);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        HashCode.Combine(
            No,
            Type,
            Manufacturer is null ? 0 : StringComparer.Ordinal.GetHashCode(Manufacturer),
            Supplier is null ? 0 : StringComparer.Ordinal.GetHashCode(Supplier));

    /// <inheritdoc/>
    public override string ToString() => string.Format(
        CultureInfo.InvariantCulture,
        "{0}-{1}",
        No,
        Type);
}

/// <summary>
/// 物料 / Material.
/// </summary>
/// <remarks>
/// 相等性只由 <see cref="Key"/> 与 <see cref="Name"/> 决定，**不含重量与仓库**：同一物料换个仓库不是新物料，
/// 把仓库算进相等性会让一份 BOM 因为库位调整而产生重复行。
/// Equality is decided by <see cref="Key"/> and <see cref="Name"/> only, **excluding weight and warehouse**: moving a
/// material to another warehouse does not make it a new material, and including the warehouse would duplicate rows in a
/// bill of materials after any location change.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public class Material<V> : IEquatable<Material<V>> where V : struct, IFloatingNumber<V> {
    /// <summary>
    /// 构造物料 / Construct a material.
    /// </summary>
    /// <param name="no">物料编号 / Material number.</param>
    /// <param name="type">物料类型 / Material type.</param>
    /// <param name="name">物料名称 / Material name.</param>
    /// <param name="weight">单件重量 / Per-piece weight.</param>
    /// <param name="manufacturer">制造商 / Manufacturer.</param>
    /// <param name="supplier">供应商 / Supplier.</param>
    /// <param name="warehouse">仓库 / Warehouse.</param>
    public Material(
        MaterialNo no,
        MaterialType type,
        string name,
        Quantity<V> weight,
        string? manufacturer = null,
        string? supplier = null,
        string? warehouse = null) {
        No = no;
        Type = type;
        Name = name;
        Weight = weight;
        Manufacturer = manufacturer;
        Supplier = supplier;
        Warehouse = warehouse;
    }

    /// <summary>物料编号 / Material number.</summary>
    public MaterialNo No { get; }

    /// <summary>物料类型 / Material type.</summary>
    public MaterialType Type { get; }

    /// <summary>物料名称 / Material name.</summary>
    public string Name { get; }

    /// <summary>单件重量 / Per-piece weight.</summary>
    public Quantity<V> Weight { get; }

    /// <summary>制造商 / Manufacturer.</summary>
    public string? Manufacturer { get; }

    /// <summary>供应商 / Supplier.</summary>
    public string? Supplier { get; }

    /// <summary>仓库 / Warehouse.</summary>
    public string? Warehouse { get; }

    /// <summary>物料键 / Material key.</summary>
    public MaterialKey Key => new(No, Type, Manufacturer, Supplier);

    /// <inheritdoc/>
    public bool Equals(Material<V>? other) =>
        other is not null && Key.Equals(other.Key) && string.Equals(Name, other.Name, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? other) => Equals(other as Material<V>);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Key, Name);

    /// <inheritdoc/>
    public override string ToString() => string.Format(
        CultureInfo.InvariantCulture,
        "{0}-{1}",
        No,
        Name);
}

/// <summary>
/// 物料用量汇总工具 / Material usage aggregation helpers.
/// </summary>
public static class MaterialSummaryOps {
    /// <summary>
    /// 把多份"物料键到件数"的映射累加成一份 / Accumulate many material-to-amount maps into one.
    /// </summary>
    /// <remarks>
    /// 用 <see cref="MaterialKey"/> 作字典键是安全的：它实现了 `IEquatable` 与 `GetHashCode`，且相等性只依赖
    /// 编号、类型、制造商、供应商四个**不可变**字段，不存在"哈希随状态漂移"的问题。这与曾经遇到的
    /// "深值对象当字典键"不是同一类情况。
    /// Using <see cref="MaterialKey"/> as a dictionary key is safe: it implements `IEquatable` and `GetHashCode` over
    /// four **immutable** fields, so its hash cannot drift with state. This is not the same hazard as the earlier
    /// "deep value object as dictionary key" problem in this session.
    /// </remarks>
    /// <param name="maps">多份物料用量 / The material usage maps.</param>
    /// <returns>累加后的物料用量，保持首次出现顺序 / Accumulated usage, keeping first-seen order.</returns>
    public static IReadOnlyList<MaterialSummary> Accumulate(
        IEnumerable<IReadOnlyDictionary<MaterialKey, ulong>> maps) {
        ArgumentNullException.ThrowIfNull(maps);

        var totals = new Dictionary<MaterialKey, ulong>();
        var order = new List<MaterialKey>();
        foreach (IReadOnlyDictionary<MaterialKey, ulong> map in maps) {
            foreach (KeyValuePair<MaterialKey, ulong> pair in map) {
                if (totals.TryGetValue(pair.Key, out ulong previous)) {
                    totals[pair.Key] = previous + pair.Value;
                }
                else {
                    totals[pair.Key] = pair.Value;
                    order.Add(pair.Key);
                }
            }
        }
        return order
            .Select(key => new MaterialSummary(key, totals[key]))
            .ToList();
    }
}
