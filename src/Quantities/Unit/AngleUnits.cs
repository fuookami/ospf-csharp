#nullable enable

using Fuookami.Ospf.Math;
using Fuookami.Ospf.Quantities.Dimension;

namespace Fuookami.Ospf.Quantities.Unit;
/// <summary>
/// 角度单位 / Angle units.
/// </summary>
public static class AngleUnits {
    /// <summary>弧度 / Radian (SI).</summary>
    public static readonly PhysicalUnit Radian = SIBaseUnits.Radian;
    /// <summary>度 / Degree (1 deg = pi/180 rad).</summary>
    public static readonly PhysicalUnit Degree = new DerivedUnit(
        Radian.ScaleMultiply(Scale.Invoke(0.017453292519943295)), "degree", "deg");
    /// <summary>角分 / Arcminute.</summary>
    public static readonly PhysicalUnit Arcminute = new DerivedUnit(Degree.ScaleMultiply(Scale.Invoke(1.0 / 60.0)), "arcminute", "'");
    /// <summary>角秒 / Arcsecond.</summary>
    public static readonly PhysicalUnit Arcsecond = new DerivedUnit(Degree.ScaleMultiply(Scale.Invoke(1.0 / 3600.0)), "arcsecond", "\"");

    // 以下 4 个角度单位与 2 个基线拼写别名是 B1143 按基线 PlaneAngle.kt 补的。
    //
    // **`RightAngle` 没有照抄基线：基线写 `Radian / 4.0`，而直角是 π/2 弧度。**
    // **正确的式子是把整个圆周（`RoundAngle` = 2π）四等分，即 `RoundAngle / 4`。**
    // 照抄会得到 0.25 rad（约 14.3°），**而"直角的一半是 45°"这类检查仍会通过**，
    // **因为错误的直角与由它派生的 Gradian 会一起错，比值依旧是 100。**
    //
    // The four angle units and two baseline spellings below were added in B1143 from PlaneAngle.kt.
    //
    // **`RightAngle` is not copied: the baseline writes `Radian / 4.0`, while a right angle is pi/2 radians.**
    // **The correct expression quarters the full turn (`RoundAngle` = 2pi), i.e. `RoundAngle / 4`.**
    // Copied as written it yields 0.25 rad (about 14.3 degrees), **while checks like "half a right angle is 45"
    // still pass**, **because the wrong right angle and the Gradian derived from it go wrong together and the ratio
    // stays 100.**

    /// <summary>**毫弧度（10⁻³ rad）** / Milliradian.</summary>
    public static readonly PhysicalUnit Milliradian = new DerivedUnit(
        Radian.ScaleMultiply(Scale.Milli), "milliradian", "mrad");

    /// <summary>**周角（2π rad）** / Full turn.</summary>
    public static readonly PhysicalUnit RoundAngle = new DerivedUnit(
        Radian.ScaleMultiply(Scale.Invoke(2.0 * global::System.Math.PI)), "round angle", "turn");

    /// <summary>**直角（π/2 rad，即周角四等分）** / Right angle.</summary>
    /// <remarks>
    /// **由 `RoundAngle / 4` 派生，不是 `Radian / 4`**——**后者是基线写的，得到 0.25 rad。**
    /// **而 90 度必须是 1 个直角：这条等式同时钉住 `Degree` 与 `RightAngle`。**
    ///
    /// **Derived as `RoundAngle / 4`, not `Radian / 4`** — **the latter is what the baseline writes, yielding
    /// 0.25 rad.** **And 90 degrees has to be one right angle: that identity pins both `Degree` and `RightAngle`.**
    /// </remarks>
    public static readonly PhysicalUnit RightAngle = new DerivedUnit(
        Radian.ScaleMultiply(Scale.Invoke(global::System.Math.PI / 2.0)), "right angle", "right angle");

    /// <summary>**百分度（直角的百分之一，即 π/200）** / Gradian.</summary>
    public static readonly PhysicalUnit Gradian = new DerivedUnit(
        Radian.ScaleMultiply(Scale.Invoke(global::System.Math.PI / 200.0)), "gradian", "gon");

    /// <summary>**角分（基线拼写）** / Arcminute, the baseline spelling.</summary>
    /// <remarks>
    /// **本仓原来是 `Arcminute`（小写 m），基线是 `ArcMinute`。两个都保留**——
    /// **按名字查的调用方只认其中一个。**
    ///
    /// **This repo had `Arcminute` (lowercase m), the baseline has `ArcMinute`. Both stay** —
    /// **a caller looking up by name knows only one of them.**
    /// </remarks>
    public static readonly PhysicalUnit ArcMinute = Arcminute;

    /// <summary>**角秒（基线拼写）** / Arcsecond, the baseline spelling.</summary>
    public static readonly PhysicalUnit ArcSecond = Arcsecond;
    /// <summary>球面度 / Steradian.</summary>
    public static readonly PhysicalUnit Steradian = SIBaseUnits.Steradian;

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
