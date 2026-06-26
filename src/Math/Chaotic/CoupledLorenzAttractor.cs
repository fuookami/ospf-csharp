#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Geometry;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// 耦合洛伦兹吸引子 / Coupled Lorenz Attractor.
/// 双三维 Lorenz 子系统耦合，Euler 单步迭代。/ Two coupled 3D Lorenz subsystems, one Euler step.
/// </summary>
public sealed record CoupledLorenzAttractor<V>(V Beta, V Gamma1, V Gamma2, V Epsilon, V Omicron, V H)
    where V : struct, IFloatingNumber<V> {
    public (Point<Dim3, V>, Point<Dim3, V>) Invoke(Point<Dim3, V> x1, Point<Dim3, V> x2) {
        V dx1 = Omicron.Times(x1[1].Minus(x1[0]));
        V dy1 = Gamma1.Times(x1[0]).Minus(x1[1]).Minus(x1[0].Times(x1[2]));
        V dz1 = Beta.Times(x1[2]).Plus(x1[0].Times(x1[1]));

        V dx2 = Omicron.Times(x2[1].Minus(x2[0])).Plus(Epsilon.Times(x1[0].Minus(x2[0])));
        V dy2 = Gamma2.Times(x2[0]).Minus(x2[1]).Minus(x2[0].Times(x2[2]));
        V dz2 = Beta.Negate().Times(x2[2]).Plus(x2[0].Times(x2[1]));

        var newX1 = new Point<Dim3, V>(new V[] {
            x1[0].Plus(H.Times(dx1)),
            x1[1].Plus(H.Times(dy1)),
            x1[2].Plus(H.Times(dz1))
        }, Dim3.Instance);
        var newX2 = new Point<Dim3, V>(new V[] {
            x2[0].Plus(H.Times(dx2)),
            x2[1].Plus(H.Times(dy2)),
            x2[2].Plus(H.Times(dz2))
        }, Dim3.Instance);
        return (newX1, newX2);
    }

    public static CoupledLorenzAttractor<Flt64> Create(
        Flt64? beta = null, Flt64? gamma1 = null, Flt64? gamma2 = null,
        Flt64? epsilon = null, Flt64? omicron = null, Flt64? h = null)
        => new(beta ?? new Flt64(8.0 / 3.0), gamma1 ?? new Flt64(35.0), gamma2 ?? new Flt64(1.15),
               epsilon ?? new Flt64(2.85), omicron ?? new Flt64(2.85), h ?? new Flt64(0.01));
}

/// <summary>
/// 耦合洛伦兹吸引子生成器 / Coupled Lorenz Attractor Generator.
/// </summary>
public sealed class CoupledLorenzAttractorGenerator : IGenerator<(Point<Dim3, Flt64>, Point<Dim3, Flt64>)> {
    public CoupledLorenzAttractor<Flt64> System { get; }
    public Point<Dim3, Flt64> X1 { get; private set; }
    public Point<Dim3, Flt64> X2 { get; private set; }

    public CoupledLorenzAttractorGenerator(
        CoupledLorenzAttractor<Flt64>? system = null,
        Point<Dim3, Flt64>? x1 = null,
        Point<Dim3, Flt64>? x2 = null) {
        System = system ?? CoupledLorenzAttractor<Flt64>.Create();
        X1 = x1 ?? PointFactory.point3(new Flt64(0.1), new Flt64(0.1), new Flt64(0.1));
        X2 = x2 ?? PointFactory.point3(new Flt64(0.2), new Flt64(0.2), new Flt64(0.2));
    }

    public static CoupledLorenzAttractorGenerator Create(
        Flt64 beta, Flt64 gamma1, Flt64 gamma2, Flt64 epsilon, Flt64 omicron, Flt64 h,
        Point<Dim3, Flt64>? x1 = null, Point<Dim3, Flt64>? x2 = null)
        => new(CoupledLorenzAttractor<Flt64>.Create(beta, gamma1, gamma2, epsilon, omicron, h), x1, x2);

    public (Point<Dim3, Flt64>, Point<Dim3, Flt64>) Invoke() {
        (Point<Dim3, Flt64>, Point<Dim3, Flt64>) cur = (X1, X2);
        (Point<Dim3, Flt64>, Point<Dim3, Flt64>) next = System.Invoke(X1, X2);
        X1 = next.Item1;
        X2 = next.Item2;
        return cur;
    }
}
