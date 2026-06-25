#nullable enable

using Fuookami.Ospf.Math;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Dimension;
using System;

namespace Fuookami.Ospf.Quantities.Unit;
/// <summary>
/// 单位转换规则 / Unit conversion rule.
/// sealed interface -> abstract record + sealed subtypes.
/// </summary>
public abstract record UnitConversionRule {
    /// <summary>线性比例因子 / Linear scale factor.</summary>
    public abstract Scale Scale { get; init; }

    /// <summary>是否为仿射转换 / Whether this is an affine conversion.</summary>
    public bool IsAffine => this is Affine;

    /// <summary>
    /// 线性转换规则 / Linear conversion rule.
    /// standard = value * scale.
    /// </summary>
    public sealed record Linear : UnitConversionRule {
        public override Scale Scale { get; init; }
        public Linear(Scale scale) { Scale = scale; }
    }

    /// <summary>
    /// 仿射转换规则 / Affine conversion rule.
    /// standard = value * scale + offset.
    /// </summary>
    public sealed record Affine : UnitConversionRule {
        public override Scale Scale { get; init; }
        public FltX Offset { get; init; }
        public Affine(Scale scale, FltX offset) { Scale = scale; Offset = offset; }
    }
}

/// <summary>
/// 物理单位抽象类 / Abstract physical unit.
/// </summary>
public abstract class PhysicalUnit {
    /// <summary>单位名称 / Unit name.</summary>
    public abstract string? Name { get; }

    /// <summary>单位符号 / Unit symbol.</summary>
    public abstract string? Symbol { get; }

    /// <summary>单位量纲 / Unit dimension.</summary>
    public abstract DerivedQuantity Quantity { get; }

    /// <summary>取值域 / Value domain.</summary>
    public virtual QuantityDomain Domain => Quantity.Domain;

    /// <summary>转换规则 / Conversion rule to standard unit.</summary>
    public abstract UnitConversionRule ConversionRule { get; }

    /// <summary>比例 / Scale.</summary>
    public Scale Scale => ConversionRule.Scale;

    /// <summary>是否为仿射单位 / Whether affine.</summary>
    public bool IsAffine => ConversionRule.IsAffine;

    /// <summary>检查量纲是否相同 / Check if dimensions match.</summary>
    public bool SameDimension(PhysicalUnit other) => Quantity.Equals(other.Quantity);

    /// <summary>
    /// 线性转换比例因子 / Linear conversion scale factor.
    /// Returns null if either end is affine or dimensions differ.
    /// </summary>
    public Scale? To(PhysicalUnit unit) {
        if (!Quantity.Equals(unit.Quantity)) {
            return null;
        }

        if (IsAffine || unit.IsAffine) {
            return null;
        }

        return Scale / unit.Scale;
    }

    /// <summary>从另一个单位转换过来 / Convert from another unit.</summary>
    public Scale? From(PhysicalUnit unit) => unit.To(this);

    /// <summary>
    /// 将值从此单位转换到目标单位 / Convert a value from this unit to target.
    /// Supports both linear and affine conversions.
    /// </summary>
    public FltX? ConvertValue(FltX value, PhysicalUnit unit) {
        if (!Quantity.Equals(unit.Quantity)) {
            return null;
        }

        UnitConversionRule thisRule = ConversionRule;
        UnitConversionRule targetRule = unit.ConversionRule;

        if (thisRule is UnitConversionRule.Linear srcLin && targetRule is UnitConversionRule.Linear tgtLin) {
            FltX? factor = (srcLin.Scale / tgtLin.Scale)?.Value;
            if (factor == null) {
                return null;
            }

            return value * factor;
        }
        if (thisRule is UnitConversionRule.Linear srcLin2 && targetRule is UnitConversionRule.Affine tgtAff) {
            FltX? srcScale = srcLin2.Scale.Value;
            FltX? tgtScale = tgtAff.Scale.Value;
            if (srcScale == null || tgtScale == null) {
                return null;
            }

            return (value * srcScale - tgtAff.Offset) / tgtScale;
        }
        if (thisRule is UnitConversionRule.Affine srcAff && targetRule is UnitConversionRule.Linear tgtLin2) {
            FltX? srcScale = srcAff.Scale.Value;
            FltX? tgtScale = tgtLin2.Scale.Value;
            if (srcScale == null || tgtScale == null) {
                return null;
            }

            return (value * srcScale + srcAff.Offset) / tgtScale;
        }
        if (thisRule is UnitConversionRule.Affine srcAff2 && targetRule is UnitConversionRule.Affine tgtAff2) {
            FltX? srcScale = srcAff2.Scale.Value;
            FltX? tgtScale = tgtAff2.Scale.Value;
            if (srcScale == null || tgtScale == null) {
                return null;
            }

            return (value * srcScale + srcAff2.Offset - tgtAff2.Offset) / tgtScale;
        }
        return null;
    }

    /// <summary>检查是否可以转换 / Check if convertible.</summary>
    public bool CanConvertTo(PhysicalUnit unit) => Quantity.Equals(unit.Quantity);

    /// <inheritdoc/>
    public override bool Equals(object? obj) {
        if (obj is not PhysicalUnit other) {
            return false;
        }

        if (ReferenceEquals(this, other)) {
            return true;
        }

        return Quantity.Equals(other.Quantity)
            && ConversionRule.Equals(other.ConversionRule)
            && Domain == other.Domain;
    }

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Quantity, ConversionRule, Domain);

    /// <inheritdoc/>
    public override string ToString() => Symbol ?? Name ?? $"{Quantity.DimensionSymbol()}({Scale})";
}

/// <summary>
/// 导出物理单位抽象类 / Derived physical unit abstract class.
/// </summary>
public abstract class DerivedPhysicalUnit : PhysicalUnit {
    private readonly PhysicalUnit _unit;

    protected DerivedPhysicalUnit(PhysicalUnit unit) {
        _unit = unit;
    }

    /// <inheritdoc/>
    public override DerivedQuantity Quantity => _unit.Quantity;

    /// <inheritdoc/>
    public override QuantityDomain Domain => _unit.Domain;

    /// <inheritdoc/>
    public override UnitConversionRule ConversionRule => _unit.ConversionRule;
}

/// <summary>
/// 匿名物理单位 / Anonymous physical unit.
/// Used for dynamically created unit instances (e.g. unit arithmetic).
/// </summary>
public sealed class AnonymousPhysicalUnit : PhysicalUnit {
    public AnonymousPhysicalUnit(
        DerivedQuantity quantity,
        UnitConversionRule conversionRule,
        string? name = null,
        string? symbol = null,
        QuantityDomain? domain = null) {
        _quantity = quantity;
        _conversionRule = conversionRule;
        _name = name;
        _symbol = symbol;
        _domain = domain ?? quantity.Domain;
    }

    private readonly DerivedQuantity _quantity;
    private readonly UnitConversionRule _conversionRule;
    private readonly string? _name;
    private readonly string? _symbol;
    private readonly QuantityDomain _domain;

    public override string? Name => _name;
    public override string? Symbol => _symbol;
    public override DerivedQuantity Quantity => _quantity;
    public override QuantityDomain Domain => _domain;
    public override UnitConversionRule ConversionRule => _conversionRule;

    public override bool Equals(object? obj) {
        if (obj is not PhysicalUnit other) {
            return false;
        }

        if (ReferenceEquals(this, other)) {
            return true;
        }

        return Quantity.Equals(other.Quantity)
            && ConversionRule.Equals(other.ConversionRule)
            && Domain == other.Domain;
    }

    public override int GetHashCode() => HashCode.Combine(Quantity, ConversionRule, Domain);

    public override string ToString() => Symbol ?? "";
}

/// <summary>
/// 无单位（无量纲）/ No unit (dimensionless).
/// </summary>
public sealed class NoneUnit : PhysicalUnit {
    public static readonly NoneUnit Instance = new();
    private NoneUnit() { }

    public override string? Name => null;
    public override string? Symbol => null;
    public override DerivedQuantity Quantity => DerivedQuantity.Dimensionless;
    public override UnitConversionRule ConversionRule => new UnitConversionRule.Linear(Scale.Invoke(1));
}

/// <summary>
/// 量纲单位（无量纲）/ Quantity unit (dimensionless).
/// </summary>
public sealed class QuantityUnit : PhysicalUnit {
    public QuantityUnit(string? name = null, string? symbol = null) {
        _name = name;
        _symbol = symbol;
    }

    private readonly string? _name;
    private readonly string? _symbol;

    public override string? Name => _name;
    public override string? Symbol => _symbol;
    public override DerivedQuantity Quantity => DerivedQuantity.Dimensionless;
    public override UnitConversionRule ConversionRule => new UnitConversionRule.Linear(Scale.Invoke(1));
}

// ============================================================================
// 单位运算符 / Unit Operators
// ============================================================================

/// <summary>
/// PhysicalUnit 运算扩展 / PhysicalUnit operator extensions.
/// </summary>
public static class PhysicalUnitOperators {
    private static void RequireLinear(PhysicalUnit unit, string operation) {
        if (unit.IsAffine) {
            throw new InvalidOperationException($"Cannot {operation} affine unit '{unit}'. Use a linear difference unit instead.");
        }
    }

    /// <summary>单位与 Scale 相乘 / Multiply unit by Scale.</summary>
    public static PhysicalUnit ScaleMultiply(this PhysicalUnit unit, Scale scale) {
        RequireLinear(unit, "scale");
        return new AnonymousPhysicalUnit(unit.Quantity, new UnitConversionRule.Linear(unit.Scale * scale), domain: unit.Domain);
    }

    /// <summary>单位与 Scale 相除 / Divide unit by Scale.</summary>
    public static PhysicalUnit ScaleDivide(this PhysicalUnit unit, Scale scale) {
        RequireLinear(unit, "scale");
        Scale divResult = unit.Scale / scale;
        return new AnonymousPhysicalUnit(unit.Quantity, new UnitConversionRule.Linear(divResult ?? throw new DivideByZeroException()), domain: unit.Domain);
    }

    /// <summary>单位与整数相乘 / Multiply unit by int.</summary>
    public static PhysicalUnit Multiply(this PhysicalUnit unit, int scale) {
        RequireLinear(unit, "scale");
        return new AnonymousPhysicalUnit(unit.Quantity, new UnitConversionRule.Linear(unit.Scale * Scale.Invoke(scale)), domain: unit.Domain);
    }

    /// <summary>单位与 double 相乘 / Multiply unit by double.</summary>
    public static PhysicalUnit Multiply(this PhysicalUnit unit, double scale) {
        RequireLinear(unit, "scale");
        return new AnonymousPhysicalUnit(unit.Quantity, new UnitConversionRule.Linear(unit.Scale * Scale.Invoke(scale)), domain: unit.Domain);
    }

    /// <summary>两个单位相乘 / Multiply two units.</summary>
    public static PhysicalUnit Multiply(this PhysicalUnit lhs, PhysicalUnit rhs) {
        RequireLinear(lhs, "multiply");
        RequireLinear(rhs, "multiply");
        return new AnonymousPhysicalUnit(lhs.Quantity * rhs.Quantity, new UnitConversionRule.Linear(lhs.Scale * rhs.Scale));
    }

    /// <summary>两个单位相除 / Divide two units.</summary>
    public static PhysicalUnit Divide(this PhysicalUnit lhs, PhysicalUnit rhs) {
        RequireLinear(lhs, "divide");
        RequireLinear(rhs, "divide");
        return new AnonymousPhysicalUnit(lhs.Quantity / rhs.Quantity, new UnitConversionRule.Linear(lhs.Scale / rhs.Scale));
    }

    /// <summary>单位的幂运算 / Power operation on unit.</summary>
    public static PhysicalUnit Pow(this PhysicalUnit unit, int index) {
        RequireLinear(unit, "raise");
        if (index == 0) {
            return NoneUnit.Instance;
        }

        if (index > 0) {
            PhysicalUnit result = unit;
            for (int i = 1; i < index; i++) {
                result = result.Multiply(unit);
            }

            return result;
        }
        else {
            PhysicalUnit result = NoneUnit.Instance.Divide(unit);
            for (int i = 1; i < -index; i++) {
                result = result.Divide(unit);
            }

            return result;
        }
    }

    /// <summary>单位的倒数 / Reciprocal of unit.</summary>
    public static PhysicalUnit Reciprocal(this PhysicalUnit unit) => NoneUnit.Instance.Divide(unit);
}
