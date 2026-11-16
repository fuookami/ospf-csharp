#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Geometry;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// Windmi 吸引子（三维，七个参数）/ The Windmi attractor.
/// </summary>
/// <remarks>
/// 对齐基线 `WindmiAttractor.kt`。
///
/// <c>dx = a(y−x) + d·xz</c>，<c>dy = bx − xz + fy</c>，<c>dz = cz + xy − e·x²</c>。
///
/// **基线声明了八个参数，而 `g`（默认 0.5）从未出现在任何一条方程里。**
/// **这与 `SprottAttractor` 的 `b` 是同一类：参数存在、签名保留、但无人使用。**
/// **判据会钉住"改 g 不改变轨迹"，以免下一个人把它当成旋钮。**
///
/// The Windmi attractor from the baseline.
///
/// <c>dx = a(y−x) + d·xz</c>, <c>dy = bx − xz + fy</c>, <c>dz = cz + xy − e·x²</c>.
///
/// **The baseline declares eight parameters, and `g` (default 0.5) appears in no equation.**
/// **This is the same kind as `SprottAttractor`'s `b`: present in the signature, used by nothing.**
/// **A criterion pins "changing g does not move the trajectory", so nobody treats it as a dial.**
/// </remarks>
public sealed record WindmiAttractor<V>(V A, V B, V C, V D, V E, V F, V G, V H)
    : IExtractor<Point<Dim3, V>, Point<Dim3, V>>
    where V : struct, IFloatingNumber<V> {
    /// <inheritdoc/>
    public Point<Dim3, V> Invoke(Point<Dim3, V> state) {
        double x = ((Flt64)(object)state[0]).Value;
        double y = ((Flt64)(object)state[1]).Value;
        double z = ((Flt64)(object)state[2]).Value;

        double a = ((Flt64)(object)A).Value;
        double b = ((Flt64)(object)B).Value;
        double c = ((Flt64)(object)C).Value;
        double d = ((Flt64)(object)D).Value;
        double e = ((Flt64)(object)E).Value;
        double f = ((Flt64)(object)F).Value;
        double h = ((Flt64)(object)H).Value;

        double dx = a * (y - x) + d * x * z;
        double dy = b * x - x * z + f * y;
        // e 乘的是 x²（不是 x*z，也不是 x*y）——这一项的形状是它与别的 Lorenz 型系统的区别。
        // e multiplies x squared (not x*z and not x*y) — that shape is what distinguishes it from other Lorenz-like
        // systems.
        double dz = c * z + x * y - e * x * x;

        return new Point<Dim3, V>(new V[] {
            (V)(object)new Flt64(x + h * dx),
            (V)(object)new Flt64(y + h * dy),
            (V)(object)new Flt64(z + h * dz),
        }, Dim3.Instance);
    }

    /// <summary>造一个 Windmi 吸引子 / Create one.</summary>
    public static WindmiAttractor<Flt64> Create(
        Flt64? a = null,
        Flt64? b = null,
        Flt64? c = null,
        Flt64? d = null,
        Flt64? e = null,
        Flt64? f = null,
        Flt64? g = null,
        Flt64? h = null) =>
        new(
            a ?? new Flt64(0.2),
            b ?? new Flt64(0.2),
            c ?? new Flt64(2.2),
            d ?? new Flt64(0.5),
            e ?? new Flt64(0.2),
            f ?? new Flt64(5.7),
            g ?? new Flt64(0.5),
            h ?? new Flt64(0.01));
}

/// <summary>Windmi 吸引子的迭代器 / Iterator over the Windmi attractor.</summary>
public sealed class WindmiAttractorGenerator : IGenerator<Point<Dim3, Flt64>> {
    /// <summary>被迭代的吸引子 / The attractor being iterated.</summary>
    public WindmiAttractor<Flt64> Attractor { get; }

    /// <summary>当前点 / The current point.</summary>
    public Point<Dim3, Flt64> X { get; private set; }

    /// <summary>造一个迭代器 / Create an iterator.</summary>
    public WindmiAttractorGenerator(WindmiAttractor<Flt64>? attractor = null, Point<Dim3, Flt64>? x = null) {
        Attractor = attractor ?? WindmiAttractor<Flt64>.Create();
        X = x ?? PointFactory.point3(new Flt64(0.1), new Flt64(0.1), new Flt64(0.1));
    }

    /// <inheritdoc/>
    public Point<Dim3, Flt64> Invoke() {
        Point<Dim3, Flt64> current = X;
        X = Attractor.Invoke(X);
        return current;
    }
}
