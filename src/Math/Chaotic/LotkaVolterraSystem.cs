#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Geometry;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// Lotka-Volterra 系统（捕食者-猎物模型）/ Lotka-Volterra System (Predator-Prey Model).
/// 二维连续系统，Euler 单步迭代。/ 2D continuous system, one Euler step.
/// </summary>
public sealed record LotkaVolterraSystem<V>(V A, V B, V C, V D, V H)
    : IExtractor<Point<Dim2, V>, Point<Dim2, V>>
    where V : struct, IFloatingNumber<V> {
    public Point<Dim2, V> Invoke(Point<Dim2, V> p) {
        V x = p[0]; V y = p[1];
        V dx = A.Times(x).Minus(B.Times(x).Times(y));
        V dy = D.Times(x).Times(y).Minus(C.Times(y));
        return new Point<Dim2, V>(new V[] {
            x.Plus(H.Times(dx)),
            y.Plus(H.Times(dy))
        }, Dim2.Instance);
    }

    public static LotkaVolterraSystem<Flt64> Create(
        Flt64? a = null, Flt64? b = null, Flt64? c = null, Flt64? d = null, Flt64? h = null)
        => new(a ?? new Flt64(1.0), b ?? new Flt64(0.1), c ?? new Flt64(1.0), d ?? new Flt64(0.1), h ?? new Flt64(0.01));
}

public sealed class LotkaVolterraSystemGenerator : IGenerator<Point<Dim2, Flt64>> {
    public LotkaVolterraSystem<Flt64> System { get; }
    public Point<Dim2, Flt64> X { get; private set; }

    public LotkaVolterraSystemGenerator(LotkaVolterraSystem<Flt64>? system = null, Point<Dim2, Flt64>? x = null) {
        System = system ?? LotkaVolterraSystem<Flt64>.Create();
        X = x ?? PointFactory.point2(new Flt64(0.5), new Flt64(0.5));
    }

    public Point<Dim2, Flt64> Invoke() {
        Point<Dim2, Flt64> cur = X;
        X = System.Invoke(X);
        return cur;
    }
}
