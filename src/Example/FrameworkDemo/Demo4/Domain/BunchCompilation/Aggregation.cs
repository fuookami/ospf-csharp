#nullable enable

using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchCompilation;

/// <summary>
/// 束编译聚合，组合任务时间用于列生成过程。
/// Aggregation for bunch compilation combining task time for the column generation process.
/// </summary>
/// <remarks>
/// 继承框架 BunchCompilationAggregation，提供任务时间和完工时间管理。
/// Extends framework BunchCompilationAggregation, providing task time and makespan management.
/// </remarks>
public sealed class Aggregation
    : BunchCompilationAggregation<FlightTaskBunch, FlightTask, Aircraft, FlightTaskAssignment> {
    /// <summary>
    /// 构造函数 / Constructor.
    /// </summary>
    /// <param name="timeWindow">时间窗口 / Time window.</param>
    /// <param name="recoveryNeededAircrafts">需要恢复的飞机列表 / List of aircraft needing recovery.</param>
    /// <param name="recoveryNeededFlightTasks">需要恢复的航班任务列表 / List of flight tasks needing recovery.</param>
    public Aggregation(
        TimeWindow<Flt64> timeWindow,
        IReadOnlyList<Aircraft> recoveryNeededAircrafts,
        IReadOnlyList<FlightTask> recoveryNeededFlightTasks)
        : base(recoveryNeededFlightTasks, recoveryNeededAircrafts) {
        RecoveryNeededAircrafts = recoveryNeededAircrafts;
        RecoveryNeededFlightTasks = recoveryNeededFlightTasks;
        TaskTime = new BunchSchedulingTaskTime<FlightTaskBunch, FlightTask, Aircraft, FlightTaskAssignment>(
            timeWindow, recoveryNeededFlightTasks, Compilation);
    }

    /// <summary>需要恢复的飞机列表 / List of aircraft needing recovery.</summary>
    public IReadOnlyList<Aircraft> RecoveryNeededAircrafts { get; }

    /// <summary>需要恢复的航班任务列表 / List of flight tasks needing recovery.</summary>
    public IReadOnlyList<FlightTask> RecoveryNeededFlightTasks { get; }

    /// <summary>任务时间 / Task time.</summary>
    public BunchSchedulingTaskTime<FlightTaskBunch, FlightTask, Aircraft, FlightTaskAssignment> TaskTime { get; }

    /// <inheritdoc/>
    public override Try Register(object model) {
        Try result = base.Register(model);
        if (result.IsFailed) {
            return result;
        }
        return TaskTime.Register(model);
    }

    /// <inheritdoc/>
    public override Result<IReadOnlyList<FlightTaskBunch>, ErrorCode, Error<ErrorCode>> AddColumns(
        ulong iteration,
        IReadOnlyList<FlightTaskBunch> newBunches,
        object model) {
        var result = base.AddColumns(iteration, newBunches, model);
        if (result.IsFailed) {
            return result;
        }
        var undupBunches = ((Ok<IReadOnlyList<FlightTaskBunch>, ErrorCode, Error<ErrorCode>>)result).Value;
        TaskTime.AddColumns(iteration, undupBunches, model);
        return Results.Ok(undupBunches);
    }
}
