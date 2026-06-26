#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Geometry;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// 洛兹映射 / Lozi Map.
/// x_{n+1} = 1 + y - a*|x|, y_{n+1} = b*x.
/// </summary>
public sealed record LoziMap<V>(V A, V B) : IExtractor<Point<Dim2, V>, Point<Dim2, V>>
    where V : struct, IFloatingNumber<V> {
    public Point<Dim2, V> Invoke(Point<Dim2, V> p) {
        V x = p[0]; V y = p[1];
        V one = x.Constants.One;
        return new Point<Dim2, V>(new V[] {
            one.Plus(y).Minus(A.Times(x.Abs())),
            B.Times(x)
        }, Dim2.Instance);
    }

    public static LoziMap<Flt64> Create(Flt64? a = null, Flt64? b = null)
        => new(a ?? new Flt64(1.7), b ?? new Flt64(0.5));
}

public sealed class LoziMapGenerator : IGenerator<Point<Dim2, Flt64>> {
    public LoziMap<Flt64> Map { get; }
    public Point<Dim2, Flt64> X { get; private set; }

    public LoziMapGenerator(LoziMap<Flt64>? map = null, Point<Dim2, Flt64>? x = null) {
        Map = map ?? LoziMap<Flt64>.Create();
        X = x ?? PointFactory.point2(new Flt64(0.1), new Flt64(0.1));
    }

    public Point<Dim2, Flt64> Invoke() {
        Point<Dim2, Flt64> cur = X;
        X = Map.Invoke(X);
        return cur;
    }
}
