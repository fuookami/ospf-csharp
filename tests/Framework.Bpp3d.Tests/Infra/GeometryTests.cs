#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp3d.Infra;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Geometry;
using Fuookami.Ospf.Quantities.Geometry;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Infra;

public class GeometryTests {
    private static Quantity<FltX> M(double v) => new(new FltX(v), SIBaseUnits.Meter);
    private static double D(Quantity<FltX> q) => q.Value.ToFlt64().ToDouble();

    [Fact]
    public void CuboidShapeTypeShouldBeCuboid() {
        var cuboid = new QuantityCuboid3<FltX>(M(2), M(3), M(4));
        var shape = new CuboidPackingShape3<FltX>(cuboid);
        shape.ShapeType.Should().Be(PackingShapeType.Cuboid);
        shape.AlgorithmShapeType.Should().Be(PackingAlgorithmShapeType.Cuboid);
    }

    [Fact]
    public void CuboidBoundingDimensionsShouldMatchCuboid() {
        var cuboid = new QuantityCuboid3<FltX>(M(2), M(3), M(4));
        var shape = new CuboidPackingShape3<FltX>(cuboid);
        D(shape.BoundingWidth).Should().BeApproximately(2.0, 1e-9);
        D(shape.BoundingHeight).Should().BeApproximately(3.0, 1e-9);
        D(shape.BoundingDepth).Should().BeApproximately(4.0, 1e-9);
    }

    [Fact]
    public void CuboidActualVolumeShouldBeWidthTimesHeightTimesDepth() {
        var cuboid = new QuantityCuboid3<FltX>(M(2), M(3), M(4));
        var shape = new CuboidPackingShape3<FltX>(cuboid);
        D(shape.ActualVolume).Should().BeApproximately(24.0, 1e-9);
    }

    [Fact]
    public void CuboidAxisShouldBeNull() {
        var cuboid = new QuantityCuboid3<FltX>(M(2), M(3), M(4));
        var shape = new CuboidPackingShape3<FltX>(cuboid);
        shape.Axis.Should().BeNull();
    }

    [Fact]
    public void CuboidFootprintShouldBeRectangle() {
        var cuboid = new QuantityCuboid3<FltX>(M(2), M(3), M(4));
        var shape = new CuboidPackingShape3<FltX>(cuboid);
        ShapeFootprint2<FltX> fp = shape.Footprint();
        fp.Should().BeOfType<ShapeFootprint2<FltX>.Rectangle>();
        var rect = (ShapeFootprint2<FltX>.Rectangle)fp;
        D(rect.Width).Should().BeApproximately(2.0, 1e-9);
        D(rect.Depth).Should().BeApproximately(4.0, 1e-9);
    }

    [Fact]
    public void CylinderVerticalAxisYShouldHaveCorrectDimensions() {
        var cyl = new QuantityCylinder3<FltX>(M(1), M(5), Axis3.Y);
        var shape = new CylinderPackingShape3(cyl);
        shape.ShapeType.Should().Be(PackingShapeType.Cylinder);
        shape.AlgorithmShapeType.Should().Be(PackingAlgorithmShapeType.VerticalCylinder);
        shape.Axis.Should().Be(Axis3.Y);
        D(shape.BoundingWidth).Should().BeApproximately(2.0, 1e-9);
        D(shape.BoundingHeight).Should().BeApproximately(5.0, 1e-9);
        D(shape.BoundingDepth).Should().BeApproximately(2.0, 1e-9);
    }

    [Fact]
    public void CylinderHorizontalAxisXShouldHaveCorrectDimensions() {
        var cyl = new QuantityCylinder3<FltX>(M(1), M(5), Axis3.X);
        var shape = new CylinderPackingShape3(cyl);
        shape.AlgorithmShapeType.Should().Be(PackingAlgorithmShapeType.HorizontalCylinderX);
        shape.Axis.Should().Be(Axis3.X);
        D(shape.BoundingWidth).Should().BeApproximately(5.0, 1e-9);
        D(shape.BoundingHeight).Should().BeApproximately(2.0, 1e-9);
        D(shape.BoundingDepth).Should().BeApproximately(2.0, 1e-9);
    }

    [Fact]
    public void CylinderHorizontalAxisZShouldHaveCorrectDimensions() {
        var cyl = new QuantityCylinder3<FltX>(M(1), M(5), Axis3.Z);
        var shape = new CylinderPackingShape3(cyl);
        shape.AlgorithmShapeType.Should().Be(PackingAlgorithmShapeType.HorizontalCylinderZ);
        shape.Axis.Should().Be(Axis3.Z);
        D(shape.BoundingWidth).Should().BeApproximately(2.0, 1e-9);
        D(shape.BoundingHeight).Should().BeApproximately(2.0, 1e-9);
        D(shape.BoundingDepth).Should().BeApproximately(5.0, 1e-9);
    }

    [Fact]
    public void CylinderActualVolumeShouldBePiTimesR2TimesH() {
        var cyl = new QuantityCylinder3<FltX>(M(1), M(5), Axis3.Y);
        var shape = new CylinderPackingShape3(cyl);
        double expected = System.Math.PI * 1.0 * 1.0 * 5.0;
        D(shape.ActualVolume).Should().BeApproximately(expected, 1e-6);
    }

    [Fact]
    public void CylinderVerticalFootprintShouldBeCircle() {
        var cyl = new QuantityCylinder3<FltX>(M(1), M(5), Axis3.Y);
        var shape = new CylinderPackingShape3(cyl);
        ShapeFootprint2<FltX> fp = shape.Footprint();
        fp.Should().BeOfType<ShapeFootprint2<FltX>.Circle>();
        var circle = (ShapeFootprint2<FltX>.Circle)fp;
        D(circle.Radius).Should().BeApproximately(1.0, 1e-9);
    }

    [Fact]
    public void CylinderHorizontalFootprintShouldBeRectangle() {
        var cyl = new QuantityCylinder3<FltX>(M(1), M(5), Axis3.Z);
        var shape = new CylinderPackingShape3(cyl);
        ShapeFootprint2<FltX> fp = shape.Footprint();
        fp.Should().BeOfType<ShapeFootprint2<FltX>.Rectangle>();
    }

    [Fact]
    public void PackingShapeTypeShouldHaveExpectedValues() {
        PackingShapeType[] values = System.Enum.GetValues<PackingShapeType>();
        values.Should().Contain(PackingShapeType.Cuboid);
        values.Should().Contain(PackingShapeType.Cylinder);
    }

    [Fact]
    public void PackingAlgorithmShapeTypeShouldHaveExpectedValues() {
        PackingAlgorithmShapeType[] values = System.Enum.GetValues<PackingAlgorithmShapeType>();
        values.Should().Contain(PackingAlgorithmShapeType.Cuboid);
        values.Should().Contain(PackingAlgorithmShapeType.VerticalCylinder);
        values.Should().Contain(PackingAlgorithmShapeType.HorizontalCylinderX);
        values.Should().Contain(PackingAlgorithmShapeType.HorizontalCylinderZ);
    }

    [Fact]
    public void OrientationShouldHaveExpectedValues() {
        Orientation[] values = System.Enum.GetValues<Orientation>();
        values.Should().Contain(Orientation.Upright);
        values.Should().Contain(Orientation.Side);
        values.Should().Contain(Orientation.Lie);
    }

    [Fact]
    public void OrientationCategoryShouldHaveExpectedValues() {
        OrientationCategory[] values = System.Enum.GetValues<OrientationCategory>();
        values.Should().Contain(OrientationCategory.Upright);
        values.Should().Contain(OrientationCategory.Side);
        values.Should().Contain(OrientationCategory.Lie);
    }

    [Fact]
    public void PackingAxis3ShouldHaveExpectedValues() {
        PackingAxis3[] values = System.Enum.GetValues<PackingAxis3>();
        values.Should().Contain(PackingAxis3.X);
        values.Should().Contain(PackingAxis3.Y);
        values.Should().Contain(PackingAxis3.Z);
    }

    [Fact]
    public void ShapeBoundingBox3ShouldStoreDimensions() {
        var box = new ShapeBoundingBox3<FltX>(M(1), M(2), M(3));
        D(box.Width).Should().BeApproximately(1.0, 1e-9);
        D(box.Height).Should().BeApproximately(2.0, 1e-9);
        D(box.Depth).Should().BeApproximately(3.0, 1e-9);
    }
}
