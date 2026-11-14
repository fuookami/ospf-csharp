// 搜索特征与奖励策略测试 / Tests for search features and reward policies.

using System;
using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// 进度特征 / The progress features.
/// </summary>
public sealed class ProgressFeaturesTests {
    /// <summary>**停滞桶夹到 `0..7`，状态不越界** / The stagnation bucket is clamped to `0..7` and the state stays in range.</summary>
    /// <remarks>
    /// 不夹会让状态越过 64 个上限，而策略不会报错、只是把越界状态当作新状态。
    /// Without the clamp the state exceeds the 64-state limit, and policies do not report an error — they treat out-of-range states as new
    /// ones.
    /// </remarks>
    [Fact]
    public void Extract_ShouldClampStagnationBucket() {
        SelectionContext context = ProgressFeatures.Instance.Extract(
            new SearchProgress(0L, 0L, 100L, 500L, 0.5));

        context.State.Should().BeLessThan(64);
        // 进度 0 → 桶 0；停滞 500 夹到 7 → 状态 0*8 + 7 = 7。
        // Progress 0 → bucket 0; stagnation 500 clamps to 7 → state 0*8 + 7 = 7.
        context.State.Should().Be(7);
    }

    /// <summary>**状态是 `进度桶 × 8 + 停滞桶`** / The state is `progress bucket × 8 + stagnation bucket`.</summary>
    [Fact]
    public void Extract_ShouldComposeStateFromBuckets() {
        // 进度 1.0 → 桶 7；停滞 3 → 状态 7*8 + 3 = 59。
        // Progress 1.0 → bucket 7; stagnation 3 → state 7*8 + 3 = 59.
        SelectionContext context = ProgressFeatures.Instance.Extract(
            new SearchProgress(0L, 100L, 100L, 3L, 0.5));

        context.State.Should().Be(59);
    }

    /// <summary>**无评估上限时进度恒为 0** / Without an evaluation limit the progress is exactly 0.</summary>
    [Fact]
    public void Extract_ShouldTreatInfiniteLimitAsZeroProgress() {
        SelectionContext context = ProgressFeatures.Instance.Extract(
            new SearchProgress(0L, 1_000_000L, long.MaxValue, 0L, 0.5));

        context.State.Should().Be(0);
        context.Features![0].ToDouble().Should().Be(0.0);
    }

    /// <summary>**特征向量含成功率与代价** / The feature vector carries the success rate and cost.</summary>
    /// <remarks>
    /// 省略后两个会让奖励策略读到恒为零的成功率，**而上下文看起来完全正常**。
    /// Omitting the last two makes reward policies read a permanently zero success rate **while the context looks perfectly normal**.
    /// </remarks>
    [Fact]
    public void Extract_ShouldIncludeSuccessRateAndCost() {
        SelectionContext context = ProgressFeatures.Instance.Extract(
            new SearchProgress(0L, 0L, 100L, 0L, 0.5, new Flt64(0.25), new Flt64(3.0)));

        context.Features!.Count.Should().Be(5);
        context.Features![3].ToDouble().Should().Be(0.25);
        context.Features![4].ToDouble().Should().Be(3.0);
    }

    /// <summary>**停滞特征除以 1000 并截断** / The stagnation feature divides by 1000 and saturates.</summary>
    [Fact]
    public void Extract_ShouldScaleStagnationFeature() {
        SelectionContext context = ProgressFeatures.Instance.Extract(
            new SearchProgress(0L, 0L, 100L, 500L, 0.5));
        SelectionContext saturated = ProgressFeatures.Instance.Extract(
            new SearchProgress(0L, 0L, 100L, 5000L, 0.5));

        context.Features![1].ToDouble().Should().Be(0.5);
        saturated.Features![1].ToDouble().Should().Be(1.0);
    }
}

/// <summary>
/// 奖励策略 / The reward policies.
/// </summary>
public sealed class RewardPolicyTests {
    /// <summary>**非有限奖励被 `Evaluate` 转成失败** / A non-finite reward becomes a failure.</summary>
    /// <remarks>
    /// 让 `NaN` 流进策略更新会让整张表变成 `NaN`，而那之后每一次选择都返回某个合法动作，只是排序已经失效。
    /// Letting `NaN` flow into the policy update turns the whole table into `NaN`, after which every selection still returns some legal
    /// action — only the ordering is broken.
    /// </remarks>
    [Fact]
    public void Evaluate_ShouldRejectNonFiniteReward() {
        // **默认接口方法只能通过接口引用调用**：C# 不会在具体类上解析 `IRewardPolicy.Evaluate` 的默认实现。
        // **Default interface methods are reachable only through an interface reference**: C# does not resolve
        // `IRewardPolicy.Evaluate`'s default body on a concrete class.
        IRewardPolicy policy = new TimeNormalizedReward();

        // 负耗时 → 奖励为 NaN → 失败。
        // A negative elapsed time → NaN reward → failure.
        policy.Evaluate(new RewardContext(true, new Flt64(1.0), 1L, ElapsedNanos: -1L))
            .Should().BeOfType<Failed<Flt64, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**改进奖励只有 0 与 1** / The improvement reward is only 0 or 1.</summary>
    [Fact]
    public void ImprovementReward_ShouldReturnZeroOrOne() {
        ImprovementRewardPolicy.Instance.Reward(new RewardContext(true, new Flt64(5.0), 1L))
            .ToDouble().Should().Be(1.0);
        ImprovementRewardPolicy.Instance.Reward(new RewardContext(false, new Flt64(5.0), 1L))
            .ToDouble().Should().Be(0.0);
    }

    /// <summary>**代价感知奖励除以评估次数的下界 1** / The cost-aware reward divides by at least one evaluation.</summary>
    [Fact]
    public void CostAwareReward_ShouldDivideByEvaluations() {
        CostAwareRewardPolicy.Instance.Reward(new RewardContext(true, new Flt64(4.0), 2L))
            .ToDouble().Should().Be(2.0);
        CostAwareRewardPolicy.Instance.Reward(new RewardContext(true, new Flt64(4.0), 0L))
            .ToDouble().Should().Be(4.0, "评估次数下界是 1 / the evaluation count floors at 1");
    }

    /// <summary>**时间归一化奖励按纳秒换算** / The time-normalized reward converts nanoseconds.</summary>
    [Fact]
    public void TimeNormalizedReward_ShouldNormalizeBySeconds() {
        var policy = new TimeNormalizedReward(epsilonSeconds: 1.0);
        var context = new RewardContext(
            true, new Flt64(0.0), 1L, ElapsedNanos: 1_000_000_000L,
            Reference: new[] { new[] { new Flt64(10.0) } },
            Candidates: new[] { new[] { new Flt64(7.0) } });

        // 增益 3，耗时 1 秒 + 下界 1 秒 = 2 秒 → 1.5。
        // Gain 3; elapsed 1 second plus the 1-second floor = 2 seconds → 1.5.
        policy.Reward(context).ToDouble().Should().BeApproximately(1.5, 1e-12);
    }

    /// <summary>**增益被截断到非负** / The gain is clamped to non-negative.</summary>
    [Fact]
    public void Reward_ShouldClampNegativeGain() {
        var context = new RewardContext(
            false, new Flt64(0.0), 1L,
            Reference: new[] { new[] { new Flt64(7.0) } },
            Candidates: new[] { new[] { new Flt64(10.0) } });

        DominanceReward.Instance.Reward(context).ToDouble().Should().Be(0.0);
        new TimeNormalizedReward(1.0).Reward(context with { ElapsedNanos = 0L })
            .ToDouble().Should().Be(0.0);
    }

    /// <summary>**多维坐标使增益为 `NaN`** / Multi-dimensional coordinates make the gain `NaN`.</summary>
    /// <remarks>
    /// 跳过该点会让一个多维坐标被静默忽略，剩下的单维点算出一个"看起来合理"的增益。
    /// Skipping the point silently ignores a multi-dimensional coordinate and the remaining scalar points produce a plausible gain.
    /// </remarks>
    [Fact]
    public void Reward_ShouldRejectMultiDimensionalCoordinates() {
        var context = new RewardContext(
            true, new Flt64(0.0), 1L, ElapsedNanos: 0L,
            Reference: new[] { new[] { new Flt64(10.0) } },
            Candidates: new[] { new[] { new Flt64(7.0), new Flt64(1.0) } });

        ((IRewardPolicy)new TimeNormalizedReward(1.0)).Evaluate(context)
            .Should().BeOfType<Failed<Flt64, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**空坐标退化为贡献度** / Empty coordinates degrade to the contribution.</summary>
    [Fact]
    public void Reward_ShouldFallBackToContribution() {
        var context = new RewardContext(true, new Flt64(0.3), 1L, ElapsedNanos: 0L);

        new TimeNormalizedReward(1.0).Reward(context).ToDouble().Should().BeApproximately(0.3, 1e-12);
    }

    /// <summary>**极值奖励按动作分别取窗口内最大值** / The extreme-value reward takes the per-action window maximum.</summary>
    /// <remarks>
    /// 用全局最大值会让所有动作拿到同一个奖励，**策略因此学不到动作之间的差异，而 Q 表仍然在更新**。
    /// Using the global maximum gives every action the same reward, so **the policy learns nothing about differences between actions
    /// while the Q table keeps updating**.
    /// </remarks>
    [Fact]
    public void ExtremeValueReward_ShouldTrackPerActionMaximum() {
        var reward = new ExtremeValueReward(windowSize: 10);

        double First(string action, double from, double to) => reward.Reward(new RewardContext(
            true, new Flt64(0.0), 1L, Action: action,
            Reference: new[] { new[] { new Flt64(from) } },
            Candidates: new[] { new[] { new Flt64(to) } })).ToDouble();

        First("a", 10.0, 7.0).Should().Be(3.0);
        First("b", 10.0, 9.0).Should().Be(1.0);
        First("a", 10.0, 8.0).Should().Be(3.0, "a 的历史最大增益仍是 3 / a's historic maximum gain is still 3");
        First("a", 10.0, 1.0).Should().Be(9.0);
        First("b", 10.0, 9.5).Should().Be(1.0, "b 的最大增益仍是 1 / b's maximum gain is still 1");
    }

    /// <summary>**极值奖励的窗口按全部动作滚动** / The extreme-value window rolls over all actions.</summary>
    /// <remarks>
    /// 按动作各自滚动会让低频动作窗口过长，高频动作的旧增益被过早丢弃。
    /// Rolling per action makes a rarely-used action's window far too long and discards a frequently-used action's gains too early.
    /// </remarks>
    [Fact]
    public void ExtremeValueReward_ShouldEvictAcrossActions() {
        var reward = new ExtremeValueReward(windowSize: 2);

        double Record(string action, double from, double to) => reward.Reward(new RewardContext(
            true, new Flt64(0.0), 1L, Action: action,
            Reference: new[] { new[] { new Flt64(from) } },
            Candidates: new[] { new[] { new Flt64(to) } })).ToDouble();

        // **记录的是"增益"而不是"目标值"**：增益依次是 9、5、4、8。第一版把第三次写成 6.0，混淆了目标值与增益。
        // **What is recorded is the gain, not the objective value**: the gains are 9, 5, 4, 8 in order. The first version wrote 6.0 for
        // the third call, conflating the objective value with the gain.
        Record("a", 10.0, 1.0).Should().Be(9.0);
        Record("b", 10.0, 5.0).Should().Be(5.0);
        // 窗口只容 2 条观测：加入 (b,4) 时 (a,9) 被挤出，b 的最大增益仍是 5。
        // The window holds only two observations: adding (b,4) evicts (a,9), and b's maximum gain is still 5.
        Record("b", 10.0, 6.0).Should().Be(5.0);
        // 这一步是"跨动作淘汰"的证据：若 (a,9) 还在窗口里，这里会返回 9.0 而不是 8.0。
        // This step is the evidence of cross-action eviction: if (a,9) were still in the window this would return 9.0 rather than 8.0.
        Record("a", 10.0, 2.0).Should().Be(8.0,
            "a 的旧极值已被挤出窗口 / a's old extreme has been evicted from the window");
    }

    /// <summary>**窗口大小非正时返回 `NaN`** / A non-positive window size returns `NaN`.</summary>
    [Fact]
    public void ExtremeValueReward_ShouldRejectNonPositiveWindow() {
        var reward = new ExtremeValueReward(windowSize: 0);

        ((IRewardPolicy)reward).Evaluate(new RewardContext(
                true, new Flt64(0.0), 1L,
                Reference: new[] { new[] { new Flt64(10.0) } },
                Candidates: new[] { new[] { new Flt64(7.0) } }))
            .Should().BeOfType<Failed<Flt64, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**极值奖励快照往返一致** / The extreme-value reward snapshot round-trips.</summary>
    [Fact]
    public void ExtremeValueReward_ShouldRoundTrip() {
        var reward = new ExtremeValueReward(windowSize: 5);
        reward.Reward(new RewardContext(
            true, new Flt64(0.0), 1L, Action: "a",
            Reference: new[] { new[] { new Flt64(10.0) } },
            Candidates: new[] { new[] { new Flt64(7.0) } }));

        JsonElement state = ((Ok<JsonElement, ErrorCode, Error<ErrorCode>>)reward.Save()).Value;
        var restored = new ExtremeValueReward(windowSize: 5);
        restored.Load(state).Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();

        restored.Reward(new RewardContext(
            true, new Flt64(0.0), 1L, Action: "a",
            Reference: new[] { new[] { new Flt64(10.0) } },
            Candidates: new[] { new[] { new Flt64(8.0) } })).ToDouble()
            .Should().Be(3.0, "恢复后仍记得历史极值 / the historic extreme survives the restore");
    }

    /// <summary>**极值奖励拒绝窗口不匹配的快照** / The extreme-value reward rejects a mismatched-window snapshot.</summary>
    [Fact]
    public void ExtremeValueReward_ShouldRejectMismatchedWindow() {
        var reward = new ExtremeValueReward(windowSize: 5);
        JsonElement state = ((Ok<JsonElement, ErrorCode, Error<ErrorCode>>)reward.Save()).Value;

        new ExtremeValueReward(windowSize: 9).Load(state)
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }
}
