// 解更新算子测试 / Tests for solution-update operators.

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
/// 解更新框架 / The solution-update framework.
/// </summary>
public sealed class SolutionUpdateTests {
    /// <summary>交叉是解更新的子类型 / Crossover is a subtype of solution update.</summary>
    [Fact]
    public void Cross_ShouldBeSolutionUpdate() =>
        typeof(ISolutionUpdate).IsAssignableFrom(typeof(ICross)).Should().BeTrue();

    /// <summary>**空父代组产生空结果** / An empty parent group yields nothing.</summary>
    [Fact]
    public void Update_ShouldHandleEmptyParents() {
        foreach (ISolutionUpdate update in AllUpdates()) {
            update.Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                    new Iteration(), Array.Empty<EvaluatedIndividual<Flt64, Flt64>>(),
                    Model(), IIntoValue<Flt64>.Identity, Entropy(1))
                .Should().BeEmpty();
        }
    }

    /// <summary>**每个父代产生一个子代且维数不变** / Each parent yields one child of the same dimension.</summary>
    [Fact]
    public void Update_ShouldPreserveCountAndDimension() {
        foreach (ISolutionUpdate update in AllUpdates()) {
            IReadOnlyList<Solution<Flt64>> children = update.Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(), Parents(4, 3), Model(), IIntoValue<Flt64>.Identity, Entropy(2));

            children.Should().HaveCount(4);
            children.Should().OnlyContain(child => child.Values.Count == 3);
        }
    }

    /// <summary>**相同种子完全可重放** / Updates replay exactly under the same seed.</summary>
    [Fact]
    public void Update_ShouldReplayUnderSameSeed() {
        foreach (ISolutionUpdate update in AllUpdates()) {
            IReadOnlyList<Solution<Flt64>> first = update.Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(), Parents(3, 2), Model(), IIntoValue<Flt64>.Identity, Entropy(9));
            IReadOnlyList<Solution<Flt64>> second = update.Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(), Parents(3, 2), Model(), IIntoValue<Flt64>.Identity, Entropy(9));

            first.SelectMany(child => child.Values).Select(value => value.ToDouble()).Should()
                .Equal(second.SelectMany(child => child.Values).Select(value => value.ToDouble()));
        }
    }

    /// <summary>**带边界时子代真的被夹到区间内** / With bounds the children really are clamped into the interval.</summary>
    /// <remarks>
    /// 这条是**可证伪**的：`BoundedStubModel` 的边界是 `[-10, 10]`，而带宽 0.1 配合宽度 20 会给出 ±2 的步长——不足以越界。因此下面的
 /// 用例把带宽放大到 10（步长 ±200），**若夹紧失效，值会落到 ±200 附近而断言立刻失败**。earlier 之前这里写的是
    /// `value &gt;= -10 &amp;&amp; value &lt;= 10` 配上无边界模型，断定的是恒真命题。
    /// This one is **falsifiable**: `BoundedStubModel` bounds are `[-10, 10]`, while bandwidth 0.1 over width 20 gives a ±2 step —
    /// not enough to overflow. The case below therefore inflates the bandwidth to 10 (a ±200 step); **if clamping were broken the
 /// values would land near ±200 and the assertion would fail immediately**. Before earlier this spot asserted
    /// `value &gt;= -10 &amp;&amp; value &lt;= 10` against an unbounded model, i.e. a tautology.
    /// </remarks>
    [Fact]
    public void Harmony_ShouldClampOversizedPitchAdjustment() {
        var update = new HarmonySearchUpdate(
            MemoryConsiderationRate: 1.0, PitchAdjustmentRate: 1.0, Bandwidth: 10.0);
        EvaluatedIndividual<Flt64, Flt64>[] parents = Enumerable.Range(0, 20)
            .Select(_ => new EvaluatedIndividual<Flt64, Flt64>(new[] { new Flt64(0.0) }, new Flt64(0.0)))
            .ToArray();

        IReadOnlyList<double> values = update
            .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(), parents, Bounded(1, -10.0, 10.0), IIntoValue<Flt64>.Identity, Entropy(7))
            .Select(child => child.Values[0].ToDouble())
            .ToList();

        // **值必须正好贴在边界上**。带宽 10 × 宽度 20 = ±200，远超 [-10, 10]，所以夹紧后只能落在 ±10。
        // 这条断言同时钉住两件事：①夹紧生效；②宽度确实来自真实边界（若读不到边界，宽度退化成 1.0，步长只有 ±10，值会落在区间内部）。
        // **The values must sit exactly on the bound.** Bandwidth 10 * width 20 = ±200, far beyond [-10, 10], so clamping can only
        // land on ±10. This pins two things at once: (1) clamping engages; (2) the width came from real bounds — if bounds were
        // unreadable the width degrades to 1.0, the step is only ±10, and the values land strictly inside the interval.
        values.Should().OnlyContain(value => global::System.Math.Abs(global::System.Math.Abs(value) - 10.0) < 1e-12);
    }

    /// <summary>**无边界模型下子代仍是有限值** / An unbounded model still yields finite children.</summary>
    /// <remarks>
    /// 这条断言是**可证伪**的：`StubModel` 的令牌表为空，`LowerBoundOf` 恒返回 `null`，于是夹紧完全不起作用。因此这里能验证的只是
    /// "缺边界时不产生 `NaN`/`∞`"，**不能**验证夹紧本身。
    ///
    /// `StubModel` 的令牌表为空意味着**全仓还没有任何测试真正走到夹紧路径**：`LowerBoundOf` 恒为 `null`，任何"结果在边界内"的断言都
 /// 恒真。最初写的正是这种空洞断言（`value &gt;= -10 &amp;&amp; value &lt;= 10`），而生成值本来就落在 [0, 4]，断言无论如何都通过。
    /// 构造带边界的测试替身需要经约束系统给变量挂 `ValueRange`，属于独立的测试基建工作，已记入计划文档的后续项。
    /// This assertion is **falsifiable**: `StubModel`'s token table is empty, so `LowerBoundOf` always returns `null` and clamping never
    /// engages. What can be verified here is only "no `NaN`/`∞` without bounds", **not** the clamping itself.
    /// The empty token table means **no test in the repository reaches the clamping path at all**: `LowerBoundOf` is always `null`, so
    /// any "result is within bounds" assertion is vacuously true. The first draft asserted exactly that
    /// (`value &gt;= -10 &amp;&amp; value &lt;= 10`) while the generated values already sat in [0, 4], so it passed regardless.
    /// Building a bounded test double requires attaching a `ValueRange` to variables through the constraint system; that is separate
    /// test-infrastructure work and is recorded as a follow-up in the plan document.
    /// </remarks>
    [Fact]
    public void Update_ShouldStayFiniteWithoutBounds() {
        foreach (ISolutionUpdate update in AllUpdates()) {
            update.Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                    new Iteration(), Parents(4, 3), Model(), IIntoValue<Flt64>.Identity, Entropy(4))
                .SelectMany(child => child.Values)
                .Should().OnlyContain(value => double.IsFinite(value.ToDouble()));
        }
    }

    /// <summary>列出全部更新算子 / Enumerate every update operator.</summary>
    private static IEnumerable<ISolutionUpdate> AllUpdates() => new ISolutionUpdate[] {
        new GreyWolfUpdate(),
        new HarmonySearchUpdate(),
    };

    /// <summary>构造无边界模型 / Build an unbounded model.</summary>
    private static StubModel Model() => new(value => value[0]);

    /// <summary>构造带边界模型 / Build a bounded model.</summary>
    /// <param name="dimension">变量数量 / The variable count.</param>
    /// <param name="lower">下界 / The lower bound.</param>
    /// <param name="upper">上界 / The upper bound.</param>
    /// <returns>带边界模型 / The bounded model.</returns>
    private static BoundedStubModel<object?> Bounded(int dimension, double lower, double upper) =>
        new(dimension, lower, upper);

    /// <summary>构造父代组 / Build a parent group.</summary>
    private static EvaluatedIndividual<Flt64, Flt64>[] Parents(int count, int dimension) =>
        Enumerable.Range(0, count)
            .Select(index => new EvaluatedIndividual<Flt64, Flt64>(
                Enumerable.Range(0, dimension).Select(_ => new Flt64(index)).ToList(),
                new Flt64(index)))
            .ToArray();

    /// <summary>构造熵源 / Build the entropy source.</summary>
    private static Func<Flt64> Entropy(long seed) {
        var random = new SearchRandom(seed);
        return () => new Flt64(random.NextDouble());
    }
}

/// <summary>
/// 灰狼更新 / The grey-wolf update.
/// </summary>
public sealed class GreyWolfUpdateTests {
    /// <summary>**领导者数量被夹在父代组规模内** / The leader amount is clamped to the parent count.</summary>
    [Fact]
    public void GreyWolf_ShouldClampLeaderAmount() {
        IReadOnlyList<Solution<Flt64>> children = new GreyWolfUpdate(
                new Fuookami.Ospf.Core.Solver.Heuristic.Operator.ConstantSource(Flt64.Zero), 100)
            .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(), Parents(2, 3), Model(), IIntoValue<Flt64>.Identity, Entropy(1));

        children.Should().HaveCount(2);
        children.Should().OnlyContain(child => child.Values.Count == 3);
    }

    /// <summary>**收敛系数为 0 时子代向领导者靠拢** / With zero convergence the children collapse onto the leaders.</summary>
    /// <remarks>
    /// `a = 2·convergence·r1 − convergence`，收敛系数为 0 时 `a = 0`，于是每个分量都退化为领导者自身；三个领导者再平均。
    /// `a = 2*convergence*r1 - convergence`; with zero convergence `a = 0`, so every component degenerates to the leader itself and the
    /// three leaders are then averaged.
    /// </remarks>
    [Fact]
    public void GreyWolf_ShouldCollapseWhenConvergenceIsZero() {
        EvaluatedIndividual<Flt64, Flt64>[] parents = {
            new(new[] { new Flt64(0.0), new Flt64(0.0) }, new Flt64(0.0)),
            new(new[] { new Flt64(2.0), new Flt64(2.0) }, new Flt64(1.0)),
            new(new[] { new Flt64(4.0), new Flt64(4.0) }, new Flt64(2.0)),
        };

        IReadOnlyList<Solution<Flt64>> children = new GreyWolfUpdate(
                new Fuookami.Ospf.Core.Solver.Heuristic.Operator.ConstantSource(Flt64.Zero), 3)
            .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(), parents, Model(), IIntoValue<Flt64>.Identity, Entropy(3));

        // 三个领导者的平均：((0+2+4)/3) = 2。
        // The average of the three leaders: (0+2+4)/3 = 2.
        children.Should().OnlyContain(
            child => child.Values.All(value => global::System.Math.Abs(value.ToDouble() - 2.0) < 1e-12));
    }

    /// <summary>构造带边界的模型 / Build a bounded model.</summary>
    private static StubModel Model() => new(value => value[0]);

    /// <summary>构造父代组 / Build a parent group.</summary>
    private static EvaluatedIndividual<Flt64, Flt64>[] Parents(int count, int dimension) =>
        Enumerable.Range(0, count)
            .Select(index => new EvaluatedIndividual<Flt64, Flt64>(
                Enumerable.Range(0, dimension).Select(_ => new Flt64(index)).ToList(),
                new Flt64(index)))
            .ToArray();

    /// <summary>构造熵源 / Build the entropy source.</summary>
    private static Func<Flt64> Entropy(long seed) {
        var random = new SearchRandom(seed);
        return () => new Flt64(random.NextDouble());
    }
}

/// <summary>
/// 和声搜索更新 / The harmony-search update.
/// </summary>
public sealed class HarmonySearchUpdateTests {
    /// <summary>**音高调整方向对称，不会持续推向单侧边界** / Pitch adjustment is symmetric and does not drift to one boundary.</summary>
    /// <remarks>
    /// 只朝一个方向调整会把和声库逐步推向单侧边界，而"平均位置在移动"这件事在单代里看不出来。
    /// Adjusting in one direction only drifts the harmony memory toward one boundary, and "the mean is drifting" is invisible within
    /// a single generation.
    /// </remarks>
    [Fact]
    public void Harmony_ShouldAdjustSymmetrically() {
        EvaluatedIndividual<Flt64, Flt64>[] parents = Enumerable.Range(0, 20)
            .Select(_ => new EvaluatedIndividual<Flt64, Flt64>(new[] { new Flt64(0.0) }, new Flt64(0.0)))
            .ToArray();
        var update = new HarmonySearchUpdate(MemoryConsiderationRate: 1.0, PitchAdjustmentRate: 1.0, Bandwidth: 0.1);

        IReadOnlyList<Solution<Flt64>> children = update
            .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(), parents, Model(), IIntoValue<Flt64>.Identity, Entropy(11));

        double mean = children.Average(child => child.Values[0].ToDouble());
        // 上下对称的调整在 20 个样本上的平均值应接近 0（带宽 0.1 × 宽度 20 → ±2）。
        // A symmetric adjustment averages near 0 over 20 samples (bandwidth 0.1 * width 20 -> ±2).
        global::System.Math.Abs(mean).Should().BeLessThan(1.0);
        children.Should().Contain(child => child.Values[0].ToDouble() > 0.0);
        children.Should().Contain(child => child.Values[0].ToDouble() < 0.0);
    }

    /// <summary>**随机生成分支产生分散的采样** / The random-generation branch produces spread samples.</summary>
    /// <remarks>
    /// 该分支的设计意图是"从下界起、按区间宽度均匀采样"，但 `StubModel` 没有边界，`lower` 恒为 `null`，于是实际走的是
    /// `origin = 0.0, width = 1.0` 的退化路径。因此这条测试只能验证"结果分散"，**不能**验证"从下界起"——后者需要带边界的测试替身，
    /// 已记入计划文档的后续项（与 `Update_ShouldStayFiniteWithoutBounds` 同一个缺口）。
    /// The branch is meant to "start at the lower bound and sample uniformly across the interval width", but `StubModel` has no bounds,
    /// `lower` is always `null`, and the degraded path `origin = 0.0, width = 1.0` runs instead. This test therefore verifies only
    /// "the samples are spread", **not** "they start at the lower bound" — the latter needs a bounded test double, recorded as the same
    /// follow-up gap as `Update_ShouldStayFiniteWithoutBounds`.
    /// </remarks>
    [Fact]
    public void Harmony_ShouldProduceSpreadSamples() {
        var update = new HarmonySearchUpdate(MemoryConsiderationRate: 0.0, PitchAdjustmentRate: 0.0, Bandwidth: 0.0);
        EvaluatedIndividual<Flt64, Flt64>[] parents = Parents(10);

        IReadOnlyList<Solution<Flt64>> children = update
            .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(), parents, Model(), IIntoValue<Flt64>.Identity, Entropy(5));

        IReadOnlyList<double> values = children.Select(child => child.Values[0].ToDouble()).ToList();
        values.Distinct().Should().HaveCountGreaterThan(1, "采样应覆盖区间而不是全落到同一点 / sampling covers the interval");
    }

    /// <summary>构造带边界的模型 / Build a bounded model.</summary>
    private static StubModel Model() => new(value => value[0]);

    /// <summary>构造父代组 / Build a parent group.</summary>
    private static EvaluatedIndividual<Flt64, Flt64>[] Parents(int count) =>
        Enumerable.Range(0, count)
            .Select(index => new EvaluatedIndividual<Flt64, Flt64>(new[] { new Flt64(index) }, new Flt64(index)))
            .ToArray();

    /// <summary>构造熵源 / Build the entropy source.</summary>
    private static Func<Flt64> Entropy(long seed) {
        var random = new SearchRandom(seed);
        return () => new Flt64(random.NextDouble());
    }
}
