#nullable enable

using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchGeneration.Service;

/// <summary>
/// 为每架飞机生成初始航班任务束。
/// Generates initial flight task bunches for each aircraft.
/// </summary>
public sealed class InitialFlightTaskBunchGenerator {
    private readonly FlightTaskFeasibilityJudger _feasibilityJudger;
    private readonly ConnectionTimeCalculator _connectionTimeCalculator;
    private readonly MinimumDepartureTimeCalculator _minimumDepartureTimeCalculator;
    private readonly TotalCostCalculator _costCalculator;

    private static readonly FlightTaskFeasibilityJudger.Config Config = new(TimeExtractor: t => t.Time);

    /// <summary>
    /// 构造函数 / Constructor.
    /// </summary>
    public InitialFlightTaskBunchGenerator(
        FlightTaskFeasibilityJudger feasibilityJudger,
        ConnectionTimeCalculator connectionTimeCalculator,
        MinimumDepartureTimeCalculator minimumDepartureTimeCalculator,
        TotalCostCalculator costCalculator) {
        _feasibilityJudger = feasibilityJudger;
        _connectionTimeCalculator = connectionTimeCalculator;
        _minimumDepartureTimeCalculator = minimumDepartureTimeCalculator;
        _costCalculator = costCalculator;
    }

    /// <summary>
    /// 为给定飞机生成初始束。
    /// Generates an initial bunch for the given aircraft.
    /// </summary>
    public FlightTaskBunch? Invoke(Aircraft aircraft, AircraftUsability usability, IReadOnlyList<FlightTask> lockedTasks, FlightTaskBunch originBunch)
        => SoftRecovery(aircraft, usability, lockedTasks, originBunch) ?? EmptyBunch(aircraft, usability, lockedTasks);

    /// <summary>
    /// 生成仅包含锁定任务的空束。
    /// Generates an empty bunch with only locked tasks.
    /// </summary>
    public FlightTaskBunch? EmptyBunch(Aircraft aircraft, AircraftUsability usability, IReadOnlyList<FlightTask> lockedTasks) {
        if (lockedTasks.Count == 0) return null;
        var flightTasks = RecoveryFlightTasks(aircraft, usability, lockedTasks);
        if (flightTasks.Count == 0) return null;
        var cost = _costCalculator(aircraft, flightTasks);
        return cost is null ? null : new FlightTaskBunch(aircraft, flightTasks, 0, cost);
    }

    private FlightTaskBunch? SoftRecovery(Aircraft aircraft, AircraftUsability usability, IReadOnlyList<FlightTask> lockedTasks, FlightTaskBunch originBunch) {
        var flightTasks = lockedTasks.Count == 0
            ? RecoveryFlightTasks(aircraft, usability, originBunch.Tasks)
            : SoftRecoveryWithLocks(aircraft, usability, lockedTasks, originBunch);
        if (flightTasks.Count == 0) return null;
        var cost = _costCalculator(aircraft, flightTasks);
        return cost is null ? null : new FlightTaskBunch(aircraft, flightTasks, 0, cost);
    }

    private List<FlightTask> SoftRecoveryWithLocks(Aircraft aircraft, AircraftUsability usability, IReadOnlyList<FlightTask> lockedTasks, FlightTaskBunch originBunch) {
        var result = new List<FlightTask>();
        var inserted = new HashSet<FlightTask>();
        var lastFlightTask = usability.LastTask as FlightTask;
        var currentLocation = usability.Location;
        var currentTime = lastFlightTask is not null ? lastFlightTask.Time!.End : usability.EnabledTime;

        foreach (var lockedTask in lockedTasks) {
            var prevTask = result.Count > 0 ? result[^1] : lastFlightTask;
            bool flag = false;
            foreach (var task in originBunch.Tasks) {
                if (inserted.Contains(task)) continue;
                var connTime = prevTask is not null ? _connectionTimeCalculator(aircraft, prevTask, task) : TimeSpan.Zero;
                var depTime = _minimumDepartureTimeCalculator(currentTime, aircraft, task, connTime);
                var actualTime = new TimeRange(depTime, depTime + task.DurationFor(aircraft));
                var policy = actualTime == task.ScheduledTime ? new FlightTaskAssignment() : new FlightTaskAssignment(Time: actualTime);
                var recovered = policy.Empty ? task : task.RecoveryEnabled(policy) ? task.Recovery(policy) : null;
                if (recovered is not null && currentLocation == recovered.Dep && recovered.Arr == lockedTask.Dep) {
                    var thisConn = _connectionTimeCalculator(aircraft, recovered, lockedTask);
                    var thisDep = _minimumDepartureTimeCalculator(actualTime.End, aircraft, lockedTask, thisConn);
                    if (thisDep == lockedTask.Time!.Start) {
                        flag = true;
                        result.Add(task);
                        result.Add(lockedTask);
                        currentTime = lockedTask.Time.End;
                        currentLocation = lockedTask.Arr;
                        break;
                    }
                } else if (recovered is not null && currentLocation == task.Dep) {
                    result.Add(task);
                    inserted.Add(task);
                    currentTime = actualTime.End;
                    currentLocation = task.Arr;
                }
            }
            if (!flag && currentLocation == lockedTask.Dep) {
                result.Add(lockedTask);
                currentTime = lockedTask.Time!.End;
                currentLocation = lockedTask.Arr;
            }
        }

        foreach (var task in originBunch.Tasks) {
            if (inserted.Contains(task)) continue;
            var prevTask = result.Count > 0 ? result[^1] : lastFlightTask;
            var connTime = prevTask is not null ? _connectionTimeCalculator(aircraft, prevTask, task) : TimeSpan.Zero;
            var depTime = _minimumDepartureTimeCalculator(currentTime, aircraft, task, connTime);
            var actualTime = new TimeRange(depTime, depTime + task.DurationFor(aircraft));
            var policy = actualTime == task.ScheduledTime ? new FlightTaskAssignment() : new FlightTaskAssignment(Time: actualTime);
            var recovered = policy.Empty ? task : task.RecoveryEnabled(policy) ? task.Recovery(policy) : null;
            if (recovered is not null && currentLocation == task.Dep) {
                result.Add(task);
                inserted.Add(task);
                currentTime = actualTime.End;
                currentLocation = task.Arr;
            }
        }

        return RecoveryFlightTasks(aircraft, usability, result);
    }

    private List<FlightTask> RecoveryFlightTasks(Aircraft aircraft, AircraftUsability usability, IReadOnlyList<FlightTask> tasks) {
        var result = new List<FlightTask>();
        if (tasks.Count == 0) return result;
        var lastTask = usability.LastTask as FlightTask;
        var time = lastTask is not null ? lastTask.Time!.End : usability.EnabledTime;

        for (int i = 0; i < tasks.Count; i++) {
            var task = tasks[i];
            var prevTask = result.Count > 0 ? result[^1] : lastTask;
            var connTime = prevTask is not null ? _connectionTimeCalculator(aircraft, prevTask, task) : TimeSpan.Zero;
            time = _minimumDepartureTimeCalculator(time, aircraft, task, connTime);
            var recoveredTime = new TimeRange(time, time + task.DurationFor(aircraft));
            var policy = recoveredTime == task.ScheduledTime ? new FlightTaskAssignment() : new FlightTaskAssignment(Time: recoveredTime);
            var recovered = policy.Empty ? task : task.RecoveryEnabled(policy) ? task.Recovery(policy) : null;
            if (recovered is not null) {
                if (!_feasibilityJudger.Invoke(aircraft, prevTask, recovered, Config)) continue;
                result.Add(recovered);
                time += recovered.DurationFor(aircraft);
            }
        }
        return result;
    }
}
