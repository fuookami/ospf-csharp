#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;

/// <summary>
/// 日历时长物理量类型别名 / Calendar duration quantity type alias.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public delegate Quantity<V> CalendarDurationQuantity<V>(V value, PhysicalUnit unit) where V : struct, IRealNumber<V>;

/// <summary>
/// 工作日历，管理不可用时间和实际时间计算 / Working calendar managing unavailable times and actual time calculations.
/// 从 kotlin WorkingCalendar.kt WorkingCalendar 类移植。Ported from kotlin WorkingCalendar.kt WorkingCalendar class.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public class WorkingCalendar<V> where V : struct, IRealNumber<V> {
    /// <summary>时间窗口 / The time window.</summary>
    public TimeWindow<V> TimeWindow { get; }

    /// <summary>排序后的不可用时间列表 / Sorted list of unavailable times.</summary>
    public IReadOnlyList<TimeRange> UnavailableTimes { get; }

    /// <summary>
    /// 构造工作日历 / Construct a working calendar.
    /// </summary>
    /// <param name="timeWindow">时间窗口 / The time window.</param>
    /// <param name="unavailableTimes">不可用时间列表 / The list of unavailable times.</param>
    public WorkingCalendar(
        TimeWindow<V> timeWindow,
        IReadOnlyList<TimeRange>? unavailableTimes = null) {
        TimeWindow = timeWindow;
        UnavailableTimes = (unavailableTimes ?? Array.Empty<TimeRange>()).OrderBy(t => t.Start).ToList();
    }

    // =========================================================================
    // Nested types: ActualTime, ValidTimes
    // =========================================================================

    /// <summary>
    /// 实际时间结果，包含工作时间、休息时间和连接时间 /
    /// Actual time result containing working times, break times, and connection times.
    /// </summary>
    public sealed record ActualTimeResult {
        /// <summary>实际时间范围 / The actual time range.</summary>
        public required TimeRange Time { get; init; }
        /// <summary>工作时间段列表 / The list of working time ranges.</summary>
        public required IReadOnlyList<TimeRange> WorkingTimes { get; init; }
        /// <summary>休息时间段列表 / The list of break time ranges.</summary>
        public required IReadOnlyList<TimeRange> BreakTimes { get; init; }
        /// <summary>连接时间段列表 / The list of connection time ranges.</summary>
        public required IReadOnlyList<TimeRange> ConnectionTimes { get; init; }

        /// <summary>实际总时长 / Actual total duration.</summary>
        public TimeSpan Duration => Time.Duration;
        /// <summary>工作时长 / Working duration.</summary>
        public TimeSpan WorkingDuration => WorkingTimes.Aggregate(TimeSpan.Zero, (acc, t) => acc + t.Duration);
        /// <summary>休息时长 / Break duration.</summary>
        public TimeSpan BreakDuration => BreakTimes.Aggregate(TimeSpan.Zero, (acc, t) => acc + t.Duration);
        /// <summary>连接时长 / Connection duration.</summary>
        public TimeSpan ConnectionDuration => ConnectionTimes.Aggregate(TimeSpan.Zero, (acc, t) => acc + t.Duration);
        /// <summary>是否已计算完成 / Whether finish time is determined.</summary>
        public bool FinishEnabled => Time.Start != TimeRange.DistantPast && Time.End != TimeRange.DistantFuture;

        /// <summary>
        /// 判断实际时间范围是否与给定时间范围相等 /
        /// Check whether the actual time range equals the given time range.
        /// </summary>
        /// <param name="time">目标时间范围 / The target time range.</param>
        /// <returns>是否相等 / Whether equal.</returns>
        public bool Eq(TimeRange time) => Time == time;
    }

    /// <summary>
    /// 有效时间结果，包含时间段、休息时间和连接时间 /
    /// Valid times result containing time ranges, break times, and connection times.
    /// </summary>
    public sealed record ValidTimesResult {
        /// <summary>有效时间段列表 / The list of valid time ranges.</summary>
        public required IReadOnlyList<TimeRange> Times { get; init; }
        /// <summary>休息时间段列表 / The list of break time ranges.</summary>
        public required IReadOnlyList<TimeRange> BreakTimes { get; init; }
        /// <summary>连接时间段列表 / The list of connection time ranges.</summary>
        public required IReadOnlyList<TimeRange> ConnectionTimes { get; init; }

        /// <summary>有效总时长 / Total valid duration.</summary>
        public TimeSpan Duration => Times.Aggregate(TimeSpan.Zero, (acc, t) => acc + t.Duration);
        /// <summary>休息总时长 / Total break duration.</summary>
        public TimeSpan BreakDuration => BreakTimes.Aggregate(TimeSpan.Zero, (acc, t) => acc + t.Duration);
        /// <summary>连接总时长 / Total connection duration.</summary>
        public TimeSpan ConnectionDuration => ConnectionTimes.Aggregate(TimeSpan.Zero, (acc, t) => acc + t.Duration);
    }

    // =========================================================================
    // Static (internal) helper methods
    // =========================================================================

    /// <summary>
    /// 查找在指定时间之前或之时结束的最后一个时间范围的索引 /
    /// Find the index of the last time range ended before or at the specified time.
    /// </summary>
    internal static int IndexOfLastEndedBeforeOrAt(IReadOnlyList<TimeRange> times, DateTimeOffset time) {
        for (int i = times.Count - 1; i >= 0; i--) {
            if (time >= times[i].End) {
                return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// 查找在指定时间之后或之时开始的第一个时间范围的索引 /
    /// Find the index of the first time range started after or at the specified time.
    /// </summary>
    internal static int IndexOfFirstStartedAfterOrAt(IReadOnlyList<TimeRange> times, DateTimeOffset time) {
        for (int i = 0; i < times.Count; i++) {
            if (time <= times[i].Start) {
                return i;
            }
        }
        return -1;
    }

    /// <summary>计算所有时间范围的最大结束时间 / Calculate the maximum end time across all time ranges.</summary>
    internal static DateTimeOffset MaxEndTime(
        IReadOnlyList<TimeRange> times,
        IReadOnlyList<TimeRange> breakTimes,
        IReadOnlyList<TimeRange> connectionTimes) {
        DateTimeOffset maximum = TimeRange.DistantPast;
        foreach (TimeRange t in times) {
            if (t.End > maximum) {
                maximum = t.End;
            }
        }
        foreach (TimeRange t in breakTimes) {
            if (t.End > maximum) {
                maximum = t.End;
            }
        }
        foreach (TimeRange t in connectionTimes) {
            if (t.End > maximum) {
                maximum = t.End;
            }
        }
        return maximum;
    }

    /// <summary>计算所有时间范围的最小开始时间 / Calculate the minimum start time across all time ranges.</summary>
    internal static DateTimeOffset MinStartTime(
        IReadOnlyList<TimeRange> times,
        IReadOnlyList<TimeRange> breakTimes,
        IReadOnlyList<TimeRange> connectionTimes) {
        DateTimeOffset minimum = TimeRange.DistantFuture;
        foreach (TimeRange t in times) {
            if (t.Start < minimum) {
                minimum = t.Start;
            }
        }
        foreach (TimeRange t in breakTimes) {
            if (t.Start < minimum) {
                minimum = t.Start;
            }
        }
        foreach (TimeRange t in connectionTimes) {
            if (t.Start < minimum) {
                minimum = t.Start;
            }
        }
        return minimum;
    }

    // =========================================================================
    // Static actualTime (Instant overload) — companion.actualTime(Instant)
    // =========================================================================

    /// <summary>
    /// 计算考虑不可用时间后的实际时间点（静态）/
    /// Calculate the actual time point considering unavailable times (static).
    /// </summary>
    protected static DateTimeOffset ActualTimeStatic(
        DateTimeOffset time,
        IReadOnlyList<TimeRange> unavailableTimes,
        DurationRange? beforeConnectionTime = null,
        DurationRange? afterConnectionTime = null,
        Func<TimeRange, DurationRange?>? beforeConditionalConnectionTime = null,
        Func<TimeRange, DurationRange?>? afterConditionalConnectionTime = null) {
        IReadOnlyList<TimeRange> mergedTimes = TimeRange.Merge(unavailableTimes);
        if (mergedTimes.Count == 0) {
            return time;
        }

        DateTimeOffset currentTime = time;
        foreach (TimeRange thisUnavailableTime in mergedTimes) {
            TimeSpan beforeMax = MaxDuration(
                beforeConditionalConnectionTime?.Invoke(thisUnavailableTime)?.Lb ?? TimeSpan.Zero,
                beforeConnectionTime?.Lb ?? TimeSpan.Zero);

            if (time <= thisUnavailableTime.Start - beforeMax) {
                return currentTime;
            }

            TimeSpan afterMax = MaxDuration(
                afterConditionalConnectionTime?.Invoke(thisUnavailableTime)?.Lb ?? TimeSpan.Zero,
                afterConnectionTime?.Lb ?? TimeSpan.Zero);

            currentTime = MaxDateTime(currentTime, thisUnavailableTime.End + afterMax);
        }
        return currentTime;
    }

    // =========================================================================
    // Static actualTime (TimeRange overload) — companion.actualTime(TimeRange)
    // =========================================================================

    /// <summary>
    /// 计算考虑不可用时间后的实际时间范围（静态）/
    /// Calculate the actual time range considering unavailable times (static).
    /// </summary>
    protected static ActualTimeResult ActualTimeStatic(
        TimeRange time,
        IReadOnlyList<TimeRange> unavailableTimes,
        DurationRange? beforeConnectionTime = null,
        DurationRange? afterConnectionTime = null,
        Func<TimeRange, DurationRange?>? beforeConditionalConnectionTime = null,
        Func<TimeRange, DurationRange?>? afterConditionalConnectionTime = null,
        TimeSpan? currentDuration = null,
        (DurationRange unit, TimeSpan breakDuration)? breakTime = null) {
        IReadOnlyList<TimeRange> mergedTimes = TimeRange.Merge(unavailableTimes);
        if (mergedTimes.Count == 0) {
            if (breakTime != null) {
                DateTimeOffset currentTime = time.Start;
                TimeSpan totalDuration = TimeSpan.Zero;
                var workingTimes = new List<TimeRange>();
                var breakTimesList = new List<TimeRange>();
                while (totalDuration != time.Duration) {
                    TimeSpan thisDuration = MinDuration(breakTime.Value.unit.Lb, time.Duration - totalDuration);
                    workingTimes.Add(new TimeRange(currentTime, currentTime + thisDuration));
                    if (thisDuration < breakTime.Value.unit.Ub && (totalDuration + thisDuration) != time.Duration) {
                        currentTime += thisDuration;
                    }
                    else {
                        breakTimesList.Add(new TimeRange(
                            currentTime + thisDuration,
                            currentTime + thisDuration + breakTime.Value.breakDuration));
                        currentTime += thisDuration + breakTime.Value.breakDuration;
                    }
                    totalDuration += thisDuration;
                }
                return new ActualTimeResult {
                    Time = new TimeRange(time.Start, currentTime),
                    WorkingTimes = workingTimes,
                    BreakTimes = breakTimesList,
                    ConnectionTimes = Array.Empty<TimeRange>()
                };
            }
            else {
                return new ActualTimeResult {
                    Time = time,
                    WorkingTimes = Array.Empty<TimeRange>(),
                    BreakTimes = Array.Empty<TimeRange>(),
                    ConnectionTimes = Array.Empty<TimeRange>()
                };
            }
        }
        else {
            DateTimeOffset currentTime = time.Start;
            TimeSpan totalDuration = TimeSpan.Zero;
            var workingTimes = new List<TimeRange>();
            var breakTimesList = new List<TimeRange>();
            var connectionTimes = new List<TimeRange>();
            int i = IndexOfLastEndedBeforeOrAt(mergedTimes, currentTime);

            while (totalDuration != time.Duration) {
                if (i == mergedTimes.Count - 1 && currentTime == TimeRange.DistantFuture) {
                    break;
                }
                else if (i != mergedTimes.Count - 1 && mergedTimes[i + 1].Contains(currentTime)) {
                    currentTime = mergedTimes[i + 1].End;
                    i += 1;
                    continue;
                }

                DurationRange? thisBeforeConnectionTime = null;
                if (i < mergedTimes.Count - 1 && (beforeConnectionTime != null || beforeConditionalConnectionTime != null)) {
                    thisBeforeConnectionTime = new DurationRange(
                        MaxDuration(
                            beforeConditionalConnectionTime?.Invoke(mergedTimes[i + 1])?.Lb ?? TimeSpan.Zero,
                            beforeConnectionTime?.Lb ?? TimeSpan.Zero),
                        MaxDuration(
                            beforeConditionalConnectionTime?.Invoke(mergedTimes[i + 1])?.Ub ?? TimeSpan.Zero,
                            beforeConnectionTime?.Ub ?? TimeSpan.Zero));
                }

                DurationRange? thisAfterConnectionTime = null;
                if (i != -1 && i != 0 && currentTime <= mergedTimes[i].End
                    && (afterConnectionTime != null || afterConditionalConnectionTime != null)) {
                    thisAfterConnectionTime = new DurationRange(
                        MaxDuration(
                            afterConditionalConnectionTime?.Invoke(mergedTimes[i])?.Lb ?? TimeSpan.Zero,
                            afterConnectionTime?.Lb ?? TimeSpan.Zero),
                        MaxDuration(
                            afterConditionalConnectionTime?.Invoke(mergedTimes[i])?.Ub ?? TimeSpan.Zero,
                            afterConnectionTime?.Ub ?? TimeSpan.Zero));
                }

                DurationRange? thisMaxDuration = null;
                if (i == -1 && thisBeforeConnectionTime != null) {
                    thisMaxDuration = new DurationRange(
                        mergedTimes[0].Start - time.Start - thisBeforeConnectionTime.Ub,
                        mergedTimes[0].Start - time.Start - thisBeforeConnectionTime.Lb);
                }
                else if (i != mergedTimes.Count - 1 && (thisBeforeConnectionTime != null || thisAfterConnectionTime != null)) {
                    thisMaxDuration = new DurationRange(
                        mergedTimes[i + 1].Start - mergedTimes[i].End
                            - (thisBeforeConnectionTime?.Ub ?? TimeSpan.Zero)
                            - (thisAfterConnectionTime?.Ub ?? TimeSpan.Zero),
                        mergedTimes[i + 1].Start - mergedTimes[i].End
                            - (thisBeforeConnectionTime?.Lb ?? TimeSpan.Zero)
                            - (thisAfterConnectionTime?.Lb ?? TimeSpan.Zero));
                }

                (TimeSpan? actualBefore, TimeSpan? actualAfter) = ComputeActualConnectionTimes(
                    thisMaxDuration, totalDuration, time.Duration,
                    thisBeforeConnectionTime, thisAfterConnectionTime);

                // after-connection
                if (actualAfter is { } aa && aa > TimeSpan.Zero) {
                    connectionTimes.Add(new TimeRange(currentTime, currentTime + aa));
                    currentTime += aa;
                }

                DateTimeOffset thisEndTime = (i == mergedTimes.Count - 1)
                    ? TimeRange.DistantFuture
                    : mergedTimes[i + 1].Start - (actualBefore ?? TimeSpan.Zero);

                var baseTime = new TimeRange(currentTime, thisEndTime);

                if (breakTime != null) {
                    TimeSpan offset = (currentTime == time.Start) ? (currentDuration ?? TimeSpan.Zero) : TimeSpan.Zero;
                    TimeRangeExtensions.SplitTimeRanges splitResult = baseTime.Split(
                        unit: breakTime.Value.unit,
                        currentDuration: offset,
                        maxDuration: time.Duration - totalDuration,
                        breakTime: breakTime.Value.breakDuration);
                    foreach (TimeRange vt in splitResult.Times) { totalDuration += vt.Duration; }
                    workingTimes.AddRange(splitResult.Times);
                    breakTimesList.AddRange(splitResult.BreakTimes);
                    currentTime = MaxEndTime(splitResult.Times, splitResult.BreakTimes, Array.Empty<TimeRange>());
                }
                else {
                    TimeSpan duration = MinDuration(baseTime.Duration, time.Duration - totalDuration);
                    totalDuration += duration;
                    workingTimes.Add(new TimeRange(currentTime, currentTime + duration));
                    currentTime += duration;
                }

                if (totalDuration != time.Duration && actualBefore is { } ab && ab > TimeSpan.Zero) {
                    connectionTimes.Add(new TimeRange(thisEndTime, thisEndTime + ab));
                }
                else if (totalDuration == time.Duration) {
                    break;
                }

                currentTime = mergedTimes[i + 1].End;
                i += 1;
            }

            return new ActualTimeResult {
                Time = new TimeRange(time.Start, currentTime),
                WorkingTimes = workingTimes,
                BreakTimes = breakTimesList,
                ConnectionTimes = connectionTimes
            };
        }
    }

    // =========================================================================
    // Static validTimes — companion.validTimes
    // =========================================================================

    /// <summary>
    /// 计算有效时间范围（静态）/ Calculate valid time ranges (static).
    /// </summary>
    protected static ValidTimesResult ValidTimesStatic(
        TimeRange time,
        IReadOnlyList<TimeRange> unavailableTimes,
        DurationRange? beforeConnectionTime = null,
        DurationRange? afterConnectionTime = null,
        Func<TimeRange, DurationRange?>? beforeConditionalConnectionTime = null,
        Func<TimeRange, DurationRange?>? afterConditionalConnectionTime = null,
        TimeSpan? currentDuration = null,
        TimeSpan? maxDuration = null,
        (DurationRange unit, TimeSpan breakDuration)? breakTime = null) {
        IReadOnlyList<TimeRange> mergedTimes = TimeRange.Merge(unavailableTimes);
        if (mergedTimes.Count == 0) {
            if (breakTime != null) {
                TimeRangeExtensions.SplitTimeRanges splitResult = time.Split(
                    unit: breakTime.Value.unit,
                    maxDuration: maxDuration,
                    breakTime: breakTime.Value.breakDuration);
                return new ValidTimesResult {
                    Times = splitResult.Times,
                    BreakTimes = splitResult.BreakTimes,
                    ConnectionTimes = Array.Empty<TimeRange>()
                };
            }
            else if (maxDuration != null) {
                return new ValidTimesResult {
                    Times = new[] { new TimeRange(time.Start, time.Start + maxDuration.Value) },
                    BreakTimes = Array.Empty<TimeRange>(),
                    ConnectionTimes = Array.Empty<TimeRange>()
                };
            }
            else {
                return new ValidTimesResult {
                    Times = new[] { time },
                    BreakTimes = Array.Empty<TimeRange>(),
                    ConnectionTimes = Array.Empty<TimeRange>()
                };
            }
        }
        else {
            var validTimesList = new List<TimeRange>();
            var breakTimesList = new List<TimeRange>();
            var connectionTimesList = new List<TimeRange>();
            DateTimeOffset currentTime = time.Start;
            int i = IndexOfLastEndedBeforeOrAt(mergedTimes, currentTime);
            TimeSpan totalDuration = TimeSpan.Zero;

            while (currentTime < time.End) {
                if (i == mergedTimes.Count - 1 && currentTime == TimeRange.DistantFuture) {
                    break;
                }
                else if (i != mergedTimes.Count - 1 && mergedTimes[i + 1].Contains(currentTime)) {
                    currentTime = mergedTimes[i + 1].End;
                    i += 1;
                    continue;
                }

                DurationRange? thisBeforeConnectionTime = null;
                if (i != mergedTimes.Count - 1 && (beforeConnectionTime != null || beforeConditionalConnectionTime != null)) {
                    thisBeforeConnectionTime = new DurationRange(
                        MaxDuration(
                            beforeConditionalConnectionTime?.Invoke(mergedTimes[i + 1])?.Lb ?? TimeSpan.Zero,
                            beforeConnectionTime?.Lb ?? TimeSpan.Zero),
                        MaxDuration(
                            beforeConditionalConnectionTime?.Invoke(mergedTimes[i + 1])?.Ub ?? TimeSpan.Zero,
                            beforeConnectionTime?.Ub ?? TimeSpan.Zero));
                }

                DurationRange? thisAfterConnectionTime = null;
                if (i != -1
                    && currentTime <= mergedTimes[i].End
                    && (afterConnectionTime != null || afterConditionalConnectionTime != null)) {
                    thisAfterConnectionTime = new DurationRange(
                        MaxDuration(
                            afterConditionalConnectionTime?.Invoke(mergedTimes[i])?.Lb ?? TimeSpan.Zero,
                            afterConnectionTime?.Lb ?? TimeSpan.Zero),
                        MaxDuration(
                            afterConditionalConnectionTime?.Invoke(mergedTimes[i])?.Ub ?? TimeSpan.Zero,
                            afterConnectionTime?.Ub ?? TimeSpan.Zero));
                }

                DurationRange? thisMaxDuration = null;
                if (i == -1 && thisBeforeConnectionTime != null) {
                    thisMaxDuration = new DurationRange(
                        mergedTimes[0].Start - time.Start - thisBeforeConnectionTime.Ub,
                        mergedTimes[0].Start - time.Start - thisBeforeConnectionTime.Lb);
                }
                else if (i != mergedTimes.Count - 1
                    && (i == -1 || currentTime != mergedTimes[i].End)
                    && thisBeforeConnectionTime != null) {
                    thisMaxDuration = new DurationRange(
                        mergedTimes[i + 1].Start - currentTime - thisBeforeConnectionTime.Ub,
                        mergedTimes[i + 1].Start - currentTime - thisBeforeConnectionTime.Lb);
                }
                else if (i != -1
                    && i != mergedTimes.Count - 1
                    && currentTime == mergedTimes[i].End
                    && (thisBeforeConnectionTime != null || thisAfterConnectionTime != null)) {
                    thisMaxDuration = new DurationRange(
                        mergedTimes[i + 1].Start - mergedTimes[i].End
                            - (thisBeforeConnectionTime?.Ub ?? TimeSpan.Zero)
                            - (thisAfterConnectionTime?.Ub ?? TimeSpan.Zero),
                        mergedTimes[i + 1].Start - mergedTimes[i].End
                            - (thisBeforeConnectionTime?.Lb ?? TimeSpan.Zero)
                            - (thisAfterConnectionTime?.Lb ?? TimeSpan.Zero));
                }

                // If maxDuration lb < 0, the gap is too narrow — skip to next segment
                if (thisMaxDuration != null && thisMaxDuration.Lb < TimeSpan.Zero) {
                    if (i == -1) {
                        connectionTimesList.Add(new TimeRange(time.Start, mergedTimes[0].Start));
                    }
                    else if (currentTime != mergedTimes[i].End && thisBeforeConnectionTime != null) {
                        connectionTimesList.Add(new TimeRange(
                            MaxDateTime(currentTime, mergedTimes[i + 1].Start - thisBeforeConnectionTime.Ub),
                            mergedTimes[i + 1].Start));
                    }
                    else if (currentTime == mergedTimes[i].End) {
                        if (thisBeforeConnectionTime != null && thisAfterConnectionTime != null) {
                            connectionTimesList.Add(new TimeRange(
                                mergedTimes[i].End,
                                mergedTimes[i].End + thisAfterConnectionTime.Lb));
                            connectionTimesList.Add(new TimeRange(
                                mergedTimes[i + 1].Start - (mergedTimes[i + 1].Start - mergedTimes[i].End - thisBeforeConnectionTime.Lb),
                                mergedTimes[i + 1].Start));
                        }
                        else {
                            connectionTimesList.Add(new TimeRange(
                                mergedTimes[i].End,
                                mergedTimes[i + 1].Start));
                        }
                    }
                    currentTime = mergedTimes[i + 1].End;
                    i += 1;
                    continue;
                }

                (TimeSpan? actualBefore, TimeSpan? actualAfter) = ComputeActualConnectionTimes(
                    thisMaxDuration, totalDuration, maxDuration,
                    thisBeforeConnectionTime, thisAfterConnectionTime);

                // after-connection
                if (actualAfter is { } aa && aa > TimeSpan.Zero) {
                    connectionTimesList.Add(new TimeRange(currentTime, currentTime + aa));
                    currentTime += aa;
                }

                DateTimeOffset thisEndTime;
                if (i == mergedTimes.Count - 1) {
                    var candidates = new List<DateTimeOffset> { time.End };
                    TimeSpan breakExtra = TimeSpan.Zero;
                    if (maxDuration != null && breakTime != null) {
                        breakExtra = breakTime.Value.breakDuration * (int)global::System.Math.Ceiling(
                            maxDuration.Value.TotalSeconds / breakTime.Value.unit.Lb.TotalSeconds);
                    }
                    candidates.Add(currentTime + (maxDuration ?? SafeMaxDuration) + breakExtra);
                    thisEndTime = candidates.Min();
                }
                else {
                    var candidates = new List<DateTimeOffset>
                    {
                        time.End,
                        mergedTimes[i + 1].Start - (actualBefore ?? TimeSpan.Zero)
                    };
                    TimeSpan breakExtra = TimeSpan.Zero;
                    if (maxDuration != null && breakTime != null) {
                        breakExtra = breakTime.Value.breakDuration * (int)global::System.Math.Ceiling(
                            maxDuration.Value.TotalSeconds / breakTime.Value.unit.Lb.TotalSeconds);
                    }
                    candidates.Add(currentTime + (maxDuration ?? SafeMaxDuration) + breakExtra);
                    thisEndTime = candidates.Min();
                }

                var baseTime = new TimeRange(currentTime, thisEndTime);

                if (breakTime != null) {
                    TimeSpan offset = (currentTime == time.Start) ? (currentDuration ?? TimeSpan.Zero) : TimeSpan.Zero;
                    TimeRangeExtensions.SplitTimeRanges splitResult = baseTime.Split(
                        unit: breakTime.Value.unit,
                        currentDuration: offset,
                        maxDuration: maxDuration != null ? maxDuration.Value - totalDuration : (TimeSpan?)null,
                        breakTime: breakTime.Value.breakDuration);
                    foreach (TimeRange vt in splitResult.Times) { totalDuration += vt.Duration; }
                    validTimesList.AddRange(splitResult.Times);
                    breakTimesList.AddRange(splitResult.BreakTimes);
                }
                else {
                    totalDuration += baseTime.Duration;
                    validTimesList.Add(baseTime);
                }

                if (thisEndTime != time.End && totalDuration != maxDuration && actualBefore is { } ab && ab > TimeSpan.Zero) {
                    connectionTimesList.Add(new TimeRange(thisEndTime, thisEndTime + ab));
                }
                else if (thisEndTime == time.End || totalDuration == maxDuration) {
                    break;
                }

                currentTime = mergedTimes[i + 1].End;
                i += 1;
            }

            return new ValidTimesResult {
                Times = validTimesList,
                BreakTimes = breakTimesList,
                ConnectionTimes = connectionTimesList
            };
        }
    }

    // =========================================================================
    // Static reversedValidTimes — companion.reversedValidTimes
    // =========================================================================

    /// <summary>
    /// 反向计算有效时间范围（静态）/ Reverse calculate valid time ranges (static).
    /// </summary>
    protected static ValidTimesResult ReversedValidTimesStatic(
        TimeRange time,
        IReadOnlyList<TimeRange> unavailableTimes,
        DurationRange? beforeConnectionTime = null,
        DurationRange? afterConnectionTime = null,
        Func<TimeRange, DurationRange?>? beforeConditionalConnectionTime = null,
        Func<TimeRange, DurationRange?>? afterConditionalConnectionTime = null,
        TimeSpan? maxDuration = null,
        (DurationRange unit, TimeSpan breakDuration)? breakTime = null) {
        IReadOnlyList<TimeRange> mergedTimes = TimeRange.Merge(unavailableTimes);
        if (mergedTimes.Count == 0) {
            if (breakTime != null) {
                TimeRangeExtensions.SplitTimeRanges? splitResult = time.RSplit(
                    unit: breakTime.Value.unit,
                    maxDuration: maxDuration,
                    breakTime: breakTime.Value.breakDuration);
                if (splitResult != null) {
                    return new ValidTimesResult {
                        Times = splitResult.Times,
                        BreakTimes = splitResult.BreakTimes,
                        ConnectionTimes = Array.Empty<TimeRange>()
                    };
                }
            }
            if (maxDuration != null) {
                return new ValidTimesResult {
                    Times = new[] { new TimeRange(time.End - maxDuration.Value, time.End) },
                    BreakTimes = Array.Empty<TimeRange>(),
                    ConnectionTimes = Array.Empty<TimeRange>()
                };
            }
            return new ValidTimesResult {
                Times = new[] { time },
                BreakTimes = Array.Empty<TimeRange>(),
                ConnectionTimes = Array.Empty<TimeRange>()
            };
        }
        else {
            var validTimesList = new List<TimeRange>();
            var breakTimesList = new List<TimeRange>();
            var connectionTimesList = new List<TimeRange>();
            DateTimeOffset currentTime = time.End;
            int i = IndexOfFirstStartedAfterOrAt(mergedTimes, currentTime);
            if (i == -1) {
                i = mergedTimes.Count;
            }

            TimeSpan totalDuration = TimeSpan.Zero;

            while (currentTime > time.Start) {
                if (i == 0 && currentTime == TimeRange.DistantPast) {
                    break;
                }
                else if (i != 0 && mergedTimes[i - 1].Contains(currentTime)) {
                    currentTime = mergedTimes[i - 1].Start;
                    i -= 1;
                    continue;
                }

                DurationRange? thisBeforeConnectionTime = null;
                if (i != mergedTimes.Count) {
                    thisBeforeConnectionTime = new DurationRange(
                        MaxDuration(
                            beforeConditionalConnectionTime?.Invoke(mergedTimes[i])?.Lb ?? TimeSpan.Zero,
                            beforeConnectionTime?.Lb ?? TimeSpan.Zero),
                        MaxDuration(
                            beforeConditionalConnectionTime?.Invoke(mergedTimes[i])?.Ub ?? TimeSpan.Zero,
                            beforeConnectionTime?.Ub ?? TimeSpan.Zero));
                }

                DurationRange? thisAfterConnectionTime = null;
                if (i != 0 && currentTime >= mergedTimes[i - 1].End) {
                    thisAfterConnectionTime = new DurationRange(
                        MaxDuration(
                            afterConditionalConnectionTime?.Invoke(mergedTimes[i - 1])?.Lb ?? TimeSpan.Zero,
                            afterConnectionTime?.Lb ?? TimeSpan.Zero),
                        MaxDuration(
                            afterConditionalConnectionTime?.Invoke(mergedTimes[i - 1])?.Ub ?? TimeSpan.Zero,
                            afterConnectionTime?.Ub ?? TimeSpan.Zero));
                }

                DurationRange? thisMaxDuration = null;
                if (i == mergedTimes.Count && thisAfterConnectionTime != null) {
                    thisMaxDuration = new DurationRange(
                        time.End - mergedTimes[mergedTimes.Count - 1].End - thisAfterConnectionTime.Ub,
                        time.End - mergedTimes[mergedTimes.Count - 1].End - thisAfterConnectionTime.Lb);
                }
                else if (i != 0 && i != mergedTimes.Count
                    && (thisBeforeConnectionTime != null || thisAfterConnectionTime != null)) {
                    DateTimeOffset minBoundary = MinDateTime(currentTime, mergedTimes[i].Start);
                    thisMaxDuration = new DurationRange(
                        minBoundary - mergedTimes[i - 1].End
                            - (thisBeforeConnectionTime?.Ub ?? TimeSpan.Zero)
                            - (thisAfterConnectionTime?.Ub ?? TimeSpan.Zero),
                        minBoundary - mergedTimes[i - 1].End
                            - (thisBeforeConnectionTime?.Lb ?? TimeSpan.Zero)
                            - (thisAfterConnectionTime?.Lb ?? TimeSpan.Zero));
                }

                // If maxDuration lb < 0, the gap is too narrow — skip
                if (thisMaxDuration != null && thisMaxDuration.Lb < TimeSpan.Zero) {
                    if (i == mergedTimes.Count) {
                        connectionTimesList.Add(new TimeRange(mergedTimes[mergedTimes.Count - 1].End, time.End));
                    }
                    else if (currentTime != mergedTimes[i].Start && thisBeforeConnectionTime != null) {
                        connectionTimesList.Add(new TimeRange(
                            MaxDateTime(currentTime, mergedTimes[i].Start - thisBeforeConnectionTime.Ub),
                            mergedTimes[i].Start));
                    }
                    else if (i != 0 && currentTime == mergedTimes[i].Start) {
                        if (thisBeforeConnectionTime != null && thisAfterConnectionTime != null) {
                            connectionTimesList.Add(new TimeRange(
                                mergedTimes[i - 1].End,
                                mergedTimes[i - 1].End + thisAfterConnectionTime.Lb));
                            connectionTimesList.Add(new TimeRange(
                                mergedTimes[i].Start - (MinDateTime(currentTime, mergedTimes[i].Start) - mergedTimes[i - 1].End - thisAfterConnectionTime.Lb),
                                mergedTimes[i].Start));
                        }
                        else {
                            connectionTimesList.Add(new TimeRange(
                                mergedTimes[i - 1].End,
                                MinDateTime(currentTime, mergedTimes[i].Start)));
                        }
                    }
                    currentTime = mergedTimes[i - 1].Start;
                    i -= 1;
                    continue;
                }

                (TimeSpan? actualBefore, TimeSpan? actualAfter) = ComputeActualConnectionTimes(
                    thisMaxDuration, totalDuration, maxDuration,
                    thisBeforeConnectionTime, thisAfterConnectionTime);

                // before-connection (reversed: subtract from currentTime)
                if (actualBefore is { } ab && ab > TimeSpan.Zero) {
                    connectionTimesList.Add(new TimeRange(currentTime - ab, currentTime));
                    currentTime -= ab;
                }

                DateTimeOffset thisStartTime;
                if (i == 0) {
                    var candidates = new List<DateTimeOffset> { time.Start };
                    TimeSpan breakExtra = TimeSpan.Zero;
                    if (maxDuration != null && breakTime != null) {
                        breakExtra = breakTime.Value.breakDuration * (int)global::System.Math.Ceiling(
                            maxDuration.Value.TotalSeconds / breakTime.Value.unit.Lb.TotalSeconds);
                    }
                    candidates.Add(currentTime - (maxDuration ?? SafeMaxDuration) - breakExtra);
                    thisStartTime = candidates.Max();
                }
                else {
                    var candidates = new List<DateTimeOffset>
                    {
                        time.Start,
                        mergedTimes[i - 1].End + (actualAfter ?? TimeSpan.Zero)
                    };
                    TimeSpan breakExtra = TimeSpan.Zero;
                    if (maxDuration != null && breakTime != null) {
                        breakExtra = breakTime.Value.breakDuration * (int)global::System.Math.Ceiling(
                            maxDuration.Value.TotalSeconds / breakTime.Value.unit.Lb.TotalSeconds);
                    }
                    candidates.Add(currentTime - (maxDuration ?? SafeMaxDuration) - breakExtra);
                    thisStartTime = candidates.Max();
                }

                var baseTime = new TimeRange(thisStartTime, currentTime);

                if (breakTime != null) {
                    TimeRangeExtensions.SplitTimeRanges? splitResult = baseTime.RSplit(
                        unit: breakTime.Value.unit,
                        maxDuration: maxDuration != null ? maxDuration.Value - totalDuration : (TimeSpan?)null,
                        breakTime: breakTime.Value.breakDuration);
                    if (splitResult != null) {
                        foreach (TimeRange vt in splitResult.Times) { totalDuration += vt.Duration; }
                        validTimesList.AddRange(splitResult.Times);
                        breakTimesList.AddRange(splitResult.BreakTimes);
                    }
                }
                else {
                    totalDuration += baseTime.Duration;
                    validTimesList.Add(baseTime);
                }

                if (thisStartTime != time.Start && totalDuration != maxDuration && actualAfter is { } aa && aa > TimeSpan.Zero) {
                    connectionTimesList.Add(new TimeRange(thisStartTime - aa, thisStartTime));
                }
                else if (thisStartTime == time.Start || totalDuration == maxDuration) {
                    break;
                }

                currentTime = mergedTimes[i - 1].Start;
                i -= 1;
            }

            return new ValidTimesResult {
                Times = validTimesList,
                BreakTimes = breakTimesList,
                ConnectionTimes = connectionTimesList
            };
        }
    }

    // =========================================================================
    // Instance methods
    // =========================================================================

    /// <summary>
    /// 计算考虑不可用时间后的实际时间点 /
    /// Calculate the actual time point considering unavailable times.
    /// </summary>
    public DateTimeOffset ActualTime(
        DateTimeOffset time,
        IReadOnlyList<TimeRange>? unavailableTimes = null,
        DurationRange? beforeConnectionTime = null,
        DurationRange? afterConnectionTime = null,
        Func<TimeRange, DurationRange?>? beforeConditionalConnectionTime = null,
        Func<TimeRange, DurationRange?>? afterConditionalConnectionTime = null) {
        return ActualTimeStatic(
            time: time,
            unavailableTimes: TimeRange.Merge(MergeUnavailableTimes(unavailableTimes)),
            beforeConnectionTime: beforeConnectionTime,
            afterConnectionTime: afterConnectionTime,
            beforeConditionalConnectionTime: beforeConditionalConnectionTime,
            afterConditionalConnectionTime: afterConditionalConnectionTime);
    }

    /// <summary>
    /// 计算考虑不可用时间后的实际时间范围 /
    /// Calculate the actual time range considering unavailable times.
    /// </summary>
    public ActualTimeResult ActualTime(
        TimeRange time,
        IReadOnlyList<TimeRange>? unavailableTimes = null,
        DurationRange? beforeConnectionTime = null,
        DurationRange? afterConnectionTime = null,
        Func<TimeRange, DurationRange?>? beforeConditionalConnectionTime = null,
        Func<TimeRange, DurationRange?>? afterConditionalConnectionTime = null,
        TimeSpan? currentDuration = null,
        (DurationRange unit, TimeSpan breakDuration)? breakTime = null) {
        return ActualTimeStatic(
            time: time,
            unavailableTimes: TimeRange.Merge(MergeUnavailableTimes(unavailableTimes)),
            beforeConnectionTime: beforeConnectionTime,
            afterConnectionTime: afterConnectionTime,
            beforeConditionalConnectionTime: beforeConditionalConnectionTime,
            afterConditionalConnectionTime: afterConditionalConnectionTime,
            currentDuration: currentDuration,
            breakTime: breakTime);
    }

    /// <summary>
    /// 计算有效时间范围 / Calculate valid time ranges.
    /// </summary>
    public ValidTimesResult ValidTimes(
        TimeRange time,
        IReadOnlyList<TimeRange>? unavailableTimes = null,
        DurationRange? beforeConnectionTime = null,
        DurationRange? afterConnectionTime = null,
        Func<TimeRange, DurationRange?>? beforeConditionalConnectionTime = null,
        Func<TimeRange, DurationRange?>? afterConditionalConnectionTime = null,
        TimeSpan? currentDuration = null,
        TimeSpan? maxDuration = null,
        (DurationRange unit, TimeSpan breakDuration)? breakTime = null) {
        return ValidTimesStatic(
            time: time,
            unavailableTimes: TimeRange.Merge(MergeUnavailableTimes(unavailableTimes)),
            beforeConnectionTime: beforeConnectionTime,
            afterConnectionTime: afterConnectionTime,
            beforeConditionalConnectionTime: beforeConditionalConnectionTime,
            afterConditionalConnectionTime: afterConditionalConnectionTime,
            currentDuration: currentDuration,
            maxDuration: maxDuration,
            breakTime: breakTime);
    }

    // =========================================================================
    // Private helpers
    // =========================================================================

    private IReadOnlyList<TimeRange> MergeUnavailableTimes(IReadOnlyList<TimeRange>? extra) {
        if (extra == null || extra.Count == 0) {
            return UnavailableTimes;
        }

        return TimeRange.Merge(extra.Concat(UnavailableTimes).ToList());
    }

    /// <summary>
    /// 计算实际连接时间 / Compute actual connection times.
    /// 对应 kotlin 中 thisActualBeforeConnectionTime / thisActualAfterConnectionTime 的解构逻辑。
    /// </summary>
    private static (TimeSpan? before, TimeSpan? after) ComputeActualConnectionTimes(
        DurationRange? maxDuration,
        TimeSpan totalDuration,
        TimeSpan? targetDuration,
        DurationRange? beforeConnectionTime,
        DurationRange? afterConnectionTime) {
        if (targetDuration != null && maxDuration != null
            && (totalDuration + maxDuration.Lb) <= targetDuration.Value
            && (totalDuration + maxDuration.Ub) >= targetDuration.Value) {
            TimeSpan restDuration = maxDuration.Ub - (targetDuration.Value - totalDuration);
            if (beforeConnectionTime != null && afterConnectionTime != null) {
                if (restDuration >= (beforeConnectionTime.Ub - beforeConnectionTime.Lb)) {
                    return (beforeConnectionTime.Ub, afterConnectionTime.Lb + restDuration);
                }
                else {
                    return (beforeConnectionTime.Lb + restDuration, afterConnectionTime.Lb);
                }
            }
            else if (beforeConnectionTime != null) {
                return (beforeConnectionTime.Lb + restDuration, null);
            }
            else if (afterConnectionTime != null) {
                return (null, afterConnectionTime.Lb + restDuration);
            }
            else {
                return (null, null);
            }
        }
        else {
            return (beforeConnectionTime?.Ub, afterConnectionTime?.Ub);
        }
    }

    private static TimeSpan MaxDuration(TimeSpan a, TimeSpan b) => a > b ? a : b;
    private static TimeSpan MinDuration(TimeSpan a, TimeSpan b) => a < b ? a : b;
    private static DateTimeOffset MaxDateTime(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;
    private static DateTimeOffset MinDateTime(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;

    /// <summary>安全的最大 TimeSpan，用于避免 DateTimeOffset 加法溢出 /
    /// Safe maximum TimeSpan to avoid DateTimeOffset addition overflow.</summary>
    private static TimeSpan SafeMaxDuration => TimeSpan.FromDays(365_000);
}
