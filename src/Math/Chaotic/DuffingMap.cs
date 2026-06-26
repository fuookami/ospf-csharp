#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Geometry;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// 达芬映射 / Duffing Map.
/// 二维混沌映射。x_{n+1} = y, y_{n+1} = -b*x + a*y - y^3.
/// </summary>
public sealed record DuffingMap<V>(V A, V B) : IExtractor<Point<Dim2, V>, Point<Dim2, V>>
    where V : struct, IFloatingNumber<V> {
    public Point<Dim2, V> Invoke(Point<Dim2, V> p) {
        V x = p[0]; V y = p[1];
        return new Point<Dim2, V>(new V[] {
            y,
            B.Negate().Times(x).Plus(A.Times(y)).Minus(y.Times(y).Times(y))
        }, Dim2.Instance);
    }

    public static DuffingMap<Flt64> Create(Flt64? a = null, Flt64? b = null)
        => new(a ?? new Flt64(2.75), b ?? new Flt64(0.2));
}

public sealed class DuffingMapGenerator : IGenerator<Point<Dim2, Flt64>> {
    public DuffingMap<Flt64> Map { get; }
    public Point<Dim2, Flt64> X { get; private set; }

    public DuffingMapGenerator(DuffingMap<Flt64>? map = null, Point<Dim2, Flt64>? x = null) {
        Map = map ?? DuffingMap<Flt64>.Create();
        X = x ?? PointFactory.point2(new Flt64(0.1), new Flt64(0.1));
    }

    public Point<Dim2, Flt64> Invoke() {
        Point<Dim2, Flt64> cur = X;
        X = Map.Invoke(X);
        return cur;
    }
}
