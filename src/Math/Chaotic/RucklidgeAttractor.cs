#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Geometry;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// Rucklidge 吸引子 / Rucklidge Attractor.
/// 三维连续混沌系统，Euler 单步迭代。/ 3D continuous chaotic system, one Euler step.
/// </summary>
public sealed record RucklidgeAttractor<V>(V Alpha, V Kappa, V H)
    : IExtractor<Point<Dim3, V>, Point<Dim3, V>>
    where V : struct, IFloatingNumber<V> {
    public Point<Dim3, V> Invoke(Point<Dim3, V> p) {
        V x = p[0]; V y = p[1]; V z = p[2];
        V dx = Kappa.Negate().Times(x).Plus(Alpha.Times(y)).Minus(y.Times(z));
        V dy = x;
        V dz = z.Negate().Plus(y.Times(y));
        return new Point<Dim3, V>(new V[] {
            x.Plus(H.Times(dx)),
            y.Plus(H.Times(dy)),
            z.Plus(H.Times(dz))
        }, Dim3.Instance);
    }

    public static RucklidgeAttractor<Flt64> Create(Flt64? alpha = null, Flt64? kappa = null, Flt64? h = null)
        => new(alpha ?? new Flt64(2.0), kappa ?? new Flt64(6.7), h ?? new Flt64(0.01));
}

public sealed class RucklidgeAttractorGenerator : IGenerator<Point<Dim3, Flt64>> {
    public RucklidgeAttractor<Flt64> System { get; }
    public Point<Dim3, Flt64> X { get; private set; }

    public RucklidgeAttractorGenerator(RucklidgeAttractor<Flt64>? system = null, Point<Dim3, Flt64>? x = null) {
        System = system ?? RucklidgeAttractor<Flt64>.Create();
        X = x ?? PointFactory.point3(new Flt64(0.1), new Flt64(0.1), new Flt64(0.1));
    }

    public Point<Dim3, Flt64> Invoke() {
        Point<Dim3, Flt64> cur = X;
        X = System.Invoke(X);
        return cur;
    }
}
