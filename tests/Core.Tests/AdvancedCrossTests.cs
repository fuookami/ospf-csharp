// 进阶交叉算子测试 / Tests for advanced crossover operators.

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
/// 模拟二进制交叉 / The simulated binary crossover.
/// </summary>
public sealed class SimulatedBinaryCrossTests {
    /// <summary>**两个子代的逐维和等于两个父代的逐维和** / The children's per-dimension sum equals the parents'.</summary>
    /// <remarks>
    /// SBX 的子代公式是 `0.5*((1+β)a + (1-β)b)` 与 `0.5*((1-β)a + (1+β)b)`，相加恰好等于 `a + b`——这是它的定义性质。
    ///
    /// **子代本来就可以越出父代区间**：`β &lt; 0` 时两个子代落在父代区间之外，这正是 SBX 用来扩大搜索范围的手段。
    /// 最初断言"子代在 `[0, 10]` 之间"（父代取 0 与 10），实测给出 `10.379…` 与 `-0.379…`——断言错在把 SBX 当成了线性插值。
    /// The SBX child formulas are `0.5*((1+β)a + (1-β)b)` and `0.5*((1-β)a + (1+β)b)`, which add up to exactly `a + b` — the defining
    /// property. **The children may legitimately leave the parents' interval**: with `β &lt; 0` both land outside, which is exactly how
    /// SBX widens the search. The first version asserted "children lie within `[0, 10]`" (parents at 0 and 10) and measured `10.379…`
    /// and `-0.379…` — the assertion was wrong for treating SBX as linear interpolation.
    /// </remarks>
    [Fact]
    public void SimulatedBinary_ShouldPreserveParentSum() {
        IReadOnlyList<Solution<Flt64>> children = new SimulatedBinaryCross()
            .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(), Parents(), new StubModel(value => value[0]),
                IIntoValue<Flt64>.Identity, Entropy(4));

        children.Should().HaveCount(2);
        for (int dim = 0; dim < 2; dim++) {
            double sum = children[0].Values[dim].ToDouble() + children[1].Values[dim].ToDouble();
            double expected = Parents()[0].Solution[dim].ToDouble() + Parents()[1].Solution[dim].ToDouble();
            sum.Should().BeApproximately(expected, 1e-9);
        }
    }

    /// <summary>**两个父代相同时不消耗随机数，子代与父代完全相同** / Equal parents consume no randomness and the children equal them.</summary>
    /// <remarks>
    /// 相等时若仍走公式，值虽然相同，但**消耗了一个随机数**，于是同一份种子的后续轨迹与"真的跳过"完全不同，重放对比会莫名失败。
    /// Running the formula on equal parents yields the same value, but **consumes a random draw**, so the rest of the trajectory under the
    /// same seed differs from "really skipping" and a replay comparison fails for no visible reason.
    /// </remarks>
    [Fact]
    public void SimulatedBinary_ShouldNotConsumeRandomnessOnEqualParents() {
        EvaluatedIndividual<Flt64, Flt64>[] same = {
            new EvaluatedIndividual<Flt64, Flt64>(new[] { new Flt64(5.0), new Flt64(5.0) }, new Flt64(0.0)),
            new EvaluatedIndividual<Flt64, Flt64>(new[] { new Flt64(5.0), new Flt64(5.0) }, new Flt64(1.0)),
        };

        // 一个"用掉就报错"的熵源：如果实现仍然抽样，这里会抛异常。
        // An entropy source that throws when touched: if the implementation still draws, this throws.
        IReadOnlyList<Solution<Flt64>> children = new SimulatedBinaryCross()
            .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(), same, new StubModel(value => value[0]), IIntoValue<Flt64>.Identity,
                () => throw new InvalidOperationException("相等维度不应消耗随机数 / equal dimensions must not draw"));

        children.SelectMany(child => child.Values)
            .Should().OnlyContain(value => value.ToDouble() == 5.0);
    }

    /// <summary>**负分布指数不会产生 `NaN`** / A negative distribution index never yields `NaN`.</summary>
    /// <remarks>
    /// 负指数会让 `beta` 的幂运算进入复数域，产出 `NaN`，而 `ClampTo` 会把它折算成边界——**整代解被钉在边界上，看起来只是"收敛得快"**。
    /// A negative exponent pushes the `beta` power into the complex domain and yields `NaN`, which `ClampTo` folds onto a bound —
    /// **the whole generation is pinned to the boundary and it merely looks like "fast convergence"**.
    /// </remarks>
    [Fact]
    public void SimulatedBinary_ShouldSurviveNegativeDistributionIndex() {
        IReadOnlyList<Solution<Flt64>> children = new SimulatedBinaryCross(new Flt64(-5.0))
            .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(), Parents(), new StubModel(value => value[0]),
                IIntoValue<Flt64>.Identity, Entropy(4));

        children.SelectMany(child => child.Values)
            .Should().OnlyContain(value => double.IsFinite(value.ToDouble()));
    }

    /// <summary>构造父代组 / Build a parent group.</summary>
    /// <returns>父代组 / The parent group.</returns>
    private static EvaluatedIndividual<Flt64, Flt64>[] Parents() => new[] {
        new EvaluatedIndividual<Flt64, Flt64>(new[] { new Flt64(0.0), new Flt64(10.0) }, new Flt64(0.0)),
        new EvaluatedIndividual<Flt64, Flt64>(new[] { new Flt64(10.0), new Flt64(0.0) }, new Flt64(1.0)),
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
/// 差分交叉 / The differential crossover.
/// </summary>
public sealed class DifferentialCrossTests {
    /// <summary>**只产一个子代** / Exactly one child is produced.</summary>
    [Fact]
    public void Differential_ShouldYieldSingleChild() {
        new DifferentialCross()
            .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(), Parents(6), new StubModel(value => value[0]),
                IIntoValue<Flt64>.Identity, Entropy(2))
            .Should().ContainSingle();
    }

    /// <summary>**父代不足两个时原样返回全部父代** / Fewer than two parents are returned unchanged.</summary>
    [Fact]
    public void Differential_ShouldPassThroughWithOneParent() {
        EvaluatedIndividual<Flt64, Flt64>[] parents = {
            new(new[] { new Flt64(3.0) }, new Flt64(0.0)),
        };

        IReadOnlyList<Solution<Flt64>> children = new DifferentialCross()
            .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(), parents, new StubModel(value => value[0]),
                IIntoValue<Flt64>.Identity, Entropy(2));

        children.Should().ContainSingle();
        children[0].Values[0].ToDouble().Should().Be(3.0);
    }

    /// <summary>**恰好两个父代时差值项退化为 0** / With exactly two parents the difference term collapses to zero.</summary>
    /// <remarks>
    /// `parents[2 % 2] == parents[0]`、`parents[3 % 2] == parents[1]`，于是 `first - second` 与 `base` 无关地变成一个固定差值；
    /// 交叉率 1 时子代全体落在同一个点上。这是基线行为，不是缺陷，但必须显式钉住以免被"顺手修正"。
    /// `parents[2 % 2] == parents[0]` and `parents[3 % 2] == parents[1]`, so `first - second` becomes a fixed difference independent of
    /// `base`; with a crossover rate of 1 every child lands on the same point. Baseline behaviour, not a defect, but it must be pinned so
    /// nobody "fixes" it by accident.
    /// </remarks>
    [Fact]
    public void Differential_ShouldCollapseWithTwoParents() {
        EvaluatedIndividual<Flt64, Flt64>[] parents = {
            new(new[] { new Flt64(1.0) }, new Flt64(0.0)),
            new(new[] { new Flt64(2.0) }, new Flt64(1.0)),
        };

        IReadOnlyList<Solution<Flt64>> children = new DifferentialCross(
                new Flt64(1.0), new Flt64(1.0))
            .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(), parents, new StubModel(value => value[0]),
                IIntoValue<Flt64>.Identity, Entropy(2));

        // base = 2, first - second = parents[0] - parents[1] = -1 → mutant = 1.0
        // base = 2, first - second = parents[0] - parents[1] = -1, so mutant = 1.0
        children[0].Values[0].ToDouble().Should().BeApproximately(1.0, 1e-12);
    }

    /// <summary>构造父代组 / Build a parent group.</summary>
    /// <param name="count">父代数量 / The parent count.</param>
    /// <returns>父代组 / The parent group.</returns>
    private static EvaluatedIndividual<Flt64, Flt64>[] Parents(int count) =>
        Enumerable.Range(0, count)
            .Select(index => new EvaluatedIndividual<Flt64, Flt64>(
                new[] { new Flt64(index) }, new Flt64(index)))
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
/// 排列类交叉算子 / Permutation crossover operators.
/// </summary>
public sealed class PermutationCrossTests {
    /// <summary>**顺序交叉的子代仍是排列（无重复值）** / The order-crossover child remains a permutation.</summary>
    /// <remarks>
    /// 片段直接从第一父代拷贝，剩余位置再从第二父代的轮转顺序里**跳过已用值**填充；不去重就会出现同一个值在两个位置，
    /// 而"子代看起来是个合法解"这件事在解码阶段才炸。
    /// The segment is copied from the first parent and the remaining positions are filled from the second parent's rotated order
    /// **skipping used values**; without deduplication a value appears twice and the fault only surfaces at decode time.
    /// </remarks>
    [Fact]
    public void OrderCross_ShouldProducePermutation() {
        for (long seed = 0; seed < 50; seed++) {
            IReadOnlyList<Solution<Flt64>> children = OrderCross.Instance
                .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                    new Iteration(), PermutationParents(), new StubModel(value => value[0]),
                    IIntoValue<Flt64>.Identity, Entropy(seed));

            foreach (Solution<Flt64> child in children) {
                child.Values.Select(value => value.ToDouble()).Distinct()
                    .Should().HaveCount(6, "排列不得出现重复值 / a permutation has no duplicates");
            }
        }
    }

    /// <summary>**循环交叉的子代仍是排列** / The cycle-crossover child remains a permutation.</summary>
    [Fact]
    public void CycleCross_ShouldProducePermutation() {
        IReadOnlyList<Solution<Flt64>> children = CycleCross.Instance
            .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(), PermutationParents(), new StubModel(value => value[0]),
                IIntoValue<Flt64>.Identity, Entropy(1));

        foreach (Solution<Flt64> child in children) {
            child.Values.Select(value => value.ToDouble()).OrderBy(value => value)
                .Should().Equal(new[] { 0.0, 1.0, 2.0, 3.0, 4.0, 5.0 });
        }
    }

    /// <summary>**循环交叉在映射成环时会终止** / The cycle crossover terminates when the map forms a loop.</summary>
    /// <remarks>
    /// `index = firstIndices[cycleSource[index]]` 沿值→位置映射跳转；一旦映射成环，没有上限的循环会永远转下去，
    /// **而每次迭代看起来都完全正常**。`guard &lt;= size` 兜住。
    /// `index = firstIndices[cycleSource[index]]` hops along the value-to-position map; once it forms a cycle an unbounded loop spins
    /// forever **while every iteration looks perfectly normal**. The code caps it with `guard &lt;= size`.
    /// </remarks>
    [Fact]
    public void CycleCross_ShouldTerminateOnSelfCycle() {
        // 两个父代互为同一排列 → 值→位置映射是自反的，容易成环。
        // The two parents are the same permutation, so the value-to-position map is reflexive and easily forms a loop.
        EvaluatedIndividual<Flt64, Flt64>[] parents = {
            new(new[] { 0.0, 1.0, 2.0, 3.0, 4.0, 5.0 }.Select(value => new Flt64(value)).ToList(), new Flt64(0.0)),
            new(new[] { 0.0, 1.0, 2.0, 3.0, 4.0, 5.0 }.Select(value => new Flt64(value)).ToList(), new Flt64(1.0)),
        };

        IReadOnlyList<Solution<Flt64>> children = CycleCross.Instance
            .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(), parents, new StubModel(value => value[0]),
                IIntoValue<Flt64>.Identity, Entropy(1));

        children.Should().HaveCount(2);
        children.Should().OnlyContain(child => child.Values.Count == 6);
    }

    /// <summary>**两个排列算子都产两个子代** / Both permutation operators yield two children.</summary>
    [Fact]
    public void PermutationCross_ShouldYieldTwoChildren() {
        foreach (ICross cross in new ICross[] { OrderCross.Instance, CycleCross.Instance }) {
            cross.Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                    new Iteration(), PermutationParents(), new StubModel(value => value[0]),
                    IIntoValue<Flt64>.Identity, Entropy(3))
                .Should().HaveCount(2);
        }
    }

    /// <summary>构造两个互为逆序的排列父代 / Build two mutually reversed permutation parents.</summary>
    /// <returns>父代组 / The parent group.</returns>
    private static EvaluatedIndividual<Flt64, Flt64>[] PermutationParents() => new[] {
        new EvaluatedIndividual<Flt64, Flt64>(
            new[] { 0.0, 1.0, 2.0, 3.0, 4.0, 5.0 }.Select(value => new Flt64(value)).ToList(), new Flt64(0.0)),
        new EvaluatedIndividual<Flt64, Flt64>(
            new[] { 5.0, 4.0, 3.0, 2.0, 1.0, 0.0 }.Select(value => new Flt64(value)).ToList(), new Flt64(1.0)),
    };

    /// <summary>构造熵源 / Build the entropy source.</summary>
    /// <param name="seed">种子 / The seed.</param>
    /// <returns>均匀熵源 / The uniform entropy source.</returns>
    private static Func<Flt64> Entropy(long seed) {
        var random = new SearchRandom(seed);
        return () => new Flt64(random.NextDouble());
    }
}
