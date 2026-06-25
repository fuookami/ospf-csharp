#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp3d.Infra;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Geometry;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Infra;

public class InfrastructureTypesTests {
    private static Quantity<FltX> M(double v) => new(new FltX(v), SIBaseUnits.Meter);
    private static double D(Quantity<FltX> q) => q.Value.ToFlt64().ToDouble();

    [Fact]
    public void Container3ShapeShouldStoreDimensions() {
        var shape = new Container3Shape<FltX>(M(2), M(3), M(4));
        D(shape.Width).Should().BeApproximately(2.0, 1e-9);
        D(shape.Height).Should().BeApproximately(3.0, 1e-9);
        D(shape.Depth).Should().BeApproximately(4.0, 1e-9);
    }

    [Fact]
    public void QuantityPoint3ShouldStoreCoordinates() {
        var pt = new QuantityPoint3<FltX>(M(1), M(2), M(3));
        D(pt.X).Should().BeApproximately(1.0, 1e-9);
        D(pt.Y).Should().BeApproximately(2.0, 1e-9);
        D(pt.Z).Should().BeApproximately(3.0, 1e-9);
    }

    [Fact]
    public void QuantityPlacement3ShouldStoreUnitPositionAndOrientation() {
        var item = new Item("i1", "Item 1");
        var pos = new QuantityPoint3<FltX>(M(1), M(2), M(3));
        var placement = new QuantityPlacement3<Item, FltX>(item, pos, Orientation.Side);
        placement.Unit.Should().BeSameAs(item);
        placement.Position.Should().Be(pos);
        placement.Orientation.Should().Be(Orientation.Side);
    }

    [Fact]
    public void QuantityPlacement3DefaultOrientationShouldBeUpright() {
        var item = new Item("i1", "Item 1");
        var pos = new QuantityPoint3<FltX>(M(0), M(0), M(0));
        var placement = new QuantityPlacement3<Item, FltX>(item, pos);
        placement.Orientation.Should().Be(Orientation.Upright);
    }

    [Fact]
    public void QuantityPlacement3ZXYShortcutsShouldReturnPositionComponents() {
        var item = new Item("i1", "Item 1");
        var pos = new QuantityPoint3<FltX>(M(1), M(2), M(3));
        var placement = new QuantityPlacement3<Item, FltX>(item, pos);
        D(placement.Z).Should().BeApproximately(3.0, 1e-9);
        D(placement.X).Should().BeApproximately(1.0, 1e-9);
        D(placement.Y).Should().BeApproximately(2.0, 1e-9);
    }

    [Fact]
    public void BatchNoShouldStoreValue() {
        var batch = new BatchNo("B001");
        batch.Value.Should().Be("B001");
    }

    [Fact]
    public void BinLayerShouldHaveDefaultEmptyUnits() {
        var layer = new BinLayer();
        layer.Units.Should().BeEmpty();
        layer.Iteration.Should().Be(0);
    }

    [Fact]
    public void BinLayerCopyShouldCreateNewInstanceWithSameData() {
        var shape = new Container3Shape<FltX>(M(3), M(3), M(3));
        var layer = new BinLayer { Iteration = 5, Shape = shape };
        BinLayer copy = layer.Copy();
        copy.Should().NotBeSameAs(layer);
        copy.Iteration.Should().Be(5);
        copy.Shape.Should().Be(shape);
    }

    [Fact]
    public void BinLayerDepthShouldReturnShapeDepth() {
        var shape = new Container3Shape<FltX>(M(2), M(3), M(4));
        var layer = new BinLayer { Shape = shape };
        D(layer.Depth).Should().BeApproximately(4.0, 1e-9);
    }

    [Fact]
    public void BinLayerEqualityShouldBeBasedOnShape() {
        var shape = new Container3Shape<FltX>(M(3), M(3), M(3));
        var layer1 = new BinLayer { Iteration = 1, Shape = shape };
        var layer2 = new BinLayer { Iteration = 2, Shape = shape };
        // BinLayer.Equals compares by Shape
        layer1.Equals(layer2).Should().BeTrue();
        // Different iterations should still be equal
        layer1.Iteration.Should().NotBe(layer2.Iteration);
    }

    [Fact]
    public void ItemShouldStoreIdAndName() {
        var item = new Item("id-1", "Test Item");
        item.Id.Should().Be("id-1");
        item.Name.Should().Be("Test Item");
    }

    [Fact]
    public void DemandModeKeyShouldStoreModeAndKey() {
        var mode = new Bpp3dDemandMode.ItemAmount();
        var key = new Bpp3dDemandKey.ItemKey("item-1");
        var dmk = new DemandModeKey(mode, key);
        dmk.Mode.Should().Be(mode);
        dmk.Key.Should().Be(key);
    }

    [Fact]
    public void DemandModeSubtypesShouldBeDistinct() {
        var itemMode = new Bpp3dDemandMode.ItemMode();
        var materialMode = new Bpp3dDemandMode.MaterialMode();
        var itemAmount = new Bpp3dDemandMode.ItemAmount();
        var itemWeight = new Bpp3dDemandMode.ItemWeight();
        var itemMatAmount = new Bpp3dDemandMode.ItemMaterialAmount();
        var itemMatWeight = new Bpp3dDemandMode.ItemMaterialWeight();

        itemMode.Should().NotBe(materialMode);
        itemAmount.Should().NotBe(itemWeight);
        itemMatAmount.Should().NotBe(itemMatWeight);
    }

    [Fact]
    public void DemandKeySubtypesShouldStoreValues() {
        var itemKey = new Bpp3dDemandKey.ItemKey("item-1");
        itemKey.ItemId.Should().Be("item-1");

        var matKey = new Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model.MaterialKey("M001", Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model.MaterialType.RawMaterial);
        var matRef = new Bpp3dDemandKey.MaterialKeyRef(matKey);
        matRef.MaterialKeyValue.Should().Be(matKey);
    }

    [Fact]
    public async Task BlockLayerGeneratorShouldReturnEmpty() {
        var gen = new BlockLayerGenerator();
        IReadOnlyList<Bpp3dLayerGenerationResult<FltX>> result = await gen.GenerateAsync(new Bpp3dLayerGenerationRequest<FltX>(
            0, Array.Empty<object>(), Array.Empty<BinLayer>()));
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task BLLocalLayerGeneratorShouldReturnEmpty() {
        var gen = new BLLocalLayerGenerator();
        IReadOnlyList<Bpp3dLayerGenerationResult<FltX>> result = await gen.GenerateAsync(new Bpp3dLayerGenerationRequest<FltX>(
            0, Array.Empty<object>(), Array.Empty<BinLayer>()));
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task BLGlobalLayerGeneratorShouldReturnEmpty() {
        var gen = new BLGlobalLayerGenerator();
        IReadOnlyList<Bpp3dLayerGenerationResult<FltX>> result = await gen.GenerateAsync(new Bpp3dLayerGenerationRequest<FltX>(
            0, Array.Empty<object>(), Array.Empty<BinLayer>()));
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task PatternLayerGeneratorShouldReturnEmpty() {
        var gen = new PatternLayerGenerator();
        IReadOnlyList<Bpp3dLayerGenerationResult<FltX>> result = await gen.GenerateAsync(new Bpp3dLayerGenerationRequest<FltX>(
            0, Array.Empty<object>(), Array.Empty<BinLayer>()));
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task PileLayerGeneratorShouldReturnEmpty() {
        var gen = new PileLayerGenerator();
        IReadOnlyList<Bpp3dLayerGenerationResult<FltX>> result = await gen.GenerateAsync(new Bpp3dLayerGenerationRequest<FltX>(
            0, Array.Empty<object>(), Array.Empty<BinLayer>()));
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task CirclePackingLayerGeneratorShouldReturnEmpty() {
        var gen = new CirclePackingLayerGenerator();
        IReadOnlyList<Bpp3dLayerGenerationResult<FltX>> result = await gen.GenerateAsync(new Bpp3dLayerGenerationRequest<FltX>(
            0, Array.Empty<object>(), Array.Empty<BinLayer>()));
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task HistoricalLayerGeneratorShouldReturnEmpty() {
        var gen = new HistoricalLayerGenerator();
        IReadOnlyList<Bpp3dLayerGenerationResult<FltX>> result = await gen.GenerateAsync(new Bpp3dLayerGenerationRequest<FltX>(
            0, Array.Empty<object>(), Array.Empty<BinLayer>()));
        result.Should().BeEmpty();
    }

    [Fact]
    public void Point3FltXShouldDefaultToZero() {
        QuantityPoint3<FltX> pt = BinLayerHelpers.Point3FltX();
        pt.X.Value.Should().Be(FltX.Zero);
        pt.Y.Value.Should().Be(FltX.Zero);
        pt.Z.Value.Should().Be(FltX.Zero);
    }

    [Fact]
    public void Point3FltXShouldAcceptCustomValues() {
        QuantityPoint3<FltX> pt = BinLayerHelpers.Point3FltX(z: M(5));
        D(pt.Z).Should().BeApproximately(5.0, 1e-9);
        pt.X.Value.Should().Be(FltX.Zero);
    }

    [Fact]
    public void ContinuousCylinderRadiusSolverPrototypeEmptyShouldHaveDefaultValues() {
        ContinuousCylinderRadiusSolverPrototype proto = ContinuousCylinderRadiusSolverPrototype.Empty;
        proto.Source.Should().BeEmpty();
        proto.VariableName.Should().BeEmpty();
        proto.Axis.Should().Be(Axis3.Y);
    }

    [Fact]
    public void ContinuousCylinderRadiusSolverPrototypeShouldStoreAllProperties() {
        var proto = new ContinuousCylinderRadiusSolverPrototype(
            "src", "var", Axis3.Z,
            InitialRadius: M(1), RadiusMin: M(0.5), RadiusMax: M(2),
            RadiusWeightFunctionKey: "wkey", IsPWLRegisterable: true);
        proto.Source.Should().Be("src");
        proto.VariableName.Should().Be("var");
        proto.Axis.Should().Be(Axis3.Z);
        proto.IsPWLRegisterable.Should().BeTrue();
        proto.RadiusWeightFunctionKey.Should().Be("wkey");
    }

    [Fact]
    public void ContinuousRadiusHelpersSourceShouldReturnItemId() {
        var item = new Item("abc", "test");
        ContinuousRadiusHelpers.Source(item).Should().Be("item-abc");
    }

    [Fact]
    public void ContinuousRadiusHelpersSourceShouldReturnUnknownForNonItem() => ContinuousRadiusHelpers.Source(42).Should().Be("unknown");

    [Fact]
    public void ContinuousRadiusHelpersPrototypeShouldBuildCorrectly() {
        ContinuousCylinderRadiusSolverPrototype proto = ContinuousRadiusHelpers.Prototype("my-src", key: "wk", axis: Axis3.X);
        proto.Source.Should().Be("my-src");
        proto.Axis.Should().Be(Axis3.X);
        proto.IsPWLRegisterable.Should().BeTrue();
        proto.RadiusWeightFunctionKey.Should().Be("wk");
    }

    [Fact]
    public void LayerGenerationRequestShouldHaveDefaultValues() {
        var req = new Bpp3dLayerGenerationRequest<FltX>(
            Iteration: 1,
            Items: Array.Empty<object>(),
            ExistingLayers: Array.Empty<BinLayer>());
        req.Iteration.Should().Be(1);
        req.MaxCandidates.Should().Be(64);
        req.ShadowPrices.Should().BeNull();
        req.Bin.Should().BeNull();
    }

    [Fact]
    public void LayerGenerationResultShouldStoreProperties() {
        var layer = new BinLayer { Shape = new Container3Shape<FltX>(M(1), M(1), M(1)) };
        var result = new Bpp3dLayerGenerationResult<FltX>(layer, new FltX(-0.5), "test-src");
        result.Layer.Should().BeSameAs(layer);
        result.ReducedCost.ToFlt64().ToDouble().Should().BeApproximately(-0.5, 1e-9);
        result.Source.Should().Be("test-src");
    }
}
