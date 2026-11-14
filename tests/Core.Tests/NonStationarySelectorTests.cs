// 非平稳选择器测试 / Tests for the non-stationary selectors.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// 非平稳选择器 / The non-stationary selectors.
/// </summary>
public sealed class NonStationarySelectorTests {
    /// <summary>**配置校验拒绝未知类型与非法参数** / Validation rejects unknown types and illegal parameters.</summary>
    [Fact]
    public void Validate_ShouldRejectBadParameters() {
        new NonStationarySelector("bogus", new[] { "a", "b" }).Validate()
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        new NonStationarySelector("choice-function", Array.Empty<string>()).Validate()
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        new NonStationarySelector("choice-function", new[] { "a", "a" }).Validate()
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        new NonStationarySelector("choice-function", new[] { "a" }, rate: 0.0).Validate()
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        new NonStationarySelector("choice-function", new[] { "a" }, decay: 1.5).Validate()
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**`frr-mab` 的窗口必须不小于动作数** / The `frr-mab` window must not be smaller than the action count.</summary>
    [Fact]
    public void Validate_ShouldRequireWindowForFrrMab() {
        new NonStationarySelector("frr-mab", new[] { "a", "b", "c" }, windowSize: 2).Validate()
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        new NonStationarySelector("frr-mab", new[] { "a", "b", "c" }, windowSize: 3).Validate()
            .Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**`frr-mab` 优先选择窗口内计数为 0 的动作** / `frr-mab` prefers actions with a zero window count.</summary>
    /// <remarks>
    /// **等价变异**：删掉"未试过"显式分支后，`counts == 0` 让探索项为 `∞`，`>` 比较仍会选中该动作，测试**仍然通过**。
    /// 这条测试钉住的是"行为正确"，不是"这条分支是必需的"。
    /// **An equivalent mutant**: removing the explicit "never tried" branch still passes because `counts == 0` makes the exploration term
    /// `∞` and the `>` comparison selects the action anyway. This test pins "the behaviour is correct", not "the branch is necessary".
    /// </remarks>
    [Fact]
    public void FrrMab_ShouldTryUntriedFirst() {
        var selector = new FrrMabSelector(new[] { "a", "b", "c" });
        var random = new SearchRandom(1);
        var chosen = new List<string>();

        for (int attempt = 0; attempt < 3; attempt++) {
            Result<string, ErrorCode, Error<ErrorCode>> result = selector.Select(
                new SelectionContext(), new[] { "a", "b", "c" }, random);
            chosen.Add(((Ok<string, ErrorCode, Error<ErrorCode>>)result).Value);
            selector.Observe(Transition(chosen[^1], 1.0, new[] { "a", "b", "c" }))
                .Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
        }

        chosen.Should().Equal("a", "b", "c");
    }

    /// <summary>**同一决策号重复选择被拒绝** / Selecting twice for one decision is rejected.</summary>
    /// <remarks>
    /// 允许覆盖会让前一次派发的前驱被静默丢弃，而后续 `observe` 仍能成功、只是记错了配对。
    /// Allowing overwrite silently drops the earlier dispatch's predecessor, and the following `observe` still succeeds — it just records
    /// the wrong pair.
    /// </remarks>
    [Fact]
    public void ChoiceFunction_ShouldRejectDuplicateDecision() {
        var selector = new ChoiceFunctionSelector(new[] { "a", "b" });
        var random = new SearchRandom(1);

        selector.Select(new SelectionContext(Decision: 7), new[] { "a", "b" }, random)
            .Should().BeOfType<Ok<string, ErrorCode, Error<ErrorCode>>>();
        selector.Select(new SelectionContext(Decision: 7), new[] { "a", "b" }, random)
            .Should().BeOfType<Failed<string, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**`discard` 清除待反馈派发** / `discard` clears the pending dispatch.</summary>
    [Fact]
    public void ChoiceFunction_ShouldDiscardPending() {
        var selector = new ChoiceFunctionSelector(new[] { "a", "b" });
        var random = new SearchRandom(1);

        selector.Select(new SelectionContext(Decision: 3), new[] { "a", "b" }, random)
            .Should().BeOfType<Ok<string, ErrorCode, Error<ErrorCode>>>();
        selector.Discard(3);

        selector.Select(new SelectionContext(Decision: 3), new[] { "a", "b" }, random)
            .Should().BeOfType<Ok<string, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**`predecessor` 返回待反馈派发的前驱** / `predecessor` returns the pending dispatch's predecessor.</summary>
    [Fact]
    public void ChoiceFunction_ShouldExposePredecessor() {
        var selector = new ChoiceFunctionSelector(new[] { "a", "b" });
        var random = new SearchRandom(1);

        // **每次选择必须用不同的决策号**：同一决策号重复选择会被拒绝（这是待验证的行为之一）。
        // 第一版两次都用 `Decision: 0`，第二次直接失败。
        // **Each selection must use a distinct decision number**: selecting twice for one decision is rejected (that is one of the
        // behaviours under test). The first version used `Decision: 0` twice and the second call failed outright.
        selector.Select(new SelectionContext(Decision: 0), new[] { "a", "b" }, random)
            .Should().BeOfType<Ok<string, ErrorCode, Error<ErrorCode>>>();
        selector.Predecessor(0).Should().BeNull("首次选择没有前驱 / the first selection has no predecessor");

        string first = selector.Predecessor(0) is null
            ? ((Ok<string, ErrorCode, Error<ErrorCode>>)selector.Select(
                new SelectionContext(Decision: 1), new[] { "a", "b" }, random)).Value
            : throw new InvalidOperationException("unreachable");
        selector.Observe(Transition(first, 1.0, new[] { "a", "b" }))
            .Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();

        selector.Select(new SelectionContext(Decision: 2), new[] { "a", "b" }, random)
            .Should().BeOfType<Ok<string, ErrorCode, Error<ErrorCode>>>();
        selector.Predecessor(2).Should().Be(first,
            "第三次选择的前驱是上一次观察的动作 / the third selection's predecessor is the previous observed action");
    }

    /// <summary>**`adaptive-pursuit` 的概率和保持为 1** / The adaptive-pursuit probabilities keep summing to 1.</summary>
    [Fact]
    public void AdaptivePursuit_ShouldKeepProbabilitySum() {
        var selector = new AdaptivePursuitSelector(new[] { "a", "b" });
        var random = new SearchRandom(1);

        for (int attempt = 0; attempt < 10; attempt++) {
            Result<string, ErrorCode, Error<ErrorCode>> result = selector.Select(
                new SelectionContext(), new[] { "a", "b" }, random);
            string chosen = ((Ok<string, ErrorCode, Error<ErrorCode>>)result).Value;
            selector.Observe(Transition(chosen, chosen == "a" ? 1.0 : 5.0, new[] { "a", "b" }))
                .Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
        }

        PolicySnapshot snapshot = selector.Snapshot();
        CreditState state = System.Text.Json.JsonSerializer.Deserialize<CreditState>(
            snapshot.Extension.GetRawText())!;
        double sum = 0.0;
        foreach (double probability in state.Probabilities.Values) {
            sum += probability;
        }
        sum.Should().BeApproximately(1.0, 1e-9);
        foreach (double probability in state.Probabilities.Values) {
            probability.Should().BeGreaterThanOrEqualTo(selector.MinimumProbability - 1e-12);
        }
    }

    /// <summary>**全更新率下概率直接取目标值** / At full rate the probabilities take the target directly.</summary>
    /// <remarks>
    /// **这条测试记录的是一个等价变异**。双精度下 `p + 1.0*(target - p)` 与 `target` 算术上完全相同，删掉实现里的全更新率捷径后
    /// 测试**仍然通过**。基线注释里"避免消去误差跌破概率下界"的理由在双精度下并不成立——把它写成"防止了数值下溢"会是假的。
    /// **This test records an equivalent mutant.** In double precision `p + 1.0*(target - p)` is arithmetically identical to `target`, so
    /// removing the full-rate shortcut **still passes**. The baseline's comment about cancellation dipping below the floor does not hold
    /// in double precision — claiming it prevents underflow would be false.
    /// </remarks>
    [Fact]
    public void AdaptivePursuit_ShouldHitTargetAtFullRate() {
        var selector = new NonStationarySelector("adaptive-pursuit", new[] { "a", "b" }, rate: 1.0);
        var random = new SearchRandom(1);

        selector.Select(new SelectionContext(), new[] { "a", "b" }, random)
            .Should().BeOfType<Ok<string, ErrorCode, Error<ErrorCode>>>();
        selector.Observe(Transition("b", 5.0, new[] { "a", "b" }))
            .Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();

        PolicySnapshot snapshot = selector.Snapshot();
        CreditState state = System.Text.Json.JsonSerializer.Deserialize<CreditState>(
            snapshot.Extension.GetRawText())!;
        // 赢家是 b：其概率是 1 - 0.01*(2-1) = 0.99，a 是 0.01。
        // The winner is b: its probability is 1 - 0.01*(2-1) = 0.99 and a is 0.01.
        state.Probabilities["b"].Should().BeApproximately(0.99, 1e-12);
        state.Probabilities["a"].Should().BeApproximately(0.01, 1e-12);
    }

    /// <summary>**关闭学习时不更新任何信用** / With learning off nothing is updated.</summary>
    [Fact]
    public void Observe_ShouldNotUpdateWhenLearningOff() {
        var selector = new NonStationarySelector("adaptive-pursuit", new[] { "a" }, learning: false);

        selector.Observe(Transition("a", 5.0, new[] { "a" }))
            .Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
        selector.Revision.Should().Be(0L);
    }

    /// <summary>**非法转移被拒绝** / An illegal transition is rejected.</summary>
    [Fact]
    public void Observe_ShouldRejectIllegalTransition() {
        var selector = new NonStationarySelector("adaptive-pursuit", new[] { "a", "b" });

        selector.Observe(Transition("z", 1.0, new[] { "a", "b" }))
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        selector.Observe(Transition("a", 1.0, new[] { "b" }))
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        selector.Observe(Transition("a", double.NaN, new[] { "a", "b" }))
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**快照往返一致** / Snapshots round-trip.</summary>
    [Fact]
    public void Snapshot_ShouldRoundTrip() {
        var selector = new ChoiceFunctionSelector(new[] { "a", "b" });
        var random = new SearchRandom(1);

        for (int attempt = 0; attempt < 3; attempt++) {
            Result<string, ErrorCode, Error<ErrorCode>> result = selector.Select(
                new SelectionContext(Decision: attempt), new[] { "a", "b" }, random);
            string chosen = ((Ok<string, ErrorCode, Error<ErrorCode>>)result).Value;
            selector.Observe(Transition(chosen, 2.0, new[] { "a", "b" }))
                .Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
        }

        PolicySnapshot snapshot = selector.Snapshot();
        var restored = new ChoiceFunctionSelector(new[] { "a", "b" });
        restored.Restore(snapshot).Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();

        PolicySnapshot again = restored.Snapshot();
        again.Revision.Should().Be(snapshot.Revision);
        again.Extension.GetRawText().Should().Be(snapshot.Extension.GetRawText());
    }

    /// <summary>**不兼容的快照被拒绝** / An incompatible snapshot is rejected.</summary>
    [Fact]
    public void Restore_ShouldRejectIncompatibleSnapshot() {
        var selector = new ChoiceFunctionSelector(new[] { "a", "b" });
        PolicySnapshot snapshot = selector.Snapshot();

        selector.Restore(snapshot with { Type = "frr-mab" })
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        selector.Restore(snapshot with { Actions = new[] { "a", "c" } })
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        selector.Restore(snapshot with { Version = 2 })
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**定义向量不匹配的快照被拒绝** / A snapshot with a mismatched definition is rejected.</summary>
    [Fact]
    public void Restore_ShouldRejectMismatchedDefinition() {
        var source = new NonStationarySelector("adaptive-pursuit", new[] { "a" }, rate: 0.2);
        PolicySnapshot snapshot = source.Snapshot();
        var target = new NonStationarySelector("adaptive-pursuit", new[] { "a" }, rate: 0.5);

        target.Restore(snapshot).Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>构造一个转移 / Build a transition.</summary>
    /// <param name="action">动作 / The action.</param>
    /// <param name="reward">奖励 / The reward.</param>
    /// <param name="available">可选动作 / The available actions.</param>
    /// <returns>转移 / The transition.</returns>
    private static SelectionTransition Transition(
        string action, double reward, IReadOnlyList<string> available) => new(
        "episode", 0L, action, new SelectionContext(), new SelectionContext(),
        available, available, new Flt64(reward), 1L, false, 0L);
}
