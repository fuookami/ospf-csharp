#nullable enable
using System.Linq;
using Fuookami.Ospf.Math.Algebra.Concept;

namespace Fuookami.Ospf.Math.Geometry;

/// <summary>
/// 距离度量策略 / Distance metric strategy.
/// </summary>
public abstract record Distance
{
    /// <summary>欧几里得距离 / Euclidean distance.</summary>
    public static readonly Distance Euclidean = new EuclideanMetric();

    /// <summary>曼哈顿距离 / Manhattan distance.</summary>
    public static readonly Distance Manhattan = new ManhattanMetric();

    /// <summary>切比雪夫距离 / Chebyshev distance.</summary>
    public static readonly Distance Chebyshev = new ChebyshevMetric();

    /// <summary>计算两点距离 / Compute distance between two points.</summary>
    public abstract V Compute<D, V>(Point<D, V> lhs, Point<D, V> rhs)
        where D : struct, IDimension
        where V : struct, IFloatingNumber<V>;
}

/// <summary>欧几里得距离 sqrt(sum((a-b)^2)) / Euclidean distance.</summary>
public sealed record EuclideanMetric : Distance
{
    /// <inheritdoc/>
    public override V Compute<D, V>(Point<D, V> lhs, Point<D, V> rhs)
    {
        var c = lhs[0].Constants;
        var sum = lhs.Indices.Aggregate(c.Zero, (acc, i) => acc.Plus(lhs[i].Minus(rhs[i]).Sqr()));
        return sum.Sqrt();
    }
}

/// <summary>曼哈顿距离 sum(|a-b|) / Manhattan distance.</summary>
public sealed record ManhattanMetric : Distance
{
    /// <inheritdoc/>
    public override V Compute<D, V>(Point<D, V> lhs, Point<D, V> rhs)
    {
        var c = lhs[0].Constants;
        return lhs.Indices.Aggregate(c.Zero, (acc, i) => acc.Plus(lhs[i].Minus(rhs[i]).Abs()));
    }
}

/// <summary>切比雪夫距离 max(|a-b|) / Chebyshev distance.</summary>
public sealed record ChebyshevMetric : Distance
{
    /// <inheritdoc/>
    public override V Compute<D, V>(Point<D, V> lhs, Point<D, V> rhs) =>
        lhs.Indices.Max(i => lhs[i].Minus(rhs[i]).Abs())!;
}

/// <summary>闵可夫斯基距离 / Minkowski distance.</summary>
public sealed record MinkowskiMetric(int P) : Distance
{
    /// <inheritdoc/>
    public override V Compute<D, V>(Point<D, V> lhs, Point<D, V> rhs)
    {
        var c = lhs[0].Constants;
        var sum = lhs.Indices.Aggregate(c.Zero, (acc, i) => acc.Plus(lhs[i].Minus(rhs[i]).Abs().Pow(P)));
        // sum^(1/P) using System.Math for the root
        var dSum = sum.ToFlt64();
        var root = new Fuookami.Ospf.Math.Algebra.Number.Flt64(
            System.Math.Pow(dSum.Value, 1.0 / P));
        // Convert back; this is approximate but covers the general case
        return (V)(object)root;
    }
}
