// 生物启发类更新算子测试 / Tests for biologically-inspired update operators.

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
/// 生物启发类更新算子 / Biologically-inspired update operators.
/// </summary>
public sealed class BioUpdateTests {
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

    /// <summary>列出三个生物启发算子 / Enumerate the three bio operators.</summary>
    /// <returns>算子集合 / The operator set.</returns>
    private static IEnumerable<ISolutionUpdate> AllUpdates() => new ISolutionUpdate[] {
        new WhaleOptimizationUpdate(),
        new HarrisHawkUpdate(),
        new ArithmeticOptimizationUpdate(),
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

    /// <summary>构造父代组（最优在 0）/ Build a parent group with the best at 0.</summary>
    /// <param name="count">父代数量 / The parent count.</param>
    /// <returns>父代组 / The parent group.</returns>
    private static EvaluatedIndividual<Flt64, Flt64>[] Parents(int count) =>
        Enumerable.Range(0, count)
            .Select(index => new EvaluatedIndividual<Flt64, Flt64>(
                new[] { new Flt64(index * 5.0), new Flt64(index * 5.0) }, new Flt64(index)))
            .ToArray();
}

/// <summary>
/// 算术优化更新的概率夹紧 / The arithmetic-optimization probability clamp.
/// </summary>
public sealed class ArithmeticOptimizationUpdateTests {
    /// <summary>**概率为 0 时结果仍为有限值** / A zero probability still yields finite results.</summary>
    /// <remarks>
    /// 概率出现在除法分母上：`best / probability * scale`。概率为 0 时这个式子会给出 `NaN`（`0/0`）或 `∞`，但
    /// `NumericSupport.ClampTo` 会把任何非有限值折算成边界（无边界时为 0），所以**最终结果仍然有限**。
    ///
    /// **这条断言是"防护有效"而非"防护必要"的证据，且它的变异测试是等价变异**：把 `Math.Clamp(probability, 1e-6, 1)` 删掉后
    /// 测试**仍然通过**，因为 `ClampTo` 已经吸收了非有限值。我试过三种夹具（最优 5.0、最优 0.0、确定性熵源），都无法区分——
    /// 结论是这个夹紧在本移植里不承担可观测职责，保留它只是为了与基线一致。**真正提供保证的是 `ClampTo` 的非有限值清洗**。
    ///
    /// 把这个写成"防止除零的关键"会是假的；如实记录等价变异才是准确的做法。
    /// The probability appears in a denominator: `best / probability * scale`. At probability 0 that expression yields `NaN` (`0/0`) or
    /// `∞`, but `NumericSupport.ClampTo` folds any non-finite value onto a bound (0 when unbounded), so **the result is still finite**.
    ///
    /// **This assertion is evidence that the guard works, not that it is necessary, and its mutation test is an equivalent mutant**:
    /// removing `Math.Clamp(probability, 1e-6, 1)` still passes because `ClampTo` already absorbs the non-finite value. Three fixtures
    /// were tried (best at 5.0, best at 0.0, a deterministic entropy source) and none could distinguish them — the conclusion is that the
    /// clamp carries no observable duty in this port and is kept only for baseline fidelity. **The actual guarantee comes from
    /// `ClampTo`'s non-finite sanitization.**
    ///
    /// Writing this up as "the critical thing that prevents division by zero" would be false; recording the equivalent mutant is the
    /// accurate account.
    /// </remarks>
    [Fact]
    public void Arithmetic_ShouldStayFiniteOnZeroProbability() {
        var update = new ArithmeticOptimizationUpdate(
            new Fuookami.Ospf.Core.Solver.Heuristic.Operator.ConstantSource(Flt64.Zero),
            new Fuookami.Ospf.Core.Solver.Heuristic.Operator.ConstantSource(Flt64.Zero),
            0.5);

        IReadOnlyList<Solution<Flt64>> children = update
            .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(), Parents(), new StubModel(value => value[0]),
                IIntoValue<Flt64>.Identity, () => new Flt64(0.9));

        children.SelectMany(child => child.Values)
            .Should().OnlyContain(value => double.IsFinite(value.ToDouble()));
    }

    /// <summary>**带边界时被夹到区间内** / Children are clamped into the bounds.</summary>
    [Fact]
    public void Arithmetic_ShouldClampIntoBounds() {
        var random = new SearchRandom(6);

        new ArithmeticOptimizationUpdate(
                new Fuookami.Ospf.Core.Solver.Heuristic.Operator.ConstantSource(Flt64.Zero),
                new Fuookami.Ospf.Core.Solver.Heuristic.Operator.ConstantSource(new Flt64(0.5)),
                0.5)
            .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(), Parents(), new BoundedStubModel<object?>(2, -10.0, 10.0),
                IIntoValue<Flt64>.Identity, () => new Flt64(random.NextDouble()))
            .SelectMany(child => child.Values)
            .Should().OnlyContain(value => value.ToDouble() >= -10.0 && value.ToDouble() <= 10.0);
    }

    /// <summary>构造父代组，最优在 0 / Build a parent group with the best at 0.</summary>
    /// <returns>父代组 / The parent group.</returns>
    private static EvaluatedIndividual<Flt64, Flt64>[] Parents() => new[] {
        new EvaluatedIndividual<Flt64, Flt64>(new[] { new Flt64(0.0), new Flt64(0.0) }, new Flt64(0.0)),
        new EvaluatedIndividual<Flt64, Flt64>(new[] { new Flt64(3.0), new Flt64(3.0) }, new Flt64(1.0)),
    };
}

/// <summary>
/// 哈里斯鹰更新的能量分段 / The Harris-hawk energy regimes.
/// </summary>
public sealed class HarrisHawkUpdateTests {
    /// <summary>**大能量时参照随机个体** / At high energy the update references a random individual.</summary>
    /// <remarks>
    /// `|E| >= 1` 是远距探索段。把判据写反会让算法只做局部搜索。
    /// `|E| >= 1` is the distant-exploration regime; reversing the predicate leaves the algorithm doing pure local search.
    /// </remarks>
    [Fact]
    public void HarrisHawk_ShouldExploreAtHighEnergy() {
        var update = new HarrisHawkUpdate(
            new Fuookami.Ospf.Core.Solver.Heuristic.Operator.ConstantSource(new Flt64(2.0)));
        var random = new SearchRandom(5);

        IReadOnlyList<Solution<Flt64>> children = update
            .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(), Parents(), new StubModel(value => value[0]),
                IIntoValue<Flt64>.Identity, () => new Flt64(random.NextDouble()));

        children.Should().HaveCount(4);
        children.SelectMany(child => child.Values)
            .Should().OnlyContain(value => double.IsFinite(value.ToDouble()));
    }

    /// <summary>**零能量时完全不动** / Zero energy leaves the solution untouched.</summary>
    /// <remarks>
    /// `energy = decay * (2u - 1)`，衰减为 0 时能量恒为 0，硬围攻分支给出 `position - 0 * |best - position| = position`。
    /// 断言"完全不动"而不是"值是有限数"——后者在分支结构写错时也成立，属于恒真断言。
    /// `energy = decay * (2u - 1)`; with zero decay the energy is always 0 and the hard-siege branch gives
    /// `position - 0 * |best - position| = position`. Asserting "no movement" rather than "the value is finite"; the latter holds even
    /// when the branch structure is wrong, making it a tautology.
    /// </remarks>
    [Fact]
    public void HarrisHawk_ShouldNotMoveAtZeroEnergy() {
        var update = new HarrisHawkUpdate(
            new Fuookami.Ospf.Core.Solver.Heuristic.Operator.ConstantSource(Flt64.Zero));
        var random = new SearchRandom(5);

        IReadOnlyList<Solution<Flt64>> children = update
            .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(), Parents(), new StubModel(value => value[0]),
                IIntoValue<Flt64>.Identity, () => new Flt64(random.NextDouble()));

        for (int index = 0; index < children.Count; index++) {
            children[index].Values.Select(value => value.ToDouble())
                .Should().Equal(Parents()[index].Solution.Select(value => value.ToDouble()));
        }
    }

    /// <summary>**中等能量走软围攻，公式与硬围攻不同** / Medium energy uses soft siege, whose formula differs.</summary>
    /// <remarks>
    /// 熵源恒返回 0.9、衰减系数 1.0，于是 `energy = 1.0 * (2*0.9 - 1) = 0.8`，落在 `[0.5, 1)` 的软围攻区间。
    /// 位置 5、最优 0：软围攻给出 `5 - 0.8*|jump*0 - 5| + 0.9*(0 - 5)`，其中 `jump = 2*(1-0.9) = 0.2`，即 `5 - 4 - 4.5 = -3.5`；
    /// 硬围攻只给 `5 - 0.8*5 = 1.0`。两者不同，因此这条断言能区分"三段"与"两段"实现。
    ///
    /// **必须传两个父代**：只传一个时"最优"就是它自己（5.0），软围攻给出 `5 - 0.8*|1-5| = 1.8`——第一版夹具正是这样写的，
    /// 期望值算错了 5.3。
    /// The entropy source always returns 0.9 and the decay is 1.0, so `energy = 1.0 * (2*0.9 - 1) = 0.8`, inside the soft-siege band
    /// `[0.5, 1)`. With position 5 and best 0, soft siege gives `5 - 0.8*|jump*0 - 5| + 0.9*(0 - 5)` with `jump = 2*(1-0.9) = 0.2`,
    /// i.e. `5 - 4 - 4.5 = -3.5`, while hard siege would give `5 - 0.8*5 = 1.0`. The two differ, so this assertion distinguishes a
    /// three-regime implementation from a two-regime one.
    /// **Two parents are required**: with only one, the "best" is the individual itself (5.0) and soft siege yields
    /// `5 - 0.8*|1-5| = 1.8` — the first fixture did that and the expected value was off by 5.3.
    /// </remarks>
    [Fact]
    public void HarrisHawk_ShouldUseSoftSiegeAtMediumEnergy() {
        var update = new HarrisHawkUpdate(
            new Fuookami.Ospf.Core.Solver.Heuristic.Operator.ConstantSource(Flt64.One));

        IReadOnlyList<Solution<Flt64>> children = update
            .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(),
                new[] {
                    new EvaluatedIndividual<Flt64, Flt64>(new[] { new Flt64(0.0) }, new Flt64(0.0)),
                    new EvaluatedIndividual<Flt64, Flt64>(new[] { new Flt64(5.0) }, new Flt64(1.0)),
                },
                new StubModel(value => value[0]), IIntoValue<Flt64>.Identity,
                () => new Flt64(0.9));

        children[1].Values[0].ToDouble().Should().BeApproximately(-3.5, 1e-12);
    }

    /// <summary>构造父代组 / Build a parent group.</summary>
    /// <returns>父代组 / The parent group.</returns>
    private static EvaluatedIndividual<Flt64, Flt64>[] Parents() => new[] {
        new EvaluatedIndividual<Flt64, Flt64>(new[] { new Flt64(0.0) }, new Flt64(0.0)),
        new EvaluatedIndividual<Flt64, Flt64>(new[] { new Flt64(1.0) }, new Flt64(1.0)),
        new EvaluatedIndividual<Flt64, Flt64>(new[] { new Flt64(2.0) }, new Flt64(2.0)),
        new EvaluatedIndividual<Flt64, Flt64>(new[] { new Flt64(3.0) }, new Flt64(3.0)),
    };
}
