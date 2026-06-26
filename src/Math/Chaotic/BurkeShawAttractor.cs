#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Geometry;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// Burke-Shaw 吸引子 / Burke-Shaw Attractor.
/// 三维连续混沌系统，Euler 单步迭代。/ 3D continuous chaotic system, one Euler step.
/// </summary>
public sealed record BurkeShawAttractor<V>(V Zeta, V Nu, V H)
    : IExtractor<Point<Dim3, V>, Point<Dim3, V>>
    where V : struct, IFloatingNumber<V> {
    public Point<Dim3, V> Invoke(Point<Dim3, V> p) {
        V x = p[0]; V y = p[1]; V z = p[2];
        V dx = Zeta.Negate().Times(x.Plus(y));
        V dy = y.Negate().Minus(Zeta.Times(x).Times(z));
        V dz = Zeta.Times(x).Times(y).Plus(Nu);
        return new Point<Dim3, V>(new V[] {
            x.Plus(H.Times(dx)),
            y.Plus(H.Times(dy)),
            z.Plus(H.Times(dz))
        }, Dim3.Instance);
    }

    public static BurkeShawAttractor<Flt64> Create(
        Flt64? zeta = null, Flt64? nu = null, Flt64? h = null)
        => new(zeta ?? new Flt64(10.0), nu ?? new Flt64(4.272), h ?? new Flt64(0.01));
}

/// <summary>
/// Burke-Shaw 吸引子生成器 / Burke-Shaw Attractor Generator.
/// </summary>
public sealed class BurkeShawAttractorGenerator : IGenerator<Point<Dim3, Flt64>> {
    public BurkeShawAttractor<Flt64> System { get; }
    public Point<Dim3, Flt64> X { get; private set; }

    public BurkeShawAttractorGenerator(BurkeShawAttractor<Flt64>? system = null, Point<Dim3, Flt64>? x = null) {
        System = system ?? BurkeShawAttractor<Flt64>.Create();
        X = x ?? PointFactory.point3(new Flt64(0.1), new Flt64(0.1), new Flt64(0.1));
    }

    public static BurkeShawAttractorGenerator Create(
        Flt64 zeta, Flt64 nu, Flt64 h, Point<Dim3, Flt64>? x = null)
        => new(BurkeShawAttractor<Flt64>.Create(zeta, nu, h), x);

    public Point<Dim3, Flt64> Invoke() {
        Point<Dim3, Flt64> cur = X;
        X = System.Invoke(X);
        return cur;
    }
}
