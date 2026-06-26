#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Geometry;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// 四涡卷超混沌吸引子 / Four-Scroll Hyper-Chaotic Attractor.
/// 四维连续混沌系统，Euler 单步迭代。/ 4D continuous chaotic system, one Euler step.
/// </summary>
public sealed record FourScrollHyperChaoticAttractor<V>(V A, V B, V C, V D, V H)
    : IExtractor<Point<Dim4, V>, Point<Dim4, V>>
    where V : struct, IFloatingNumber<V> {
    public Point<Dim4, V> Invoke(Point<Dim4, V> p) {
        V x = p[0]; V y = p[1]; V z = p[2]; V u = p[3];
        V dx = A.Negate().Times(x).Plus(y.Times(z)).Plus(z);
        V dy = B.Times(y).Minus(x.Times(z));
        V dz = C.Negate().Times(z).Plus(x.Times(y)).Plus(u);
        V du = y.Minus(D.Times(u));
        return new Point<Dim4, V>(new V[] {
            x.Plus(H.Times(dx)),
            y.Plus(H.Times(dy)),
            z.Plus(H.Times(dz)),
            u.Plus(H.Times(du))
        }, Dim4.Instance);
    }

    public static FourScrollHyperChaoticAttractor<Flt64> Create(
        Flt64? a = null, Flt64? b = null, Flt64? c = null, Flt64? d = null, Flt64? h = null)
        => new(a ?? new Flt64(0.5), b ?? new Flt64(0.5), c ?? new Flt64(0.5), d ?? new Flt64(0.5), h ?? new Flt64(0.01));
}

public sealed class FourScrollHyperChaoticAttractorGenerator : IGenerator<Point<Dim4, Flt64>> {
    public FourScrollHyperChaoticAttractor<Flt64> System { get; }
    public Point<Dim4, Flt64> X { get; private set; }

    public FourScrollHyperChaoticAttractorGenerator(FourScrollHyperChaoticAttractor<Flt64>? system = null, Point<Dim4, Flt64>? x = null) {
        System = system ?? FourScrollHyperChaoticAttractor<Flt64>.Create();
        X = x ?? PointFactory.point4(new Flt64(0.1), new Flt64(0.1), new Flt64(0.1), new Flt64(0.1));
    }

    public Point<Dim4, Flt64> Invoke() {
        Point<Dim4, Flt64> cur = X;
        X = System.Invoke(X);
        return cur;
    }
}
