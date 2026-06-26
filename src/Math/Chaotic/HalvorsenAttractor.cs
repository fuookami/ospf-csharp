#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Geometry;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// Halvorsen 吸引子 / Halvorsen Attractor.
/// 三维连续混沌系统，Euler 单步迭代。/ 3D continuous chaotic system, one Euler step.
/// </summary>
public sealed record HalvorsenAttractor<V>(V Alpha, V H)
    : IExtractor<Point<Dim3, V>, Point<Dim3, V>>
    where V : struct, IFloatingNumber<V> {
    public Point<Dim3, V> Invoke(Point<Dim3, V> p) {
        V x = p[0]; V y = p[1]; V z = p[2];
        V two = ((IHasTwo<V>)x.Constants).Two;
        V four = two.Times(two);
        V dx = Alpha.Negate().Times(x).Minus(four.Times(y)).Minus(four.Times(z)).Minus(y.Times(y));
        V dy = Alpha.Negate().Times(y).Minus(four.Times(z)).Minus(four.Times(x)).Minus(z.Times(z));
        V dz = Alpha.Negate().Times(z).Minus(four.Times(x)).Minus(four.Times(y)).Minus(x.Times(x));
        return new Point<Dim3, V>(new V[] {
            x.Plus(H.Times(dx)),
            y.Plus(H.Times(dy)),
            z.Plus(H.Times(dz))
        }, Dim3.Instance);
    }

    public static HalvorsenAttractor<Flt64> Create(Flt64? alpha = null, Flt64? h = null)
        => new(alpha ?? new Flt64(1.4), h ?? new Flt64(0.01));
}

public sealed class HalvorsenAttractorGenerator : IGenerator<Point<Dim3, Flt64>> {
    public HalvorsenAttractor<Flt64> System { get; }
    public Point<Dim3, Flt64> X { get; private set; }

    public HalvorsenAttractorGenerator(HalvorsenAttractor<Flt64>? system = null, Point<Dim3, Flt64>? x = null) {
        System = system ?? HalvorsenAttractor<Flt64>.Create();
        X = x ?? PointFactory.point3(new Flt64(0.1), new Flt64(0.1), new Flt64(0.1));
    }

    public Point<Dim3, Flt64> Invoke() {
        Point<Dim3, Flt64> cur = X;
        X = System.Invoke(X);
        return cur;
    }
}
