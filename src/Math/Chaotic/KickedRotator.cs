#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Geometry;
using System;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// 受击转子 / Kicked Rotator.
/// 二维混沌映射，含三角函数。/ 2D chaotic map with trigonometric functions.
/// </summary>
public sealed record KickedRotator<V>(V K) : IExtractor<Point<Dim2, V>, Point<Dim2, V>>
    where V : struct, IFloatingNumber<V> {
    public Point<Dim2, V> Invoke(Point<Dim2, V> p) {
        V x = p[0]; V y = p[1];
        double sinVal = global::System.Math.Sin(((Flt64)(object)y).Value);
        V newX = x.Plus(K.Times((V)(object)new Flt64(sinVal)));
        return new Point<Dim2, V>(new V[] { newX, y.Plus(newX) }, Dim2.Instance);
    }

    public static KickedRotator<Flt64> Create(Flt64? k = null)
        => new(k ?? new Flt64(0.971635));
}

public sealed class KickedRotatorGenerator : IGenerator<Point<Dim2, Flt64>> {
    public KickedRotator<Flt64> Map { get; }
    public Point<Dim2, Flt64> X { get; private set; }

    public KickedRotatorGenerator(KickedRotator<Flt64>? map = null, Point<Dim2, Flt64>? x = null) {
        Map = map ?? KickedRotator<Flt64>.Create();
        X = x ?? PointFactory.point2(new Flt64(0.1), new Flt64(0.1));
    }

    public Point<Dim2, Flt64> Invoke() {
        Point<Dim2, Flt64> cur = X;
        X = Map.Invoke(X);
        return cur;
    }
}
