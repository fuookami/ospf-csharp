#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Geometry;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// Lu-Chen 系统 / Lu-Chen System.
/// 三维连续混沌系统，Euler 单步迭代。/ 3D continuous chaotic system, one Euler step.
/// </summary>
public sealed record LuChenSystem<V>(V A, V B, V C, V D, V H)
    : IExtractor<Point<Dim3, V>, Point<Dim3, V>>
    where V : struct, IFloatingNumber<V> {
    public Point<Dim3, V> Invoke(Point<Dim3, V> p) {
        V x = p[0]; V y = p[1]; V z = p[2];
        V dx = A.Times(y.Minus(x));
        V dy = x.Minus(x.Times(z)).Plus(C.Times(y)).Plus(D);
        V dz = x.Times(y).Minus(B.Times(z));
        return new Point<Dim3, V>(new V[] {
            x.Plus(H.Times(dx)),
            y.Plus(H.Times(dy)),
            z.Plus(H.Times(dz))
        }, Dim3.Instance);
    }

    public static LuChenSystem<Flt64> Create(
        Flt64? a = null, Flt64? b = null, Flt64? c = null, Flt64? d = null, Flt64? h = null)
        => new(a ?? new Flt64(36.0), b ?? new Flt64(20.0), c ?? new Flt64(3.0), d ?? new Flt64(1.0), h ?? new Flt64(0.01));
}

public sealed class LuChenSystemGenerator : IGenerator<Point<Dim3, Flt64>> {
    public LuChenSystem<Flt64> System { get; }
    public Point<Dim3, Flt64> X { get; private set; }

    public LuChenSystemGenerator(LuChenSystem<Flt64>? system = null, Point<Dim3, Flt64>? x = null) {
        System = system ?? LuChenSystem<Flt64>.Create();
        X = x ?? PointFactory.point3(new Flt64(0.1), new Flt64(0.1), new Flt64(0.1));
    }

    public Point<Dim3, Flt64> Invoke() {
        Point<Dim3, Flt64> cur = X;
        X = System.Invoke(X);
        return cur;
    }
}
