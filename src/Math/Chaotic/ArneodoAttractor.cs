#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Geometry;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// Arneodo 吸引子 / Arneodo Attractor.
/// 三维连续混沌系统，Euler 单步迭代。/ 3D continuous chaotic system, one Euler step.
/// </summary>
public sealed record ArneodoAttractor<V>(V Alpha, V Beta, V Delta, V H)
    : IExtractor<Point<Dim3, V>, Point<Dim3, V>>
    where V : struct, IFloatingNumber<V> {
    public Point<Dim3, V> Invoke(Point<Dim3, V> p) {
        V x = p[0]; V y = p[1]; V z = p[2];
        V dx = y;
        V dy = z;
        V dz = Alpha.Negate().Times(x).Minus(Beta.Times(y)).Minus(z).Plus(Delta.Times(x.Cub()));
        return new Point<Dim3, V>(new V[] {
            x.Plus(H.Times(dx)),
            y.Plus(H.Times(dy)),
            z.Plus(H.Times(dz))
        }, Dim3.Instance);
    }

    public static ArneodoAttractor<Flt64> Create(
        Flt64? alpha = null, Flt64? beta = null, Flt64? delta = null, Flt64? h = null)
        => new(alpha ?? new Flt64(-5.5), beta ?? new Flt64(3.5), delta ?? new Flt64(-1.0), h ?? new Flt64(0.01));
}

/// <summary>
/// Arneodo 吸引子生成器 / Arneodo Attractor Generator.
/// </summary>
public sealed class ArneodoAttractorGenerator : IGenerator<Point<Dim3, Flt64>> {
    public ArneodoAttractor<Flt64> System { get; }
    public Point<Dim3, Flt64> X { get; private set; }

    public ArneodoAttractorGenerator(ArneodoAttractor<Flt64>? system = null, Point<Dim3, Flt64>? x = null) {
        System = system ?? ArneodoAttractor<Flt64>.Create();
        X = x ?? PointFactory.point3(new Flt64(0.1), new Flt64(0.1), new Flt64(0.1));
    }

    public static ArneodoAttractorGenerator Create(
        Flt64 alpha, Flt64 beta, Flt64 delta, Flt64 h, Point<Dim3, Flt64>? x = null)
        => new(ArneodoAttractor<Flt64>.Create(alpha, beta, delta, h), x);

    public Point<Dim3, Flt64> Invoke() {
        Point<Dim3, Flt64> cur = X;
        X = System.Invoke(X);
        return cur;
    }
}
