#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Geometry;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// Rabinovich-Fabrikant 方程 / Rabinovich-Fabrikant Equation.
/// 三维连续混沌系统，Euler 单步迭代。/ 3D continuous chaotic system, one Euler step.
/// </summary>
public sealed record RabinovichFabrikantEquation<V>(V A, V B, V H)
    : IExtractor<Point<Dim3, V>, Point<Dim3, V>>
    where V : struct, IFloatingNumber<V> {
    public Point<Dim3, V> Invoke(Point<Dim3, V> p) {
        V x = p[0]; V y = p[1]; V z = p[2];
        V one = x.Constants.One;
        V two = ((IHasTwo<V>)x.Constants).Two;
        V three = two.Plus(one);
        V dx = y.Times(z.Minus(one).Plus(x.Times(x))).Plus(B.Times(x));
        V dy = x.Times(three.Times(z).Plus(one).Minus(x.Times(x))).Plus(B.Times(y));
        V dz = two.Negate().Times(z).Times(A.Plus(x.Times(y)));
        return new Point<Dim3, V>(new V[] {
            x.Plus(H.Times(dx)),
            y.Plus(H.Times(dy)),
            z.Plus(H.Times(dz))
        }, Dim3.Instance);
    }

    public static RabinovichFabrikantEquation<Flt64> Create(Flt64? a = null, Flt64? b = null, Flt64? h = null)
        => new(a ?? new Flt64(1.1), b ?? new Flt64(0.9), h ?? new Flt64(0.01));
}

public sealed class RabinovichFabrikantEquationGenerator : IGenerator<Point<Dim3, Flt64>> {
    public RabinovichFabrikantEquation<Flt64> System { get; }
    public Point<Dim3, Flt64> X { get; private set; }

    public RabinovichFabrikantEquationGenerator(RabinovichFabrikantEquation<Flt64>? system = null, Point<Dim3, Flt64>? x = null) {
        System = system ?? RabinovichFabrikantEquation<Flt64>.Create();
        X = x ?? PointFactory.point3(new Flt64(0.1), new Flt64(0.1), new Flt64(0.1));
    }

    public Point<Dim3, Flt64> Invoke() {
        Point<Dim3, Flt64> cur = X;
        X = System.Invoke(X);
        return cur;
    }
}
