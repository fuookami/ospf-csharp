#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Geometry;
using System;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// Yu-Wang 吸引子 / Yu-Wang Attractor.
/// 三维连续混沌系统，含指数函数。/ 3D continuous chaotic system with exponential function.
/// </summary>
public sealed record YuWangAttractor<V>(V Alpha, V Beta, V Delta, V Zeta, V H)
    : IExtractor<Point<Dim3, V>, Point<Dim3, V>>
    where V : struct, IFloatingNumber<V> {
    public Point<Dim3, V> Invoke(Point<Dim3, V> p) {
        V x = p[0]; V y = p[1]; V z = p[2];
        double xDbl = ((Flt64)(object)x).Value;
        double yDbl = ((Flt64)(object)y).Value;
        double zDbl = ((Flt64)(object)z).Value;
        double alphaDbl = ((Flt64)(object)Alpha).Value;
        double betaDbl = ((Flt64)(object)Beta).Value;
        double deltaDbl = ((Flt64)(object)Delta).Value;
        double zetaDbl = ((Flt64)(object)Zeta).Value;
        double hDbl = ((Flt64)(object)H).Value;
        double dx = alphaDbl * (yDbl - xDbl);
        double dy = betaDbl * xDbl - zetaDbl * xDbl * zDbl;
        double dz = global::System.Math.Exp(xDbl * yDbl) - deltaDbl * zDbl;
        return new Point<Dim3, V>(new V[] {
            (V)(object)new Flt64(xDbl + hDbl * dx),
            (V)(object)new Flt64(yDbl + hDbl * dy),
            (V)(object)new Flt64(zDbl + hDbl * dz)
        }, Dim3.Instance);
    }

    public static YuWangAttractor<Flt64> Create(
        Flt64? alpha = null, Flt64? beta = null, Flt64? delta = null, Flt64? zeta = null, Flt64? h = null)
        => new(alpha ?? new Flt64(10.0), beta ?? new Flt64(40.0), delta ?? new Flt64(2.5), zeta ?? new Flt64(2.0), h ?? new Flt64(0.01));
}

public sealed class YuWangAttractorGenerator : IGenerator<Point<Dim3, Flt64>> {
    public YuWangAttractor<Flt64> System { get; }
    public Point<Dim3, Flt64> X { get; private set; }

    public YuWangAttractorGenerator(YuWangAttractor<Flt64>? system = null, Point<Dim3, Flt64>? x = null) {
        System = system ?? YuWangAttractor<Flt64>.Create();
        X = x ?? PointFactory.point3(new Flt64(0.1), new Flt64(0.1), new Flt64(0.1));
    }

    public Point<Dim3, Flt64> Invoke() {
        Point<Dim3, Flt64> cur = X;
        X = System.Invoke(X);
        return cur;
    }
}
