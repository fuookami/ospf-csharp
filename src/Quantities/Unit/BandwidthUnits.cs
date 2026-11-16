#nullable enable

using Fuookami.Ospf.Math;
using Fuookami.Ospf.Quantities.Dimension;

namespace Fuookami.Ospf.Quantities.Unit;
/// <summary>
/// 带宽/数据速率单位 / Bandwidth/data rate units.
/// </summary>
public static class BandwidthUnits {
    /// <summary>比特每秒 / Bits per second.</summary>
    public static readonly PhysicalUnit BitsPerSecond = new DerivedUnit(
        SIBaseUnits.Bit.Divide(SIBaseUnits.Second), "bits per second", "bit/s");
    /// <summary>千比特每秒 / Kilobits per second.</summary>
    public static readonly PhysicalUnit KilobitsPerSecond = new DerivedUnit(BitsPerSecond.ScaleMultiply(Scale.Kilo), "kilobits per second", "kbit/s");
    /// <summary>兆比特每秒 / Megabits per second.</summary>
    public static readonly PhysicalUnit MegabitsPerSecond = new DerivedUnit(BitsPerSecond.ScaleMultiply(Scale.Mega), "megabits per second", "Mbit/s");
    /// <summary>吉比特每秒 / Gigabits per second.</summary>
    public static readonly PhysicalUnit GigabitsPerSecond = new DerivedUnit(BitsPerSecond.ScaleMultiply(Scale.Giga), "gigabits per second", "Gbit/s");
    /// <summary>字节每秒 / Bytes per second.</summary>
    public static readonly PhysicalUnit BytesPerSecond = new DerivedUnit(BitsPerSecond.ScaleMultiply(Scale.Invoke(8)), "bytes per second", "B/s");

    // 以下 5 个按基线 `Bandwidth.kt` 补齐（现存 5 个 → 10 个）。
    //
    // **带宽单位延续了 `Information` 的十进制/二进制之分**：
    // **`KilobytesPerSecond` 是 1000 B/s，`KibibytesPerSecond` 才是 1024 B/s。**
    // **基线只定义到十进制那一套，所以这里也只有十进制。**
    //
    // The five below were added from the baseline Bandwidth.kt (five existing, ten total).
    //
    // **Bandwidth keeps the decimal/binary split from `Information`**:
    // **`KilobytesPerSecond` is 1000 B/s and `KibibytesPerSecond` would be 1024.**
    // **The baseline defines only the decimal family, and so does this.**

    /// <summary>太比特每秒（10¹² bit/s）/ Terabit per second.</summary>
    public static readonly PhysicalUnit TerabitsPerSecond = new DerivedUnit(
        InformationUnits.Terabit.Divide(SIBaseUnits.Second), "terabit-per-second", "Tbit/s");
    /// <summary>拍比特每秒（10¹⁵）/ Petabit per second.</summary>
    public static readonly PhysicalUnit PetabitsPerSecond = new DerivedUnit(
        InformationUnits.Petabit.Divide(SIBaseUnits.Second), "petabit-per-second", "Pbit/s");
    /// <summary>艾比特每秒（10¹⁸）/ Exabit per second.</summary>
    public static readonly PhysicalUnit ExabitsPerSecond = new DerivedUnit(
        InformationUnits.Exabit.Divide(SIBaseUnits.Second), "exabit-per-second", "Ebit/s");
    /// <summary>**千字节每秒（1000 B/s，不是 1024）** / Kilobyte per second.</summary>
    public static readonly PhysicalUnit KilobytesPerSecond = new DerivedUnit(
        InformationUnits.Kilobyte.Divide(SIBaseUnits.Second), "kilobyte-per-second", "KB/s");
    /// <summary>兆字节每秒（10⁶ B/s）/ Megabyte per second.</summary>
    public static readonly PhysicalUnit MegabytesPerSecond = new DerivedUnit(
        InformationUnits.Megabyte.Divide(SIBaseUnits.Second), "megabyte-per-second", "MB/s");

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
