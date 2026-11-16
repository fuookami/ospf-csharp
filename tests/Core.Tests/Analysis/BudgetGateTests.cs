#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using System;
using System.Diagnostics;
using System.Threading;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// 预算门控 / The budget gate.
/// </summary>
/// <remarks>
/// **没有预算就是永不超支**，而"恰好等于预算"算超支（`>=`）。
/// **这两行是本类全部的价值**，也是唯一容易写反的地方。
///
/// **No budget means never exceeded**, and "exactly the budget" counts as exceeded (the code uses `>=`). **Those two rows are the whole value here**, and the only easy inversion.
/// </remarks>
public class BudgetGateTests {
    /// <summary>**没有预算 = 永不超支** / No budget means never exceeded.</summary>
    /// <remarks>
    /// 写反会让"没设预算"的调用**一次都不跑**——而它本该一直跑到做完。
    ///
    /// Inverting it makes every unbudgeted call **do nothing at all**, while it should run to completion.
    /// </remarks>
    [Fact]
    public void TheNullBudget_ShouldNeverBeExceeded() {
        BudgetGate.Exceeded(TimeSpan.Zero, null).Should().BeFalse();
        BudgetGate.Exceeded(TimeSpan.FromDays(365), null)
            .Should().BeFalse("a year of elapsed time is still not a budget overrun when there is no budget");
        BudgetGate.ExceededSince(Stopwatch.GetTimestamp(), null).Should().BeFalse();
    }

    /// <summary>**恰好等于预算算超支** / Exactly the budget counts as exceeded.</summary>
    /// <remarks>
    /// `>=`。改成 `>` 会让"刚好用完预算"的那一次**多跑一次求解**——而它正是最后那一次。
    ///
    /// The code uses `>=`. Changing it to `>` lets the solve that exactly spends the budget **run again** — and it is precisely the last one.
    /// </remarks>
    [Fact]
    public void TheExactBudget_ShouldBeExceeded() {
        var budget = TimeSpan.FromSeconds(1);

        BudgetGate.Exceeded(budget, budget).Should().BeTrue("the baseline compares with >=, not >");
        BudgetGate.Exceeded(budget - TimeSpan.FromTicks(1), budget).Should().BeFalse();
        BudgetGate.Exceeded(budget + TimeSpan.FromTicks(1), budget).Should().BeTrue();
    }

    /// <summary>**单调时钟真的走得动** / The monotonic clock actually advances.</summary>
    /// <remarks>
    /// 前两条判据喂的是常量，**它们证明不了 `ExceededSince` 真的读了时钟**——
    /// 一个恒返回 `false` 的实现在前两条下也是绿的。
    ///
    /// The first two criteria feed constants, **so they cannot show that `ExceededSince` actually reads a clock** — an implementation returning `false` forever is green under both.
    /// </remarks>
    [Fact]
    public void TheElapsedTime_ShouldActuallyAdvance() {
        long startedAt = Stopwatch.GetTimestamp();

        BudgetGate.ExceededSince(startedAt, TimeSpan.FromSeconds(30))
            .Should().BeFalse("nothing has elapsed yet");

        Thread.Sleep(15);

        BudgetGate.ExceededSince(startedAt, TimeSpan.FromMilliseconds(5))
            .Should().BeTrue("fifteen milliseconds is more than a five millisecond budget");
    }
}
