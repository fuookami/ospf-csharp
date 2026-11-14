// 模式搜索与组合器测试 / Tests for pattern search and composite operators.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// 模式搜索更新 / The pattern-search update.
/// </summary>
public sealed class PatternSearchUpdateTests {
    /// <summary>**从最优父代出发做下降** / The descent starts from the best parent.</summary>
    [Fact]
    public void PatternSearch_ShouldDescendFromBestParent() {
        // 目标就是第一个分量：从 5.0 出发，下降应把值推向更小的方向。
        // The objective is the first component: descending from 5.0 should push it downward.
        IReadOnlyList<Solution<Flt64>> children = new PatternSearchUpdate(0.1, 0.5, 32)
            .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(), Parents(5.0), new StubModel(value => value[0]),
                IIntoValue<Flt64>.Identity, Entropy(3));

        children.Should().ContainSingle();
        children[0].Values[0].ToDouble().Should().BeLessThan(5.0);
    }

    /// <summary>**参数非法时返回空列表** / Invalid parameters yield an empty list.</summary>
    /// <remarks>
    /// `contraction >= 1` 会让网格永远不收缩，搜索在一整个平面上反复试探而**迭代计数看起来完全正常**。
    /// A `contraction >= 1` means the mesh never shrinks and the search keeps probing a plane **while the iteration counter looks normal**.
    /// </remarks>
    [Theory]
    [InlineData(0.0, 0.5, 8)]
    [InlineData(-1.0, 0.5, 8)]
    [InlineData(0.1, 1.0, 8)]
    [InlineData(0.1, 1.5, 8)]
    [InlineData(0.1, 0.0, 8)]
    [InlineData(0.1, 0.5, 0)]
    public void PatternSearch_ShouldRejectInvalidParameters(double step, double contraction, int sweeps) {
        new PatternSearchUpdate(step, contraction, sweeps)
            .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(), Parents(5.0), new StubModel(value => value[0]),
                IIntoValue<Flt64>.Identity, Entropy(1))
            .Should().BeEmpty();
    }

    /// <summary>**空父代组产生空结果** / An empty parent group yields nothing.</summary>
    [Fact]
    public void PatternSearch_ShouldHandleEmptyParents() =>
        new PatternSearchUpdate()
            .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(), Array.Empty<EvaluatedIndividual<Flt64, Flt64>>(),
                new StubModel(value => value[0]), IIntoValue<Flt64>.Identity, Entropy(1))
            .Should().BeEmpty();

    /// <summary>**带边界时不越界** / The descent never leaves the bounds.</summary>
    [Fact]
    public void PatternSearch_ShouldStayWithinBounds() {
        IReadOnlyList<Solution<Flt64>> children = new PatternSearchUpdate(5.0, 0.5, 16)
            .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(), Parents(9.0), new BoundedStubModel<object?>(2, -10.0, 10.0),
                IIntoValue<Flt64>.Identity, Entropy(3));

        children.SelectMany(child => child.Values)
            .Should().OnlyContain(value => value.ToDouble() >= -10.0 && value.ToDouble() <= 10.0);
    }

    /// <summary>构造父代组 / Build a parent group.</summary>
    /// <param name="value">第一个分量的初值 / The initial value of the first component.</param>
    /// <returns>父代组 / The parent group.</returns>
    private static EvaluatedIndividual<Flt64, Flt64>[] Parents(double value) => new[] {
        new EvaluatedIndividual<Flt64, Flt64>(new[] { new Flt64(value), new Flt64(value) }, new Flt64(0.0)),
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
/// 组合更新器 / Composite update operators.
/// </summary>
public sealed class CompositeUpdateTests {
    /// <summary>**流水线每一级后按优度重排** / The pipeline re-sorts by quality after every stage.</summary>
    /// <remarks>
    /// 不排序就把上一级的输出顺序当成下一级的优度顺序，而下游算子普遍依赖"父代按优度升序"——**取前几个最好个体时会取到最差的那些**。
    /// Without sorting, the previous stage's output order is mistaken for the next stage's quality order, and downstream operators rely
    /// on the ascending contract — **taking the top few takes the worst ones**.
    /// </remarks>
    [Fact]
    public void Pipeline_ShouldSortAfterEachStage() {
        // 第一级原样返回（恒等），第二级记录它收到的父代。若流水线没排序，第二级收到的是乱序。
        // The first stage passes through; the second records its parents. Without sorting the second stage receives an unsorted list.
        var recorder = new RecordingUpdate();
        var pipeline = new PipelineCompositeUpdate(new ISolutionUpdate[] { IdentityUpdate.Instance, recorder });

        pipeline.Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
            new Iteration(), new[] { Individual(9.0), Individual(1.0), Individual(5.0) },
            new StubModel(value => value[0]), IIntoValue<Flt64>.Identity, Entropy(1));

        recorder.Objectives.Should().Equal(1.0, 5.0, 9.0);
    }

    /// <summary>**流水线保留每一级的输出数量** / The pipeline keeps the stage output count.</summary>
    [Fact]
    public void Pipeline_ShouldKeepCount() {
        var pipeline = new PipelineCompositeUpdate(new ISolutionUpdate[] {
            IdentityUpdate.Instance, new OppositionUpdate(),
        });

        pipeline.Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(), Parents(), new StubModel(value => value[0]),
                IIntoValue<Flt64>.Identity, Entropy(1))
            .Should().HaveCount(3);
    }

    /// <summary>**流水线为空时原样返回** / An empty pipeline returns the inputs unchanged.</summary>
    [Fact]
    public void Pipeline_ShouldPassThroughWithoutStages() {
        var pipeline = new PipelineCompositeUpdate(Array.Empty<ISolutionUpdate>());

        IReadOnlyList<Solution<Flt64>> children = pipeline
            .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(), Parents(), new StubModel(value => value[0]),
                IIntoValue<Flt64>.Identity, Entropy(1));

        children.Select(child => child.Values[0].ToDouble()).Should().BeEquivalentTo(new[] { 3.0, 1.0, 2.0 });
    }

    /// <summary>**VND 在 `maxTrials` 内终止** / VND terminates within `maxTrials`.</summary>
    /// <remarks>
    /// 没有硬上限，一个持续产生微小改进的邻域会让循环永不退出，而每次改进看起来都合法。
    /// Without a hard cap, a neighbourhood that keeps producing tiny improvements makes the loop never exit.
    /// </remarks>
    [Fact]
    public void VND_ShouldTerminateWithinMaxTrials() {
        var counter = new CountingUpdate();
        var vnd = new VNDCompositeUpdate(new ISolutionUpdate[] { counter }, MaxTrials: 5);

        IReadOnlyList<Solution<Flt64>> children = vnd
            .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(), Parents(), new StubModel(value => value[0]),
                IIntoValue<Flt64>.Identity, Entropy(1));

        counter.Calls.Should().BeLessThanOrEqualTo(5);
        children.Should().ContainSingle();
    }

    /// <summary>**VND 在改进后重置邻域序号** / VND resets the neighbourhood index after an improvement.</summary>
    /// <remarks>
    /// 改进后不自零，搜索会停在靠后的邻域上，前面的邻域再也不被访问——而"改进后回到最简单的邻域"正是变邻域下降的核心。
    /// Without the reset the search stalls on the later neighbourhoods and never revisits the earlier ones.
    /// </remarks>
    [Fact]
    public void VND_ShouldResetIndexAfterImprovement() {
        var first = new ImprovingUpdate();
        var second = new RecordingUpdate();
        var vnd = new VNDCompositeUpdate(new ISolutionUpdate[] { first, second }, MaxTrials: 3);

        vnd.Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
            new Iteration(), Parents(), new StubModel(value => value[0]),
            IIntoValue<Flt64>.Identity, Entropy(1));

        first.Calls.Should().BeGreaterThan(1, "改进后序号自零，第一个邻域被再次调用 / the first neighbourhood is revisited");
    }

    /// <summary>**精英包装只搜索前若干个，其余原样返回** / The elite wrapper searches the leading few and passes the rest through.</summary>
    [Fact]
    public void EliteWrapper_ShouldSearchOnlyElites() {
        var counter = new CountingUpdate();
        var wrapper = new EliteLocalSearchWrapper(counter, 2);

        IReadOnlyList<Solution<Flt64>> children = wrapper
            .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(), Parents(), new StubModel(value => value[0]),
                IIntoValue<Flt64>.Identity, Entropy(1));

        counter.ReceivedCounts.Should().BeEquivalentTo(new[] { 2 },
            "只把两个精英交给局部搜索 / only two elites reach the local search");
        children.Should().HaveCount(3);
    }

    /// <summary>构造父代组 / Build a parent group.</summary>
    /// <returns>父代组 / The parent group.</returns>
    private static EvaluatedIndividual<Flt64, Flt64>[] Parents() => new[] {
        Individual(3.0), Individual(1.0), Individual(2.0),
    };

    /// <summary>构造个体 / Build an individual.</summary>
    /// <param name="objective">目标值 / The objective value.</param>
    /// <returns>个体 / The individual.</returns>
    private static EvaluatedIndividual<Flt64, Flt64> Individual(double objective) =>
        new(new[] { new Flt64(objective) }, new Flt64(objective));

    /// <summary>构造熵源 / Build the entropy source.</summary>
    /// <param name="seed">种子 / The seed.</param>
    /// <returns>均匀熵源 / The uniform entropy source.</returns>
    private static Func<Flt64> Entropy(long seed) {
        var random = new SearchRandom(seed);
        return () => new Flt64(random.NextDouble());
    }
}

/// <summary>
/// 原样返回的更新算子 / An update operator that passes solutions through.
/// </summary>
internal sealed class IdentityUpdate : ISolutionUpdate {
    /// <summary>共享单例 / The shared singleton.</summary>
    public static IdentityUpdate Instance { get; } = new();

    /// <inheritdoc/>
    public IReadOnlyList<Solution<V>> Update<TObj, TObjValue, V, T>(
        Iteration iteration,
        IReadOnlyList<T> parents,
        Fuookami.Ospf.Core.Model.Callback.IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
        IIntoValue<V> converter,
        Func<Flt64> entropy)
        where V : struct, IRealNumber<V>, INumberField<V>
        where T : IIndividual<TObjValue, V> =>
        parents.Select(parent => new Solution<V>(parent.Solution)).ToList();
}

/// <summary>
/// 记录收到父代目标的更新算子 / An update operator recording the objectives it receives.
/// </summary>
internal sealed class RecordingUpdate : ISolutionUpdate {
    /// <summary>收到的目标值 / The objectives received.</summary>
    public List<double> Objectives { get; } = new();

    /// <summary>收到的父代数量 / The parent counts received.</summary>
    public List<int> ReceivedCounts { get; } = new();

    /// <inheritdoc/>
    public IReadOnlyList<Solution<V>> Update<TObj, TObjValue, V, T>(
        Iteration iteration,
        IReadOnlyList<T> parents,
        Fuookami.Ospf.Core.Model.Callback.IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
        IIntoValue<V> converter,
        Func<Flt64> entropy)
        where V : struct, IRealNumber<V>, INumberField<V>
        where T : IIndividual<TObjValue, V> {
        ReceivedCounts.Add(parents.Count);
        foreach (T parent in parents) {
            if (parent.Objective is Flt64 objective) {
                Objectives.Add(objective.ToDouble());
            }
        }
        return parents.Select(parent => new Solution<V>(parent.Solution)).ToList();
    }
}

/// <summary>
/// 统计调用次数的更新算子 / An update operator counting its invocations.
/// </summary>
internal sealed class CountingUpdate : ISolutionUpdate {
    /// <summary>调用次数 / The invocation count.</summary>
    public int Calls { get; private set; }

    /// <summary>每次收到的父代数量 / The parent counts received per call.</summary>
    public List<int> ReceivedCounts { get; } = new();

    /// <inheritdoc/>
    public IReadOnlyList<Solution<V>> Update<TObj, TObjValue, V, T>(
        Iteration iteration,
        IReadOnlyList<T> parents,
        Fuookami.Ospf.Core.Model.Callback.IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
        IIntoValue<V> converter,
        Func<Flt64> entropy)
        where V : struct, IRealNumber<V>, INumberField<V>
        where T : IIndividual<TObjValue, V> {
        Calls++;
        ReceivedCounts.Add(parents.Count);
        return parents.Select(parent => new Solution<V>(parent.Solution)).ToList();
    }
}

/// <summary>
/// 每次都被判为改进的更新算子 / An update operator whose output always looks better.
/// </summary>
/// <remarks>
/// 用于验证 VND 的邻域序号重置与 `maxTrials` 上限：它每次都把目标减半，于是每一轮都被判为改进。
/// Used to verify VND's index reset and `maxTrials` cap: it halves the objective every time, so every round looks like an improvement.
/// </remarks>
internal sealed class ImprovingUpdate : ISolutionUpdate {
    /// <summary>调用次数 / The invocation count.</summary>
    public int Calls { get; private set; }

    /// <inheritdoc/>
    public IReadOnlyList<Solution<V>> Update<TObj, TObjValue, V, T>(
        Iteration iteration,
        IReadOnlyList<T> parents,
        Fuookami.Ospf.Core.Model.Callback.IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
        IIntoValue<V> converter,
        Func<Flt64> entropy)
        where V : struct, IRealNumber<V>, INumberField<V>
        where T : IIndividual<TObjValue, V> {
        Calls++;
        var children = new List<Solution<V>>(parents.Count);
        foreach (T parent in parents) {
            var moved = new List<V>(parent.Solution.Count);
            foreach (V value in parent.Solution) {
                moved.Add(NumericSupport.AsValue(converter.FromValue(value).ToDouble() * 0.5, converter));
            }
            children.Add(new Solution<V>(moved));
        }
        return children;
    }
}
