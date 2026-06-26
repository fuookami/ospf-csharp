#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Geometry;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// 丁克贝尔映射 / Tinkerbell Map.
/// x_{n+1} = x^2 - y^2 + a*x + b*y, y_{n+1} = 2*x*y + c*x + d*y.
/// </summary>
public sealed record TinkerbellMap<V>(V A, V B, V C, V D) : IExtractor<Point<Dim2, V>, Point<Dim2, V>>
    where V : struct, IFloatingNumber<V> {
    public Point<Dim2, V> Invoke(Point<Dim2, V> p) {
        V x = p[0]; V y = p[1];
        V two = ((IHasTwo<V>)x.Constants).Two;
        return new Point<Dim2, V>(new V[] {
            x.Times(x).Minus(y.Times(y)).Plus(A.Times(x)).Plus(B.Times(y)),
            two.Times(x).Times(y).Plus(C.Times(x)).Plus(D.Times(y))
        }, Dim2.Instance);
    }

    public static TinkerbellMap<Flt64> Create(Flt64? a = null, Flt64? b = null, Flt64? c = null, Flt64? d = null)
        => new(a ?? new Flt64(0.9), b ?? new Flt64(-0.6013), c ?? new Flt64(2.0), d ?? new Flt64(0.5));
}

public sealed class TinkerbellMapGenerator : IGenerator<Point<Dim2, Flt64>> {
    public TinkerbellMap<Flt64> Map { get; }
    public Point<Dim2, Flt64> X { get; private set; }

    public TinkerbellMapGenerator(TinkerbellMap<Flt64>? map = null, Point<Dim2, Flt64>? x = null) {
        Map = map ?? TinkerbellMap<Flt64>.Create();
        X = x ?? PointFactory.point2(new Flt64(0.1), new Flt64(0.1));
    }

    public Point<Dim2, Flt64> Invoke() {
        Point<Dim2, Flt64> cur = X;
        X = Map.Invoke(X);
        return cur;
    }
}
