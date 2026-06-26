#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Geometry;
using System;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// Martin 迭代 / Martin Iterate.
/// 二维混沌映射。/ 2D chaotic map.
/// </summary>
public sealed record MartinIterate<V>(V A, V B, V C) : IExtractor<Point<Dim2, V>, Point<Dim2, V>>
    where V : struct, IFloatingNumber<V> {
    public Point<Dim2, V> Invoke(Point<Dim2, V> p) {
        V x = p[0]; V y = p[1];
        V epsilon = x.Constants.PositiveMinimum;
        double bx = ((Flt64)(object)B.Times(x)).Value;
        double c = ((Flt64)(object)C).Value;
        double absVal = global::System.Math.Abs(bx - c);
        V temp = (V)(object)new Flt64(global::System.Math.Sqrt(absVal));
        V g;
        if (x.Gr(epsilon)) {
            g = y.Minus(temp);
        }
        else if (x.Ls(epsilon.Negate())) {
            g = y.Plus(temp);
        }
        else {
            g = y;
        }
        return new Point<Dim2, V>(new V[] { g, A.Minus(x) }, Dim2.Instance);
    }

    public static MartinIterate<Flt64> Create(Flt64? a = null, Flt64? b = null, Flt64? c = null)
        => new(a ?? new Flt64(68.0), b ?? new Flt64(75.0), c ?? new Flt64(83.0));
}

public sealed class MartinIterateGenerator : IGenerator<Point<Dim2, Flt64>> {
    public MartinIterate<Flt64> Map { get; }
    public Point<Dim2, Flt64> X { get; private set; }

    public MartinIterateGenerator(MartinIterate<Flt64>? map = null, Point<Dim2, Flt64>? x = null) {
        Map = map ?? MartinIterate<Flt64>.Create();
        X = x ?? PointFactory.point2(new Flt64(0.1), new Flt64(0.1));
    }

    public Point<Dim2, Flt64> Invoke() {
        Point<Dim2, Flt64> cur = X;
        X = Map.Invoke(X);
        return cur;
    }
}
