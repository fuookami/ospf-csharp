#nullable enable

using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Error;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
/// <summary>
/// 步骤关系枚举 / Step relation enumeration.
/// </summary>
public enum StepRelation {
    /// <summary>与关系（所有前置步骤必须完成）/ And relation (all preceding steps must complete).</summary>
    And,
    /// <summary>或关系（任一前置步骤完成即可）/ Or relation (any preceding step completion is sufficient).</summary>
    Or
}

/// <summary>
/// 抽象任务步骤 / Abstract task step.
/// </summary>
/// <typeparam name="T">多步任务类型 / The multi-step task type.</typeparam>
/// <typeparam name="S">步骤计划类型 / The step plan type.</typeparam>
/// <typeparam name="E">执行者类型 / The executor type.</typeparam>
public abstract class TaskStep<T, S, E>
    where T : IAbstractMultiStepTask<T, S, E>
    where S : IAbstractTaskStepPlan<S, T, E>
    where E : Executor {
    /// <summary>
    /// 构造函数 / Constructor.
    /// </summary>
    /// <param name="id">步骤ID / The step ID.</param>
    /// <param name="name">步骤名称 / The step name.</param>
    /// <param name="enabledExecutors">启用的执行者集合 / The set of enabled executors.</param>
    /// <param name="status">任务状态集合 / The set of task statuses.</param>
    protected TaskStep(string id, string name, ISet<E> enabledExecutors, ISet<TaskStatus> status) {
        Id = id;
        Name = name;
        EnabledExecutors = enabledExecutors;
        Status = status;
    }

    /// <summary>步骤ID / The step ID.</summary>
    public string Id { get; }

    /// <summary>步骤名称 / The step name.</summary>
    public string Name { get; }

    /// <summary>显示名称 / The display name.</summary>
    public virtual string? DisplayName => Name;

    /// <summary>启用的执行者集合 / The set of enabled executors.</summary>
    public ISet<E> EnabledExecutors { get; }

    /// <summary>任务状态集合 / The set of task statuses.</summary>
    public ISet<TaskStatus> Status { get; }

    /// <summary>计算步骤持续时间 / Calculate step duration.</summary>
    /// <param name="step">步骤计划 / The step plan.</param>
    /// <param name="executor">执行者 / The executor.</param>
    /// <returns>持续时间 / The duration.</returns>
    public abstract TimeSpan Duration(S step, E executor);

    /// <inheritdoc/>
    public override string ToString() => DisplayName ?? Name;
}

/// <summary>
/// 任务步骤接口 / Task step interface.
/// </summary>
/// <typeparam name="T">多步任务类型 / The multi-step task type.</typeparam>
/// <typeparam name="S">步骤计划类型 / The step plan type.</typeparam>
/// <typeparam name="E">执行者类型 / The executor type.</typeparam>
public interface ITaskStep<T, S, E>
    where T : IAbstractMultiStepTask<T, S, E>
    where S : IAbstractTaskStepPlan<S, T, E>
    where E : Executor {
    /// <summary>步骤ID / The step ID.</summary>
    string Id { get; }

    /// <summary>步骤名称 / The step name.</summary>
    string Name { get; }

    /// <summary>显示名称 / The display name.</summary>
    string? DisplayName => Name;

    /// <summary>启用的执行者集合 / The set of enabled executors.</summary>
    ISet<E> EnabledExecutors { get; }

    /// <summary>任务状态集合 / The set of task statuses.</summary>
    ISet<TaskStatus> Status { get; }

    /// <summary>计算步骤持续时间 / Calculate step duration.</summary>
    TimeSpan Duration(S step, E executor);
}

/// <summary>
/// 任务步骤图接口 / Task step graph interface.
/// </summary>
/// <typeparam name="T">多步任务类型 / The multi-step task type.</typeparam>
/// <typeparam name="S">步骤计划类型 / The step plan type.</typeparam>
/// <typeparam name="E">执行者类型 / The executor type.</typeparam>
public interface ITaskStepGraph<T, S, E>
    where T : IAbstractMultiStepTask<T, S, E>
    where S : IAbstractTaskStepPlan<S, T, E>
    where E : Executor {
    /// <summary>图ID / The graph ID.</summary>
    string Id { get; }

    /// <summary>图名称 / The graph name.</summary>
    string Name { get; }

    /// <summary>步骤列表 / The list of steps.</summary>
    IReadOnlyList<ITaskStep<T, S, E>> Steps { get; }

    /// <summary>起始步骤及关系 / The start steps and their relation.</summary>
    (IReadOnlyList<ITaskStep<T, S, E>> Steps, StepRelation Relation) StartSteps { get; }

    /// <summary>前向步骤向量映射 / The forward step vector mapping.</summary>
    IReadOnlyDictionary<ITaskStep<T, S, E>, ForwardTaskStepVector<T, S, E>> ForwardTaskStepVector { get; }

    /// <summary>后向步骤关系映射 / The backward step relation mapping.</summary>
    IReadOnlyDictionary<ITaskStep<T, S, E>, BackwardTaskStepVector<T, S, E>> BackwardStepRelation { get; }
}

/// <summary>
/// 前向任务步骤向量 / Forward task step vector representing forward relationships between steps.
/// </summary>
/// <typeparam name="T">多步任务类型 / The multi-step task type.</typeparam>
/// <typeparam name="S">步骤计划类型 / The step plan type.</typeparam>
/// <typeparam name="E">执行者类型 / The executor type.</typeparam>
/// <param name="From">源步骤 / The source step.</param>
/// <param name="To">目标步骤列表 / The list of target steps.</param>
/// <param name="Relation">步骤关系 / The step relation.</param>
public sealed record ForwardTaskStepVector<T, S, E>(
    ITaskStep<T, S, E> From,
    IReadOnlyList<ITaskStep<T, S, E>> To,
    StepRelation Relation
)
    where T : IAbstractMultiStepTask<T, S, E>
    where S : IAbstractTaskStepPlan<S, T, E>
    where E : Executor;

/// <summary>
/// 后向任务步骤向量 / Backward task step vector representing backward relationships between steps.
/// </summary>
/// <typeparam name="T">多步任务类型 / The multi-step task type.</typeparam>
/// <typeparam name="S">步骤计划类型 / The step plan type.</typeparam>
/// <typeparam name="E">执行者类型 / The executor type.</typeparam>
/// <param name="From">源步骤列表 / The list of source steps.</param>
/// <param name="To">目标步骤 / The target step.</param>
/// <param name="Relation">步骤关系 / The step relation.</param>
public sealed record BackwardTaskStepVector<T, S, E>(
    IReadOnlyList<ITaskStep<T, S, E>> From,
    ITaskStep<T, S, E> To,
    StepRelation Relation
)
    where T : IAbstractMultiStepTask<T, S, E>
    where S : IAbstractTaskStepPlan<S, T, E>
    where E : Executor;

/// <summary>
/// 任务步骤图，表示多步任务的步骤依赖关系（必须为DAG）/ Task step graph representing step dependencies for multi-step tasks (must be a DAG).
/// </summary>
/// <typeparam name="T">多步任务类型 / The multi-step task type.</typeparam>
/// <typeparam name="S">步骤计划类型 / The step plan type.</typeparam>
/// <typeparam name="E">执行者类型 / The executor type.</typeparam>
/// <param name="Id">图ID / The graph ID.</param>
/// <param name="Name">图名称 / The graph name.</param>
/// <param name="Steps">步骤列表 / The list of steps.</param>
/// <param name="StartSteps">起始步骤及关系 / The start steps and their relation.</param>
/// <param name="ForwardTaskStepVector">前向步骤向量映射 / The forward step vector mapping.</param>
/// <param name="BackwardStepRelation">后向步骤关系映射 / The backward step relation mapping.</param>
public sealed record TaskStepGraph<T, S, E>(
    string Id,
    string Name,
    IReadOnlyList<ITaskStep<T, S, E>> Steps,
    (IReadOnlyList<ITaskStep<T, S, E>> Steps, StepRelation Relation) StartSteps,
    IReadOnlyDictionary<ITaskStep<T, S, E>, ForwardTaskStepVector<T, S, E>> ForwardTaskStepVector,
    IReadOnlyDictionary<ITaskStep<T, S, E>, BackwardTaskStepVector<T, S, E>> BackwardStepRelation
) : ITaskStepGraph<T, S, E>
    where T : IAbstractMultiStepTask<T, S, E>
    where S : IAbstractTaskStepPlan<S, T, E>
    where E : Executor;
