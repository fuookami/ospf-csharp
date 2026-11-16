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

    // 以下 4 个角度单位与 2 个拼写别名。
    //
    // **`RightAngle` 不取 `Radian / 4.0`：直角是 π/2 弧度。**
    // **正确的式子是把整个圆周（`RoundAngle` = 2π）四等分，即 `RoundAngle / 4`。**
    // 照 `Radian / 4.0` 写会得到 0.25 rad（约 14.3°），**而"直角的一半是 45°"这类检查仍会通过**，
    // **因为错误的直角与由它派生的 Gradian 会一起错，比值依旧是 100。**
    //
    // The four angle units and two spelling aliases below.
    //
    // **`RightAngle` is not `Radian / 4.0`: a right angle is pi/2 radians.**
    // **The correct expression quarters the full turn (`RoundAngle` = 2pi), i.e. `RoundAngle / 4`.**
    // Written as `Radian / 4.0` it yields 0.25 rad (about 14.3 degrees), **while checks like "half a right angle is 45"
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
    /// **由 `RoundAngle / 4` 派生，不是 `Radian / 4`**——**后者会得到 0.25 rad。**
    /// **而 90 度必须是 1 个直角：这条等式同时钉住 `Degree` 与 `RightAngle`。**
    ///
    /// **Derived as `RoundAngle / 4`, not `Radian / 4`** — **the latter yields
    /// 0.25 rad.** **And 90 degrees has to be one right angle: that identity pins both `Degree` and `RightAngle`.**
    /// </remarks>
    public static readonly PhysicalUnit RightAngle = new DerivedUnit(
        Radian.ScaleMultiply(Scale.Invoke(global::System.Math.PI / 2.0)), "right angle", "right angle");

    /// <summary>**百分度（直角的百分之一，即 π/200）** / Gradian.</summary>
    public static readonly PhysicalUnit Gradian = new DerivedUnit(
        Radian.ScaleMultiply(Scale.Invoke(global::System.Math.PI / 200.0)), "gradian", "gon");

    /// <summary>**角分（另一种拼写）** / Arcminute, the alternate spelling.</summary>
    /// <remarks>
    /// **本仓有 `Arcminute`（小写 m）与 `ArcMinute` 两种拼写，两个都保留**——
    /// **按名字查的调用方只认其中一个。**
    ///
    /// **Both `Arcminute` (lowercase m) and `ArcMinute` exist and both stay** —
    /// **a caller looking up by name knows only one of them.**
    /// </remarks>
    public static readonly PhysicalUnit ArcMinute = Arcminute;

    /// <summary>**角秒（另一种拼写）** / Arcsecond, the alternate spelling.</summary>
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
