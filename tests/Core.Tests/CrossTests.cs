// 交叉算子测试 / Tests for crossover operators.

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
/// 交叉算子 / Crossover operators.
/// </summary>
public sealed class CrossTests {
    /// <summary>**交叉恰好产出两个子代** / A crossover yields exactly two children.</summary>
    /// <remarks>
    /// 与"每个父代产一个子代"的更新算子不同。调用方按父代数量推算子代数量时，奇数父代会拿到比预期少一个的结果，而种群规模看起来仍正常。
    /// Unlike update operators that yield one child per parent. A caller deriving the child count from the parent count gets one fewer
    /// for an odd parent count while the population size still looks right.
    /// </remarks>
    [Fact]
    public void Cross_ShouldYieldExactlyTwoChildren() {
        foreach (ICross cross in AllCrosses()) {
            cross.Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                    new Iteration(), Parents(5), Model(), IIntoValue<Flt64>.Identity, Entropy(1))
                .Should().HaveCount(2);
        }
    }

    /// <summary>**少于两个父代不产生子代** / Fewer than two parents yield nothing.</summary>
    [Fact]
    public void Cross_ShouldRequireTwoParents() {
        foreach (ICross cross in AllCrosses()) {
            cross.Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                    new Iteration(), Parents(1), Model(), IIntoValue<Flt64>.Identity, Entropy(1))
                .Should().BeEmpty();
            cross.Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                    new Iteration(), Array.Empty<EvaluatedIndividual<Flt64, Flt64>>(),
                    Model(), IIntoValue<Flt64>.Identity, Entropy(1))
                .Should().BeEmpty();
        }
    }

    /// <summary>**维数不一致时原样返回** / Mismatched dimensions are returned unchanged.</summary>
    /// <remarks>
    /// 强行按较短的一侧交叉会静默截断解向量，之后每次评估都在一个缺维的解上进行。
    /// Crossing over the shorter side silently truncates the solution vector and every later evaluation runs on a deficient one.
    /// </remarks>
    [Fact]
    public void Cross_ShouldRejectMismatchedDimensions() {
        EvaluatedIndividual<Flt64, Flt64>[] parents = {
            new(new[] { new Flt64(1.0), new Flt64(2.0), new Flt64(3.0) }, new Flt64(0.0)),
            new(new[] { new Flt64(4.0) }, new Flt64(1.0)),
        };

        foreach (ICross cross in AllCrosses()) {
            IReadOnlyList<Solution<Flt64>> children = cross
                .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                    new Iteration(), parents, Model(), IIntoValue<Flt64>.Identity, Entropy(2));

            children.Select(child => child.Values.Count).Should().Equal(3, 1);
        }
    }

    /// <summary>**相同种子完全可重放** / Crossovers replay exactly under the same seed.</summary>
    [Fact]
    public void Cross_ShouldReplayUnderSameSeed() {
        foreach (ICross cross in AllCrosses()) {
            IReadOnlyList<Solution<Flt64>> first = cross
                .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                    new Iteration(), Parents(3), Model(), IIntoValue<Flt64>.Identity, Entropy(7));
            IReadOnlyList<Solution<Flt64>> second = cross
                .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                    new Iteration(), Parents(3), Model(), IIntoValue<Flt64>.Identity, Entropy(7));

            Flatten(first).Should().Equal(Flatten(second));
        }
    }

    /// <summary>**带边界时算术与混合交叉被夹到区间内** / Arithmetic and blend crossovers clamp into the bounds.</summary>
    [Fact]
    public void Cross_ShouldClampIntoBounds() {
        foreach (ICross cross in new ICross[] { ArithmeticCross.Instance, new BlendCross(new Flt64(10.0)) }) {
            IReadOnlyList<Solution<Flt64>> children = cross
                .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                    new Iteration(), Parents(2), new BoundedStubModel<object?>(3, -10.0, 10.0),
                    IIntoValue<Flt64>.Identity, Entropy(4));

            children.SelectMany(child => child.Values)
                .Should().OnlyContain(value => value.ToDouble() >= -10.0 && value.ToDouble() <= 10.0);
        }
    }

    /// <summary>列出全部交叉算子 / Enumerate every crossover operator.</summary>
    private static IEnumerable<ICross> AllCrosses() => new ICross[] {
        SinglePointCross.Instance,
        TwoPointCross.Instance,
        new MultiPointCross(),
        new UniformCross(),
        ArithmeticCross.Instance,
        new BlendCross(),
    };

    /// <summary>摊平子代数值便于比较 / Flatten child values for comparison.</summary>
    /// <param name="children">子代 / The children.</param>
    /// <returns>摊平后的数值 / The flattened values.</returns>
    private static IEnumerable<double> Flatten(IReadOnlyList<Solution<Flt64>> children) =>
        children.SelectMany(child => child.Values).Select(value => value.ToDouble());

    /// <summary>构造模型 / Build the model.</summary>
    private static StubModel Model() => new(value => value[0]);

    /// <summary>构造两个维数 3 的父代 / Build two parents of dimension 3.</summary>
    /// <param name="count">父代数量 / The parent count.</param>
    /// <returns>父代组 / The parent group.</returns>
    private static EvaluatedIndividual<Flt64, Flt64>[] Parents(int count) =>
        Enumerable.Range(0, count)
            .Select(index => new EvaluatedIndividual<Flt64, Flt64>(
                new[] { new Flt64(index), new Flt64(index + 10), new Flt64(index + 20) },
                new Flt64(index)))
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
/// 两点交叉的退化情形 / The degenerate two-point case.
/// </summary>
public sealed class TwoPointCrossTests {
    /// <summary>**两个切点相同时仍然发生交叉** / Coincident cut points still produce a crossover.</summary>
    /// <remarks>
    /// `start == end` 时若不做区间扩展，循环体一次都不执行，交叉退化成"原样返回"——**子代就是父代本身，种群停止演化**，
    /// 而调用方从返回值上看不出任何异常。这条测试遍历大量种子，断言**至少有一次**子代与父代不同：若区间扩展缺失，
    /// 所有切点相同的情形都会原样返回，而切点相同的概率约为 `1 / 维数`，遍历 200 个种子必然覆盖到。
    /// Without the interval expansion, `start == end` makes the loop body run zero times and the crossover degrades into "return the
    /// inputs unchanged" — **the children are literally the parents and the population stops evolving**, invisible from the return
    /// value. This test walks many seeds and asserts that **at least one** child differs from its parent: if the expansion were
    /// missing, every coincident-cut case would return the inputs, and since that happens with probability roughly `1 / dimension`,
    /// 200 seeds cover it.
    /// </remarks>
    [Fact]
    public void TwoPoint_ShouldCrossEvenWhenCutsCoincide() {
        // **强制两个切点相同**：熵源恒返回 0.5，两次抽样都落在 `(int)(0.5 * 4) = 2`。于是 `start == max == 2`，
        // 若不做区间扩展，循环体一次都不执行，子代与父代完全相同。这条是**确定性的可证伪断言**，不靠概率。
        // 早先写的是"遍历 200 个种子、至少有一次子代互不相同"——错了两处：一是拿两个子代互比（父代是 0×4 与 1×4，任何交叉后两个孩子
        // 都互不相同，退化时也成立）；二是 200 个种子里其余约 3/4 本来就在正常交叉，`differing > 0` 恒真。
        // **Force the two cut points to coincide**: the entropy source always returns 0.5, so both draws land on `(int)(0.5 * 4) = 2`.
        // Then `start == max == 2`, and without the interval expansion the loop body runs zero times and the children equal their
        // parents. This is a **deterministic, falsifiable assertion** that does not rely on probability.
        // The earlier version said "walk 200 seeds, at least once the children differ from each other" — wrong twice: it compared the
        // two children (the parents are 0×4 and 1×4, so after any crossover the children differ, including in the degenerate case),
        // and roughly three quarters of those 200 seeds cross normally anyway, so `differing > 0` was a tautology.
        IReadOnlyList<Solution<Flt64>> children = TwoPointCross.Instance
            .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(), Parents(), Model(), IIntoValue<Flt64>.Identity,
                () => new Flt64(0.5));

        children[0].Values.Select(value => value.ToDouble())
            .Should().NotEqual(Parents()[0].Solution.Select(value => value.ToDouble()));
        children[1].Values.Select(value => value.ToDouble())
            .Should().NotEqual(Parents()[1].Solution.Select(value => value.ToDouble()));
        children.Should().OnlyContain(child => child.Values.Count == 4);
    }

    /// <summary>**子代保持解长度** / Children keep the solution length.</summary>
    [Fact]
    public void TwoPoint_ShouldPreserveLength() {
        IReadOnlyList<Solution<Flt64>> children = TwoPointCross.Instance
            .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(), Parents(), Model(), IIntoValue<Flt64>.Identity, Entropy(3));

        children.Should().OnlyContain(child => child.Values.Count == 4);
    }

    /// <summary>构造两个内容不同的父代 / Build two distinct parents.</summary>
    /// <returns>父代组 / The parent group.</returns>
    private static EvaluatedIndividual<Flt64, Flt64>[] Parents() => new[] {
        new EvaluatedIndividual<Flt64, Flt64>(
            new[] { new Flt64(0.0), new Flt64(0.0), new Flt64(0.0), new Flt64(0.0) }, new Flt64(0.0)),
        new EvaluatedIndividual<Flt64, Flt64>(
            new[] { new Flt64(1.0), new Flt64(1.0), new Flt64(1.0), new Flt64(1.0) }, new Flt64(1.0)),
    };

    /// <summary>构造模型 / Build the model.</summary>
    private static StubModel Model() => new(value => value[0]);

    /// <summary>构造熵源 / Build the entropy source.</summary>
    /// <param name="seed">种子 / The seed.</param>
    /// <returns>均匀熵源 / The uniform entropy source.</returns>
    private static Func<Flt64> Entropy(long seed) {
        var random = new SearchRandom(seed);
        return () => new Flt64(random.NextDouble());
    }
}
