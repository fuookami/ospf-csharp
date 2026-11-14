// 水循环与海鸥更新测试 / Tests for water-cycle and seagull updates.

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
/// 水循环更新 / The water-cycle update.
/// </summary>
public sealed class WaterCycleUpdateTests {
    /// <summary>**每个父代产一个子代且维数不变** / Each parent yields one child of the same dimension.</summary>
    [Fact]
    public void WaterCycle_ShouldPreserveCountAndDimension() {
        IReadOnlyList<Solution<Flt64>> children = new WaterCycleUpdate(RiverAmount: 2, FlowScale: 1.0, RainingProbability: 0.0)
            .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(), Parents(6), new StubModel(value => value[0]),
                IIntoValue<Flt64>.Identity, Entropy(3));

        children.Should().HaveCount(6);
        children.Should().OnlyContain(child => child.Values.Count == 2);
    }

    /// <summary>**空父代组产生空结果** / An empty parent group yields nothing.</summary>
    [Fact]
    public void WaterCycle_ShouldHandleEmptyParents() =>
        new WaterCycleUpdate()
            .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(), Array.Empty<EvaluatedIndividual<Flt64, Flt64>>(),
                new StubModel(value => value[0]), IIntoValue<Flt64>.Identity, Entropy(1))
            .Should().BeEmpty();

    /// <summary>**降雨概率为 1 时全体重生，且落点分散** / A full rain probability rebirths everyone at spread positions.</summary>
    /// <remarks>
    /// 重生从下界起按宽度均匀采样；固定从 0 起采样会让下界远大于 0 的变量空间根本采不到有效值，全部夹紧到同一边界点，
    /// 而"随机重生"看起来毫无问题。
    /// Rebirth samples uniformly from the lower bound across the width; sampling from 0 makes variables whose lower bound is far above 0
    /// unreachable and clamps everything to one boundary point while the "random rebirth" looks fine.
    /// </remarks>
    [Fact]
    public void WaterCycle_ShouldRebirthSpreadUnderFullRain() {
        IReadOnlyList<Solution<Flt64>> children = new WaterCycleUpdate(
                RiverAmount: 2, FlowScale: 1.0, RainingProbability: 1.0)
            .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(), Parents(8), new BoundedStubModel<object?>(2, 100.0, 200.0),
                IIntoValue<Flt64>.Identity, Entropy(4));

        children.SelectMany(child => child.Values)
            .Should().OnlyContain(value => value.ToDouble() >= 100.0 && value.ToDouble() <= 200.0);
        children.Select(child => child.Values[0].ToDouble()).Distinct()
            .Should().HaveCountGreaterThan(1, "重生应覆盖区间而不是全落到同一点 / rebirth covers the interval");
    }

    /// <summary>**降雨概率为 0 时河流向海靠拢** / With no rain the rivers contract toward the sea.</summary>
    [Fact]
    public void WaterCycle_ShouldFlowRiversTowardSea() {
        IReadOnlyList<Solution<Flt64>> children = new WaterCycleUpdate(
                RiverAmount: 2, FlowScale: 1.0, RainingProbability: 0.0)
            .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(), Parents(4), new StubModel(value => value[0]),
                IIntoValue<Flt64>.Identity, Entropy(5));

        // 海在 0，河在 10 与 20；一步流动后距离必须减小。
        // The sea sits at 0 and the rivers at 10 and 20; after one flow step the distance must shrink.
        global::System.Math.Abs(children[1].Values[0].ToDouble())
            .Should().BeLessThan(10.0, "河流向海流动 / rivers flow toward the sea");
    }

    /// <summary>**`riverAmount = 0` 时不会除零** / A zero river amount never divides by zero.</summary>
    [Fact]
    public void WaterCycle_ShouldHandleZeroRivers() {
        new WaterCycleUpdate(RiverAmount: 0, FlowScale: 1.0, RainingProbability: 0.0)
            .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(), Parents(4), new StubModel(value => value[0]),
                IIntoValue<Flt64>.Identity, Entropy(2))
            .Should().HaveCount(4);
    }

    /// <summary>**相同种子完全可重放** / The update replays exactly under the same seed.</summary>
    [Fact]
    public void WaterCycle_ShouldReplayUnderSameSeed() =>
        Flatten(Run(seed: 11)).Should().Equal(Flatten(Run(seed: 11)));

    /// <summary>执行一次更新 / Run one update.</summary>
    /// <param name="seed">种子 / The seed.</param>
    /// <returns>子代 / The children.</returns>
    private static IReadOnlyList<Solution<Flt64>> Run(long seed) {
        var random = new SearchRandom(seed);
        return new WaterCycleUpdate()
            .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(), Parents(6), new StubModel(value => value[0]),
                IIntoValue<Flt64>.Identity, () => new Flt64(random.NextDouble()));
    }

    /// <summary>摊平子代数值 / Flatten child values.</summary>
    /// <param name="children">子代 / The children.</param>
    /// <returns>摊平后的数值 / The flattened values.</returns>
    private static IEnumerable<double> Flatten(IReadOnlyList<Solution<Flt64>> children) =>
        children.SelectMany(child => child.Values).Select(value => value.ToDouble());

    /// <summary>构造父代组（下标 0 是海）/ Build a parent group where index 0 is the sea.</summary>
    /// <param name="count">父代数量 / The parent count.</param>
    /// <returns>父代组 / The parent group.</returns>
    private static EvaluatedIndividual<Flt64, Flt64>[] Parents(int count) =>
        Enumerable.Range(0, count)
            .Select(index => new EvaluatedIndividual<Flt64, Flt64>(
                new[] { new Flt64(index * 10.0), new Flt64(index * 10.0) }, new Flt64(index)))
            .ToArray();

    /// <summary>构造熵源 / Build the entropy source.</summary>
    /// <param name="seed">种子 / The seed.</param>
    /// <returns>均匀熵源 / The uniform entropy source.</returns>
    private static Func<Flt64> Entropy(long seed) {
        var random = new SearchRandom(seed);
        return () => new Flt64(random.NextDouble());
    }
}

/// <summary>
/// 海鸥更新 / The seagull update.
/// </summary>
public sealed class SeagullUpdateTests {
    /// <summary>**控制系数达到 1 时走迁移阶段，向最优靠拢** / A control coefficient at or above 1 migrates toward the best.</summary>
    [Fact]
    public void Seagull_ShouldMigrateTowardBestWhenControlHigh() {
        IReadOnlyList<Solution<Flt64>> children = new SeagullUpdate(
                new Fuookami.Ospf.Core.Solver.Heuristic.Operator.ConstantSource(new Flt64(2.0)), 0.1)
            .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(), Parents(), new StubModel(value => value[0]),
                IIntoValue<Flt64>.Identity, Entropy(3));

        // 最优在 0，个体在 100；迁移步长 2*4*unit ∈ [0,8) 倍差，夹紧后最多到 0。方向必须朝最优。
        // The best is 0 and the individual sits at 100; the migration step is 2*4*unit in [0,8) times the gap, so it must at least
        // shrink. The direction must be toward the best.
        children[1].Values[0].ToDouble().Should().BeLessThan(100.0);
    }

    /// <summary>**控制系数低于 1 时走螺旋阶段** / A control coefficient below 1 uses the spiral phase.</summary>
    /// <remarks>
    /// 两个阶段按系数切换；把判据写反或合并两段仍会收敛，但会失去"先探索后开发"的结构。
    /// The phases switch on the coefficient; reversing the predicate or merging them still converges but loses the
    /// explore-then-exploit structure.
    /// </remarks>
    [Fact]
    public void Seagull_ShouldSpiralWhenControlLow() {
        IReadOnlyList<Solution<Flt64>> children = new SeagullUpdate(
                new Fuookami.Ospf.Core.Solver.Heuristic.Operator.ConstantSource(new Flt64(0.5)), 0.1)
            .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(), Parents(), new StubModel(value => value[0]),
                IIntoValue<Flt64>.Identity, Entropy(3));

        // 最优在 1.0，个体在 5.0；螺旋围绕最优，结果必须落在两者附近而不等于任一端点。
        // The best is 1.0 and the individual sits at 5.0; the spiral orbits the best, so the result lands near it and equals neither.
        children[1].Values[0].ToDouble().Should().NotBe(5.0);
    }

    /// <summary>**`theta` 覆盖整个圆周，不会挤在同一条射线上** / `theta` covers the full circle instead of one ray.</summary>
    /// <remarks>
    /// 把 `2π` 漏掉会让所有个体挤在同一个角度上，螺旋退化成一条射线——而"螺旋"这个名字会让人以为没问题。
    /// Dropping the `2π` puts every individual on one angle and the spiral degenerates into a single ray, while the name "spiral" makes
    /// it look fine.
    /// </remarks>
    [Fact]
    public void Seagull_ShouldCoverFullCircle() {
        var random = new SearchRandom(7);
        IReadOnlyList<Solution<Flt64>> children = new SeagullUpdate(
                new Fuookami.Ospf.Core.Solver.Heuristic.Operator.ConstantSource(new Flt64(0.5)), 0.1)
            .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(), ManyParents(30), new StubModel(value => value[0]),
                IIntoValue<Flt64>.Identity, () => new Flt64(random.NextDouble()));

        IEnumerable<double> values = children.Select(child => child.Values[0].ToDouble());
        values.Should().Contain(value => value > 1.0, "螺旋应有一半落在最优右侧 / half the spiral lands right of the best");
        values.Should().Contain(value => value < 1.0, "螺旋应有一半落在最优左侧 / half the spiral lands left of the best");
    }

    /// <summary>**带边界时不越界** / The update never leaves the bounds.</summary>
    [Fact]
    public void Seagull_ShouldStayWithinBounds() {
        var random = new SearchRandom(5);

        new SeagullUpdate(
                new Fuookami.Ospf.Core.Solver.Heuristic.Operator.ConstantSource(new Flt64(0.5)), 0.5)
            .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(), ManyParents(10), new BoundedStubModel<object?>(1, -10.0, 10.0),
                IIntoValue<Flt64>.Identity, () => new Flt64(random.NextDouble()))
            .SelectMany(child => child.Values)
            .Should().OnlyContain(value => value.ToDouble() >= -10.0 && value.ToDouble() <= 10.0);
    }

    /// <summary>构造两个父代 / Build two parents.</summary>
    /// <returns>父代组 / The parent group.</returns>
    private static EvaluatedIndividual<Flt64, Flt64>[] Parents() => new[] {
        new EvaluatedIndividual<Flt64, Flt64>(new[] { new Flt64(1.0) }, new Flt64(0.0)),
        new EvaluatedIndividual<Flt64, Flt64>(new[] { new Flt64(5.0) }, new Flt64(1.0)),
    };

    /// <summary>构造多父代组 / Build many parents.</summary>
    /// <param name="count">父代数量 / The parent count.</param>
    /// <returns>父代组 / The parent group.</returns>
    /// <remarks>
    /// **最优必须与其余个体不同**。若最优也是 5.0，螺旋项 `target + (position - target) * radius * cos` 里的 `position - target`
    /// 恒为 0，子代永远不动——测试会误判成"螺旋方向单一"。第一版夹具正是这样写的，30 个个体全部原样返回 5.0。
    /// **The best must differ from the others.** If the best is also 5.0, the `position - target` factor in
    /// `target + (position - target) * radius * cos` is always 0 and the children never move — the test then misreads it as "the spiral
    /// has only one direction". The first fixture did exactly that, and all 30 individuals came back unchanged at 5.0.
    /// </remarks>
    private static EvaluatedIndividual<Flt64, Flt64>[] ManyParents(int count) =>
        Enumerable.Range(0, count)
            .Select(index => new EvaluatedIndividual<Flt64, Flt64>(
                new[] { new Flt64(index == 0 ? 1.0 : 5.0) }, new Flt64(index)))
            .ToArray();

    /// <summary>构造熵源 / Build the entropy source.</summary>
    /// <param name="seed">种子 / The seed.</param>
    /// <returns>均匀熵源 / The uniform entropy source.</returns>
    private static Func<Flt64> Entropy(long seed) {
        var random = new SearchRandom(seed);
        return () => new Flt64(random.NextDouble());
    }
}
