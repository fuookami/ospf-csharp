#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Geometry;
using System;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// 蔡氏电路（变体）/ Chua's Circuit (variant).
/// 三维连续混沌系统，含分段线性非线性（注意：绝对值项相加）。/ 3D continuous chaotic system with piecewise-linear nonlinearity (note: abs terms added).
/// </summary>
public sealed record ChuasCircuit<V>(V A, V B, V C, V D, V H)
    : IExtractor<Point<Dim3, V>, Point<Dim3, V>>
    where V : struct, IFloatingNumber<V> {
    public Point<Dim3, V> Invoke(Point<Dim3, V> x) {
        V one = x[0].Constants.One;
        V two = ((IHasTwo<V>)x[0].Constants).Two;
        V half = one.Div(two);
        V f = C.Times(x[0]).Plus(half.Times(D.Minus(C)).Times(x[0].Plus(one).Abs().Plus(x[0].Minus(one).Abs())));
        V dx = A.Times(x[1].Minus(x[0]).Minus(f));
        V dy = x[0].Minus(x[1]).Plus(x[2]);
        V dz = B.Negate().Times(x[1]);
        return new Point<Dim3, V>(new V[] {
            x[0].Plus(H.Times(dx)),
            x[1].Plus(H.Times(dy)),
            x[2].Plus(H.Times(dz))
        }, Dim3.Instance);
    }

    public static ChuasCircuit<Flt64> Create(
        Flt64? a = null, Flt64? b = null, Flt64? c = null, Flt64? d = null, Flt64? h = null)
        => new(a ?? new Flt64(15.6), b ?? new Flt64(28.0), c ?? new Flt64(-1.143), d ?? new Flt64(-0.714), h ?? new Flt64(0.01));
}

/// <summary>
/// 蔡氏电路（变体）生成器 / Chua's Circuit (variant) Generator.
/// </summary>
public sealed class ChuasCircuitGenerator : IGenerator<Point<Dim3, Flt64>> {
    public ChuasCircuit<Flt64> System { get; }
    public Point<Dim3, Flt64> X { get; private set; }

    public ChuasCircuitGenerator(ChuasCircuit<Flt64>? system = null, Point<Dim3, Flt64>? x = null) {
        System = system ?? ChuasCircuit<Flt64>.Create();
        X = x ?? PointFactory.point3(new Flt64(0.1), new Flt64(0.1), new Flt64(0.1));
    }

    public Point<Dim3, Flt64> Invoke() {
        Point<Dim3, Flt64> cur = X;
        X = System.Invoke(X);
        return cur;
    }
}
