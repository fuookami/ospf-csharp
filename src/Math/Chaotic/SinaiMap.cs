#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Geometry;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// Sinai 映射（二维，环面上的加法 + 余弦扰动）/ The Sinai map on the torus.
/// </summary>
/// <remarks>
/// 对齐。
///
/// <c>x' = (x + y + r·cos(2πx)) mod 1</c>，<c>y' = (y + r·cos(2πx)) mod 1</c>。
///
/// **两处容易写错**：
/// ① **`mod 1` 是取小数部分，不是取绝对值**——**负数坐标（环面上的左侧）取绝对值会折返到右边，破坏环面拓扑**；
/// ② **两个方程都用同一个 `cos(2πx)`**，**而不是第二个用 `cos(2πy)`。**
///
/// The Sinai map.
///
/// <c>x' = (x + y + r·cos(2πx)) mod 1</c>, <c>y' = (y + r·cos(2πx)) mod 1</c>.
///
/// **Two things go wrong here**.
/// ① **`mod 1` is the fractional part, not the absolute value** — **taking an absolute value on a negative
/// coordinate folds the torus's left side onto its right and breaks the topology**;
/// ② **both equations use the same `cos(2πx)`**, **not `cos(2πy)` in the second.**
/// </remarks>
public sealed record SinaiMap<V>(V R, V TwoPi) : IExtractor<Point<Dim2, V>, Point<Dim2, V>>
    where V : struct, IFloatingNumber<V> {
    /// <inheritdoc/>
    public Point<Dim2, V> Invoke(Point<Dim2, V> p) {
        double x = ((Flt64)(object)p[0]).Value;
        double y = ((Flt64)(object)p[1]).Value;
        double r = ((Flt64)(object)R).Value;

        // 余弦只算一次，两个方程共用——写法就是这样，而"第二个用 y"是个很容易犯的对称性错误。
        // The cosine is computed once and shared — that is its form, and "use y in the second" is an easy
        // symmetry mistake to make.
        double cosVal = global::System.Math.Cos(2.0 * global::System.Math.PI * x);

        // mod 1 是取小数部分。用 Floor 而不是 Abs：环面上 -0.2 与 0.8 是同一个点，
        // 而 Abs 会把 -0.2 变成 0.2，那是另一个点。
        //
        // mod 1 is the fractional part. Floor rather than Abs: on the torus -0.2 and 0.8 are the same point, while Abs
        // turns -0.2 into 0.2, a different point.
        double newX = x + y + r * cosVal;
        newX -= global::System.Math.Floor(newX);
        double newY = y + r * cosVal;
        newY -= global::System.Math.Floor(newY);

        return new Point<Dim2, V>(new V[] {
            (V)(object)new Flt64(newX),
            (V)(object)new Flt64(newY),
        }, Dim2.Instance);
    }

    /// <summary>造一个 Sinai 映射 / Create one.</summary>
    public static SinaiMap<Flt64> Create(Flt64? r = null) =>
        new(r ?? new Flt64(0.3), new Flt64(2.0) * new Flt64(global::System.Math.PI));
}

/// <summary>Sinai 映射的迭代器 / Iterator over the Sinai map.</summary>
public sealed class SinaiMapGenerator : IGenerator<Point<Dim2, Flt64>> {
    /// <summary>被迭代的映射 / The map being iterated.</summary>
    public SinaiMap<Flt64> Map { get; }

    /// <summary>当前点 / The current point.</summary>
    public Point<Dim2, Flt64> X { get; private set; }

    /// <summary>造一个迭代器 / Create an iterator.</summary>
    public SinaiMapGenerator(SinaiMap<Flt64>? map = null, Point<Dim2, Flt64>? x = null) {
        Map = map ?? SinaiMap<Flt64>.Create();
        X = x ?? PointFactory.point2(new Flt64(0.1), new Flt64(0.1));
    }

    /// <inheritdoc/>
    public Point<Dim2, Flt64> Invoke() {
        Point<Dim2, Flt64> current = X;
        X = Map.Invoke(X);
        return current;
    }
}
