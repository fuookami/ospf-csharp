#nullable enable

using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Error;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Utils.Concept;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
/// <summary>
/// 任务类型，基于 Type / Task type based on Type.
/// </summary>
public sealed record TaskType(Type Cls) {
    /// <summary>类型名称 / The type name.</summary>
    public string TypeName => Cls.FullName ?? Cls.Name;
}

/// <summary>
/// 任务键，由ID和类型组成 / Task key composed of ID and type.
/// </summary>
public sealed record TaskKey(string Id, TaskType Type);

/// <summary>
/// 抽象任务接口，定义任务的基本属性和行为 / Abstract task interface defining basic task properties and behaviors.
/// </summary>
/// <typeparam name="E">执行者类型 / The executor type.</typeparam>
/// <typeparam name="A">分配策略类型 / The assignment policy type.</typeparam>
public interface IAbstractTask<E, A> : IIndexed
    where E : Executor
    where A : IAssignmentPolicy<E> {
    /// <summary>任务类型 / The task type.</summary>
    TaskType TaskType => new(typeof(IAbstractTask<E, A>));

    /// <summary>任务键 / The task key.</summary>
    TaskKey Key => new(Id, TaskType);

    /// <summary>分配策略 / The assignment policy.</summary>
    A? AssignmentPolicy => default;

    /// <summary>任务ID / The task ID.</summary>
    string Id { get; }

    /// <summary>实际ID / The actual ID.</summary>
    string ActualId => Id;

    /// <summary>任务名称 / The task name.</summary>
    string Name { get; }

    /// <summary>显示名称 / The display name.</summary>
    string DisplayName => Name;

    /// <summary>任务状态 / The task status.</summary>
    ISet<TaskStatus> Status => new HashSet<TaskStatus>();

    /// <summary>分配的执行者 / The assigned executor.</summary>
    E? Executor => default;

    /// <summary>启用的执行者集合 / The set of enabled executors.</summary>
    ISet<E> EnabledExecutors => new HashSet<E>();

    /// <summary>计划时间 / The scheduled time.</summary>
    TimeRange? ScheduledTime => null;

    /// <summary>分配时间 / The assigned time.</summary>
    TimeRange? Time => null;

    /// <summary>最早结束时间 / The earliest end time.</summary>
    DateTimeOffset? EarliestEndTime => null;

    /// <summary>最晚结束时间 / The last end time.</summary>
    DateTimeOffset? LastEndTime => null;

    /// <summary>持续时间 / Duration.</summary>
    TimeSpan? Duration => Time?.Duration;

    /// <summary>最小持续时间 / Minimum duration.</summary>
    TimeSpan? MinDuration => null;

    /// <summary>最大持续时间 / Maximum duration.</summary>
    TimeSpan? MaxDuration => null;

    /// <summary>时间窗口 / The time window.</summary>
    TimeRange? TimeWindow => null;

    /// <summary>最早开始时间 / The earliest start time.</summary>
    DateTimeOffset? EarliestStartTime => null;

    /// <summary>最晚开始时间 / The last start time.</summary>
    DateTimeOffset? LastStartTime => null;

    /// <summary>最大延迟 / Maximum delay.</summary>
    TimeSpan? MaxDelay => null;

    /// <summary>最大提前 / Maximum advance.</summary>
    TimeSpan? MaxAdvance => null;

    /// <summary>是否允许取消 / Whether cancel is enabled.</summary>
    bool CancelEnabled => false;

    /// <summary>是否不优先取消 / Whether cancel is not preferred.</summary>
    bool NotCancelPreferred => false;

    /// <summary>是否允许延迟 / Whether delay is enabled.</summary>
    bool DelayEnabled => false;

    /// <summary>是否允许提前 / Whether advance is enabled.</summary>
    bool AdvanceEnabled => false;

    /// <summary>是否允许变更执行者 / Whether executor change is enabled.</summary>
    bool ExecutorChangeEnabled => false;

    /// <summary>是否可并行执行 / Whether parallelable.</summary>
    bool Parallelable => false;

    /// <summary>是否可分割 / Whether divisible.</summary>
    bool Divisible => false;

    /// <summary>执行者是否已变更 / Whether executor has changed.</summary>
    bool ExecutorChanged => false;

    /// <summary>提前时间 / Advance duration.</summary>
    TimeSpan Advance {
        get {
            if (ScheduledTime is not null && Time is not null) {
                TimeSpan adv = ScheduledTime.Start - Time.Start;
                return adv > TimeSpan.Zero ? adv : TimeSpan.Zero;
            }
            if (TimeWindow is not null && Time is not null) {
                TimeSpan adv = TimeWindow.Start - Time.Start;
                return adv > TimeSpan.Zero ? adv : TimeSpan.Zero;
            }
            return TimeSpan.Zero;
        }
    }

    /// <summary>延迟时间 / Delay duration.</summary>
    TimeSpan Delay {
        get {
            if (ScheduledTime is not null && Time is not null) {
                TimeSpan del = Time.Start - ScheduledTime.Start;
                return del > TimeSpan.Zero ? del : TimeSpan.Zero;
            }
            if (TimeWindow is not null && Time is not null) {
                TimeSpan del = Time.Start - TimeWindow.End;
                return del > TimeSpan.Zero ? del : TimeSpan.Zero;
            }
            return TimeSpan.Zero;
        }
    }

    /// <summary>超过最大延迟的时间 / Time over max delay.</summary>
    TimeSpan OverMaxDelay {
        get {
            if (MaxDelay is null || Delay <= MaxDelay) {
                return TimeSpan.Zero;
            }

            return Delay - MaxDelay.Value;
        }
    }

    /// <summary>超过最大提前的时间 / Time over max advance.</summary>
    TimeSpan OverMaxAdvance {
        get {
            if (MaxAdvance is null || Advance <= MaxAdvance) {
                return TimeSpan.Zero;
            }

            return Advance - MaxAdvance.Value;
        }
    }

    /// <summary>是否准时 / Whether on time.</summary>
    bool OnTime {
        get {
            if (Time is not null) {
                if (LastEndTime is not null && Time.End > LastEndTime) {
                    return false;
                }

                if (EarliestEndTime is not null && Time.End < EarliestEndTime) {
                    return false;
                }
            }
            return true;
        }
    }

    /// <summary>部分相等比较 / Partial equality comparison.</summary>
    bool? PartialEq(IAbstractTask<E, A> rhs);

    /// <summary>相等比较 / Equality comparison.</summary>
    bool Eq(IAbstractTask<E, A> rhs) => PartialEq(rhs) ?? false;

    /// <summary>不等比较 / Inequality comparison.</summary>
    bool Neq(IAbstractTask<E, A> rhs) => !Eq(rhs);
}

/// <summary>
/// 抽象未计划任务 / Abstract unplanned task.
/// </summary>
public class AbstractUnplannedTask<E, A> : IIterativeAbstractTask<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    public AbstractUnplannedTask(string id, string name, A assignmentPolicy) {
        Id = id;
        Name = name;
        AssignmentPolicy = assignmentPolicy;
    }

    /// <inheritdoc/>
    public string Id { get; }

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public A AssignmentPolicy { get; }
    /// <inheritdoc/>
    public long Iteration { get; set; }

    /// <inheritdoc/>
    public E? Executor => AssignmentPolicy.Executor;

    /// <inheritdoc/>
    public TimeRange? Time => AssignmentPolicy.Time;

    /// <inheritdoc/>
    public bool CancelEnabled => true;

    /// <inheritdoc/>
    public int Index { get; private set; }

    /// <inheritdoc/>
    public void SetIndexed() => Index = ManualIndexed.Impl.NextIndex(GetType());

    /// <inheritdoc/>
    public bool? PartialEq(IAbstractTask<E, A> rhs) {
        if (ReferenceEquals(this, rhs)) {
            return true;
        }

        if (rhs is not AbstractUnplannedTask<E, A> other) {
            return false;
        }

        return Equals(AssignmentPolicy, other.AssignmentPolicy);
    }

    /// <inheritdoc/>
    public override string ToString() => string.IsNullOrEmpty(Name) ? Id : Name;
}

/// <summary>
/// 抽象已计划任务 / Abstract planned task.
/// </summary>
public class AbstractPlannedTask<P, E, A> : IAbstractTask<E, A>
    where P : IAbstractTaskPlan<E>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    public AbstractPlannedTask(P plan, A? assignmentPolicy) {
        Plan = plan;
        AssignmentPolicy = assignmentPolicy;
    }

    /// <summary>任务计划 / The task plan.</summary>
    public P Plan { get; }

    /// <inheritdoc/>
    public A? AssignmentPolicy { get; }

    /// <inheritdoc/>
    public string Id => Plan.Id;

    /// <inheritdoc/>
    public string Name => Plan.Name;

    /// <inheritdoc/>
    public ISet<TaskStatus> Status => Plan.Status;

    /// <inheritdoc/>
    public E? Executor => AssignmentPolicy?.Executor ?? Plan.Executor;

    /// <inheritdoc/>
    public ISet<E> EnabledExecutors => Plan.EnabledExecutors;

    /// <inheritdoc/>
    public TimeRange? ScheduledTime => Plan.ScheduledTime;

    /// <inheritdoc/>
    public TimeRange? Time => AssignmentPolicy?.Time ?? Plan.Time;

    /// <inheritdoc/>
    public DateTimeOffset? EarliestEndTime => Plan.EarliestEndTime;

    /// <inheritdoc/>
    public DateTimeOffset? LastEndTime => Plan.LastEndTime;

    /// <inheritdoc/>
    public TimeSpan? Duration => AssignmentPolicy?.Time?.Duration ?? Plan.Duration;

    /// <inheritdoc/>
    public TimeSpan? MinDuration => Plan.MinDuration;

    /// <inheritdoc/>
    public TimeSpan? MaxDuration => Plan.MaxDuration;

    /// <inheritdoc/>
    public TimeRange? TimeWindow => Plan.TimeWindow;

    /// <inheritdoc/>
    public DateTimeOffset? EarliestStartTime => Plan.EarliestStartTime;

    /// <inheritdoc/>
    public DateTimeOffset? LastStartTime => Plan.LastStartTime;

    /// <inheritdoc/>
    public bool CancelEnabled => Plan.CancelEnabled;

    /// <inheritdoc/>
    public bool NotCancelPreferred => Plan.NotCancelPreferred;

    /// <inheritdoc/>
    public bool DelayEnabled => Plan.DelayEnabled;

    /// <inheritdoc/>
    public bool AdvanceEnabled => Plan.AdvanceEnabled;

    /// <inheritdoc/>
    public bool ExecutorChangeEnabled => Plan.ExecutorChangeEnabled;

    /// <inheritdoc/>
    public bool Parallelable => Plan.Parallelable;

    /// <inheritdoc/>
    public bool Divisible => Plan.Divisible;

    /// <inheritdoc/>
    public bool ExecutorChanged => ExecutorChangeEnabled && AssignmentPolicy?.Executor is not null;

    /// <inheritdoc/>
    public TimeSpan? MaxDelay => !DelayEnabled ? TimeSpan.Zero : null;

    /// <inheritdoc/>
    public TimeSpan? MaxAdvance => !AdvanceEnabled ? TimeSpan.Zero : null;

    /// <inheritdoc/>
    public int Index { get; private set; }

    /// <summary>设置索引 / Set index.</summary>
    public void SetIndexed() => Index = ManualIndexed.Impl.NextIndex(GetType());

    /// <inheritdoc/>
    public bool? PartialEq(IAbstractTask<E, A> rhs) {
        if (ReferenceEquals(this, rhs)) {
            return true;
        }

        if (rhs is not AbstractPlannedTask<P, E, A> other) {
            return false;
        }

        if (!Equals(Plan, other.Plan)) {
            return false;
        }

        if (AssignmentPolicy is null && other.AssignmentPolicy is null) {
            return true;
        }

        if (AssignmentPolicy is null || other.AssignmentPolicy is null) {
            return false;
        }

        return Equals(AssignmentPolicy, other.AssignmentPolicy);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is AbstractPlannedTask<P, E, A> other && ((IAbstractTask<E, A>)this).Key == ((IAbstractTask<E, A>)other).Key;

    /// <inheritdoc/>
    public override int GetHashCode() => ((IAbstractTask<E, A>)this).Key.GetHashCode();

    /// <inheritdoc/>
    public override string ToString() => string.IsNullOrEmpty(Name) ? Id : Name;
}

/// <summary>任务类型别名 / Task type alias.</summary>
public class PlannedTask<P, E> : AbstractPlannedTask<P, E, IAssignmentPolicy<E>>
    where P : IAbstractTaskPlan<E>
    where E : Executor {
    public PlannedTask(P plan, IAssignmentPolicy<E>? assignmentPolicy = null)
        : base(plan, assignmentPolicy) { }
}

/// <summary>
/// 迭代抽象任务接口 / Iterative abstract task interface.
/// </summary>
public interface IIterativeAbstractTask<E, A> : IAbstractTask<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    /// <summary>迭代次数 / The iteration number.</summary>
    long Iteration { get; }
}
