#nullable enable

using Fuookami.Ospf.Math;
using Fuookami.Ospf.Quantities.Dimension;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Quantities.Unit;
/// <summary>
/// 单位制接口 / Unit system interface.
/// </summary>
public interface IUnitSystem {
    /// <summary>单位制名称 / Unit system name.</summary>
    string Name { get; }

    /// <summary>基本单位映射 / Base units by fundamental dimension.</summary>
    IReadOnlyDictionary<IFundamentalQuantityDimension, PhysicalUnit> BaseUnits { get; }

    /// <summary>用户指定的标准单位 / User-specified standard units.</summary>
    IDictionary<DerivedQuantity, PhysicalUnit> StandardUnits { get; }

    /// <summary>导出单位缓存 / Derived units cache.</summary>
    IDictionary<DerivedQuantity, PhysicalUnit> DerivedCache { get; }

    /// <summary>获取指定量纲的标准单位 / Get standard unit for a dimension.</summary>
    PhysicalUnit? GetStandardUnit(DerivedQuantity quantity);
}

/// <summary>具体单位制实现 / Concrete unit system implementation.</summary>
public sealed record ConcreteUnitSystem(
    string Name,
    IReadOnlyDictionary<IFundamentalQuantityDimension, PhysicalUnit> BaseUnits,
    IDictionary<DerivedQuantity, PhysicalUnit> StandardUnits,
    IDictionary<DerivedQuantity, PhysicalUnit> DerivedCache) : IUnitSystem {
    public PhysicalUnit? GetStandardUnit(DerivedQuantity quantity) {
        if (StandardUnits.TryGetValue(quantity, out PhysicalUnit? std)) {
            return std;
        }

        return UnitForDimension(quantity);
    }

    private PhysicalUnit? UnitForDimension(DerivedQuantity quantity) {
        if (quantity.Quantities.Count == 1) {
            FundamentalQuantity fq = quantity.Quantities[0];
            if (fq.Index == 1 && BaseUnits.TryGetValue(fq.Dimension, out PhysicalUnit? baseUnit)) {
                return baseUnit;
            }
        }

        if (DerivedCache.TryGetValue(quantity, out PhysicalUnit? cached)) {
            return cached;
        }

        PhysicalUnit? unit = DeriveUnit(quantity);
        if (unit != null) {
            DerivedCache[quantity] = unit;
        }

        return unit;
    }

    private PhysicalUnit? DeriveUnit(DerivedQuantity quantity) {
        var resultScale = Scale.Invoke(1);
        var symbolParts = new List<string>();

        foreach (FundamentalQuantity fq in quantity.Quantities) {
            if (!BaseUnits.TryGetValue(fq.Dimension, out PhysicalUnit? baseUnit)) {
                return null;
            }

            int power = fq.Index;
            if (power == 0) {
                continue;
            }

            Scale unitScale = baseUnit.Scale;
            if (power == 1) {
                resultScale = resultScale * unitScale;
            }
            else if (power == -1) {
                Scale div = resultScale / unitScale;
                if (div == null) {
                    return null;
                }

                resultScale = div;
            }
            else {
                var powScale = Scale.Invoke(1);
                int absPower = System.Math.Abs(power);
                for (int i = 0; i < absPower; i++) {
                    powScale = powScale * unitScale;
                }

                if (power > 0) {
                    resultScale = resultScale * powScale;
                }
                else {
                    Scale div = resultScale / powScale;
                    if (div == null) {
                        return null;
                    }

                    resultScale = div;
                }
            }

            string unitSym = baseUnit.Symbol ?? "";
            if (power == 1) {
                symbolParts.Add(unitSym);
            }
            else if (power == -1) {
                symbolParts.Add($"{unitSym}/");
            }
            else {
                symbolParts.Add($"{unitSym}^{power}");
            }
        }

        string symbol = string.Join("·", symbolParts);
        string name = $"derived_{quantity.DimensionSymbol()}";
        return new AnonymousPhysicalUnit(quantity, new UnitConversionRule.Linear(resultScale), name, symbol);
    }
}

/// <summary>单位制构造器 / Fluent builder for custom unit systems.</summary>
public sealed class UnitSystemBuilder {
    private readonly string _name;
    private readonly Dictionary<IFundamentalQuantityDimension, PhysicalUnit> _baseUnits;
    private readonly Dictionary<DerivedQuantity, PhysicalUnit> _derivedUnits;
    private readonly Dictionary<DerivedQuantity, PhysicalUnit> _standardUnits;

    public UnitSystemBuilder(string name) {
        _name = name;
        _baseUnits = new Dictionary<IFundamentalQuantityDimension, PhysicalUnit>();
        _derivedUnits = new Dictionary<DerivedQuantity, PhysicalUnit>();
        _standardUnits = new Dictionary<DerivedQuantity, PhysicalUnit>();
    }

    public UnitSystemBuilder(string name, IUnitSystem prototype) {
        _name = name;
        _baseUnits = new Dictionary<IFundamentalQuantityDimension, PhysicalUnit>(prototype.BaseUnits);
        _derivedUnits = new Dictionary<DerivedQuantity, PhysicalUnit>();
        _standardUnits = new Dictionary<DerivedQuantity, PhysicalUnit>(prototype.StandardUnits);
    }

    public UnitSystemBuilder AddBaseUnit(IFundamentalQuantityDimension dim, PhysicalUnit unit) {
        _baseUnits[dim] = unit;
        return this;
    }

    public UnitSystemBuilder SetStandardUnit(DerivedQuantity q, PhysicalUnit unit) {
        _standardUnits[q] = unit;
        return this;
    }

    public IUnitSystem Build() {
        return new ConcreteUnitSystem(
            _name,
            new Dictionary<IFundamentalQuantityDimension, PhysicalUnit>(_baseUnits),
            new ConcurrentDictionary<DerivedQuantity, PhysicalUnit>(_standardUnits),
            new ConcurrentDictionary<DerivedQuantity, PhysicalUnit>(_derivedUnits));
    }
}

// ============================================================================
// SI / MKS / CGS 单位制 / Unit systems
// ============================================================================

/// <summary>
/// SI 单位制 / SI unit system singleton.
/// </summary>
public static class SI {
    public static readonly IUnitSystem Instance = new ConcreteUnitSystem(
        "SI",
        BaseUnits: new Dictionary<IFundamentalQuantityDimension, PhysicalUnit> {
            [Dims.L] = SIBaseUnits.Meter,
            [Dims.M] = SIBaseUnits.Kilogram,
            [Dims.T] = SIBaseUnits.Second,
            [Dims.I] = SIBaseUnits.Ampere,
            [Dims.Theta] = SIBaseUnits.Kelvin,
            [Dims.N] = SIBaseUnits.Mole,
            [Dims.J] = SIBaseUnits.Candela,
            [Dims.B] = SIBaseUnits.Bit,
            [Dims.rad] = SIBaseUnits.Radian,
            [Dims.sr] = SIBaseUnits.Steradian,
        },
        StandardUnits: new ConcurrentDictionary<DerivedQuantity, PhysicalUnit>(),
        DerivedCache: new ConcurrentDictionary<DerivedQuantity, PhysicalUnit>());
}

/// <summary>MKS 单位制 / MKS unit system singleton.</summary>
public static class MKS {
    public static readonly IUnitSystem Instance = new ConcreteUnitSystem(
        "MKS",
        BaseUnits: new Dictionary<IFundamentalQuantityDimension, PhysicalUnit> {
            [Dims.L] = SIBaseUnits.Meter,
            [Dims.M] = SIBaseUnits.Kilogram,
            [Dims.T] = SIBaseUnits.Second,
        },
        StandardUnits: new ConcurrentDictionary<DerivedQuantity, PhysicalUnit>(),
        DerivedCache: new ConcurrentDictionary<DerivedQuantity, PhysicalUnit>());
}

/// <summary>CGS 单位制 / CGS unit system singleton.</summary>
public static class CGS {
    public static readonly IUnitSystem Instance = new ConcreteUnitSystem(
        "CGS",
        BaseUnits: new Dictionary<IFundamentalQuantityDimension, PhysicalUnit> {
            [Dims.L] = LengthUnits.Centimeter,
            [Dims.M] = MassUnits.Gram,
            [Dims.T] = SIBaseUnits.Second,
        },
        StandardUnits: new ConcurrentDictionary<DerivedQuantity, PhysicalUnit>(),
        DerivedCache: new ConcurrentDictionary<DerivedQuantity, PhysicalUnit>());
}
