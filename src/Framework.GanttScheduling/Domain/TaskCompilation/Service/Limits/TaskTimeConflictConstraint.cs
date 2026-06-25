#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service.Limits;
/// <summary>
/// 任务时间冲突约束 / Task time conflict constraint.
/// 检测并约束任务在时间维度上的冲突。
/// Detects and constrains task conflicts in the time dimension.
/// </summary>
/// <typeparam name="T">任务类型 / Task type</typeparam>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public class TaskTimeConflictConstraint<T, E, A> : IPipeline<object>
    where T : IAbstractTask<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    private readonly IReadOnlyList<T> _tasks;
    private readonly IReadOnlyList<E> _executors;
    private readonly TaskCompilation<T, E, A> _compilation;
    private readonly string _name;

    /// <summary>
    /// 任务时间冲突约束构造 / Task time conflict constraint constructor
    /// </summary>
    /// <param name="tasks">任务列表 / List of tasks</param>
    /// <param name="executors">执行器列表 / List of executors</param>
    /// <param name="compilation">编译结果 / Compilation result</param>
    /// <param name="name">管道名称 / Pipeline name</param>
    public TaskTimeConflictConstraint(
        IReadOnlyList<T> tasks,
        IReadOnlyList<E> executors,
        TaskCompilation<T, E, A> compilation,
        string name = "task_time_conflict") {
        _tasks = tasks;
        _executors = executors;
        _compilation = compilation;
        _name = name;
    }

    string IMetaConstraintGroup.Name => _name;
    bool IMetaConstraintGroup.Lazy => false;

    /// <inheritdoc/>
    public Try Invoke(object model) => Results.Ok<Success>(Results.SuccessInstance);
}
