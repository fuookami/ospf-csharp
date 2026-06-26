#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Geometry;
using System;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// Zaslavskii 映射 / Zaslavskii Map.
/// 二维混沌映射，含三角和指数函数。/ 2D chaotic map with trigonometric and exponential functions.
/// </summary>
public sealed record ZaslavskiiMap<V>(V Epsilon, V Upsilon, V R, V Mu, V TwoPi)
    : IExtractor<Point<Dim2, V>, Point<Dim2, V>>
    where V : struct, IFloatingNumber<V> {
    public Point<Dim2, V> Invoke(Point<Dim2, V> p) {
        V x = p[0]; V y = p[1];
        double xDbl = ((Flt64)(object)x).Value;
        double yDbl = ((Flt64)(object)y).Value;
        double epsDbl = ((Flt64)(object)Epsilon).Value;
        double upsDbl = ((Flt64)(object)Upsilon).Value;
        double rDbl = ((Flt64)(object)R).Value;
        double muDbl = ((Flt64)(object)Mu).Value;
        double twoPiDbl = ((Flt64)(object)TwoPi).Value;
        double cosVal = global::System.Math.Cos(twoPiDbl * xDbl);
        double expR = global::System.Math.Exp(-rDbl);
        double newXdbl = xDbl + upsDbl * (1.0 + muDbl * yDbl) + epsDbl * upsDbl * muDbl * cosVal;
        double newYdbl = expR * (yDbl + epsDbl * cosVal);
        return new Point<Dim2, V>(new V[] {
            (V)(object)new Flt64(newXdbl % 1.0),
            (V)(object)new Flt64(newYdbl)
        }, Dim2.Instance);
    }

    public static ZaslavskiiMap<Flt64> Create(Flt64? epsilon = null, Flt64? upsilon = null, Flt64? r = null) {
        Flt64 rVal = r ?? new Flt64(2.0);
        Flt64 mu = (Flt64.One - new Flt64(global::System.Math.Exp(-rVal.Value))) / rVal;
        return new ZaslavskiiMap<Flt64>(
            epsilon ?? new Flt64(5.0), upsilon ?? new Flt64(0.2), rVal, mu,
            new Flt64(2.0 * global::System.Math.PI));
    }
}

public sealed class ZaslavskiiMapGenerator : IGenerator<Point<Dim2, Flt64>> {
    public ZaslavskiiMap<Flt64> Map { get; }
    public Point<Dim2, Flt64> X { get; private set; }

    public ZaslavskiiMapGenerator(ZaslavskiiMap<Flt64>? map = null, Point<Dim2, Flt64>? x = null) {
        Map = map ?? ZaslavskiiMap<Flt64>.Create();
        X = x ?? PointFactory.point2(new Flt64(0.1), new Flt64(0.1));
    }

    public Point<Dim2, Flt64> Invoke() {
        Point<Dim2, Flt64> cur = X;
        X = Map.Invoke(X);
        return cur;
    }
}
