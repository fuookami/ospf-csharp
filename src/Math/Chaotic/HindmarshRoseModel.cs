#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Geometry;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// Hindmarsh-Rose 神经元模型 / Hindmarsh-Rose Neuron Model.
/// 三维连续混沌系统，Euler 单步迭代。/ 3D continuous chaotic system, one Euler step.
/// </summary>
public sealed record HindmarshRoseModel<V>(V A, V B, V C, V D, V S, V R, V Xr, V I, V H)
    : IExtractor<Point<Dim3, V>, Point<Dim3, V>>
    where V : struct, IFloatingNumber<V> {
    public Point<Dim3, V> Invoke(Point<Dim3, V> p) {
        V x = p[0]; V y = p[1]; V z = p[2];
        V phi = A.Negate().Times(x).Times(x).Times(x).Plus(B.Times(x).Times(x));
        V psi = C.Minus(D.Times(x).Times(x));
        V dx = y.Plus(phi).Minus(z).Plus(I);
        V dy = psi.Minus(y);
        V dz = R.Times(S.Times(x.Minus(Xr)).Minus(z));
        return new Point<Dim3, V>(new V[] {
            x.Plus(H.Times(dx)),
            y.Plus(H.Times(dy)),
            z.Plus(H.Times(dz))
        }, Dim3.Instance);
    }

    public static HindmarshRoseModel<Flt64> Create(
        Flt64? a = null, Flt64? b = null, Flt64? c = null, Flt64? d = null,
        Flt64? s = null, Flt64? r = null, Flt64? xr = null, Flt64? i = null, Flt64? h = null)
        => new(a ?? new Flt64(1.0), b ?? new Flt64(1.0), c ?? new Flt64(1.0), d ?? new Flt64(1.0),
            s ?? new Flt64(1.0), r ?? new Flt64(1.0), xr ?? new Flt64(1.0), i ?? new Flt64(1.0), h ?? new Flt64(0.01));
}

public sealed class HindmarshRoseModelGenerator : IGenerator<Point<Dim3, Flt64>> {
    public HindmarshRoseModel<Flt64> System { get; }
    public Point<Dim3, Flt64> X { get; private set; }

    public HindmarshRoseModelGenerator(HindmarshRoseModel<Flt64>? system = null, Point<Dim3, Flt64>? x = null) {
        System = system ?? HindmarshRoseModel<Flt64>.Create();
        X = x ?? PointFactory.point3(new Flt64(0.1), new Flt64(0.1), new Flt64(0.1));
    }

    public Point<Dim3, Flt64> Invoke() {
        Point<Dim3, Flt64> cur = X;
        X = System.Invoke(X);
        return cur;
    }
}
