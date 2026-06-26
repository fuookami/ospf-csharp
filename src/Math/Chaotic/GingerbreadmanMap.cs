#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Geometry;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// 姜饼人映射 / Gingerbreadman Map.
/// x_{n+1} = 1 - y + |x|, y_{n+1} = x.
/// </summary>
public sealed record GingerbreadmanMap<V>(V One) : IExtractor<Point<Dim2, V>, Point<Dim2, V>>
    where V : struct, IFloatingNumber<V> {
    public Point<Dim2, V> Invoke(Point<Dim2, V> p) {
        V x = p[0]; V y = p[1];
        return new Point<Dim2, V>(new V[] {
            One.Minus(y).Plus(x.Abs()),
            x
        }, Dim2.Instance);
    }

    public static GingerbreadmanMap<Flt64> Create()
        => new(Flt64.One);
}

public sealed class GingerbreadmanMapGenerator : IGenerator<Point<Dim2, Flt64>> {
    public GingerbreadmanMap<Flt64> Map { get; }
    public Point<Dim2, Flt64> X { get; private set; }

    public GingerbreadmanMapGenerator(GingerbreadmanMap<Flt64>? map = null, Point<Dim2, Flt64>? x = null) {
        Map = map ?? GingerbreadmanMap<Flt64>.Create();
        X = x ?? PointFactory.point2(new Flt64(0.1), new Flt64(0.1));
    }

    public Point<Dim2, Flt64> Invoke() {
        Point<Dim2, Flt64> cur = X;
        X = Map.Invoke(X);
        return cur;
    }
}
