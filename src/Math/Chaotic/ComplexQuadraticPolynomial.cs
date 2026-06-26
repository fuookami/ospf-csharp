#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Geometry;
using System;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// 复二次多项式 / Complex Quadratic Polynomial.
/// 二维离散混沌映射，z_{n+1} = z_n^d + c（复数运算）。/ 2D discrete chaotic map, z_{n+1} = z_n^d + c (complex arithmetic).
/// </summary>
public sealed record ComplexQuadraticPolynomial(Point<Dim2, Flt64> C, Flt64 D)
    : IExtractor<Point<Dim2, Flt64>, Point<Dim2, Flt64>> {
    public Point<Dim2, Flt64> Invoke(Point<Dim2, Flt64> x) {
        double re = x[0].Value;
        double im = x[1].Value;
        double d = D.Value;
        double magnitude = global::System.Math.Sqrt(re * re + im * im);
        double theta = global::System.Math.Atan2(im, re);
        double powMag = global::System.Math.Pow(magnitude, d);
        double newRe = powMag * global::System.Math.Cos(d * theta) + C[0].Value;
        double newIm = powMag * global::System.Math.Sin(d * theta) + C[1].Value;
        return PointFactory.point2(new Flt64(newRe), new Flt64(newIm));
    }

    public static ComplexQuadraticPolynomial Create(Point<Dim2, Flt64>? c = null, Flt64? d = null)
        => new(c ?? PointFactory.point2(new Flt64(0.5), new Flt64(0.5)), d ?? new Flt64(2.0));
}

/// <summary>
/// 复二次多项式生成器 / Complex Quadratic Polynomial Generator.
/// </summary>
public sealed class ComplexQuadraticPolynomialGenerator : IGenerator<Point<Dim2, Flt64>> {
    public ComplexQuadraticPolynomial Map { get; }
    public Point<Dim2, Flt64> X { get; private set; }

    public ComplexQuadraticPolynomialGenerator(ComplexQuadraticPolynomial? map = null, Point<Dim2, Flt64>? x = null) {
        Map = map ?? ComplexQuadraticPolynomial.Create();
        X = x ?? PointFactory.point2(new Flt64(0.1), new Flt64(0.1));
    }

    public static ComplexQuadraticPolynomialGenerator Create(
        Point<Dim2, Flt64> c, Flt64 d, Point<Dim2, Flt64>? x = null)
        => new(ComplexQuadraticPolynomial.Create(c, d), x);

    public Point<Dim2, Flt64> Invoke() {
        Point<Dim2, Flt64> cur = X;
        X = Map.Invoke(X);
        return cur;
    }
}
