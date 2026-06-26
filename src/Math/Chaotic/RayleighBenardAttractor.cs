#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Geometry;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// Rayleigh-Benard 吸引子 / Rayleigh-Benard Attractor.
/// 三维连续混沌系统，Euler 单步迭代。/ 3D continuous chaotic system, one Euler step.
/// </summary>
public sealed record RayleighBenardAttractor<V>(V Alpha, V Beta, V Gamma, V H)
    : IExtractor<Point<Dim3, V>, Point<Dim3, V>>
    where V : struct, IFloatingNumber<V> {
    public Point<Dim3, V> Invoke(Point<Dim3, V> p) {
        V x = p[0]; V y = p[1]; V z = p[2];
        V dx = Alpha.Negate().Times(x).Plus(Alpha.Times(y));
        V dy = Gamma.Times(x).Minus(y).Minus(x.Times(z));
        V dz = x.Times(y).Minus(Beta.Times(z));
        return new Point<Dim3, V>(new V[] {
            x.Plus(H.Times(dx)),
            y.Plus(H.Times(dy)),
            z.Plus(H.Times(dz))
        }, Dim3.Instance);
    }

    public static RayleighBenardAttractor<Flt64> Create(
        Flt64? alpha = null, Flt64? beta = null, Flt64? gamma = null, Flt64? h = null)
        => new(alpha ?? new Flt64(9.0), beta ?? new Flt64(5.0), gamma ?? new Flt64(12.0), h ?? new Flt64(0.01));
}

public sealed class RayleighBenardAttractorGenerator : IGenerator<Point<Dim3, Flt64>> {
    public RayleighBenardAttractor<Flt64> System { get; }
    public Point<Dim3, Flt64> X { get; private set; }

    public RayleighBenardAttractorGenerator(RayleighBenardAttractor<Flt64>? system = null, Point<Dim3, Flt64>? x = null) {
        System = system ?? RayleighBenardAttractor<Flt64>.Create();
        X = x ?? PointFactory.point3(new Flt64(0.1), new Flt64(0.1), new Flt64(0.1));
    }

    public Point<Dim3, Flt64> Invoke() {
        Point<Dim3, Flt64> cur = X;
        X = System.Invoke(X);
        return cur;
    }
}
