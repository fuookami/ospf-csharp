#nullable enable

using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;

/// <summary>
/// 具有出发、到达、时间窗和启用飞机集的中转航班计划。
/// A transfer flight plan with departure, arrival, time window, and enabled aircraft set.
/// </summary>
public sealed class TransferPlan : FlightTaskPlan {
    private const string Prefix = "tf";
    private readonly Airport _dep;
    private readonly Airport _arr;
    private readonly TimeRange _timeWindow;
    private readonly Aircraft? _aircraft;
    private readonly ISet<Aircraft> _enabledAircrafts;
    private readonly TimeSpan? _duration;

    internal TransferPlan(
        Airport dep,
        Airport arr,
        TimeRange timeWindow,
        Aircraft? aircraft,
        ISet<Aircraft> enabledAircrafts,
        TimeSpan? duration,
        FlightTaskStatus status,
        string? actualId = null
    ) : base($"{Prefix}_{(actualId ?? Guid.NewGuid().ToString("N"))}", $"transfer_{dep}_{arr}_{timeWindow.Start.ToShortString()}", status) {
        ActualId = actualId ?? Guid.NewGuid().ToString("N");
        _dep = dep;
        _arr = arr;
        _timeWindow = timeWindow;
        _aircraft = aircraft;
        _enabledAircrafts = enabledAircrafts;
        _duration = duration;
    }

    /// <summary>稳定状态 / Stable status.</summary>
    public static FlightTaskStatus StableStatus => FlightTaskStatus.NotDelay | FlightTaskStatus.NotAdvance | FlightTaskStatus.NotTerminalChange;

    /// <inheritdoc/>
    public override string ActualId { get; }

    /// <inheritdoc/>
    public override string DisplayName => "transfer";

    /// <inheritdoc/>
    public override Aircraft? Executor => _aircraft;

    /// <inheritdoc/>
    public override ISet<Aircraft> EnabledExecutors => _enabledAircrafts;

    /// <inheritdoc/>
    public override TimeRange? ScheduledTime => null;

    /// <inheritdoc/>
    public override Airport Dep => _dep;

    /// <inheritdoc/>
    public override Airport Arr => _arr;

    /// <inheritdoc/>
    public override TimeRange TimeWindow => _timeWindow;

    /// <inheritdoc/>
    public override TimeSpan? Duration => _duration ?? TimeSpan.Zero;

    /// <inheritdoc/>
    public override TimeSpan DurationFor(Aircraft aircraft) {
        if (_duration.HasValue) return _duration.Value;
        return aircraft.RouteFlyTime.GetFlyTime(_dep, _arr) ?? aircraft.MaxRouteFlyTime;
    }

    /// <summary>工厂方法 / Factory method.</summary>
    public static TransferPlan Create(Airport dep, Airport arr, TimeRange timeWindow, ISet<Aircraft> aircrafts, TimeSpan? duration = null) {
        var status = StableStatus;
        if (aircrafts.Count == 1) status |= FlightTaskStatus.NotAircraftChange;
        return new TransferPlan(dep, arr, timeWindow, System.Linq.Enumerable.First(aircrafts), aircrafts, duration, status);
    }
}

/// <summary>
/// 中转航班的任务类型对象。
/// Task type object for transfer flights.
/// </summary>
public sealed record TransferFlightTaskType : FlightTaskType {
    /// <summary>共享实例 / Shared instance.</summary>
    public static readonly TransferFlightTaskType Instance = new();
    private TransferFlightTaskType() : base(FlightTaskCategory.Flight, typeof(TransferFlightTaskType)) { }
    /// <inheritdoc/>
    public override string TypeName => "transfer";
}

/// <summary>
/// 具有可选恢复飞机和时间的中转航班任务。
/// A transfer flight task with optional recovery aircraft and time.
/// </summary>
public sealed class Transfer : FlightTask {
    private readonly TransferPlan _plan;
    private readonly Aircraft? _recoveryAircraft;
    private readonly TimeRange? _recoveryTime;

    internal Transfer(
        TransferPlan plan,
        Aircraft? recoveryAircraft = null,
        TimeRange? recoveryTime = null,
        Transfer? origin = null
    ) : base(TransferFlightTaskType.Instance, origin) {
        _plan = plan;
        _recoveryAircraft = recoveryAircraft;
        _recoveryTime = recoveryTime;
    }

    /// <summary>从计划创建 / Create from plan.</summary>
    public static Transfer Create(TransferPlan plan) => new(plan);

    /// <summary>创建已恢复的中转任务 / Create recovered transfer.</summary>
    public static Transfer Create(Transfer origin, FlightTaskAssignment policy) {
        var recoveryAircraft = origin._plan.Executor is not null
            && (policy.Aircraft is null || policy.Aircraft == origin._plan.Executor)
            ? null : policy.Aircraft;
        return new Transfer(origin._plan, recoveryAircraft, policy.Time!, origin);
    }

    /// <inheritdoc/>
    public override FlightTaskPlan Plan => _plan;

    /// <inheritdoc/>
    public override Aircraft? Aircraft => _recoveryAircraft ?? _plan.Executor;

    /// <inheritdoc/>
    public override TimeRange? Time => _recoveryTime ?? Plan.Time;

    /// <inheritdoc/>
    public override bool Recovered => _recoveryAircraft is not null || _recoveryTime is not null;

    /// <inheritdoc/>
    public override FlightTaskAssignment RecoveryPolicy => new(_recoveryAircraft, _recoveryTime, null);

    /// <inheritdoc/>
    public override bool RecoveryEnabled(FlightTaskAssignment policy) {
        if (policy.Aircraft is not null && !_plan.EnabledExecutors.Contains(policy.Aircraft)) return false;
        if (policy.Time is null || !_plan.TimeWindow.Contains(policy.Time)) return false;
        return true;
    }

    /// <inheritdoc/>
    public override FlightTask Recovery(FlightTaskAssignment policy) => Create(this, policy);
}
