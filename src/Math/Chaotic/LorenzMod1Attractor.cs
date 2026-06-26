#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Geometry;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// Lorenz 修正 1 吸引子 / Lorenz Mod 1 Attractor.
/// 三维连续混沌系统，Euler 单步迭代。/ 3D continuous chaotic system, one Euler step.
/// </summary>
public sealed record LorenzMod1Attractor<V>(V Alpha, V Beta, V Delta, V Zeta, V H)
    : IExtractor<Point<Dim3, V>, Point<Dim3, V>>
    where V : struct, IFloatingNumber<V> {
    public Point<Dim3, V> Invoke(Point<Dim3, V> p) {
        V x = p[0]; V y = p[1]; V z = p[2];
        V dx = Alpha.Negate().Times(x).Plus(y.Times(y)).Minus(z.Times(z)).Plus(Alpha.Times(Zeta));
        V dy = x.Times(y.Minus(Beta.Times(z))).Plus(Delta);
        V dz = z.Plus(x.Times(Beta.Times(y).Plus(z)));
        return new Point<Dim3, V>(new V[] {
            x.Plus(H.Times(dx)),
            y.Plus(H.Times(dy)),
            z.Plus(H.Times(dz))
        }, Dim3.Instance);
    }

    public static LorenzMod1Attractor<Flt64> Create(
        Flt64? alpha = null, Flt64? beta = null, Flt64? delta = null, Flt64? zeta = null, Flt64? h = null)
        => new(alpha ?? new Flt64(0.1), beta ?? new Flt64(4.0), delta ?? new Flt64(0.08), zeta ?? new Flt64(14.0), h ?? new Flt64(0.01));
}

public sealed class LorenzMod1AttractorGenerator : IGenerator<Point<Dim3, Flt64>> {
    public LorenzMod1Attractor<Flt64> System { get; }
    public Point<Dim3, Flt64> X { get; private set; }

    public LorenzMod1AttractorGenerator(LorenzMod1Attractor<Flt64>? system = null, Point<Dim3, Flt64>? x = null) {
        System = system ?? LorenzMod1Attractor<Flt64>.Create();
        X = x ?? PointFactory.point3(new Flt64(0.1), new Flt64(0.1), new Flt64(0.1));
    }

    public Point<Dim3, Flt64> Invoke() {
        Point<Dim3, Flt64> cur = X;
        X = System.Invoke(X);
        return cur;
    }
}
