// 小生境更新算子测试 / Tests for niche-flavoured update operators.

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
/// 小生境类更新算子 / Niche-flavoured update operators.
/// </summary>
public sealed class NicheUpdateTests {
    /// <summary>**每个父代产一个子代且维数不变** / Each parent yields one child of the same dimension.</summary>
    [Fact]
    public void Update_ShouldPreserveCountAndDimension() {
        foreach (ISolutionUpdate update in AllUpdates()) {
            IReadOnlyList<Solution<Flt64>> children = Run(update, Parents(5), seed: 2);

            children.Should().HaveCount(5);
            children.Should().OnlyContain(child => child.Values.Count == 3);
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
            Flatten(Run(update, Parents(4), seed: 5)).Should().Equal(Flatten(Run(update, Parents(4), seed: 5)));
        }
    }

    /// <summary>**带边界时子代被夹到区间内** / Children are clamped into the bounds.</summary>
    [Fact]
    public void Update_ShouldClampIntoBounds() {
        foreach (ISolutionUpdate update in AllUpdates()) {
            var random = new SearchRandom(3);
            IReadOnlyList<Solution<Flt64>> children = update
                .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                    new Iteration(), Parents(6), new BoundedStubModel<object?>(3, -10.0, 10.0),
                    IIntoValue<Flt64>.Identity, () => new Flt64(random.NextDouble()));

            children.SelectMany(child => child.Values)
                .Should().OnlyContain(value => value.ToDouble() >= -10.0 && value.ToDouble() <= 10.0);
        }
    }

    /// <summary>列出全部小生境算子 / Enumerate every niche operator.</summary>
    private static IEnumerable<ISolutionUpdate> AllUpdates() => new ISolutionUpdate[] {
        new WarStrategyUpdate(),
        new EnergyValleyUpdate(),
        new GerminalCenterUpdate(),
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
            new Iteration(), parents, Model(), IIntoValue<Flt64>.Identity,
            () => new Flt64(random.NextDouble()));
    }

    /// <summary>摊平子代数值 / Flatten child values.</summary>
    /// <param name="children">子代 / The children.</param>
    /// <returns>摊平后的数值 / The flattened values.</returns>
    private static IEnumerable<double> Flatten(IReadOnlyList<Solution<Flt64>> children) =>
        children.SelectMany(child => child.Values).Select(value => value.ToDouble());

    /// <summary>构造模型 / Build the model.</summary>
    private static StubModel Model() => new(value => value[0]);

    /// <summary>构造父代组（含零向量，便于观测发散）/ Build parents including a zero vector for divergence checks.</summary>
    /// <param name="count">父代数量 / The parent count.</param>
    /// <returns>父代组 / The parent group.</returns>
    private static EvaluatedIndividual<Flt64, Flt64>[] Parents(int count) =>
        Enumerable.Range(0, count)
            .Select(index => new EvaluatedIndividual<Flt64, Flt64>(
                Enumerable.Range(0, 3).Select(dim => new Flt64(index + dim)).ToList(),
                new Flt64(index)))
            .ToArray();
}

/// <summary>
/// 战争策略的发散防护 / The war-strategy divergence guard.
/// </summary>
public sealed class WarStrategyUpdateTests {
    /// <summary>**纯防守保持在父代凸包内** / Pure defence stays inside the parents' convex hull.</summary>
    /// <remarks>
    /// 防守的步长是 `scale * unit * (target - current)`，`unit ∈ [0,1)`。当 `scale = 1` 时新值必然落在 `current` 与 `target` 之间，
    /// 而两者都是父代中的个体，所以**每个分量都不会越出父代该分量的 `[min, max]` 区间**——凸包不变性。
    /// 当 `scale = 2` 时新值最远可越过 `target` 一整个 `(target - current)`，**凸包立刻被打破**。
    ///
    /// 这条测试是**确定性的、可证伪的**，已用变异测试验证：把防守的 `scale` 从 `1.0` 改成 `StrategyScale`（= 2.0），测试随即失败。
    ///
    /// 早先写的"连续 20 代模长不超过 100"是**恒真**的：随机方向的 2 倍外推是随机游走而非复利增长，模长并不逐代翻倍，
    /// 两种实现都能通过——变异测试直接暴露了这一点。
    /// The defence step is `scale * unit * (target - current)` with `unit ∈ [0,1)`. For `scale = 1` the new value necessarily lies
    /// between `current` and `target`, both of which are parents, so **no component leaves the parents' `[min, max]` range for that
    /// component** — convex-hull invariance. For `scale = 2` the new value can overshoot `target` by a full `(target - current)`,
    /// **breaking the hull immediately**.
    ///
    /// This test is **deterministic and falsifiable**, verified by mutation testing: changing the defence `scale` from `1.0` to
    /// `StrategyScale` (= 2.0) makes it fail.
    ///
    /// The earlier version — "the mean magnitude stays below 100 over 20 generations" — was **vacuous**: a 2x extrapolation in random
    /// directions is a random walk, not compounding growth, so the magnitude does not double per generation and both implementations
    /// passed. The mutation test exposed exactly that.
    /// </remarks>
    [Fact]
    public void WarStrategy_ShouldStayInsideConvexHullUnderPureDefence() {
        var update = new WarStrategyUpdate(AttackRate: 0.0, StrategyScale: 2.0);
        var random = new SearchRandom(17);
        EvaluatedIndividual<Flt64, Flt64>[] parents = {
            new(new[] { new Flt64(0.0), new Flt64(0.0) }, new Flt64(0.0)),
            new(new[] { new Flt64(1.0), new Flt64(0.5) }, new Flt64(1.0)),
            new(new[] { new Flt64(2.0), new Flt64(1.0) }, new Flt64(2.0)),
        };

        IReadOnlyList<Solution<Flt64>> children = update
            .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(), parents, new StubModel(value => value[0]), IIntoValue<Flt64>.Identity,
                () => new Flt64(random.NextDouble()));

        for (int dim = 0; dim < 2; dim++) {
            double low = parents.Min(parent => parent.Solution[dim].ToDouble());
            double high = parents.Max(parent => parent.Solution[dim].ToDouble());
            foreach (Solution<Flt64> child in children) {
                child.Values[dim].ToDouble().Should().BeInRange(low, high,
                    "scale = 1 的防守步长不能越出父代凸包 / a scale-1 defence step cannot leave the parents' hull");
            }
        }
    }

    /// <summary>计算全体父代的平均模长 / Compute the mean magnitude of all parents.</summary>
    /// <param name="solutions">解集合 / The solutions.</param>
    /// <returns>平均模长 / The mean magnitude.</returns>
    private static double Magnitude(IReadOnlyList<IReadOnlyList<Flt64>> solutions) =>
        solutions.Count == 0
            ? 0.0
            : solutions.Average(solution => global::System.Math.Sqrt(
                solution.Sum(value => value.ToDouble() * value.ToDouble())));
}
