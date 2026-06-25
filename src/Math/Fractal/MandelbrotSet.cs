#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Geometry;

namespace Fuookami.Ospf.Math.Fractal;
/// <summary>
/// Mandelbrot 集迭代 / Mandelbrot set iteration z -> z^2 + c.
/// </summary>
public sealed record MandelbrotSet<V>(Point<Dim2, V> C)
    : IExtractor<Point<Dim2, V>, Point<Dim2, V>>
    where V : struct, IFloatingNumber<V> {
    public Point<Dim2, V> Invoke(Point<Dim2, V> z) {
        V two = ((IHasTwo<V>)z[0].Constants).Two;
        V re = z[0].Pow(2).Minus(z[1].Pow(2)).Plus(C[0]);
        V im = two.Times(z[0]).Times(z[1]).Plus(C[1]);
        return new Point<Dim2, V>(new V[] { re, im }, Dim2.Instance);
    }

    public static MandelbrotSet<Flt64> Create(Flt64 real, Flt64 imag)
        => new(PointFactory.point2(real, imag));
}

/// <summary>
/// Mandelbrot 集序列生成器 / Mandelbrot Set sequence generator.
/// </summary>
public sealed class MandelbrotSetGenerator : IGenerator<Point<Dim2, Flt64>> {
    public MandelbrotSet<Flt64> Set { get; }
    public Point<Dim2, Flt64> Z { get; private set; }

    public MandelbrotSetGenerator(MandelbrotSet<Flt64>? set = null, Point<Dim2, Flt64>? z = null) {
        Set = set ?? MandelbrotSet<Flt64>.Create(new Flt64(1.0), new Flt64(1.0));
        Z = z ?? PointFactory.point2(new Flt64(0.0), new Flt64(0.0));
    }

    public static MandelbrotSetGenerator Create(Flt64 real, Flt64 imag, Point<Dim2, Flt64>? z = null)
        => new(MandelbrotSet<Flt64>.Create(real, imag), z);

    public Point<Dim2, Flt64> Invoke() {
        Point<Dim2, Flt64> cur = Z;
        Z = Set.Invoke(Z);
        return cur;
    }
}
