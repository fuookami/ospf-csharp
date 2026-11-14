// 二线更新算子测试 / Tests for the tier-2 update operators.

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
/// 二线更新算子 / The tier-2 update operators.
/// </summary>
public sealed class Tier2UpdateTests {
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

    /// <summary>**带边界时被夹到区间内** / Children are clamped into the bounds.</summary>
    [Fact]
    public void Update_ShouldClampIntoBounds() {
        foreach (ISolutionUpdate update in AllUpdates()) {
            var random = new SearchRandom(3);
            update.Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                    new Iteration(), Parents(6), new BoundedStubModel<object?>(2, -10.0, 10.0),
                    IIntoValue<Flt64>.Identity, () => new Flt64(random.NextDouble()))
                .SelectMany(child => child.Values)
                .Should().OnlyContain(value => value.ToDouble() >= -10.0 && value.ToDouble() <= 10.0);
        }
    }

    /// <summary>列出全部二线算子 / Enumerate the tier-2 operators.</summary>
    /// <returns>算子集合 / The operator set.</returns>
    private static IEnumerable<ISolutionUpdate> AllUpdates() => new ISolutionUpdate[] {
        new TeachingLearningUpdate(),
        LearnerPhaseUpdate.Instance,
        new SalpSwarmUpdate(),
        new GravitationalSearchUpdate(),
        new FireflyUpdate(),
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
                new[] { new Flt64(index * 2.0), new Flt64(index * 2.0 + 1.0) }, new Flt64(index)))
            .ToArray();
}

/// <summary>
/// 教学因子舍入 / The teaching-factor rounding.
/// </summary>
public sealed class TeachingLearningUpdateTests {
    /// <summary>**教学因子使用 half-up 舍入，而不是银行家舍入** / The teaching factor rounds half-up, not to even.</summary>
    /// <remarks>
    /// Kotlin 的 `Math.round` 是 half-up，C# 的 `Math.Round` 默认是**银行家舍入**（`0.5 → 0`、`1.5 → 2`）。教学因子是
    /// `1 + round(unit * range)`，两种舍入产出不同的整数序列，同一份种子的轨迹随之分叉，**而算法看起来仍然正常工作**。
    ///
    /// 构造：熵源恒返回 0.25、`range = 2.0` → `0.25 * 2 = 0.5`。half-up 得 1（因子 2），银行家舍入得 0（因子 1）。
    /// 教师取 10、均值也是 10（两个父代都在 10），于是 `step = unit * (10 - factor*10)`：
    /// 因子 2 → `0.25 * (10 - 20) = -2.5`；因子 1 → `0.25 * (10 - 10) = 0`。
    /// 子代位置因此差 2.5，足以区分。
    /// Kotlin's `Math.round` is half-up while C#'s `Math.Round` defaults to **banker's rounding** (`0.5 -> 0`, `1.5 -> 2`). The factor is
    /// `1 + round(unit * range)`, so the two roundings produce different integer sequences and the same seed diverges **while the
    /// algorithm still looks fine**.
    ///
    /// Setup: constant entropy 0.25 with `range = 2.0` gives `0.25 * 2 = 0.5`. Half-up yields 1 (factor 2); banker's rounding yields 0
    /// (factor 1). The teacher and the mean are both 10, so `step = unit * (10 - factor*10)`: factor 2 gives `0.25 * -10 = -2.5`, factor
    /// 1 gives `0`. The children differ by 2.5, which is easy to distinguish.
    /// </remarks>
    [Fact]
    public void TeachingLearning_ShouldRoundHalfAwayFromZero() {
        EvaluatedIndividual<Flt64, Flt64>[] parents = {
            new(new[] { new Flt64(10.0) }, new Flt64(0.0)),
        };

        IReadOnlyList<Solution<Flt64>> children = new TeachingLearningUpdate(2.0)
            .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(), parents, new StubModel(value => value[0]),
                IIntoValue<Flt64>.Identity, () => new Flt64(0.25));

        children[0].Values[0].ToDouble().Should().BeApproximately(7.5, 1e-12);
    }

    /// <summary>**单个体种群也能更新** / A single-individual population still updates.</summary>
    [Fact]
    public void TeachingLearning_ShouldHandleSingleParent() {
        var random = new SearchRandom(2);

        new TeachingLearningUpdate()
            .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(),
                new[] { new EvaluatedIndividual<Flt64, Flt64>(new[] { new Flt64(3.0) }, new Flt64(0.0)) },
                new StubModel(value => value[0]), IIntoValue<Flt64>.Identity,
                () => new Flt64(random.NextDouble()))
            .Should().ContainSingle();
    }
}

/// <summary>
/// 学习阶段的伙伴选择 / The learner-phase partner choice.
/// </summary>
public sealed class LearnerPhaseUpdateTests {
    /// <summary>**只有一个个体的种群原样返回** / A single-individual population is returned unchanged.</summary>
    [Fact]
    public void LearnerPhase_ShouldPassThroughSingleParent() {
        EvaluatedIndividual<Flt64, Flt64>[] parents = {
            new(new[] { new Flt64(3.0) }, new Flt64(0.0)),
        };

        IReadOnlyList<Solution<Flt64>> children = LearnerPhaseUpdate.Instance
            .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(), parents, new StubModel(value => value[0]),
                IIntoValue<Flt64>.Identity, Entropy(1));

        children[0].Values[0].ToDouble().Should().Be(3.0);
    }

    /// <summary>**抽到自己时换人，个体不会原地不动** / A self-draw switches partners so the individual moves.</summary>
    /// <remarks>
    /// 若允许"自己当伙伴"，`partner - current` 恒为 0，该个体原地不动——**而"学习阶段"的名字让人以为它在学习**。
    /// 两个个体内容不同时，无论抽到谁，子代都应与原值不同。
    /// Allowing a self-partner makes `partner - current` always 0 and the individual stands still — **while the name "learner phase"
    /// suggests it is learning**. With two distinct individuals, the child differs from the original whichever partner is drawn.
    /// </remarks>
    [Fact]
    public void LearnerPhase_ShouldMoveEveryIndividual() {
        EvaluatedIndividual<Flt64, Flt64>[] parents = {
            new(new[] { new Flt64(0.0) }, new Flt64(0.0)),
            new(new[] { new Flt64(10.0) }, new Flt64(1.0)),
        };

        IReadOnlyList<Solution<Flt64>> children = LearnerPhaseUpdate.Instance
            .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(), parents, new StubModel(value => value[0]),
                IIntoValue<Flt64>.Identity, Entropy(4));

        // 熵源 4 的首抽可能命中自己；换人后必然移动。
        // The first draw under seed 4 may hit self; after the switch the individual must move.
        children.Select(child => child.Values[0].ToDouble())
            .Should().NotEqual(parents.Select(parent => parent.Solution[0].ToDouble()));
    }

    /// <summary>构造熵源 / Build the entropy source.</summary>
    /// <param name="seed">种子 / The seed.</param>
    /// <returns>均匀熵源 / The uniform entropy source.</returns>
    private static Func<Flt64> Entropy(long seed) {
        var random = new SearchRandom(seed);
        return () => new Flt64(random.NextDouble());
    }
}

/// <summary>
/// 引力搜索的质量与软化项 / Masses and the softening term in gravitational search.
/// </summary>
public sealed class GravitationalSearchUpdateTests {
    /// <summary>**坐标完全相同的个体不会产生 `NaN`** / Identical coordinates never produce `NaN`.</summary>
    /// <remarks>
    /// 两个个体坐标完全相同时 `distance == 0`，除以 0 会得到 `NaN` 或 `∞`；`softness`（默认 `1e-6`）是唯一的防线，
    /// 而 `ClampTo` 会把 `NaN` 折算成边界——**整代被钉在边界上，看起来只是"收敛得快"**。
    /// Identical coordinates give `distance == 0` and dividing by 0 yields `NaN` or `∞`; `softness` (default `1e-6`) is the only guard,
    /// and `ClampTo` folds `NaN` onto a bound — **the whole generation is pinned to the boundary and merely looks like "fast
    /// convergence"**.
    /// </remarks>
    [Fact]
    public void GravitationalSearch_ShouldNotProduceNaNOnIdenticalIndividuals() {
        EvaluatedIndividual<Flt64, Flt64>[] parents = {
            new(new[] { new Flt64(5.0), new Flt64(5.0) }, new Flt64(0.0)),
            new(new[] { new Flt64(5.0), new Flt64(5.0) }, new Flt64(1.0)),
        };
        var random = new SearchRandom(2);

        IReadOnlyList<Solution<Flt64>> children = new GravitationalSearchUpdate()
            .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(), parents, new StubModel(value => value[0]),
                IIntoValue<Flt64>.Identity, () => new Flt64(random.NextDouble()));

        // **断言"恰好不变"而不是"值是有限数"**。有 `softness` 时分子也是 0（`delta == 0`），所以加速度为 0、位置保持 5.0；
        // 去掉 `softness` 后是 `0/0 = NaN`，而 `ClampTo` 会把它折算成 0.0——`double.IsFinite(0.0)` 为真，第一版断言因此
        // 在变异下仍然通过。断言具体值才能区分。
        // **Assert "exactly unchanged" rather than "the value is finite".** With `softness` the numerator is also 0 (since `delta == 0`),
        // so the acceleration is 0 and the position stays 5.0; without it the expression is `0/0 = NaN`, which `ClampTo` folds to 0.0 —
        // and `double.IsFinite(0.0)` is true, so the first version still passed under mutation. Asserting the concrete value distinguishes
        // the two.
        children.SelectMany(child => child.Values)
            .Should().OnlyContain(value => value.ToDouble() == 5.0);
    }

    /// <summary>**优者引力更大：质量按排名加权** / Better ranks pull harder thanks to rank-weighted masses.</summary>
    /// <remarks>
    /// 改成均匀质量会让"优者引力更大"这一核心机制消失，而加速度数值仍在合理范围内，**看不出任何异常**。
    /// Uniform masses remove the "better individuals pull harder" mechanism while the acceleration values stay plausible — **nothing looks
    /// wrong**.
    /// </remarks>
    [Fact]
    public void GravitationalSearch_ShouldWeightMassesByRank() {
        // 三个一维父代：位置 0（第 0 名）、10（第 1 名）、10（第 2 名），引力常数 1、软化 1e-6。
        // 质量按排名：总和 `3*4/2 = 6` → `m0 = 0.5`、`m1 = 1/3`、`m2 = 1/6`。
        // 对第 1 名：来自第 0 名的加速度是 `1 * 0.5 * (0-10)/10 = -0.5`；来自第 2 名的距离为 0、分子也为 0，贡献 0。
        // 于是位置从 10 变为 **9.5**。均匀质量（各 1/3）会给出 `1 * (1/3) * (-1) = -1/3`，即 9.6667——两者可区分。
        //
        // Three one-dimensional parents at 0 (rank 0), 10 (rank 1) and 10 (rank 2), constant 1, softness 1e-6.
        // Rank-weighted masses: total `3*4/2 = 6` gives `m0 = 0.5`, `m1 = 1/3`, `m2 = 1/6`. For rank 1 the acceleration from rank 0 is
        // `1 * 0.5 * (0-10)/10 = -0.5`, and rank 2 contributes 0 (zero distance and zero numerator), so the position becomes **9.5**.
        // Uniform masses (1/3 each) would give `-1/3`, i.e. 9.6667 — clearly distinguishable.
        EvaluatedIndividual<Flt64, Flt64>[] parents = {
            new(new[] { new Flt64(0.0) }, new Flt64(0.0)),
            new(new[] { new Flt64(10.0) }, new Flt64(1.0)),
            new(new[] { new Flt64(10.0) }, new Flt64(2.0)),
        };
        var random = new SearchRandom(1);

        IReadOnlyList<Solution<Flt64>> children = new GravitationalSearchUpdate(
                new Fuookami.Ospf.Core.Solver.Heuristic.Operator.ConstantSource(Flt64.One), 1.0e-6)
            .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(), parents, new StubModel(value => value[0]),
                IIntoValue<Flt64>.Identity, () => new Flt64(random.NextDouble()));

        children[1].Values[0].ToDouble().Should().BeApproximately(9.5, 1e-6);
    }
}

/// <summary>
/// 萤火虫的伙伴选择 / The firefly partner choice.
/// </summary>
public sealed class FireflyUpdateTests {
    /// <summary>**最优个体不向任何人靠拢，只做抖动** / The best individual only jitters.</summary>
    [Fact]
    public void Firefly_ShouldOnlyJitterForBest() {
        EvaluatedIndividual<Flt64, Flt64>[] parents = {
            new(new[] { new Flt64(0.0) }, new Flt64(0.0)),
            new(new[] { new Flt64(10.0) }, new Flt64(1.0)),
        };
        var random = new SearchRandom(3);

        IReadOnlyList<Solution<Flt64>> children = new FireflyUpdate(1.0, 1.0, 0.2)
            .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(), parents, new StubModel(value => value[0]),
                IIntoValue<Flt64>.Identity, () => new Flt64(random.NextDouble()));

        // 最优个体的伙伴就是自己，pull = attraction * 0 = 0，只剩抖动（幅度 ≤ 0.2 * 1.0 * 0.5 = 0.1）。
        // The best's partner is itself, so pull = 0 and only the jitter remains (magnitude at most 0.2 * 1.0 * 0.5 = 0.1).
        global::System.Math.Abs(children[0].Values[0].ToDouble()).Should().BeLessThanOrEqualTo(0.1 + 1e-12);
    }

    /// <summary>**非最优个体向更优者靠拢** / A non-best individual is pulled toward a better one.</summary>
    [Fact]
    public void Firefly_ShouldPullTowardBetter() {
        // **两个父代必须靠得足够近**：相距 10 时 `exp(-100) ≈ 4e-44`，引力远低于双精度可分辨度，子代算出来仍是 10.0。
        // 第一版夹具正是相距 10，断言 `小于 10` 因此失败——不是实现的问题，是夹具把可观测效应淹没在浮点精度里。
        // **The two parents must be close enough**: at a distance of 10, `exp(-100) ≈ 4e-44` is far below double precision and the child
        // still computes to exactly 10.0. The first fixture used a distance of 10 and the "less than 10" assertion failed — not an
        // implementation issue, but a fixture that drowned the observable effect in floating-point precision.
        EvaluatedIndividual<Flt64, Flt64>[] parents = {
            new(new[] { new Flt64(9.0) }, new Flt64(0.0)),
            new(new[] { new Flt64(10.0) }, new Flt64(1.0)),
        };
        var random = new SearchRandom(3);

        IReadOnlyList<Solution<Flt64>> children = new FireflyUpdate(1.0, 1.0, 0.0)
            .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(), parents, new StubModel(value => value[0]),
                IIntoValue<Flt64>.Identity, () => new Flt64(random.NextDouble()));

        // 抖动为 0 时，个体 1 只会朝 0 移动，位置必然减小。
        // With zero jitter, individual 1 can only move toward 0, so its position decreases.
        children[1].Values[0].ToDouble().Should().BeLessThan(10.0);
    }
}
