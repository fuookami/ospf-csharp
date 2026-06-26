#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Geometry;
using System;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// 托马斯吸引子 / Thomas Attractor.
/// 三维连续混沌系统，含三角函数。/ 3D continuous chaotic system with trigonometric functions.
/// </summary>
public sealed record ThomasAttractor<V>(V Beta, V H) : IExtractor<Point<Dim3, V>, Point<Dim3, V>>
    where V : struct, IFloatingNumber<V> {
    public Point<Dim3, V> Invoke(Point<Dim3, V> p) {
        V x = p[0]; V y = p[1]; V z = p[2];
        double sinX = global::System.Math.Sin(((Flt64)(object)x).Value);
        double sinY = global::System.Math.Sin(((Flt64)(object)y).Value);
        double sinZ = global::System.Math.Sin(((Flt64)(object)z).Value);
        double beta = ((Flt64)(object)Beta).Value;
        V dx = ((V)(object)new Flt64(sinX)).Minus(Beta.Times(x));
        V dy = ((V)(object)new Flt64(sinY)).Minus(Beta.Times(y));
        V dz = ((V)(object)new Flt64(sinZ)).Minus(Beta.Times(z));
        return new Point<Dim3, V>(new V[] {
            x.Plus(H.Times(dx)),
            y.Plus(H.Times(dy)),
            z.Plus(H.Times(dz))
        }, Dim3.Instance);
    }

    public static ThomasAttractor<Flt64> Create(Flt64? beta = null, Flt64? h = null)
        => new(beta ?? new Flt64(0.19), h ?? new Flt64(0.01));
}

public sealed class ThomasAttractorGenerator : IGenerator<Point<Dim3, Flt64>> {
    public ThomasAttractor<Flt64> System { get; }
    public Point<Dim3, Flt64> X { get; private set; }

    public ThomasAttractorGenerator(ThomasAttractor<Flt64>? system = null, Point<Dim3, Flt64>? x = null) {
        System = system ?? ThomasAttractor<Flt64>.Create();
        X = x ?? PointFactory.point3(new Flt64(0.1), new Flt64(0.1), new Flt64(0.1));
    }

    public Point<Dim3, Flt64> Invoke() {
        Point<Dim3, Flt64> cur = X;
        X = System.Invoke(X);
        return cur;
    }
}
