#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Geometry;
using System;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// 达芬方程 / Duffing Equation.
/// 三维连续混沌系统，Euler 单步迭代。/ 3D continuous chaotic system, one Euler step.
/// </summary>
public sealed record DuffingEquation<V>(
    V Alpha, V Beta, V Gamma, V Delta, V Omega, V H) : IExtractor<Point<Dim3, V>, Point<Dim3, V>>
    where V : struct, IFloatingNumber<V> {
    public Point<Dim3, V> Invoke(Point<Dim3, V> p) {
        V x = p[0]; V y = p[1]; V t = p[2];
        V dx = y;
        double cosVal = global::System.Math.Cos(((Flt64)(object)Omega).Value * ((Flt64)(object)t).Value);
        V dy = Alpha.Negate().Times(x).Minus(Gamma.Times(y)).Minus(Beta.Times(x).Times(x).Times(x)).Plus(Delta.Times((V)(object)new Flt64(cosVal)));
        return new Point<Dim3, V>(new V[] {
            x.Plus(H.Times(dx)),
            y.Plus(H.Times(dy)),
            t.Plus(H)
        }, Dim3.Instance);
    }

    public static DuffingEquation<Flt64> Create(
        Flt64? alpha = null, Flt64? beta = null, Flt64? gamma = null,
        Flt64? delta = null, Flt64? omega = null, Flt64? h = null)
        => new(alpha ?? new Flt64(1.0), beta ?? new Flt64(5.0), gamma ?? new Flt64(0.02),
            delta ?? new Flt64(8.0), omega ?? new Flt64(0.5), h ?? new Flt64(0.01));
}

public sealed class DuffingEquationGenerator : IGenerator<Point<Dim3, Flt64>> {
    public DuffingEquation<Flt64> System { get; }
    public Point<Dim3, Flt64> X { get; private set; }

    public DuffingEquationGenerator(DuffingEquation<Flt64>? system = null, Point<Dim3, Flt64>? x = null) {
        System = system ?? DuffingEquation<Flt64>.Create();
        X = x ?? PointFactory.point3(new Flt64(0.1), new Flt64(0.1), Flt64.Zero);
    }

    public Point<Dim3, Flt64> Invoke() {
        Point<Dim3, Flt64> cur = X;
        X = System.Invoke(X);
        return cur;
    }
}
