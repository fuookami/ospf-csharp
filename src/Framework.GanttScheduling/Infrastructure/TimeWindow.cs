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
