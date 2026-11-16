#nullable enable

using Fuookami.Ospf.Math;
using Fuookami.Ospf.Quantities.Dimension;

namespace Fuookami.Ospf.Quantities.Unit;

/// <summary>
/// 波数单位 / Wavenumber units.
/// </summary>
/// <remarks>
/// **按新建。** 有一个 `ReciprocalMeter`，**是米的倒数**。
///
/// **`Dimensions.cs` 里原本没有 `Wavenumber` 这个量纲**（实测 13 个缺失量纲里只有它连量纲都没有），
/// **所以这个文件同时是"单位 + 量纲"的第一处**——
/// **它用一个 `1/m` 的单位来代表量纲，而不是先在 `Dimensions` 里注册再引用。**
///
/// **Created from the code.** The code has one unit, `ReciprocalMeter`, the reciprocal of the metre.
///
/// **`Dimensions.cs` did not have a `Wavenumber` quantity** (of the 13 missing dimensions measured, this was the only
/// one missing even the dimension), **so this file is the first place where the unit and the dimension arrive together** —
/// **it represents the dimension with a `1/m` unit rather than registering it in `Dimensions` first.**
/// </remarks>
public static class WavenumberUnits
{
    /// <summary>**每米（波数）** / Reciprocal metre, as a wavenumber.</summary>
    /// <remarks>
    /// **波数是"每米多少个波"，与波长互为倒数**——**所以它是 `1/m` 而不是 `m`。**
    /// **把它当成长度单位用，数值会全部反过来，而类型系统不会拦。**
    ///
    /// **A wavenumber is "how many waves per metre", the reciprocal of wavelength** — **so it is `1/m`, not `m`.**
    /// **Use it as a length and every number inverts, with nothing in the type system to stop you.**
    /// </remarks>
    public static readonly PhysicalUnit ReciprocalMeter = new DerivedUnit(
        SIBaseUnits.Meter.ScaleDivide(Scale.Invoke(1.0)), "reciprocal meter", "m-1");

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
