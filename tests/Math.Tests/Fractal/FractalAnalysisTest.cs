#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Fractal;
using Fuookami.Ospf.Math.Geometry;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System.Collections.Generic;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;
using Xunit;

namespace Fuookami.Ospf.Math.Tests.Fractal;

public class FractalAnalysisTest {
    [Fact]
    public void EscapeTimeRemainsBoundedInsideTheSet() {
        Result<UInt64, ErrorCode, Error<ErrorCode>> result = FractalAnalysis.EscapeTime(
            z: PointFactory.point2(Flt64.Zero, Flt64.Zero),
            c: PointFactory.point2(new Flt64(-0.5), Flt64.Zero),
            maxIterations: new UInt64(50));

        Assert.True(result.IsOk);
        Assert.Equal(50UL, result.Value.ToULong());
    }

    [Fact]
    public void EscapeTimeIsZeroWhenAlreadyOutsideTheBailoutRadius() {
        Result<UInt64, ErrorCode, Error<ErrorCode>> result = FractalAnalysis.EscapeTime(
            z: PointFactory.point2(new Flt64(3.0), Flt64.Zero),
            c: PointFactory.point2<Flt64>(),
            maxIterations: new UInt64(50));

        Assert.True(result.IsOk);
        Assert.Equal(0UL, result.Value.ToULong());
    }

    [Fact]
    public void EscapeTimeRejectsInvalidParameters() {
        Assert.True(FractalAnalysis.EscapeTime(
            z: PointFactory.point2<Flt64>(),
            c: PointFactory.point2<Flt64>(),
            maxIterations: UInt64.Zero).IsFailed);
        Assert.True(FractalAnalysis.EscapeTime(
            z: PointFactory.point2<Flt64>(),
            c: PointFactory.point2<Flt64>(),
            bailoutRadius: Flt64.Zero).IsFailed);
    }

    [Fact]
    public void BoxCountingDimensionApproximatesALine() {
        var points = new List<Point<Dim2, Flt64>>();
        for (int index = 0; index <= 100; index++) {
            points.Add(PointFactory.point2(new Flt64(index / 100.0), Flt64.Zero));
        }

        Result<Flt64, ErrorCode, Error<ErrorCode>> dimension = FractalAnalysis.BoxCountingDimension(points);
        Assert.True(dimension.IsOk);
        Assert.InRange(dimension.Value.ToDouble(), 0.95, 1.05);
    }

    [Fact]
    public void BoxCountingDimensionApproximatesAFilledSquare() {
        var points = new List<Point<Dim2, Flt64>>();
        for (int x = 0; x <= 32; x++) {
            for (int y = 0; y <= 32; y++) {
                points.Add(PointFactory.point2(
                    new Flt64(x / 32.0),
                    new Flt64(y / 32.0)));
            }
        }

        Result<Flt64, ErrorCode, Error<ErrorCode>> dimension = FractalAnalysis.BoxCountingDimension(points);
        Assert.True(dimension.IsOk);
        Assert.InRange(dimension.Value.ToDouble(), 1.95, 2.05);
    }

    [Fact]
    public void BoxCountingDimensionRejectsDegenerateInput() {
        Assert.True(FractalAnalysis.BoxCountingDimension(
            new[] { PointFactory.point2<Flt64>() }).IsFailed);
        Assert.True(FractalAnalysis.BoxCountingDimension(
            points: new[] { PointFactory.point2<Flt64>(), PointFactory.point2<Flt64>() },
            scales: new[] { 2 }).IsFailed);
    }

    [Fact]
    public void CorrelationDimensionApproximatesAOneDimensionalSet() {
        var solutions = new List<IReadOnlyList<Flt64>>();
        for (int index = 0; index <= 100; index++) {
            solutions.Add(new[] { new Flt64(index / 100.0) });
        }

        Result<Flt64, ErrorCode, Error<ErrorCode>> dimension = FractalAnalysis.CorrelationDimension(solutions);
        Assert.True(dimension.IsOk);
        Assert.InRange(dimension.Value.ToDouble(), 0.75, 1.25);
    }

    [Fact]
    public void CorrelationDimensionApproximatesATwoDimensionalSet() {
        var solutions = new List<IReadOnlyList<Flt64>>();
        for (int x = 0; x <= 39; x++) {
            for (int y = 0; y <= 39; y++) {
                solutions.Add(new[] { new Flt64(x / 39.0), new Flt64(y / 39.0) });
            }
        }

        Result<Flt64, ErrorCode, Error<ErrorCode>> dimension = FractalAnalysis.CorrelationDimension(solutions);
        Assert.True(dimension.IsOk);
        Assert.InRange(dimension.Value.ToDouble(), 1.75, 2.25);
    }

    [Fact]
    public void CorrelationDimensionRejectsDegenerateInput() {
        Assert.True(FractalAnalysis.CorrelationDimension(
            System.Array.Empty<IReadOnlyList<Flt64>>()).IsFailed);
        Assert.True(FractalAnalysis.CorrelationDimension(
            new[] { new[] { Flt64.One } }).IsFailed);
        Assert.True(FractalAnalysis.CorrelationDimension(new IReadOnlyList<Flt64>[] {
            new[] { Flt64.One, new Flt64(2.0) },
            new[] { Flt64.One },
        }).IsFailed);
        Assert.True(FractalAnalysis.CorrelationDimension(new IReadOnlyList<Flt64>[] {
            new[] { Flt64.One },
            new[] { Flt64.One },
        }).IsFailed);
    }
}
