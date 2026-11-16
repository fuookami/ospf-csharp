// 连续变异测试 / Tests for continuous mutation operators.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator.Mutation;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// 连续变异 / Continuous mutation.
/// </summary>
public sealed class ContinuousMutationTests {
    /// <summary>**无界时宽度退化为 1.0** / The width degrades to 1.0 without bounds.</summary>
    /// <remarks>
    /// 退化到 0 会让所有基于宽度的步长变成零位移——变异静默失效，而种群看上去仍在正常迭代。
    /// Degrading to 0 would make every width-based step a zero displacement: mutation silently stops working.
    /// </remarks>
    [Fact]
    public void WidthOf_ShouldDegradeToOne() {
        ContinuousMutationSupport.WidthOf(null, null).Should().Be(1.0);
        ContinuousMutationSupport.WidthOf(0.0, null).Should().Be(1.0);
        ContinuousMutationSupport.WidthOf(double.NegativeInfinity, 10.0).Should().Be(1.0);
    }

    /// <summary>有界时宽度等于区间长度 / With bounds the width is the interval length.</summary>
    [Fact]
    public void WidthOf_ShouldReturnInterval() =>
        ContinuousMutationSupport.WidthOf(2.0, 12.0).Should().Be(10.0);

    /// <summary>越过令牌数量时边界为 null / Out-of-range indices yield null bounds.</summary>
    [Fact]
    public void LowerBoundOf_ShouldReturnNullOutOfRange() {
        var model = new StubModel(value => value[0]);

        NumericSupport.LowerBoundOf<object?, Flt64, Flt64>(model, 0, IIntoValue<Flt64>.Identity)
            .Should().BeNull();
        NumericSupport.UpperBoundOf<object?, Flt64, Flt64>(model, -1, IIntoValue<Flt64>.Identity)
            .Should().BeNull();
    }

    /// <summary>**变异保持解的长度** / Mutation preserves the solution length.</summary>
    [Fact]
    public void Mutation_ShouldPreserveLength() {
        foreach (IMutationOperator mutation in AllOperators()) {
            IReadOnlyList<Flt64> mutated = Mutate(mutation, Sequence(5), rate: 1.0, seed: 4);
            mutated.Should().HaveCount(5, "变异不增删基因 / mutation neither adds nor drops genes");
        }
    }

    /// <summary>**带边界时变异结果真的被夹到区间内** / With bounds the mutated value is really clamped into the interval.</summary>
    /// <remarks>
 /// 这是 earlier 之前缺失的验证：`StubModelCore` 的令牌表为空，`LowerBoundOf` 恒返回 `null`，任何"在区间内"的断言都恒真。
    /// 这里用 `BoundedStubModel([-10, 10])` 并把起点放到 9.5，高斯扰动的标准差足以让它越界。
 /// This validation was missing before earlier: `StubModelCore`'s token table is empty, `LowerBoundOf` always returns `null`, and
    /// any "within the interval" assertion was vacuous. `BoundedStubModel([-10, 10])` with an origin of 9.5 lets a Gaussian step
    /// overflow with near certainty.
    /// </remarks>
    [Fact]
    public void Mutation_ShouldClampIntoBounds() {
        var random = new SearchRandom(3);
        var individual = new EvaluatedIndividual<Flt64, Flt64>(new[] { new Flt64(9.5) }, new Flt64(9.5));

        IReadOnlyList<Flt64> mutated = new UniformMutation()
            .Invoke<Flt64, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(), individual, new BoundedStubModel<Flt64>(1, -10.0, 10.0),
                IIntoValue<Flt64>.Identity, () => new Flt64(random.NextDouble()), new Flt64(1.0)).Values;

        mutated[0].ToDouble().Should().BeInRange(-10.0, 10.0);
    }

    /// <summary>**变异率为 0 时原样返回** / A zero rate returns the solution unchanged.</summary>
    [Fact]
    public void Mutation_ShouldReturnOriginalAtZeroRate() {
        IReadOnlyList<Flt64> original = Sequence(4);
        foreach (IMutationOperator mutation in AllOperators()) {
            Mutate(mutation, original, rate: 0.0, seed: 9).Should().Equal(original);
        }
    }

    /// <summary>**相同种子完全可重放** / Every operator replays exactly under the same seed.</summary>
    [Fact]
    public void Mutation_ShouldReplayUnderSameSeed() {
        IReadOnlyList<Flt64> original = Sequence(6);
        foreach (IMutationOperator mutation in AllOperators()) {
            Mutate(mutation, original, rate: 1.0, seed: 21)
                .Should().Equal(Mutate(mutation, original, rate: 1.0, seed: 21));
        }
    }

    /// <summary>**变异不修改输入解** / Mutation never mutates its input.</summary>
    [Fact]
    public void Mutation_ShouldNotMutateInput() {
        IReadOnlyList<Flt64> original = Sequence(4);
        var snapshot = original.ToList();
        foreach (IMutationOperator mutation in AllOperators()) {
            Mutate(mutation, original, rate: 1.0, seed: 3);
        }

        original.Should().Equal(snapshot);
    }

    /// <summary>**边界变异在无界时原样保留基因** / Boundary mutation keeps genes when there are no bounds.</summary>
    [Fact]
    public void BoundaryMutation_ShouldKeepGenesWithoutBounds() =>
        Mutate(BoundaryMutation.Instance, Sequence(4), rate: 1.0, seed: 2).Should().Equal(Sequence(4));

    /// <summary>**混沌变异不会产生零位移** / Chaotic mutation never yields a zero displacement.</summary>
    /// <remarks>
    /// Logistic 映射在 0 与 1 上是不动点；若初值不被夹到开区间内，整段扰动会恒为零，而搜索看起来仍在运行。
    /// The logistic map has fixed points at 0 and 1; without clamping the seed into the open interval the whole perturbation is
    /// identically zero while the search still appears to run.
    /// </remarks>
    [Fact]
    public void ChaoticMutation_ShouldAlwaysDisplace() {
        IReadOnlyList<Flt64> original = Sequence(8);
        IReadOnlyList<Flt64> mutated = Mutate(new ChaoticMutation(), original, rate: 1.0, seed: 1);

        mutated.Should().NotEqual(original);
    }

    /// <summary>**多项式变异在无界时步长不超过 ±1** / Polynomial mutation steps at most ±1 without bounds.</summary>
    [Fact]
    public void PolynomialMutation_ShouldStepWithinOneWithoutBounds() {
        IReadOnlyList<Flt64> mutated = Mutate(new PolynomialMutation(), Sequence(8), rate: 1.0, seed: 6);

        for (int index = 0; index < mutated.Count; index++) {
            double delta = global::System.Math.Abs(mutated[index].ToDouble() - index);
            delta.Should().BeLessThanOrEqualTo(1.0);
        }
    }

    /// <summary>**非一致变异的衰减指数被下限钳制** / The non-uniform decay exponent is clamped from below.</summary>
    [Fact]
    public void NonUniformMutation_ShouldClampDecay() {
        var mutation = new NonUniformMutation(new Flt64(0.0), 1000);

        IReadOnlyList<Flt64> mutated = Mutate(mutation, Sequence(5), rate: 1.0, seed: 8);

        mutated.Should().HaveCount(5, "零衰减指数不该让算子崩溃 / a zero decay must not break the operator");
    }

    /// <summary>**迭代上限为 0 时进度按 0 处理** / A zero iteration limit treats progress as 0.</summary>
    [Fact]
    public void NonUniformMutation_ShouldHandleZeroMaxIteration() {
        var mutation = new NonUniformMutation(new Flt64(5.0), 0);

        IReadOnlyList<Flt64> mutated = Mutate(mutation, Sequence(5), rate: 1.0, seed: 8);

        mutated.Should().HaveCount(5);
    }

    /// <summary>列出全部连续算子 / Enumerate every continuous operator.</summary>
    private static IEnumerable<IMutationOperator> AllOperators() => new IMutationOperator[] {
        new GaussianMutation(),
        new CauchyMutation(),
        new LevyMutation(),
        UniformMutation.Instance,
        BoundaryMutation.Instance,
        new PolynomialMutation(),
        new NonUniformMutation(),
        new ChaoticMutation(),
    };

    /// <summary>执行一次变异 / Run one mutation.</summary>
    private static IReadOnlyList<Flt64> Mutate(
        IMutationOperator mutation,
        IReadOnlyList<Flt64> solution,
        double rate,
        long seed) {
        var random = new SearchRandom(seed);
        var individual = new EvaluatedIndividual<Flt64, Flt64>(solution, new Flt64(0.0));

        return mutation.Invoke<Flt64, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
            new Iteration(),
            individual,
            new StubModelCore<Flt64>(value => value[0]),
            IIntoValue<Flt64>.Identity,
            () => new Flt64(random.NextDouble()),
            new Flt64(rate)).Values;
    }

    /// <summary>构造 0..count-1 的序列 / Build the sequence 0..count-1.</summary>
    private static List<Flt64> Sequence(int count) =>
        Enumerable.Range(0, count).Select(index => new Flt64(index)).ToList();
}
