#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Geometry;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// Lorenz 84 模型 / Lorenz 84 Model.
/// 三维连续混沌系统，Euler 单步迭代。/ 3D continuous chaotic system, one Euler step.
/// </summary>
public sealed record Lorenz84Model<V>(V A, V B, V F, V G, V H)
    : IExtractor<Point<Dim3, V>, Point<Dim3, V>>
    where V : struct, IFloatingNumber<V> {
    public Point<Dim3, V> Invoke(Point<Dim3, V> p) {
        V x = p[0]; V y = p[1]; V z = p[2];
        V dx = y.Negate().Times(y).Minus(z.Times(z)).Minus(A.Times(x)).Plus(A.Times(F));
        V dy = x.Times(y).Minus(B.Times(x).Times(z)).Minus(y).Plus(G);
        V dz = B.Times(x).Times(y).Plus(x.Times(z)).Minus(z);
        return new Point<Dim3, V>(new V[] {
            x.Plus(H.Times(dx)),
            y.Plus(H.Times(dy)),
            z.Plus(H.Times(dz))
        }, Dim3.Instance);
    }

    public static Lorenz84Model<Flt64> Create(
        Flt64? a = null, Flt64? b = null, Flt64? f = null, Flt64? g = null, Flt64? h = null)
        => new(a ?? new Flt64(0.25), b ?? new Flt64(4.0), f ?? new Flt64(8.0), g ?? new Flt64(1.0), h ?? new Flt64(0.01));
}

public sealed class Lorenz84ModelGenerator : IGenerator<Point<Dim3, Flt64>> {
    public Lorenz84Model<Flt64> System { get; }
    public Point<Dim3, Flt64> X { get; private set; }

    public Lorenz84ModelGenerator(Lorenz84Model<Flt64>? system = null, Point<Dim3, Flt64>? x = null) {
        System = system ?? Lorenz84Model<Flt64>.Create();
        X = x ?? PointFactory.point3(new Flt64(0.1), new Flt64(0.1), new Flt64(0.1));
    }

    public Point<Dim3, Flt64> Invoke() {
        Point<Dim3, Flt64> cur = X;
        X = System.Invoke(X);
        return cur;
    }
}
