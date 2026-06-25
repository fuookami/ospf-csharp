#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
/// <summary>
/// 时间范围 [start, end)，实现 ITimeSlot 接口 / Time range [start, end), implementing ITimeSlot interface.
/// </summary>
/// <param name="Start">开始时间 / Start time.</param>
/// <param name="End">结束时间 / End time.</param>
public sealed record TimeRange(DateTimeOffset Start, DateTimeOffset End) : ITimeSlot {
    /// <summary>默认远过去时间 / Default distant past time.</summary>
    public static readonly DateTimeOffset DistantPast = DateTimeOffset.MinValue;

    /// <summary>默认远未来时间 / Default distant future time.</summary>
    public static readonly DateTimeOffset DistantFuture = DateTimeOffset.MaxValue;

    /// <summary>默认构造：全范围 / Default constructor: full range.</summary>
    public TimeRange() : this(DistantPast, DistantFuture) { }

    /// <summary>仅指定开始 / Constructor with start only.</summary>
    public TimeRange(DateTimeOffset start) : this(start, DistantFuture) { }

    /// <inheritdoc/>
    public TimeRange Time => this;

    /// <summary>是否为空范围 / Whether this is an empty range.</summary>
    public bool Empty => Start >= End;

    /// <summary>持续时间 / Duration.</summary>
    public TimeSpan Duration => End - Start;

    /// <summary>获取 start 之前的前置区间 / Get the front range before start.</summary>
    public TimeRange? Front => Start != DistantPast ? new TimeRange(DistantPast, Start) : null;

    /// <summary>获取 end 之后的后置区间 / Get the back range after end.</summary>
    public TimeRange? Back => End != DistantFuture ? new TimeRange(End, DistantFuture) : null;

    /// <summary>
    /// 获取当前范围与另一范围之间的前置间隙 / Get the front gap between this range and another range.
    /// </summary>
    /// <param name="other">另一时间范围 / Another time range.</param>
    /// <returns>前置间隙，若不存在则为null / The front gap, or null if none exists.</returns>
    public TimeRange? FrontBetween(TimeRange other)
        => other.End < Start ? new TimeRange(other.End, Start) : null;

    /// <summary>
    /// 获取当前范围与另一范围之间的后置间隙 / Get the back gap between this range and another range.
    /// </summary>
    /// <param name="other">另一时间范围 / Another time range.</param>
    /// <returns>后置间隙，若不存在则为null / The back gap, or null if none exists.</returns>
    public TimeRange? BackBetween(TimeRange other)
        => other.Start > End ? new TimeRange(End, other.Start) : null;

    /// <summary>
    /// 判断是否与另一时间范围有交集 / Check whether this range intersects with another range.
    /// </summary>
    /// <param name="other">另一时间范围 / Another time range.</param>
    /// <returns>是否有交集 / Whether there is an intersection.</returns>
    public bool WithIntersection(TimeRange other)
        => Start < other.End && other.Start < End;

    /// <summary>
    /// 计算与另一时间范围的交集 / Compute the intersection with another range.
    /// </summary>
    /// <param name="other">另一时间范围 / Another time range.</param>
    /// <returns>交集，若不相交则为null / The intersection, or null if no intersection.</returns>
    public TimeRange? IntersectionWith(TimeRange other) {
        DateTimeOffset maxBegin = Start > other.Start ? Start : other.Start;
        DateTimeOffset minEnd = End < other.End ? End : other.End;
        return minEnd > maxBegin ? new TimeRange(maxBegin, minEnd) : null;
    }

    /// <summary>
    /// 计算与另一时间范围的差集 / Compute the difference with another range.
    /// </summary>
    /// <param name="other">另一时间范围 / Another time range.</param>
    /// <returns>差集结果列表 / The list of difference results.</returns>
    public IReadOnlyList<TimeRange> DifferenceWith(TimeRange other) {
        TimeRange? intersection = IntersectionWith(other);
        if (intersection is null) {
            return new[] { this };
        }

        if (intersection.Start == Start && intersection.End == End) {
            return Array.Empty<TimeRange>();
        }

        if (intersection.Start == Start) {
            return new[] { new TimeRange(intersection.End, End) };
        }

        if (intersection.End == End) {
            return new[] { new TimeRange(Start, intersection.Start) };
        }

        return new[] { new TimeRange(Start, intersection.Start), new TimeRange(intersection.End, End) };
    }

    /// <summary>
    /// 计算与多个时间范围列表的差集 / Compute the difference with a list of time ranges.
    /// </summary>
    /// <param name="others">时间范围列表 / The list of time ranges.</param>
    /// <returns>差集结果列表 / The list of difference results.</returns>
    public IReadOnlyList<TimeRange> DifferenceWith(IReadOnlyList<TimeRange> others) {
        var intersections = others
            .Where(t => t.WithIntersection(this))
            .Select(t => new TimeRange(
                Start > t.Start ? Start : t.Start,
                End < t.End ? End : t.End))
            .ToList();
        IReadOnlyList<TimeRange> merged = Merge(intersections);
        if (merged.Count == 0) {
            return new[] { this };
        }

        var result = new List<TimeRange>();
        DateTimeOffset currentTime = Start;
        foreach (TimeRange mt in merged) {
            if (currentTime < mt.Start) {
                result.Add(new TimeRange(currentTime, mt.Start));
            }

            currentTime = mt.End;
        }
        if (currentTime < End) {
            result.Add(new TimeRange(currentTime, End));
        }

        return result;
    }

    /// <summary>
    /// 判断是否包含指定时间点 / Check whether this range contains the specified instant.
    /// </summary>
    /// <param name="time">时间点 / The instant.</param>
    /// <returns>是否包含 / Whether contained.</returns>
    public bool Contains(DateTimeOffset time) => Start <= time && time < End;

    /// <summary>
    /// 判断是否包含指定时间范围 / Check whether this range contains the specified time range.
    /// </summary>
    /// <param name="time">时间范围 / The time range.</param>
    /// <returns>是否包含 / Whether contained.</returns>
    public bool Contains(TimeRange time) => Start <= time.Start && time.End <= End;

    /// <summary>
    /// 判断是否在另一时间范围之前连续 / Check whether this range is continuous before another range.
    /// </summary>
    /// <param name="time">另一时间范围 / Another time range.</param>
    /// <returns>是否连续 / Whether continuous.</returns>
    public bool ContinuousBefore(TimeRange time) => End == time.Start;

    /// <summary>
    /// 判断是否在另一时间范围之后连续 / Check whether this range is continuous after another range.
    /// </summary>
    /// <param name="time">另一时间范围 / Another time range.</param>
    /// <returns>是否连续 / Whether continuous.</returns>
    public bool ContinuousAfter(TimeRange time) => Start == time.End;

    /// <summary>
    /// 判断是否与另一时间范围连续 / Check whether this range is continuous with another range.
    /// </summary>
    /// <param name="time">另一时间范围 / Another time range.</param>
    /// <returns>是否连续 / Whether continuous.</returns>
    public bool ContinuousWith(TimeRange time) => ContinuousBefore(time) || ContinuousAfter(time);

    /// <summary>时间范围平移（加法）/ Shift the time range (plus).</summary>
    public TimeRange Plus(TimeSpan rhs) => new(Start + rhs, End + rhs);

    /// <summary>时间范围平移（减法）/ Shift the time range (minus).</summary>
    public TimeRange Minus(TimeSpan rhs) => new(Start - rhs, End - rhs);

    /// <inheritdoc/>
    public ITimeSlot? SubOf(TimeRange subTime) => IntersectionWith(subTime);

    /// <summary>
    /// 合并重叠或相邻的时间范围列表 / Merge overlapping or adjacent time ranges in the list.
    /// </summary>
    /// <param name="ranges">时间范围列表 / The list of time ranges.</param>
    /// <returns>合并后的时间范围列表 / The merged list of time ranges.</returns>
    public static IReadOnlyList<TimeRange> Merge(IReadOnlyList<TimeRange> ranges) {
        if (ranges.Count == 0) {
            return Array.Empty<TimeRange>();
        }

        var sorted = ranges.OrderBy(r => r.Start).ToList();
        var merged = new List<TimeRange>();
        TimeRange current = sorted[0];
        for (int i = 1; i < sorted.Count; i++) {
            if (sorted[i].Start <= current.End) {
                current = new TimeRange(current.Start, current.End > sorted[i].End ? current.End : sorted[i].End);
            }
            else {
                merged.Add(current);
                current = sorted[i];
            }
        }
        merged.Add(current);
        return merged;
    }

    /// <inheritdoc/>
    public override string ToString() => $"[{Start:O}, {End:O})";
}
