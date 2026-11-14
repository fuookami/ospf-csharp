#nullable enable

using Fuookami.Ospf.Quantities.Dimension;

namespace Fuookami.Ospf.Quantities.Unit;

/// <summary>
/// 角速度单位 / Angular velocity units.
/// </summary>
/// <remarks>
/// **B1129 按基线 `AngularVelocity.kt` 新建，2 个单位。**
///
/// **角速度与频率同量纲**（都是 `1/s`），**所以 `RadianPerSecond` 与 `Hertz` 在量纲上不可区分**——
/// **这与 B1128 的力矩/能量是同一类 SI 取舍**：**"每秒转多少弧度"与"每秒多少个周期"数值差 `2π`，**
/// **而量纲完全一样。**
///
/// **Created in B1129 from the baseline `AngularVelocity.kt`, two units.**
///
/// **Angular velocity and frequency share a dimension** (both `1/s`), **so `RadianPerSecond` and `Hertz` are
/// dimensionally indistinguishable** — **the same kind of SI trade-off as B1128's torque and energy**:
/// **"how many radians per second" and "how many cycles per second" differ by `2π`,**
/// **with identical dimensions.**
/// </remarks>
public static class AngularVelocityUnits
{
    /// <summary>弧度每秒 / Radians per second.</summary>
    public static readonly PhysicalUnit RadianPerSecond = new DerivedUnit(
        AngleUnits.Radian.Divide(SIBaseUnits.Second), "radians per second", "rad/s");

    /// <summary>**度每秒** / Degrees per second.</summary>
    /// <remarks>
    /// **`Degree` 是 `π/180` 弧度**，**所以度每秒不是弧度每秒的整数分之一**——
    /// **写成 1/360 会把"每秒 1 度"算成"每秒 0.00278 弧度"而不是 0.01745。**
    ///
    /// **`Degree` is `π/180` radians**, **so degrees per second is not an integer fraction of radians per second** —
    /// **writing 1/360 makes "one degree per second" 0.00278 rad/s instead of 0.01745.**
    /// </remarks>
    public static readonly PhysicalUnit DegreePerSecond = new DerivedUnit(
        AngleUnits.Degree.Divide(SIBaseUnits.Second), "degrees per second", "deg/s");

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
