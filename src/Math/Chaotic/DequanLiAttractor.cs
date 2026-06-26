#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Geometry;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// 德全-李吸引子 / Dequan-Li Attractor.
/// 三维连续混沌系统，Euler 单步迭代。/ 3D continuous chaotic system, one Euler step.
/// </summary>
public sealed record DequanLiAttractor<V>(V Alpha, V Beta, V Delta, V Epsilon, V Zeta, V Rho, V H)
    : IExtractor<Point<Dim3, V>, Point<Dim3, V>>
    where V : struct, IFloatingNumber<V> {
    public Point<Dim3, V> Invoke(Point<Dim3, V> p) {
        V x = p[0]; V y = p[1]; V z = p[2];
        V dx = Alpha.Times(y.Minus(x)).Plus(Delta.Times(x).Times(z));
        V dy = Rho.Times(x).Plus(Zeta.Times(y)).Minus(x.Times(z));
        V dz = Beta.Times(z).Plus(x.Times(y)).Minus(Epsilon.Times(x.Sqr()));
        return new Point<Dim3, V>(new V[] {
            x.Plus(H.Times(dx)),
            y.Plus(H.Times(dy)),
            z.Plus(H.Times(dz))
        }, Dim3.Instance);
    }

    public static DequanLiAttractor<Flt64> Create(
        Flt64? alpha = null, Flt64? beta = null, Flt64? delta = null,
        Flt64? epsilon = null, Flt64? zeta = null, Flt64? rho = null, Flt64? h = null)
        => new(alpha ?? new Flt64(40.0), beta ?? new Flt64(1.833), delta ?? new Flt64(0.16),
               epsilon ?? new Flt64(0.65), zeta ?? new Flt64(20.0), rho ?? new Flt64(55.0),
               h ?? new Flt64(0.01));
}

/// <summary>
/// 德全-李吸引子生成器 / Dequan-Li Attractor Generator.
/// </summary>
public sealed class DequanLiAttractorGenerator : IGenerator<Point<Dim3, Flt64>> {
    public DequanLiAttractor<Flt64> System { get; }
    public Point<Dim3, Flt64> X { get; private set; }

    public DequanLiAttractorGenerator(DequanLiAttractor<Flt64>? system = null, Point<Dim3, Flt64>? x = null) {
        System = system ?? DequanLiAttractor<Flt64>.Create();
        X = x ?? PointFactory.point3(new Flt64(0.1), new Flt64(0.1), new Flt64(0.1));
    }

    public static DequanLiAttractorGenerator Create(
        Flt64 alpha, Flt64 beta, Flt64 delta, Flt64 epsilon, Flt64 zeta, Flt64 rho, Flt64 h,
        Point<Dim3, Flt64>? x = null)
        => new(DequanLiAttractor<Flt64>.Create(alpha, beta, delta, epsilon, zeta, rho, h), x);

    public Point<Dim3, Flt64> Invoke() {
        Point<Dim3, Flt64> cur = X;
        X = System.Invoke(X);
        return cur;
    }
}
