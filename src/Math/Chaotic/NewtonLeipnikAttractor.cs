#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Geometry;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// Newton-Leipnik 吸引子 / Newton-Leipnik Attractor.
/// 三维连续混沌系统，Euler 单步迭代。/ 3D continuous chaotic system, one Euler step.
/// </summary>
public sealed record NewtonLeipnikAttractor<V>(V Alpha, V Beta, V H, V C10, V C5, V C04)
    : IExtractor<Point<Dim3, V>, Point<Dim3, V>>
    where V : struct, IFloatingNumber<V> {
    public Point<Dim3, V> Invoke(Point<Dim3, V> p) {
        V x = p[0]; V y = p[1]; V z = p[2];
        V dx = Alpha.Negate().Times(x).Plus(y).Plus(C10.Times(y).Times(z));
        V dy = x.Negate().Minus(C04.Times(y)).Plus(C5.Times(x).Times(z));
        V dz = Beta.Times(z).Minus(C5.Times(x).Times(y));
        return new Point<Dim3, V>(new V[] {
            x.Plus(H.Times(dx)),
            y.Plus(H.Times(dy)),
            z.Plus(H.Times(dz))
        }, Dim3.Instance);
    }

    public static NewtonLeipnikAttractor<Flt64> Create(Flt64? alpha = null, Flt64? beta = null, Flt64? h = null)
        => new(alpha ?? new Flt64(0.4), beta ?? new Flt64(0.175), h ?? new Flt64(0.01),
            new Flt64(10.0), new Flt64(5.0), new Flt64(0.4));
}

public sealed class NewtonLeipnikAttractorGenerator : IGenerator<Point<Dim3, Flt64>> {
    public NewtonLeipnikAttractor<Flt64> System { get; }
    public Point<Dim3, Flt64> X { get; private set; }

    public NewtonLeipnikAttractorGenerator(NewtonLeipnikAttractor<Flt64>? system = null, Point<Dim3, Flt64>? x = null) {
        System = system ?? NewtonLeipnikAttractor<Flt64>.Create();
        X = x ?? PointFactory.point3(new Flt64(0.1), new Flt64(0.1), new Flt64(0.1));
    }

    public Point<Dim3, Flt64> Invoke() {
        Point<Dim3, Flt64> cur = X;
        X = System.Invoke(X);
        return cur;
    }
}
