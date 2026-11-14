#nullable enable

using Fuookami.Ospf.Math;
using Fuookami.Ospf.Quantities.Dimension;

namespace Fuookami.Ospf.Quantities.Unit;
/// <summary>
/// 电荷单位 / Electric charge units.
/// </summary>
public static class ElectricChargeUnits {
    /// <summary>库仑 / Coulomb (SI derived: A*s).</summary>
    public static readonly PhysicalUnit Coulomb = new DerivedUnit(
        SIBaseUnits.Ampere.Multiply(SIBaseUnits.Second), "coulomb", "C");
    /// <summary>毫库 / Millicoulomb.</summary>
    public static readonly PhysicalUnit Millicoulomb = new DerivedUnit(Coulomb.ScaleMultiply(Scale.Milli), "millicoulomb", "mC");
    /// <summary>微库 / Microcoulomb.</summary>
    public static readonly PhysicalUnit Microcoulomb = new DerivedUnit(Coulomb.ScaleMultiply(Scale.Micro), "microcoulomb", "uC");
    /// <summary>安时 / Ampere-hour (1 Ah = 3600 C).</summary>
    public static readonly PhysicalUnit AmpereHour = new DerivedUnit(Coulomb.ScaleMultiply(Scale.Invoke(3600)), "ampere-hour", "Ah");

    // 以下 6 个是 B1142 按基线 ElectricCharge.kt 补的。
    //
    // **法拉系列不在这个文件里**：**基线把它写在这里，却声明 `Capacitance` 量纲。**
    // **跟着文件名会建成电荷单位——所以它们在 `CapacitanceUnits`。**
    //
    // The six below were added in B1142 from the baseline ElectricCharge.kt.
    //
    // **The farad family is not here**: **the baseline writes it in this file while declaring `Capacitance`.**
    // **Following the file name would build charge units, so they live in `CapacitanceUnits`.**

    /// <summary>千库（10³ C）/ Kilocoulomb.</summary>
    public static readonly PhysicalUnit Kilocoulomb =
        new DerivedUnit(Coulomb.ScaleMultiply(Scale.Kilo), "kilocoulomb", "kC");

    /// <summary>**安秒（与库仑是同一个量）** / Ampere-second, the same quantity as the coulomb.</summary>
    /// <remarks>
    /// **1 A·s = 1 C，所以它与库仑数值相同而名字不同。**
    /// **两个都保留：电化学文献写 A·s，电学写 C。**
    /// **判据用 `BeSameAs` 之外的"比值恰为 1"，因为它必须是独立的单位对象（名字不同），
    /// 而不是库仑的别名——否则按名字查 A·s 会查到"coulomb"。**
    ///
    /// **1 A·s = 1 C, so it equals the coulomb in value under a different name.**
    /// **Both stay: electrochemistry writes A·s, electronics writes C.**
    /// **The criterion checks the ratio is exactly 1 rather than `BeSameAs`, because it has to be its own unit**
    /// **object (different name) and not an alias — a caller looking up A·s would otherwise get "coulomb".**
    /// </remarks>
    public static readonly PhysicalUnit AmpereSecond =
        new DerivedUnit(Coulomb.ScaleMultiply(Scale.Invoke(1)), "ampere-second", "A*s");

    /// <summary>毫安时（1 mAh = 3.6 C）/ Milliampere-hour.</summary>
    public static readonly PhysicalUnit MilliampereHour =
        new DerivedUnit(Coulomb.ScaleMultiply(Scale.Invoke(3.6)), "milliampere-hour", "mAh");
    /// <summary>微安时（1 uAh = 0.0036 C）/ Microampere-hour.</summary>
    public static readonly PhysicalUnit MicroampereHour =
        new DerivedUnit(Coulomb.ScaleMultiply(Scale.Invoke(0.0036)), "microampere-hour", "uAh");
    /// <summary>千安时（1 kAh = 3.6e6 C）/ Kiloampere-hour.</summary>
    public static readonly PhysicalUnit KiloampereHour =
        new DerivedUnit(Coulomb.ScaleMultiply(Scale.Invoke(3600000)), "kiloampere-hour", "kAh");

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
