#nullable enable

using Fuookami.Ospf.Math;
using Fuookami.Ospf.Quantities.Dimension;

namespace Fuookami.Ospf.Quantities.Unit;
/// <summary>
/// 电容单位 / Capacitance units.
/// </summary>
/// <remarks>
/// B1142 新建。**基线把法拉系列写在 `ElectricCharge.kt` 里，但声明的是 `Capacitance` 量纲**——
/// **电荷是 C，电容是 C/V，两者不是同一个量。**
///
/// **跟着文件名会把它建成电荷单位，跟着声明的量纲才对**——
/// **所以这里独立成文件，并且用 `Coulomb.Divide(Volt)` 让量纲自己算出来**，
/// **而不是手写一个"看起来是电容"的包装。**
///
/// Created in B1142. **The baseline writes the farad family inside `ElectricCharge.kt` but declares
/// `Capacitance`** — **charge is coulombs, capacitance is coulombs per volt, and they differ.**
///
/// **Following the file name builds charge units; following the declared quantity is right** —
/// **so this is its own file, and `Coulomb.Divide(Volt)` lets the dimensions work themselves out** rather than a
/// hand-written wrapper that merely looks like capacitance.
/// </remarks>
public static class CapacitanceUnits {
    /// <summary>法拉（1 F = 1 C/V）/ Farad.</summary>
    public static readonly PhysicalUnit Farad = new DerivedUnit(
        ElectricChargeUnits.Coulomb.Divide(VoltageUnits.Volt), "farad", "F");

    /// <summary>毫法（10⁻³ F）/ Millifarad.</summary>
    public static readonly PhysicalUnit Millifarad =
        new DerivedUnit(Farad.ScaleMultiply(Scale.Milli), "millifarad", "mF");
    /// <summary>微法（10⁻⁶ F）/ Microfarad.</summary>
    public static readonly PhysicalUnit Microfarad =
        new DerivedUnit(Farad.ScaleMultiply(Scale.Micro), "microfarad", "uF");
    /// <summary>纳法（10⁻⁹ F）/ Nanofarad.</summary>
    public static readonly PhysicalUnit Nanofarad =
        new DerivedUnit(Farad.ScaleMultiply(Scale.Nano), "nanofarad", "nF");
    /// <summary>**皮法（10⁻¹² F）** / Picofarad.</summary>
    /// <remarks>
    /// **电容里最常用的量级就是 pF 与 nF**：**射频与寄生电容几乎全在 pF**，
    /// **所以这条链一旦在 nano 与 pico 之间写错一个数量级，最常见的场景就全错了。**
    ///
    /// **pF and nF are the common scales in capacitance**: **RF and stray capacitance live almost entirely in pF**,
    /// **so a slip of one decade between nano and pico breaks the most common case.**
    /// </remarks>
    public static readonly PhysicalUnit Picofarad =
        new DerivedUnit(Farad.ScaleMultiply(Scale.Pico), "picofarad", "pF");

    private sealed class DerivedUnit : PhysicalUnit {
        private readonly PhysicalUnit _inner;
        private readonly string _name;
        private readonly string _symbol;
        public DerivedUnit(PhysicalUnit inner, string name, string symbol) { _inner = inner; _name = name; _symbol = symbol; }
        public override string Name => _name;
        public override string Symbol => _symbol;
        public override DerivedQuantity Quantity => _inner.Quantity;
        public override UnitConversionRule ConversionRule => _inner.ConversionRule;
    }
}
