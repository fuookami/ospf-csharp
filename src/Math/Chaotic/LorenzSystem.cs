#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Geometry;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// 洛伦兹系统 / Lorenz System.
/// 三维连续混沌系统，Euler 单步迭代。/ 3D continuous chaotic system, one Euler step.
/// </summary>
public sealed record LorenzSystem<V>(
    V A, V B, V C, V H) : IExtractor<Point<Dim3, V>, Point<Dim3, V>>
    where V : struct, IFloatingNumber<V> {
    public Point<Dim3, V> Invoke(Point<Dim3, V> x) {
        V dx = A.Times(x[1].Minus(x[0]));
        V dy = C.Times(x[0]).Minus(x[0].Times(x[2])).Minus(x[1]);
        V dz = x[0].Times(x[1]).Minus(B.Times(x[2]));
        return new Point<Dim3, V>(new V[] {
            x[0].Plus(H.Times(dx)),
            x[1].Plus(H.Times(dy)),
            x[2].Plus(H.Times(dz))
        }, Dim3.Instance);
    }

    public static LorenzSystem<Flt64> Create(
        Flt64? a = null, Flt64? b = null, Flt64? c = null, Flt64? h = null)
        => new(a ?? new Flt64(10.0), b ?? new Flt64(28.0), c ?? new Flt64(8.0 / 3.0), h ?? new Flt64(0.01));
}

/// <summary>
/// 洛伦兹系统生成器 / Lorenz System Generator.
/// </summary>
public sealed class LorenzSystemGenerator : IGenerator<Point<Dim3, Flt64>> {
    public LorenzSystem<Flt64> System { get; }
    public Point<Dim3, Flt64> X { get; private set; }

    public LorenzSystemGenerator(LorenzSystem<Flt64>? system = null, Point<Dim3, Flt64>? x = null) {
        System = system ?? LorenzSystem<Flt64>.Create();
        X = x ?? PointFactory.point3(new Flt64(0.1), new Flt64(0.1), new Flt64(0.1));
    }

    public static LorenzSystemGenerator Create(
        Flt64 a, Flt64 b, Flt64 c, Flt64 h, Point<Dim3, Flt64>? x = null)
        => new(LorenzSystem<Flt64>.Create(a, b, c, h), x);

    public Point<Dim3, Flt64> Invoke() {
        Point<Dim3, Flt64> cur = X;
        X = System.Invoke(X);
        return cur;
    }
}

/// <summary>
/// 洛伦兹吸引子（物理参数命名别名）/ Lorenz Attractor (physics parameter naming alias).
/// </summary>
public sealed record LorenzAttractor<V>(V Sigma, V Rho, V Beta, V H)
    : IExtractor<Point<Dim3, V>, Point<Dim3, V>>
    where V : struct, IFloatingNumber<V> {
    private readonly LorenzSystem<V> _inner = new(Sigma, Beta, Rho, H);

    public Point<Dim3, V> Invoke(Point<Dim3, V> x) => _inner.Invoke(x);

    public static LorenzAttractor<Flt64> Create(
        Flt64? sigma = null, Flt64? rho = null, Flt64? beta = null, Flt64? h = null)
        => new(sigma ?? new Flt64(10.0), rho ?? new Flt64(28.0), beta ?? new Flt64(8.0 / 3.0), h ?? new Flt64(0.01));
}
