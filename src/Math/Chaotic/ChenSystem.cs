#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Geometry;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// 陈氏系统 / Chen System.
/// 三维连续混沌系统。/ 3D continuous chaotic system.
/// </summary>
public sealed record ChenSystem<V>(V A, V B, V C, V H)
    : IExtractor<Point<Dim3, V>, Point<Dim3, V>>
    where V : struct, IFloatingNumber<V> {
    public Point<Dim3, V> Invoke(Point<Dim3, V> x) {
        V dx = A.Times(x[1].Minus(x[0]));
        V dy = C.Minus(A).Times(x[0]).Minus(x[0].Times(x[2])).Plus(C.Times(x[1]));
        V dz = x[0].Times(x[1]).Minus(B.Times(x[2]));
        return new Point<Dim3, V>(new V[] {
            x[0].Plus(H.Times(dx)),
            x[1].Plus(H.Times(dy)),
            x[2].Plus(H.Times(dz))
        }, Dim3.Instance);
    }

    public static ChenSystem<Flt64> Create(
        Flt64? a = null, Flt64? b = null, Flt64? c = null, Flt64? h = null)
        => new(a ?? new Flt64(10.0), b ?? new Flt64(8.0 / 3.0), c ?? new Flt64(137.0 / 5.0), h ?? new Flt64(0.01));
}

public sealed class ChenSystemGenerator : IGenerator<Point<Dim3, Flt64>> {
    public ChenSystem<Flt64> System { get; }
    public Point<Dim3, Flt64> X { get; private set; }

    public ChenSystemGenerator(ChenSystem<Flt64>? system = null, Point<Dim3, Flt64>? x = null) {
        System = system ?? ChenSystem<Flt64>.Create();
        X = x ?? PointFactory.point3(new Flt64(0.1), new Flt64(0.1), new Flt64(0.1));
    }

    public static ChenSystemGenerator Create(
        Flt64 a, Flt64 b, Flt64 c, Flt64 h, Point<Dim3, Flt64>? x = null)
        => new(ChenSystem<Flt64>.Create(a, b, c, h), x);

    public Point<Dim3, Flt64> Invoke() {
        Point<Dim3, Flt64> cur = X;
        X = System.Invoke(X);
        return cur;
    }
}
