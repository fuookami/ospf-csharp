#nullable enable

using Fuookami.Ospf.Math;
using Fuookami.Ospf.Quantities.Dimension;

namespace Fuookami.Ospf.Quantities.Unit;
/// <summary>
/// 频率单位 / Frequency units.
/// </summary>
public static class FrequencyUnits {
    /// <summary>赫兹 / Hertz (1 Hz = 1/s).</summary>
    public static readonly PhysicalUnit Hertz = new DerivedUnit(SIBaseUnits.Second.Reciprocal(), "hertz", "Hz");
    /// <summary>千赫 / Kilohertz.</summary>
    public static readonly PhysicalUnit Kilohertz = new DerivedUnit(Hertz.ScaleMultiply(Scale.Kilo), "kilohertz", "kHz");
    /// <summary>兆赫 / Megahertz.</summary>
    public static readonly PhysicalUnit Megahertz = new DerivedUnit(Hertz.ScaleMultiply(Scale.Mega), "megahertz", "MHz");
    /// <summary>吉赫 / Gigahertz.</summary>
    public static readonly PhysicalUnit Gigahertz = new DerivedUnit(Hertz.ScaleMultiply(Scale.Giga), "gigahertz", "GHz");

    /// <summary>**每小时次数（1/3600 Hz）** / Cycle per hour.</summary>
    /// <remarks>
    /// **基线写的是 `Hertz * Second.to(Hour)`，而 `Second.to(Hour)` 是"一秒折合多少小时" = 1/3600。**
    /// **所以这个单位比赫兹小 3600 倍，而不是大 3600 倍——**
    /// **"每小时一次"当然比"每秒一次"慢，方向必须是除。**
    ///
    /// **The baseline writes `Hertz * Second.to(Hour)`, and `Second.to(Hour)` is "how many hours one second is"
    /// = 1/3600.** **So this unit is 3600 times smaller than the hertz, not larger** —
    /// **once per hour is obviously slower than once per second, so the direction has to be a division.**
    /// </remarks>
    public static readonly PhysicalUnit CyclePerHour =
        new DerivedUnit(Hertz.ScaleMultiply(Scale.Invoke(1.0 / 3600.0)), "cycle-per-hour", "cph");

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
