#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Geometry;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// Qi 吸引子（四维超混沌）/ Qi Attractor (4D Hyperchaotic).
/// 四维连续混沌系统，Euler 单步迭代。/ 4D continuous chaotic system, one Euler step.
/// </summary>
public sealed record QiAttractor<V>(V Alpha, V Beta, V Delta, V Zeta, V H)
    : IExtractor<Point<Dim4, V>, Point<Dim4, V>>
    where V : struct, IFloatingNumber<V> {
    public Point<Dim4, V> Invoke(Point<Dim4, V> p) {
        V x = p[0]; V y = p[1]; V z = p[2]; V u = p[3];
        V dx = Alpha.Times(y.Minus(x)).Plus(y.Times(z).Times(u));
        V dy = Beta.Times(x.Plus(y)).Minus(x.Times(z).Times(u));
        V dz = Zeta.Negate().Times(z).Plus(x.Times(y).Times(u));
        V du = Delta.Negate().Times(u).Plus(x.Times(y).Times(z));
        return new Point<Dim4, V>(new V[] {
            x.Plus(H.Times(dx)),
            y.Plus(H.Times(dy)),
            z.Plus(H.Times(dz)),
            u.Plus(H.Times(du))
        }, Dim4.Instance);
    }

    public static QiAttractor<Flt64> Create(
        Flt64? alpha = null, Flt64? beta = null, Flt64? delta = null, Flt64? zeta = null, Flt64? h = null)
        => new(alpha ?? new Flt64(30.0), beta ?? new Flt64(10.0), delta ?? new Flt64(10.0), zeta ?? new Flt64(1.0), h ?? new Flt64(0.001));
}

public sealed class QiAttractorGenerator : IGenerator<Point<Dim4, Flt64>> {
    public QiAttractor<Flt64> System { get; }
    public Point<Dim4, Flt64> X { get; private set; }

    public QiAttractorGenerator(QiAttractor<Flt64>? system = null, Point<Dim4, Flt64>? x = null) {
        System = system ?? QiAttractor<Flt64>.Create();
        X = x ?? PointFactory.point4(new Flt64(0.1), new Flt64(0.1), new Flt64(0.1), new Flt64(0.1));
    }

    public Point<Dim4, Flt64> Invoke() {
        Point<Dim4, Flt64> cur = X;
        X = System.Invoke(X);
        return cur;
    }
}
