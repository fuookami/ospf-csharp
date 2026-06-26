#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Geometry;
using System;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// 蔡氏吸引子 / Chua Attractor.
/// 三维连续混沌系统，含分段线性非线性。/ 3D continuous chaotic system with piecewise-linear nonlinearity.
/// </summary>
public sealed record ChuaAttractor<V>(V Alpha, V Beta, V Delta, V Epsilon, V Zeta, V H)
    : IExtractor<Point<Dim3, V>, Point<Dim3, V>>
    where V : struct, IFloatingNumber<V> {
    public Point<Dim3, V> Invoke(Point<Dim3, V> x) {
        V one = x[0].Constants.One;
        V g = Epsilon.Times(x[0]).Plus(Delta.Minus(Epsilon).Times(x[0].Plus(one).Abs().Minus(x[0].Minus(one).Abs())));
        V dx = Alpha.Times(x[1].Minus(x[0]).Minus(g));
        V dy = Beta.Times(x[0].Minus(x[1]).Plus(x[2]));
        V dz = Zeta.Negate().Times(x[1]);
        return new Point<Dim3, V>(new V[] {
            x[0].Plus(H.Times(dx)),
            x[1].Plus(H.Times(dy)),
            x[2].Plus(H.Times(dz))
        }, Dim3.Instance);
    }

    public static ChuaAttractor<Flt64> Create(
        Flt64? alpha = null, Flt64? beta = null, Flt64? delta = null,
        Flt64? epsilon = null, Flt64? zeta = null, Flt64? h = null)
        => new(alpha ?? new Flt64(15.6), beta ?? new Flt64(1.0), delta ?? new Flt64(-1.0),
               epsilon ?? new Flt64(0.0), zeta ?? new Flt64(25.58), h ?? new Flt64(0.01));
}

/// <summary>
/// 蔡氏吸引子生成器 / Chua Attractor Generator.
/// </summary>
public sealed class ChuaAttractorGenerator : IGenerator<Point<Dim3, Flt64>> {
    public ChuaAttractor<Flt64> System { get; }
    public Point<Dim3, Flt64> X { get; private set; }

    public ChuaAttractorGenerator(ChuaAttractor<Flt64>? system = null, Point<Dim3, Flt64>? x = null) {
        System = system ?? ChuaAttractor<Flt64>.Create();
        X = x ?? PointFactory.point3(new Flt64(0.1), new Flt64(0.1), new Flt64(0.1));
    }

    public static ChuaAttractorGenerator Create(
        Flt64 alpha, Flt64 beta, Flt64 delta, Flt64 epsilon, Flt64 zeta, Flt64 h,
        Point<Dim3, Flt64>? x = null)
        => new(ChuaAttractor<Flt64>.Create(alpha, beta, delta, epsilon, zeta, h), x);

    public Point<Dim3, Flt64> Invoke() {
        Point<Dim3, Flt64> cur = X;
        X = System.Invoke(X);
        return cur;
    }
}
