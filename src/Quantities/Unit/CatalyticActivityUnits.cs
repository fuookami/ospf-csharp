#nullable enable

using Fuookami.Ospf.Math;
using Fuookami.Ospf.Quantities.Dimension;

namespace Fuookami.Ospf.Quantities.Unit;

/// <summary>
/// 催化活度单位 / Catalytic activity units.
/// </summary>
/// <remarks>
/// **按基线 `CatalyticActivity.kt` 新建，4 个单位。**
///
/// **`EnzymeUnit` 是这里里唯一"不是十进倍数"的单位**：**它是 `Katal / 60000000`**，
/// **因为 1 U = 1 µmol/min，而 1 katal = 1 mol/s**——
/// **换算里同时有 10⁻⁶（微）与 60（分转秒），合起来就是 6×10⁷。**
///
/// **Created from the baseline `CatalyticActivity.kt`, four units.**
///
/// **`EnzymeUnit` is the only non-decimal unit here**: **it is `Katal / 60000000`**,
/// **because 1 U = 1 µmol/min while 1 katal = 1 mol/s** —
/// **the conversion carries both 10⁻⁶ (micro) and 60 (minute to second), which together make 6×10⁷.**
/// </remarks>
public static class CatalyticActivityUnits
{
    /// <summary>开特 / Katal (mol/s).</summary>
    public static readonly PhysicalUnit Katal = new DerivedUnit(
        SIBaseUnits.Mole.Divide(SIBaseUnits.Second), "katal", "kat");

    /// <summary>毫开特 / Millikatal.</summary>
    public static readonly PhysicalUnit Millikatal = new DerivedUnit(
        SIBaseUnits.Mole.Divide(SIBaseUnits.Second).ScaleMultiply(Scale.Milli), "millikatal", "mkat");

    /// <summary>微开特 / Microkatal.</summary>
    public static readonly PhysicalUnit Microkatal = new DerivedUnit(
        SIBaseUnits.Mole.Divide(SIBaseUnits.Second).ScaleMultiply(Scale.Micro), "microkatal", "ukat");

    /// <summary>**酶单位 U** / Enzyme unit.</summary>
    /// <remarks>
    /// **1 U = 1 µmol/min = 1/60000000 katal。**
    /// **这个数不能简化成"六千万分之一"就完了**：**它是两个不同换算（微与分）乘出来的结果**，
    /// **所以它既不是十进倍数，也不是任何单一 `Scale` 前缀。**
    ///
    /// **1 U = 1 µmol/min = 1/60000000 katal.**
    /// **The number is not simply "one sixty-millionth"**: **it is the product of two separate conversions (micro and minute)**,
    /// **so it is neither a decimal multiple nor any single `Scale` prefix.**
    /// </remarks>
    public static readonly PhysicalUnit EnzymeUnit = new DerivedUnit(
        SIBaseUnits.Mole.Divide(SIBaseUnits.Second).ScaleDivide(Scale.Invoke(60000000.0)), "enzyme unit", "U");

    private sealed class DerivedUnit : PhysicalUnit
    {
        private readonly PhysicalUnit _inner;
        private readonly string _name;
        private readonly string _symbol;

        public DerivedUnit(PhysicalUnit inner, string name, string symbol)
        {
            _inner = inner;
            _name = name;
            _symbol = symbol;
        }

        public override string Name => _name;

        public override string Symbol => _symbol;

        public override DerivedQuantity Quantity => _inner.Quantity;

        public override UnitConversionRule ConversionRule => _inner.ConversionRule;
    }
}
