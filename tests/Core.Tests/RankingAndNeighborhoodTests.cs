// 排序与邻域测试 / Tests for ranking and neighbourhood operators.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// 个体排序 / Ranking individuals.
/// </summary>
public sealed class RankingTests {
    /// <summary>按原目标升序排列，优者在前 / Ranking by the objective puts the best first.</summary>
    [Fact]
    public void Objective_ShouldPutBestFirst() {
        IReadOnlyList<EvaluatedIndividual<Flt64, Flt64>> ranked = ObjectiveRanking.Instance
            .Rank<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new StubModel(value => value[0]),
                new[] { Individual(5.0), Individual(1.0), Individual(3.0) });

        ranked.Select(individual => individual.Objective.ToDouble()).Should().Equal(1.0, 3.0, 5.0);
    }

    /// <summary>反转排序把劣者放前面 / Reversed ranking puts the worst first.</summary>
    [Fact]
    public void Reversed_ShouldPutWorstFirst() {
        IReadOnlyList<EvaluatedIndividual<Flt64, Flt64>> ranked = ReversedObjectiveRanking.Instance
            .Rank<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new StubModel(value => value[0]),
                new[] { Individual(5.0), Individual(1.0), Individual(3.0) });

        ranked.Select(individual => individual.Objective.ToDouble()).Should().Equal(5.0, 3.0, 1.0);
    }

    /// <summary>恒等排序保持原顺序 / Identity ranking keeps the original order.</summary>
    [Fact]
    public void Identity_ShouldKeepOrder() {
        EvaluatedIndividual<Flt64, Flt64>[] input = { Individual(5.0), Individual(1.0) };

        IdentityRanking.Instance.Rank<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
            new StubModel(value => value[0]), input)
            .Should().Equal(input);
    }

    /// <summary>排序不修改输入集合 / Ranking never mutates the input.</summary>
    [Fact]
    public void Ranking_ShouldNotMutateInput() {
        var input = new List<EvaluatedIndividual<Flt64, Flt64>> {
            Individual(5.0), Individual(1.0), Individual(3.0),
        };
        var snapshot = input.ToList();

        ObjectiveRanking.Instance.Rank<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
            new StubModel(value => value[0]), input);

        input.Should().Equal(snapshot);
    }

    /// <summary>**非严格比较器下排序仍然稳定** / Ranking stays consistent under a non-strict comparator.</summary>
    /// <remarks>
    /// 模型注册 `&lt;=` 时 `CompareObjective(a, a)` 返回 `Less`；不走双向比较会破坏比较器契约。
    /// With a `&lt;=` comparator, `CompareObjective(a, a)` returns `Less`; skipping the bidirectional check breaks the contract.
    /// </remarks>
    [Fact]
    public void Ranking_ShouldHandleNonStrictComparator() {
        var model = new StubModel(value => value[0]) {
            Comparer = (lhs, rhs) => OrderHelpers.OrderOf(lhs.ToDouble() <= rhs.ToDouble() ? -1 : 1),
        };

        IReadOnlyList<EvaluatedIndividual<Flt64, Flt64>> ranked = ObjectiveRanking.Instance
            .Rank<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                model,
                new[] { Individual(2.0), Individual(2.0), Individual(1.0) });

        ranked.Select(individual => individual.Objective.ToDouble()).Should().Equal(1.0, 2.0, 2.0);
    }

    /// <summary>构造个体 / Build an individual.</summary>
    private static EvaluatedIndividual<Flt64, Flt64> Individual(double objective) =>
        new(new[] { new Flt64(objective) }, new Flt64(objective));
}

/// <summary>
/// 适应度评估 / Fitness evaluation.
/// </summary>
public sealed class FitnessEvaluatorTests {
    /// <summary>目标评估直接取模型目标 / Objective evaluation takes the model objective.</summary>
    [Fact]
    public void Objective_ShouldUseModelObjective() {
        var model = new StubModel(value => value[0]);

        ObjectiveFitnessEvaluator.Instance.Evaluate<object?, Flt64, Flt64>(
            model, new Solution<Flt64>(new[] { new Flt64(4.0) }))
            .Should().Be(new Flt64(4.0));
    }

    /// <summary>**目标缺失时回落到默认目标而不是 0** / A missing objective falls back to the default, never 0.</summary>
    /// <remarks>
    /// 用 0 冒充缺失目标会让不可评估的解在最小化问题里看起来最优，随后被当作精英保留。
    /// Substituting 0 makes an unevaluable solution look optimal in a minimization problem and it gets retained as an elite.
    /// </remarks>
    [Fact]
    public void DefaultOnMiss_ShouldFallBackToDefaultObjective() {
        var model = new MissingObjectiveModel();

        DefaultOnMissFitnessEvaluator.Instance.Evaluate<object?, string, Flt64>(
            model, new Solution<Flt64>(new[] { new Flt64(4.0) }))
            .Should().Be("default");
    }

    /// <summary>目标存在时不回落 / An existing objective is not replaced.</summary>
    [Fact]
    public void DefaultOnMiss_ShouldKeepExistingObjective() {
        var model = new StubModel(value => value[0]);

        DefaultOnMissFitnessEvaluator.Instance.Evaluate<object?, Flt64, Flt64>(
            model, new Solution<Flt64>(new[] { new Flt64(4.0) }))
            .Should().Be(new Flt64(4.0));
    }
}

/// <summary>
/// 邻域算子 / Neighbourhood operators.
/// </summary>
public sealed class NeighborhoodTests {
    /// <summary>**邻域解保持解长度** / A neighbour keeps the solution length.</summary>
    [Fact]
    public void Neighborhood_ShouldPreserveLength() {
        foreach (INeighborhood neighborhood in AllNeighborhoods()) {
            Generate(neighborhood, Sequence(6), seed: 3).Values
                .Should().HaveCount(6, "邻域只扰动位置，不增删基因 / neighbourhoods perturb positions, never add or drop genes");
        }
    }

    /// <summary>**空解不产生邻域** / An empty solution yields no neighbour.</summary>
    [Fact]
    public void Neighborhood_ShouldHandleEmptySolution() {
        foreach (INeighborhood neighborhood in AllNeighborhoods()) {
            Generate(neighborhood, new List<Flt64>(), seed: 1).Values.Should().BeEmpty();
        }
    }

    /// <summary>**相同种子完全可重放** / Neighbourhoods replay exactly under the same seed.</summary>
    [Fact]
    public void Neighborhood_ShouldReplayUnderSameSeed() {
        foreach (INeighborhood neighborhood in AllNeighborhoods()) {
            Generate(neighborhood, Sequence(6), seed: 12).Values
                .Should().Equal(Generate(neighborhood, Sequence(6), seed: 12).Values);
        }
    }

    /// <summary>**扰动确实改变解** / Perturbation actually changes the solution.</summary>
    [Fact]
    public void Neighborhood_ShouldDisplace() {
        IReadOnlyList<Flt64> original = Sequence(6);

        Generate(new RandomPerturbation(), original, seed: 2).Values.Should().NotEqual(original);
    }

    /// <summary>**带边界时邻域解真的被夹到区间内** / With bounds the neighbour is really clamped into the interval.</summary>
    /// <remarks>
    /// 这是 Batch 168 之前缺失的验证：`StubModel` 读不到边界，任何"在区间内"的断言都恒真。这里用 `BoundedStubModel([-10, 10])`，
    /// 均匀扰动的步长是 `(unit - 0.5) * width = ±10`，起点取 9.5 使越界必然发生；**若夹紧失效值会超过 10**。
    /// This validation was missing before Batch 168: `StubModel` cannot read bounds, so any "within the interval" assertion was vacuous.
    /// Here `BoundedStubModel([-10, 10])` yields a uniform step of `(unit - 0.5) * width = ±10` from an origin of 9.5, so an overflow is
    /// guaranteed; **without clamping the value would exceed 10**.
    /// </remarks>
    [Fact]
    public void Neighborhood_ShouldClampIntoBounds() {
        var random = new SearchRandom(6);
        var individual = new EvaluatedIndividual<Flt64, Flt64>(new[] { new Flt64(9.5) }, new Flt64(9.5));

        Solution<Flt64> neighbour = new RandomPerturbation()
            .Generate<Flt64, Flt64, Flt64>(
                new Iteration(), individual, new BoundedStubModel<Flt64>(1, -10.0, 10.0),
                () => new Flt64(random.NextDouble()), IIntoValue<Flt64>.Identity);

        neighbour.Values[0].ToDouble().Should().BeInRange(-10.0, 10.0);
        neighbour.Values[0].ToDouble().Should().NotBe(9.5, "扰动确实发生了 / perturbation actually fired");
    }

    /// <summary>**扰动位置数量不超过解长度** / The perturbed position count never exceeds the solution length.</summary>
    /// <remarks>
    /// 位置抽样有 `dimension * 4` 次尝试上限；没有上限时"抽到足够多不同位置"会退化成近似无限循环，而迭代计数看起来毫无异常。
    /// Position sampling is capped at `dimension * 4` attempts; without the cap, drawing enough distinct positions degenerates into a
    /// near-infinite loop while the iteration counter looks entirely normal.
    /// </remarks>
    [Fact]
    public void Neighborhood_ShouldNotLoopOnMassiveAmount() {
        Solution<Flt64> neighbour = Generate(new RandomPerturbation(1000), Sequence(4), seed: 5);

        neighbour.Values.Should().HaveCount(4);
    }

    /// <summary>不修改输入个体的解 / The input individual's solution is untouched.</summary>
    [Fact]
    public void Neighborhood_ShouldNotMutateInput() {
        IReadOnlyList<Flt64> original = Sequence(5);
        var snapshot = original.ToList();
        Generate(new RandomPerturbation(), original, seed: 7);

        original.Should().Equal(snapshot);
    }

    /// <summary>列出全部邻域算子 / Enumerate every neighbourhood operator.</summary>
    private static IEnumerable<INeighborhood> AllNeighborhoods() => new INeighborhood[] {
        new RandomPerturbation(),
        new GaussianPerturbation(),
        new LevyPerturbation(),
    };

    /// <summary>执行一次邻域生成 / Run one neighbourhood generation.</summary>
    private static Solution<Flt64> Generate(INeighborhood neighborhood, IReadOnlyList<Flt64> solution, long seed) {
        var random = new SearchRandom(seed);
        var individual = new EvaluatedIndividual<Flt64, Flt64>(solution, new Flt64(0.0));
        var model = new StubModelCore<Flt64>(value => value[0]);

        return neighborhood.Generate<Flt64, Flt64, Flt64>(
            new Iteration(), individual, model,
            () => new Flt64(random.NextDouble()), IIntoValue<Flt64>.Identity);
    }

    /// <summary>构造 0..count-1 的序列 / Build the sequence 0..count-1.</summary>
    private static List<Flt64> Sequence(int count) =>
        Enumerable.Range(0, count).Select(index => new Flt64(index)).ToList();
}

/// <summary>
/// 目标缺失的回调模型 / A call-back model with no objective.
/// </summary>
internal sealed class MissingObjectiveModel : Fuookami.Ospf.Core.Model.Callback.IAbstractCallBackModelInterface<object?, string, Flt64> {
    public string DefaultObjective => "default";

    public Fuookami.Ospf.Core.Model.Basic.ObjectCategory ObjectCategory =>
        Fuookami.Ospf.Core.Model.Basic.ObjectCategory.Minimum;

    public Fuookami.Ospf.Core.Token.IAbstractMutableTokenTable<Flt64> Tokens { get; } =
        new Fuookami.Ospf.Core.Token.ConcurrentAutoTokenTable<Flt64>(
            new Fuookami.Ospf.Math.Symbol.LinearCategory(),
            Array.Empty<Fuookami.Ospf.Core.Symbol.IIntermediateSymbol>());

    public IReadOnlyList<(Func<IReadOnlyList<Flt64>, bool?> Extractor, string Name)> Constraints =>
        Array.Empty<(Func<IReadOnlyList<Flt64>, bool?>, string)>();

    public IReadOnlyList<(Func<IReadOnlyList<Flt64>, object?> Extractor, string Name)> ObjectiveFunctions =>
        Array.Empty<(Func<IReadOnlyList<Flt64>, object?>, string)>();

    public string Operation(string lhs, string rhs) => lhs;

    public string ObjectiveValue() => DefaultObjective;

    public string ObjectiveValue(object? obj) => DefaultObjective;

    public string? Objective(IReadOnlyList<Flt64> solution) => null;

    public Order CompareObjective(string? lhs, string? rhs) => OrderHelpers.OrderOf(0);

    public bool? ConstraintSatisfied(IReadOnlyList<Flt64> solution) => true;

    public void Flush() { }

    public void Dispose() { }
}
