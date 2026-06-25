#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Geometry;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// Arnold 猫映射 / Arnold's Cat Map.
/// (2*x + y) mod 1, (x + y) mod 1.
/// </summary>
public sealed record ArnoldsCatMap<V>(V Two) : IExtractor<Point<Dim2, V>, Point<Dim2, V>>
    where V : struct, IFloatingNumber<V> {
    public Point<Dim2, V> Invoke(Point<Dim2, V> x) {
        V one = x[0].Constants.One;
        V newX = Two.Times(x[0]).Plus(x[1]);
        V newY = x[0].Plus(x[1]);
        // mod 1: newX - floor(newX)
        return new Point<Dim2, V>(new V[] {
            ModOne(newX),
            ModOne(newY)
        }, Dim2.Instance);
    }

    private static V ModOne(V val) {
        // val - floor(val) via double conversion
        double dbl = ((Flt64)(object)val).Value;
        return (V)(object)new Flt64(dbl - global::System.Math.Floor(dbl));
    }

    public static ArnoldsCatMap<Flt64> Create()
        => new(new Flt64(2.0));
}

public sealed class ArnoldsCatMapGenerator : IGenerator<Point<Dim2, Flt64>> {
    public ArnoldsCatMap<Flt64> Map { get; }
    public Point<Dim2, Flt64> X { get; private set; }

    public ArnoldsCatMapGenerator(ArnoldsCatMap<Flt64>? map = null, Point<Dim2, Flt64>? x = null) {
        Map = map ?? ArnoldsCatMap<Flt64>.Create();
        X = x ?? PointFactory.point2(new Flt64(0.1), new Flt64(0.1));
    }

    public Point<Dim2, Flt64> Invoke() {
        Point<Dim2, Flt64> cur = X;
        X = Map.Invoke(X);
        return cur;
    }
}
