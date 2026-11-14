// 多目标支配、非支配排序与参考点测试 / Tests for multi-objective dominance, non-dominated sorting and reference points.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator.MultiObjective;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// Pareto 支配 / Pareto dominance.
/// </summary>
public sealed class DominanceTests {
    /// <summary>全面更优的目标向量支配另一向量 / An all-round better vector dominates.</summary>
    [Fact]
    public void Dominates_ShouldRequireAllObjectivesBetterOrEqual() {
        IDominance dominance = new MinimizationDominance(2);

        dominance.Dominates(Vector(1.0, 2.0), Vector(3.0, 4.0)).Should().BeTrue();
    }

    /// <summary>**完全相同的向量互不支配** / Identical vectors never dominate each other.</summary>
    /// <remarks>
    /// 只判"没有更差"会让相同向量互相支配，非支配排序里同层个体被拆散到不同层。
    /// Testing only "not worse" makes identical vectors dominate each other and splits peers across fronts.
    /// </remarks>
    [Fact]
    public void Dominates_ShouldRejectIdenticalVectors() {
        IDominance dominance = new MinimizationDominance(2);

        dominance.Dominates(Vector(1.0, 1.0), Vector(1.0, 1.0)).Should().BeFalse();
        dominance.Dominates(Vector(1.0, 1.0), Vector(1.0, 1.0)).Should().BeFalse();
    }

    /// <summary>一优一劣互相不支配 / A trade-off pair is mutually non-dominating.</summary>
    [Fact]
    public void Dominates_ShouldRejectTradeOff() {
        IDominance dominance = new MinimizationDominance(2);

        dominance.Dominates(Vector(1.0, 5.0), Vector(5.0, 1.0)).Should().BeFalse();
        dominance.Dominates(Vector(5.0, 1.0), Vector(1.0, 5.0)).Should().BeFalse();
    }

    /// <summary>**方向表反转后支配关系随之反转** / Flipping a direction flips the dominance relation.</summary>
    [Fact]
    public void Dominates_ShouldRespectDirections() {
        IDominance minimize = new ParetoDominance(new[] { true, true });
        IDominance maximizeFirst = new ParetoDominance(new[] { false, true });

        minimize.Dominates(Vector(1.0, 1.0), Vector(2.0, 2.0)).Should().BeTrue();
        maximizeFirst.Dominates(Vector(1.0, 1.0), Vector(2.0, 2.0)).Should().BeFalse(
            "第一个目标改为最大化后，1 比 2 更差 / with the first objective maximized, 1 is worse than 2");
    }

    /// <summary>**维度不符时拒绝判定支配** / An arity mismatch refuses to assert dominance.</summary>
    /// <remarks>
    /// 把缺失目标当 0 会让一个完整向量凭空"支配"一个残缺向量。
    /// Treating missing objectives as 0 would let a complete vector spuriously dominate a truncated one.
    /// </remarks>
    [Fact]
    public void Dominates_ShouldRefuseOnArityMismatch() {
        IDominance dominance = new MinimizationDominance(2);

        dominance.Dominates(Vector(1.0), Vector(3.0, 4.0)).Should().BeFalse();
        dominance.Dominates(Vector(1.0, 1.0), Vector(9.0)).Should().BeFalse();
    }

    /// <summary>构造目标向量 / Build an objective vector.</summary>
    private static List<Flt64> Vector(params double[] values) =>
        values.Select(value => new Flt64(value)).ToList();
}

/// <summary>
/// 快速非支配排序 / Fast non-dominated sorting.
/// </summary>
public sealed class NonDominatedSortTests {
    /// <summary>**空输入返回空分层** / An empty input yields no fronts.</summary>
    [Fact]
    public void Sort_ShouldHandleEmpty() =>
        FastNonDominatedSort.Sort(Array.Empty<double[]>(), Vector, new MinimizationDominance(2))
            .Should().BeEmpty();

    /// <summary>**互不支配的个体全部落在第 0 层** / Mutually non-dominating individuals all land in front 0.</summary>
    [Fact]
    public void Sort_ShouldPutNonDominatedInFirstFront() {
        double[][] items = { new[] { 1.0, 5.0 }, new[] { 5.0, 1.0 }, new[] { 3.0, 3.0 } };

        IReadOnlyList<IReadOnlyList<double[]>> fronts =
            FastNonDominatedSort.Sort(items, Vector, new MinimizationDominance(2));

        fronts.Should().HaveCount(1);
        fronts[0].Should().HaveCount(3);
    }

    /// <summary>**被支配的个体落到后一层** / A dominated individual falls to a later front.</summary>
    [Fact]
    public void Sort_ShouldLayerDominated() {
        double[][] items = { new[] { 1.0, 1.0 }, new[] { 3.0, 3.0 } };

        IReadOnlyList<IReadOnlyList<double[]>> fronts =
            FastNonDominatedSort.Sort(items, Vector, new MinimizationDominance(2));

        fronts.Should().HaveCount(2);
        fronts[0][0].Should().Equal(1.0, 1.0);
        fronts[1][0].Should().Equal(3.0, 3.0);
    }

    /// <summary>**分层不丢个体** / Layering never drops individuals.</summary>
    [Fact]
    public void Sort_ShouldKeepEveryIndividual() {
        double[][] items = {
            new[] { 1.0, 1.0 }, new[] { 2.0, 2.0 }, new[] { 3.0, 3.0 },
            new[] { 1.0, 4.0 }, new[] { 4.0, 1.0 },
        };

        IReadOnlyList<IReadOnlyList<double[]>> fronts =
            FastNonDominatedSort.Sort(items, Vector, new MinimizationDominance(2));

        fronts.Sum(front => front.Count).Should().Be(items.Length);
    }

    /// <summary>**维度不符造成的环不会吞掉个体** / Arity-mismatch cycles do not swallow individuals.</summary>
    /// <remarks>
    /// 维度不符时支配判定一律返回 false，可能让分层循环提前结束；没有兜底的话剩余个体就此消失，而迭代继续正常进行。
    /// Arity mismatches make dominance always false, which can end the layering loop early; without the fallback the remaining
    /// individuals vanish while iterations keep running normally.
    /// </remarks>
    [Fact]
    public void Sort_ShouldNotLoseIndividualsOnMismatch() {
        double[][] items = { new[] { 1.0, 1.0 }, new[] { 2.0 } };

        IReadOnlyList<IReadOnlyList<double[]>> fronts =
            FastNonDominatedSort.Sort(items, Vector, new MinimizationDominance(2));

        fronts.Sum(front => front.Count).Should().Be(2);
    }

    /// <summary>构造目标向量 / Build an objective vector.</summary>
    private static List<Flt64> Vector(double[] values) =>
        values.Select(value => new Flt64(value)).ToList();
}

/// <summary>
/// Das–Dennis 参考点 / Das-Dennis reference points.
/// </summary>
public sealed class ReferencePointsTests {
    /// <summary>**单目标只有一个参考点** / A single objective has exactly one reference point.</summary>
    /// <remarks>
    /// 递归在 objectivesLeft == 1 处立即收敛，得到 divisions / divisions = 1 这一个点。单目标的 Pareto 前沿就是最优点本身，
    /// 不需要参考点网格。
    /// The recursion collapses immediately at objectivesLeft == 1, yielding the single point divisions / divisions = 1. A
    /// single-objective Pareto front is just the optimum itself and needs no reference grid.
    /// </remarks>
    [Fact]
    public void DasDennis_ShouldReturnSinglePointForSingleObjective() =>
        ReferencePoints.DasDennis(1, 4).Should().HaveCount(1);

    /// <summary>**点数等于组合数** / The count matches the binomial coefficient.</summary>
    [Fact]
    public void DasDennis_ShouldMatchBinomialCount() =>
        // C(4 + 3 - 1, 3 - 1) = C(6, 2) = 15
        ReferencePoints.DasDennis(3, 4).Should().HaveCount(15);

    /// <summary>**每个参考点各分量之和为 1** / Every reference point's components sum to one.</summary>
    [Fact]
    public void DasDennis_ShouldBeSimplexPoints() {
        foreach (IReadOnlyList<double> point in ReferencePoints.DasDennis(3, 3)) {
            point.Sum().Should().BeApproximately(1.0, 1e-12);
        }
    }

    /// <summary>**非法参数返回空列表而不抛异常** / Invalid arguments return an empty list instead of throwing.</summary>
    [Fact]
    public void DasDennis_ShouldReturnEmptyForInvalidArguments() {
        ReferencePoints.DasDennis(0, 3).Should().BeEmpty();
        ReferencePoints.DasDennis(3, 0).Should().BeEmpty();
        ReferencePoints.DasDennis(-1, -1).Should().BeEmpty();
    }
}
