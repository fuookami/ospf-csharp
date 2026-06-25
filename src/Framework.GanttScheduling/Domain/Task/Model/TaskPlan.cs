#nullable enable

using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
/// <summary>
/// 任务状态枚举 / Task status enumeration.
/// </summary>
public enum TaskStatus {
    NotAdvance,
    NotDelay,
    NotCancel,
    NotCancelPreferred,
    NotExecutorChange,
    Parallelable,
    Divisible
}

/// <summary>
/// 抽象任务计划接口 / Abstract task plan interface.
/// </summary>
public interface IAbstractTaskPlan<E> where E : Executor {
    string Id { get; }
    string ActualId => Id;
    string Name { get; }
    string DisplayName => Name;
    ISet<TaskStatus> Status { get; }
    E? Executor => null;
    ISet<E> EnabledExecutors { get; }
    TimeRange? ScheduledTime => null;
    TimeRange? Time => ScheduledTime;
    DateTimeOffset? EarliestEndTime => null;
    DateTimeOffset? LastEndTime => null;
    TimeSpan? Duration => Time?.Duration ?? ScheduledTime?.Duration;
    TimeSpan MinDuration => Duration ?? TimeSpan.Zero;
    TimeSpan MaxDuration => Duration ?? TimeSpan.Zero;

    TimeRange? TimeWindow {
        get {
            if (LastEndTime is not null && EarliestEndTime is not null) {
                return new TimeRange(EarliestEndTime.Value - MaxDuration, LastEndTime.Value);
            }

            if (LastEndTime is not null) {
                return new TimeRange(LastEndTime.Value);
            }

            if (EarliestEndTime is not null) {
                return new TimeRange(EarliestEndTime.Value - MaxDuration);
            }

            return null;
        }
    }

    DateTimeOffset? EarliestStartTime {
        get {
            if (Time is not null && Status.Contains(TaskStatus.NotAdvance)) {
                return Time.Start;
            }

            if (TimeWindow is not null) {
                return TimeWindow.Start;
            }

            if (EarliestEndTime is not null) {
                return EarliestEndTime.Value - MaxDuration;
            }

            return null;
        }
    }

    DateTimeOffset? LastStartTime {
        get {
            if (Time is not null && Status.Contains(TaskStatus.NotDelay)) {
                return Time.Start;
            }

            if (TimeWindow is not null && Duration is not null) {
                return TimeWindow.End - Duration.Value;
            }

            if (LastEndTime is not null) {
                return LastEndTime.Value - MinDuration;
            }

            return null;
        }
    }

    bool CancelEnabled => !Status.Contains(TaskStatus.NotCancel);
    bool NotCancelPreferred => Status.Contains(TaskStatus.NotCancelPreferred);
    bool AdvanceEnabled => !Status.Contains(TaskStatus.NotAdvance);
    bool DelayEnabled => !Status.Contains(TaskStatus.NotDelay);
    bool ExecutorChangeEnabled => !Status.Contains(TaskStatus.NotExecutorChange);
    bool Parallelable => Status.Contains(TaskStatus.Parallelable);
    bool Divisible => Status.Contains(TaskStatus.Divisible);
}

/// <summary>
/// 单步任务计划 / Single step task plan.
/// </summary>
public record SingleStepTaskPlan<E>(
    string Id,
    string Name,
    ISet<E> EnabledExecutors,
    ISet<TaskStatus> Status
) : IAbstractTaskPlan<E>
    where E : Executor;

/// <summary>
/// 抽象任务步骤计划接口 / Abstract task step plan interface.
/// </summary>
public interface IAbstractTaskStepPlan<TSelf, T, E> : IAbstractTaskPlan<E>
    where TSelf : IAbstractTaskStepPlan<TSelf, T, E>
    where T : IAbstractMultiStepTask<T, TSelf, E>
    where E : Executor {
    T Parent { get; }
    ITaskStep<T, TSelf, E> Step { get; }
}

/// <summary>
/// 任务步骤计划 / Task step plan.
/// </summary>
public class TaskStepPlan<T, E> : IAbstractTaskStepPlan<TaskStepPlan<T, E>, T, E>
    where T : IAbstractMultiStepTask<T, TaskStepPlan<T, E>, E>
    where E : Executor {
    public TaskStepPlan(T parent, ITaskStep<T, TaskStepPlan<T, E>, E> step, ISet<TaskStatus>? status = null) {
        Parent = parent;
        Step = step;
        Status = status is not null
            ? new HashSet<TaskStatus>(status.Concat(step.Status))
            : new HashSet<TaskStatus>(parent.Status.Concat(step.Status));
    }

    public T Parent { get; }
    public ITaskStep<T, TaskStepPlan<T, E>, E> Step { get; }
    public string Id => $"{Parent.Id}-{Step.Id}";
    public string Name => $"{Parent.Name}-{Step.Name}";
    public ISet<E> EnabledExecutors => Step.EnabledExecutors;
    public ISet<TaskStatus> Status { get; }
}

/// <summary>
/// 抽象多步任务接口 / Abstract multi-step task interface.
/// </summary>
public interface IAbstractMultiStepTask<TSelf, S, E>
    where TSelf : IAbstractMultiStepTask<TSelf, S, E>
    where S : IAbstractTaskStepPlan<S, TSelf, E>
    where E : Executor {
    string Id { get; }
    string Name { get; }
    ITaskStepGraph<TSelf, S, E> StepGraph { get; }
    ISet<TaskStatus> Status { get; }
    IReadOnlyList<S> Steps { get; }
}

/// <summary>
/// 多步任务 / Multi-step task.
/// </summary>
public class MultiStepTask<E> : IAbstractMultiStepTask<MultiStepTask<E>, TaskStepPlan<MultiStepTask<E>, E>, E>
    where E : Executor {
    private readonly Lazy<IReadOnlyList<TaskStepPlan<MultiStepTask<E>, E>>> _steps;

    public MultiStepTask(
        string id, string name,
        ITaskStepGraph<MultiStepTask<E>, TaskStepPlan<MultiStepTask<E>, E>, E> stepGraph,
        ISet<TaskStatus> status) {
        Id = id;
        Name = name;
        StepGraph = stepGraph;
        Status = status;
        _steps = new Lazy<IReadOnlyList<TaskStepPlan<MultiStepTask<E>, E>>>(() =>
            StepGraph.Steps.Select(s => new TaskStepPlan<MultiStepTask<E>, E>(this, s)).ToList());
    }

    public string Id { get; }
    public string Name { get; }
    public ITaskStepGraph<MultiStepTask<E>, TaskStepPlan<MultiStepTask<E>, E>, E> StepGraph { get; }
    public ISet<TaskStatus> Status { get; }
    public IReadOnlyList<TaskStepPlan<MultiStepTask<E>, E>> Steps => _steps.Value;
}
