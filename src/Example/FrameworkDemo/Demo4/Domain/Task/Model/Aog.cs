#nullable enable

using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using System;
using System.Linq;
using System.Collections.Generic;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;

/// <summary>
/// 具有固定飞机、时间和机场的 AOG（飞机停场）计划。
/// An AOG (Aircraft On Ground) plan with fixed aircraft, time, and airport.
/// </summary>
public sealed class AogPlan : FlightTaskPlan {
    private const string Prefix = "a";

    internal AogPlan(
        Aircraft aircraft,
        TimeRange scheduledTime,
        Airport airport,
        FlightTaskStatus status,
        string? actualId = null
    ) : base($"{Prefix}_{(actualId ?? Guid.NewGuid().ToString("N"))}", $"{aircraft.RegNo}_AOG_{scheduledTime.Start.ToShortString()}", status) {
        ActualId = actualId ?? Guid.NewGuid().ToString("N");
        Aircraft = aircraft;
        ScheduledTime = scheduledTime;
        Airport = airport;
    }

    /// <summary>稳定状态 / Stable status.</summary>
    public static FlightTaskStatus StableStatus => FlightTaskStatus.NotCancel | FlightTaskStatus.NotDelay
        | FlightTaskStatus.NotAdvance | FlightTaskStatus.NotAircraftChange
        | FlightTaskStatus.NotAircraftTypeChange | FlightTaskStatus.NotAircraftMinorTypeChange;

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
    public Airport Airport { get; }

    /// <inheritdoc/>
    public override string DisplayName => "AOG";

    /// <inheritdoc/>
    public override Airport Dep => Airport;

    /// <inheritdoc/>
    public override Airport Arr => Airport;

    /// <inheritdoc/>
    public override Airport? ActualArr(Airport dep) {
        if (dep == Dep) return Arr;
        if (DepBackup.Contains(dep)) return dep;
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

    /// <summary>工厂方法 / Factory method.</summary>
    public static AogPlan Create(Aircraft aircraft, TimeRange scheduledTime, Airport airport)
        => new(aircraft, scheduledTime, airport, StableStatus);
}

/// <summary>
/// AOG 事件的任务类型对象。
/// Task type object for AOG events.
/// </summary>
public sealed record AogFlightTaskType : FlightTaskType {
    /// <summary>共享实例 / Shared instance.</summary>
    public static readonly AogFlightTaskType Instance = new();
    private AogFlightTaskType() : base(FlightTaskCategory.AOG, typeof(AogFlightTaskType)) { }
    /// <inheritdoc/>
    public override string TypeName => "AOG";
}

/// <summary>
/// 具有可选恢复机场的 AOG 航班任务。
/// An AOG flight task with optional recovery airport.
/// </summary>
public sealed class Aog : FlightTask {
    private readonly AogPlan _plan;
    private readonly Airport? _recoveryAirport;

    internal Aog(AogPlan plan, Airport? recoveryAirport = null, Aog? origin = null)
        : base(AogFlightTaskType.Instance, origin) {
        _plan = plan;
        _recoveryAirport = recoveryAirport;
    }

    /// <summary>从计划创建 / Create from plan.</summary>
    public static Aog Create(AogPlan plan) => new(plan);

    /// <summary>创建已恢复的 AOG / Create recovered AOG.</summary>
    public static Aog Create(Aog origin, FlightTaskAssignment policy) {
        Airport? recoveryAirport = null;
        if (policy.Route is not null && !(policy.Route.Dep == origin.Dep && policy.Route.Arr == origin.Arr)) {
            recoveryAirport = policy.Route.Dep;
        }
        return new Aog(origin._plan, recoveryAirport, origin);
    }

    /// <inheritdoc/>
    public override FlightTaskPlan Plan => _plan;

    /// <inheritdoc/>
    public override Airport Dep => _recoveryAirport ?? _plan.Dep;

    /// <inheritdoc/>
    public override Airport Arr => _recoveryAirport ?? _plan.Arr;

    /// <inheritdoc/>
    public override bool Recovered => _recoveryAirport is not null;

    /// <inheritdoc/>
    public override FlightTaskAssignment RecoveryPolicy => new();

    /// <inheritdoc/>
    public override bool RecoveryEnabled(FlightTaskAssignment policy) {
        if (policy.Aircraft is not null && Aircraft != policy.Aircraft) return false;
        if (policy.Time is not null && Time != policy.Time) return false;
        return base.RecoveryEnabled(policy);
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
