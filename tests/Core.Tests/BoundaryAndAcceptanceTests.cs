// 边界处理、终止判据与接受准则测试 / Tests for boundary handling, stopping criteria and acceptance rules.

using System;
using System.Threading;
using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// 边界处理 / Boundary handling.
/// </summary>
public sealed class BoundaryHandlerTests {
    /// <summary>截断把越界值压回边界 / Clamping presses out-of-range values onto the bounds.</summary>
    [Fact]
    public void Clamp_ShouldPressOntoBounds() {
        ClampBoundary.Instance.Invoke(new Iteration(), 0, new Flt64(50.0), new Flt64(0.0), new Flt64(10.0))
            .Should().Be(new Flt64(10.0));
        ClampBoundary.Instance.Invoke(new Iteration(), 0, new Flt64(-5.0), new Flt64(0.0), new Flt64(10.0))
            .Should().Be(new Flt64(0.0));
    }

    /// <summary>区间内的值不被改动 / An in-range value is untouched.</summary>
    [Fact]
    public void Clamp_ShouldKeepInRangeValue() =>
        ClampBoundary.Instance.Invoke(new Iteration(), 0, new Flt64(4.0), new Flt64(0.0), new Flt64(10.0))
            .Should().Be(new Flt64(4.0));

    /// <summary>**反射把越界值镜像折返** / Reflection mirrors an out-of-range value back.</summary>
    [Fact]
    public void Reflect_ShouldMirrorBack() =>
        ReflectBoundary.Instance.Invoke(new Iteration(), 0, new Flt64(13.0), new Flt64(0.0), new Flt64(10.0))
            .Should().Be(new Flt64(7.0), "13 越过上界 3，镜像回到 7 / 13 overshoots by 3 and mirrors back to 7");

    /// <summary>左越界同样折返 / A left overshoot reflects too.</summary>
    [Fact]
    public void Reflect_ShouldMirrorFromBelow() =>
        ReflectBoundary.Instance.Invoke(new Iteration(), 0, new Flt64(-3.0), new Flt64(0.0), new Flt64(10.0))
            .Should().Be(new Flt64(3.0));

    /// <summary>**极端越界仍落在区间内** / An extreme overshoot still lands inside the interval.</summary>
    /// <remarks>
    /// 折返次数上限是 64 而不是"直到收敛"；没有上限时，宽度极窄而值极大会让一次变异卡死整个搜索。
    /// The reflection cap is 64 rather than "until converged"; without it, a narrow width and a huge value would hang the search.
    /// </remarks>
    [Fact]
    public void Reflect_ShouldStayInRangeAtExtremes() {
        Flt64 handled = ReflectBoundary.Instance.Invoke(
            new Iteration(), 0, new Flt64(1.0e9), new Flt64(0.0), new Flt64(1.0e-3));

        handled.ToDouble().Should().BeInRange(0.0, 1.0e-3);
    }

    /// <summary>宽度为零时返回下界 / A zero width yields the lower bound.</summary>
    [Fact]
    public void Reflect_ShouldReturnLowerForZeroWidth() =>
        ReflectBoundary.Instance.Invoke(new Iteration(), 0, new Flt64(5.0), new Flt64(2.0), new Flt64(2.0))
            .Should().Be(new Flt64(2.0));

    /// <summary>**环绕按区间长度取模** / Wrapping takes the value modulo the interval width.</summary>
    [Fact]
    public void Wrap_ShouldWrapModuloWidth() =>
        WrapBoundary.Instance.Invoke(new Iteration(), 0, new Flt64(13.0), new Flt64(0.0), new Flt64(10.0))
            .Should().Be(new Flt64(3.0));

    /// <summary>**左越界环绕补一个宽度** / A left overshoot adds one width.</summary>
    /// <remarks>
    /// C# 的 `%` 对负数返回负余数；不补宽度会让左越界值落在区间外。
    /// C#'s `%` yields a negative remainder for negative operands; without adding one width the value lands outside the interval.
    /// </remarks>
    [Fact]
    public void Wrap_ShouldHandleNegativeRemainder() =>
        WrapBoundary.Instance.Invoke(new Iteration(), 0, new Flt64(-3.0), new Flt64(0.0), new Flt64(10.0))
            .Should().Be(new Flt64(7.0));

    /// <summary>宽度为零时返回下界 / A zero width yields the lower bound.</summary>
    [Fact]
    public void Wrap_ShouldReturnLowerForZeroWidth() =>
        WrapBoundary.Instance.Invoke(new Iteration(), 0, new Flt64(5.0), new Flt64(2.0), new Flt64(2.0))
            .Should().Be(new Flt64(2.0));
}

/// <summary>
/// 终止判据 / Stopping criteria.
/// </summary>
public sealed class StoppingCriterionTests {
    /// <summary>从不终止 / Never stop.</summary>
    [Fact]
    public void NeverStop_ShouldNeverStop() {
        var iteration = new Iteration();
        for (int index = 0; index < 100; index++) {
            iteration.Next(false);
        }

        NeverStop.Instance.ShouldStop(iteration).Should().BeFalse();
    }

    /// <summary>**迭代次数达到上限即终止** / An iteration limit stops once reached.</summary>
    [Fact]
    public void IterationLimit_ShouldStopAtLimit() {
        var iteration = new Iteration();
        var criterion = new IterationLimit(2);

        criterion.ShouldStop(iteration).Should().BeFalse();
        iteration.Next(true);
        criterion.ShouldStop(iteration).Should().BeFalse();
        iteration.Next(true);
        criterion.ShouldStop(iteration).Should().BeTrue();
    }

    /// <summary>**无改进上限只看连续无改进次数** / The no-improvement limit only counts consecutive misses.</summary>
    [Fact]
    public void NotBetterLimit_ShouldStopOnStagnation() {
        var iteration = new Iteration();
        var criterion = new NotBetterLimit(2);

        iteration.Next(false);
        criterion.ShouldStop(iteration).Should().BeFalse();
        iteration.Next(false);
        criterion.ShouldStop(iteration).Should().BeTrue();
        iteration.Next(true);
        criterion.ShouldStop(iteration).Should().BeFalse("有改进就清零 / an improvement resets the counter");
    }

    /// <summary>**时间上限使用实时读数** / The time limit reads live time.</summary>
    [Fact]
    public void TimeLimit_ShouldUseLiveTime() {
        var iteration = new Iteration();
        // "还没到点"这一半必须用一个宽到不可能在创建与检查之间跑完的上限：
        // 20 毫秒的窗口在全量测试并行跑满机器时会在**第一次检查就超时**，于是这条测试会随机失败。
        // The "not yet" half needs a limit so wide that creation and check cannot cross it: a 20 ms window trips **on the very first check**
        // when the full suite saturates the machine, so this test failed at random.
        var patient = new TimeLimit(TimeSpan.FromMinutes(1));
        patient.ShouldStop(iteration).Should().BeFalse();

        var criterion = new TimeLimit(TimeSpan.FromMilliseconds(20));
        Thread.Sleep(60);
        criterion.ShouldStop(iteration).Should().BeTrue(
            "不推进迭代也要能超时 / the timeout must fire without advancing the iteration");
    }

    /// <summary>暂停期间时间上限不触发 / A paused clock never trips the time limit.</summary>
    [Fact]
    public void TimeLimit_ShouldNotFireWhilePaused() {
        var iteration = new Iteration();
        iteration.Pause();
        // **上限必须相对冻结值给出**。写成固定 10 ms 会让测试依赖"构造到暂停之间"的耗时：并行测试负载下这一小段就可能超过 10 ms，
        // 断言随即失败，而实现完全正确。这类 flaky 只在整仓并发跑时才现形，单跑本类永远通过。
        // **The limit must be relative to the frozen value.** A fixed 10 ms makes the test depend on how long the constructor-to-pause
        // gap takes; under parallel test load that gap alone can exceed 10 ms and the assertion fails while the implementation is
        // entirely correct. Such flakes only appear when the whole suite runs concurrently — this class alone always passes.
        TimeSpan frozen = iteration.Time;
        var criterion = new TimeLimit(frozen + TimeSpan.FromSeconds(1));
        Thread.Sleep(30);

        criterion.ShouldStop(iteration).Should().BeFalse(
            "暂停期间不累计时间 / nothing accumulates while paused");
    }

    /// <summary>任一成立即终止 / "Any of" stops when either holds.</summary>
    [Fact]
    public void Or_ShouldStopWhenEitherHolds() {
        var iteration = new Iteration();
        iteration.Next(true);
        IStoppingCriterion criterion = new IterationLimit(1).Or(NeverStop.Instance);

        criterion.ShouldStop(iteration).Should().BeTrue();
    }

    /// <summary>全部成立才终止 / "All of" stops only when both hold.</summary>
    [Fact]
    public void And_ShouldStopOnlyWhenBothHold() {
        var iteration = new Iteration();
        iteration.Next(true);
        IStoppingCriterion criterion = new IterationLimit(1).And(new NotBetterLimit(1));

        criterion.ShouldStop(iteration).Should().BeFalse("第二个判据还没成立 / the second criterion is not met");
    }
}

/// <summary>
/// 接受准则 / Acceptance rules.
/// </summary>
public sealed class AcceptanceTests {
    /// <summary>**严格改进才接受** / Only strict improvement is accepted.</summary>
    [Fact]
    public void BetterOnly_ShouldRejectTies() {
        var model = new StubModel(value => value[0]);
        var iteration = new Iteration();

        BetterOnlyAcceptance.Instance.Invoke<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
            model, iteration, Individual(5.0), Individual(4.0)).Should().BeTrue();
        BetterOnlyAcceptance.Instance.Invoke<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
            model, iteration, Individual(5.0), Individual(5.0)).Should().BeFalse();
        BetterOnlyAcceptance.Instance.Invoke<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
            model, iteration, Individual(5.0), Individual(6.0)).Should().BeFalse();
    }

    /// <summary>**不劣即接受，允许平台横移** / Anything not worse is accepted, allowing lateral moves.</summary>
    [Fact]
    public void NotWorse_ShouldAcceptTies() {
        var model = new StubModel(value => value[0]);
        var iteration = new Iteration();

        NotWorseAcceptance.Instance.Invoke<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
            model, iteration, Individual(5.0), Individual(5.0)).Should().BeTrue();
        NotWorseAcceptance.Instance.Invoke<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
            model, iteration, Individual(5.0), Individual(6.0)).Should().BeFalse();
    }

    /// <summary>**非严格比较器下持平不被误判为改进** / A tie is not misread as an improvement under a non-strict comparator.</summary>
    [Fact]
    public void BetterOnly_ShouldHandleNonStrictComparator() {
        var model = new StubModel(value => value[0]) {
            Comparer = (lhs, rhs) => Fuookami.Ospf.Utils.Functional.OrderHelpers.OrderOf(
                lhs.ToDouble() <= rhs.ToDouble() ? -1 : 1),
        };

        BetterOnlyAcceptance.Instance.Invoke<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
            model, new Iteration(), Individual(5.0), Individual(5.0)).Should().BeFalse(
            "持平不是改进 / a tie is not an improvement");
    }

    /// <summary>无条件接受 / Unconditional acceptance.</summary>
    [Fact]
    public void Always_ShouldAcceptEverything() {
        var model = new StubModel(value => value[0]);

        AlwaysAcceptance.Instance.Invoke<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
            model, new Iteration(), Individual(1.0), Individual(100.0)).Should().BeTrue();
    }

    /// <summary>构造个体 / Build an individual.</summary>
    private static EvaluatedIndividual<Flt64, Flt64> Individual(double objective) =>
        new(new[] { new Flt64(objective) }, new Flt64(objective));
}
