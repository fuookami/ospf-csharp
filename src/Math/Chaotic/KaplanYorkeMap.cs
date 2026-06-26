#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Geometry;
using System;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// Kaplan-Yorke 映射 / Kaplan-Yorke Map.
/// x_{n+1} = 2*x mod 1, y_{n+1} = a*y + cos(4*pi*x).
/// </summary>
public sealed record KaplanYorkeMap<V>(V A, V FourPi) : IExtractor<Point<Dim2, V>, Point<Dim2, V>>
    where V : struct, IFloatingNumber<V> {
    public Point<Dim2, V> Invoke(Point<Dim2, V> p) {
        V x = p[0]; V y = p[1];
        V one = x.Constants.One;
        V two = ((IHasTwo<V>)x.Constants).Two;
        double twoX = ((Flt64)(object)two.Times(x)).Value;
        double oneVal = ((Flt64)(object)one).Value;
        V newX = (V)(object)new Flt64(twoX % oneVal);
        double cosVal = global::System.Math.Cos(((Flt64)(object)FourPi).Value * ((Flt64)(object)x).Value);
        V newY = A.Times(y).Plus((V)(object)new Flt64(cosVal));
        return new Point<Dim2, V>(new V[] { newX, newY }, Dim2.Instance);
    }

    public static KaplanYorkeMap<Flt64> Create(Flt64? a = null)
        => new(a ?? new Flt64(0.2), new Flt64(4.0 * global::System.Math.PI));
}

public sealed class KaplanYorkeMapGenerator : IGenerator<Point<Dim2, Flt64>> {
    public KaplanYorkeMap<Flt64> Map { get; }
    public Point<Dim2, Flt64> X { get; private set; }

    public KaplanYorkeMapGenerator(KaplanYorkeMap<Flt64>? map = null, Point<Dim2, Flt64>? x = null) {
        Map = map ?? KaplanYorkeMap<Flt64>.Create();
        X = x ?? PointFactory.point2(new Flt64(0.1), new Flt64(0.1));
    }

    public Point<Dim2, Flt64> Invoke() {
        Point<Dim2, Flt64> cur = X;
        X = Map.Invoke(X);
        return cur;
    }
}
