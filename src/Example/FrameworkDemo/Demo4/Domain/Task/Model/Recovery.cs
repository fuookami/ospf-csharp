#nullable enable

using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;

/// <summary>
/// 航班任务分配策略（包含可选飞机、时间和航线变更）。
/// Flight task assignment policy with optional aircraft, time, and route changes.
/// </summary>
/// <param name="Aircraft">分配的飞机 / Assigned aircraft.</param>
/// <param name="Time">分配的时间范围 / Assigned time range.</param>
/// <param name="Route">分配的航线 / Assigned route.</param>
public sealed record FlightTaskAssignment(
    Aircraft? Aircraft = null,
    Fuookami.Ospf.Framework.GanttScheduling.Infrastructure.TimeRange? Time = null,
    Route? Route = null
) : IAssignmentPolicy<Aircraft> {
    /// <summary>是否为空分配 / Whether the assignment is empty.</summary>
    public bool Empty => Aircraft is null && Time is null && Route is null;

    /// <inheritdoc/>
    Aircraft? IAssignmentPolicy<Aircraft>.Executor => Aircraft;

    /// <inheritdoc/>
    Fuookami.Ospf.Framework.GanttScheduling.Infrastructure.TimeRange? IAssignmentPolicy<Aircraft>.Time => Time;
}

/// <summary>
/// 记录从一架飞机到另一架飞机的变更。
/// Records an aircraft change from one aircraft to another.
/// </summary>
/// <param name="From">原飞机 / The original aircraft.</param>
/// <param name="To">新飞机 / The new aircraft.</param>
public sealed record AircraftChange(Aircraft From, Aircraft To);

/// <summary>
/// 记录从一种类型到另一种类型的飞机类型变更。
/// Records an aircraft type change from one type to another.
/// </summary>
/// <param name="From">原类型 / The original type.</param>
/// <param name="To">新类型 / The new type.</param>
public sealed record AircraftTypeChange(AircraftType From, AircraftType To);

/// <summary>
/// 记录从一个子类型到另一个子类型的飞机子类型变更。
/// Records an aircraft minor type change from one minor type to another.
/// </summary>
/// <param name="From">原子类型 / The original minor type.</param>
/// <param name="To">新子类型 / The new minor type.</param>
public sealed record AircraftMinorTypeChange(AircraftMinorType From, AircraftMinorType To);

/// <summary>
/// 记录从一条航线到另一条航线的变更。
/// Records a route change from one route to another.
/// </summary>
/// <param name="From">原航线 / The original route.</param>
/// <param name="To">新航线 / The new route.</param>
public sealed record RouteChange(Route From, Route To);

/// <summary>
/// 用于查找的航班任务与其恢复策略配对的键。
/// Key pairing a flight task with its recovery policy for lookup purposes.
/// </summary>
/// <param name="Task">航班任务 / The flight task.</param>
/// <param name="Policy">恢复策略 / The recovery policy.</param>
public sealed record RecoveryFlightTaskKey(FlightTask Task, FlightTaskAssignment Policy);
