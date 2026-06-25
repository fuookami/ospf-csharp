#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Geometry;

namespace Fuookami.Ospf.Math.Fractal;
/// <summary>
/// Julia 集迭代 / Julia set iteration z -> z^2 + c.
/// </summary>
public sealed record JuliaSet<V>(Point<Dim2, V> C)
    : IExtractor<Point<Dim2, V>, Point<Dim2, V>>
    where V : struct, IFloatingNumber<V> {
    public Point<Dim2, V> Invoke(Point<Dim2, V> z) {
        V two = ((IHasTwo<V>)z[0].Constants).Two;
        V re = z[0].Pow(2).Minus(z[1].Pow(2)).Plus(C[0]);
        V im = two.Times(z[0]).Times(z[1]).Plus(C[1]);
        return new Point<Dim2, V>(new V[] { re, im }, Dim2.Instance);
    }

    public static JuliaSet<Flt64> Create(Flt64 real, Flt64 imag)
        => new(PointFactory.point2(real, imag));
}

/// <summary>
/// Julia 集序列生成器 / Julia Set sequence generator.
/// </summary>
public sealed class JuliaSetGenerator : IGenerator<Point<Dim2, Flt64>> {
    public JuliaSet<Flt64> Set { get; }
    public Point<Dim2, Flt64> Z { get; private set; }

    public JuliaSetGenerator(JuliaSet<Flt64>? set = null, Point<Dim2, Flt64>? z = null) {
        Set = set ?? JuliaSet<Flt64>.Create(new Flt64(-0.7), new Flt64(0.27015));
        Z = z ?? PointFactory.point2(new Flt64(0.0), new Flt64(0.0));
    }

    public static JuliaSetGenerator Create(Flt64 real, Flt64 imag, Point<Dim2, Flt64>? z = null)
        => new(JuliaSet<Flt64>.Create(real, imag), z);

    public Point<Dim2, Flt64> Invoke() {
        Point<Dim2, Flt64> cur = Z;
        Z = Set.Invoke(Z);
        return cur;
    }
}
