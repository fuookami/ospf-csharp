#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Geometry;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// 辛映射 / Symplectic Map.
/// 二维混沌映射。/ 2D chaotic map.
/// </summary>
public sealed record SymplecticMap<V>(V H) : IExtractor<Point<Dim2, V>, Point<Dim2, V>>
    where V : struct, IFloatingNumber<V> {
    public Point<Dim2, V> Invoke(Point<Dim2, V> p) {
        V x = p[0]; V y = p[1];
        V one = x.Constants.One;
        V onePlusX = one.Plus(x);
        V dx = x.Div(onePlusX);
        V dy = y.Times(onePlusX).Times(onePlusX);
        return new Point<Dim2, V>(new V[] {
            x.Plus(H.Times(dx)),
            y.Plus(H.Times(dy))
        }, Dim2.Instance);
    }

    public static SymplecticMap<Flt64> Create(Flt64? h = null)
        => new(h ?? new Flt64(0.01));
}

public sealed class SymplecticMapGenerator : IGenerator<Point<Dim2, Flt64>> {
    public SymplecticMap<Flt64> Map { get; }
    public Point<Dim2, Flt64> X { get; private set; }

    public SymplecticMapGenerator(SymplecticMap<Flt64>? map = null, Point<Dim2, Flt64>? x = null) {
        Map = map ?? SymplecticMap<Flt64>.Create();
        X = x ?? PointFactory.point2(new Flt64(0.1), new Flt64(0.1));
    }

    public Point<Dim2, Flt64> Invoke() {
        Point<Dim2, Flt64> cur = X;
        X = Map.Invoke(X);
        return cur;
    }
}
