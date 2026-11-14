// 拓扑、幸存者选择与多样性测试 / Tests for topology, survivor selection and diversity.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// 种群拓扑 / Population topology.
/// </summary>
public sealed class TopologyTests {
    /// <summary>全局拓扑包含全部个体 / Global topology covers the whole population.</summary>
    [Fact]
    public void Global_ShouldCoverEveryone() =>
        GlobalTopology.Instance.Neighbours(3, 6).Should().BeEquivalentTo(new[] { 0, 1, 2, 3, 4, 5 });

    /// <summary>**环形拓扑必须包含自己** / A ring topology includes the centre.</summary>
    [Fact]
    public void Ring_ShouldIncludeCentre() =>
        RingTopology.Instance.Neighbours(0, 5).Should().BeEquivalentTo(new[] { 4, 0, 1 });

    /// <summary>**单个体种群的环里只有自己** / A single-element population looks only at itself.</summary>
    [Fact]
    public void Ring_ShouldHandleSingleElement() =>
        RingTopology.Instance.Neighbours(0, 1).Should().BeEquivalentTo(new[] { 0 });

    /// <summary>环形 K 邻域按半径展开 / Ring-K expands by radius.</summary>
    [Fact]
    public void RingK_ShouldExpandByRadius() =>
        new RingKTopology(2).Neighbours(0, 6).Should().BeEquivalentTo(new[] { 4, 5, 0, 1, 2 });

    /// <summary>**冯·诺依曼拓扑在网格边界不跨行环绕** / Von Neumann does not wrap columns across rows.</summary>
    /// <remarks>
    /// wrap 会把网格拓扑悄悄变成一条莫比乌斯带，邻居关系与二维直觉完全不同。
    /// Wrapping silently turns the grid topology into a Möbius band whose neighbour relations match no 2D intuition.
    /// </remarks>
    [Fact]
    public void VonNeumann_ShouldNotWrapAcrossRows() {
        // 3 宽网格，下标 2 是第 0 行最后一列；右邻应被丢弃而不是变成下标 3（第 1 行第一列）。
        // A 3-wide grid: index 2 is the last column of row 0; its right neighbour must be dropped, not become index 3.
        new VonNeumannTopology(3).Neighbours(2, 9)
            .Should().BeEquivalentTo(new[] { 2, 5, 1 });
    }

    /// <summary>随机拓扑包含自己且不超过种群规模 / Random topology includes the centre and stays within bounds.</summary>
    [Fact]
    public void Random_ShouldStayWithinBounds() {
        var random = new SearchRandom(5);
        RandomTopology topology = new(3, () => new Flt64(random.NextDouble()));

        IReadOnlySet<int> neighbours = topology.Neighbours(0, 10);

        neighbours.Should().Contain(0);
        neighbours.Should().OnlyContain(index => index >= 0 && index < 10);
    }

    /// <summary>**邻居数量超过种群规模时不会卡住** / An excessive neighbour amount does not hang.</summary>
    [Fact]
    public void Random_ShouldNotLoopOnMassiveAmount() {
        var random = new SearchRandom(1);
        RandomTopology topology = new(1000, () => new Flt64(random.NextDouble()));

        topology.Neighbours(0, 4).Should().HaveCount(4);
    }
}

/// <summary>
/// 幸存者选择 / Survivor selection.
/// </summary>
public sealed class SurvivorSelectionTests {
    /// <summary>精英幸存者从父代与子代合并池里取最优 / The elitist survivor takes the best of both pools.</summary>
    [Fact]
    public void Elitist_ShouldConsiderBothPools() {
        IReadOnlyList<EvaluatedIndividual<Flt64, Flt64>> survivors = ElitistSurvivor.Instance
            .Select<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                Model(), new[] { Individual(5.0) }, new[] { Individual(1.0) }, 1);

        survivors.Should().ContainSingle();
        survivors[0].Objective.Should().Be(new Flt64(1.0));
    }

    /// <summary>**世代幸存者只看子代** / The generational survivor ignores parents.</summary>
    [Fact]
    public void Generational_ShouldIgnoreParents() {
        IReadOnlyList<EvaluatedIndividual<Flt64, Flt64>> survivors = GenerationalSurvivor.Instance
            .Select<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                Model(), new[] { Individual(1.0) }, new[] { Individual(9.0) }, 1);

        survivors[0].Objective.Should().Be(new Flt64(9.0), "只从子代取 / only offspring is considered");
    }

    /// <summary>**截断幸存者同时受固定上限与请求数量约束** / Truncation is bounded by both the cap and the request.</summary>
    [Fact]
    public void Truncation_ShouldRespectBothBounds() {
        EvaluatedIndividual<Flt64, Flt64>[] population = { Individual(1.0), Individual(2.0), Individual(3.0) };

        new TruncationSurvivor(2)
            .Select<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                Model(), population, Array.Empty<EvaluatedIndividual<Flt64, Flt64>>(), 3)
            .Should().HaveCount(2);
        new TruncationSurvivor(5)
            .Select<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                Model(), population, Array.Empty<EvaluatedIndividual<Flt64, Flt64>>(), 1)
            .Should().HaveCount(1);
    }

    /// <summary>**保精英幸存者先保父代精英再从子代补足** / Elites come from parents, the remainder from offspring.</summary>
    [Fact]
    public void ElitistPreserving_ShouldSplitPools() {
        IReadOnlyList<EvaluatedIndividual<Flt64, Flt64>> survivors = new ElitistPreservingSurvivor(1)
            .Select<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                Model(),
                new[] { Individual(2.0), Individual(8.0) },
                new[] { Individual(3.0) },
                2);

        survivors.Should().HaveCount(2);
        survivors[0].Objective.Should().Be(new Flt64(2.0), "精英来自父代最优 / the elite is the best parent");
        survivors[1].Objective.Should().Be(new Flt64(3.0), "其余来自子代 / the rest comes from offspring");
    }

    /// <summary>请求 0 个个体时返回空 / Requesting zero yields nothing.</summary>
    [Fact]
    public void Survivor_ShouldReturnEmptyForZeroAmount() =>
        ElitistSurvivor.Instance.Select<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
            Model(), new[] { Individual(1.0) }, new[] { Individual(2.0) }, 0)
            .Should().BeEmpty();

    /// <summary>构造模型 / Build the model.</summary>
    private static StubModel Model() => new(value => value[0]);

    /// <summary>构造个体 / Build an individual.</summary>
    private static EvaluatedIndividual<Flt64, Flt64> Individual(double objective) =>
        new(new[] { new Flt64(objective) }, new Flt64(objective));
}

/// <summary>
/// 多样性度量 / Diversity metrics.
/// </summary>
public sealed class DiversityMetricTests {
    /// <summary>**少于两个解时多样性为 0** / Fewer than two solutions measure 0.</summary>
    /// <remarks>
    /// 返回无穷大或抛异常会让"种群塌缩成一个点"这一最需要被检测的状态反而报出最高多样性。
    /// Returning infinity or throwing would make a collapsed population report the highest diversity.
    /// </remarks>
    [Fact]
    public void MeanPairwiseDistance_ShouldBeZeroForTinySets() {
        MeanPairwiseDistance.Instance.Measure(Array.Empty<Solution<Flt64>>(), IIntoValue<Flt64>.Identity)
            .Should().Be(Flt64.Zero);
        MeanPairwiseDistance.Instance.Measure(
                new[] { new Solution<Flt64>(new[] { new Flt64(1.0) }) }, IIntoValue<Flt64>.Identity)
            .Should().Be(Flt64.Zero);
    }

    /// <summary>**完全相同的解多样性为 0** / Identical solutions measure 0.</summary>
    [Fact]
    public void MeanPairwiseDistance_ShouldBeZeroForIdenticalSolutions() {
        var solution = new Solution<Flt64>(new[] { new Flt64(1.0), new Flt64(2.0) });

        MeanPairwiseDistance.Instance.Measure(new[] { solution, solution }, IIntoValue<Flt64>.Identity)
            .Should().Be(Flt64.Zero);
    }

    /// <summary>**成对距离按欧氏均方根计算** / The pairwise distance is the Euclidean RMS.</summary>
    [Fact]
    public void MeanPairwiseDistance_ShouldComputeRms() {
        var first = new Solution<Flt64>(new[] { new Flt64(0.0), new Flt64(0.0) });
        var second = new Solution<Flt64>(new[] { new Flt64(3.0), new Flt64(4.0) });

        MeanPairwiseDistance.Instance.Measure(new[] { first, second }, IIntoValue<Flt64>.Identity)
            .ToDouble().Should().BeApproximately(5.0, 1e-12);
    }

    /// <summary>分散的解比聚集的解多样性更高 / A spread-out set measures higher than a clustered one.</summary>
    [Fact]
    public void MeanPairwiseDistance_ShouldPreferSpread() {
        Solution<Flt64>[] clustered = {
            new Solution<Flt64>(new[] { new Flt64(0.0) }),
            new Solution<Flt64>(new[] { new Flt64(0.1) }),
        };
        Solution<Flt64>[] spread = {
            new Solution<Flt64>(new[] { new Flt64(0.0) }),
            new Solution<Flt64>(new[] { new Flt64(10.0) }),
        };

        MeanPairwiseDistance.Instance.Measure(spread, IIntoValue<Flt64>.Identity).ToDouble()
            .Should().BeGreaterThan(
                MeanPairwiseDistance.Instance.Measure(clustered, IIntoValue<Flt64>.Identity).ToDouble());
    }
}
