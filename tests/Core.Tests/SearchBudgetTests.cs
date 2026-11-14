// 搜索预算与随机流测试 / Tests for the search budget and the random stream.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// 搜索预算 / The search budget.
/// </summary>
public sealed class SearchBudgetTests {
    /// <summary>**领满上限后拒绝** / Acquisition stops at the limit.</summary>
    [Fact]
    public void Budget_ShouldRefuseBeyondLimit() {
        var budget = new SearchBudget(2);

        budget.Acquire().Should().BeTrue();
        budget.Acquire().Should().BeTrue();
        budget.Acquire().Should().BeFalse();
        budget.Used.Should().Be(2);
    }

    /// <summary>**并发领取不会超过上限** / Concurrent acquisition never exceeds the limit.</summary>
    [Fact]
    public void Budget_ShouldNeverExceedLimitUnderConcurrency() {
        var budget = new SearchBudget(50);
        int granted = 0;
        Parallel.For(0, 200, _ => {
            if (budget.Acquire()) {
                System.Threading.Interlocked.Increment(ref granted);
            }
        });

        granted.Should().Be(50, "原子领取不该让总数越过上限 / atomic admission must not overrun the limit");
        budget.Used.Should().Be(50);
    }

    /// <summary>**目标调用与已用额度互相独立** / Objective calls and consumed tokens are independent.</summary>
    [Fact]
    public void Budget_ShouldTrackObjectiveCallsSeparately() {
        var budget = new SearchBudget(10);
        budget.Acquire();
        budget.ObjectiveCalled();
        budget.ObjectiveCalled();

        budget.Used.Should().Be(1);
        budget.ObjectiveCalls.Should().Be(2);
    }

    /// <summary>已消费额度可以从外部恢复 / The consumed tokens can be restored.</summary>
    [Fact]
    public void Budget_ShouldRestoreConsumed() {
        var budget = new SearchBudget(10, consumed: 7);

        budget.Used.Should().Be(7);
        budget.Acquire().Should().BeTrue();
        budget.Used.Should().Be(8);
    }

    /// <summary>构造要求全局令牌非空 / Construction requires a non-null global budget.</summary>
    [Fact]
    public void Slice_ShouldRejectNullGlobal() =>
        ((Func<SearchSlice>)(() => new SearchSlice(null!, 1)))
            .Should().Throw<ArgumentNullException>();

    /// <summary>**片段额度用尽即拒绝** / A slice refuses once its own allowance is spent.</summary>
    [Fact]
    public void Slice_ShouldRefuseBeyondSliceAllowance() {
        var budget = new SearchBudget(10);
        var slice = new SearchSlice(budget, evaluations: 2);

        slice.Acquire().Should().BeTrue();
        slice.Acquire().Should().BeTrue();
        slice.Acquire().Should().BeFalse();
        budget.Used.Should().Be(2, "每次批准都消费全局额度 / each admission consumes the global budget");
    }

    /// <summary>**父级额度用尽时子片段一并拒绝** / A child refuses once the parent is spent.</summary>
    [Fact]
    public void Slice_ShouldRefuseWhenParentExhausted() {
        var budget = new SearchBudget(10);
        var parent = new SearchSlice(budget, evaluations: 1);
        SearchSlice child = parent.Child(evaluations: 5, steps: 1);

        child.Acquire().Should().BeTrue();
        child.Acquire().Should().BeFalse("父级已用尽 / the parent is exhausted");
        parent.Used.Should().Be(1);
    }

    /// <summary>**`Remaining` 取三层最小值** / `Remaining` is the minimum of the three layers.</summary>
    [Fact]
    public void Slice_RemainingShouldBeMinimumOfLayers() {
        var budget = new SearchBudget(10, consumed: 8);
        var parent = new SearchSlice(budget, evaluations: 100);
        SearchSlice child = parent.Child(evaluations: 5, steps: 1);

        child.Remaining.Should().Be(2, "全局只剩 2 / only 2 remain globally");
    }

    /// <summary>**超时后拒绝评估** / An expired slice refuses evaluation.</summary>
    [Fact]
    public void Slice_ShouldRefuseWhenExpired() {
        var budget = new SearchBudget(10);
        var slice = new SearchSlice(budget, evaluations: 5, duration: TimeSpan.Zero);

        slice.Expired.Should().BeTrue();
        slice.Acquire().Should().BeFalse();
    }

    /// <summary>**父级超时会传染给子片段** / A parent expiry propagates to the child.</summary>
    [Fact]
    public void Slice_ExpiredShouldPropagateToChild() {
        var budget = new SearchBudget(10);
        var parent = new SearchSlice(budget, evaluations: 5, duration: TimeSpan.Zero);
        SearchSlice child = parent.Child(evaluations: 5, steps: 1);

        child.Expired.Should().BeTrue();
        child.Acquire().Should().BeFalse();
    }

    /// <summary>**子片段额度不会让全局超发** / Nested slices never overdraw the global budget.</summary>
    [Fact]
    public void Slice_ChildrenShouldNotOverdrawGlobal() {
        var budget = new SearchBudget(3);
        var parent = new SearchSlice(budget, evaluations: 10);

        for (int index = 0; index < 5; index++) {
            SearchSlice child = parent.Child(evaluations: 2, steps: 1);
            child.Acquire();
        }

        budget.Used.Should().Be(3, "全局上限是硬约束 / the global limit is hard");
    }
}

/// <summary>
/// SplitMix64 随机流 / The SplitMix64 random stream.
/// </summary>
public sealed class SearchRandomTests {
    /// <summary>**黄金向量：种子 0** / Golden vector at seed 0.</summary>
    [Fact]
    public void Random_ShouldMatchGoldenVectorAtSeedZero() {
        var random = new SearchRandom(0);

        random.NextDouble().Should().BeApproximately(0.8833108082136426, 1e-15);
        random.NextDouble().Should().BeApproximately(0.43152799704850997, 1e-15);
        random.NextDouble().Should().BeApproximately(0.026433771592597743, 1e-15);
    }

    /// <summary>**黄金向量：种子 12345** / Golden vector at seed 12345.</summary>
    [Fact]
    public void Random_ShouldMatchGoldenVectorAtSeed12345() {
        var random = new SearchRandom(12345);

        random.NextDouble().Should().BeApproximately(0.1330796686614273, 1e-15);
        random.NextDouble().Should().BeApproximately(0.20481663336165912, 1e-15);
        random.NextDouble().Should().BeApproximately(0.11954258300911547, 1e-15);
    }

    /// <summary>**同一状态必然重放同一序列** / The same state necessarily replays the same sequence.</summary>
    [Fact]
    public void Random_ShouldReplayFromSameState() {
        var first = new SearchRandom(42);
        var second = new SearchRandom(42);

        Enumerable.Range(0, 20).Select(_ => first.NextDouble())
            .Should().Equal(Enumerable.Range(0, 20).Select(_ => second.NextDouble()));
    }

    /// <summary>**取值恒在 [0,1)** / Values always fall in [0,1).</summary>
    [Fact]
    public void Random_ShouldStayWithinUnitInterval() {
        var random = new SearchRandom(7);
        for (int index = 0; index < 2000; index++) {
            double value = random.NextDouble();
            value.Should().BeGreaterThanOrEqualTo(0.0).And.BeLessThan(1.0);
        }
    }

    /// <summary>**派生不消费父流** / Deriving never consumes the parent stream.</summary>
    [Fact]
    public void Random_SplitShouldNotConsumeParent() {
        var random = new SearchRandom(99);
        long before = random.State;

        random.Split("worker");
        random.State.Should().Be(before);

        var reference = new SearchRandom(99);
        random.NextDouble().Should().Be(reference.NextDouble());
    }

    /// <summary>**同一标识派生出同一子流** / The same identifier derives the same child stream.</summary>
    [Fact]
    public void Random_SplitShouldBeDeterministic() {
        var random = new SearchRandom(5);
        SearchRandom first = random.Split("alpha");
        SearchRandom second = random.Split("alpha");

        first.NextDouble().Should().Be(second.NextDouble());
    }

    /// <summary>**不同标识派生出不同子流** / Different identifiers derive different child streams.</summary>
    [Fact]
    public void Random_SplitShouldDifferByIdentifier() {
        var random = new SearchRandom(5);
        double a = random.Split("alpha").NextDouble();
        double b = random.Split("beta").NextDouble();

        a.Should().NotBe(b);
    }

    /// <summary>**状态可无损保存与恢复** / The state round-trips losslessly.</summary>
    [Fact]
    public void Random_StateShouldRoundTrip() {
        var random = new SearchRandom(1234);
        random.NextDouble();
        long saved = random.State;
        var restored = new SearchRandom(saved);

        random.NextDouble().Should().Be(restored.NextDouble());
    }
}
