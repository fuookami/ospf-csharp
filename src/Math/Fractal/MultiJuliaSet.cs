#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Geometry;
using System;

namespace Fuookami.Ospf.Math.Fractal;
/// <summary>
/// 多重 Julia 集 / Multi Julia Set (z^n + c, Flt64-only, polar form).
/// </summary>
public sealed record MultiJuliaSet(Point<Dim2, Flt64> C, Flt64 N) {
    public Point<Dim2, Flt64> Invoke(Point<Dim2, Flt64> z) {
        double x = z[0].Value;
        double y = z[1].Value;
        double n = N.Value;
        double c0 = C[0].Value;
        double c1 = C[1].Value;

        double r2 = x * x + y * y;
        double halfN = n / 2.0;
        double rN = global::System.Math.Pow(r2, halfN);

        double theta = global::System.Math.Abs(x) < 1e-15
            ? global::System.Math.PI / 2.0
            : global::System.Math.Atan(y / x);
        double nTheta = n * theta;

        return PointFactory.point2(
            new Flt64(rN * global::System.Math.Cos(nTheta) + c0),
            new Flt64(rN * global::System.Math.Sin(nTheta) + c1));
    }

    public static MultiJuliaSet Create(Flt64 real, Flt64 imag, Flt64? n = null)
        => new(PointFactory.point2(real, imag), n ?? new Flt64(2.0));
}

/// <summary>
/// 多重 Julia 集序列生成器 / Multi Julia Set sequence generator.
/// </summary>
public sealed class MultiJuliaSetGenerator : IGenerator<Point<Dim2, Flt64>> {
    public MultiJuliaSet Set { get; }
    public Point<Dim2, Flt64> Z { get; private set; }

    public MultiJuliaSetGenerator(MultiJuliaSet? set = null, Point<Dim2, Flt64>? z = null) {
        Set = set ?? MultiJuliaSet.Create(new Flt64(-0.7), new Flt64(0.27015));
        Z = z ?? PointFactory.point2(new Flt64(0.0), new Flt64(0.0));
    }

    public static MultiJuliaSetGenerator Create(Flt64 real, Flt64 imag, Flt64? n = null, Point<Dim2, Flt64>? z = null)
        => new(MultiJuliaSet.Create(real, imag, n), z);

    public Point<Dim2, Flt64> Invoke() {
        Point<Dim2, Flt64> cur = Z;
        Z = Set.Invoke(Z);
        return cur;
    }
}
