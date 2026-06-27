#nullable enable

using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using System;
using System.Linq;
using System.Collections.Generic;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;

/// <summary>
/// 分配给单架飞机的航班任务束（具有成本和时间跟踪）。
/// A bunch of flight tasks assigned to a single aircraft with cost and time tracking.
/// </summary>
public sealed class FlightTaskBunch : AbstractTaskBunch<FlightTask, Aircraft, FlightTaskAssignment> {
    /// <summary>
    /// 主构造函数 / Primary constructor.
    /// </summary>
    public FlightTaskBunch(
        Aircraft aircraft,
        TimeRange time,
        Airport dep,
        Airport arr,
        IReadOnlyList<FlightTask> tasks,
        ICost<Flt64> cost,
        long iteration
    ) : base(aircraft, time, tasks, cost, ToInitialUsability(aircraft.Usability), iteration) {
        Dep = dep;
        Arr = arr;
    }

    /// <summary>
    /// 从飞机、机场和时间窗构造空束 / Construct empty bunch from aircraft, airport, and time window.
    /// </summary>
    public FlightTaskBunch(
        Aircraft aircraft,
        Airport airport,
        DateTimeOffset time,
        long iteration
    ) : base(aircraft, new TimeRange(time, DateTimeOffset.MaxValue), Array.Empty<FlightTask>(),
        new ImmutableCost<Flt64>(Array.Empty<CostItem<Flt64>>()), ToInitialUsability(aircraft.Usability), iteration) {
        Dep = airport;
        Arr = airport;
    }

    /// <summary>
    /// 从飞机和任务列表构造 / Construct from aircraft and task list.
    /// </summary>
    public FlightTaskBunch(
        Aircraft aircraft,
        IReadOnlyList<FlightTask> tasks,
        long iteration,
        ICost<Flt64>? cost = null
    ) : base(aircraft, ToInitialUsability(aircraft.Usability), tasks, cost, iteration) {
        Dep = tasks[0].Dep;
        Arr = tasks[^1].Arr;
    }

    /// <summary>出发机场 / Departure airport.</summary>
    public Airport Dep { get; }

    /// <summary>到达机场 / Arrival airport.</summary>
    public Airport Arr { get; }

    /// <summary>飞机变更次数 / Aircraft change count.</summary>
    public ulong AircraftChangeCount {
        get {
            ulong count = 0;
            foreach (var task in Tasks) {
                if (task.AircraftChanged) count++;
            }
            return count;
        }
    }

    /// <summary>
    /// 返回此任务束中给定原始任务的恢复版本。
    /// Returns the recovered version of the given origin task within this bunch.
    /// </summary>
    public FlightTask? Get(FlightTask originTask) {
        if (!Keys.TryGetValue(originTask.Key, out int idx)) return null;
        return Tasks[idx];
    }

    /// <summary>
    /// 检查此任务束中是否有任务在时间窗口内到达机场。
    /// Checks whether any task in this bunch arrives at the airport within the time window.
    /// </summary>
    public bool ArrivedWhen(Airport airport, TimeRange timeWindow) {
        if (!Time.WithIntersection(timeWindow)) return false;
        foreach (var task in Tasks) {
            if (task.ArrivedWhen(airport, timeWindow)) return true;
            if (task.Time!.End >= timeWindow.End) break;
        }
        return false;
    }

    /// <summary>
    /// 检查此任务束中是否有任务在时间窗口内从机场出发。
    /// Checks whether any task in this bunch departs from the airport within the time window.
    /// </summary>
    public bool DepartedWhen(Airport airport, TimeRange timeWindow) {
        if (!Time.WithIntersection(timeWindow)) return false;
        foreach (var task in Tasks) {
            if (task.DepartedWhen(airport, timeWindow)) return true;
            if (task.Time!.End >= timeWindow.End) break;
        }
        return false;
    }

    /// <summary>
    /// 检查飞机是否在时间窗口内位于机场。
    /// Checks whether the aircraft is located at the airport within the time window.
    /// </summary>
    public bool LocatedWhen(Airport airport, TimeRange timeWindow) {
        if (Tasks.Count == 0) return false;
        if (Tasks[0].DepartedWhen(airport, timeWindow)) return true;
        if (Tasks[^1].ArrivedWhen(airport, timeWindow)) return true;
        for (int i = 1; i < Tasks.Count; i++) {
            if (Tasks[i].LocatedWhen(Tasks[i - 1], airport, timeWindow)) return true;
        }
        return false;
    }

    /// <inheritdoc/>
    public override string ToString()
        => $"{Executor.RegNo}, {Dep.Icao} - {Arr.Icao}, {Time.Start.ToShortString()} - {Time.End.ToShortString()}, {Tasks.Count} tasks";

    private static ExecutorInitialUsability<FlightTask, Aircraft, FlightTaskAssignment> ToInitialUsability(AircraftUsability usability)
        => new(usability.LastTask as FlightTask, usability.EnabledTime);
}
