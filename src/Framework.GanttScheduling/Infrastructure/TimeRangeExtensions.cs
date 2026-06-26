#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;

/// <summary>
/// TimeRange 拆分与查找扩展方法 / TimeRange split and find extension methods.
/// 从 kotlin TimeRange.kt 的 split/rsplit/findFrom/findUntil/find 扩展函数移植。
/// Ported from kotlin TimeRange.kt split/rsplit/findFrom/findUntil/find extension functions.
/// </summary>
public static class TimeRangeExtensions {
    /// <summary>
    /// 拆分结果，包含工作时间段和休息时间段 / Split result containing working time ranges and break time ranges.
    /// </summary>
    /// <param name="Times">工作时间段列表 / The list of working time ranges.</param>
    /// <param name="BreakTimes">休息时间段列表 / The list of break time ranges.</param>
    public sealed record SplitTimeRanges(
        IReadOnlyList<TimeRange> Times,
        IReadOnlyList<TimeRange> BreakTimes
    );

    /// <summary>
    /// 按持续时间单元拆分时间范围 / Split the time range by duration unit.
    /// 从 kotlin TimeRange.split(DurationRange, ...) 移植。
    /// </summary>
    /// <param name="range">待拆分的时间范围 / The time range to split.</param>
    /// <param name="unit">持续时间单元范围 / The duration unit range [lb, ub].</param>
    /// <param name="currentDuration">当前已消耗的持续时间 / The current consumed duration.</param>
    /// <param name="maxDuration">最大持续时间限制 / The maximum duration limit.</param>
    /// <param name="breakTime">休息时间 / The break time duration.</param>
    /// <returns>拆分结果 / The split result.</returns>
    public static SplitTimeRanges Split(
        this TimeRange range,
        DurationRange unit,
        TimeSpan? currentDuration = null,
        TimeSpan? maxDuration = null,
        TimeSpan? breakTime = null) {
        var times = new List<TimeRange>();
        var breakTimes = new List<TimeRange>();
        DateTimeOffset currentTime = range.Start;
        TimeSpan totalDuration = TimeSpan.Zero;
        TimeSpan curDur = currentDuration ?? TimeSpan.Zero;

        while (currentTime < range.End && (maxDuration == null || totalDuration < maxDuration)) {
            if (currentTime == range.Start && curDur >= unit.Lb && breakTime != null) {
                TimeSpan duration = Min(
                    range.End - currentTime,
                    maxDuration ?? TimeSpan.MaxValue
                );
                if (curDur + duration <= unit.Ub) {
                    times.Add(new TimeRange(currentTime, currentTime + duration));
                    currentTime += duration;
                    totalDuration += duration;
                    break;
                }
                else {
                    breakTimes.Add(new TimeRange(currentTime, currentTime + breakTime.Value));
                    currentTime += breakTime.Value;
                    continue;
                }
            }
            else {
                TimeSpan duration = Min3(
                    unit.Lb - ((breakTime != null && currentTime == range.Start) ? curDur : TimeSpan.Zero),
                    range.End - currentTime,
                    maxDuration != null ? maxDuration.Value - totalDuration : TimeSpan.MaxValue
                );
                if (maxDuration != null
                    && ((duration + totalDuration + (unit.Ub - unit.Lb)) >= maxDuration.Value
                        || (currentTime + duration + (unit.Ub - unit.Lb)) >= range.End)) {
                    TimeSpan extraDuration = Min(
                        maxDuration.Value - totalDuration,
                        range.End - currentTime
                    );
                    times.Add(new TimeRange(currentTime, currentTime + extraDuration));
                    totalDuration += extraDuration;
                    currentTime += extraDuration;
                }
                else if (breakTime != null
                    && duration + ((currentTime == range.Start) ? curDur : TimeSpan.Zero) == unit.Lb
                    && (currentTime + duration) != range.End) {
                    if (currentTime + duration + breakTime.Value + (unit.Ub - unit.Lb) >= range.End
                        || currentTime + duration + breakTime.Value >= range.End) {
                        times.Add(new TimeRange(currentTime, range.End - breakTime.Value));
                        breakTimes.Add(new TimeRange(range.End - breakTime.Value, range.End));
                        totalDuration += (range.End - breakTime.Value) - currentTime;
                        currentTime = range.End;
                    }
                    else {
                        times.Add(new TimeRange(currentTime, currentTime + duration));
                        breakTimes.Add(new TimeRange(currentTime + duration, currentTime + duration + breakTime.Value));
                        totalDuration += duration;
                        currentTime += duration + breakTime.Value;
                    }
                }
                else {
                    times.Add(new TimeRange(currentTime, currentTime + duration));
                    totalDuration += duration;
                    currentTime += duration;
                }
            }
        }
        return new SplitTimeRanges(times, breakTimes);
    }

    /// <summary>
    /// 反向按持续时间单元拆分时间范围 / Reverse split the time range by duration unit.
    /// 当 maxDuration 大于区间时长时返回 null（与 kotlin 行为一致）。
    /// Returns null when maxDuration exceeds range duration (consistent with kotlin behavior).
    /// </summary>
    /// <param name="range">待拆分的时间范围 / The time range to split.</param>
    /// <param name="unit">持续时间单元范围 / The duration unit range.</param>
    /// <param name="maxDuration">最大持续时间限制 / The maximum duration limit.</param>
    /// <param name="breakTime">休息时间 / The break time duration.</param>
    /// <returns>拆分结果，若 maxDuration 超过区间时长则为 null / The split result, or null if maxDuration exceeds range duration.</returns>
    public static SplitTimeRanges? RSplit(
        this TimeRange range,
        DurationRange unit,
        TimeSpan? maxDuration = null,
        TimeSpan? breakTime = null) {
        if (maxDuration == null || maxDuration.Value <= range.Duration) {
            return range.Split(
                unit: unit,
                currentDuration: TimeSpan.Zero,
                maxDuration: maxDuration,
                breakTime: breakTime
            );
        }
        return null;
    }

    /// <summary>
    /// 从指定时间点开始查找相交的时间范围列表 / Find time ranges from the specified instant.
    /// 从 kotlin List&lt;TimeRange&gt;.findFrom(Instant) 移植。
    /// </summary>
    /// <param name="ranges">已排序的时间范围列表 / The sorted list of time ranges.</param>
    /// <param name="time">起始时间点 / The start instant.</param>
    /// <returns>匹配的时间范围列表 / The list of matching time ranges.</returns>
    public static IReadOnlyList<TimeRange> FindFrom(
        this IReadOnlyList<TimeRange> ranges,
        DateTimeOffset time) {
        (int, int)? bounds = FindFromImpl(ranges, time, static x => x);
        if (bounds is null) {
            return Array.Empty<TimeRange>();
        }

        return ranges.Skip(bounds.Value.Item1).Take(bounds.Value.Item2 - bounds.Value.Item1).ToList();
    }

    /// <summary>
    /// 从指定时间点开始查找相交的元素 / Find elements from the specified instant.
    /// 从 kotlin List&lt;T&gt;.findFrom(Instant, Extractor) 移植。
    /// </summary>
    /// <typeparam name="T">列表元素类型 / The list element type.</typeparam>
    /// <param name="list">已排序的元素列表 / The sorted list of elements.</param>
    /// <param name="time">起始时间点 / The start instant.</param>
    /// <param name="extractor">时间范围提取函数 / The time range extraction function.</param>
    /// <returns>匹配的元素列表 / The list of matching elements.</returns>
    public static IReadOnlyList<T> FindFrom<T>(
        this IReadOnlyList<T> list,
        DateTimeOffset time,
        Func<T, TimeRange> extractor) {
        (int, int)? bounds = FindFromImpl(list, time, extractor);
        if (bounds is null) {
            return Array.Empty<T>();
        }

        return list.Skip(bounds.Value.Item1).Take(bounds.Value.Item2 - bounds.Value.Item1).ToList();
    }

    /// <summary>
    /// 查找直到指定时间点的时间范围列表 / Find time ranges until the specified instant.
    /// 从 kotlin List&lt;TimeRange&gt;.findUntil(Instant) 移植。
    /// </summary>
    /// <param name="ranges">已排序的时间范围列表 / The sorted list of time ranges.</param>
    /// <param name="time">结束时间点 / The end instant.</param>
    /// <returns>匹配的时间范围列表 / The list of matching time ranges.</returns>
    public static IReadOnlyList<TimeRange> FindUntil(
        this IReadOnlyList<TimeRange> ranges,
        DateTimeOffset time) {
        (int, int)? bounds = FindUntilImpl(ranges, time, static x => x);
        if (bounds is null) {
            return Array.Empty<TimeRange>();
        }

        return ranges.Skip(bounds.Value.Item1).Take(bounds.Value.Item2 - bounds.Value.Item1).ToList();
    }

    /// <summary>
    /// 查找直到指定时间点的元素 / Find elements until the specified instant.
    /// 从 kotlin List&lt;T&gt;.findUntil(Instant, Extractor) 移植。
    /// </summary>
    /// <typeparam name="T">列表元素类型 / The list element type.</typeparam>
    /// <param name="list">已排序的元素列表 / The sorted list of elements.</param>
    /// <param name="time">结束时间点 / The end instant.</param>
    /// <param name="extractor">时间范围提取函数 / The time range extraction function.</param>
    /// <returns>匹配的元素列表 / The list of matching elements.</returns>
    public static IReadOnlyList<T> FindUntil<T>(
        this IReadOnlyList<T> list,
        DateTimeOffset time,
        Func<T, TimeRange> extractor) {
        (int, int)? bounds = FindUntilImpl(list, time, extractor);
        if (bounds is null) {
            return Array.Empty<T>();
        }

        return list.Skip(bounds.Value.Item1).Take(bounds.Value.Item2 - bounds.Value.Item1).ToList();
    }

    /// <summary>
    /// 查找与给定时间范围相交的时间范围列表 / Find time ranges intersecting with the given time range.
    /// 从 kotlin List&lt;TimeRange&gt;.find(TimeRange) 移植。
    /// </summary>
    /// <param name="ranges">已排序的时间范围列表 / The sorted list of time ranges.</param>
    /// <param name="time">目标时间范围 / The target time range.</param>
    /// <returns>匹配的时间范围列表 / The list of matching time ranges.</returns>
    public static IReadOnlyList<TimeRange> Find(
        this IReadOnlyList<TimeRange> ranges,
        TimeRange time) {
        (int, int)? bounds = FindImpl(ranges, time, static x => x);
        if (bounds is null) {
            return Array.Empty<TimeRange>();
        }

        return ranges.Skip(bounds.Value.Item1).Take(bounds.Value.Item2 - bounds.Value.Item1).ToList();
    }

    /// <summary>
    /// 查找与给定时间范围相交的元素 / Find elements intersecting with the given time range.
    /// 从 kotlin List&lt;T&gt;.find(TimeRange, Extractor) 移植。
    /// </summary>
    /// <typeparam name="T">列表元素类型 / The list element type.</typeparam>
    /// <param name="list">已排序的元素列表 / The sorted list of elements.</param>
    /// <param name="time">目标时间范围 / The target time range.</param>
    /// <param name="extractor">时间范围提取函数 / The time range extraction function.</param>
    /// <returns>匹配的元素列表 / The list of matching elements.</returns>
    public static IReadOnlyList<T> Find<T>(
        this IReadOnlyList<T> list,
        TimeRange time,
        Func<T, TimeRange> extractor) {
        (int, int)? bounds = FindImpl(list, time, extractor);
        if (bounds is null) {
            return Array.Empty<T>();
        }

        return list.Skip(bounds.Value.Item1).Take(bounds.Value.Item2 - bounds.Value.Item1).ToList();
    }

    // ===== Binary search implementations =====

    /// <summary>二分查找下界实现 / Binary search lower bound implementation.</summary>
    internal static int FindLowerBound<T>(IReadOnlyList<T> list, TimeRange time, Func<T, TimeRange> extractor) {
        if (time.Start <= extractor(list[0]).Start) {
            return 0;
        }
        if (time.Start >= extractor(list[list.Count - 1]).End) {
            return list.Count;
        }

        int left = 0;
        int right = list.Count;
        while (left < right) {
            int mid = (left + right) / 2;
            if (time.Start < extractor(list[mid]).Start) {
                right = mid;
            }
            else if (time.Start >= extractor(list[mid]).End) {
                left = mid + 1;
            }
            else {
                return mid;
            }
        }
        return left;
    }

    /// <summary>二分查找上界实现 / Binary search upper bound implementation.</summary>
    internal static int FindUpperBound<T>(IReadOnlyList<T> list, TimeRange time, Func<T, TimeRange> extractor) {
        if (time.End <= extractor(list[0]).Start) {
            return 0;
        }
        if (time.End >= extractor(list[list.Count - 1]).End) {
            return list.Count;
        }

        int left = 0;
        int right = list.Count;
        while (left < right) {
            int mid = (left + right) / 2;
            if (time.End < extractor(list[mid]).Start) {
                right = mid;
            }
            else if (time.End >= extractor(list[mid]).End) {
                left = mid + 1;
            }
            else {
                return mid + 1;
            }
        }
        return left;
    }

    private static (int, int)? FindImpl<T>(IReadOnlyList<T> list, TimeRange time, Func<T, TimeRange> extractor) {
        if (list.Count == 0) {
            return null;
        }

        if (list.Count == 1) {
            return extractor(list[0]).WithIntersection(time) ? (0, 1) : null;
        }
        int lower = FindLowerBound(list, time, extractor);
        int upper = FindUpperBound(list, time, extractor);
        return (lower, upper);
    }

    private static (int, int)? FindFromImpl<T>(IReadOnlyList<T> list, DateTimeOffset time, Func<T, TimeRange> extractor) => FindImpl(list, new TimeRange(time), extractor);

    private static (int, int)? FindUntilImpl<T>(IReadOnlyList<T> list, DateTimeOffset time, Func<T, TimeRange> extractor) => FindImpl(list, new TimeRange(TimeRange.DistantPast, time), extractor);

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

    private static TimeSpan Min3(TimeSpan a, TimeSpan b, TimeSpan c) => Min(Min(a, b), c);
}
