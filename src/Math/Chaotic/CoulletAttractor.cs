#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Geometry;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// 库莱吸引子 / Coullet Attractor.
/// 三维连续混沌系统，Euler 单步迭代。/ 3D continuous chaotic system, one Euler step.
/// </summary>
public sealed record CoulletAttractor<V>(V Alpha, V Beta, V Delta, V Zeta, V H)
    : IExtractor<Point<Dim3, V>, Point<Dim3, V>>
    where V : struct, IFloatingNumber<V> {
    public Point<Dim3, V> Invoke(Point<Dim3, V> p) {
        V x = p[0]; V y = p[1]; V z = p[2];
        V dx = y;
        V dy = z;
        V dz = Alpha.Times(x).Plus(Beta.Times(y)).Plus(Delta.Times(z)).Plus(Delta.Times(x.Cub()));
        return new Point<Dim3, V>(new V[] {
            x.Plus(H.Times(dx)),
            y.Plus(H.Times(dy)),
            z.Plus(H.Times(dz))
        }, Dim3.Instance);
    }

    public static CoulletAttractor<Flt64> Create(
        Flt64? alpha = null, Flt64? beta = null, Flt64? delta = null, Flt64? zeta = null, Flt64? h = null)
        => new(alpha ?? new Flt64(0.8), beta ?? new Flt64(-1.1), delta ?? new Flt64(-1.0),
               zeta ?? new Flt64(-0.45), h ?? new Flt64(0.01));
}

/// <summary>
/// 库莱吸引子生成器 / Coullet Attractor Generator.
/// </summary>
public sealed class CoulletAttractorGenerator : IGenerator<Point<Dim3, Flt64>> {
    public CoulletAttractor<Flt64> System { get; }
    public Point<Dim3, Flt64> X { get; private set; }

    public CoulletAttractorGenerator(CoulletAttractor<Flt64>? system = null, Point<Dim3, Flt64>? x = null) {
        System = system ?? CoulletAttractor<Flt64>.Create();
        X = x ?? PointFactory.point3(new Flt64(0.1), new Flt64(0.1), new Flt64(0.1));
    }

    public static CoulletAttractorGenerator Create(
        Flt64 alpha, Flt64 beta, Flt64 delta, Flt64 zeta, Flt64 h, Point<Dim3, Flt64>? x = null)
        => new(CoulletAttractor<Flt64>.Create(alpha, beta, delta, zeta, h), x);

    public Point<Dim3, Flt64> Invoke() {
        Point<Dim3, Flt64> cur = X;
        X = System.Invoke(X);
        return cur;
    }
}
