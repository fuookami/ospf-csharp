#nullable enable

using Fuookami.Ospf.Math;
using Fuookami.Ospf.Quantities.Dimension;

namespace Fuookami.Ospf.Quantities.Unit;

/// <summary>
/// 立体角单位 / Solid angle units.
/// </summary>
/// <remarks>
/// **B1129 按基线 `SolidAngle.kt` 新建。**
///
/// **`Steradian` 在基线里是 `PhysicalUnit()` 而不是 `DerivedPhysicalUnit(...)`**——
/// **它是 SI 导出单位，但相对 SI 基准的因子恰好是 1，所以没有"导出"这一步。**
///
/// **Created in B1129 from the baseline `SolidAngle.kt`.**
///
/// **`Steradian` is `PhysicalUnit()` in the baseline rather than `DerivedPhysicalUnit(...)`** —
/// **it is an SI derived unit whose factor against the SI base is exactly 1, so there is nothing to derive.**
/// </remarks>
public static class SolidAngleUnits
{
    /// <summary>球面度 / Steradian.</summary>
    public static readonly PhysicalUnit Steradian = new DerivedUnit(
        SIBaseUnits.Meter.ScaleMultiply(Scale.Invoke(1.0)), "steradian", "sr");

    /// <summary>**平方度** / Square degree.</summary>
    /// <remarks>
    /// **`0.00030461741978670857` 是 `(π/180)²`**，即"一度转弧度"的平方。
    /// **它是全角度球面 `4π` 的 1/41252.96**，**天文里用它报全天区覆盖。**
    ///
    /// **`0.00030461741978670857` is `(π/180)²`**, the square of one degree in radians.
    /// **The whole sphere is 41252.96 square degrees**, **and astronomy quotes sky coverage in it.**
    /// </remarks>
    public static readonly PhysicalUnit SquareDegree = new DerivedUnit(
        SIBaseUnits.Meter.ScaleMultiply(Scale.Invoke(0.00030461741978670857)), "square degree", "deg2");

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
