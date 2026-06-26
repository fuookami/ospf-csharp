#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Geometry;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// 布鲁塞尔振子 / Brusselator.
/// 二维连续混沌系统，Euler 单步迭代。/ 2D continuous chaotic system, one Euler step.
/// </summary>
public sealed record Brusselator<V>(V A, V B, V H)
    : IExtractor<Point<Dim2, V>, Point<Dim2, V>>
    where V : struct, IFloatingNumber<V> {
    public Point<Dim2, V> Invoke(Point<Dim2, V> p) {
        V x = p[0]; V y = p[1];
        V one = x.Constants.One;
        V x2y = A.Times(x.Sqr()).Times(y);
        V dx = x2y.Minus(B.Times(x)).Minus(x).Plus(one);
        V dy = B.Times(x).Minus(x2y);
        return new Point<Dim2, V>(new V[] {
            x.Plus(H.Times(dx)),
            y.Plus(H.Times(dy))
        }, Dim2.Instance);
    }

    public static Brusselator<Flt64> Create(
        Flt64? a = null, Flt64? b = null, Flt64? h = null)
        => new(a ?? new Flt64(1.0), b ?? new Flt64(3.0), h ?? new Flt64(0.01));
}

/// <summary>
/// 布鲁塞尔振子生成器 / Brusselator Generator.
/// </summary>
public sealed class BrusselatorGenerator : IGenerator<Point<Dim2, Flt64>> {
    public Brusselator<Flt64> System { get; }
    public Point<Dim2, Flt64> X { get; private set; }

    public BrusselatorGenerator(Brusselator<Flt64>? system = null, Point<Dim2, Flt64>? x = null) {
        System = system ?? Brusselator<Flt64>.Create();
        X = x ?? PointFactory.point2(new Flt64(0.1), new Flt64(0.1));
    }

    public static BrusselatorGenerator Create(
        Flt64 a, Flt64 b, Flt64 h, Point<Dim2, Flt64>? x = null)
        => new(Brusselator<Flt64>.Create(a, b, h), x);

    public Point<Dim2, Flt64> Invoke() {
        Point<Dim2, Flt64> cur = X;
        X = System.Invoke(X);
        return cur;
    }
}
