#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Geometry;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// Sprott 吸引子（三维离散 / 显式欧拉）/ The Sprott attractor.
/// </summary>
/// <remarks>
/// 对齐。
///
/// <c>dx = a(y−x)</c>，<c>dy = y(c−x) − y</c>，<c>dz = xy − cz</c>，然后 <c>x += h·dx</c>。
///
/// **基线声明了参数 `b`（默认 28.0）而公式里从未使用它。**
/// **这不是笔误可以推断的地方**——**保留它是因为那是签名**，
/// **但判据会钉住"改 b 不改变轨迹"这件事**，**以免下一个人以为 b 有效而调它。**
///
/// The Sprott attractor from the code.
///
/// <c>dx = a(y−x)</c>, <c>dy = y(c−x) − y</c>, <c>dz = xy − cz</c>, then <c>x += h·dx</c>.
///
/// **The code declares a parameter `b` (default 28.0) that the equations never use.**
/// **This is not a place to infer a typo** — **it is kept because it is its signature**,
/// **but a criterion pins "changing b does not move the trajectory", so nobody tunes it believing it matters.**
/// </remarks>
public sealed record SprottAttractor<V>(V A, V B, V C, V H) : IExtractor<Point<Dim3, V>, Point<Dim3, V>>
    where V : struct, IFloatingNumber<V> {
    /// <inheritdoc/>
    public Point<Dim3, V> Invoke(Point<Dim3, V> p) {
        double x = ((Flt64)(object)p[0]).Value;
        double y = ((Flt64)(object)p[1]).Value;
        double z = ((Flt64)(object)p[2]).Value;

        double a = ((Flt64)(object)A).Value;
        double c = ((Flt64)(object)C).Value;
        double h = ((Flt64)(object)H).Value;

        // dx = a(y - x)
        double dx = a * (y - x);
        // dy = y(c - x) - y
        double dy = y * (c - x) - y;
        // dz = x*y - c*z
        double dz = x * y - c * z;

        return new Point<Dim3, V>(new V[] {
            (V)(object)new Flt64(x + h * dx),
            (V)(object)new Flt64(y + h * dy),
            (V)(object)new Flt64(z + h * dz),
        }, Dim3.Instance);
    }

    /// <summary>造一个 Sprott 吸引子 / Create one.</summary>
    public static SprottAttractor<Flt64> Create(
        Flt64? a = null,
        Flt64? b = null,
        Flt64? c = null,
        Flt64? h = null) =>
        new(
            a ?? new Flt64(10.0),
            b ?? new Flt64(28.0),
            c ?? new Flt64(8.0) / new Flt64(3.0),
            h ?? new Flt64(0.01));
}

/// <summary>Sprott 吸引子的迭代器 / Iterator over the Sprott attractor.</summary>
public sealed class SprottAttractorGenerator : IGenerator<Point<Dim3, Flt64>> {
    /// <summary>被迭代的吸引子 / The attractor being iterated.</summary>
    public SprottAttractor<Flt64> Attractor { get; }

    /// <summary>当前点 / The current point.</summary>
    public Point<Dim3, Flt64> X { get; private set; }

    /// <summary>造一个迭代器 / Create an iterator.</summary>
    public SprottAttractorGenerator(SprottAttractor<Flt64>? attractor = null, Point<Dim3, Flt64>? x = null) {
        Attractor = attractor ?? SprottAttractor<Flt64>.Create();
        X = x ?? PointFactory.point3(new Flt64(0.1), new Flt64(0.1), new Flt64(0.1));
    }

    /// <inheritdoc/>
    public Point<Dim3, Flt64> Invoke() {
        Point<Dim3, Flt64> current = X;
        X = Attractor.Invoke(X);
        return current;
    }
}
