#nullable enable

using Fuookami.Ospf.Utils.Concept;
using System;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
/// <summary>
/// 执行者，表示可执行任务的实体 / Executor representing an entity that can execute tasks.
/// </summary>
public class Executor : ManualIndexed {
    /// <summary>
    /// 构造函数 / Constructor.
    /// </summary>
    /// <param name="id">执行者ID / The executor ID.</param>
    /// <param name="name">执行者名称 / The executor name.</param>
    public Executor(string id, string name) {
        Id = id;
        Name = name;
    }

    /// <summary>执行者ID / The executor ID.</summary>
    public string Id { get; }

    /// <summary>执行者名称 / The executor name.</summary>
    public string Name { get; }

    /// <summary>实际ID / The actual ID.</summary>
    public virtual string ActualId => Id;

    /// <summary>显示名称 / The display name.</summary>
    public virtual string DisplayName => Name;

    /// <inheritdoc/>
    public override string ToString() => DisplayName;
}

/// <summary>
/// 执行者初始可用性，记录上一个任务和可用时间 / Executor initial usability recording the last task and enabled time.
/// </summary>
/// <typeparam name="T">任务类型 / The task type.</typeparam>
/// <typeparam name="E">执行者类型 / The executor type.</typeparam>
/// <typeparam name="A">分配策略类型 / The assignment policy type.</typeparam>
/// <param name="LastTask">上一个任务 / The last task.</param>
/// <param name="EnabledTime">可用时间 / The enabled time.</param>
public record ExecutorInitialUsability<T, E, A>(
    T? LastTask,
    DateTimeOffset EnabledTime
)
    where E : Executor
    where A : IAssignmentPolicy<E> {
    /// <summary>是否处于可用状态 / Whether the executor is on.</summary>
    public bool On => LastTask is not null;
}
