#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// Hopfractional 映射（一维，分数阶 Hopf 型）/ The Hopfractional map.
/// </summary>
/// <remarks>
/// Hopfractional 映射的方程：
///
/// <c>x' = p·x·(1 + (k·x)^q) / (1 + (s·x)^q)</c>。
///
/// **`(k·x)^q` 在 `k·x &lt; 0` 且 `q` 非整数时是 NaN**（实数域里负数没有一般的分数次幂）。
/// **这里不做保护**——**但把这条限制写下来，因为"轨迹突然全变 NaN"会被误当成发散。**
///
/// **`iterations`（默认 1000）在迭代器里只被保存、从不使用**——与 `Windmi` 的 `g`、`Sprott` 的 `b` 同类。
///
/// The Hopfractional map.
///
/// <c>x' = p·x·(1 + (k·x)^q) / (1 + (s·x)^q)</c>.
///
/// **`(k·x)^q` is NaN when `k·x &lt; 0` and `q` is not an integer** (a negative base has no general real fractional
/// power). **Nothing is guarded here** — **but the limit is written down, because "the
/// trajectory suddenly went all NaN" gets mistaken for divergence.**
///
/// **`iterations` (default 1000) is stored and never used by the iterator** — the same kind as `Windmi`'s `g` and
/// `Sprott`'s `b`.
/// </remarks>
public sealed record HopfractionalMap<V>(V P, V Q, V K, V S) : IExtractor<V, V>
    where V : struct, IFloatingNumber<V> {
    /// <inheritdoc/>
    public V Invoke(V x) {
        double xv = ((Flt64)(object)x).Value;
        double p = ((Flt64)(object)P).Value;
        double q = ((Flt64)(object)Q).Value;
        double k = ((Flt64)(object)K).Value;
        double s = ((Flt64)(object)S).Value;

        double numerator = 1.0 + global::System.Math.Pow(k * xv, q);
        double denominator = 1.0 + global::System.Math.Pow(s * xv, q);

        return (V)(object)new Flt64(p * xv * numerator / denominator);
    }

    /// <summary>造一个 Hopfractional 映射 / Create one.</summary>
    public static HopfractionalMap<Flt64> Create(
        Flt64? p = null,
        Flt64? q = null,
        Flt64? k = null,
        Flt64? s = null) =>
        new(
            p ?? new Flt64(0.5),
            q ?? new Flt64(2.0),
            k ?? new Flt64(1.0),
            s ?? new Flt64(1.0));
}

/// <summary>Hopfractional 映射的迭代器 / Iterator over the Hopfractional map.</summary>
public sealed class HopfractionalMapGenerator : IGenerator<Flt64> {
    /// <summary>被迭代的映射 / The map being iterated.</summary>
    public HopfractionalMap<Flt64> Map { get; }

    /// <summary>**只被保存、从不使用的迭代次数** / Iteration count, stored and unused.</summary>
    /// <remarks>
    /// **保留是因为它是签名的一部分**——**而"它不影响任何输出"必须可测，否则调用方会以为改它能改结果。**
    ///
    /// **Kept because it is part of the signature** — **and "it changes no output" has to be measurable, or a
    /// caller believes changing it changes the result.**
    /// </remarks>
    public ulong Iterations { get; }

    /// <summary>当前值 / The current value.</summary>
    public Flt64 X { get; private set; }

    /// <summary>造一个迭代器 / Create an iterator.</summary>
    public HopfractionalMapGenerator(
        HopfractionalMap<Flt64>? map = null,
        ulong iterations = 1000UL,
        Flt64? x = null) {
        Map = map ?? HopfractionalMap<Flt64>.Create();
        Iterations = iterations;
        X = x ?? new Flt64(0.1);
    }

    /// <inheritdoc/>
    public Flt64 Invoke() {
        Flt64 current = X;
        X = Map.Invoke(X);
        return current;
    }
}
