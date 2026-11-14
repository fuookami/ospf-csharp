// 量纲包裹的判据 / Criteria for quantity packages.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Domain;

/// <summary>
/// **体积是量纲相乘、外层材料是子包裹之和** / The volume is a product of quantities and an outer package's material map is the sum of its children.
/// </summary>
/// <remarks>
/// 这两条都是"看起来对、其实错"的地方：体积若按三个数值相乘，量纲检查就失效；外层材料若后写覆盖，
/// 重复材料只会留下最后一箱。判据必须分别打到这两处。
///
/// Both are "looks right, is wrong" spots: a volume built from three numbers defeats dimensional checking, and an outer material map that overwrites keeps only the last
/// box. The criteria must hit each of them separately.
/// </remarks>
public class QuantityPackageTests {
    /// **体积是 `米³` 而不是无量纲数** / The volume carries `m³`, not a bare number.
    [Fact]
    public void Volume_ShouldCarryCubicMeter() {
        QuantityPackageShape<Flt64> shape = ShapeOf(2.0, 3.0, 4.0);

        shape.Volume.Value.ToDouble().Should().BeApproximately(24.0, 1e-12);
        shape.Volume.Unit.Should().NotBe(shape.Width.Unit,
            "a cubic metre is not a metre; equal units would mean the multiplication lost a dimension");
    }

    /// **外层包裹的材料是子包裹之和** / An outer package sums its children's materials.
    [Fact]
    public void OuterPackage_ShouldSumMaterialsAcrossChildren() {
        QuantityMaterial<Flt64> steel = MaterialOf("M1");
        QuantityMaterial<Flt64> plastic = MaterialOf("M2");
        var first = QuantityPackage<Flt64>.InnerPackage(
            ShapeOf(1.0, 1.0, 1.0), Map((steel, 2UL), (plastic, 1UL)));
        var second = QuantityPackage<Flt64>.InnerPackage(
            ShapeOf(1.0, 1.0, 1.0), Map((steel, 3UL)));

        var outer = QuantityPackage<Flt64>.OuterPackage(
            ShapeOf(2.0, 1.0, 1.0), new List<QuantityPackage<Flt64>> { first, second });

        outer.Materials.Should().ContainKey(steel);
        outer.Materials[steel].Should().Be(5UL, "2 plus 3 of the same material");
        outer.Materials[plastic].Should().Be(1UL);
        outer.Packages.Should().HaveCount(2);
    }

    /// <summary>
    /// **同一种材料出现两次要相加而不是覆盖** / A material appearing twice accumulates rather than overwrites.
    /// </summary>
    /// <remarks>
    /// 这条单独写，是因为上一版判据里两个子包裹的材料不同，**覆盖式实现照样通过**——
    /// 只有让同一材料在两个子包裹里都出现，才分得清"累加"与"覆盖"。
    ///
    /// This one is separate because an earlier criterion used two children with *different* materials, and **an overwriting implementation passed it** — only repeating
    /// the same material across children distinguishes accumulation from overwriting.
    /// </remarks>
    [Fact]
    public void OuterPackage_ShouldAccumulateARepeatedMaterial() {
        QuantityMaterial<Flt64> steel = MaterialOf("M1");
        var first = QuantityPackage<Flt64>.InnerPackage(
            ShapeOf(1.0, 1.0, 1.0), Map((steel, 7UL)));
        var second = QuantityPackage<Flt64>.InnerPackage(
            ShapeOf(1.0, 1.0, 1.0), Map((steel, 5UL)));

        var outer = QuantityPackage<Flt64>.OuterPackage(
            ShapeOf(2.0, 1.0, 1.0), new List<QuantityPackage<Flt64>> { first, second });

        outer.Materials[steel].Should().Be(12UL, "overwriting would leave 5 and look plausible");
    }

    /// **内层包裹保留自己的材料表** / An inner package keeps its own material map.
    [Fact]
    public void InnerPackage_ShouldKeepItsOwnMaterials() {
        QuantityMaterial<Flt64> steel = MaterialOf("M1");

        var inner = QuantityPackage<Flt64>.InnerPackage(
            ShapeOf(1.0, 1.0, 1.0), Map((steel, 4UL)));

        inner.Materials[steel].Should().Be(4UL);
        inner.Packages.Should().BeNull("an inner package has no children");
        inner.Volume.Value.ToDouble().Should().BeApproximately(1.0, 1e-12);
    }

    /// **材料转模型保留单位与字段** / Converting a material to the model keeps its unit and fields.
    [Fact]
    public void QuantityMaterial_ShouldConvertKeepingTheUnit() {
        var quantityMaterial = new QuantityMaterial<Flt64>(
            "M1", MaterialType.RawMaterial, "steel", Kilograms(2.5),
            Manufacturer: "acme", Warehouse: "W1");

        Material<FltX> model = quantityMaterial.ToModel();

        model.No.Should().Be("M1");
        model.Name.Should().Be("steel");
        model.Manufacturer.Should().Be("acme");
        model.Warehouse.Should().Be("W1");
        model.Weight.Value.ToFlt64().ToDouble().Should().BeApproximately(2.5, 1e-12);
        model.Weight.Unit.Should().Be(Kilograms(1.0).Unit, "the unit must survive the conversion");
    }

    /// <summary>构造包裹形状 / Build a package shape.</summary>
    /// <param name="width">宽 / The width.</param>
    /// <param name="height">高 / The height.</param>
    /// <param name="depth">深 / The depth.</param>
    /// <returns>形状 / The shape.</returns>
    private static QuantityPackageShape<Flt64> ShapeOf(double width, double height, double depth) =>
        new(Metres(width), Metres(height), Metres(depth), Kilograms(1.0), PackageType.Box);

    /// <summary>构造材料 / Build a material.</summary>
    /// <param name="no">编号 / The number.</param>
    /// <returns>材料 / The material.</returns>
    private static QuantityMaterial<Flt64> MaterialOf(string no) =>
        new(no, MaterialType.RawMaterial, "m" + no, Kilograms(1.0));

    /// <summary>构造材料映射 / Build a material map.</summary>
    /// <param name="entries">材料与数量 / Materials and amounts.</param>
    /// <returns>映射 / The map.</returns>
    private static IReadOnlyDictionary<QuantityMaterial<Flt64>, ulong> Map(
        params (QuantityMaterial<Flt64> Material, ulong Amount)[] entries) {
        var map = new Dictionary<QuantityMaterial<Flt64>, ulong>();
        foreach ((QuantityMaterial<Flt64> material, ulong amount) in entries) {
            map[material] = amount;
        }
        return map;
    }

    /// <summary>构造米量 / Build a length in metres.</summary>
    /// <param name="value">数值 / The value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> Metres(double value) => new(new Flt64(value), SIBaseUnits.Meter);

    /// <summary>构造千克量 / Build a mass in kilograms.</summary>
    /// <param name="value">数值 / The value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> Kilograms(double value) => new(new Flt64(value), SIBaseUnits.Kilogram);
}
