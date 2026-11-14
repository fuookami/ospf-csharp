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

/// <summary>
/// 量纲包裹形状 / A quantity package shape.
/// </summary>
/// <param name="Width">宽度 / The width.</param>
/// <param name="Height">高度 / The height.</param>
/// <param name="Depth">深度 / The depth.</param>
/// <param name="Weight">重量 / The weight.</param>
/// <param name="PackageType">包裹类型 / The package type.</param>
/// <param name="ShapeSpec">形状规格 / The shape spec.</param>
public sealed record QuantityPackageShape<V>(
    Quantity<V> Width,
    Quantity<V> Height,
    Quantity<V> Depth,
    Quantity<V> Weight,
    PackageType PackageType,
    PackageShapeSpec? ShapeSpec = null) where V : struct, IFloatingNumber<V> {
    /// <summary>
    /// 体积 / The volume.
    /// </summary>
    /// <returns>体积量 / The volume quantity.</returns>
    /// <remarks>
    /// **体积是量纲相乘的结果，不是三个数值相乘**：`米 × 米 × 米` 得到 `米³`，而把三个数值乘起来再贴一个单位
    /// 会让量纲检查失效——之后任何"体积加质量"都会被当成合法。
    ///
    /// **The volume is the product of quantities, not of three numbers**: `m × m × m` yields `m³`, while multiplying three numbers and attaching a unit would defeat
    /// dimensional checking, making any later "volume plus mass" look legal.
    /// </remarks>
    public Quantity<V> Volume => Width.Multiply(Height).Multiply(Depth);
}

/// <summary>
/// 量纲包裹（材料汇总部分）/ A quantity package (the material roll-up only).
/// </summary>
/// <param name="Shape">包裹形状 / The package shape.</param>
/// <param name="Materials">材料映射 / The material map.</param>
/// <param name="Code">包裹编码 / The package code.</param>
/// <param name="Pattern">包裹模式 / The package pattern.</param>
/// <param name="Packages">子包裹列表 / The child packages.</param>
/// <param name="Amount">数量 / The amount.</param>
/// <param name="Pending">是否待定 / Whether it is pending.</param>
/// <remarks>
/// **`Materials` 在外层包裹里是"子包裹材料之和"**：外层自己不装材料，它的材料表必须由子包裹累加，
/// 否则"这个托盘里到底有多少货"要递归到叶子才能回答。
///
/// **`Materials` on an outer package is the sum over its children**: an outer package holds no material itself, so its map must be accumulated from the children, or
/// "how much cargo is on this pallet" would need a walk to the leaves.
/// </remarks>
public sealed record QuantityPackage<V>(
    QuantityPackageShape<V> Shape,
    IReadOnlyDictionary<QuantityMaterial<V>, ulong> Materials,
    PackageCode? Code = null,
    PackagePattern? Pattern = null,
    IReadOnlyList<QuantityPackage<V>>? Packages = null,
    ulong Amount = 1UL,
    bool Pending = false) where V : struct, IFloatingNumber<V> {
    /// <summary>宽度 / The width.</summary>
    public Quantity<V> Width => Shape.Width;

    /// <summary>高度 / The height.</summary>
    public Quantity<V> Height => Shape.Height;

    /// <summary>深度 / The depth.</summary>
    public Quantity<V> Depth => Shape.Depth;

    /// <summary>重量 / The weight.</summary>
    public Quantity<V> Weight => Shape.Weight;

    /// <summary>体积 / The volume.</summary>
    public Quantity<V> Volume => Shape.Volume;

    /// <summary>
    /// 创建外层包裹 / Create an outer package.
    /// </summary>
    /// <param name="shape">包裹形状 / The package shape.</param>
    /// <param name="packages">子包裹列表 / The child packages.</param>
    /// <param name="code">包裹编码 / The package code.</param>
    /// <param name="pattern">包裹模式 / The package pattern.</param>
    /// <param name="amount">数量 / The amount.</param>
    /// <param name="pending">是否待定 / Whether it is pending.</param>
    /// <returns>外层包裹 / The outer package.</returns>
    /// <remarks>
    /// **同一材料出现多次要相加，而不是后写覆盖**：一个托盘里两个箱子各装同一种物料时，
    /// 覆盖式写法只会留下最后一箱的数量，而那个数字看起来完全合理。
    ///
    /// **A repeated material accumulates instead of overwriting**: when two boxes on one pallet carry the same material, an overwriting implementation keeps only the
    /// last box's amount — and that number looks entirely plausible.
    /// </remarks>
    public static QuantityPackage<V> OuterPackage(
        QuantityPackageShape<V> shape,
        IReadOnlyList<QuantityPackage<V>> packages,
        PackageCode? code = null,
        PackagePattern? pattern = null,
        ulong amount = 1UL,
        bool pending = false) {
        var materials = new Dictionary<QuantityMaterial<V>, ulong>();
        foreach (QuantityPackage<V> package in packages) {
            foreach (KeyValuePair<QuantityMaterial<V>, ulong> entry in package.Materials) {
                materials[entry.Key] = (materials.TryGetValue(entry.Key, out ulong known) ? known : 0UL)
                    + entry.Value;
            }
        }
        return new QuantityPackage<V>(shape, materials, code, pattern, packages, amount, pending);
    }

    /// <summary>创建内层包裹 / Create an inner package.</summary>
    /// <param name="shape">包裹形状 / The package shape.</param>
    /// <param name="materials">材料映射 / The material map.</param>
    /// <param name="code">包裹编码 / The package code.</param>
    /// <param name="pattern">包裹模式 / The package pattern.</param>
    /// <param name="amount">数量 / The amount.</param>
    /// <param name="pending">是否待定 / Whether it is pending.</param>
    /// <returns>内层包裹 / The inner package.</returns>
    public static QuantityPackage<V> InnerPackage(
        QuantityPackageShape<V> shape,
        IReadOnlyDictionary<QuantityMaterial<V>, ulong> materials,
        PackageCode? code = null,
        PackagePattern? pattern = null,
        ulong amount = 1UL,
        bool pending = false) =>
        new(shape, materials, code, pattern, null, amount, pending);
}
