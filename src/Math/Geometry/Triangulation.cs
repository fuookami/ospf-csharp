#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Math.Geometry;

/// <summary>
/// 二维 Delaunay 三角剖分结果 / 2D Delaunay triangulation result.
/// </summary>
public sealed record DelaunayTriangulation2(
    IReadOnlyList<Triangle<Dim2, Flt64>> Triangles,
    IReadOnlyList<Point<Dim2, Flt64>> Points)
{
    /// <summary>去重后的边列表 / Deduplicated edges.</summary>
    public IReadOnlyList<Edge<Dim2, Flt64>> Edges
    {
        get
        {
            var result = new List<Edge<Dim2, Flt64>>();
            var seen = new HashSet<(int Min, int Max)>();
            foreach (var t in Triangles)
                foreach (var e in t.Edges)
                {
                    var (i1, i2) = FindPointIndices(e.From, e.To);
                    var key = i1 < i2 ? (i1, i2) : (i2, i1);
                    if (seen.Add(key)) result.Add(e);
                }
            return result;
        }
    }

    private (int, int) FindPointIndices(Point<Dim2, Flt64> p1, Point<Dim2, Flt64> p2)
    {
        var (i1, i2) = (0, 0);
        for (var i = 0; i < Points.Count; i++)
        {
            if (Points[i].ApproxEq(p1)) i1 = i;
            if (Points[i].ApproxEq(p2)) i2 = i;
        }
        return (i1, i2);
    }
}

/// <summary>
/// Delaunay 三角剖分算法 / Delaunay triangulation (Bowyer-Watson).
/// </summary>
public static class Delaunay
{
    /// <summary>三角剖分，返回完整结果 / Triangulate, returning full result.</summary>
    public static DelaunayTriangulation2 Triangulate(IReadOnlyList<Point<Dim2, Flt64>> points)
        => new(Invoke(points), points);

    /// <summary>三角剖分，返回完整结果（带错误处理）/ Triangulate with error handling.</summary>
    public static Result<DelaunayTriangulation2, ErrorCode, Error<ErrorCode>> TriangulateRet(IReadOnlyList<Point<Dim2, Flt64>> points)
        => points.Count < 3
            ? Results.Failed<DelaunayTriangulation2>(new Err<ErrorCode>(ErrorCode.IllegalArgument, "At least 3 points are required for triangulation."))
            : Results.Ok(Triangulate(points));

    /// <summary>三角剖分，返回三角形列表 / Triangulate, returning triangle list.</summary>
    public static IReadOnlyList<Triangle<Dim2, Flt64>> Invoke(IReadOnlyList<Point<Dim2, Flt64>> points)
    {
        if (points.Count < 3) return Array.Empty<Triangle<Dim2, Flt64>>();

        var sortedPoints = points.OrderBy(p => p.X().Value).ThenBy(p => p.Y().Value).ToList();
        var superTriangle = GetSuperTriangle(sortedPoints);
        var triangles = new List<Triangle<Dim2, Flt64>> { superTriangle };
        var undeterminedTriangles = new List<Triangle<Dim2, Flt64>> { superTriangle };

        foreach (var point in sortedPoints)
        {
            var edges = new List<Edge<Dim2, Flt64>>();
            var thisTriangles = new List<Triangle<Dim2, Flt64>>();

            foreach (var triangle in undeterminedTriangles)
            {
                var circumcircle = Circle<Dim2, Flt64>.CircumcircleOf(triangle);
                var dx = circumcircle.X().Minus(point.X());
                var dy = circumcircle.Y().Minus(point.Y());
                var dist = (dx.Sqr().Plus(dy.Sqr())).Sqrt();
                if (triangle.Illegal || dist.Leq(circumcircle.Radius))
                {
                    edges.Add(triangle.E1);
                    edges.Add(triangle.E2);
                    edges.Add(triangle.E3);
                }
                else if (circumcircle.X().Plus(circumcircle.Radius).Ls(point.X()))
                {
                    triangles.Add(triangle);
                }
                else
                {
                    thisTriangles.Add(triangle);
                }
            }

            var uniqueEdges = DeleteDuplicateEdges(edges);
            UpdateTriangles(thisTriangles, point, uniqueEdges);
            undeterminedTriangles = thisTriangles;
        }

        RemoveOriginSuperTriangle(triangles, superTriangle, undeterminedTriangles);
        return triangles;
    }

    /// <summary>三角剖分，返回三角形列表（带错误处理）/ Triangulate with error handling.</summary>
    public static Result<IReadOnlyList<Triangle<Dim2, Flt64>>, ErrorCode, Error<ErrorCode>> InvokeRet(IReadOnlyList<Point<Dim2, Flt64>> points)
        => points.Count < 3
            ? Results.Failed<IReadOnlyList<Triangle<Dim2, Flt64>>>(new Err<ErrorCode>(ErrorCode.IllegalArgument, "At least 3 points are required for triangulation."))
            : Results.Ok(Invoke(points));

    private static List<Edge<Dim2, Flt64>> DeleteDuplicateEdges(List<Edge<Dim2, Flt64>> edges)
    {
        var duplication = new bool[edges.Count];
        for (var i = 0; i < edges.Count; i++)
        {
            if (duplication[i]) continue;
            for (var j = i + 1; j < edges.Count; j++)
            {
                if (edges[i].Equals(edges[j]))
                {
                    duplication[i] = true;
                    duplication[j] = true;
                }
            }
        }
        return edges.Where((_, idx) => !duplication[idx]).ToList();
    }

    private static void UpdateTriangles(
        List<Triangle<Dim2, Flt64>> triangles,
        Point<Dim2, Flt64> point,
        List<Edge<Dim2, Flt64>> edges)
    {
        triangles.AddRange(edges.Select(e => new Triangle<Dim2, Flt64>(e.From, e.To, point)));
    }

    private static void RemoveOriginSuperTriangle(
        List<Triangle<Dim2, Flt64>> triangles,
        Triangle<Dim2, Flt64> superTriangle,
        List<Triangle<Dim2, Flt64>> undeterminedTriangles)
    {
        bool IsSuperTriangle(Triangle<Dim2, Flt64> t) =>
            t.Illegal ||
            t.Vertices.Any(v =>
                v.ApproxEq(superTriangle.P1) || v.ApproxEq(superTriangle.P2) || v.ApproxEq(superTriangle.P3));

        triangles.RemoveAll(IsSuperTriangle);
        triangles.AddRange(undeterminedTriangles.Where(t => !IsSuperTriangle(t)));
    }

    private static Triangle<Dim2, Flt64> GetSuperTriangle(IReadOnlyList<Point<Dim2, Flt64>> points)
    {
        var minX = points.Min(p => p.X().Value);
        var maxX = points.Max(p => p.X().Value);
        var minY = points.Min(p => p.Y().Value);
        var maxY = points.Max(p => p.Y().Value);

        var dx = maxX - minX;
        var dy = maxY - minY;
        var midX = (maxX + minX) / 2.0;
        var midY = (maxY + minY) / 2.0;
        var dMax = global::System.Math.Max(dx, dy);

        return new Triangle<Dim2, Flt64>(
            Point<Dim2, Flt64>.Point2(new Flt64(midX - 2 * dMax), new Flt64(midY - dMax)),
            Point<Dim2, Flt64>.Point2(new Flt64(midX), new Flt64(midY + 2 * dMax)),
            Point<Dim2, Flt64>.Point2(new Flt64(midX + 2 * dMax), new Flt64(midY - dMax)));
    }
}

/// <summary>三角剖分辅助函数 / Triangulation helper functions.</summary>
public static class TriangulationFunctions
{
    /// <summary>是否满足 Delaunay 条件 / Whether triangles satisfy the Delaunay condition.</summary>
    public static bool IsDelaunay(IReadOnlyList<Triangle<Dim2, Flt64>> triangles, IReadOnlyList<Point<Dim2, Flt64>> points)
        => !triangles.Any(t => points.Any(p => !t.Vertices.Any(v => v.ApproxEq(p)) && PointInCircumcircle(p, t)));

    /// <summary>点是否在外接圆内（严格）/ Strictly inside the circumcircle.</summary>
    public static bool PointInCircumcircle(Point<Dim2, Flt64> point, Triangle<Dim2, Flt64> triangle)
        => triangle.Circumcircle().ContainsPointStrict(point);

    /// <summary>Delaunay 三角剖分（返回完整结果）/ Delaunay triangulation (full result).</summary>
    public static DelaunayTriangulation2 DelaunayTriangulate(IReadOnlyList<Point<Dim2, Flt64>> points) =>
        Delaunay.Triangulate(points);

    /// <summary>Delaunay 三角剖分（带错误处理）/ Delaunay triangulation with error handling.</summary>
    public static Result<DelaunayTriangulation2, ErrorCode, Error<ErrorCode>> DelaunayTriangulateRet(
        IReadOnlyList<Point<Dim2, Flt64>> points) => Delaunay.TriangulateRet(points);

    /// <summary>二维三角剖分 / 2D triangulation.</summary>
    public static IReadOnlyList<Triangle<Dim2, Flt64>> Triangulate2(IReadOnlyList<Point<Dim2, Flt64>> points) =>
        Delaunay.Invoke(points);

    /// <summary>二维三角剖分（带错误处理）/ 2D triangulation with error handling.</summary>
    public static Result<IReadOnlyList<Triangle<Dim2, Flt64>>, ErrorCode, Error<ErrorCode>> Triangulate2Ret(
        IReadOnlyList<Point<Dim2, Flt64>> points) => Delaunay.InvokeRet(points);

    /// <summary>三维点集三角剖分（投影到 XY）/ 3D point triangulation (projected to XY).</summary>
    public static Result<IReadOnlyList<Triangle<Dim3, Flt64>>, ErrorCode, Error<ErrorCode>> Triangulate3D(
        IReadOnlyList<Point<Dim3, Flt64>> points)
    {
        for (var i = 0; i < points.Count; i++)
            for (var j = i + 1; j < points.Count; j++)
                if (points[i].X().Eq(points[j].X()) && points[i].Y().Eq(points[j].Y()))
                    return Results.Failed<IReadOnlyList<Triangle<Dim3, Flt64>>>(new Err<ErrorCode>(
                        ErrorCode.IllegalArgument,
                        $"Point<Dim3, Flt64> list contains duplicated projected coordinates at indices {i} and {j}."));

        Point<Dim3, Flt64> Get(Point<Dim2, Flt64> p) =>
            points.First(it => it.X().Eq(p.X()) && it.Y().Eq(p.Y()));

        var tris = Triangulate2(points.Select(p => Point<Dim2, Flt64>.Point2(p.X(), p.Y())).ToList());
        return Results.Ok<IReadOnlyList<Triangle<Dim3, Flt64>>>(
            tris.Select(t => new Triangle<Dim3, Flt64>(Get(t.P1), Get(t.P2), Get(t.P3))).ToList());
    }

    /// <summary>等值线三角剖分 / Isoline triangulation.</summary>
    public static Result<IReadOnlyList<Triangle<Dim3, Flt64>>, ErrorCode, Error<ErrorCode>> TriangulateIsolines(
        IReadOnlyList<(Flt64 Z, IReadOnlyList<Point<Dim2, Flt64>> Points)> isolines)
    {
        var triangles = new List<Triangle<Dim3, Flt64>>();
        for (var i = 0; i < isolines.Count - 1; i++)
        {
            var (z, pts) = isolines[i];
            var (nz, npts) = isolines[i + 1];
            var points = pts.Select(p => Point<Dim3, Flt64>.Point3(p.X(), p.Y(), z))
                .Concat(npts.Select(p => Point<Dim3, Flt64>.Point3(p.X(), p.Y(), nz))).ToList();
            var result = Triangulate3D(points);
            if (result is Ok<IReadOnlyList<Triangle<Dim3, Flt64>>, ErrorCode, Error<ErrorCode>> ok)
                triangles.AddRange(ok.Value);
            else
                return Results.Failed<IReadOnlyList<Triangle<Dim3, Flt64>>>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument, "isoline triangulation failed"));
        }
        return Results.Ok<IReadOnlyList<Triangle<Dim3, Flt64>>>(triangles);
    }
}
