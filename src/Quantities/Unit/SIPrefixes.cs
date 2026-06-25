#nullable enable

using Fuookami.Ospf.Math;

namespace Fuookami.Ospf.Quantities.Unit;
/// <summary>
/// SI 前缀缩放 / SI prefix scales.
/// </summary>
public static class SIPrefixes {
    /// <summary>千 / Kilo (10^3).</summary>
    public static readonly Scale Kilo = Scale.Kilo;
    /// <summary>兆 / Mega (10^6).</summary>
    public static readonly Scale Mega = Scale.Mega;
    /// <summary>吉 / Giga (10^9).</summary>
    public static readonly Scale Giga = Scale.Giga;
    /// <summary>太 / Tera (10^12).</summary>
    public static readonly Scale Tera = Scale.Tera;
    /// <summary>毫 / Milli (10^-3).</summary>
    public static readonly Scale Milli = Scale.Milli;
    /// <summary>微 / Micro (10^-6).</summary>
    public static readonly Scale Micro = Scale.Micro;
    /// <summary>纳 / Nano (10^-9).</summary>
    public static readonly Scale Nano = Scale.Nano;
    /// <summary>皮 / Pico (10^-12).</summary>
    public static readonly Scale Pico = Scale.Pico;
}
