// 档案与拥挤距离测试 / Tests for search archives and crowding distance.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator.MultiObjective;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// 拥挤距离 / Crowding distance.
/// </summary>
public sealed class CrowdingDistanceTests {
    /// <summary>**边界个体距离为无穷大** / Boundary individuals get infinite distance.</summary>
    /// <remarks>
    /// 边界距离算成有限值，前沿会向中间收缩，最终丢掉整个极端权衡区域，而每一代的成员看起来都合法。
    /// A finite boundary distance makes the front contract toward its middle and eventually lose the extreme trade-off region.
    /// </remarks>
    [Fact]
    public void Assign_ShouldGiveInfiniteDistanceToBoundaries() {
        IReadOnlyList<double> distances = CrowdingDistance.Assign(new[] {
            Vector(1.0, 5.0), Vector(3.0, 3.0), Vector(5.0, 1.0),
        });

        distances[0].Should().Be(double.PositiveInfinity);
        distances[2].Should().Be(double.PositiveInfinity);
        double.IsFinite(distances[1]).Should().BeTrue("中间点距离有限 / the middle point has a finite distance");
    }

    /// <summary>两个及以下个体全部无穷远 / Two or fewer individuals are all infinitely distant.</summary>
    /// <summary>**中间点的拥挤距离有确定的数值** / The middle point's crowding distance has a determinate value.</summary>
    /// <remarks>
    /// **这条补的是验收表 8.4 说的那个缺口：前面只查了"中间点有限"，而"有限"对 0.5 和 200 一视同仁。**
    ///
    /// **这两组黄金值是按 `(next - previous) / span` 手算的，不是从 C# 输出抄的：**
    ///
    /// - `(1,5) (3,3) (5,1)`：`x` 跨度 4，`y` 跨度 4，中间点各得 `(5-1)/4 = 1`，合计 **2**。
    /// - `(0,0) (1,10) (3,11) (10,12)`：`x` 跨度 10，`y` 跨度 12；
    ///   `(1,10)` = `(3-0)/10 + (11-0)/12` = `3/10 + 11/12` = **73/60**；
    ///   `(3,11)` = `(10-1)/10 + (12-10)/12` = `9/10 + 1/6` = **16/15**。
    ///
    /// **第二组的两个数不等，所以它同时钉住了"每个点各自算"，而不只是"总和对了"。**
    ///
    /// **This fills the gap acceptance row 8.4 names: the earlier test only checked that the middle point is finite,
    /// and "finite" treats 0.5 and 200 alike.**
    /// **Both golden values are computed by hand from `(next - previous) / span`, not copied from C# output.**
    /// **The two values in the second set differ, so it also pins "per point" rather than only "the sum happens to match".**
    /// </remarks>
    [Fact]
    public void Assign_ShouldMatchANumericGoldenVector()
    {
        IReadOnlyList<double> three = CrowdingDistance.Assign(new[] {
            Vector(1.0, 5.0), Vector(3.0, 3.0), Vector(5.0, 1.0),
        });
        three[1].Should().BeApproximately(2.0, 1e-12, "3/10-style fractions cancel exactly here");

        IReadOnlyList<double> four = CrowdingDistance.Assign(new[] {
            Vector(0.0, 0.0), Vector(1.0, 10.0), Vector(3.0, 11.0), Vector(10.0, 12.0),
        });
        four[1].Should().BeApproximately(73.0 / 60.0, 1e-12, "3/10 + 11/12");
        four[2].Should().BeApproximately(16.0 / 15.0, 1e-12, "9/10 + 1/6, which is not equal to the previous point");
    }

    [Fact]
    public void Assign_ShouldHandleTinySets() {
        CrowdingDistance.Assign(Array.Empty<IReadOnlyList<Flt64>>()).Should().BeEmpty();
        CrowdingDistance.Assign(new[] { Vector(1.0, 1.0) }).Should().OnlyContain(value => double.IsPositiveInfinity(value));
        CrowdingDistance.Assign(new[] { Vector(1.0, 1.0), Vector(2.0, 2.0) })
            .Should().OnlyContain(value => double.IsPositiveInfinity(value));
    }

    /// <summary>**跨度为 0 的维度被跳过，不产生 NaN** / A zero-span dimension is skipped, never producing NaN.</summary>
    [Fact]
    public void Assign_ShouldSkipZeroSpanDimension() {
        IReadOnlyList<double> distances = CrowdingDistance.Assign(new[] {
            Vector(1.0, 7.0), Vector(2.0, 7.0), Vector(3.0, 7.0),
        });

        distances.Should().NotContain(value => double.IsNaN(value));
    }

    /// <summary>构造目标向量 / Build an objective vector.</summary>
    private static IReadOnlyList<Flt64> Vector(params double[] values) =>
        values.Select(value => new Flt64(value)).ToList();
}

/// <summary>
/// Pareto 档案 / The Pareto archive.
/// </summary>
public sealed class ParetoArchiveTests {
    /// <summary>互不支配的候选全部进入档案 / Non-dominating candidates all enter the archive.</summary>
    [Fact]
    public void Offer_ShouldKeepNonDominated() {
        ParetoArchive<double[]> archive = Archive(capacity: 10);

        archive.Offer(new[] { new[] { 1.0, 5.0 }, new[] { 5.0, 1.0 } });

        archive.Size.Should().Be(2);
    }

    /// <summary>**被支配的候选被丢弃** / A dominated candidate is discarded.</summary>
    [Fact]
    public void Offer_ShouldDiscardDominated() {
        ParetoArchive<double[]> archive = Archive(capacity: 10);

        archive.Offer(new[] { new[] { 1.0, 1.0 }, new[] { 3.0, 3.0 } });

        archive.Size.Should().Be(1);
        archive.Items[0].Should().Equal(1.0, 1.0);
    }

    /// <summary>**新解支配档案成员时淘汰它们** / A dominating newcomer evicts archive members.</summary>
    [Fact]
    public void Offer_ShouldEvictDominatedMembers() {
        ParetoArchive<double[]> archive = Archive(capacity: 10);
        archive.Offer(new[] { new[] { 3.0, 3.0 } });

        archive.Offer(new[] { new[] { 1.0, 1.0 } });

        archive.Items.Should().HaveCount(1);
        archive.Items[0].Should().Equal(1.0, 1.0);
    }

    /// <summary>**重复目标向量只保留一份** / A repeated objective vector is kept once.</summary>
    /// <remarks>
    /// 全部保留会让档案被同一片区域的重复点塞满，真正的权衡区域挤不进去。
    /// Keeping all of them fills the archive with duplicates from one region and crowds out the genuine trade-off areas.
    /// </remarks>
    [Fact]
    public void Offer_ShouldRejectDuplicates() {
        ParetoArchive<double[]> archive = Archive(capacity: 10);

        archive.Offer(new[] { new[] { 2.0, 2.0 }, new[] { 2.0, 2.0 } });

        archive.Size.Should().Be(1);
    }

    /// <summary>**超出容量时按拥挤距离裁剪** / Trimming keeps the largest crowding distances.</summary>
    [Fact]
    public void Offer_ShouldTrimToCapacity() {
        ParetoArchive<double[]> archive = Archive(capacity: 2);

        archive.Offer(new[] {
            new[] { 1.0, 5.0 }, new[] { 2.0, 4.0 }, new[] { 5.0, 1.0 },
        });

        archive.Size.Should().Be(2);
        // 极端权衡的两端距离为无穷大，必然保留；中间点被裁掉。
        // The two extremes have infinite distance and must survive; the middle is trimmed.
        archive.Items.Should().Contain(item => item[0] == 1.0);
        archive.Items.Should().Contain(item => item[0] == 5.0);
    }

    /// <summary>**同一输入两次裁剪结果一致** / Trimming is stable across runs.</summary>
    [Fact]
    public void Offer_ShouldTrimDeterministically() {
        IReadOnlyList<double[]> first = Archive(capacity: 2).Offer(new[] {
            new[] { 1.0, 5.0 }, new[] { 2.0, 4.0 }, new[] { 5.0, 1.0 },
        });
        IReadOnlyList<double[]> second = Archive(capacity: 2).Offer(new[] {
            new[] { 1.0, 5.0 }, new[] { 2.0, 4.0 }, new[] { 5.0, 1.0 },
        });

        // double[] 在断言库里按引用比较，必须显式摊平成可比较的标量序列，否则两条内容相同的档案会被判为不同。
        // double[] compares by reference in the assertion library, so the vectors must be flattened into comparable scalars;
        // otherwise two archives with identical contents read as different.
        first.Select(item => string.Join(",", item)).Should()
            .Equal(second.Select(item => string.Join(",", item)));
    }

    /// <summary>构造档案 / Build an archive.</summary>
    private static ParetoArchive<double[]> Archive(int capacity) =>
        new(capacity, item => item.Select(value => new Flt64(value)).ToList(), new MinimizationDominance(2));
}

/// <summary>
/// 保优档案 / The best-only archive.
/// </summary>
public sealed class BestArchiveTests {
    /// <summary>**首次更新即视为改善** / The first update counts as an improvement.</summary>
    [Fact]
    public void Update_ShouldReportImprovementOnFirstFill() {
        var archive = new BestArchive<Flt64, Flt64>(3);

        archive.Update<object?>(new StubModel(value => value[0]), new[] { Individual(5.0) })
            .Should().BeTrue();
    }

    /// <summary>更差的候选不改善档案最优 / A worse candidate does not improve the best.</summary>
    [Fact]
    public void Update_ShouldNotReportImprovementForWorse() {
        var archive = new BestArchive<Flt64, Flt64>(3);
        var model = new StubModel(value => value[0]);
        archive.Update<object?>(model, new[] { Individual(1.0) });

        archive.Update<object?>(model, new[] { Individual(9.0) }).Should().BeFalse();
    }

    /// <summary>更好的候选改善档案最优 / A better candidate improves the best.</summary>
    [Fact]
    public void Update_ShouldReportImprovementForBetter() {
        var archive = new BestArchive<Flt64, Flt64>(3);
        var model = new StubModel(value => value[0]);
        archive.Update<object?>(model, new[] { Individual(9.0) });

        archive.Update<object?>(model, new[] { Individual(1.0) }).Should().BeTrue();
    }

    /// <summary>**档案按容量截断且最优在前** / The archive truncates to capacity with the best first.</summary>
    [Fact]
    public void Update_ShouldTruncateKeepingBest() {
        var archive = new BestArchive<Flt64, Flt64>(2);
        var model = new StubModel(value => value[0]);

        archive.Update<object?>(model, new[] { Individual(5.0), Individual(1.0), Individual(3.0) });

        archive.Individuals.Should().HaveCount(2);
        archive.Individuals[0].Objective.Should().Be(new Flt64(1.0));
    }

    /// <summary>清空后档案为空 / Clearing empties the archive.</summary>
    [Fact]
    public void Clear_ShouldEmpty() {
        var archive = new BestArchive<Flt64, Flt64>(3);
        archive.Update<object?>(new StubModel(value => value[0]), new[] { Individual(1.0) });

        archive.Clear();

        archive.Individuals.Should().BeEmpty();
    }

    /// <summary>构造个体 / Build an individual.</summary>
    private static EvaluatedIndividual<Flt64, Flt64> Individual(double objective) =>
        new(new[] { new Flt64(objective) }, new Flt64(objective));
}
