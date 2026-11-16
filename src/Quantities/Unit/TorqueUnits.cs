#nullable enable

using Fuookami.Ospf.Quantities.Dimension;

namespace Fuookami.Ospf.Quantities.Unit;

/// <summary>
/// 力矩单位 / Torque units.
/// </summary>
/// <remarks>
/// **按新建。**
///
/// **力矩与能量的量纲在 SI 里是同一个**（都是 `kg·m²/s²`），
/// **所以 `NewtonMeter` 与能量单位 `Joule` 可以互换而编译器不会拦**——
/// **这是 SI 的已知设计取舍，不是本仓的缺陷**，但**判据要钉住"力矩单位确实按力乘长度构造"**，
/// **否则某天有人把 `NewtonMeter` 直接指向 `Joule`，数值全对而语义全丢。**
///
/// **Created from the code.**
///
/// **Torque and energy share a dimension in SI** (both `kg·m²/s²`),
/// **so `NewtonMeter` and the energy unit `Joule` are interchangeable and the compiler will not object** —
/// **that is a known SI trade-off, not a defect here** — but **the criteria pin that the torque units are built
/// as force times length**, **or someone will one day point `NewtonMeter` straight at `Joule`, with every number right
/// and every meaning gone.**
/// </remarks>
public static class TorqueUnits
{
    /// <summary>牛顿米 / Newton metres.</summary>
    public static readonly PhysicalUnit NewtonMeter = new DerivedUnit(
        ForceUnits.Newton.Multiply(SIBaseUnits.Meter), "newton meter", "N·m");

    /// <summary>**千克力米** / Kilogram force metres.</summary>
    /// <remarks>
    /// **`KilogramForce` 是 9.80665 N**（标准重力下的 1 kg），**不是 1 N**——
    /// **写成 1 N 会让所有以 kgf·m 标注的扭矩差 9.8 倍。**
    ///
    /// **`KilogramForce` is 9.80665 N** (1 kg under standard gravity), **not 1 N** —
    /// **writing 1 N makes every torque quoted in kgf·m off by 9.8×.**
    /// </remarks>
    public static readonly PhysicalUnit KilogramForceMeter = new DerivedUnit(
        ForceUnits.KilogramForce.Multiply(SIBaseUnits.Meter), "kilogram force meter", "kgf·m");

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
