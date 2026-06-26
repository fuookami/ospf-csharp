#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Geometry;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// 混沌电路映射 / Chaotic Circuit Map.
/// 二维离散混沌映射。/ 2D discrete chaotic map.
/// </summary>
public sealed record CircuitChaotic<V>(V A, V B, V C, V D)
    : IExtractor<Point<Dim2, V>, Point<Dim2, V>>
    where V : struct, IFloatingNumber<V> {
    public Point<Dim2, V> Invoke(Point<Dim2, V> x) {
        V newX = A.Times(x[1]).Minus(D.Times(x[1]).Times(x[1]));
        V newY = B.Negate().Times(x[0]).Plus(C.Times(x[1]));
        return new Point<Dim2, V>(new V[] { newX, newY }, Dim2.Instance);
    }

    public static CircuitChaotic<Flt64> Create(
        Flt64? a = null, Flt64? b = null, Flt64? c = null, Flt64? d = null)
        => new(a ?? new Flt64(1.0), b ?? new Flt64(1.0), c ?? new Flt64(1.0), d ?? new Flt64(1.0));
}

/// <summary>
/// 混沌电路映射生成器 / Chaotic Circuit Map Generator.
/// </summary>
public sealed class CircuitChaoticGenerator : IGenerator<Point<Dim2, Flt64>> {
    public CircuitChaotic<Flt64> Map { get; }
    public Point<Dim2, Flt64> X { get; private set; }

    public CircuitChaoticGenerator(CircuitChaotic<Flt64>? map = null, Point<Dim2, Flt64>? x = null) {
        Map = map ?? CircuitChaotic<Flt64>.Create();
        X = x ?? PointFactory.point2(new Flt64(0.1), new Flt64(0.1));
    }

    public static CircuitChaoticGenerator Create(Flt64 a, Flt64 b, Flt64 c, Flt64 d, Point<Dim2, Flt64>? x = null)
        => new(CircuitChaotic<Flt64>.Create(a, b, c, d), x);

    public Point<Dim2, Flt64> Invoke() {
        Point<Dim2, Flt64> cur = X;
        X = Map.Invoke(X);
        return cur;
    }
}
