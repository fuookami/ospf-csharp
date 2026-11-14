// 迭代计数器测试 / Tests for the heuristic iteration counter.

using System;
using System.Threading;
using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// 迭代计数器 / The heuristic iteration counter.
/// </summary>
public sealed class IterationTests {
    /// <summary>**有改进时无改进计数归零** / An improving round resets the no-improvement counter.</summary>
    [Fact]
    public void Next_ShouldResetNotBetterWhenImproved() {
        var iteration = new Iteration();
        iteration.Next(false);
        iteration.Next(false);
        iteration.Next(true);

        iteration.Current.Should().Be(3);
        iteration.NotBetterIteration.Should().Be(0);
    }

    /// <summary>**无改进时计数累加** / A non-improving round accumulates the no-improvement counter.</summary>
    [Fact]
    public void Next_ShouldAccumulateNotBetterWhenNotImproved() {
        var iteration = new Iteration();
        iteration.Next(false);
        iteration.Next(false);

        iteration.Current.Should().Be(2);
        iteration.NotBetterIteration.Should().Be(2);
    }

    /// <summary>**暂停期间迭代计数照常推进** / Iteration keeps advancing while timing is paused.</summary>
    [Fact]
    public void Pause_ShouldNotFreezeCounters() {
        var iteration = new Iteration();
        iteration.Pause();
        iteration.Next(false);

        iteration.Current.Should().Be(1, "暂停只冻结计时 / pausing freezes timing only");
        iteration.NotBetterIteration.Should().Be(1);
    }

    /// <summary>**暂停期间时间不前进** / The clock does not advance while paused.</summary>
    [Fact]
    public void Pause_ShouldFreezeTime() {
        var iteration = new Iteration();
        iteration.Pause();
        TimeSpan atPause = iteration.Time;
        Thread.Sleep(30);

        iteration.Time.Should().Be(atPause, "暂停期间不累计 / nothing accumulates while paused");
    }

    /// <summary>**恢复后时间继续累计** / The clock resumes accumulating after `Resume`.</summary>
    [Fact]
    public void Resume_ShouldContinueAccumulating() {
        var iteration = new Iteration();
        iteration.Pause();
        TimeSpan atPause = iteration.Time;
        Thread.Sleep(30);
        iteration.Resume();
        Thread.Sleep(30);

        iteration.Time.Should().BeGreaterThan(atPause);
    }

    /// <summary>**恢复时的耗时不包含暂停区间** / The resumed elapsed time excludes the paused interval.</summary>
    [Fact]
    public void Resume_ShouldExcludePausedInterval() {
        var iteration = new Iteration();
        iteration.Pause();
        Thread.Sleep(120);
        iteration.Resume();

        iteration.Time.Should().BeLessThan(
            TimeSpan.FromMilliseconds(120),
            "暂停区间不该被算成有效时间 / the paused interval must not count as active time");
    }

    /// <summary>**重复暂停是幂等的** / Repeated `Pause` calls are idempotent.</summary>
    [Fact]
    public void Pause_ShouldBeIdempotent() {
        var iteration = new Iteration();
        iteration.Pause();
        TimeSpan first = iteration.Time;
        Thread.Sleep(20);
        iteration.Pause();

        iteration.Time.Should().Be(first);
    }

    /// <summary>**重复恢复不重复打点** / Repeated `Resume` calls do not re-mark twice.</summary>
    [Fact]
    public void Resume_ShouldBeIdempotent() {
        var iteration = new Iteration();
        iteration.Pause();
        iteration.Resume();
        TimeSpan first = iteration.Time;
        iteration.Resume();

        iteration.Time.Should().BeGreaterThanOrEqualTo(first);
    }

    /// <summary>**恢复的耗时被保留** / Restored elapsed time is preserved.</summary>
    [Fact]
    public void Construct_ShouldRestoreElapsed() {
        var iteration = new Iteration(TimeSpan.FromSeconds(5));

        iteration.Time.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(5));
    }

    /// <summary>**重置清零计数与计时** / `Reset` zeroes both counters and the clock.</summary>
    [Fact]
    public void Reset_ShouldClearCountersAndTiming() {
        var iteration = new Iteration(TimeSpan.FromSeconds(5));
        iteration.Next(false);
        iteration.Next(false);

        iteration.Reset();

        iteration.Current.Should().Be(0);
        iteration.NotBetterIteration.Should().Be(0);
        iteration.Time.Should().BeLessThan(TimeSpan.FromSeconds(5));
    }

    /// <summary>**重置后计时继续运行** / The clock keeps running after `Reset`.</summary>
    [Fact]
    public void Reset_ShouldLeaveClockRunning() {
        var iteration = new Iteration();
        iteration.Reset();
        Thread.Sleep(30);

        iteration.Time.Should().BeGreaterThan(TimeSpan.Zero);
    }
}
