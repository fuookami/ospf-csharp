#nullable enable

using System;

namespace Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
/// <summary>
/// 时间槽接口，表示具有时间范围的可切片对象 / Time slot interface representing a sliceable object with a time range.
/// </summary>
public interface ITimeSlot {
    /// <summary>时间范围 / The time range.</summary>
    TimeRange Time { get; }

    /// <summary>开始时间 / Start time.</summary>
    DateTimeOffset Start => Time.Start;

    /// <summary>结束时间 / End time.</summary>
    DateTimeOffset End => Time.End;

    /// <summary>持续时间 / Duration.</summary>
    TimeSpan Duration => Time.Duration;

    /// <summary>
    /// 获取在给定时间范围内的子槽 / Get a sub-slot within the given time range.
    /// </summary>
    /// <param name="subTime">子时间范围 / The sub time range.</param>
    /// <returns>子槽，若不相交则为null / The sub-slot, or null if no intersection.</returns>
    ITimeSlot? SubOf(TimeRange subTime);
}
