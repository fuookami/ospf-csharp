#nullable enable

using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using System;
using System.Linq;
using System.Collections.Generic;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;

/// <summary>
/// 枚举维护类别及其稳定状态标志。
/// Enumerates the maintenance categories with their stable status flags.
/// </summary>
public enum MaintenanceCategory {
    /// <summary>航线维护 / Line maintenance.</summary>
    Line,
    /// <summary>计划维护 / Schedule maintenance.</summary>
    Schedule
}

/// <summary>
/// 维护类别扩展方法。
/// Extension methods for maintenance category.
/// </summary>
public static class MaintenanceCategoryExtensions {
    /// <summary>
    /// 获取维护类别的稳定状态。
    /// Gets the stable status for the maintenance category.
    /// </summary>
    public static FlightTaskStatus StableStatus(this MaintenanceCategory category) => category switch {
        MaintenanceCategory.Line => FlightTaskStatus.NotAdvance | FlightTaskStatus.NotAircraftChange
            | FlightTaskStatus.NotAircraftTypeChange | FlightTaskStatus.NotAircraftMinorTypeChange,
        MaintenanceCategory.Schedule => FlightTaskStatus.NotDelay | FlightTaskStatus.NotAdvance
            | FlightTaskStatus.NotAircraftChange | FlightTaskStatus.NotAircraftTypeChange | FlightTaskStatus.NotAircraftMinorTypeChange,
        _ => throw new ArgumentOutOfRangeException(nameof(category))
    };

    /// <inheritdoc/>
    public static string ToShortString(this MaintenanceCategory category) => category switch {
        MaintenanceCategory.Line => "line",
        MaintenanceCategory.Schedule => "schedule",
        _ => throw new ArgumentOutOfRangeException(nameof(category))
    };
}

/// <summary>
/// 具有飞机、时间、机场、类别和过期信息的维护计划。
/// A maintenance plan with aircraft, time, airport, category, and expiration information.
/// </summary>
public sealed class MaintenancePlan : FlightTaskPlan {
    private const string Prefix = "m";
    private readonly Airport _airport;
    private readonly IReadOnlyList<Airport> _airportBackup;

    internal MaintenancePlan(
        Aircraft aircraft,
        TimeRange scheduledTime,
        Airport airport,
        IReadOnlyList<Airport> airportBackup,
        MaintenanceCategory category,
        DateTimeOffset expirationTime,
        FlightTaskStatus status,
        string? actualId = null
    ) : base($"{Prefix}_{(actualId ?? Guid.NewGuid().ToString("N"))}", $"{aircraft.RegNo}_{category.ToShortString()}_{scheduledTime.Start.ToShortString()}", status) {
        ActualId = actualId ?? Guid.NewGuid().ToString("N");
        Aircraft = aircraft;
        ScheduledTime = scheduledTime;
        _airport = airport;
        _airportBackup = airportBackup;
        Category = category;
        ExpirationTime = expirationTime;
    }

    /// <inheritdoc/>
    public override string ActualId { get; }

    /// <summary>飞机 / Aircraft.</summary>
    public Aircraft Aircraft { get; }

    /// <inheritdoc/>
    public override Aircraft Executor => Aircraft;

    /// <inheritdoc/>
    public override ISet<Aircraft> EnabledExecutors => new HashSet<Aircraft> { Aircraft };

    /// <inheritdoc/>
    public override TimeRange ScheduledTime { get; }

    /// <summary>机场 / Airport.</summary>
    public Airport Airport => _airport;

    /// <summary>备用机场列表 / Backup airports.</summary>
    public IReadOnlyList<Airport> AirportBackup => _airportBackup;

    /// <summary>维护类别 / Maintenance category.</summary>
    public MaintenanceCategory Category { get; }

    /// <summary>过期时间 / Expiration time.</summary>
    public DateTimeOffset ExpirationTime { get; }

    /// <inheritdoc/>
    public override string DisplayName => $"{Category}-{Aircraft.RegNo}";

    /// <inheritdoc/>
    public override Airport Dep => _airport;

    /// <inheritdoc/>
    public override Airport Arr => _airport;

    /// <inheritdoc/>
    public override IReadOnlyList<Airport> DepBackup => _airportBackup;

    /// <inheritdoc/>
    public override Airport? ActualArr(Airport dep) {
        if (dep == _airport) return Arr;
        if (_airportBackup.Contains(dep)) return dep;
        return null;
    }

    /// <inheritdoc/>
    public override TimeSpan? Duration => ScheduledTime.Duration;

    /// <inheritdoc/>
    public override TimeSpan DurationFor(Aircraft aircraft) => ScheduledTime.Duration;

    /// <inheritdoc/>
    public override TimeSpan ConnectionTime(FlightTask? succTask) => NotFlightStaticConnectionTime;

    /// <inheritdoc/>
    public override TimeSpan ConnectionTime(Aircraft aircraft, FlightTask? succTask) => NotFlightStaticConnectionTime;

    /// <summary>
    /// 创建 MaintenancePlan 工厂方法。
    /// Factory method for creating a MaintenancePlan.
    /// </summary>
    public static MaintenancePlan Create(
        Aircraft aircraft,
        TimeRange scheduledTime,
        IReadOnlyList<Airport> airports,
        DateTimeOffset expirationTime,
        MaintenanceCategory category,
        TimeRange timeWindow) {
        var status = category.StableStatus();
        if (expirationTime <= timeWindow.End) status |= FlightTaskStatus.NotCancel;
        if (airports.Count == 1) status |= FlightTaskStatus.NotTerminalChange;
        return new MaintenancePlan(aircraft, scheduledTime, airports[0],
            airports.Count > 1 ? airports.Skip(1).ToArray() : Array.Empty<Airport>(),
            category, expirationTime, status);
    }
}

/// <summary>
/// 维护事件的任务类型对象。
/// Task type object for maintenance events.
/// </summary>
public sealed record MaintenanceFlightTaskType : FlightTaskType {
    /// <summary>共享实例 / Shared instance.</summary>
    public static readonly MaintenanceFlightTaskType Instance = new();
    private MaintenanceFlightTaskType() : base(FlightTaskCategory.Maintenance, typeof(MaintenanceFlightTaskType)) { }
    /// <inheritdoc/>
    public override string TypeName => "Maintenance";
}

/// <summary>
/// 具有可选恢复时间和机场的维护航班任务。
/// A maintenance flight task with optional recovery time and airport.
/// </summary>
public sealed class Maintenance : FlightTask {
    private readonly MaintenancePlan _plan;
    private readonly TimeRange? _recoveryTime;
    private readonly Airport? _recoveryAirport;

    internal Maintenance(
        MaintenancePlan plan,
        TimeRange? recoveryTime = null,
        Airport? recoveryAirport = null,
        Maintenance? origin = null
    ) : base(MaintenanceFlightTaskType.Instance, origin) {
        _plan = plan;
        _recoveryTime = recoveryTime;
        _recoveryAirport = recoveryAirport;
    }

    /// <summary>从计划创建 / Create from plan.</summary>
    public static Maintenance Create(MaintenancePlan plan) => new(plan);

    /// <summary>创建已恢复的维护任务 / Create recovered maintenance.</summary>
    public static Maintenance Create(Maintenance origin, FlightTaskAssignment policy) {
        var recoveryTime = policy.Time is null || policy.Time == origin.ScheduledTime ? null : policy.Time;
        Airport? recoveryAirport = null;
        if (policy.Route is not null && !(policy.Route.Dep == origin.Dep && policy.Route.Arr == origin.Arr)) {
            recoveryAirport = policy.Route.Dep;
        }
        return new Maintenance(origin._plan, recoveryTime, recoveryAirport, origin);
    }

    /// <inheritdoc/>
    public override FlightTaskPlan Plan => _plan;

    /// <inheritdoc/>
    public override Airport Dep => _recoveryAirport ?? _plan.Dep;

    /// <inheritdoc/>
    public override Airport Arr => _recoveryAirport ?? _plan.Arr;

    /// <inheritdoc/>
    public override TimeRange? Time => _recoveryTime ?? Plan.Time;

    /// <inheritdoc/>
    public override bool Recovered => _recoveryTime is not null || _recoveryAirport is not null;

    /// <inheritdoc/>
    public override FlightTaskAssignment RecoveryPolicy => new(null, _recoveryTime, _recoveryAirport is not null ? new Route(_recoveryAirport, _recoveryAirport) : null);

    /// <inheritdoc/>
    public override bool RecoveryEnabled(TimeRange timeWindow)
        => _plan.ExpirationTime < timeWindow.End || timeWindow.Contains(ScheduledTime!.Start);

    /// <inheritdoc/>
    public override bool RecoveryNeeded(TimeRange timeWindow)
        => _plan.ExpirationTime < timeWindow.End || timeWindow.Contains(ScheduledTime!.Start);

    /// <inheritdoc/>
    public override bool RecoveryEnabled(FlightTaskAssignment policy) {
        if (policy.Aircraft is not null && Aircraft != policy.Aircraft) return false;
        if (!DelayEnabled && policy.Time is not null && ScheduledTime!.Start < policy.Time.Start) return false;
        if (!AdvanceEnabled && policy.Time is not null && ScheduledTime!.Start > policy.Time.Start) return false;
        return true;
    }

    /// <inheritdoc/>
    public override FlightTask Recovery(FlightTaskAssignment policy) => Create(this, policy);

    /// <inheritdoc/>
    public override bool RouteChanged => _recoveryAirport is not null;

    /// <inheritdoc/>
    public override RouteChange? RouteChange => _recoveryAirport is not null
        ? new RouteChange(new Route(_plan.Airport, _plan.Airport), new Route(_recoveryAirport, _recoveryAirport))
        : null;
}
