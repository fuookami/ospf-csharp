#nullable enable

using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Concept;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;

/// <summary>
/// 枚举航班任务类别。
/// Enumerates the flight task categories.
/// </summary>
public enum FlightTaskCategory {
    /// <summary>航班 / Flight.</summary>
    Flight,
    /// <summary>虚拟航班 / Virtual flight.</summary>
    VirtualFlight,
    /// <summary>维护 / Maintenance.</summary>
    Maintenance,
    /// <summary>飞机停场 / Aircraft on ground.</summary>
    AOG
}

/// <summary>
/// 航班任务类别扩展方法。
/// Extension methods for flight task category.
/// </summary>
public static class FlightTaskCategoryExtensions {
    /// <summary>
    /// 是否为航班类型。
    /// Whether this is a flight type.
    /// </summary>
    public static bool IsFlightType(this FlightTaskCategory category)
        => category is FlightTaskCategory.Flight or FlightTaskCategory.VirtualFlight;
}

/// <summary>
/// 航班任务类型基类。
/// Abstract base for flight task types.
/// </summary>
/// <param name="Category">任务类别 / Task category.</param>
/// <param name="Cls">关联的 CLR 类型 / Associated CLR type.</param>
public abstract record FlightTaskType(FlightTaskCategory Category, Type Cls) {
    /// <summary>是否为航班类型 / Whether this is a flight type.</summary>
    public bool IsFlightType => Category.IsFlightType();

    /// <summary>类型名称 / Type name.</summary>
    public abstract string TypeName { get; }
}

/// <summary>
/// 枚举可在航班任务上设置的状态标志。
/// Enumerates the status flags that can be set on flight tasks.
/// </summary>
[Flags]
public enum FlightTaskStatus {
    /// <summary>不允许提前 / Not advance.</summary>
    NotAdvance = 1,
    /// <summary>不允许延迟 / Not delay.</summary>
    NotDelay = 2,
    /// <summary>不允许取消 / Not cancel.</summary>
    NotCancel = 4,
    /// <summary>不优先取消 / Not cancel preferred.</summary>
    NotCancelPreferred = 8,
    /// <summary>不允许飞机变更 / Not aircraft change.</summary>
    NotAircraftChange = 16,
    /// <summary>不允许飞机类型变更 / Not aircraft type change.</summary>
    NotAircraftTypeChange = 32,
    /// <summary>不允许飞机子类型变更 / Not aircraft minor type change.</summary>
    NotAircraftMinorTypeChange = 64,
    /// <summary>不允许终端变更 / Not terminal change.</summary>
    NotTerminalChange = 128,
    /// <summary>忽略强限制 / Strong limit ignored.</summary>
    StrongLimitIgnored = 256
}

/// <summary>
/// 航班任务状态到框架任务状态的映射辅助类。
/// Helper class for mapping flight task status to framework task status.
/// </summary>
public static class FlightTaskStatusMapping {
    /// <summary>
    /// 将航班任务状态集合转换为框架任务状态集合。
    /// Converts flight task status set to framework task status set.
    /// </summary>
    public static HashSet<TaskStatus> ToTaskStatus(FlightTaskStatus status) {
        var result = new HashSet<TaskStatus>();
        if (status.HasFlag(FlightTaskStatus.NotAdvance)) result.Add(TaskStatus.NotAdvance);
        if (status.HasFlag(FlightTaskStatus.NotDelay)) result.Add(TaskStatus.NotDelay);
        if (status.HasFlag(FlightTaskStatus.NotCancel)) result.Add(TaskStatus.NotCancel);
        if (status.HasFlag(FlightTaskStatus.NotCancelPreferred)) result.Add(TaskStatus.NotCancelPreferred);
        if (status.HasFlag(FlightTaskStatus.NotAircraftChange)) result.Add(TaskStatus.NotExecutorChange);
        return result;
    }
}

/// <summary>
/// 具有状态、飞机、机场和连接时间逻辑的航班任务计划的抽象基类。
/// Abstract base for flight task plans with status, aircraft, airports, and connection time logic.
/// </summary>
public abstract class FlightTaskPlan : IAbstractTaskPlan<Aircraft> {
    /// <summary>非航班静态连接时间 / Non-flight static connection time.</summary>
    public static readonly TimeSpan NotFlightStaticConnectionTime = TimeSpan.FromMinutes(5);

    private readonly FlightTaskStatus _flightTaskStatus;

    /// <summary>
    /// 构造函数 / Constructor.
    /// </summary>
    /// <param name="id">任务ID / Task ID.</param>
    /// <param name="name">任务名称 / Task name.</param>
    /// <param name="flightTaskStatus">任务状态 / Task status.</param>
    protected FlightTaskPlan(string id, string name, FlightTaskStatus flightTaskStatus) {
        Id = id;
        Name = name;
        _flightTaskStatus = flightTaskStatus;
    }

    /// <inheritdoc/>
    public string Id { get; }

    /// <inheritdoc/>
    public virtual string ActualId => Id;

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public virtual string DisplayName => Name;

    /// <inheritdoc/>
    public ISet<TaskStatus> Status => FlightTaskStatusMapping.ToTaskStatus(_flightTaskStatus);

    /// <inheritdoc/>
    public abstract Aircraft? Executor { get; }

    /// <inheritdoc/>
    public abstract ISet<Aircraft> EnabledExecutors { get; }

    /// <inheritdoc/>
    public virtual TimeRange? ScheduledTime => null;

    /// <inheritdoc/>
    public virtual TimeRange? Time => ScheduledTime;

    /// <summary>出发机场 / Departure airport.</summary>
    public abstract Airport Dep { get; }

    /// <summary>到达机场 / Arrival airport.</summary>
    public abstract Airport Arr { get; }

    /// <summary>备用出发机场列表 / Backup departure airports.</summary>
    public virtual IReadOnlyList<Airport> DepBackup => Array.Empty<Airport>();

    /// <summary>备用到达机场列表 / Backup arrival airports.</summary>
    public virtual IReadOnlyList<Airport> ArrBackup => Array.Empty<Airport>();

    /// <summary>
    /// 返回给定出发机场的实际到达机场。
    /// Returns the actual arrival airport for the given departure airport.
    /// </summary>
    public virtual Airport? ActualArr(Airport dep) => Arr;

    /// <summary>
    /// 计算连接时间。
    /// Computes the connection time.
    /// </summary>
    public virtual TimeSpan ConnectionTime(FlightTask? succTask) {
        if (Executor is null) return TimeSpan.Zero;
        return ConnectionTime(Executor, succTask);
    }

    /// <summary>
    /// 计算给定飞机的连接时间。
    /// Computes the connection time for the given aircraft.
    /// </summary>
    public virtual TimeSpan ConnectionTime(Aircraft aircraft, FlightTask? succTask) {
        if (succTask is not null) {
            if (succTask.IsFlight) {
                return aircraft.ConnectionTime.TryGetValue(Arr, out var ct) ? ct : aircraft.MaxConnectionTime;
            }
            return NotFlightStaticConnectionTime;
        }
        return TimeSpan.Zero;
    }

    /// <summary>是否允许飞机变更 / Whether aircraft change is enabled.</summary>
    public bool AircraftChangeEnabled => !_flightTaskStatus.HasFlag(FlightTaskStatus.NotAircraftChange);

    /// <summary>是否允许飞机类型变更 / Whether aircraft type change is enabled.</summary>
    public bool AircraftTypeChangeEnabled => !_flightTaskStatus.HasFlag(FlightTaskStatus.NotAircraftTypeChange);

    /// <summary>是否允许飞机子类型变更 / Whether aircraft minor type change is enabled.</summary>
    public bool AircraftMinorTypeChangeEnabled => !_flightTaskStatus.HasFlag(FlightTaskStatus.NotAircraftMinorTypeChange);

    /// <summary>是否允许终端变更 / Whether terminal change is enabled.</summary>
    public bool TerminalChangeEnabled => !_flightTaskStatus.HasFlag(FlightTaskStatus.NotTerminalChange);

    /// <summary>权重 / Weight.</summary>
    public virtual Flt64 Weight => Flt64.One;

    /// <summary>是否忽略强限制 / Whether strong limit is ignored.</summary>
    public bool StrongLimitIgnored => _flightTaskStatus.HasFlag(FlightTaskStatus.StrongLimitIgnored);

    /// <summary>是否允许取消 / Whether cancel is enabled.</summary>
    public bool CancelEnabled => !_flightTaskStatus.HasFlag(FlightTaskStatus.NotCancel);

    /// <summary>是否不优先取消 / Whether cancel is not preferred.</summary>
    public bool NotCancelPreferred => _flightTaskStatus.HasFlag(FlightTaskStatus.NotCancelPreferred);

    /// <summary>是否允许延迟 / Whether delay is enabled.</summary>
    public bool DelayEnabled => !_flightTaskStatus.HasFlag(FlightTaskStatus.NotDelay);

    /// <summary>是否允许提前 / Whether advance is enabled.</summary>
    public bool AdvanceEnabled => !_flightTaskStatus.HasFlag(FlightTaskStatus.NotAdvance);

    /// <inheritdoc/>
    public virtual TimeSpan? Duration => Time?.Duration;

    /// <summary>
    /// 给定飞机的持续时间。
    /// Duration for the given aircraft.
    /// </summary>
    public abstract TimeSpan DurationFor(Aircraft aircraft);

    /// <summary>时间窗口 / Time window.</summary>
    public virtual TimeRange? TimeWindow {
        get {
            var dur = Duration ?? TimeSpan.Zero;
            if (LastEndTime is not null && EarliestEndTime is not null)
                return new TimeRange(EarliestEndTime.Value - dur, LastEndTime.Value);
            if (LastEndTime is not null) return new TimeRange(LastEndTime.Value);
            if (EarliestEndTime is not null) return new TimeRange(EarliestEndTime.Value - dur);
            return null;
        }
    }

    /// <inheritdoc/>
    public virtual DateTimeOffset? EarliestEndTime => null;

    /// <inheritdoc/>
    public virtual DateTimeOffset? LastEndTime => null;
}

/// <summary>
/// 具有计划、恢复、延迟/提前跟踪和变更检测的航班任务的抽象基类。
/// Abstract base for flight tasks with plan, recovery, delay/advance tracking, and change detection.
/// </summary>
public abstract class FlightTask : IAbstractTask<Aircraft, FlightTaskAssignment> {
    private readonly FlightTask? _origin;

    /// <summary>
    /// 构造函数 / Constructor.
    /// </summary>
    /// <param name="type">任务类型 / Task type.</param>
    /// <param name="origin">原始任务（恢复前）/ Origin task (before recovery).</param>
    protected FlightTask(FlightTaskType type, FlightTask? origin = null) {
        Type = type;
        _origin = origin;
        SetIndexed();
    }

    /// <summary>任务类型 / Task type.</summary>
    public FlightTaskType Type { get; }

    /// <summary>是否为航班 / Whether this is a flight.</summary>
    public bool IsFlight => Type.IsFlightType;

    /// <summary>任务计划 / Task plan.</summary>
    public abstract FlightTaskPlan Plan { get; }

    /// <inheritdoc/>
    public int Index { get; private set; }

    /// <summary>设置索引 / Set index.</summary>
    public void SetIndexed() => Index = ManualIndexed.Impl.NextIndex(GetType());

    /// <inheritdoc/>
    public string Id => Plan.Id;

    /// <inheritdoc/>
    public string ActualId => Plan.ActualId;

    /// <inheritdoc/>
    public string Name => Plan.Name;

    /// <inheritdoc/>
    public string DisplayName => Plan.DisplayName;

    /// <inheritdoc/>
    public TaskType TaskType => new(Type.Cls);

    /// <inheritdoc/>
    public TaskKey Key => new(Id, TaskType);

    /// <summary>飞机 / Aircraft.</summary>
    public virtual Aircraft? Aircraft => Plan.Executor;

    /// <summary>容量 / Capacity.</summary>
    public virtual AircraftCapacity? Capacity => IsFlight ? Aircraft?.Capacity : null;

    /// <summary>出发机场 / Departure airport.</summary>
    public virtual Airport Dep => Plan.Dep;

    /// <summary>到达机场 / Arrival airport.</summary>
    public virtual Airport Arr => Plan.Arr;

    /// <summary>备用出发机场 / Backup departure airports.</summary>
    public virtual IReadOnlyList<Airport> DepBackup => Plan.DepBackup;

    /// <summary>备用到达机场 / Backup arrival airports.</summary>
    public virtual IReadOnlyList<Airport> ArrBackup => Plan.ArrBackup;

    /// <summary>
    /// 返回给定出发机场的实际到达机场。
    /// Returns the actual arrival airport for the given departure.
    /// </summary>
    public virtual Airport? ActualArr(Airport dep) => Plan.ActualArr(dep);

    /// <inheritdoc/>
    public TimeRange? ScheduledTime => Plan.ScheduledTime;

    /// <inheritdoc/>
    public virtual TimeRange? Time => Plan.Time;

    /// <inheritdoc/>
    public TimeSpan? Duration => Plan.Duration;

    /// <summary>
    /// 给定飞机的持续时间。
    /// Duration for the given aircraft.
    /// </summary>
    public virtual TimeSpan DurationFor(Aircraft aircraft) => Plan.DurationFor(aircraft);

    /// <summary>飞行小时 / Flight hour.</summary>
    public virtual FlightHour? FlightHour => IsFlight ? new FlightHour(Duration ?? TimeSpan.Zero) : null;

    /// <summary>
    /// 给定飞机的飞行小时。
    /// Flight hour for the given aircraft.
    /// </summary>
    public FlightHour FlightHourFor(Aircraft aircraft)
        => new(IsFlight ? DurationFor(aircraft) : TimeSpan.Zero);

    /// <summary>飞行循环 / Flight cycle.</summary>
    public virtual FlightCycle FlightCycle => new(IsFlight ? 1UL : 0UL);

    /// <summary>
    /// 计算连接时间。
    /// Computes the connection time.
    /// </summary>
    public virtual TimeSpan? ConnectionTime(FlightTask? succTask)
        => Plan.ConnectionTime(succTask);

    /// <summary>
    /// 计算给定飞机的连接时间。
    /// Computes the connection time for the given aircraft.
    /// </summary>
    public virtual TimeSpan ConnectionTime(Aircraft aircraft, FlightTask? succTask)
        => Plan.ConnectionTime(aircraft, succTask);

    /// <summary>
    /// 返回给定飞机的最晚正常开始时间。
    /// Returns the latest normal start time for the given aircraft.
    /// </summary>
    public DateTimeOffset LatestNormalStartTime(Aircraft aircraft) {
        if (ScheduledTime is not null) return ScheduledTime.Start;
        var tw = TimeWindow;
        if (tw is not null) return tw.End - DurationFor(aircraft);
        throw new InvalidOperationException("Cannot determine latest normal start time.");
    }

    /// <summary>最大延迟 / Maximum delay.</summary>
    public TimeSpan? MaxDelay => !DelayEnabled ? TimeSpan.Zero : null;

    /// <inheritdoc/>
    public bool CancelEnabled => Plan.CancelEnabled;

    /// <inheritdoc/>
    public bool NotCancelPreferred => Plan.NotCancelPreferred;

    /// <summary>是否允许飞机变更 / Whether aircraft change is enabled.</summary>
    public virtual bool AircraftChangeEnabled => Plan.AircraftChangeEnabled;

    /// <summary>是否允许飞机类型变更 / Whether aircraft type change is enabled.</summary>
    public virtual bool AircraftTypeChangeEnabled => Plan.AircraftTypeChangeEnabled;

    /// <summary>是否允许飞机子类型变更 / Whether aircraft minor type change is enabled.</summary>
    public virtual bool AircraftMinorTypeChangeEnabled => Plan.AircraftMinorTypeChangeEnabled;

    /// <inheritdoc/>
    public bool DelayEnabled => Plan.Status.Contains(TaskStatus.NotDelay) == false;

    /// <inheritdoc/>
    public bool AdvanceEnabled => Plan.Status.Contains(TaskStatus.NotAdvance) == false;

    /// <summary>是否允许航线变更 / Whether route change is enabled.</summary>
    public virtual bool RouteChangeEnabled => Plan.TerminalChangeEnabled;

    /// <summary>权重 / Weight.</summary>
    public virtual Flt64 Weight => Plan.Weight;

    /// <summary>是否忽略强限制 / Whether strong limit is ignored.</summary>
    public bool StrongLimitIgnored => Plan.StrongLimitIgnored;

    /// <summary>原始任务（恢复前）/ Origin task (before recovery).</summary>
    public FlightTask OriginTask => _origin ?? this;

    /// <inheritdoc/>
    public virtual bool ExecutorChanged => AircraftChangeEnabled && Aircraft is not null;

    /// <summary>是否已恢复 / Whether recovered.</summary>
    public abstract bool Recovered { get; }

    /// <summary>恢复策略 / Recovery policy.</summary>
    public abstract FlightTaskAssignment RecoveryPolicy { get; }

    /// <summary>是否允许恢复 / Whether recovery is enabled.</summary>
    public virtual bool RecoveryEnabled(TimeRange timeWindow) {
        var planTime = Plan.Time;
        if (planTime is not null) return timeWindow.Contains(planTime.Start);
        var tw = TimeWindow;
        return tw is null || timeWindow.WithIntersection(tw);
    }

    /// <summary>是否需要恢复 / Whether recovery is needed.</summary>
    public virtual bool RecoveryNeeded(TimeRange timeWindow) {
        if (!RecoveryEnabled(timeWindow)) return false;
        var t = Time;
        return t is null || timeWindow.WithIntersection(t);
    }

    /// <summary>
    /// 检查给定恢复策略是否允许。
    /// Checks whether the given recovery policy is allowed.
    /// </summary>
    public virtual bool RecoveryEnabled(FlightTaskAssignment policy) => true;

    /// <summary>
    /// 应用恢复策略，返回新的恢复任务。
    /// Applies the recovery policy, returning a new recovered task.
    /// </summary>
    public abstract FlightTask Recovery(FlightTaskAssignment policy);

    /// <inheritdoc/>
    public TimeSpan Advance {
        get {
            var target = Plan.Time ?? TimeWindow;
            if (target is not null && Time is not null) {
                var adv = target.Start - Time!.Start;
                return adv > TimeSpan.Zero ? adv : TimeSpan.Zero;
            }
            return TimeSpan.Zero;
        }
    }

    /// <inheritdoc/>
    public TimeSpan Delay {
        get {
            var target = Plan.Time ?? TimeWindow;
            if (target is not null && Time is not null) {
                var del = Time!.Start - target.Start;
                return del > TimeSpan.Zero ? del : TimeSpan.Zero;
            }
            return TimeSpan.Zero;
        }
    }

    /// <inheritdoc/>
    public TimeSpan OverMaxDelay => MaxDelay is null || Delay <= MaxDelay.Value
        ? TimeSpan.Zero
        : Delay - MaxDelay.Value;

    /// <summary>飞机是否已变更 / Whether aircraft has changed.</summary>
    public virtual bool AircraftChanged => AircraftChangeEnabled && RecoveryPolicy.Aircraft is not null;

    /// <summary>飞机类型是否已变更 / Whether aircraft type has changed.</summary>
    public virtual bool AircraftTypeChanged => AircraftTypeChange is not null;

    /// <summary>飞机子类型是否已变更 / Whether aircraft minor type has changed.</summary>
    public virtual bool AircraftMinorTypeChanged => AircraftMinorTypeChange is not null;

    /// <summary>航线是否已变更 / Whether route has changed.</summary>
    public virtual bool RouteChanged => RouteChangeEnabled && RecoveryPolicy.Route is not null;

    /// <summary>飞机变更详情 / Aircraft change details.</summary>
    public virtual AircraftChange? AircraftChange {
        get {
            if (!AircraftChangeEnabled) return null;
            var policy = RecoveryPolicy;
            if (Plan.Executor is not null && policy.Aircraft is not null && policy.Aircraft != Plan.Executor) {
                return new AircraftChange(Plan.Executor, policy.Aircraft);
            }
            return null;
        }
    }

    /// <summary>飞机类型变更详情 / Aircraft type change details.</summary>
    public virtual AircraftTypeChange? AircraftTypeChange {
        get {
            if (!AircraftTypeChangeEnabled) return null;
            var policy = RecoveryPolicy;
            if (Plan.Executor is not null && policy.Aircraft is not null && policy.Aircraft.Type != Plan.Executor.Type) {
                return new AircraftTypeChange(Plan.Executor.Type, policy.Aircraft.Type);
            }
            return null;
        }
    }

    /// <summary>飞机子类型变更详情 / Aircraft minor type change details.</summary>
    public virtual AircraftMinorTypeChange? AircraftMinorTypeChange {
        get {
            if (!AircraftMinorTypeChangeEnabled) return null;
            var policy = RecoveryPolicy;
            if (Plan.Executor is not null && policy.Aircraft is not null && policy.Aircraft.MinorType != Plan.Executor.MinorType) {
                return new AircraftMinorTypeChange(Plan.Executor.MinorType, policy.Aircraft.MinorType);
            }
            return null;
        }
    }

    /// <summary>航线变更详情 / Route change details.</summary>
    public virtual RouteChange? RouteChange {
        get {
            if (!RouteChangeEnabled) return null;
            var policy = RecoveryPolicy;
            if (policy.Route is not null && (policy.Route.Dep != Dep || policy.Route.Arr != Arr)) {
                return new RouteChange(new Route(Dep, Arr), policy.Route);
            }
            return null;
        }
    }

    /// <summary>
    /// 检查任务是否在给定时间窗口内到达机场。
    /// Checks whether the task arrives at the airport within the time window.
    /// </summary>
    public bool ArrivedWhen(Airport airport, TimeRange timeWindow)
        => IsFlight && Time is not null && Arr == airport && timeWindow.Contains(Time);

    /// <summary>
    /// 检查任务是否在给定时间窗口内从机场出发。
    /// Checks whether the task departs from the airport within the time window.
    /// </summary>
    public bool DepartedWhen(Airport airport, TimeRange timeWindow)
        => IsFlight && Time is not null && Dep == airport && timeWindow.Contains(timeWindow.Start);

    /// <summary>
    /// 检查任务是否在前一任务之后位于机场。
    /// Checks whether the task is located at the airport after the previous task.
    /// </summary>
    public bool LocatedWhen(FlightTask prevTask, Airport airport, TimeRange timeWindow) {
        var prevTime = prevTask.Time;
        var myTime = Time;
        if (prevTime is null || myTime is null) return false;
        if (prevTask.Arr != airport) return false;
        var start = prevTime.End;
        var end = IsFlight ? myTime.Start : myTime.End;
        return timeWindow.WithIntersection(new TimeRange(start, end));
    }

    /// <inheritdoc/>
    public bool? PartialEq(IAbstractTask<Aircraft, FlightTaskAssignment> rhs) {
        if (rhs is FlightTask other) {
            return Plan == other.Plan && RecoveryPolicy == other.RecoveryPolicy;
        }
        return false;
    }

    /// <inheritdoc/>
    public override int GetHashCode() => Key.GetHashCode();

    /// <inheritdoc/>
    public override bool Equals(object? obj)
        => obj is FlightTask other && Key == other.Key;

    /// <inheritdoc/>
    public override string ToString() => Name;

    /// <summary>
    /// 获取时间窗口。
    /// Gets the time window.
    /// </summary>
    public TimeRange? TimeWindow => Plan.TimeWindow;
}
