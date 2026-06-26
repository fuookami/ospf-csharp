#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Geometry;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// Anishchenko-Astakhov 吸引子 / Anishchenko-Astakhov Attractor.
/// 三维连续混沌系统，Euler 单步迭代。/ 3D continuous chaotic system, one Euler step.
/// </summary>
public sealed record AnishchenkoAstakhovAttractor<V>(V Mu, V Eta, V H)
    : IExtractor<Point<Dim3, V>, Point<Dim3, V>>
    where V : struct, IFloatingNumber<V> {
    public Point<Dim3, V> Invoke(Point<Dim3, V> p) {
        V x = p[0]; V y = p[1]; V z = p[2];
        V one = x.Constants.One;
        V i = ((Flt64)(object)x).Value >= 0.0 ? one : x.Constants.Zero;
        V dx = Mu.Times(x).Plus(y).Minus(x.Times(z));
        V dy = x.Negate();
        V dz = Eta.Negate().Times(z).Plus(Eta.Times(i).Times(x.Sqr()));
        return new Point<Dim3, V>(new V[] {
            x.Plus(H.Times(dx)),
            y.Plus(H.Times(dy)),
            z.Plus(H.Times(dz))
        }, Dim3.Instance);
    }

    public static AnishchenkoAstakhovAttractor<Flt64> Create(
        Flt64? mu = null, Flt64? eta = null, Flt64? h = null)
        => new(mu ?? new Flt64(1.2), eta ?? new Flt64(0.5), h ?? new Flt64(0.01));
}

/// <summary>
/// Anishchenko-Astakhov 吸引子生成器 / Anishchenko-Astakhov Attractor Generator.
/// </summary>
public sealed class AnishchenkoAstakhovAttractorGenerator : IGenerator<Point<Dim3, Flt64>> {
    public AnishchenkoAstakhovAttractor<Flt64> System { get; }
    public Point<Dim3, Flt64> X { get; private set; }

    public AnishchenkoAstakhovAttractorGenerator(AnishchenkoAstakhovAttractor<Flt64>? system = null, Point<Dim3, Flt64>? x = null) {
        System = system ?? AnishchenkoAstakhovAttractor<Flt64>.Create();
        X = x ?? PointFactory.point3(new Flt64(0.1), new Flt64(0.1), new Flt64(0.1));
    }

    public static AnishchenkoAstakhovAttractorGenerator Create(
        Flt64 mu, Flt64 eta, Flt64 h, Point<Dim3, Flt64>? x = null)
        => new(AnishchenkoAstakhovAttractor<Flt64>.Create(mu, eta, h), x);

    public Point<Dim3, Flt64> Invoke() {
        Point<Dim3, Flt64> cur = X;
        X = System.Invoke(X);
        return cur;
    }
}
