#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using System;
using System.Collections.Generic;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.Infrastructure;

/// <summary>
/// WorkingCalendar 特征化测试 / WorkingCalendar characterization tests.
/// 覆盖：工作时间计算、班次处理、时间窗口交集、边界情况。
/// Covers: working-time computation, shift handling, time-window intersection, boundary cases.
/// </summary>
public class WorkingCalendarTests {
    // ===== Helpers =====

    private static DateTimeOffset D(int year, int month, int day, int hour = 0, int minute = 0)
        => new(year, month, day, hour, minute, 0, TimeSpan.Zero);

    private static TimeRange R(DateTimeOffset start, DateTimeOffset end) => new(start, end);

    private static DurationRange DR(TimeSpan lb, TimeSpan ub) => new(lb, ub);

    private static TimeWindow<Flt64> CreateDayWindow() {
        DateTimeOffset start = D(2024, 1, 1);
        DateTimeOffset end = D(2024, 1, 2);
        return new TimeWindow<Flt64>(R(start, end), true, TimeSpan.FromHours(1));
    }

    // =========================================================================
    // TimeRange.Split
    // =========================================================================

    [Fact]
    public void Split_NoBreakTime_SplitsByUnitLb() {
        TimeRange range = R(D(2024, 1, 1, 8), D(2024, 1, 1, 12));
        DurationRange unit = DR(TimeSpan.FromHours(2), TimeSpan.FromHours(4));

        TimeRangeExtensions.SplitTimeRanges result = range.Split(unit);

        // Splits into segments of unit.Lb (2h each): [8-10, 10-12]
        result.Times.Should().HaveCount(2);
        result.BreakTimes.Should().BeEmpty();
        TimeSpan totalWork = TimeSpan.Zero;
        foreach (TimeRange t in result.Times) {
            totalWork += t.Duration;
        }

        totalWork.Should().Be(range.Duration);
    }

    [Fact]
    public void Split_WithBreakTime_SplitsIntoWorkAndBreak() {
        // 8:00-12:00, work unit [2h, 4h], break 30min
        TimeRange range = R(D(2024, 1, 1, 8), D(2024, 1, 1, 12));
        DurationRange unit = DR(TimeSpan.FromHours(2), TimeSpan.FromHours(4));

        TimeRangeExtensions.SplitTimeRanges result = range.Split(unit, breakTime: TimeSpan.FromMinutes(30));

        // Should have work segments and break segments
        result.Times.Should().NotBeEmpty();
        result.BreakTimes.Should().NotBeEmpty();
        // Total work + break should span the range
        TimeSpan totalWork = TimeSpan.Zero;
        foreach (TimeRange t in result.Times) {
            totalWork += t.Duration;
        }

        TimeSpan totalBreak = TimeSpan.Zero;
        foreach (TimeRange t in result.BreakTimes) {
            totalBreak += t.Duration;
        } (totalWork + totalBreak).Should().Be(range.Duration);
    }

    [Fact]
    public void Split_WithMaxDuration_LimitsTotalWork() {
        TimeRange range = R(D(2024, 1, 1, 8), D(2024, 1, 1, 18));
        DurationRange unit = DR(TimeSpan.FromHours(2), TimeSpan.FromHours(4));
        var maxDur = TimeSpan.FromHours(3);

        TimeRangeExtensions.SplitTimeRanges result = range.Split(unit, maxDuration: maxDur);

        TimeSpan totalWork = TimeSpan.Zero;
        foreach (TimeRange t in result.Times) {
            totalWork += t.Duration;
        }

        totalWork.Should().Be(maxDur);
    }

    [Fact]
    public void Split_EmptyRange_ReturnsEmpty() {
        TimeRange range = R(D(2024, 1, 1, 8), D(2024, 1, 1, 8));
        DurationRange unit = DR(TimeSpan.FromHours(1), TimeSpan.FromHours(2));

        TimeRangeExtensions.SplitTimeRanges result = range.Split(unit);

        result.Times.Should().BeEmpty();
        result.BreakTimes.Should().BeEmpty();
    }

    // =========================================================================
    // TimeRange.RSplit
    // =========================================================================

    [Fact]
    public void RSplit_MaxDurationLessThanRange_WorksLikeSplit() {
        TimeRange range = R(D(2024, 1, 1, 8), D(2024, 1, 1, 12));
        DurationRange unit = DR(TimeSpan.FromHours(1), TimeSpan.FromHours(2));
        var maxDur = TimeSpan.FromHours(2);

        TimeRangeExtensions.SplitTimeRanges? result = range.RSplit(unit, maxDuration: maxDur);

        result.Should().NotBeNull();
        TimeSpan totalWork = TimeSpan.Zero;
        foreach (TimeRange t in result!.Times) {
            totalWork += t.Duration;
        }

        totalWork.Should().Be(maxDur);
    }

    [Fact]
    public void RSplit_MaxDurationGreaterThanRange_ReturnsNull() {
        TimeRange range = R(D(2024, 1, 1, 8), D(2024, 1, 1, 10));
        DurationRange unit = DR(TimeSpan.FromHours(1), TimeSpan.FromHours(2));
        var maxDur = TimeSpan.FromHours(5);

        TimeRangeExtensions.SplitTimeRanges? result = range.RSplit(unit, maxDuration: maxDur);

        result.Should().BeNull();
    }

    // =========================================================================
    // TimeRange.FindFrom / FindUntil / Find
    // =========================================================================

    [Fact]
    public void FindFrom_ReturnsRangesStartingAtOrAfterTime() {
        var ranges = new List<TimeRange>
        {
            R(D(2024, 1, 1, 8), D(2024, 1, 1, 10)),
            R(D(2024, 1, 1, 10), D(2024, 1, 1, 12)),
            R(D(2024, 1, 1, 14), D(2024, 1, 1, 16)),
        };

        IReadOnlyList<TimeRange> result = ranges.FindFrom(D(2024, 1, 1, 10));

        result.Should().HaveCount(2);
        result[0].Start.Should().Be(D(2024, 1, 1, 10));
    }

    [Fact]
    public void FindUntil_ReturnsRangesEndingAtOrBeforeTime() {
        var ranges = new List<TimeRange>
        {
            R(D(2024, 1, 1, 8), D(2024, 1, 1, 10)),
            R(D(2024, 1, 1, 10), D(2024, 1, 1, 12)),
            R(D(2024, 1, 1, 14), D(2024, 1, 1, 16)),
        };

        IReadOnlyList<TimeRange> result = ranges.FindUntil(D(2024, 1, 1, 12));

        result.Should().HaveCount(2);
        result[^1].End.Should().Be(D(2024, 1, 1, 12));
    }

    [Fact]
    public void Find_ReturnsIntersectingRanges() {
        var ranges = new List<TimeRange>
        {
            R(D(2024, 1, 1, 8), D(2024, 1, 1, 10)),
            R(D(2024, 1, 1, 10), D(2024, 1, 1, 12)),
            R(D(2024, 1, 1, 14), D(2024, 1, 1, 16)),
        };
        TimeRange query = R(D(2024, 1, 1, 9), D(2024, 1, 1, 11));

        IReadOnlyList<TimeRange> result = ranges.Find(query);

        result.Should().HaveCount(2);
    }

    [Fact]
    public void FindFrom_EmptyList_ReturnsEmpty() {
        var ranges = new List<TimeRange>();
        IReadOnlyList<TimeRange> result = ranges.FindFrom(D(2024, 1, 1, 10));
        result.Should().BeEmpty();
    }

    [Fact]
    public void FindFrom_GenericVersion_WithExtractor() {
        var items = new List<(TimeRange Time, string Name)>
        {
            (R(D(2024, 1, 1, 8), D(2024, 1, 1, 10)), "A"),
            (R(D(2024, 1, 1, 10), D(2024, 1, 1, 12)), "B"),
            (R(D(2024, 1, 1, 14), D(2024, 1, 1, 16)), "C"),
        };

        IReadOnlyList<(TimeRange Time, string Name)> result = items.FindFrom(D(2024, 1, 1, 10), x => x.Time);

        result.Should().HaveCount(2);
        result[0].Name.Should().Be("B");
    }

    // =========================================================================
    // WorkingCalendar - ActualTime (Instant overload)
    // =========================================================================

    [Fact]
    public void ActualTime_Instant_NoUnavailableTimes_ReturnsSameTime() {
        var calendar = new WorkingCalendar<Flt64>(CreateDayWindow());
        DateTimeOffset time = D(2024, 1, 1, 10);

        DateTimeOffset result = calendar.ActualTime(time);

        result.Should().Be(time);
    }

    [Fact]
    public void ActualTime_Instant_WithUnavailableTime_PushesForward() {
        TimeWindow<Flt64> tw = CreateDayWindow();
        var unavailable = new List<TimeRange>
        {
            R(D(2024, 1, 1, 10), D(2024, 1, 1, 12))
        };
        var calendar = new WorkingCalendar<Flt64>(tw, unavailable);

        // Time at 10:30 falls inside unavailable -> pushed to 12:00
        DateTimeOffset result = calendar.ActualTime(D(2024, 1, 1, 10, 30));

        result.Should().Be(D(2024, 1, 1, 12));
    }

    [Fact]
    public void ActualTime_Instant_BeforeUnavailable_ReturnsSameTime() {
        TimeWindow<Flt64> tw = CreateDayWindow();
        var unavailable = new List<TimeRange>
        {
            R(D(2024, 1, 1, 14), D(2024, 1, 1, 16))
        };
        var calendar = new WorkingCalendar<Flt64>(tw, unavailable);

        DateTimeOffset result = calendar.ActualTime(D(2024, 1, 1, 10));

        result.Should().Be(D(2024, 1, 1, 10));
    }

    // =========================================================================
    // WorkingCalendar - ActualTime (TimeRange overload)
    // =========================================================================

    [Fact]
    public void ActualTime_Range_NoUnavailableTimes_ReturnsSameRange() {
        var calendar = new WorkingCalendar<Flt64>(CreateDayWindow());
        TimeRange time = R(D(2024, 1, 1, 8), D(2024, 1, 1, 12));

        WorkingCalendar<Flt64>.ActualTimeResult result = calendar.ActualTime(time);

        result.Time.Should().Be(time);
        result.WorkingTimes.Should().BeEmpty();
        result.BreakTimes.Should().BeEmpty();
        result.ConnectionTimes.Should().BeEmpty();
    }

    [Fact]
    public void ActualTime_Range_WithBreakTime_SplitsIntoWorkAndBreak() {
        var calendar = new WorkingCalendar<Flt64>(CreateDayWindow());
        TimeRange time = R(D(2024, 1, 1, 8), D(2024, 1, 1, 12));
        (DurationRange, TimeSpan) breakTime = (DR(TimeSpan.FromHours(2), TimeSpan.FromHours(4)), TimeSpan.FromMinutes(30));

        WorkingCalendar<Flt64>.ActualTimeResult result = calendar.ActualTime(time, breakTime: breakTime);

        result.WorkingTimes.Should().NotBeEmpty();
        result.BreakTimes.Should().NotBeEmpty();
    }

    [Fact]
    public void ActualTime_Range_WithUnavailableTime_SkipsUnavailable() {
        TimeWindow<Flt64> tw = CreateDayWindow();
        var unavailable = new List<TimeRange>
        {
            R(D(2024, 1, 1, 10), D(2024, 1, 1, 11))
        };
        var calendar = new WorkingCalendar<Flt64>(tw, unavailable);
        // Want 2 hours of work starting at 9:00
        TimeRange time = R(D(2024, 1, 1, 9), D(2024, 1, 1, 11));

        WorkingCalendar<Flt64>.ActualTimeResult result = calendar.ActualTime(time);

        // Should extend past 11:00 because of the gap
        result.Time.End.Should().BeAfter(D(2024, 1, 1, 11));
    }

    [Fact]
    public void ActualTime_Range_FinishEnabled() {
        var calendar = new WorkingCalendar<Flt64>(CreateDayWindow());
        TimeRange time = R(D(2024, 1, 1, 8), D(2024, 1, 1, 12));

        WorkingCalendar<Flt64>.ActualTimeResult result = calendar.ActualTime(time);

        result.FinishEnabled.Should().BeTrue();
    }

    // =========================================================================
    // WorkingCalendar - ValidTimes
    // =========================================================================

    [Fact]
    public void ValidTimes_NoUnavailableTimes_ReturnsWholeRange() {
        var calendar = new WorkingCalendar<Flt64>(CreateDayWindow());
        TimeRange time = R(D(2024, 1, 1, 8), D(2024, 1, 1, 12));

        WorkingCalendar<Flt64>.ValidTimesResult result = calendar.ValidTimes(time);

        result.Times.Should().HaveCount(1);
        result.Times[0].Should().Be(time);
    }

    [Fact]
    public void ValidTimes_WithUnavailableTime_SplitsAroundUnavailable() {
        TimeWindow<Flt64> tw = CreateDayWindow();
        var unavailable = new List<TimeRange>
        {
            R(D(2024, 1, 1, 10), D(2024, 1, 1, 11))
        };
        var calendar = new WorkingCalendar<Flt64>(tw, unavailable);
        TimeRange time = R(D(2024, 1, 1, 8), D(2024, 1, 1, 14));

        WorkingCalendar<Flt64>.ValidTimesResult result = calendar.ValidTimes(time);

        result.Times.Should().HaveCount(2);
        result.Times[0].End.Should().Be(D(2024, 1, 1, 10));
        result.Times[1].Start.Should().Be(D(2024, 1, 1, 11));
    }

    [Fact]
    public void ValidTimes_WithMaxDuration_LimitsValidTime() {
        var calendar = new WorkingCalendar<Flt64>(CreateDayWindow());
        TimeRange time = R(D(2024, 1, 1, 8), D(2024, 1, 1, 18));
        var maxDur = TimeSpan.FromHours(3);

        WorkingCalendar<Flt64>.ValidTimesResult result = calendar.ValidTimes(time, maxDuration: maxDur);

        result.Duration.Should().Be(maxDur);
    }

    [Fact]
    public void ValidTimes_WithBreakTime_SplitsIntoWorkAndBreak() {
        var calendar = new WorkingCalendar<Flt64>(CreateDayWindow());
        TimeRange time = R(D(2024, 1, 1, 8), D(2024, 1, 1, 14));
        (DurationRange, TimeSpan) breakTime = (DR(TimeSpan.FromHours(2), TimeSpan.FromHours(3)), TimeSpan.FromMinutes(30));

        WorkingCalendar<Flt64>.ValidTimesResult result = calendar.ValidTimes(time, breakTime: breakTime);

        result.Times.Should().NotBeEmpty();
        result.BreakTimes.Should().NotBeEmpty();
    }

    // =========================================================================
    // Boundary cases
    // =========================================================================

    [Fact]
    public void ActualTime_Instant_AtDistantPast_ReturnsDistantPast() {
        var calendar = new WorkingCalendar<Flt64>(CreateDayWindow());
        DateTimeOffset result = calendar.ActualTime(TimeRange.DistantPast);
        result.Should().Be(TimeRange.DistantPast);
    }

    [Fact]
    public void WorkingCalendar_EmptyUnavailableTimes_SortsEmpty() {
        var calendar = new WorkingCalendar<Flt64>(CreateDayWindow(), Array.Empty<TimeRange>());
        calendar.UnavailableTimes.Should().BeEmpty();
    }

    [Fact]
    public void WorkingCalendar_SortsUnavailableTimes() {
        TimeWindow<Flt64> tw = CreateDayWindow();
        var unavailable = new List<TimeRange>
        {
            R(D(2024, 1, 1, 14), D(2024, 1, 1, 15)),
            R(D(2024, 1, 1, 10), D(2024, 1, 1, 11)),
            R(D(2024, 1, 1, 12), D(2024, 1, 1, 13)),
        };
        var calendar = new WorkingCalendar<Flt64>(tw, unavailable);

        calendar.UnavailableTimes.Should().HaveCount(3);
        calendar.UnavailableTimes[0].Start.Should().Be(D(2024, 1, 1, 10));
        calendar.UnavailableTimes[1].Start.Should().Be(D(2024, 1, 1, 12));
        calendar.UnavailableTimes[2].Start.Should().Be(D(2024, 1, 1, 14));
    }

    [Fact]
    public void ActualTime_Eq_ReturnsTrueForMatchingRange() {
        var calendar = new WorkingCalendar<Flt64>(CreateDayWindow());
        TimeRange time = R(D(2024, 1, 1, 8), D(2024, 1, 1, 12));
        WorkingCalendar<Flt64>.ActualTimeResult result = calendar.ActualTime(time);
        result.Eq(time).Should().BeTrue();
    }

    // =========================================================================
    // Duration computation
    // =========================================================================

    [Fact]
    public void ActualTime_Duration_EqualsTimeDuration() {
        var calendar = new WorkingCalendar<Flt64>(CreateDayWindow());
        TimeRange time = R(D(2024, 1, 1, 8), D(2024, 1, 1, 12));

        WorkingCalendar<Flt64>.ActualTimeResult result = calendar.ActualTime(time);

        result.Duration.Should().Be(time.Duration);
    }

    [Fact]
    public void ValidTimes_Duration_ComputedCorrectly() {
        TimeWindow<Flt64> tw = CreateDayWindow();
        var unavailable = new List<TimeRange>
        {
            R(D(2024, 1, 1, 10), D(2024, 1, 1, 11))
        };
        var calendar = new WorkingCalendar<Flt64>(tw, unavailable);
        TimeRange time = R(D(2024, 1, 1, 8), D(2024, 1, 1, 14));

        WorkingCalendar<Flt64>.ValidTimesResult result = calendar.ValidTimes(time);

        // 8-10 (2h) + 11-14 (3h) = 5h
        result.Duration.Should().Be(TimeSpan.FromHours(5));
    }

    [Fact]
    public void ActualTime_WorkingDuration_ComputedCorrectly() {
        var calendar = new WorkingCalendar<Flt64>(CreateDayWindow());
        TimeRange time = R(D(2024, 1, 1, 8), D(2024, 1, 1, 12));
        (DurationRange, TimeSpan) breakTime = (DR(TimeSpan.FromHours(2), TimeSpan.FromHours(4)), TimeSpan.FromMinutes(30));

        WorkingCalendar<Flt64>.ActualTimeResult result = calendar.ActualTime(time, breakTime: breakTime);

        result.WorkingDuration.Should().BeGreaterThan(TimeSpan.Zero);
        result.WorkingDuration.Should().BeLessThanOrEqualTo(time.Duration);
    }

    // =========================================================================
    // Multiple unavailable times
    // =========================================================================

    [Fact]
    public void ActualTime_Instant_MultipleUnavailableTimes_PushesPastAll() {
        TimeWindow<Flt64> tw = CreateDayWindow();
        var unavailable = new List<TimeRange>
        {
            R(D(2024, 1, 1, 9), D(2024, 1, 1, 10)),
            R(D(2024, 1, 1, 11), D(2024, 1, 1, 12)),
        };
        var calendar = new WorkingCalendar<Flt64>(tw, unavailable);

        // At 9:30 -> pushed to 10:00 (past first unavailable)
        DateTimeOffset result = calendar.ActualTime(D(2024, 1, 1, 9, 30));
        result.Should().Be(D(2024, 1, 1, 10));
    }

    [Fact]
    public void ValidTimes_MultipleUnavailableTimes_CreatesMultipleSegments() {
        TimeWindow<Flt64> tw = CreateDayWindow();
        var unavailable = new List<TimeRange>
        {
            R(D(2024, 1, 1, 10), D(2024, 1, 1, 11)),
            R(D(2024, 1, 1, 14), D(2024, 1, 1, 15)),
        };
        var calendar = new WorkingCalendar<Flt64>(tw, unavailable);
        TimeRange time = R(D(2024, 1, 1, 8), D(2024, 1, 1, 18));

        WorkingCalendar<Flt64>.ValidTimesResult result = calendar.ValidTimes(time);

        // 3 segments: 8-10, 11-14, 15-18
        result.Times.Should().HaveCount(3);
    }
}
