#nullable enable

using Fuookami.Ospf.Quantities.Dimension;

namespace Fuookami.Ospf.Quantities.Unit;

/// <summary>
/// 流量单位 / Flow rate units.
/// </summary>
/// <remarks>
/// **按基线 `FlowRate.kt` 新建。** 基线的三个流量单位全部是 `体积 / 时间`，
/// **所以它们复用体积与时间单位，不各写一个数。**
///
/// **Created from the baseline `FlowRate.kt`.** All three baseline flow units are `volume / time`,
/// **so they reuse the volume and time units rather than each carrying a number.**
/// </remarks>
public static class FlowRateUnits
{
    /// <summary>立方米每秒 / Cubic metres per second.</summary>
    public static readonly PhysicalUnit CubicMetersPerSecond = new DerivedUnit(
        VolumeUnits.CubicMeter.Divide(SIBaseUnits.Second), "cubic meters per second", "m3/s");

    /// <summary>升每秒 / Litres per second.</summary>
    public static readonly PhysicalUnit LitersPerSecond = new DerivedUnit(
        VolumeUnits.Liter.Divide(SIBaseUnits.Second), "liters per second", "L/s");

    /// <summary>**升每分钟** / Litres per minute.</summary>
    /// <remarks>
    /// **这个单位是"每分钟"而不是"每秒"**，**所以它不能与上面两个混用**：
    /// **把 L/min 的数当成 L/s 用，结果差 60 倍**——
    /// **而两者都叫"升每…"，只差一个词。**
    ///
    /// **This one is per minute, not per second**, **so it cannot be mixed with the two above**:
    /// **treating an L/min figure as L/s is off by a factor of 60** —
    /// **and both are called "litres per something", differing by one word.**
    /// </remarks>
    public static readonly PhysicalUnit LitersPerMinute = new DerivedUnit(
        VolumeUnits.Liter.Divide(TimeUnits.Minute), "liters per minute", "L/min");

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
