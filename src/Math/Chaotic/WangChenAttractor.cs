#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Geometry;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// Wang–Chen 吸引子（四维）/ The Wang–Chen four-dimensional attractor.
/// </summary>
/// <remarks>
/// 对齐基线 `WangChenAttractor.kt`。
///
/// <c>dx = α(y−x)</c>，<c>dy = βx − γxz + w</c>，<c>dz = xy − δz</c>，<c>dw = −yz</c>。
///
/// **四维里最容易漏掉的是 `dw = −yz`**：**它没有 `w` 项，所以 `w` 只被别的分量推动、自己不衰减。**
/// **把它写成 `dw = −δw` 或 `dw = −y` 都会让第四个分量变号或归零，**
/// **而"轨迹仍在有界范围内"这种弱检查看不出来。**
///
/// The Wang–Chen attractor from the baseline.
///
/// <c>dx = α(y−x)</c>, <c>dy = βx − γxz + w</c>, <c>dz = xy − δz</c>, <c>dw = −yz</c>.
///
/// **The easiest thing to drop in four dimensions is that `dw = −yz` carries no `w`**: **so `w` is pushed by the other
/// components and never decays on its own.** **Writing `dw = −δw` or `dw = −y` flips or zeroes the fourth component,**
/// **and a weak check like "the trajectory stays bounded" cannot see it.**
/// </remarks>
public sealed record WangChenAttractor<V>(V Alpha, V Beta, V Gamma, V Delta, V H)
    : IExtractor<Point<Dim4, V>, Point<Dim4, V>>
    where V : struct, IFloatingNumber<V> {
    /// <inheritdoc/>
    public Point<Dim4, V> Invoke(Point<Dim4, V> p) {
        double x = ((Flt64)(object)p[0]).Value;
        double y = ((Flt64)(object)p[1]).Value;
        double z = ((Flt64)(object)p[2]).Value;
        double w = ((Flt64)(object)p[3]).Value;

        double alpha = ((Flt64)(object)Alpha).Value;
        double beta = ((Flt64)(object)Beta).Value;
        double gamma = ((Flt64)(object)Gamma).Value;
        double delta = ((Flt64)(object)Delta).Value;
        double h = ((Flt64)(object)H).Value;

        double dx = alpha * (y - x);
        double dy = beta * x - gamma * x * z + w;
        double dz = x * y - delta * z;
        // dw 里没有 w：第四个分量只被 y 与 z 的乘积推动。
        // No w in dw: the fourth component is driven only by the product of y and z.
        double dw = -y * z;

        return new Point<Dim4, V>(new V[] {
            (V)(object)new Flt64(x + h * dx),
            (V)(object)new Flt64(y + h * dy),
            (V)(object)new Flt64(z + h * dz),
            (V)(object)new Flt64(w + h * dw),
        }, Dim4.Instance);
    }

    /// <summary>造一个 Wang–Chen 吸引子 / Create one.</summary>
    public static WangChenAttractor<Flt64> Create(
        Flt64? alpha = null,
        Flt64? beta = null,
        Flt64? gamma = null,
        Flt64? delta = null,
        Flt64? h = null) =>
        new(
            alpha ?? new Flt64(10.0),
            beta ?? new Flt64(40.0),
            gamma ?? new Flt64(1.0),
            delta ?? new Flt64(2.5),
            h ?? new Flt64(0.01));
}

/// <summary>Wang–Chen 吸引子的迭代器 / Iterator over the Wang–Chen attractor.</summary>
public sealed class WangChenAttractorGenerator : IGenerator<Point<Dim4, Flt64>> {
    /// <summary>被迭代的吸引子 / The attractor being iterated.</summary>
    public WangChenAttractor<Flt64> Attractor { get; }

    /// <summary>当前点 / The current point.</summary>
    public Point<Dim4, Flt64> X { get; private set; }

    /// <summary>造一个迭代器 / Create an iterator.</summary>
    public WangChenAttractorGenerator(
        WangChenAttractor<Flt64>? attractor = null,
        Point<Dim4, Flt64>? x = null) {
        Attractor = attractor ?? WangChenAttractor<Flt64>.Create();
        X = x ?? PointFactory.point4(
            new Flt64(0.1), new Flt64(0.1), new Flt64(0.1), new Flt64(0.1));
    }

    /// <inheritdoc/>
    public Point<Dim4, Flt64> Invoke() {
        Point<Dim4, Flt64> current = X;
        X = Attractor.Invoke(X);
        return current;
    }
}
