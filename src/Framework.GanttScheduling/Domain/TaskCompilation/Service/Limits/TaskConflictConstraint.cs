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
/// 任务冲突约束 / Task conflict constraint.
/// 检测并约束任务之间的冲突关系。
/// Detects and constrains conflict relationships between tasks.
/// </summary>
/// <typeparam name="T">任务类型 / Task type</typeparam>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public class TaskConflictConstraint<T, E, A> : IPipeline<object>
    where T : IAbstractTask<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    private readonly IReadOnlyList<T> _tasks;
    private readonly IReadOnlyList<E> _executors;
    private readonly TaskCompilation<T, E, A> _compilation;
    private readonly Func<E, T, T, bool> _conflict;
    private readonly string _name;

    /// <summary>
    /// 任务冲突约束构造 / Task conflict constraint constructor
    /// </summary>
    /// <param name="tasks">任务列表 / List of tasks</param>
    /// <param name="executors">执行器列表 / List of executors</param>
    /// <param name="compilation">编译结果 / Compilation result</param>
    /// <param name="conflict">冲突判定谓词 / Conflict predicate</param>
    /// <param name="name">管道名称 / Pipeline name</param>
    public TaskConflictConstraint(
        IReadOnlyList<T> tasks,
        IReadOnlyList<E> executors,
        TaskCompilation<T, E, A> compilation,
        Func<E, T, T, bool> conflict,
        string name = "task_conflict") {
        _tasks = tasks;
        _executors = executors;
        _compilation = compilation;
        _conflict = conflict;
        _name = name;
    }

    string IMetaConstraintGroup.Name => _name;
    bool IMetaConstraintGroup.Lazy => false;

    /// <inheritdoc/>
    /// <remarks>
    /// 谓词版：对每个执行器、每对**无序**任务，谓词说"冲突"就加一行 `x[左, e] + x[右, e] &lt;= 1`。
    ///
    /// 省掉这一行**不会让任何解不可行**，只会让两个冲突的任务被排到同一台机器上——而目标函数通常照样给出一个
    /// 看起来不错的成本。这是"约束缺失"里最难发现的一类：解合法、求解成功、计划不可执行。
    ///
    /// The predicate version: for every executor and every **unordered** task pair, a conflicting pair gets `x[left, e] + x[right, e] &lt;= 1`.
    ///
    /// Omitting this row **never makes a solution infeasible**; it merely lets two conflicting tasks land on one machine — and the objective usually still reports a
    /// respectable cost. That is the hardest kind of missing constraint: a legal solution, a successful solve, an unexecutable plan.
    /// </remarks>
    public Try Invoke(object model) {
        ArgumentNullException.ThrowIfNull(model);

        if (model is not IAbstractLinearMetaModel<Flt64> linear) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "任务冲突约束只能施加到线性元模型 / the conflict constraint can only be applied to a linear meta model"));
        }
        return TaskConflictRows.Add(linear, _executors, _tasks, _compilation, _conflict, _name);
    }
}
