#nullable enable

using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Geometry;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp2d.Tests;

public class RectangularPackingContractTest {
    /// <summary>
    /// Unit-aware quantity equality (mirrors Kotlin `eq`).
    /// Converts actual to expected's unit, then compares values.
    /// </summary>
    private static void AssertQuantityEq(Quantity<FltX> expected, Quantity<FltX> actual) {
        Result<Quantity<FltX>, ErrorCode, Error<ErrorCode>> converted = actual.ConvertTo(expected.Unit);
        Assert.True(converted.IsOk,
            $"Cannot convert actual unit '{actual.Unit}' to expected unit '{expected.Unit}'.");
        Assert.Equal(expected.Value, converted.Value.Value);
    }

    [Fact]
    public void Projection2NeedShouldKeepFutureProjection2AreaContract() {
        var projection = new Projection2Need<FltX>(
            Width: new Quantity<FltX>(new FltX(2.0), SIBaseUnits.Meter),
            Height: new Quantity<FltX>(new FltX(150.0), LengthUnits.Centimeter));

        Result<Quantity<FltX>, ErrorCode, Error<ErrorCode>> areaInSquareMeter = projection.Area.ConvertTo(AreaUnits.SquareMeter);
        Assert.True(areaInSquareMeter.IsOk);
        AssertQuantityEq(
            new Quantity<FltX>(new FltX(3.0), AreaUnits.SquareMeter),
            areaInSquareMeter.Value);
    }

    [Fact]
    public void Placement2NeedShouldKeepFuturePlacement2BoundaryContract() {
        var placement = new Placement2Need<FltX>(
            X: new Quantity<FltX>(new FltX(1.0), SIBaseUnits.Meter),
            Y: new Quantity<FltX>(new FltX(50.0), LengthUnits.Centimeter),
            Projection: new Projection2Need<FltX>(
                Width: new Quantity<FltX>(new FltX(120.0), LengthUnits.Centimeter),
                Height: new Quantity<FltX>(new FltX(2.0), SIBaseUnits.Meter)));

        AssertQuantityEq(new Quantity<FltX>(new FltX(2.2), SIBaseUnits.Meter), placement.MaxX);
        AssertQuantityEq(new Quantity<FltX>(new FltX(2.5), SIBaseUnits.Meter), placement.MaxY);
        var box = placement.ToBox2Need();
        AssertQuantityEq(new Quantity<FltX>(new FltX(120.0), LengthUnits.Centimeter), box.Width);
        AssertQuantityEq(new Quantity<FltX>(new FltX(2.0), SIBaseUnits.Meter), box.Height);
    }

    [Fact]
    public void Box2NeedShouldKeepFutureBox2OverlapIntersectContract() {
        var lhs = new Box2Need<FltX>(
            MinX: new Quantity<FltX>(new FltX(0.0), SIBaseUnits.Meter),
            MinY: new Quantity<FltX>(new FltX(0.0), SIBaseUnits.Meter),
            MaxX: new Quantity<FltX>(new FltX(250.0), LengthUnits.Centimeter),
            MaxY: new Quantity<FltX>(new FltX(2.0), SIBaseUnits.Meter));
        var rhs = new Box2Need<FltX>(
            MinX: new Quantity<FltX>(new FltX(2.0), SIBaseUnits.Meter),
            MinY: new Quantity<FltX>(new FltX(50.0), LengthUnits.Centimeter),
            MaxX: new Quantity<FltX>(new FltX(4.0), SIBaseUnits.Meter),
            MaxY: new Quantity<FltX>(new FltX(2.0), SIBaseUnits.Meter));
        var apart = new Box2Need<FltX>(
            MinX: new Quantity<FltX>(new FltX(4.1), SIBaseUnits.Meter),
            MinY: new Quantity<FltX>(new FltX(0.0), SIBaseUnits.Meter),
            MaxX: new Quantity<FltX>(new FltX(5.0), SIBaseUnits.Meter),
            MaxY: new Quantity<FltX>(new FltX(1.0), SIBaseUnits.Meter));

        Assert.True(lhs.Overlaps(rhs));
        Box2Need<FltX>? intersect = lhs.Intersect(rhs);
        Assert.NotNull(intersect);
        AssertQuantityEq(new Quantity<FltX>(new FltX(0.5), SIBaseUnits.Meter), intersect!.Width);
        AssertQuantityEq(new Quantity<FltX>(new FltX(1.5), SIBaseUnits.Meter), intersect!.Height);
        Assert.Null(lhs.Intersect(apart));
    }

    [Fact]
    public void NeedModelsShouldMapToQuantityGeometryApi() {
        var projectionNeed = new Projection2Need<FltX>(
            Width: new Quantity<FltX>(new FltX(2.0), SIBaseUnits.Meter),
            Height: new Quantity<FltX>(new FltX(3.0), SIBaseUnits.Meter));
        var placementNeed = new Placement2Need<FltX>(
            X: new Quantity<FltX>(new FltX(1.0), SIBaseUnits.Meter),
            Y: new Quantity<FltX>(new FltX(2.0), SIBaseUnits.Meter),
            Projection: projectionNeed);
        var boxNeed = placementNeed.ToBox2Need();

        QuantityRectangle2<FltX> projection = projectionNeed.ToGeometryProjection2();
        QuantityPlacement2<FltX> placement = placementNeed.ToGeometryPlacement2();
        QuantityBox2<FltX> box = boxNeed.ToGeometryBox2();

        AssertQuantityEq(new Quantity<FltX>(new FltX(6.0), AreaUnits.SquareMeter), projection.Area);
        AssertQuantityEq(new Quantity<FltX>(new FltX(3.0), SIBaseUnits.Meter), placement.MaxX().ValueOrFail());
        AssertQuantityEq(new Quantity<FltX>(new FltX(5.0), SIBaseUnits.Meter), placement.MaxY().ValueOrFail());
        AssertQuantityEq(new Quantity<FltX>(new FltX(3.0), SIBaseUnits.Meter), box.MaxX().ValueOrFail());
        AssertQuantityEq(new Quantity<FltX>(new FltX(5.0), SIBaseUnits.Meter), box.MaxY().ValueOrFail());
    }

    [Fact]
    public void MappedBox2ShouldKeepOverlapAndIntersectContract() {
        var lhsNeed = new Box2Need<FltX>(
            MinX: new Quantity<FltX>(new FltX(0.0), SIBaseUnits.Meter),
            MinY: new Quantity<FltX>(new FltX(0.0), SIBaseUnits.Meter),
            MaxX: new Quantity<FltX>(new FltX(250.0), LengthUnits.Centimeter),
            MaxY: new Quantity<FltX>(new FltX(2.0), SIBaseUnits.Meter));
        var rhsNeed = new Box2Need<FltX>(
            MinX: new Quantity<FltX>(new FltX(2.0), SIBaseUnits.Meter),
            MinY: new Quantity<FltX>(new FltX(50.0), LengthUnits.Centimeter),
            MaxX: new Quantity<FltX>(new FltX(4.0), SIBaseUnits.Meter),
            MaxY: new Quantity<FltX>(new FltX(2.0), SIBaseUnits.Meter));
        var apartNeed = new Box2Need<FltX>(
            MinX: new Quantity<FltX>(new FltX(4.1), SIBaseUnits.Meter),
            MinY: new Quantity<FltX>(new FltX(0.0), SIBaseUnits.Meter),
            MaxX: new Quantity<FltX>(new FltX(5.0), SIBaseUnits.Meter),
            MaxY: new Quantity<FltX>(new FltX(1.0), SIBaseUnits.Meter));

        QuantityBox2<FltX> lhs = lhsNeed.ToGeometryBox2();
        QuantityBox2<FltX> rhs = rhsNeed.ToGeometryBox2();
        QuantityBox2<FltX> apart = apartNeed.ToGeometryBox2();

        Assert.True(lhs.Overlapped(rhs).ValueOrFail());
        Assert.False(lhs.Overlapped(apart).ValueOrFail());

        Box2Need<FltX>? needIntersection = lhsNeed.Intersect(rhsNeed);
        Assert.NotNull(needIntersection);

        QuantityBox2<FltX> geometryIntersection = lhs.Intersect(rhs).ValueOrFail().OrFail();
        Assert.NotNull(geometryIntersection);

        AssertQuantityEq(needIntersection!.Width, geometryIntersection.Width);
        AssertQuantityEq(needIntersection!.Height, geometryIntersection.Height);
        Quantity<FltX> geometryArea = geometryIntersection.Width.Multiply(geometryIntersection.Height);
        AssertQuantityEq(new Quantity<FltX>(new FltX(0.75), AreaUnits.SquareMeter), geometryArea);
    }
}
