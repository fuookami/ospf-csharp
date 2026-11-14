#nullable enable

using Fuookami.Ospf.Math;
using Fuookami.Ospf.Quantities.Dimension;

namespace Fuookami.Ospf.Quantities.Unit;
/// <summary>
/// 信息单位 / Information units.
/// </summary>
/// <remarks>
/// **这一批修掉了一个真实错误，而它属于最容易被放过的那一类。**
///
/// B1138 按基线 `Information.kt` 核对时发现：**本仓原来的 `Kilobyte` 用的是 1024**，
/// **而基线的 `Kilobyte` 是 `Scale.kilo × 8 = 8000`**。**1024 是 `Kibibyte` 的值。**
/// `Megabyte`/`Gigabyte` 同样错了（用了 1024²、1024³）。
///
/// **这个错误不会报错，只会让"1 GB 的磁盘"按二进制解释——差 7.4%。**
/// **而"KB 到底是 1000 还是 1024"在业界本来就被混用，所以谁也不会怀疑代码。**
///
/// **这一批修掉这个错误，并把十进制（KB/MB/GB…）与二进制（KiB/MiB/GiB…）两套都建全。**
///
/// **This batch fixes a real defect, and it belongs to the easiest kind to let through.**
///
/// Checking against the baseline's `Information.kt` in B1138 turned up that **this repo's `Kilobyte` used 1024**,
/// **while the baseline's is `Scale.kilo × 8 = 8000`**. **1024 is `Kibibyte`'s value.**
/// `Megabyte`/`Gigabyte` were wrong the same way (1024², 1024³).
///
/// **Nothing throws — it only makes "a 1 GB disk" read as binary, off by 7.4%.**
/// **And "is a KB 1000 or 1024" is genuinely mixed in practice, so nobody suspects the code.**
///
/// **This batch fixes that and builds out both families: decimal (KB/MB/GB…) and binary (KiB/MiB/GiB…).**
/// </remarks>
public static class InformationUnits {
    // **基准是 bit**：十进制字节单位是 `10^n × 8`，二进制字节单位是 `1024^n × 8`。
    // **两套的符号也不同**（`KB` 对 `KiB`），所以混用可以从符号上看出来——
    // **前提是两者都建出来了；原来只建了"KB 但值是 KiB"，符号与数值对不上，从外面完全看不出来。**
    //
    // **The base is the bit**: decimal byte units are `10^n × 8`, binary ones are `1024^n × 8`.
    // **The symbols differ too** (`KB` versus `KiB`), so a mix-up shows in the symbol —
    // **provided both exist; the original had "KB holding KiB's value", where symbol and magnitude disagree invisibly.**

    /// <summary>比特 / Bit.</summary>
    public static readonly PhysicalUnit Bit = SIBaseUnits.Bit;

    /// <summary>千比特（10³）/ Kilobit.</summary>
    public static readonly PhysicalUnit Kilobit = new DerivedUnit(Bit.ScaleMultiply(Scale.Kilo), "kilobit", "kbit");
    /// <summary>兆比特（10⁶）/ Megabit.</summary>
    public static readonly PhysicalUnit Megabit = new DerivedUnit(Bit.ScaleMultiply(Scale.Mega), "megabit", "Mbit");
    /// <summary>吉比特（10⁹）/ Gigabit.</summary>
    public static readonly PhysicalUnit Gigabit = new DerivedUnit(Bit.ScaleMultiply(Scale.Giga), "gigabit", "Gbit");
    /// <summary>太比特（10¹²）/ Terabit.</summary>
    public static readonly PhysicalUnit Terabit = new DerivedUnit(Bit.ScaleMultiply(Scale.Tera), "terabit", "Tbit");
    /// <summary>拍比特（10¹⁵）/ Petabit.</summary>
    public static readonly PhysicalUnit Petabit = new DerivedUnit(Bit.ScaleMultiply(Scale.Peta), "petabit", "Pbit");
    /// <summary>艾比特（10¹⁸）/ Exabit.</summary>
    public static readonly PhysicalUnit Exabit = new DerivedUnit(Bit.ScaleMultiply(Scale.Exa), "exabit", "Ebit");

    /// <summary>字节（8 bit）/ Byte.</summary>
    public static readonly PhysicalUnit Byte = new DerivedUnit(Bit.ScaleMultiply(Scale.Invoke(8)), "byte", "B");

    /// <summary>**千字节（1000 字节，不是 1024）** / Kilobyte, one thousand bytes.</summary>
    /// <remarks>
    /// **1000，不是 1024。** **1024 是 `Kibibyte`。**
    /// **本仓原来这里写的是 1024，而符号写着 `KB`**——**符号与数值对不上，从外面完全看不出来。**
    ///
    /// **One thousand, not 1024.** **1024 is `Kibibyte`.**
    /// **This repo used to say 1024 here while the symbol said `KB`** — **symbol and magnitude disagreed, invisibly.**
    /// </remarks>
    public static readonly PhysicalUnit Kilobyte = new DerivedUnit(Byte.ScaleMultiply(Scale.Kilo), "kilobyte", "KB");
    /// <summary>兆字节（10⁶）/ Megabyte.</summary>
    public static readonly PhysicalUnit Megabyte = new DerivedUnit(Byte.ScaleMultiply(Scale.Mega), "megabyte", "MB");
    /// <summary>吉字节（10⁹）/ Gigabyte.</summary>
    public static readonly PhysicalUnit Gigabyte = new DerivedUnit(Byte.ScaleMultiply(Scale.Giga), "gigabyte", "GB");
    /// <summary>太字节（10¹²）/ Terabyte.</summary>
    public static readonly PhysicalUnit Terabyte = new DerivedUnit(Byte.ScaleMultiply(Scale.Tera), "terabyte", "TB");
    /// <summary>拍字节（10¹⁵）/ Petabyte.</summary>
    public static readonly PhysicalUnit Petabyte = new DerivedUnit(Byte.ScaleMultiply(Scale.Peta), "petabyte", "PB");
    /// <summary>艾字节（10¹⁸）/ Exabyte.</summary>
    public static readonly PhysicalUnit Exabyte = new DerivedUnit(Byte.ScaleMultiply(Scale.Exa), "exabyte", "EB");

    /// <summary>**基比字节（1024 字节）** / Kibibyte, 1024 bytes.</summary>
    public static readonly PhysicalUnit Kibibyte =
        new DerivedUnit(Byte.ScaleMultiply(Scale.Invoke(1024)), "kibibyte", "KiB");
    /// <summary>**兆比字节（1024²）** / Mebibyte.</summary>
    public static readonly PhysicalUnit Mebibyte =
        new DerivedUnit(Byte.ScaleMultiply(Scale.Invoke(1048576)), "mebibyte", "MiB");
    /// <summary>**吉比字节（1024³）** / Gibibyte.</summary>
    public static readonly PhysicalUnit Gibibyte =
        new DerivedUnit(Byte.ScaleMultiply(Scale.Invoke(1073741824)), "gibibyte", "GiB");
    /// <summary>**太比字节（1024⁴）** / Tebibyte.</summary>
    public static readonly PhysicalUnit Tebibyte =
        new DerivedUnit(Byte.ScaleMultiply(Scale.Invoke(1099511627776)), "tebibyte", "TiB");

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
