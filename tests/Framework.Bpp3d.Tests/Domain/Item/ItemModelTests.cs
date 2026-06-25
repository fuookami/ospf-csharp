#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using System;
using Xunit;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Domain.Item;

public class ItemModelTests {
    private static Quantity<FltX> M(double v) => new(new FltX(v), SIBaseUnits.Meter);
    private static Quantity<FltX> Kg(double v) => new(new FltX(v), SIBaseUnits.Kilogram);
    private static double D(Quantity<FltX> q) => q.Value.ToFlt64().ToDouble();

    [Fact]
    public void PackageAttributeShouldStoreAllProperties() {
        var attr = new PackageAttribute(
            PackageType.Box,
            MaxLayer: new UInt64(5),
            MaxHeight: new UInt64(200),
            MinDepth: new UInt64(10),
            MaxDepth: new UInt64(100),
            BottomOnly: true,
            TopFlat: false,
            EnabledSideOnTop: true,
            EnabledLieOnTop: true);

        attr.PackageType.Should().Be(PackageType.Box);
        attr.MaxLayer.Should().Be(new UInt64(5));
        attr.MaxHeight.Should().Be(new UInt64(200));
        attr.MinDepth.Should().Be(new UInt64(10));
        attr.MaxDepth.Should().Be(new UInt64(100));
        attr.BottomOnly.Should().BeTrue();
        attr.TopFlat.Should().BeFalse();
        attr.EnabledSideOnTop.Should().BeTrue();
        attr.EnabledLieOnTop.Should().BeTrue();
    }

    [Fact]
    public void PackageAttributeCategoryShouldBeStandardForBox() {
        var attr = new PackageAttribute(PackageType.Box, new UInt64(1), new UInt64(1), new UInt64(1), new UInt64(1));
        attr.Category.Should().Be(PackageCategory.Standard);
    }

    [Fact]
    public void PackageAttributeCategoryShouldBeStandardForPallet() {
        var attr = new PackageAttribute(PackageType.Pallet, new UInt64(1), new UInt64(1), new UInt64(1), new UInt64(1));
        attr.Category.Should().Be(PackageCategory.Standard);
    }

    [Fact]
    public void PackageAttributeCategoryShouldBeCylindricalForBarrel() {
        var attr = new PackageAttribute(PackageType.Barrel, new UInt64(1), new UInt64(1), new UInt64(1), new UInt64(1));
        attr.Category.Should().Be(PackageCategory.Cylindrical);
    }

    [Fact]
    public void PackageAttributeCategoryShouldBeCylindricalForRoll() {
        var attr = new PackageAttribute(PackageType.Roll, new UInt64(1), new UInt64(1), new UInt64(1), new UInt64(1));
        attr.Category.Should().Be(PackageCategory.Cylindrical);
    }

    [Fact]
    public void PackageAttributeCategoryShouldBeIrregularForBag() {
        var attr = new PackageAttribute(PackageType.Bag, new UInt64(1), new UInt64(1), new UInt64(1), new UInt64(1));
        attr.Category.Should().Be(PackageCategory.Irregular);
    }

    [Fact]
    public void PackageAttributeCategoryShouldBeIrregularForBundle() {
        var attr = new PackageAttribute(PackageType.Bundle, new UInt64(1), new UInt64(1), new UInt64(1), new UInt64(1));
        attr.Category.Should().Be(PackageCategory.Irregular);
    }

    [Fact]
    public void PackageAttributeDefaultValuesShouldBeCorrect() {
        var attr = new PackageAttribute(PackageType.Box, new UInt64(1), new UInt64(1), new UInt64(1), new UInt64(1));
        attr.BottomOnly.Should().BeFalse();
        attr.TopFlat.Should().BeTrue();
        attr.EnabledSideOnTop.Should().BeFalse();
        attr.EnabledLieOnTop.Should().BeFalse();
    }

    [Fact]
    public void BinTypeShouldStoreAllProperties() {
        var binType = new BinType<FltX>(
            "BIN-001", M(1.0), M(1.2), M(0.8), Kg(500), IsMain: true);
        binType.TypeCode.Should().Be("BIN-001");
        D(binType.Width).Should().BeApproximately(1.0, 1e-9);
        D(binType.Height).Should().BeApproximately(1.2, 1e-9);
        D(binType.Depth).Should().BeApproximately(0.8, 1e-9);
        binType.IsMain.Should().BeTrue();
    }

    [Fact]
    public void BinTypeVolumeShouldBeWidthTimesHeightTimesDepth() {
        var binType = new BinType<FltX>("B", M(2), M(3), M(4), Kg(1));
        D(binType.Volume).Should().BeApproximately(24.0, 1e-9);
    }

    [Fact]
    public void MaterialShouldStoreAllProperties() {
        var mat = new Material<FltX>(
            "M001", MaterialType.FinishedProduct, "Widget",
            Kg(2.5), Manufacturer: "Acme", Supplier: "SupplyCo", Warehouse: "WH-1");
        mat.No.Should().Be("M001");
        mat.Type.Should().Be(MaterialType.FinishedProduct);
        mat.Name.Should().Be("Widget");
        D(mat.Weight).Should().BeApproximately(2.5, 1e-9);
        mat.Manufacturer.Should().Be("Acme");
        mat.Supplier.Should().Be("SupplyCo");
        mat.Warehouse.Should().Be("WH-1");
    }

    [Fact]
    public void MaterialKeyShouldDeriveFromProperties() {
        var mat = new Material<FltX>("M001", MaterialType.RawMaterial, "Steel", Kg(1));
        MaterialKey key = mat.Key;
        key.No.Should().Be("M001");
        key.Type.Should().Be(MaterialType.RawMaterial);
    }

    [Fact]
    public void MaterialKeyShouldIncludeManufacturerAndSupplier() {
        var key = new MaterialKey("M001", MaterialType.SemiFinishedProduct, "Mfg", "Sup");
        key.No.Should().Be("M001");
        key.Type.Should().Be(MaterialType.SemiFinishedProduct);
        key.Manufacturer.Should().Be("Mfg");
        key.Supplier.Should().Be("Sup");
    }

    [Fact]
    public void GenericItemShouldStoreAllProperties() {
        var attr = new PackageAttribute(PackageType.Box, new UInt64(1), new UInt64(1), new UInt64(1), new UInt64(1));
        var item = new Item<FltX>("I001", "Item1", M(1), M(2), M(3), Kg(4), attr, BatchNo: "B1", Warehouse: "WH");
        item.Id.Should().Be("I001");
        item.Name.Should().Be("Item1");
        D(item.Width).Should().BeApproximately(1.0, 1e-9);
        D(item.Height).Should().BeApproximately(2.0, 1e-9);
        D(item.Depth).Should().BeApproximately(3.0, 1e-9);
        D(item.Weight).Should().BeApproximately(4.0, 1e-9);
        item.BatchNo.Should().Be("B1");
        item.Warehouse.Should().Be("WH");
    }

    [Fact]
    public void GenericItemVolumeShouldBeWidthTimesHeightTimesDepth() {
        var attr = new PackageAttribute(PackageType.Box, new UInt64(1), new UInt64(1), new UInt64(1), new UInt64(1));
        var item = new Item<FltX>("I001", "Item1", M(2), M(3), M(4), Kg(1), attr);
        D(item.Volume).Should().BeApproximately(24.0, 1e-9);
    }

    [Fact]
    public void BlockShouldStoreAllProperties() {
        var block = new Block<FltX>("B1", M(1), M(2), M(3), Kg(4));
        block.Id.Should().Be("B1");
        D(block.Width).Should().BeApproximately(1.0, 1e-9);
        D(block.Height).Should().BeApproximately(2.0, 1e-9);
        D(block.Depth).Should().BeApproximately(3.0, 1e-9);
        D(block.Weight).Should().BeApproximately(4.0, 1e-9);
    }

    [Fact]
    public void LayerShouldStoreAllProperties() {
        var layer = new Layer<FltX>("L1", M(5), Kg(10));
        layer.Id.Should().Be("L1");
        D(layer.Depth).Should().BeApproximately(5.0, 1e-9);
        D(layer.Weight).Should().BeApproximately(10.0, 1e-9);
    }

    [Fact]
    public void DemandReducedCostShouldStoreValues() {
        var drc = new DemandReducedCost("M001", 3.14);
        drc.MaterialNo.Should().Be("M001");
        drc.ReducedCost.Should().BeApproximately(3.14, 1e-9);
    }

    [Fact]
    public void DemandStatisticsShouldStoreValues() {
        var ds = new DemandStatistics("M001", 100);
        ds.MaterialNo.Should().Be("M001");
        ds.Amount.Should().Be(100);
    }

    [Fact]
    public void SchemaShouldStoreBatchNoAndItems() {
        var attr = new PackageAttribute(PackageType.Box, new UInt64(1), new UInt64(1), new UInt64(1), new UInt64(1));
        var item = new Item<FltX>("I1", "Item1", M(1), M(1), M(1), Kg(1), attr);
        var schema = new Schema<FltX>("B001", new[] { (item, new UInt64(10)) });
        schema.BatchNo.Should().Be("B001");
        schema.PatternedItems.Should().HaveCount(1);
        schema.PatternedItems[0].Amount.Should().Be(new UInt64(10));
    }

    [Fact]
    public void MaterialTypeShouldHaveExpectedValues() {
        System.Enum.GetValues<MaterialType>().Should().Contain(MaterialType.RawMaterial);
        System.Enum.GetValues<MaterialType>().Should().Contain(MaterialType.SemiFinishedProduct);
        System.Enum.GetValues<MaterialType>().Should().Contain(MaterialType.FinishedProduct);
    }

    [Fact]
    public void PackageTypeShouldHaveExpectedValues() {
        PackageType[] values = System.Enum.GetValues<PackageType>();
        values.Should().Contain(PackageType.Box);
        values.Should().Contain(PackageType.Bag);
        values.Should().Contain(PackageType.Barrel);
        values.Should().Contain(PackageType.Bundle);
        values.Should().Contain(PackageType.Pallet);
        values.Should().Contain(PackageType.Roll);
    }

    [Fact]
    public void PackageCategoryShouldHaveExpectedValues() {
        PackageCategory[] values = System.Enum.GetValues<PackageCategory>();
        values.Should().Contain(PackageCategory.Standard);
        values.Should().Contain(PackageCategory.Irregular);
        values.Should().Contain(PackageCategory.Cylindrical);
    }

    [Fact]
    public void QuantityDemandReducedCostShouldStoreValues() {
        var qdrc = new QuantityDemandReducedCost<FltX>("M001", Kg(2.5));
        qdrc.MaterialNo.Should().Be("M001");
        D(qdrc.ReducedCost).Should().BeApproximately(2.5, 1e-9);
    }

    [Fact]
    public void QuantityDemandStatisticsShouldStoreValues() {
        var qds = new QuantityDemandStatistics<FltX>("M001", Kg(10));
        qds.MaterialNo.Should().Be("M001");
        D(qds.Amount).Should().BeApproximately(10.0, 1e-9);
    }

    [Fact]
    public void ItemShadowPriceMapShouldBeInstantiable() {
        var map = new ItemShadowPriceMap();
        map.Should().NotBeNull();
        map.Prices.Should().BeEmpty();
    }
}
