#nullable enable

using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using System;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
/// <summary>
/// Solver 时间窗口边界，用于集中把业务时间转为 solver 数值 /
/// Solver time-window boundary that centralizes business-time to solver-value conversion
/// </summary>
public class SolverTimeWindowBoundary {
    /// <summary>
    /// Solver 时间窗口边界构造 / Solver time-window boundary constructor
    /// </summary>
    /// <param name="source">Flt64 时间窗口 / Flt64 time window</param>
    public SolverTimeWindowBoundary(TimeWindow<Flt64> source) {
        Source = source;
    }

    /// <summary>Flt64 时间窗口 / Flt64 time window</summary>
    public TimeWindow<Flt64> Source { get; }

    /// <summary>是否连续 / Whether the solver time window is continuous</summary>
    public bool Continues => Source.Continues;

    /// <summary>窗口持续时间数值 / Solver value of the window duration</summary>
    public Flt64 DurationValue => new Flt64(Source.Duration.TotalSeconds);

    /// <summary>窗口结束时间数值 / Solver value of the window end</summary>
    public Flt64 EndValue => new Flt64((Source.End - Source.Start).TotalSeconds);

    /// <summary>
    /// 读取持续时间数值 / Read a duration as a solver value
    /// </summary>
    /// <param name="duration">持续时间 / Duration</param>
    /// <returns>solver 数值 / Solver value</returns>
    public Flt64 ValueOf(TimeSpan duration) => new Flt64(duration.TotalSeconds);

    /// <summary>
    /// 读取时间点数值 / Read an instant as a solver value
    /// </summary>
    /// <param name="instant">时间点 / Instant (DateTimeOffset)</param>
    /// <returns>solver 数值 / Solver value</returns>
    public Flt64 ValueOf(DateTimeOffset instant) => new Flt64((instant - Source.Start).TotalSeconds);

    /// <summary>
    /// 从 solver 数值读取时间点 / Read an instant from a solver value
    /// </summary>
    /// <param name="value">solver 数值 / Solver value</param>
    /// <returns>时间点 / Instant</returns>
    public DateTimeOffset InstantOf(Flt64 value) => Source.Start + TimeSpan.FromSeconds(value.ToDouble());

    /// <summary>
    /// 读取向下取整后的时间点数值 / Read a floored instant solver value
    /// </summary>
    /// <param name="instant">时间点 / Instant</param>
    /// <returns>向下取整后的 solver 数值 / Floored solver value</returns>
    public Flt64 FlooredValueOf(DateTimeOffset instant) => new Flt64(System.Math.Floor((instant - Source.Start).TotalSeconds));

    /// <summary>
    /// 读取窗口结束到指定时间点的剩余数值 / Read remaining solver value from an instant to the window end
    /// </summary>
    /// <param name="instant">时间点 / Instant</param>
    /// <returns>剩余 solver 数值 / Remaining solver value</returns>
    public Flt64 RemainingValueAfter(DateTimeOffset instant) => new Flt64((Source.End - instant).TotalSeconds);

    /// <summary>
    /// 读取窗口开始到指定时间点的已过数值 / Read elapsed solver value from the window start to an instant
    /// </summary>
    /// <param name="instant">时间点 / Instant</param>
    /// <returns>已过 solver 数值 / Elapsed solver value</returns>
    public Flt64 ElapsedValueBefore(DateTimeOffset instant) => new Flt64((instant - Source.Start).TotalSeconds);

    /// <summary>
    /// 读取两个时间点之间的距离数值 / Read solver value between two instants
    /// </summary>
    /// <param name="from">开始时间点 / Start instant</param>
    /// <param name="to">结束时间点 / End instant</param>
    /// <returns>距离 solver 数值 / Distance solver value</returns>
    public Flt64 DistanceValue(DateTimeOffset from, DateTimeOffset to) => new Flt64((to - from).TotalSeconds);

    /// <summary>
    /// 读取指定时间点加一个窗口持续时间后的数值 / Read solver value after adding one window duration to an instant
    /// </summary>
    /// <param name="instant">时间点 / Instant</param>
    /// <returns>solver 数值 / Solver value</returns>
    public Flt64 AfterWindowDurationValue(DateTimeOffset instant) => new Flt64((instant - Source.Start + Source.Duration).TotalSeconds);

    /// <summary>
    /// 读取指定时间点减一个窗口持续时间后的数值 / Read solver value after subtracting one window duration from an instant
    /// </summary>
    /// <param name="instant">时间点 / Instant</param>
    /// <returns>solver 数值 / Solver value</returns>
    public Flt64 BeforeWindowDurationValue(DateTimeOffset instant) => new Flt64((instant - Source.Start - Source.Duration).TotalSeconds);
}
