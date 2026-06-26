#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Geometry;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// 相泽吸引子 / Aizawa Attractor.
/// 三维连续混沌系统，Euler 单步迭代。/ 3D continuous chaotic system, one Euler step.
/// </summary>
public sealed record AizawaAttractor<V>(
    V Alpha, V Beta, V Gamma, V Delta, V Epsilon, V Zeta, V H)
    : IExtractor<Point<Dim3, V>, Point<Dim3, V>>
    where V : struct, IFloatingNumber<V> {
    public Point<Dim3, V> Invoke(Point<Dim3, V> p) {
        V x = p[0]; V y = p[1]; V z = p[2];
        V dy = Delta.Times(x).Plus(z.Minus(Beta).Times(y));
        V dx = z.Minus(Beta).Times(x).Minus(dy);
        var three = (V)(object)new Flt64(3.0);
        V xy2Term = x.Sqr().Plus(y.Sqr()).Times(x.Constants.One.Plus(Epsilon.Times(z)));
        V dz = Gamma.Plus(Alpha.Times(x)).Minus(z.Cub().Div(three))
            .Minus(xy2Term).Plus(Zeta.Times(z).Times(x.Cub()));
        return new Point<Dim3, V>(new V[] {
            x.Plus(H.Times(dx)),
            y.Plus(H.Times(dz)),
            z.Plus(H.Times(dy))
        }, Dim3.Instance);
    }

    public static AizawaAttractor<Flt64> Create(
        Flt64? alpha = null, Flt64? beta = null, Flt64? gamma = null,
        Flt64? delta = null, Flt64? epsilon = null, Flt64? zeta = null, Flt64? h = null)
        => new(alpha ?? new Flt64(0.95), beta ?? new Flt64(0.7), gamma ?? new Flt64(0.6),
            delta ?? new Flt64(3.5), epsilon ?? new Flt64(0.25), zeta ?? new Flt64(0.1), h ?? new Flt64(0.01));
}

/// <summary>
/// 相泽吸引子生成器 / Aizawa Attractor Generator.
/// </summary>
public sealed class AizawaAttractorGenerator : IGenerator<Point<Dim3, Flt64>> {
    public AizawaAttractor<Flt64> System { get; }
    public Point<Dim3, Flt64> X { get; private set; }

    public AizawaAttractorGenerator(AizawaAttractor<Flt64>? system = null, Point<Dim3, Flt64>? x = null) {
        System = system ?? AizawaAttractor<Flt64>.Create();
        X = x ?? PointFactory.point3(new Flt64(0.1), new Flt64(0.1), new Flt64(0.1));
    }

    public static AizawaAttractorGenerator Create(
        Flt64 alpha, Flt64 beta, Flt64 gamma, Flt64 delta, Flt64 epsilon, Flt64 zeta, Flt64 h,
        Point<Dim3, Flt64>? x = null)
        => new(AizawaAttractor<Flt64>.Create(alpha, beta, gamma, delta, epsilon, zeta, h), x);

    public Point<Dim3, Flt64> Invoke() {
        Point<Dim3, Flt64> cur = X;
        X = System.Invoke(X);
        return cur;
    }
}
