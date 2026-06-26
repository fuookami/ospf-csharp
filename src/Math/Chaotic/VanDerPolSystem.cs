#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Geometry;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// 范德波尔系统 / Van der Pol System.
/// 二维连续系统，Euler 单步迭代。/ 2D continuous system, one Euler step.
/// </summary>
public sealed record VanDerPolSystem<V>(V A, V H) : IExtractor<Point<Dim2, V>, Point<Dim2, V>>
    where V : struct, IFloatingNumber<V> {
    public Point<Dim2, V> Invoke(Point<Dim2, V> p) {
        V x = p[0]; V y = p[1];
        V one = x.Constants.One;
        V two = ((IHasTwo<V>)x.Constants).Two;
        V three = two.Plus(one);
        V dx = A.Times(x.Minus(x.Times(x).Times(x).Div(three))).Minus(y);
        V dy = x.Div(A);
        return new Point<Dim2, V>(new V[] {
            x.Plus(H.Times(dx)),
            y.Plus(H.Times(dy))
        }, Dim2.Instance);
    }

    public static VanDerPolSystem<Flt64> Create(Flt64? a = null, Flt64? h = null)
        => new(a ?? Flt64.One, h ?? new Flt64(0.01));
}

public sealed class VanDerPolSystemGenerator : IGenerator<Point<Dim2, Flt64>> {
    public VanDerPolSystem<Flt64> System { get; }
    public Point<Dim2, Flt64> X { get; private set; }

    public VanDerPolSystemGenerator(VanDerPolSystem<Flt64>? system = null, Point<Dim2, Flt64>? x = null) {
        System = system ?? VanDerPolSystem<Flt64>.Create();
        X = x ?? PointFactory.point2(new Flt64(0.1), new Flt64(0.1));
    }

    public Point<Dim2, Flt64> Invoke() {
        Point<Dim2, Flt64> cur = X;
        X = System.Invoke(X);
        return cur;
    }
}
