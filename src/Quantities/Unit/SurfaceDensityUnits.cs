#nullable enable

using Fuookami.Ospf.Quantities.Dimension;

namespace Fuookami.Ospf.Quantities.Unit;

/// <summary>
/// 面密度单位 / Surface density units.
/// </summary>
/// <remarks>
/// **按新建。** 两个单位都是 `质量 / 面积`。
///
/// **`KilogramPerSquareMeter` 与 `GramPerSquareMeter` 相差 1000 倍**，
/// **而这两个名字只差一个词**——**与 `LitersPerMinute` / `LitersPerSecond` 是同一类风险。**
///
/// **Created.** Both are `mass / area`.
///
/// **`KilogramPerSquareMeter` and `GramPerSquareMeter` differ by 1000**,
/// **while their names differ by one word** — **the same risk as the earlier `LitersPerMinute` / `LitersPerSecond`.**
/// </remarks>
public static class SurfaceDensityUnits
{
    /// <summary>千克每平方米 / Kilograms per square metre.</summary>
    public static readonly PhysicalUnit KilogramsPerSquareMeter = new DerivedUnit(
        SIBaseUnits.Kilogram.Divide(AreaUnits.SquareMeter), "kilograms per square meter", "kg/m2");

    /// <summary>**克每平方米** / Grams per square metre.</summary>
    /// <remarks>
    /// **这个单位在纺织与造纸里极常用**（纸张克重就是 g/m²），
    /// **而它与 `KilogramsPerSquareMeter` 只差一个词、差 1000 倍。**
    ///
    /// **This unit is ubiquitous in textiles and paper** (paper weight is quoted in g/m²),
    /// **and it differs from `KilogramsPerSquareMeter` by one word and by 1000×.**
    /// </remarks>
    public static readonly PhysicalUnit GramsPerSquareMeter = new DerivedUnit(
        MassUnits.Gram.Divide(AreaUnits.SquareMeter), "grams per square meter", "g/m2");

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
