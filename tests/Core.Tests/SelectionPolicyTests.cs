// 选择策略测试 / Tests for the selection policies.

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
/// 表格型选择策略 / The tabular selection policy.
/// </summary>
public sealed class TabularSelectionPolicyTests {
    /// <summary>**配置校验拒绝未知类型** / Validation rejects an unknown type.</summary>
    [Fact]
    public void Validate_ShouldRejectUnknownType() {
        new TabularSelectionPolicy("bogus", new[] { "a", "b" }).Validate()
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**配置校验拒绝重复动作与非法超参** / Validation rejects duplicate actions and illegal hyperparameters.</summary>
    [Fact]
    public void Validate_ShouldRejectBadDefinitions() {
        new TabularSelectionPolicy("random", new[] { "a", "a" }).Validate()
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        new TabularSelectionPolicy("random", Array.Empty<string>()).Validate()
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        new TabularSelectionPolicy("random", new[] { "a" }, epsilon: 1.5).Validate()
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        new TabularSelectionPolicy("random", new[] { "a" }, stagnationLimit: 0).Validate()
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**round-robin 的游标是"上一个选中下标 + 1"** / The round-robin cursor is the last chosen index plus one.</summary>
    /// <remarks>
    /// 用"调用次数 + 1"会让掩码变化后的轮转从错误位置开始，被屏蔽的动作反复"跳过"，而每次调用本身看起来完全正常。
    /// Using "call count + 1" restarts rotation from the wrong position after the mask changes, repeatedly skipping masked actions while
    /// every call looks perfectly normal.
    /// </remarks>
    [Fact]
    public void RoundRobin_ShouldAdvanceFromLastChosen() {
        var policy = new TabularSelectionPolicy("round-robin", new[] { "a", "b", "c" });
        var random = new SearchRandom(1);
        string[] available = { "a", "b", "c" };

        policy.Select(new SelectionContext(), available, random).Should()
            .BeOfType<Ok<string, ErrorCode, Error<ErrorCode>>>();
        ((Ok<string, ErrorCode, Error<ErrorCode>>)policy.Select(
            new SelectionContext(), available, random)).Value.Should().Be("b");

        // 掩码只剩 c：游标在 b+1 = 2，应从 c 开始 → 选 c。
        // With only c available, the cursor is at b+1 = 2, so it starts at c and picks c.
        ((Ok<string, ErrorCode, Error<ErrorCode>>)policy.Select(
            new SelectionContext(), new[] { "c" }, random)).Value.Should().Be("c");
    }

    /// <summary>**ucb1 优先选择从未试过的动作** / UCB1 prefers never-tried actions.</summary>
    /// <remarks>
    /// **这条测试记录的是一个等价变异**。删掉实现里的"未试过"显式分支后，`mean + sqrt(2*ln(total+1)/counts)` 在 `counts == 0`
    /// 时得到 `0 + ∞ = ∞`，`Argmax` 仍会选中未试过的动作，测试**仍然通过**。保留该分支只是把"依赖 `∞` 比较"变成显式判断，
    /// 没有行为差异——把它写成"守住了关键逻辑"会是假的。
    /// **This test records an equivalent mutant.** Removing the explicit "never tried" branch still passes: `mean +
    /// sqrt(2*ln(total+1)/counts)` yields `0 + ∞ = ∞` at `counts == 0` and `Argmax` selects the untried action anyway. Keeping the branch
    /// only turns "rely on an `∞` comparison" into an explicit check, with no behavioural difference — claiming it guards critical logic
    /// would be false.
    /// </remarks>
    [Fact]
    public void Ucb1_ShouldTryUntriedActionsFirst() {
        // **必须开启学习**：关闭学习时 `observe` 不累加计数，于是"从未试过"永远成立、每次都选第一个动作。
        // 第一版忘了这一点，实测得到 `{a, a, a}`。
        // **Learning must be on**: with it off, `observe` never accumulates counts, so "never tried" always holds and the first action
        // is chosen every time. The first version missed this and measured `{a, a, a}`.
        var policy = new TabularSelectionPolicy("ucb1", new[] { "a", "b", "c" });
        var random = new SearchRandom(1);
        string[] available = { "a", "b", "c" };
        var chosen = new List<string>();

        for (int attempt = 0; attempt < 3; attempt++) {
            Result<string, ErrorCode, Error<ErrorCode>> result = policy.Select(
                new SelectionContext(), available, random);
            chosen.Add(((Ok<string, ErrorCode, Error<ErrorCode>>)result).Value);
            // 每次选择后记录一次该动作，使其计数离开 0。
            // Record the chosen action after each selection so its count leaves 0.
            policy.Observe(Transition(chosen[^1], reward: 1.0, available: available))
                .Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
        }

        chosen.Should().Equal("a", "b", "c");
    }

    /// <summary>**关闭学习时 observe 不累加计数** / Observing with learning off does not accumulate counts.</summary>
    /// <remarks>
    /// 仍然累加会让 `ucb1` 的探索项随观察次数增长，"关闭学习"实际上没有关闭。
    /// Accumulating anyway makes `ucb1`'s exploration term grow with observations, so "learning off" is not actually off.
    /// </remarks>
    [Fact]
    public void Observe_ShouldNotAccumulateWhenLearningOff() {
        var policy = new TabularSelectionPolicy("ucb1", new[] { "a" }, learning: false);

        // **转移的可选动作必须与策略动作集一致**：策略只有 `a`，若转移里写 `{a, b}`，校验会失败。
        // **The transition's available actions must match the policy's action set**: the policy has only `a`, so writing `{a, b}` in the
        // transition makes validation fail.
        policy.Observe(Transition("a", reward: 5.0, available: new[] { "a" }))
            .Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
        policy.Observe(Transition("a", reward: 5.0, available: new[] { "a" }))
            .Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
        IReadOnlyList<long> counts = ((Ok<PolicySnapshot, ErrorCode, Error<ErrorCode>>)Snapshot(policy)).Value.Counts!;
        counts[0].Should().Be(0L, "关闭学习时计数不得增长 / counts must not grow when learning is off");
        policy.Revision.Should().Be(0L);
    }

    /// <summary>**开启学习时 observe 累加计数与奖励和** / Observing with learning on accumulates counts and sums.</summary>
    [Fact]
    public void Observe_ShouldAccumulateWhenLearningOn() {
        var policy = new TabularSelectionPolicy("ucb1", new[] { "a" });

        policy.Observe(Transition("a", reward: 4.0, available: new[] { "a" }))
            .Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
        policy.Observe(Transition("a", reward: 6.0, available: new[] { "a" }))
            .Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();

        PolicySnapshot snapshot = ((Ok<PolicySnapshot, ErrorCode, Error<ErrorCode>>)Snapshot(policy)).Value;
        snapshot.Counts![0].Should().Be(2L);
        snapshot.Sums![0].Should().BeApproximately(10.0, 1e-12);
        policy.Revision.Should().Be(2L);
    }

    /// <summary>**快照与恢复按动作名对齐，而不是按下标** / Snapshots align by action name, not by index.</summary>
    /// <remarks>
    /// 快照里的 `actions` 顺序是"当时配置的顺序"，恢复时可能已重排。按下标恢复会把 A 的计数实现到 B 上，
    /// **每个动作的数值都仍然合理**，只是整体错位。
    /// The snapshot's `actions` order is the order configured then, while the restoring side may have reordered. Restoring by index moves
    /// A's counts onto B, and **every action's number still looks plausible**, only misattributed.
    /// </remarks>
    [Fact]
    public void Restore_ShouldAlignByActionName() {
        var original = new TabularSelectionPolicy("ucb1", new[] { "a", "b" });
        original.Observe(Transition("a", reward: 1.0))
            .Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
        original.Observe(Transition("b", reward: 9.0))
            .Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();

        PolicySnapshot snapshot = ((Ok<PolicySnapshot, ErrorCode, Error<ErrorCode>>)Snapshot(original)).Value;

        // 恢复方动作顺序颠倒：若按下标恢复，a 会拿到 9。
        // The restoring side reverses the action order: restoring by index would give a the 9.
        var reversed = new TabularSelectionPolicy("ucb1", new[] { "b", "a" });
        reversed.Restore(snapshot).Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();

        PolicySnapshot restored = ((Ok<PolicySnapshot, ErrorCode, Error<ErrorCode>>)Snapshot(reversed)).Value;
        // reversed 的 Actions 是 [b, a]，所以 counts[1] 对应 a、counts[0] 对应 b。
        // reversed's Actions are [b, a], so counts[1] is a and counts[0] is b.
        restored.Counts![0].Should().Be(1L, "b 的计数是 1 / b's count is 1");
        restored.Sums![0].Should().BeApproximately(9.0, 1e-12, "b 的奖励和是 9 / b's sum is 9");
        restored.Counts![1].Should().Be(1L, "a 的计数是 1 / a's count is 1");
        restored.Sums![1].Should().BeApproximately(1.0, 1e-12, "a 的奖励和是 1 / a's sum is 1");
    }

    /// <summary>**不兼容的快照被拒绝** / An incompatible snapshot is rejected.</summary>
    [Fact]
    public void Restore_ShouldRejectIncompatibleSnapshot() {
        var policy = new TabularSelectionPolicy("ucb1", new[] { "a", "b" });
        PolicySnapshot snapshot = ((Ok<PolicySnapshot, ErrorCode, Error<ErrorCode>>)Snapshot(policy)).Value;

        policy.Restore(snapshot with { Type = "random" })
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        policy.Restore(snapshot with { Actions = new[] { "a", "c" } })
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        policy.Restore(snapshot with { Version = 2 })
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**非 q-learning 策略的快照不含 Q 表** / Non-q-learning snapshots carry no Q table.</summary>
    [Fact]
    public void Snapshot_ShouldOmitTableForNonQLearning() {
        var policy = new TabularSelectionPolicy("ucb1", new[] { "a" });

        PolicySnapshot snapshot = ((Ok<PolicySnapshot, ErrorCode, Error<ErrorCode>>)Snapshot(policy)).Value;
        snapshot.Table!.Count.Should().Be(0);
    }

    /// <summary>**q-learning 的快照包含 Q 表** / Q-learning snapshots carry the table.</summary>
    [Fact]
    public void Snapshot_ShouldIncludeTableForQLearning() {
        var policy = new TabularSelectionPolicy("q-learning", new[] { "a" }, states: 2);

        PolicySnapshot snapshot = ((Ok<PolicySnapshot, ErrorCode, Error<ErrorCode>>)Snapshot(policy)).Value;
        snapshot.Table!.Count.Should().Be(2);
        snapshot.Table[0].Count.Should().Be(1);
    }

    /// <summary>**非法动作掩码被拒绝** / An illegal action mask is rejected.</summary>
    [Fact]
    public void Select_ShouldRejectIllegalMask() {
        var policy = new TabularSelectionPolicy("random", new[] { "a", "b" });
        var random = new SearchRandom(1);

        policy.Select(new SelectionContext(), Array.Empty<string>(), random)
            .Should().BeOfType<Failed<string, ErrorCode, Error<ErrorCode>>>();
        policy.Select(new SelectionContext(), new[] { "a", "a" }, random)
            .Should().BeOfType<Failed<string, ErrorCode, Error<ErrorCode>>>();
        policy.Select(new SelectionContext(), new[] { "z" }, random)
            .Should().BeOfType<Failed<string, ErrorCode, Error<ErrorCode>>>();
        policy.Select(new SelectionContext(State: 99), new[] { "a" }, random)
            .Should().BeOfType<Failed<string, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**非有限特征被拒绝** / Non-finite features are rejected.</summary>
    [Fact]
    public void Select_ShouldRejectNonFiniteFeatures() {
        var policy = new TabularSelectionPolicy("random", new[] { "a" });
        var random = new SearchRandom(1);

        policy.Select(
                new SelectionContext(Features: new[] { new Flt64(double.NaN) }),
                new[] { "a" }, random)
            .Should().BeOfType<Failed<string, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>生成快照 / Produce a snapshot.</summary>
    /// <param name="policy">策略 / The policy.</param>
    /// <returns>快照结果 / The snapshot result.</returns>
    private static Result<PolicySnapshot, ErrorCode, Error<ErrorCode>> Snapshot(ISelectionPolicy policy) =>
        Results.Ok(policy.Snapshot());

    /// <summary>构造一个转移 / Build a transition.</summary>
    /// <param name="action">动作 / The action.</param>
    /// <param name="reward">奖励 / The reward.</param>
    /// <returns>转移 / The transition.</returns>
    private static SelectionTransition Transition(
        string action, double reward, IReadOnlyList<string>? available = null) => new(
        "episode", 0L, action, new SelectionContext(), new SelectionContext(),
        available ?? new[] { "a", "b" }, available ?? new[] { "a", "b" },
        new Flt64(reward), 1L, false, 0L);
}
