#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Geometry;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// 三涡卷统一混沌系统 TSUCS1 / Three-Scroll Unified Chaotic System TSUCS1.
/// 三维连续混沌系统，Euler 单步迭代。/ 3D continuous chaotic system, one Euler step.
/// </summary>
public sealed record ThreeScrollUnifiedChaoticSystemTsucs1Attractor<V>(
    V Alpha, V Beta, V Delta, V Epsilon, V Zeta, V Rho, V H)
    : IExtractor<Point<Dim3, V>, Point<Dim3, V>>
    where V : struct, IFloatingNumber<V> {
    public Point<Dim3, V> Invoke(Point<Dim3, V> p) {
        V x = p[0]; V y = p[1]; V z = p[2];
        V dx = Alpha.Times(y.Minus(x)).Plus(Delta.Times(x).Times(z));
        V dy = Rho.Times(x).Minus(x.Times(z)).Plus(Zeta.Times(y));
        V dz = Beta.Times(z).Plus(x.Times(y)).Minus(Epsilon.Times(x).Times(x));
        return new Point<Dim3, V>(new V[] {
            x.Plus(H.Times(dx)),
            y.Plus(H.Times(dy)),
            z.Plus(H.Times(dz))
        }, Dim3.Instance);
    }

    public static ThreeScrollUnifiedChaoticSystemTsucs1Attractor<Flt64> Create(
        Flt64? alpha = null, Flt64? beta = null, Flt64? delta = null, Flt64? epsilon = null,
        Flt64? zeta = null, Flt64? rho = null, Flt64? h = null)
        => new(alpha ?? new Flt64(40.0), beta ?? new Flt64(0.833), delta ?? new Flt64(0.5),
            epsilon ?? new Flt64(0.65), zeta ?? new Flt64(20.0), rho ?? new Flt64(55.0), h ?? new Flt64(0.001));
}

public sealed class ThreeScrollUnifiedChaoticSystemTsucs1AttractorGenerator : IGenerator<Point<Dim3, Flt64>> {
    public ThreeScrollUnifiedChaoticSystemTsucs1Attractor<Flt64> System { get; }
    public Point<Dim3, Flt64> X { get; private set; }

    public ThreeScrollUnifiedChaoticSystemTsucs1AttractorGenerator(
        ThreeScrollUnifiedChaoticSystemTsucs1Attractor<Flt64>? system = null, Point<Dim3, Flt64>? x = null) {
        System = system ?? ThreeScrollUnifiedChaoticSystemTsucs1Attractor<Flt64>.Create();
        X = x ?? PointFactory.point3(new Flt64(0.1), new Flt64(0.1), new Flt64(0.1));
    }

    public Point<Dim3, Flt64> Invoke() {
        Point<Dim3, Flt64> cur = X;
        X = System.Invoke(X);
        return cur;
    }
}

/// <summary>
/// 三涡卷统一混沌系统 TSUCS2 / Three-Scroll Unified Chaotic System TSUCS2.
/// 三维连续混沌系统，Euler 单步迭代。/ 3D continuous chaotic system, one Euler step.
/// </summary>
public sealed record ThreeScrollUnifiedChaoticSystemTsucs2Attractor<V>(
    V Alpha, V Beta, V Delta, V Zeta, V Rho, V H)
    : IExtractor<Point<Dim3, V>, Point<Dim3, V>>
    where V : struct, IFloatingNumber<V> {
    public Point<Dim3, V> Invoke(Point<Dim3, V> p) {
        V x = p[0]; V y = p[1]; V z = p[2];
        V one = x.Constants.One;
        V two = ((IHasTwo<V>)x.Constants).Two;
        V three = two.Plus(one);
        V dx = Alpha.Times(y.Minus(x)).Plus(Delta.Times(x).Times(z));
        V dy = Rho.Times(x).Minus(x.Times(z)).Plus(Zeta.Times(y));
        V dz = Beta.Times(z).Plus(x.Times(y).Div(three));
        return new Point<Dim3, V>(new V[] {
            x.Plus(H.Times(dx)),
            y.Plus(H.Times(dy)),
            z.Plus(H.Times(dz))
        }, Dim3.Instance);
    }

    public static ThreeScrollUnifiedChaoticSystemTsucs2Attractor<Flt64> Create(
        Flt64? alpha = null, Flt64? beta = null, Flt64? delta = null,
        Flt64? zeta = null, Flt64? rho = null, Flt64? h = null)
        => new(alpha ?? new Flt64(40.0), beta ?? new Flt64(0.833), delta ?? new Flt64(0.5),
            zeta ?? new Flt64(20.0), rho ?? new Flt64(55.0), h ?? new Flt64(0.001));
}

public sealed class ThreeScrollUnifiedChaoticSystemTsucs2AttractorGenerator : IGenerator<Point<Dim3, Flt64>> {
    public ThreeScrollUnifiedChaoticSystemTsucs2Attractor<Flt64> System { get; }
    public Point<Dim3, Flt64> X { get; private set; }

    public ThreeScrollUnifiedChaoticSystemTsucs2AttractorGenerator(
        ThreeScrollUnifiedChaoticSystemTsucs2Attractor<Flt64>? system = null, Point<Dim3, Flt64>? x = null) {
        System = system ?? ThreeScrollUnifiedChaoticSystemTsucs2Attractor<Flt64>.Create();
        X = x ?? PointFactory.point3(new Flt64(0.1), new Flt64(0.1), new Flt64(0.1));
    }

    public Point<Dim3, Flt64> Invoke() {
        Point<Dim3, Flt64> cur = X;
        X = System.Invoke(X);
        return cur;
    }
}
