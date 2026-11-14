// 群智能类更新算子测试 / Tests for swarm-intelligence update operators.

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
/// 群智能类更新算子 / Swarm-intelligence update operators.
/// </summary>
public sealed class SwarmUpdateTests {
    /// <summary>**每个父代产一个子代且维数不变** / Each parent yields one child of the same dimension.</summary>
    [Fact]
    public void Update_ShouldPreserveCountAndDimension() {
        foreach (ISolutionUpdate update in AllUpdates()) {
            IReadOnlyList<Solution<Flt64>> children = Run(update, Parents(5), seed: 2);

            children.Should().HaveCount(5);
            children.Should().OnlyContain(child => child.Values.Count == 2);
        }
    }

    /// <summary>**空父代组产生空结果** / An empty parent group yields nothing.</summary>
    [Fact]
    public void Update_ShouldHandleEmptyParents() {
        foreach (ISolutionUpdate update in AllUpdates()) {
            Run(update, Array.Empty<EvaluatedIndividual<Flt64, Flt64>>(), seed: 1).Should().BeEmpty();
        }
    }

    /// <summary>**相同种子完全可重放** / Updates replay exactly under the same seed.</summary>
    [Fact]
    public void Update_ShouldReplayUnderSameSeed() {
        foreach (ISolutionUpdate update in AllUpdates()) {
            Flatten(Run(update, Parents(4), seed: 7)).Should().Equal(Flatten(Run(update, Parents(4), seed: 7)));
        }
    }

    /// <summary>**单个体种群也能更新** / A single-individual population still updates.</summary>
    [Fact]
    public void Update_ShouldHandleSingleParent() {
        foreach (ISolutionUpdate update in AllUpdates()) {
            IReadOnlyList<Solution<Flt64>> children = Run(update, Parents(1), seed: 3);

            children.Should().ContainSingle();
            children[0].Values.Should().HaveCount(2);
        }
    }

    /// <summary>列出两个群智能算子 / Enumerate the two swarm operators.</summary>
    private static IEnumerable<ISolutionUpdate> AllUpdates() => new ISolutionUpdate[] {
        new SineCosineUpdate(),
        new MultiVerseUpdate(),
    };

    /// <summary>执行一次更新 / Run one update.</summary>
    /// <param name="update">更新算子 / The update operator.</param>
    /// <param name="parents">父代组 / The parent group.</param>
    /// <param name="seed">种子 / The seed.</param>
    /// <returns>子代 / The children.</returns>
    private static IReadOnlyList<Solution<Flt64>> Run(
        ISolutionUpdate update,
        IReadOnlyList<EvaluatedIndividual<Flt64, Flt64>> parents,
        long seed) {
        var random = new SearchRandom(seed);
        return update.Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
            new Iteration(), parents, new StubModel(value => value[0]), IIntoValue<Flt64>.Identity,
            () => new Flt64(random.NextDouble()));
    }

    /// <summary>摊平子代数值 / Flatten child values.</summary>
    /// <param name="children">子代 / The children.</param>
    /// <returns>摊平后的数值 / The flattened values.</returns>
    private static IEnumerable<double> Flatten(IReadOnlyList<Solution<Flt64>> children) =>
        children.SelectMany(child => child.Values).Select(value => value.ToDouble());

    /// <summary>构造父代组 / Build a parent group.</summary>
    /// <param name="count">父代数量 / The parent count.</param>
    /// <returns>父代组 / The parent group.</returns>
    private static EvaluatedIndividual<Flt64, Flt64>[] Parents(int count) =>
        Enumerable.Range(0, count)
            .Select(index => new EvaluatedIndividual<Flt64, Flt64>(
                new[] { new Flt64(index), new Flt64(index + 10) },
                new Flt64(index)))
            .ToArray();
}

/// <summary>
/// 正弦余弦更新 / The sine-cosine update.
/// </summary>
public sealed class SineCosineUpdateTests {
    /// <summary>**幅值为 0 时不移动** / A zero amplitude does not move the solution.</summary>
    /// <remarks>
    /// 幅值按迭代线性衰减到 0 是收敛机制的核心；这条同时说明"幅值确实从参数源读取"，而不是写死的常量。
    /// The amplitude decaying to 0 is the core convergence mechanism; this also shows the amplitude really comes from the parameter
    /// source instead of a hard-coded constant.
    /// </remarks>
    [Fact]
    public void SineCosine_ShouldNotMoveWhenAmplitudeIsZero() {
        IReadOnlyList<Solution<Flt64>> children = new SineCosineUpdate(
                new Fuookami.Ospf.Core.Solver.Heuristic.Operator.ConstantSource(Flt64.Zero))
            .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(), Parents(), new StubModel(value => value[0]),
                IIntoValue<Flt64>.Identity, Entropy(5));

        for (int index = 0; index < children.Count; index++) {
            children[index].Values.Select(value => value.ToDouble())
                .Should().Equal(Parents()[index].Solution.Select(value => value.ToDouble()));
        }
    }

    /// <summary>**非零幅值时子代确实移动** / A non-zero amplitude moves the solution.</summary>
    [Fact]
    public void SineCosine_ShouldMoveWithAmplitude() {
        IReadOnlyList<Solution<Flt64>> children = new SineCosineUpdate(
                new Fuookami.Ospf.Core.Solver.Heuristic.Operator.ConstantSource(new Flt64(2.0)))
            .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(), Parents(), new StubModel(value => value[0]),
                IIntoValue<Flt64>.Identity, Entropy(5));

        children[1].Values.Select(value => value.ToDouble())
            .Should().NotEqual(Parents()[1].Solution.Select(value => value.ToDouble()));
    }

    /// <summary>**带边界时不越界** / The update never leaves the bounds.</summary>
    [Fact]
    public void SineCosine_ShouldStayWithinBounds() {
        var random = new SearchRandom(9);

        new SineCosineUpdate(
                new Fuookami.Ospf.Core.Solver.Heuristic.Operator.ConstantSource(new Flt64(2.0)))
            .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(), Parents(), new BoundedStubModel<object?>(2, -10.0, 10.0),
                IIntoValue<Flt64>.Identity, () => new Flt64(random.NextDouble()))
            .SelectMany(child => child.Values)
            .Should().OnlyContain(value => value.ToDouble() >= -10.0 && value.ToDouble() <= 10.0);
    }

    /// <summary>构造父代组 / Build a parent group.</summary>
    /// <returns>父代组 / The parent group.</returns>
    private static EvaluatedIndividual<Flt64, Flt64>[] Parents() => new[] {
        new EvaluatedIndividual<Flt64, Flt64>(new[] { new Flt64(0.0), new Flt64(0.0) }, new Flt64(0.0)),
        new EvaluatedIndividual<Flt64, Flt64>(new[] { new Flt64(5.0), new Flt64(5.0) }, new Flt64(1.0)),
    };

    /// <summary>构造熵源 / Build the entropy source.</summary>
    /// <param name="seed">种子 / The seed.</param>
    /// <returns>均匀熵源 / The uniform entropy source.</returns>
    private static Func<Flt64> Entropy(long seed) {
        var random = new SearchRandom(seed);
        return () => new Flt64(random.NextDouble());
    }
}

/// <summary>
/// 多元宇宙更新 / The multi-verse update.
/// </summary>
public sealed class MultiVerseUpdateTests {
    /// <summary>**虫洞概率为 0 且膨胀不足时个体原地不动** / A zero wormhole rate with no inflation leaves individuals in place.</summary>
    /// <remarks>
    /// `wormhole = 0`、`travelling = 0`，且把 `inflation` 压到最小（排名靠后的个体 `inflation` 很小），于是绝大多数抽样落到"原地"分支。
    /// 这条同时说明虫洞偏移确实乘了 `travelling`——若漏乘，即使 `travelling = 0` 也会移动。
    /// With `wormhole = 0` and `travelling = 0`, and `inflation` at its minimum for the last individuals, nearly every draw lands in the
    /// "stand still" branch. This also shows the wormhole offset really multiplies by `travelling` — if that multiplication were missing,
    /// the value would move even with `travelling = 0`.
    /// </remarks>
    [Fact]
    public void MultiVerse_ShouldNotMoveWhenRatesAreZero() {
        var update = new MultiVerseUpdate(
            new Fuookami.Ospf.Core.Solver.Heuristic.Operator.ConstantSource(Flt64.Zero),
            new Fuookami.Ospf.Core.Solver.Heuristic.Operator.ConstantSource(Flt64.Zero));
        EvaluatedIndividual<Flt64, Flt64>[] parents = {
            new(new[] { new Flt64(5.0) }, new Flt64(0.0)),
        };

        IReadOnlyList<Solution<Flt64>> children = update
            .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(), parents, new StubModel(value => value[0]),
                IIntoValue<Flt64>.Identity, Entropy(3));

        children[0].Values[0].ToDouble().Should().Be(5.0);
    }

    /// <summary>**捐赠者按排名加权，最优个体被选中的频率显著高于最差** / Rank weighting favours the best donor.</summary>
    /// <remarks>
    /// 权重是 `size - rank`，所以最优个体（`rank = 0`）被选中的概率是最后一名（`rank = size-1`）的 `size` 倍。
    /// 改成均匀抽样后算法仍然运行、日志也仍然正常，但"向最优个体学习"这一核心机制消失了。
    /// The weight is `size - rank`, so rank 0 is `size` times more likely than the last. Uniform sampling still runs and still logs
    /// ordinary behaviour, but the "learn from the best" mechanism is gone.
    /// </remarks>
    [Fact]
    public void MultiVerse_ShouldFavourBestDonor() {
        // 前两个个体在第 0 维上是 0 与 100，用较大的 wormhole=0 与 travelling=1 让膨胀分支主导，再统计子代取值分布。
        // The first two individuals differ at dimension 0 (0 vs 100); wormhole 0 and travelling 1 let inflation dominate, then the
        // distribution of the children's values is tallied.
        var update = new MultiVerseUpdate(
            new Fuookami.Ospf.Core.Solver.Heuristic.Operator.ConstantSource(Flt64.Zero),
            new Fuookami.Ospf.Core.Solver.Heuristic.Operator.ConstantSource(Flt64.One));
        var parents = new EvaluatedIndividual<Flt64, Flt64>[2];
        for (int index = 0; index < 2; index++) {
            parents[index] = new EvaluatedIndividual<Flt64, Flt64>(
                new[] { new Flt64(index * 100.0) }, new Flt64(index));
        }

        int best = 0;
        int worst = 0;
        for (long seed = 0; seed < 200; seed++) {
            var random = new SearchRandom(seed);
            IReadOnlyList<Solution<Flt64>> children = update
                .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                    new Iteration(), parents, new StubModel(value => value[0]),
                    IIntoValue<Flt64>.Identity, () => new Flt64(random.NextDouble()));
            // **只看第一个个体**：`inflation = (size - 0) / size = 1.0`，膨胀分支覆盖整个 `[0, 1)`，
            // 于是抽样必进捐赠者分支，"原地"分支不会污染计数。
            // 早先用的是最后一个个体（`inflation = 0.5`），而 `second >= 0.5` 的"原地"分支会产出 `parents[1]` 的值 100，
            // 被误计为"最差捐赠者"——统计出 58 : 142 的假象，实际捐赠者分布是 2/3 : 1/3。
            // **Only the first individual**: `inflation = (size - 0) / size = 1.0`, so the inflation branch covers the whole
            // `[0, 1)` and every draw reaches the donor selection; the "stand still" branch cannot pollute the tally.
            // The earlier version used the last individual (`inflation = 0.5`), and the `second >= 0.5` "stand still" branch emits
            // `parents[1]`'s value 100, which was mis-counted as "worst donor" — producing a spurious 58 : 142 while the true donor
            // split is 2/3 : 1/3.
            double value = children[0].Values[0].ToDouble();
            if (global::System.Math.Abs(value) < 1e-9) {
                best++;
            } else if (global::System.Math.Abs(value - 100.0) < 1e-9) {
                worst++;
            }
        }

        best.Should().BeGreaterThan(worst,
            "排名加权应让最优个体更常被选为捐赠者 / rank weighting should favour the best donor");
    }

    /// <summary>构造熵源 / Build the entropy source.</summary>
    /// <param name="seed">种子 / The seed.</param>
    /// <returns>均匀熵源 / The uniform entropy source.</returns>
    private static Func<Flt64> Entropy(long seed) {
        var random = new SearchRandom(seed);
        return () => new Flt64(random.NextDouble());
    }
}
