#nullable enable

using Fuookami.Ospf.Quantities.Dimension;

namespace Fuookami.Ospf.Quantities.Unit;

/// <summary>
/// 质量密度单位 / Mass density units.
/// </summary>
/// <remarks>
/// **按新建。** 四个单位全部是 `质量 / 体积`。
///
/// **四个里有一个数量级差别极大**：`KilogramPerCubicMeter`（水约 1000）与
/// `GramPerCubicCentimeter`（水正好 1）——**它们描述同一杯水，而数值差 1000 倍**，
/// **因为前者用 SI 基本单位、后者用 CGS 惯例。**
///
/// **Created.** All four are `mass / volume`.
///
/// **One pair differs by three orders of magnitude**: `KilogramPerCubicMeter` (water is about 1000) and
/// `GramPerCubicCentimeter` (water is exactly 1) — **they describe the same glass of water with values 1000 apart**,
/// **because the first uses SI base units and the second follows the CGS convention.**
/// </remarks>
public static class MassDensityUnits
{
    /// <summary>千克每立方米 / Kilograms per cubic metre.</summary>
    public static readonly PhysicalUnit KilogramsPerCubicMeter = new DerivedUnit(
        SIBaseUnits.Kilogram.Divide(VolumeUnits.CubicMeter), "kilograms per cubic meter", "kg/m3");

    /// <summary>千克每升 / Kilograms per litre.</summary>
    public static readonly PhysicalUnit KilogramsPerLiter = new DerivedUnit(
        SIBaseUnits.Kilogram.Divide(VolumeUnits.Liter), "kilograms per liter", "kg/L");

    /// <summary>千克每立方厘米 / Kilograms per cubic centimetre.</summary>
    public static readonly PhysicalUnit KilogramsPerCubicCentimeter = new DerivedUnit(
        SIBaseUnits.Kilogram.Divide(VolumeUnits.CubicCentimeter), "kilograms per cubic centimeter", "kg/cm3");

    /// <summary>**克每立方厘米** / Grams per cubic centimetre.</summary>
    /// <remarks>
    /// **这个单位不是"千克每立方厘米除以 1000"就完了**：它同时换了质量与体积两把尺子。
    /// **把它与 `KilogramsPerCubicMeter` 混淆，水的密度会从 1000 变成 1**——
    /// **而 1 这个数看起来完全正常，只是单位错了。**
    ///
    /// **This is not simply "kilograms per cubic centimetre divided by 1000"**: it changes both the mass and the volume ruler.
    /// **Confuse it with `KilogramsPerCubicMeter` and water's density goes from 1000 to 1** —
    /// **and 1 looks like a perfectly ordinary number, only the unit is wrong.**
    /// </remarks>
    public static readonly PhysicalUnit GramsPerCubicCentimeter = new DerivedUnit(
        MassUnits.Gram.Divide(VolumeUnits.CubicCentimeter), "grams per cubic centimeter", "g/cm3");

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
