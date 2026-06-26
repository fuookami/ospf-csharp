#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Geometry;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// 电容方程 / Capacitance Equation.
/// 三维连续混沌系统，含分段线性非线性，Euler 单步迭代。/ 3D continuous chaotic system with piecewise-linear nonlinearity, one Euler step.
/// </summary>
public sealed record CapacitanceEquation<V>(V A, V B, V C, V D, V E, V H)
    : IExtractor<Point<Dim3, V>, Point<Dim3, V>>
    where V : struct, IFloatingNumber<V> {
    public Point<Dim3, V> Invoke(Point<Dim3, V> p) {
        V x = p[0]; V y = p[1]; V z = p[2];
        V one = x.Constants.One;
        double xDbl = ((Flt64)(object)x).Value;
        V g;
        if (xDbl > 1.0) {
            g = E.Times(x).Minus(E.Minus(D));
        }
        else if (xDbl < -1.0) {
            g = E.Times(y).Plus(E.Minus(D));
        }
        else {
            g = D.Times(x);
        }
        V dx = A.Times(C.Minus(one).Times(g).Plus(y));
        V dy = g.Minus(y).Plus(z);
        V dz = B.Negate().Times(y);
        return new Point<Dim3, V>(new V[] {
            x.Plus(H.Times(dx)),
            y.Plus(H.Times(dy)),
            z.Plus(H.Times(dz))
        }, Dim3.Instance);
    }

    public static CapacitanceEquation<Flt64> Create(
        Flt64? a = null, Flt64? b = null, Flt64? c = null,
        Flt64? d = null, Flt64? e = null, Flt64? h = null)
        => new(a ?? new Flt64(0.5), b ?? new Flt64(0.5), c ?? new Flt64(0.5),
            d ?? new Flt64(0.5), e ?? new Flt64(0.5), h ?? new Flt64(0.01));
}

/// <summary>
/// 电容方程生成器 / Capacitance Equation Generator.
/// </summary>
public sealed class CapacitanceEquationGenerator : IGenerator<Point<Dim3, Flt64>> {
    public CapacitanceEquation<Flt64> System { get; }
    public Point<Dim3, Flt64> X { get; private set; }

    public CapacitanceEquationGenerator(CapacitanceEquation<Flt64>? system = null, Point<Dim3, Flt64>? x = null) {
        System = system ?? CapacitanceEquation<Flt64>.Create();
        X = x ?? PointFactory.point3(new Flt64(0.1), new Flt64(0.1), new Flt64(0.1));
    }

    public static CapacitanceEquationGenerator Create(
        Flt64 a, Flt64 b, Flt64 c, Flt64 d, Flt64 e, Flt64 h, Point<Dim3, Flt64>? x = null)
        => new(CapacitanceEquation<Flt64>.Create(a, b, c, d, e, h), x);

    public Point<Dim3, Flt64> Invoke() {
        Point<Dim3, Flt64> cur = X;
        X = System.Invoke(X);
        return cur;
    }
}
