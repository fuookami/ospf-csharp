#nullable enable

using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;

/// <summary>
/// 基于出发/到达机场类型枚举航班类型。
/// Enumerates the flight types based on departure/arrival airport types.
/// </summary>
public enum FlightType {
    /// <summary>国内 / Domestic.</summary>
    Domestic,
    /// <summary>地区 / Regional.</summary>
    Regional,
    /// <summary>国际 / International.</summary>
    International
}

/// <summary>
/// 航班类型扩展方法。
/// Extension methods for flight type.
/// </summary>
public static class FlightTypeExtensions {
    /// <summary>
    /// 是否为国内类型。
    /// Whether this is a domestic type.
    /// </summary>
    public static bool IsDomesticType(this FlightType type) => type == FlightType.Domestic;

    /// <summary>
    /// 根据出发和到达机场类型确定航班类型。
    /// Determines the flight type from departure and arrival airport types.
    /// </summary>
    public static FlightType FromAirportTypes(AirportType dep, AirportType arr) {
        int maxOrdinal = global::System.Math.Max((int)dep, (int)arr);
        return (FlightType)maxOrdinal;
    }
}

/// <summary>
/// 具有计划/估计/实际时间、飞机和航线信息的航段计划。
/// A flight leg plan with scheduled/estimated/actual times, aircraft, and route information.
/// </summary>
public sealed class FlightLegPlan : FlightTaskPlan {
    /// <summary>前缀 / Prefix.</summary>
    public const string Prefix = "f";

    private readonly FlightType _type;
    private readonly Aircraft _aircraft;
    private readonly ISet<Aircraft> _enabledAircrafts;
    private readonly Airport _dep;
    private readonly Airport _arr;
    private readonly TimeRange _scheduledTime;
    private readonly TimeRange? _estimatedTime;
    private readonly TimeRange? _actualTime;
    private readonly DateTimeOffset? _outTime;

    /// <summary>
    /// 构造函数 / Constructor.
    /// </summary>
    public FlightLegPlan(
        string actualId,
        string no,
        FlightType type,
        DateTimeOffset date,
        Aircraft aircraft,
        ISet<Aircraft> enabledAircrafts,
        Airport dep,
        Airport arr,
        TimeRange scheduledTime,
        TimeRange? estimatedTime,
        TimeRange? actualTime,
        DateTimeOffset? outTime,
        FlightTaskStatus flightTaskStatus,
        Flt64? weight = null
    ) : base($"{Prefix}_{actualId}", $"{no}_{date:yyyyMMdd}", flightTaskStatus) {
        ActualId = actualId;
        No = no;
        _type = type;
        Date = date;
        _aircraft = aircraft;
        _enabledAircrafts = enabledAircrafts;
        _dep = dep;
        _arr = arr;
        _scheduledTime = scheduledTime;
        _estimatedTime = estimatedTime;
        _actualTime = actualTime;
        _outTime = outTime;
        Weight = weight ?? Flt64.One;
    }

    /// <inheritdoc/>
    public override string ActualId { get; }

    /// <summary>航班号 / Flight number.</summary>
    public string No { get; }

    /// <summary>航班类型 / Flight type.</summary>
    public FlightType FlightType => _type;

    /// <summary>日期 / Date.</summary>
    public DateTimeOffset Date { get; }

    /// <inheritdoc/>
    public override string DisplayName => No;

    /// <inheritdoc/>
    public override Aircraft Executor => _aircraft;

    /// <inheritdoc/>
    public override ISet<Aircraft> EnabledExecutors => _enabledAircrafts;

    /// <inheritdoc/>
    public override Airport Dep => _dep;

    /// <inheritdoc/>
    public override Airport Arr => _arr;

    /// <inheritdoc/>
    public override TimeRange ScheduledTime => _scheduledTime;

    /// <inheritdoc/>
    public override TimeRange? Time => _actualTime ?? _estimatedTime ?? base.Time;

    /// <summary>权重 / Weight.</summary>
    public new Flt64 Weight { get; }

    /// <summary>
    /// 检查此航段是否有资格进行恢复（无实际时间或推出时间）。
    /// Checks whether this flight leg is eligible for recovery (no actual time or out time).
    /// </summary>
    public bool RecoveryEnabled() => _actualTime is null && _outTime is null;

    /// <inheritdoc/>
    public override TimeSpan DurationFor(Aircraft aircraft) => _scheduledTime.Duration;
}

/// <summary>
/// 航段的任务类型对象。
/// Task type object for flight legs.
/// </summary>
public sealed record FlightLegTaskType : FlightTaskType {
    /// <summary>共享实例 / Shared instance.</summary>
    public static readonly FlightLegTaskType Instance = new();

    private FlightLegTaskType() : base(FlightTaskCategory.Flight, typeof(FlightLegTaskType)) { }

    /// <inheritdoc/>
    public override string TypeName => "flight";
}

/// <summary>
/// 具有可选恢复飞机和时间的航段任务。
/// A flight leg task with optional recovery aircraft and time.
/// </summary>
public sealed class FlightLeg : FlightTask {
    private readonly FlightLegPlan _plan;
    private readonly Aircraft? _recoveryAircraft;
    private readonly TimeRange? _recoveryTime;

    internal FlightLeg(
        FlightLegPlan plan,
        Aircraft? recoveryAircraft = null,
        TimeRange? recoveryTime = null,
        FlightLeg? origin = null
    ) : base(FlightLegTaskType.Instance, origin) {
        _plan = plan;
        _recoveryAircraft = recoveryAircraft;
        _recoveryTime = recoveryTime;
    }

    /// <summary>
    /// 从计划创建 FlightLeg。
    /// Creates a FlightLeg from a plan.
    /// </summary>
    public static FlightLeg Create(FlightLegPlan plan) => new(plan);

    /// <summary>
    /// 创建应用给定恢复策略的已恢复 FlightLeg。
    /// Creates a recovered FlightLeg applying the given recovery policy.
    /// </summary>
    public static FlightLeg Create(FlightLeg origin, FlightTaskAssignment recoveryPolicy) {
        var recoveryAircraft = recoveryPolicy.Aircraft is null || recoveryPolicy.Aircraft == origin.Aircraft
            ? null : recoveryPolicy.Aircraft;
        var recoveryTime = recoveryPolicy.Time is null || recoveryPolicy.Time == origin.ScheduledTime
            ? null : recoveryPolicy.Time;
        return new FlightLeg(origin._plan, recoveryAircraft, recoveryTime, origin);
    }

    /// <inheritdoc/>
    public override FlightTaskPlan Plan => _plan;

    /// <inheritdoc/>
    public override Aircraft? Aircraft => _recoveryAircraft ?? _plan.Executor;

    /// <inheritdoc/>
    public override TimeRange? Time => _recoveryTime ?? Plan.Time;

    /// <inheritdoc/>
    public override bool RecoveryEnabled(TimeRange timeWindow)
        => _plan.RecoveryEnabled() && base.RecoveryEnabled(timeWindow);

    /// <inheritdoc/>
    public override bool RecoveryNeeded(TimeRange timeWindow)
        => _plan.RecoveryEnabled() && timeWindow.Contains(Time!.Start);

    /// <inheritdoc/>
    public override bool Recovered => _recoveryAircraft is not null || _recoveryTime is not null;

    /// <inheritdoc/>
    public override FlightTaskAssignment RecoveryPolicy => new(_recoveryAircraft, _recoveryTime, null);

    /// <inheritdoc/>
    public override bool RecoveryEnabled(FlightTaskAssignment policy) {
        if (!AircraftChangeEnabled && policy.Aircraft is not null && Aircraft != policy.Aircraft) return false;
        if (!AircraftTypeChangeEnabled && policy.Aircraft is not null && Aircraft!.Type != policy.Aircraft.Type) return false;
        if (!AircraftMinorTypeChangeEnabled && policy.Aircraft is not null && Aircraft!.MinorType != policy.Aircraft.MinorType) return false;
        if (!DelayEnabled && policy.Time is not null && Plan.ScheduledTime!.Start < policy.Time.Start) return false;
        if (!AdvanceEnabled && policy.Time is not null && Plan.ScheduledTime!.Start > policy.Time.Start) return false;
        return true;
    }

    /// <inheritdoc/>
    public override FlightTask Recovery(FlightTaskAssignment policy) => Create(this, policy);

    /// <inheritdoc/>
    public override bool AircraftChanged => _recoveryAircraft is not null;

    /// <inheritdoc/>
    public override bool AircraftTypeChanged => _recoveryAircraft?.Type != _plan.Executor.Type;

    /// <inheritdoc/>
    public override bool AircraftMinorTypeChanged => _recoveryAircraft?.MinorType != _plan.Executor.MinorType;

    /// <inheritdoc/>
    public override AircraftChange? AircraftChange => _recoveryAircraft is not null ? new(_plan.Executor, _recoveryAircraft) : null;

    /// <inheritdoc/>
    public override AircraftTypeChange? AircraftTypeChange {
        get {
            if (_recoveryAircraft is null || _recoveryAircraft.Type == _plan.Executor.Type) return null;
            return new AircraftTypeChange(_plan.Executor.Type, _recoveryAircraft.Type);
        }
    }

    /// <inheritdoc/>
    public override AircraftMinorTypeChange? AircraftMinorTypeChange {
        get {
            if (_recoveryAircraft is null || _recoveryAircraft.MinorType == _plan.Executor.MinorType) return null;
            return new AircraftMinorTypeChange(_plan.Executor.MinorType, _recoveryAircraft.MinorType);
        }
    }

    /// <inheritdoc/>
    public override string ToString()
        => $"{_plan.No}, {Aircraft!.RegNo}, {Dep.Icao} - {Arr.Icao}, {Time!.Start.ToShortString()} - {Time.End.ToShortString()}";
}
