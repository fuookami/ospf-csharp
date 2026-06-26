#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Geometry;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// 复数平方映射 / Complex Squaring Map. Flt64-only (singleton, no params).
/// z_{n+1} = z_n^2（复数平方）。/ z_{n+1} = z_n^2 (complex squaring).
/// </summary>
public sealed record ComplexSquaringMap : IExtractor<Point<Dim2, Flt64>, Point<Dim2, Flt64>> {
    public static readonly ComplexSquaringMap Instance = new();

    public Point<Dim2, Flt64> Invoke(Point<Dim2, Flt64> x) {
        double re = x[0].Value;
        double im = x[1].Value;
        var newRe = new Flt64(re * re - im * im);
        var newIm = new Flt64(2.0 * re * im);
        return PointFactory.point2(newRe, newIm);
    }
}

/// <summary>
/// 复数平方映射生成器 / Complex Squaring Map Generator.
/// </summary>
public sealed class ComplexSquaringMapGenerator : IGenerator<Point<Dim2, Flt64>> {
    public Point<Dim2, Flt64> X { get; private set; }

    public ComplexSquaringMapGenerator(Point<Dim2, Flt64>? x = null) {
        X = x ?? PointFactory.point2(new Flt64(0.5), new Flt64(0.5));
    }

    public Point<Dim2, Flt64> Invoke() {
        Point<Dim2, Flt64> cur = X;
        X = ComplexSquaringMap.Instance.Invoke(X);
        return cur;
    }
}
