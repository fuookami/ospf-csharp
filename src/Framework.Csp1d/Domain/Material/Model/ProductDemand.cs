#nullable enable

using Fuookami.Ospf.Math;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Dimension;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Functional;
using System;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
/// <summary>
/// 卷数离散单位 / Discrete unit for roll count
/// </summary>
public sealed class RollCountUnit : PhysicalUnit {
    /// <summary>单例实例 / Singleton instance.</summary>
    public static readonly RollCountUnit Instance = new();

    private RollCountUnit() { }

    /// <inheritdoc/>
    public override string Name => "roll";
    /// <inheritdoc/>
    public override string Symbol => "roll";
    /// <inheritdoc/>
    public override DerivedQuantity Quantity { get; } = new DerivedQuantity(
        System.Array.Empty<FundamentalQuantity>(),
        name: "roll count",
        symbol: "roll",
        domain: QuantityDomain.Discrete);
    /// <inheritdoc/>
    public override UnitConversionRule ConversionRule => new UnitConversionRule.Linear(new Scale(Array.Empty<(Either<FltX, RtnX>, FltX)>()));
}

/// <summary>
/// 张数离散单位 / Discrete unit for sheet count
/// </summary>
public sealed class SheetCountUnit : PhysicalUnit {
    /// <summary>单例实例 / Singleton instance.</summary>
    public static readonly SheetCountUnit Instance = new();

    private SheetCountUnit() { }

    /// <inheritdoc/>
    public override string Name => "sheet";
    /// <inheritdoc/>
    public override string Symbol => "sheet";
    /// <inheritdoc/>
    public override DerivedQuantity Quantity { get; } = new DerivedQuantity(
        System.Array.Empty<FundamentalQuantity>(),
        name: "sheet count",
        symbol: "sheet",
        domain: QuantityDomain.Discrete);
    /// <inheritdoc/>
    public override UnitConversionRule ConversionRule => new UnitConversionRule.Linear(new Scale(Array.Empty<(Either<FltX, RtnX>, FltX)>()));
}

/// <summary>
/// 产品需求模型，统一使用 Quantity&lt;V&gt; 表达需求值 / Product demand model using Quantity&lt;V&gt; as the single demand value
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public sealed class ProductDemand<V> where V : struct {
    /// <summary>
    /// 构造产品需求 / Construct a product demand
    /// </summary>
    /// <param name="product">产品 / Product.</param>
    /// <param name="quantity">需求值 / Demand quantity.</param>
    /// <param name="mode">需求口径标签 / Demand mode label.</param>
    public ProductDemand(Product<V> product, Quantity<V> quantity, DemandMode? mode = null) {
        Product = product;
        Quantity = quantity;
        Mode = mode;
    }

    /// <summary>产品 / Product.</summary>
    public Product<V> Product { get; }

    /// <summary>需求值 / Demand quantity.</summary>
    public Quantity<V> Quantity { get; }

    /// <summary>需求口径标签，仅用于语义标识 / Demand mode label for semantic tracing only.</summary>
    public DemandMode? Mode { get; }

    /// <summary>是否离散需求 / Whether discrete demand.</summary>
    public bool IsDiscrete => Quantity.Unit.Domain == QuantityDomain.Discrete;

    /// <summary>是否连续需求 / Whether continuous demand.</summary>
    public bool IsContinuous => Quantity.Unit.Domain == QuantityDomain.Continuous;

    /// <summary>按卷数口径创建需求 / Create demand with roll mode label.</summary>
    public static ProductDemand<V> Roll(Product<V> product, Quantity<V> quantity)
        => new(product, quantity, DemandMode.Roll);

    /// <summary>按重量口径创建需求 / Create demand with weight mode label.</summary>
    public static ProductDemand<V> Weight(Product<V> product, Quantity<V> quantity)
        => new(product, quantity, DemandMode.Weight);

    /// <summary>按张数口径创建需求 / Create demand with sheet mode label.</summary>
    public static ProductDemand<V> Sheet(Product<V> product, Quantity<V> quantity)
        => new(product, quantity, DemandMode.Sheet);

    /// <summary>legacy 卷数输入转换 / Legacy roll-amount input adapter.</summary>
    public static ProductDemand<V> LegacyRoll(Product<V> product, V rollAmount, PhysicalUnit? unit = null)
        => Roll(product, new Quantity<V>(rollAmount, unit ?? RollCountUnit.Instance));

    /// <summary>legacy 重量输入转换 / Legacy weight-amount input adapter.</summary>
    public static ProductDemand<V> LegacyWeight(Product<V> product, V weightAmount, PhysicalUnit? unit = null)
        => Weight(product, new Quantity<V>(weightAmount, unit ?? SIBaseUnits.Kilogram));

    /// <summary>legacy 张数输入转换 / Legacy sheet-amount input adapter.</summary>
    public static ProductDemand<V> LegacySheet(Product<V> product, V sheetAmount, PhysicalUnit? unit = null)
        => Sheet(product, new Quantity<V>(sheetAmount, unit ?? SheetCountUnit.Instance));
}
