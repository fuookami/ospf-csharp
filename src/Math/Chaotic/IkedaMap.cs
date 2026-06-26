#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Geometry;
using System;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// 池田映射 / Ikeda Map.
/// 二维混沌映射，含三角函数。/ 2D chaotic map with trigonometric functions.
/// </summary>
public sealed record IkedaMap<V>(V U, V T0, V T1) : IExtractor<Point<Dim2, V>, Point<Dim2, V>>
    where V : struct, IFloatingNumber<V> {
    public Point<Dim2, V> Invoke(Point<Dim2, V> p) {
        V x = p[0]; V y = p[1];
        V one = x.Constants.One;
        double xDbl = ((Flt64)(object)x).Value;
        double yDbl = ((Flt64)(object)y).Value;
        double t0 = ((Flt64)(object)T0).Value;
        double t1 = ((Flt64)(object)T1).Value;
        double u = ((Flt64)(object)U).Value;
        double t = t0 - t1 / (1.0 + xDbl * xDbl + yDbl * yDbl);
        double sinT = global::System.Math.Sin(t);
        double cosT = global::System.Math.Cos(t);
        return new Point<Dim2, V>(new V[] {
            (V)(object)new Flt64(1.0 + u * (xDbl * cosT - yDbl * sinT)),
            (V)(object)new Flt64(u * (xDbl * sinT + yDbl * cosT))
        }, Dim2.Instance);
    }

    public static IkedaMap<Flt64> Create(Flt64? u = null)
        => new(u ?? new Flt64(0.918), new Flt64(0.4), new Flt64(6.0));
}

public sealed class IkedaMapGenerator : IGenerator<Point<Dim2, Flt64>> {
    public IkedaMap<Flt64> Map { get; }
    public Point<Dim2, Flt64> X { get; private set; }

    public IkedaMapGenerator(IkedaMap<Flt64>? map = null, Point<Dim2, Flt64>? x = null) {
        Map = map ?? IkedaMap<Flt64>.Create();
        X = x ?? PointFactory.point2(new Flt64(0.1), new Flt64(0.1));
    }

    public Point<Dim2, Flt64> Invoke() {
        Point<Dim2, Flt64> cur = X;
        X = Map.Invoke(X);
        return cur;
    }
}
