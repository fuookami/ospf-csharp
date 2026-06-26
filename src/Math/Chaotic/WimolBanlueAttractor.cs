#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Geometry;
using System;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// Wimol-Banlue 吸引子 / Wimol-Banlue Attractor.
/// 三维连续混沌系统，含三角函数。/ 3D continuous chaotic system with trigonometric functions.
/// </summary>
public sealed record WimolBanlueAttractor<V>(V Alpha, V H)
    : IExtractor<Point<Dim3, V>, Point<Dim3, V>>
    where V : struct, IFloatingNumber<V> {
    public Point<Dim3, V> Invoke(Point<Dim3, V> p) {
        V x = p[0]; V y = p[1]; V z = p[2];
        double tanVal = global::System.Math.Tan(((Flt64)(object)x).Value);
        V dx = y.Minus(x);
        V dy = z.Negate().Times((V)(object)new Flt64(tanVal));
        V dz = Alpha.Negate().Plus(x.Times(y)).Plus(x.Abs());
        return new Point<Dim3, V>(new V[] {
            x.Plus(H.Times(dx)),
            y.Plus(H.Times(dy)),
            z.Plus(H.Times(dz))
        }, Dim3.Instance);
    }

    public static WimolBanlueAttractor<Flt64> Create(Flt64? alpha = null, Flt64? h = null)
        => new(alpha ?? new Flt64(2.0), h ?? new Flt64(0.01));
}

public sealed class WimolBanlueAttractorGenerator : IGenerator<Point<Dim3, Flt64>> {
    public WimolBanlueAttractor<Flt64> System { get; }
    public Point<Dim3, Flt64> X { get; private set; }

    public WimolBanlueAttractorGenerator(WimolBanlueAttractor<Flt64>? system = null, Point<Dim3, Flt64>? x = null) {
        System = system ?? WimolBanlueAttractor<Flt64>.Create();
        X = x ?? PointFactory.point3(new Flt64(0.1), new Flt64(0.1), new Flt64(0.1));
    }

    public Point<Dim3, Flt64> Invoke() {
        Point<Dim3, Flt64> cur = X;
        X = System.Invoke(X);
        return cur;
    }
}
