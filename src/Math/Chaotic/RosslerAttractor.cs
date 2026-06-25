#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Geometry;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// Rossler 吸引子 / Rossler Attractor.
/// </summary>
public sealed record RosslerAttractor<V>(V Alpha, V Beta, V Zeta, V H)
    : IExtractor<Point<Dim3, V>, Point<Dim3, V>>
    where V : struct, IFloatingNumber<V> {
    public Point<Dim3, V> Invoke(Point<Dim3, V> p) {
        V x = p[0]; V y = p[1]; V z = p[2];
        V dx = y.Negate().Minus(z);
        V dy = x.Plus(Alpha.Times(y));
        V dz = Beta.Plus(z.Times(x.Minus(Zeta)));
        return new Point<Dim3, V>(new V[] {
            x.Plus(H.Times(dx)),
            y.Plus(H.Times(dy)),
            z.Plus(H.Times(dz))
        }, Dim3.Instance);
    }

    public static RosslerAttractor<Flt64> Create(
        Flt64? alpha = null, Flt64? beta = null, Flt64? zeta = null, Flt64? h = null)
        => new(alpha ?? new Flt64(0.2), beta ?? new Flt64(0.2), zeta ?? new Flt64(5.7), h ?? new Flt64(0.01));
}

public sealed class RosslerAttractorGenerator : IGenerator<Point<Dim3, Flt64>> {
    public RosslerAttractor<Flt64> System { get; }
    public Point<Dim3, Flt64> X { get; private set; }

    public RosslerAttractorGenerator(RosslerAttractor<Flt64>? system = null, Point<Dim3, Flt64>? x = null) {
        System = system ?? RosslerAttractor<Flt64>.Create();
        X = x ?? PointFactory.point3(new Flt64(0.1), new Flt64(0.1), new Flt64(0.1));
    }

    public Point<Dim3, Flt64> Invoke() {
        Point<Dim3, Flt64> cur = X;
        X = System.Invoke(X);
        return cur;
    }
}
