#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Geometry;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// 达德拉斯吸引子 / Dadras Attractor.
/// 三维连续混沌系统，Euler 单步迭代。/ 3D continuous chaotic system, one Euler step.
/// </summary>
public sealed record DadrasAttractor<V>(V Gamma, V Epsilon, V Zeta, V Rho, V Sigma, V H)
    : IExtractor<Point<Dim3, V>, Point<Dim3, V>>
    where V : struct, IFloatingNumber<V> {
    public Point<Dim3, V> Invoke(Point<Dim3, V> p) {
        V x = p[0]; V y = p[1]; V z = p[2];
        V dx = y.Minus(Rho.Times(x)).Plus(Sigma.Times(y).Times(z));
        V dy = Gamma.Times(y).Minus(x.Times(z)).Plus(z);
        V dz = Zeta.Times(x).Times(y).Minus(Epsilon.Times(z));
        return new Point<Dim3, V>(new V[] {
            x.Plus(H.Times(dx)),
            y.Plus(H.Times(dy)),
            z.Plus(H.Times(dz))
        }, Dim3.Instance);
    }

    public static DadrasAttractor<Flt64> Create(
        Flt64? gamma = null, Flt64? epsilon = null, Flt64? zeta = null,
        Flt64? rho = null, Flt64? sigma = null, Flt64? h = null)
        => new(gamma ?? new Flt64(1.7), epsilon ?? new Flt64(9.0), zeta ?? new Flt64(2.0),
               rho ?? new Flt64(3.0), sigma ?? new Flt64(2.7), h ?? new Flt64(0.01));
}

/// <summary>
/// 达德拉斯吸引子生成器 / Dadras Attractor Generator.
/// </summary>
public sealed class DadrasAttractorGenerator : IGenerator<Point<Dim3, Flt64>> {
    public DadrasAttractor<Flt64> System { get; }
    public Point<Dim3, Flt64> X { get; private set; }

    public DadrasAttractorGenerator(DadrasAttractor<Flt64>? system = null, Point<Dim3, Flt64>? x = null) {
        System = system ?? DadrasAttractor<Flt64>.Create();
        X = x ?? PointFactory.point3(new Flt64(0.1), new Flt64(0.1), new Flt64(0.1));
    }

    public static DadrasAttractorGenerator Create(
        Flt64 gamma, Flt64 epsilon, Flt64 zeta, Flt64 rho, Flt64 sigma, Flt64 h,
        Point<Dim3, Flt64>? x = null)
        => new(DadrasAttractor<Flt64>.Create(gamma, epsilon, zeta, rho, sigma, h), x);

    public Point<Dim3, Flt64> Invoke() {
        Point<Dim3, Flt64> cur = X;
        X = System.Invoke(X);
        return cur;
    }
}
