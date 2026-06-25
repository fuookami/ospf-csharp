#nullable enable

using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Utils.Functional;
using System;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
/// <summary>
/// 分配策略接口，描述任务的执行者和时间分配 / Assignment policy interface describing executor and time assignment for a task.
/// </summary>
/// <typeparam name="E">执行者类型 / The executor type.</typeparam>
public interface IAssignmentPolicy<out E> where E : Executor {
    /// <summary>分配的执行者 / The assigned executor.</summary>
    E? Executor { get; }

    /// <summary>分配的时间范围 / The assigned time range.</summary>
    TimeRange? Time { get; }

    /// <summary>是否完整分配（同时有执行者和时间）/ Whether the assignment is full (has both executor and time).</summary>
    bool Full => Executor is not null && Time is not null;

    /// <summary>是否为空分配（无执行者和时间）/ Whether the assignment is empty (no executor and time).</summary>
    bool Empty => Executor is null && Time is null;
}

/// <summary>
/// 分配策略记录 / Assignment policy record.
/// </summary>
/// <typeparam name="E">执行者类型 / The executor type.</typeparam>
/// <param name="Executor">分配的执行者 / The assigned executor.</param>
/// <param name="Time">分配的时间范围 / The assigned time range.</param>
public record AssignmentPolicy<E>(
    E? Executor = null,
    TimeRange? Time = null
) : IAssignmentPolicy<E>, IEq<AssignmentPolicy<E>>
    where E : Executor {
    /// <summary>是否完整分配 / Whether the assignment is full.</summary>
    public bool Full => Executor is not null && Time is not null;

    /// <summary>是否为空分配 / Whether the assignment is empty.</summary>
    public bool Empty => Executor is null && Time is null;

    /// <inheritdoc/>
    public bool? PartialEq(AssignmentPolicy<E> rhs) {
        if (ReferenceEquals(this, rhs)) {
            return true;
        }

        if (Executor?.Id != rhs.Executor?.Id) {
            return false;
        }

        if (Time != rhs.Time) {
            return false;
        }

        return true;
    }

    /// <inheritdoc/>
    public bool Eq(AssignmentPolicy<E> rhs) => PartialEq(rhs) ?? false;

    /// <inheritdoc/>
    public bool Neq(AssignmentPolicy<E> rhs) => !Eq(rhs);
}

/// <summary>
/// 执行者变更记录 / Executor change record.
/// </summary>
/// <typeparam name="E">执行者类型 / The executor type.</typeparam>
/// <param name="From">原执行者 / The original executor.</param>
/// <param name="To">新执行者 / The new executor.</param>
public sealed record ExecutorChange<E>(E From, E To) where E : Executor;
