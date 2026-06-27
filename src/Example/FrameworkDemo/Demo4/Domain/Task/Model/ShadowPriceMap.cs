#nullable enable

using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;

/// <summary>
/// 专门用于飞机和航班任务分配的影子价格映射。
/// Shadow price map specialized for aircraft and flight task assignment.
/// </summary>
public class FlightShadowPriceMap : GanttSchedulingShadowPriceMap<Aircraft, FlightTaskAssignment> {
    /// <summary>
    /// 查找特定飞机的影子价格。
    /// Looks up the shadow price for a specific aircraft.
    /// </summary>
    public Flt64 Invoke(Aircraft aircraft)
        => Invoke(new BunchGanttSchedulingShadowPriceArguments<Aircraft, FlightTaskAssignment>(aircraft, null, null));

    /// <summary>
    /// 查找特定航班任务的影子价格。
    /// Looks up the shadow price for a specific flight task.
    /// </summary>
    public Flt64 Invoke(FlightTask task)
        => Invoke(new BunchGanttSchedulingShadowPriceArguments<Aircraft, FlightTaskAssignment>(task.Aircraft!, task, null));

    /// <summary>
    /// 查找任务对（前一个和当前）的影子价格。
    /// Looks up the shadow price for a task pair (previous and current).
    /// </summary>
    public Flt64 Invoke(FlightTask? prevTask, FlightTask? task) {
        if (prevTask is null && task is null) return Flt64.Zero;
        return Invoke(new BunchGanttSchedulingShadowPriceArguments<Aircraft, FlightTaskAssignment>(
            task?.Aircraft ?? prevTask!.Aircraft!, task, prevTask));
    }

    /// <summary>
    /// 使用影子价格计算批次的缩减成本。
    /// Computes the reduced cost of a bunch using shadow prices.
    /// </summary>
    public Flt64 ReducedCost(FlightTaskBunch bunch) {
        Flt64 ret = bunch.Cost.CostSum ?? Flt64.Zero;
        if (bunch.Executor.Indexed) {
            ret -= Invoke(bunch.Executor);
            for (int i = 0; i < bunch.Tasks.Count; i++) {
                var prevTask = i > 0 ? bunch.Tasks[i - 1] : bunch.LastTask;
                ret -= Invoke(prevTask, bunch.Tasks[i]);
            }
            if (bunch.Tasks.Count > 0) {
                ret -= Invoke(bunch.Tasks[^1], null);
            }
        }
        return ret;
    }
}
