// 局部搜索更新算子测试 / Tests for local-search update operators.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator.Mutation;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// 局部搜索更新算子 / Local-search update operators.
/// </summary>
public sealed class LocalSearchUpdateTests {
    /// <summary>**每个父代产一个子代且维数不变** / Each parent yields one child of the same dimension.</summary>
    [Fact]
    public void Update_ShouldPreserveCountAndDimension() {
        foreach (ISolutionUpdate update in AllUpdates()) {
            IReadOnlyList<Solution<Flt64>> children = Run(update, Parents(4), seed: 2);

            children.Should().HaveCount(4);
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
            Flatten(Run(update, Parents(3), seed: 5)).Should().Equal(Flatten(Run(update, Parents(3), seed: 5)));
        }
    }

    /// <summary>列出全部已实现的局部搜索算子 / Enumerate every implemented local-search operator.</summary>
    private static IEnumerable<ISolutionUpdate> AllUpdates() => new ISolutionUpdate[] {
        new MutationUpdate(new UniformMutation()),
        new NeighborhoodUpdate(new RandomPerturbation()),
        OppositionUpdate.Instance,
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
                Enumerable.Range(0, 3).Select(dim => new Flt64(index + dim)).ToList(),
                new Flt64(index)))
            .ToArray();
}

/// <summary>
/// 反向学习更新 / The opposition-based update.
/// </summary>
public sealed class OppositionUpdateTests {
    /// <summary>**有界变量取到关于区间中点的对称点** / A bounded variable is reflected about the interval midpoint.</summary>
    [Fact]
    public void Opposition_ShouldReflectBoundedVariables() {
        EvaluatedIndividual<Flt64, Flt64>[] parents = {
            new EvaluatedIndividual<Flt64, Flt64>(new[] { new Flt64(3.0) }, new Flt64(0.0)),
        };
        var random = new SearchRandom(1);

        IReadOnlyList<Solution<Flt64>> children = OppositionUpdate.Instance
            .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(), parents, new BoundedStubModel<object?>(1, -10.0, 10.0),
                IIntoValue<Flt64>.Identity, () => new Flt64(random.NextDouble()));

        // -10 + (10 - 3) = 3? 不对：lower + (upper - value) = -10 + 7 = -3。
        // -10 + (10 - 3) = -3, the mirror of 3 about the midpoint 0.
        children[0].Values[0].ToDouble().Should().BeApproximately(-3.0, 1e-12);
    }

    /// <summary>**无界变量原样保留，不凭空发明区间** / Unbounded variables are kept unchanged.</summary>
    /// <remarks>
    /// 对无界变量做"反向"等于凭空发明一个区间，得到的值既不是原解的反向、也不在可行域里——而它看起来只是一个普通的解。
    /// Reflecting an unbounded variable invents an interval; the result is neither the opposite nor feasible, yet looks ordinary.
    /// </remarks>
    [Fact]
    public void Opposition_ShouldKeepUnboundedVariables() {
        EvaluatedIndividual<Flt64, Flt64>[] parents = {
            new EvaluatedIndividual<Flt64, Flt64>(new[] { new Flt64(3.0), new Flt64(7.0) }, new Flt64(0.0)),
        };
        var random = new SearchRandom(1);

        // 维度 2 的带边界模型只覆盖前两维；这里用维度 1 的有界模型配二维解，第二维越界 → 无边界 → 原样保留。
        // A bounded model with one dimension against a two-dimensional solution: the second dimension has no bound and is kept.
        IReadOnlyList<Solution<Flt64>> children = OppositionUpdate.Instance
            .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(), parents, new BoundedStubModel<object?>(1, -10.0, 10.0),
                IIntoValue<Flt64>.Identity, () => new Flt64(random.NextDouble()));

        children[0].Values[1].ToDouble().Should().Be(7.0, "越界维度没有边界，必须原样保留 / no bound means unchanged");
    }

    /// <summary>**反向两次回到原值** / Reflecting twice returns to the original value.</summary>
    [Fact]
    public void Opposition_ShouldBeInvolutive() {
        EvaluatedIndividual<Flt64, Flt64>[] parents = {
            new EvaluatedIndividual<Flt64, Flt64>(new[] { new Flt64(3.0) }, new Flt64(0.0)),
        };
        var random = new SearchRandom(1);
        BoundedStubModel<object?> model = new(1, -10.0, 10.0);

        IReadOnlyList<Solution<Flt64>> once = OppositionUpdate.Instance
            .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(), parents, model, IIntoValue<Flt64>.Identity,
                () => new Flt64(random.NextDouble()));
        IReadOnlyList<Solution<Flt64>> twice = OppositionUpdate.Instance
            .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(),
                new[] { new EvaluatedIndividual<Flt64, Flt64>(once[0].Values, new Flt64(0.0)) },
                model, IIntoValue<Flt64>.Identity, () => new Flt64(random.NextDouble()));

        twice[0].Values[0].ToDouble().Should().BeApproximately(3.0, 1e-12);
    }
}

/// <summary>
/// 适应度评估辅助 / The fitness-evaluation helper.
/// </summary>
public sealed class FitnessEvaluatorSupportTests {
    /// <summary>**正常目标被包装成已评估个体** / A normal objective is wrapped as an evaluated individual.</summary>
    [Fact]
    public void ToIndividual_ShouldWrapEvaluatedSolution() {
        IIndividual<Flt64, Flt64>? individual = FitnessEvaluatorSupport.ToIndividual<object?, Flt64, Flt64>(
            new StubModel(value => value[0]), new Solution<Flt64>(new[] { new Flt64(4.0) }));

        individual.Should().NotBeNull();
        individual!.Objective.Should().Be(new Flt64(4.0));
    }

    /// <summary>**评估失败返回 null，而不是目标为默认值的个体** / A failed evaluation returns null, not a default-objective individual.</summary>
    /// <remarks>
    /// 返回一个"目标被填成默认值"的个体，会让它在最小化问题里看起来最优，随后被局部搜索当作改进保留下来——
    /// 整条搜索链从此建立在一个从未真正评估过的点上。
    /// Such an individual looks optimal in a minimization problem and gets retained as an improvement, so the whole chain is built on
    /// a point that was never actually evaluated.
    /// </remarks>
    [Fact]
    public void ToIndividual_ShouldReturnNullOnMissingObjective() {
        FitnessEvaluatorSupport.ToIndividual<object?, string, Flt64>(
                new MissingObjectiveModel(), new Solution<Flt64>(new[] { new Flt64(4.0) }))
            .Should().BeNull();
    }
}
