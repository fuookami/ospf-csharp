#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Geometry;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// Liu-Chen 吸引子 / Liu-Chen Attractor.
/// 三维连续混沌系统，Euler 单步迭代。/ 3D continuous chaotic system, one Euler step.
/// </summary>
public sealed record LiuChenAttractor<V>(V Alpha, V Beta, V Delta, V Epsilon, V Zeta, V Xi, V Rho, V H)
    : IExtractor<Point<Dim3, V>, Point<Dim3, V>>
    where V : struct, IFloatingNumber<V> {
    public Point<Dim3, V> Invoke(Point<Dim3, V> p) {
        V x = p[0]; V y = p[1]; V z = p[2];
        V dx = Alpha.Times(y).Plus(Beta.Times(x)).Plus(Zeta.Times(y).Times(z));
        V dy = Delta.Times(y).Minus(z).Plus(Epsilon.Times(x).Times(z));
        V dz = Xi.Times(z).Plus(Rho.Times(x).Times(y));
        return new Point<Dim3, V>(new V[] {
            x.Plus(H.Times(dx)),
            y.Plus(H.Times(dy)),
            z.Plus(H.Times(dz))
        }, Dim3.Instance);
    }

    public static LiuChenAttractor<Flt64> Create(
        Flt64? alpha = null, Flt64? beta = null, Flt64? delta = null, Flt64? epsilon = null,
        Flt64? zeta = null, Flt64? xi = null, Flt64? rho = null, Flt64? h = null)
        => new(alpha ?? new Flt64(2.4), beta ?? new Flt64(-3.78), delta ?? new Flt64(14.0), epsilon ?? new Flt64(-11.0),
            zeta ?? new Flt64(4.0), xi ?? new Flt64(5.58), rho ?? new Flt64(1.0), h ?? new Flt64(0.01));
}

public sealed class LiuChenAttractorGenerator : IGenerator<Point<Dim3, Flt64>> {
    public LiuChenAttractor<Flt64> System { get; }
    public Point<Dim3, Flt64> X { get; private set; }

    public LiuChenAttractorGenerator(LiuChenAttractor<Flt64>? system = null, Point<Dim3, Flt64>? x = null) {
        System = system ?? LiuChenAttractor<Flt64>.Create();
        X = x ?? PointFactory.point3(new Flt64(0.1), new Flt64(0.1), new Flt64(0.1));
    }

    public Point<Dim3, Flt64> Invoke() {
        Point<Dim3, Flt64> cur = X;
        X = System.Invoke(X);
        return cur;
    }
}
