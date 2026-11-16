#nullable enable

using Fuookami.Ospf.Quantities.Dimension;

namespace Fuookami.Ospf.Quantities.Unit;

/// <summary>
/// 动量单位 / Momentum units.
/// </summary>
/// <remarks>
/// **按新建。** 有一个动量单位，**它是 `kg·m/s`，不是 `N·s`**——
/// **两者数值相同、量纲相同，但写成 `kg·m/s` 让动量与"质量 × 速度"这个定义直接对应**，
/// **而写成 `N·s` 会让它看起来像冲量。**
///
/// **Created from the code.** The code has one momentum unit, **and it is `kg·m/s`, not `N·s`** —
/// **the two agree in value and dimension, but `kg·m/s` mirrors the definition "mass × velocity"**,
/// **while `N·s` makes it look like an impulse.**
/// </remarks>
public static class MomentumUnits
{
    /// <summary>千克米每秒 / Kilogram metres per second.</summary>
    public static readonly PhysicalUnit KilogramMetersPerSecond = new DerivedUnit(
        SIBaseUnits.Kilogram.Multiply(SIBaseUnits.Meter).Divide(SIBaseUnits.Second),
        "kilogram meters per second", "kg·m/s");

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
