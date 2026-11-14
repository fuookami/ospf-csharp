// 破坏—重建算子测试 / Tests for the ruin-and-recreate operators.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator.Neighborhood;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// 破坏算子 / The ruin operators.
/// </summary>
public sealed class RuinOperatorTests {
    /// <summary>**重复元素或越界移除数量被拒绝** / Duplicate elements or an out-of-range amount are rejected.</summary>
    /// <remarks>
    /// 允许重复元素会让"移走 amount 个"这句话不再成立，**而破坏本身看起来成功**；允许越界数量会让后续"身份守恒"检查永远为假。
    /// Allowing duplicates makes "removed `amount` elements" stop being true **while the ruin looks successful**; allowing an out-of-range
    /// count makes the later identity check never true.
    /// </remarks>
    [Fact]
    public void RandomRuin_ShouldRejectInvalidInput() {
        var ruin = new RandomRuin<int>();
        var random = new SearchRandom(7L);

        ruin.Remove(new[] { 1, 2, 2 }, 1, random)
            .Should().BeOfType<Failed<RuinResult<int>, ErrorCode, Error<ErrorCode>>>();
        ruin.Remove(new[] { 1, 2, 3 }, 4, random)
            .Should().BeOfType<Failed<RuinResult<int>, ErrorCode, Error<ErrorCode>>>();
        ruin.Remove(new[] { 1, 2, 3 }, -1, random)
            .Should().BeOfType<Failed<RuinResult<int>, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**随机破坏移走恰好 amount 个，并保持元素身份** / Random ruin removes exactly `amount` and conserves identities.</summary>
    [Fact]
    public void RandomRuin_ShouldConserveIdentities() {
        var ruin = new RandomRuin<int>();
        int[] solution = new[] { 0, 1, 2, 3, 4 };

        RuinResult<int> result = Unwrap(ruin.Remove(solution, 2, new SearchRandom(11L)));

        result.Removed.Should().HaveCount(2);
        result.Retained.Should().HaveCount(3);
        var all = new List<int>(result.Retained);
        all.AddRange(result.Removed);
        all.Should().BeEquivalentTo(solution);
    }

    /// <summary>**移除数量为 0 时什么都不移走** / A zero amount removes nothing.</summary>
    [Fact]
    public void RandomRuin_ShouldRemoveNothingForZero() {
        var ruin = new RandomRuin<int>();

        RuinResult<int> result = Unwrap(ruin.Remove(new[] { 0, 1, 2 }, 0, new SearchRandom(3L)));

        result.Removed.Should().BeEmpty();
        result.Retained.Should().BeEquivalentTo(new[] { 0, 1, 2 });
    }

    /// <summary>**最差代价破坏移走代价最高的那些** / Worst-cost ruin removes the highest-cost elements.</summary>
    /// <remarks>
    /// 升序取会把最便宜的移走，**于是"重建"永远只能把解变得更差，而每步仍然成功返回**。
    /// Ascending order removes the cheapest, **so "recreate" can only make the solution worse while every step still returns successfully**.
    /// </remarks>
    [Fact]
    public void WorstCostRuin_ShouldRemoveHighestCost() {
        var ruin = new WorstCostRuin<int>((solution, index) =>
            Results.Ok<Flt64>(new Flt64(solution[index])));

        RuinResult<int> result = Unwrap(ruin.Remove(new[] { 0, 1, 2, 3 }, 2, new SearchRandom(5L)));

        result.Removed.Should().Equal(new[] { 2, 3 }, "代价最高的 2 与 3 必须被移走 / the highest-cost 2 and 3 must be removed");
    }

    /// <summary>**非有限代价被拒绝** / A non-finite cost is rejected.</summary>
    [Fact]
    public void WorstCostRuin_ShouldRejectNonFiniteCost() {
        var ruin = new WorstCostRuin<int>((solution, index) =>
            Results.Ok<Flt64>(new Flt64(double.NaN)));

        ruin.Remove(new[] { 0, 1, 2 }, 1, new SearchRandom(5L))
            .Should().BeOfType<Failed<RuinResult<int>, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**相关度破坏优先移走最相似的一簇** / Relatedness ruin removes the most similar cluster first.</summary>
    /// <remarks>
    /// 距离定义为"与 1 的距离最小"，于是无论种子落在哪个元素上，第二轮都会选中 1（种子是 1 时选 0）。
    /// **这条不变性让断言与随机源无关**，同时仍然能区分"选最相似"与"选最不相似"。
    /// The distance makes 1 the nearest element to everything, so whichever element seeds the ruin, the second pick is 1 (or 0 when the seed
    /// is 1). **This invariant keeps the assertion independent of the random source** while still distinguishing "most similar" from
    /// "least similar".
    /// </remarks>
    [Fact]
    public void RelatednessRuin_ShouldRemoveSimilarCluster() {
        var ruin = new RelatednessRuin<int>((lhs, rhs) =>
            Results.Ok<Flt64>(new Flt64(rhs == 1 ? 1.0 : 100.0)));

        RuinResult<int> result = Unwrap(ruin.Remove(new[] { 0, 1, 2, 3 }, 2, new SearchRandom(9L)));

        result.Removed.Should().HaveCount(2);
        result.Removed.Should().Contain(1,
            "与 1 最相似的那个元素必须被移走 / the element most similar to 1 must be removed");
    }

    /// <summary>**负相关度被拒绝** / A negative relatedness is rejected.</summary>
    [Fact]
    public void RelatednessRuin_ShouldRejectNegativeDistance() {
        var ruin = new RelatednessRuin<int>((lhs, rhs) =>
            Results.Ok<Flt64>(new Flt64(-1.0)));

        ruin.Remove(new[] { 0, 1, 2 }, 2, new SearchRandom(9L))
            .Should().BeOfType<Failed<RuinResult<int>, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>解包成功结果 / Unwrap a successful result.</summary>
    /// <typeparam name="T">结果类型 / The result type.</typeparam>
    /// <param name="result">结果 / The result.</param>
    /// <returns>成功值 / The success value.</returns>
    private static T Unwrap<T>(Result<T, ErrorCode, Error<ErrorCode>> result) =>
        result is Ok<T, ErrorCode, Error<ErrorCode>> ok
            ? ok.Value
            : throw new InvalidOperationException("expected a successful result");
}

/// <summary>
/// 修复与破坏—重建算子 / The repair and ruin-and-recreate operators.
/// </summary>
public sealed class RuinRecreateOperatorTests {
    /// <summary>**遗憾 2 把"可选位置最少"的元素排在最前，即使它更贵** / Regret-2 puts the least-flexible item first even when it costs more.</summary>
    /// <remarks>
    /// 手算：部分解 `[0]`，待插元素 A（只有位置 0 可行，代价 10）与 B（位置 0、1 可行，代价 1、2）。
    /// k = 2 时 A 的"可选位置数"是 min(1,2) = 1，B 是 min(2,2) = 2，**A 优先**，于是 A 先插到位置 0，得到 `[A, 0]`；
    /// 再插 B 到位置 0，最终 `[B, A, 0]`。
    ///
    /// 贪心（k = 1）时两者的"可选位置数"都等于 k = 1，退化成比较遗憾：A 的遗憾 0、B 的遗憾 1，**B 优先**，
    /// 最终是 `[A, B, 0]`。两条测试因此可以互相区分。
    ///
    /// Hand-computed: partial solution `[0]`, pending items A (feasible only at position 0, cost 10) and B (feasible at 0 and 1, costs 1
    /// and 2). With k = 2, A's flexible-position count is min(1,2) = 1 and B's is min(2,2) = 2, **so A goes first**, landing at position 0
    /// to give `[A, 0]`; then B lands at position 0, giving `[B, A, 0]`.
    ///
    /// With greedy (k = 1) both counts are k = 1, so the comparison falls back to regret: A has 0 and B has 1, **so B goes first**, giving
    /// `[A, B, 0]`. The two tests therefore separate the two rules.
    /// </remarks>
    [Fact]
    public void Regret2_ShouldPreferLeastFlexible() {
        var repair = new RegretKInsertRepair<char>(new LayoutCost(), 2);

        IReadOnlyList<char> repaired = Unwrap(
            repair.Repair(new RuinResult<char>(new[] { '0' }, new[] { 'A', 'B' })));

        repaired.Should().Equal(new[] { 'B', 'A', '0' });
    }

    /// <summary>**贪心修复只按代价排序** / Greedy repair orders by cost only.</summary>
    [Fact]
    public void Greedy_ShouldPreferCheapest() {
        var repair = new GreedyInsertRepair<char>(new LayoutCost());

        IReadOnlyList<char> repaired = Unwrap(
            repair.Repair(new RuinResult<char>(new[] { '0' }, new[] { 'A', 'B' })));

        repaired.Should().Equal(new[] { 'A', 'B', '0' });
    }

    /// <summary>**没有任何可行插入时失败** / Having no feasible insertion fails.</summary>
    [Fact]
    public void Repair_ShouldFailWhenNothingFits() {
        var repair = new RegretKInsertRepair<char>(new NoCost(), 2);

        repair.Repair(new RuinResult<char>(new[] { '0' }, new[] { 'A' }))
            .Should().BeOfType<Failed<IReadOnlyList<char>, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**破坏丢了元素时整体失败** / A ruin that drops an element fails the whole step.</summary>
    /// <remarks>
    /// 只检查长度会让"重建"漏掉一个元素、重复另一个，**而解看起来仍然是一个合法排列**，于是搜索在另一个问题的解空间里继续。
    /// Checking only the length lets "recreate" drop one element and duplicate another, **while the solution still looks like a valid
    /// permutation**, so the search continues inside a different space.
    /// </remarks>
    [Fact]
    public void Operator_ShouldRejectIdentityLoss() {
        var ruin = new RuinRecreateOperator<char>(
            new DuplicatingRuin(), new GreedyInsertRepair<char>(new LayoutCost()));

        ruin.Invoke(new[] { 'A', 'B', 'C' }, 1, new SearchRandom(4L))
            .Should().BeOfType<Failed<IReadOnlyList<char>, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**重建结果必须是原解的一个排列** / The recreated solution must be a permutation of the original.</summary>
    [Fact]
    public void Operator_ShouldProducePermutation() {
        var ruin = new RuinRecreateOperator<char>(
            new RandomRuin<char>(), new GreedyInsertRepair<char>(new LayoutCost()));
        char[] solution = new[] { 'A', 'B', 'C', 'D' };

        IReadOnlyList<char> result = Unwrap(ruin.Invoke(solution, 2, new SearchRandom(13L)));

        result.Should().HaveCount(4);
        result.Should().BeEquivalentTo(solution);
    }

    /// <summary>插入代价：A 只在位置 0 可行且贵，B 在位置 0、1 可行且便宜 / A is feasible only at position 0 and expensive; B is feasible at 0 and 1 and cheap.</summary>
    private sealed class LayoutCost : IInsertionCost<char> {
        /// <inheritdoc/>
        public Result<Flt64?, ErrorCode, Error<ErrorCode>> Cost(
            IReadOnlyList<char> partial, char item, int position) {
            if (item == 'A') {
                return position == 0
                    ? Results.Ok<Flt64?>(new Flt64(10.0))
                    : Results.Ok<Flt64?>(null);
            }
            if (item == 'B') {
                return position <= 1
                    ? Results.Ok<Flt64?>(new Flt64(position + 1.0))
                    : Results.Ok<Flt64?>(null);
            }
            return Results.Ok<Flt64?>(new Flt64(position));
        }
    }

    /// <summary>处处不可行的插入代价 / An insertion cost with no feasible position.</summary>
    private sealed class NoCost : IInsertionCost<char> {
        /// <inheritdoc/>
        public Result<Flt64?, ErrorCode, Error<ErrorCode>> Cost(
            IReadOnlyList<char> partial, char item, int position) =>
            Results.Ok<Flt64?>(null);
    }

    /// <summary>故意丢元素的破坏算子 / A ruin operator that deliberately drops an element.</summary>
    private sealed class DuplicatingRuin : IRuin<char> {
        /// <inheritdoc/>
        public Result<RuinResult<char>, ErrorCode, Error<ErrorCode>> Remove(
            IReadOnlyList<char> solution, int amount, SearchRandom random) =>
            Results.Ok(new RuinResult<char>(new[] { 'A', 'B', 'B' }, new[] { 'C' }));
    }

    /// <summary>解包成功结果 / Unwrap a successful result.</summary>
    /// <typeparam name="T">结果类型 / The result type.</typeparam>
    /// <param name="result">结果 / The result.</param>
    /// <returns>成功值 / The success value.</returns>
    private static T Unwrap<T>(Result<T, ErrorCode, Error<ErrorCode>> result) =>
        result is Ok<T, ErrorCode, Error<ErrorCode>> ok
            ? ok.Value
            : throw new InvalidOperationException("expected a successful result");
}
