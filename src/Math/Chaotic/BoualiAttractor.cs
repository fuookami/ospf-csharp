#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Geometry;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// Bouali 吸引子 / Bouali Attractor.
/// 三维连续混沌系统，Euler 单步迭代。/ 3D continuous chaotic system, one Euler step.
/// </summary>
public sealed record BoualiAttractor<V>(V Alpha, V Zeta, V H)
    : IExtractor<Point<Dim3, V>, Point<Dim3, V>>
    where V : struct, IFloatingNumber<V> {
    public Point<Dim3, V> Invoke(Point<Dim3, V> p) {
        V x = p[0]; V y = p[1]; V z = p[2];
        var c4 = (V)(object)new Flt64(4.0);
        var c15 = (V)(object)new Flt64(1.5);
        var c005 = (V)(object)new Flt64(0.05);
        V dx = x.Times(c4.Minus(y)).Plus(Alpha.Times(z));
        V dy = y.Negate().Times(x.Constants.One.Minus(x.Sqr()));
        V dz = x.Negate().Times(c15.Minus(Zeta.Times(z))).Minus(c005.Times(z));
        return new Point<Dim3, V>(new V[] {
            x.Plus(H.Times(dx)),
            y.Plus(H.Times(dy)),
            z.Plus(H.Times(dz))
        }, Dim3.Instance);
    }

    public static BoualiAttractor<Flt64> Create(
        Flt64? alpha = null, Flt64? zeta = null, Flt64? h = null)
        => new(alpha ?? new Flt64(0.3), zeta ?? new Flt64(1.0), h ?? new Flt64(0.01));
}

/// <summary>
/// Bouali 吸引子生成器 / Bouali Attractor Generator.
/// </summary>
public sealed class BoualiAttractorGenerator : IGenerator<Point<Dim3, Flt64>> {
    public BoualiAttractor<Flt64> System { get; }
    public Point<Dim3, Flt64> X { get; private set; }

    public BoualiAttractorGenerator(BoualiAttractor<Flt64>? system = null, Point<Dim3, Flt64>? x = null) {
        System = system ?? BoualiAttractor<Flt64>.Create();
        X = x ?? PointFactory.point3(new Flt64(0.1), new Flt64(0.1), new Flt64(0.1));
    }

    public static BoualiAttractorGenerator Create(
        Flt64 alpha, Flt64 zeta, Flt64 h, Point<Dim3, Flt64>? x = null)
        => new(BoualiAttractor<Flt64>.Create(alpha, zeta, h), x);

    public Point<Dim3, Flt64> Invoke() {
        Point<Dim3, Flt64> cur = X;
        X = System.Invoke(X);
        return cur;
    }
}
