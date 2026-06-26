#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Geometry;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// 陈-李吸引子 / Chen-Lee Attractor.
/// 三维连续混沌系统，Euler 单步迭代。/ 3D continuous chaotic system, one Euler step.
/// </summary>
public sealed record ChenLeeAttractor<V>(V Alpha, V Beta, V Delta, V H, V Three)
    : IExtractor<Point<Dim3, V>, Point<Dim3, V>>
    where V : struct, IFloatingNumber<V> {
    public Point<Dim3, V> Invoke(Point<Dim3, V> x) {
        V dx = Alpha.Times(x[0]).Minus(x[1].Times(x[2]));
        V dy = Beta.Times(x[1]).Plus(x[0].Times(x[2]));
        V dz = Delta.Times(x[2]).Plus(x[0].Times(x[1]).Div(Three));
        return new Point<Dim3, V>(new V[] {
            x[0].Plus(H.Times(dx)),
            x[1].Plus(H.Times(dy)),
            x[2].Plus(H.Times(dz))
        }, Dim3.Instance);
    }

    public static ChenLeeAttractor<Flt64> Create(
        Flt64? alpha = null, Flt64? beta = null, Flt64? delta = null, Flt64? h = null, Flt64? three = null)
        => new(alpha ?? new Flt64(5.0), beta ?? new Flt64(-10.0), delta ?? new Flt64(0.38), h ?? new Flt64(0.01), three ?? new Flt64(3.0));
}

/// <summary>
/// 陈-李吸引子生成器 / Chen-Lee Attractor Generator.
/// </summary>
public sealed class ChenLeeAttractorGenerator : IGenerator<Point<Dim3, Flt64>> {
    public ChenLeeAttractor<Flt64> System { get; }
    public Point<Dim3, Flt64> X { get; private set; }

    public ChenLeeAttractorGenerator(ChenLeeAttractor<Flt64>? system = null, Point<Dim3, Flt64>? x = null) {
        System = system ?? ChenLeeAttractor<Flt64>.Create();
        X = x ?? PointFactory.point3(new Flt64(0.1), new Flt64(0.1), new Flt64(0.1));
    }

    public static ChenLeeAttractorGenerator Create(
        Flt64 alpha, Flt64 beta, Flt64 delta, Flt64 h, Point<Dim3, Flt64>? x = null)
        => new(ChenLeeAttractor<Flt64>.Create(alpha, beta, delta, h), x);

    public Point<Dim3, Flt64> Invoke() {
        Point<Dim3, Flt64> cur = X;
        X = System.Invoke(X);
        return cur;
    }
}
