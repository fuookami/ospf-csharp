#nullable enable

using System.Collections.Generic;
using Xunit;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Framework.Bpp2d.Domain;

namespace Fuookami.Ospf.Framework.Bpp2d.Tests
{
    public class RectangularPackingDemandTest
    {
        /// <summary>
        /// Unit-aware quantity equality (mirrors Kotlin `eq`).
        /// Converts actual to expected's unit, then compares values.
        /// </summary>
        private static void AssertQuantityEq(Quantity<FltX> expected, Quantity<FltX> actual)
        {
            var converted = actual.ConvertTo(expected.Unit);
            Assert.True(converted.IsOk,
                $"Cannot convert actual unit '{actual.Unit}' to expected unit '{expected.Unit}'.");
            Assert.Equal(expected.Value, converted.Value.Value);
        }

        [Fact]
        public void SceneShouldExposeRealPlacementOverlapNeed()
        {
            var sheet = new Sheet2<FltX>(
                Id: "sheet-1",
                Width: new Quantity<FltX>(new FltX(10.0), SIBaseUnits.Meter),
                Height: new Quantity<FltX>(new FltX(6.0), SIBaseUnits.Meter));
            var itemA = new RectangleItem2<FltX>(
                Id: "item-a",
                Width: new Quantity<FltX>(new FltX(4.0), SIBaseUnits.Meter),
                Height: new Quantity<FltX>(new FltX(2.0), SIBaseUnits.Meter),
                AllowRotate: true);
            var itemB = new RectangleItem2<FltX>(
                Id: "item-b",
                Width: new Quantity<FltX>(new FltX(3.0), SIBaseUnits.Meter),
                Height: new Quantity<FltX>(new FltX(3.0), SIBaseUnits.Meter));

            var placementA = new PlannedRectangle2<FltX>(
                Item: itemA,
                X: new Quantity<FltX>(new FltX(1.0), SIBaseUnits.Meter),
                Y: new Quantity<FltX>(new FltX(1.0), SIBaseUnits.Meter));
            var placementB = new PlannedRectangle2<FltX>(
                Item: itemB,
                X: new Quantity<FltX>(new FltX(3.0), SIBaseUnits.Meter),
                Y: new Quantity<FltX>(new FltX(2.0), SIBaseUnits.Meter));

            var scene = new PackingScene2<FltX>(
                Sheet: sheet,
                Placements: new List<PlannedRectangle2<FltX>> { placementA, placementB });

            Assert.True(scene.AllInsideSheet());
            Assert.Equal(
                new List<(string, string)> { ("item-a", "item-b") },
                scene.OverlappedPairs());

            var overlap = placementA.ToBox2Need().Intersect(placementB.ToBox2Need());
            Assert.NotNull(overlap);
            AssertQuantityEq(new Quantity<FltX>(new FltX(2.0), AreaUnits.SquareMeter), overlap!.Area);
        }

        [Fact]
        public void RotatedPlacementShouldStillRespectSheetBoundary()
        {
            var sheet = new Sheet2<FltX>(
                Id: "sheet-2",
                Width: new Quantity<FltX>(new FltX(5.0), SIBaseUnits.Meter),
                Height: new Quantity<FltX>(new FltX(4.0), SIBaseUnits.Meter));
            var item = new RectangleItem2<FltX>(
                Id: "item-c",
                Width: new Quantity<FltX>(new FltX(4.0), SIBaseUnits.Meter),
                Height: new Quantity<FltX>(new FltX(2.0), SIBaseUnits.Meter),
                AllowRotate: true);
            var validRotated = new PlannedRectangle2<FltX>(
                Item: item,
                X: new Quantity<FltX>(new FltX(1.0), SIBaseUnits.Meter),
                Y: new Quantity<FltX>(new FltX(0.0), SIBaseUnits.Meter),
                Rotated: true);
            var outOfSheet = new PlannedRectangle2<FltX>(
                Item: item,
                X: new Quantity<FltX>(new FltX(4.0), SIBaseUnits.Meter),
                Y: new Quantity<FltX>(new FltX(1.5), SIBaseUnits.Meter));

            var validScene = new PackingScene2<FltX>(
                Sheet: sheet,
                Placements: new List<PlannedRectangle2<FltX>> { validRotated });
            var invalidScene = new PackingScene2<FltX>(
                Sheet: sheet,
                Placements: new List<PlannedRectangle2<FltX>> { outOfSheet });

            Assert.True(validScene.AllInsideSheet());
            Assert.False(invalidScene.AllInsideSheet());
        }

        [Fact]
        public void SceneShouldReportIllegalOverlapAndRemainingArea()
        {
            var sheet = new Sheet2<FltX>(
                Id: "sheet-3",
                Width: new Quantity<FltX>(new FltX(10.0), SIBaseUnits.Meter),
                Height: new Quantity<FltX>(new FltX(6.0), SIBaseUnits.Meter));
            var itemA = new RectangleItem2<FltX>(
                Id: "item-a",
                Width: new Quantity<FltX>(new FltX(4.0), SIBaseUnits.Meter),
                Height: new Quantity<FltX>(new FltX(2.0), SIBaseUnits.Meter));
            var itemB = new RectangleItem2<FltX>(
                Id: "item-b",
                Width: new Quantity<FltX>(new FltX(3.0), SIBaseUnits.Meter),
                Height: new Quantity<FltX>(new FltX(2.0), SIBaseUnits.Meter));
            var itemC = new RectangleItem2<FltX>(
                Id: "item-c",
                Width: new Quantity<FltX>(new FltX(2.0), SIBaseUnits.Meter),
                Height: new Quantity<FltX>(new FltX(2.0), SIBaseUnits.Meter));

            var scene = new PackingScene2<FltX>(
                Sheet: sheet,
                Placements: new List<PlannedRectangle2<FltX>>
                {
                    new PlannedRectangle2<FltX>(
                        Item: itemA,
                        X: new Quantity<FltX>(new FltX(0.0), SIBaseUnits.Meter),
                        Y: new Quantity<FltX>(new FltX(0.0), SIBaseUnits.Meter)),
                    new PlannedRectangle2<FltX>(
                        Item: itemB,
                        X: new Quantity<FltX>(new FltX(4.0), SIBaseUnits.Meter),
                        Y: new Quantity<FltX>(new FltX(0.0), SIBaseUnits.Meter)),
                    new PlannedRectangle2<FltX>(
                        Item: itemC,
                        X: new Quantity<FltX>(new FltX(3.0), SIBaseUnits.Meter),
                        Y: new Quantity<FltX>(new FltX(1.0), SIBaseUnits.Meter))
                });

            Assert.True(scene.AllInsideSheet());
            Assert.Equal(
                new List<(string, string)> { ("item-a", "item-c"), ("item-b", "item-c") },
                scene.IllegalOverlaps());
            AssertQuantityEq(new Quantity<FltX>(new FltX(18.0), AreaUnits.SquareMeter), scene.UsedArea());
            AssertQuantityEq(new Quantity<FltX>(new FltX(42.0), AreaUnits.SquareMeter), scene.RemainingArea());
        }

        [Fact]
        public void GeometryMappingShouldMatchNeedIntersectionContract()
        {
            var sheet = new Sheet2<FltX>(
                Id: "sheet-4",
                Width: new Quantity<FltX>(new FltX(10.0), SIBaseUnits.Meter),
                Height: new Quantity<FltX>(new FltX(6.0), SIBaseUnits.Meter));
            var itemA = new RectangleItem2<FltX>(
                Id: "item-a",
                Width: new Quantity<FltX>(new FltX(4.0), SIBaseUnits.Meter),
                Height: new Quantity<FltX>(new FltX(2.0), SIBaseUnits.Meter));
            var itemB = new RectangleItem2<FltX>(
                Id: "item-b",
                Width: new Quantity<FltX>(new FltX(3.0), SIBaseUnits.Meter),
                Height: new Quantity<FltX>(new FltX(3.0), SIBaseUnits.Meter));
            var placementA = new PlannedRectangle2<FltX>(
                Item: itemA,
                X: new Quantity<FltX>(new FltX(1.0), SIBaseUnits.Meter),
                Y: new Quantity<FltX>(new FltX(1.0), SIBaseUnits.Meter));
            var placementB = new PlannedRectangle2<FltX>(
                Item: itemB,
                X: new Quantity<FltX>(new FltX(3.0), SIBaseUnits.Meter),
                Y: new Quantity<FltX>(new FltX(2.0), SIBaseUnits.Meter));
            var scene = new PackingScene2<FltX>(
                Sheet: sheet,
                Placements: new List<PlannedRectangle2<FltX>> { placementA, placementB });

            Assert.True(scene.AllInsideSheet());

            var needIntersection = placementA.ToBox2Need().Intersect(placementB.ToBox2Need());
            Assert.NotNull(needIntersection);

            var geometryIntersection = placementA
                .ToPlacement2Need()
                .ToGeometryPlacement2()
                .Intersect(placementB.ToPlacement2Need().ToGeometryPlacement2())
                .ValueOrFail()
                .OrFail();
            Assert.NotNull(geometryIntersection);
            var geometryArea = geometryIntersection.Width.Multiply(geometryIntersection.Height);

            AssertQuantityEq(needIntersection!.Area, geometryArea);
            AssertQuantityEq(new Quantity<FltX>(new FltX(2.0), AreaUnits.SquareMeter), geometryArea);
        }
    }
}
