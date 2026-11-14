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
/// 泛型时间窗口，提供时间离散化和舍入功能 / Generic time window providing time discretization and rounding capabilities.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed record TimeWindow<V>(
    TimeRange Window,
    bool Continues,
    TimeSpan Interval
) where V : struct, IRealNumber<V> {
    /// <summary>是否为空窗口 / Whether this is an empty window.</summary>
    public bool Empty => Window.Empty;

    /// <summary>开始时间 / Start time.</summary>
    public DateTimeOffset Start => Window.Start;

    /// <summary>结束时间 / End time.</summary>
    public DateTimeOffset End => Window.End;

    /// <summary>持续时间 / Duration.</summary>
    public TimeSpan Duration => Window.Duration;

    /// <summary>按默认间隔划分的时间段列表 / List of time slots divided by default interval.</summary>
    public IReadOnlyList<TimeRange> TimeSlots => TimeSlotsOf(Interval);

    /// <summary>按指定间隔划分时间段 / Divide time slots by the specified interval.</summary>
    public IReadOnlyList<TimeRange> TimeSlotsOf(TimeSpan interval) {
        var timeSlots = new List<TimeRange>();
        DateTimeOffset current = Start;
        while (current != End) {
            TimeSpan duration = (End - current) < interval ? (End - current) : interval;
            timeSlots.Add(new TimeRange(current, current + duration));
            current += duration;
        }
        return timeSlots;
    }

    /// <summary>判断是否与另一时间范围有交集 / Check whether this window intersects with another range.</summary>
    public bool WithIntersection(TimeRange other) => Window.WithIntersection(other);

    /// <summary>判断是否包含指定时间点 / Check whether this window contains the specified instant.</summary>
    public bool Contains(DateTimeOffset time) => Window.Contains(time);

    /// <summary>判断是否包含指定时间范围 / Check whether this window contains the specified time range.</summary>
    public bool Contains(TimeRange time) => Window.Contains(time);

    /// <summary>创建新的时间窗口 / Create a new time window.</summary>
    public TimeWindow<V> New(TimeRange window, bool continues)
        => new(window, continues, Interval);

    // ===== Duration/value conversion methods (used by WorkingCalendar/ProductivityCalendar) =====

    /// <summary>将 TimeSpan 转换为以 Interval 为单位的 Flt64 值 / Convert TimeSpan to Flt64 value in Interval units.</summary>
    public Flt64 ValueOf(TimeSpan duration) => new(duration.TotalSeconds / Interval.TotalSeconds);

    /// <summary>
    /// 将时间点转换为以 Interval 为单位、相对窗口起点的 Flt64 值 / Convert an instant to an Flt64 value in Interval units,
    /// relative to the window start.
    /// </summary>
    /// <remarks>
    /// 这是 <see cref="ValueOf(TimeSpan)"/> 的时间点对应项：两者都以**窗口起点**为原点、以 `Interval` 为刻度，因此
    /// "时刻减去起点"与"时长"落在同一坐标轴上，可以直接相加比较。若时间点用绝对秒数、时长用 Interval 刻度，两者混算会
    /// 得到相差若干个数量级的结果，而且不会抛异常。
    /// This is the instant counterpart of <see cref="ValueOf(TimeSpan)"/>: both use the **window start** as origin and
    /// `Interval` as the tick, so "instant minus start" and a duration land on the same axis and can be added and compared
    /// directly. Mixing absolute seconds for instants with Interval ticks for durations yields results off by orders of
    /// magnitude, with nothing throwing.
    /// </remarks>
    /// <param name="instant">时间点 / The instant.</param>
    /// <returns>窗口坐标值 / The value in window coordinates.</returns>
    public Flt64 ValueOf(DateTimeOffset instant) => ValueOf(instant - Window.Start);

    /// <summary>
    /// 将窗口坐标值还原为时间点 / Convert a window coordinate back into an instant.
    /// </summary>
    /// <remarks>
    /// 这是 <see cref="ValueOf(DateTimeOffset)"/> 的逆运算，也是 <see cref="DurationOf(Flt64)"/> 的时间点版本：以窗口起点为
    /// 原点、`Interval` 为刻度。定价回溯必须用它把标签里的时间坐标还原成绝对时刻，否则生成的 `RouteStop` 会带着相对时刻
    /// 混进业务时间轴。
    /// This is the inverse of <see cref="ValueOf(DateTimeOffset)"/> and the instant counterpart of
    /// <see cref="DurationOf(Flt64)"/>: origin at the window start, tick = `Interval`. Pricing backtracking needs it to turn label
    /// time coordinates back into absolute instants, or the produced `RouteStop`s carry relative times into the business timeline.
    /// </remarks>
    /// <param name="value">窗口坐标值 / The value in window coordinates.</param>
    /// <returns>绝对时间点 / The absolute instant.</returns>
    public DateTimeOffset InstantOf(Flt64 value) => Window.Start.Add(DurationOf(value));

    /// <summary>
    /// 将窗口坐标值还原为时间点 / Convert a window coordinate back into an instant.
    /// </summary>
    /// <param name="value">窗口坐标值 / The value in window coordinates.</param>
    /// <returns>绝对时间点 / The absolute instant.</returns>
    public DateTimeOffset InstantOf(double value) => Window.Start.Add(DurationOf(value));

    /// <summary>将 Flt64 值（以 Interval 为单位）转换为 TimeSpan / Convert Flt64 value (in Interval units) to TimeSpan.</summary>
    public TimeSpan DurationOf(Flt64 value) => TimeSpan.FromSeconds(value.ToDouble() * Interval.TotalSeconds);

    /// <summary>将 double 值（以 Interval 为单位）转换为 TimeSpan / Convert double value (in Interval units) to TimeSpan.</summary>
    public TimeSpan DurationOf(double value) => TimeSpan.FromSeconds(value * Interval.TotalSeconds);

    /// <summary>向上取整到下一个 Interval 边界 / Ceil to the next Interval boundary.</summary>
    public TimeSpan Ceil(TimeSpan duration) {
        double units = duration.TotalSeconds / Interval.TotalSeconds;
        return TimeSpan.FromSeconds(global::System.Math.Ceiling(units) * Interval.TotalSeconds);
    }

    /// <summary>从 TimeSpan 创建 Quantity / Create a Quantity from a TimeSpan.</summary>
    public Quantity<V> QuantityOf(TimeSpan duration, Func<double, V> fromDouble, PhysicalUnit? unit = null)
        => new(fromDouble(duration.TotalSeconds / Interval.TotalSeconds), unit ?? NoneUnit.Instance);
}
