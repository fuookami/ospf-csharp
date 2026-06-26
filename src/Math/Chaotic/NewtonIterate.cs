#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Geometry;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// Newton 迭代 / Newton Iterate.
/// 二维混沌映射。/ 2D chaotic map.
/// </summary>
public sealed record NewtonIterate<V>(V Three, V Four, V TwoThirds, V Two)
    : IExtractor<Point<Dim2, V>, Point<Dim2, V>>
    where V : struct, IFloatingNumber<V> {
    public Point<Dim2, V> Invoke(Point<Dim2, V> p) {
        V x = p[0]; V y = p[1];
        V zero = x.Constants.Zero;
        if (x.Leq(zero)) return new Point<Dim2, V>(new V[] { zero, zero }, Dim2.Instance);
        V x2 = x.Times(x); V y2 = y.Times(y);
        V d = Three.Times(x2.Minus(y2).Times(x2.Minus(y2)).Plus(Four.Times(x2).Times(y2)));
        if (d.Eq(zero)) return new Point<Dim2, V>(new V[] { zero, zero }, Dim2.Instance);
        return new Point<Dim2, V>(new V[] {
            TwoThirds.Times(x).Plus(x2.Minus(y2).Div(d)),
            TwoThirds.Times(y).Minus(Two.Times(x).Times(y).Div(d))
        }, Dim2.Instance);
    }

    public static NewtonIterate<Flt64> Create()
        => new(new Flt64(3.0), new Flt64(4.0), new Flt64(2.0) / new Flt64(3.0), new Flt64(2.0));
}

public sealed class NewtonIterateGenerator : IGenerator<Point<Dim2, Flt64>> {
    public NewtonIterate<Flt64> Map { get; }
    public Point<Dim2, Flt64> X { get; private set; }

    public NewtonIterateGenerator(NewtonIterate<Flt64>? map = null, Point<Dim2, Flt64>? x = null) {
        Map = map ?? NewtonIterate<Flt64>.Create();
        X = x ?? PointFactory.point2(new Flt64(1.5), new Flt64(0.5));
    }

    public Point<Dim2, Flt64> Invoke() {
        Point<Dim2, Flt64> cur = X;
        X = Map.Invoke(X);
        return cur;
    }
}
