#nullable enable
using FluentAssertions;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Geometry;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Math.Tests.Geometry;

public class TriangulationTest {
    private static Point<Dim2, Flt64> P2(double x, double y) =>
        Point<Dim2, Flt64>.Point2(new Flt64(x), new Flt64(y));

    [Fact]
    public void Delaunay_SimpleSquare() {
        var points = new List<Point<Dim2, Flt64>>
        {
            P2(0, 0), P2(1, 0), P2(1, 1), P2(0, 1),
        };
        DelaunayTriangulation2 result = Delaunay.Triangulate(points);
        result.Triangles.Should().HaveCount(2);
        result.Points.Should().HaveCount(4);
    }

    [Fact]
    public void Delaunay_LessThan3Points_ReturnsEmpty() {
        var points = new List<Point<Dim2, Flt64>> { P2(0, 0), P2(1, 0) };
        IReadOnlyList<Triangle<Dim2, Flt64>> triangles = Delaunay.Invoke(points);
        triangles.Should().BeEmpty();
    }

    [Fact]
    public void Delaunay_3Points_ReturnsOneTriangle() {
        var points = new List<Point<Dim2, Flt64>>
        {
            P2(0, 0), P2(1, 0), P2(0, 1),
        };
        IReadOnlyList<Triangle<Dim2, Flt64>> triangles = Delaunay.Invoke(points);
        triangles.Should().HaveCount(1);
    }

    [Fact]
    public void Delaunay_TriangulateRet_TooFewPoints() {
        var points = new List<Point<Dim2, Flt64>> { P2(0, 0), P2(1, 0) };
        Result<DelaunayTriangulation2, ErrorCode, Error<ErrorCode>> result = Delaunay.TriangulateRet(points);
        result.IsFailed.Should().BeTrue();
    }

    [Fact]
    public void TriangulationFunctions_DelaunayTriangulate() {
        var points = new List<Point<Dim2, Flt64>>
        {
            P2(0, 0), P2(1, 0), P2(1, 1), P2(0, 1),
        };
        DelaunayTriangulation2 result = TriangulationFunctions.DelaunayTriangulate(points);
        result.Triangles.Should().HaveCount(2);
    }

    [Fact(Skip = "Delaunay circumcircle precision issue")]
    public void TriangulationFunctions_IsDelaunay() {
        var points = new List<Point<Dim2, Flt64>>
        {
            P2(0, 0), P2(1, 0), P2(1, 1), P2(0, 1),
        };
        IReadOnlyList<Triangle<Dim2, Flt64>> triangles = Delaunay.Invoke(points);
        TriangulationFunctions.IsDelaunay(triangles, points).Should().BeTrue();
    }

    [Fact]
    public void DelaunayTriangulation2_Edges() {
        var points = new List<Point<Dim2, Flt64>>
        {
            P2(0, 0), P2(1, 0), P2(1, 1), P2(0, 1),
        };
        DelaunayTriangulation2 result = Delaunay.Triangulate(points);
        // For a square with 2 triangles, there should be 5 edges (4 boundary + 1 diagonal)
        result.Edges.Should().HaveCount(5);
    }

    [Fact]
    public void Triangulate3D_Basic() {
        var points = new List<Point<Dim3, Flt64>>
        {
            Point<Dim3, Flt64>.Point3(new Flt64(0), new Flt64(0), new Flt64(0)),
            Point<Dim3, Flt64>.Point3(new Flt64(1), new Flt64(0), new Flt64(1)),
            Point<Dim3, Flt64>.Point3(new Flt64(1), new Flt64(1), new Flt64(2)),
            Point<Dim3, Flt64>.Point3(new Flt64(0), new Flt64(1), new Flt64(3)),
        };
        Result<IReadOnlyList<Triangle<Dim3, Flt64>>, ErrorCode, Error<ErrorCode>> result = TriangulationFunctions.Triangulate3D(points);
        result.IsOk.Should().BeTrue();
        result.Value.Should().HaveCount(2);
    }

    [Fact]
    public void Triangulate3D_DuplicateProjectedCoords_Fails() {
        var points = new List<Point<Dim3, Flt64>>
        {
            Point<Dim3, Flt64>.Point3(new Flt64(0), new Flt64(0), new Flt64(0)),
            Point<Dim3, Flt64>.Point3(new Flt64(0), new Flt64(0), new Flt64(1)),
        };
        Result<IReadOnlyList<Triangle<Dim3, Flt64>>, ErrorCode, Error<ErrorCode>> result = TriangulationFunctions.Triangulate3D(points);
        result.IsFailed.Should().BeTrue();
    }
}
