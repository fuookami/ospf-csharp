#nullable enable

using Fuookami.Ospf.Math;
using Fuookami.Ospf.Quantities.Dimension;

namespace Fuookami.Ospf.Quantities.Unit;

/// <summary>
/// 发光强度单位 / Luminous intensity units.
/// </summary>
/// <remarks>
/// **B1129 按基线 `LuminousIntensity.kt` 新建。** 基线只有一个 `Candela`，**它是七个 SI 基本单位之一**，
/// **所以这里不是"新建一个导出单位"，而是"给已有的基本单位一个入口"。**
///
/// **Created in B1129 from the baseline `LuminousIntensity.kt`.** The baseline has one unit, `Candela`, **which is one of the
/// seven SI base units**, **so this is not a new derived unit but an entry point to an existing base unit.**
/// </remarks>
public static class LuminousIntensityUnits
{
    /// <summary>**坎德拉** / Candela.</summary>
    /// <remarks>
    /// **`Candela` 已经在 `SIBaseUnits` 里**，这里指向它而不是再建一个——
    /// **再建一个会让"基本单位的坎德拉"与"发光强度的坎德拉"成为两个不相等的值，**
    /// **而它们在 SI 里必须是同一个。**
    ///
    /// **`Candela` already lives in `SIBaseUnits`**, and this points at it rather than building another —
    /// **a second one would make "the base unit candela" and "the luminous-intensity candela" two unequal values,**
    /// **while in SI they must be the same.**
    /// </remarks>
    public static readonly PhysicalUnit Candela = SIBaseUnits.Candela;

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
