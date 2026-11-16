// 排列变异测试 / Tests for permutation mutation operators.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator.Mutation;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// 排列变异 / Permutation mutation.
/// </summary>
public sealed class PermutationMutationTests {
    /// <summary>**交换变异保持元素多重集** / Swap mutation preserves the element multiset.</summary>
    [Fact]
    public void Swap_ShouldPreserveMultiset() {
        IReadOnlyList<Flt64> mutated = Mutate(SwapMutation.Instance, Sequence(6), rate: 1.0, seed: 3);

        mutated.Should().BeEquivalentTo(Sequence(6), options => options.WithoutStrictOrdering());
    }

    /// <summary>**交换变异确实改变顺序** / Swap mutation actually reorders.</summary>
    [Fact]
    public void Swap_ShouldReorder() {
        IReadOnlyList<Flt64> original = Sequence(6);
        IReadOnlyList<Flt64> mutated = Mutate(SwapMutation.Instance, original, rate: 1.0, seed: 3);

        mutated.Should().NotEqual(original);
    }

    /// <summary>**逆序变异保持元素多重集** / Inversion mutation preserves the element multiset.</summary>
    [Fact]
    public void Inversion_ShouldPreserveMultiset() {
        IReadOnlyList<Flt64> mutated = Mutate(InversionMutation.Instance, Sequence(7), rate: 1.0, seed: 5);

        mutated.Should().BeEquivalentTo(Sequence(7), options => options.WithoutStrictOrdering());
    }

    /// <summary>**插入变异保持元素多重集且不越界** / Insertion mutation preserves the multiset and never overflows.</summary>
    [Fact]
    public void Insertion_ShouldPreserveMultiset() {
        IReadOnlyList<Flt64> mutated = Mutate(InsertionMutation.Instance, Sequence(5), rate: 1.0, seed: 11);

        mutated.Should().HaveCount(5, "插入是移动而非增删 / insertion moves, it does not add or drop");
        mutated.Should().BeEquivalentTo(Sequence(5), options => options.WithoutStrictOrdering());
    }

    /// <summary>**插入在长度 2 的边界上不越界** / Insertion stays in range at length 2.</summary>
    [Fact]
    public void Insertion_ShouldHandleMinimumLength() {
        for (long seed = 0; seed < 20; seed++) {
            IReadOnlyList<Flt64> mutated = Mutate(InsertionMutation.Instance, Sequence(2), rate: 1.0, seed: seed);
            mutated.Should().HaveCount(2);
        }
    }

    /// <summary>**散射变异保持元素多重集** / Scramble mutation preserves the element multiset.</summary>
    [Fact]
    public void Scramble_ShouldPreserveMultiset() {
        IReadOnlyList<Flt64> mutated = Mutate(new ScrambleMutation(), Sequence(9), rate: 1.0, seed: 7);

        mutated.Should().BeEquivalentTo(Sequence(9), options => options.WithoutStrictOrdering());
    }

    /// <summary>**散射变异在相同种子下完全可重放** / Scramble mutation replays exactly under the same seed.</summary>
    /// <remarks>
    /// 用全局随机源 `shuffled` 时，同一份种子在散射变异上无法重放。本实现改用注入熵源，
    /// 这条断言钉住的正是那个修正。
    /// A global `shuffled` source is unrelated to the injected entropy, so the same seed cannot replay
    /// scramble mutation. This implementation uses the injected source, and this assertion pins that correction.
    /// </remarks>
    [Fact]
    public void Scramble_ShouldReplayUnderSameSeed() {
        IReadOnlyList<Flt64> first = Mutate(new ScrambleMutation(), Sequence(12), rate: 1.0, seed: 42);
        IReadOnlyList<Flt64> second = Mutate(new ScrambleMutation(), Sequence(12), rate: 1.0, seed: 42);

        first.Should().Equal(second);
    }

    /// <summary>**变异率为 0 时原样返回** / A zero mutation rate returns the solution unchanged.</summary>
    [Theory]
    [InlineData("swap")]
    [InlineData("inversion")]
    [InlineData("insertion")]
    [InlineData("scramble")]
    public void Mutation_ShouldReturnOriginalAtZeroRate(string kind) {
        IReadOnlyList<Flt64> original = Sequence(6);
        IReadOnlyList<Flt64> mutated = Mutate(OperatorOf(kind), original, rate: 0.0, seed: 1);

        mutated.Should().Equal(original);
    }

    /// <summary>**单元素解不参与变异** / A single-element solution is never mutated.</summary>
    [Fact]
    public void Mutation_ShouldSkipSingleElement() {
        var single = new List<Flt64> { new(1.0) };

        Mutate(SwapMutation.Instance, single, rate: 1.0, seed: 1).Should().Equal(single);
        Mutate(InversionMutation.Instance, single, rate: 1.0, seed: 1).Should().Equal(single);
        Mutate(InsertionMutation.Instance, single, rate: 1.0, seed: 1).Should().Equal(single);
        Mutate(new ScrambleMutation(), single, rate: 1.0, seed: 1).Should().Equal(single);
    }

    /// <summary>**空变异原样返回** / No-op mutation returns the solution unchanged.</summary>
    [Fact]
    public void NoMutation_ShouldReturnOriginal() {
        IReadOnlyList<Flt64> original = Sequence(4);

        Mutate(NoMutation.Instance, original, rate: 1.0, seed: 1).Should().Equal(original);
    }

    /// <summary>变异不修改输入解 / Mutation never mutates its input.</summary>
    [Fact]
    public void Mutation_ShouldNotMutateInput() {
        IReadOnlyList<Flt64> original = Sequence(6);
        var snapshot = original.ToList();

        Mutate(SwapMutation.Instance, original, rate: 1.0, seed: 2);

        original.Should().Equal(snapshot);
    }

    /// <summary>根据名称取算子 / Resolve an operator by name.</summary>
    private static IMutationOperator OperatorOf(string kind) => kind switch {
        "swap" => SwapMutation.Instance,
        "inversion" => InversionMutation.Instance,
        "insertion" => InsertionMutation.Instance,
        "scramble" => new ScrambleMutation(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>执行一次变异 / Run one mutation.</summary>
    private static IReadOnlyList<Flt64> Mutate(
        IMutationOperator mutation,
        IReadOnlyList<Flt64> solution,
        double rate,
        long seed) {
        var random = new SearchRandom(seed);
        var individual = new EvaluatedIndividual<Flt64, Flt64>(solution, new Flt64(0.0));

        // 排列算子从不读取模型：接入点保留该参数只是为了与连续算子共用接口。传 null 是**刻意的**——若某个排列算子开始读模型，
        // 这里会立刻以空引用暴露，而不是被一个假模型悄悄满足。
        // Permutation operators never read the model: the parameter exists only to share the interface with continuous operators.
        // Passing null is *deliberate* — if a permutation operator starts reading the model, this fails immediately with a null
        // reference instead of being quietly satisfied by a fake model.
        return mutation.Invoke<Flt64, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
            new Iteration(),
            individual,
            null!,
            IIntoValue<Flt64>.Identity,
            () => new Flt64(random.NextDouble()),
            new Flt64(rate)).Values;
    }

    /// <summary>构造 0..count-1 的序列 / Build the sequence 0..count-1.</summary>
    private static List<Flt64> Sequence(int count) =>
        Enumerable.Range(0, count).Select(index => new Flt64(index)).ToList();
}
