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
/// 任务步骤冲突约束 / Task step conflict constraint.
/// 约束任务步骤之间的冲突关系。
/// Constrains conflict relationships between task steps.
/// </summary>
/// <typeparam name="T">任务类型 / Task type</typeparam>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public class TaskStepConflictConstraint<T, E, A>
    : ICGPipeline<IGanttSchedulingShadowPriceArguments<E, A>, object, GanttSchedulingShadowPriceMap<E, A>>
    where T : IAbstractTask<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    private readonly IReadOnlyList<T> _tasks;
    private readonly ICompilation _compilation;
    private readonly string _name;

    /// <summary>
    /// 任务步骤冲突约束构造 / Task step conflict constraint constructor
    /// </summary>
    /// <param name="tasks">任务列表 / List of tasks</param>
    /// <param name="compilation">编译结果 / Compilation result</param>
    /// <param name="name">管道名称 / Pipeline name</param>
    public TaskStepConflictConstraint(
        IReadOnlyList<T> tasks,
        ICompilation compilation,
        string name = "task_step_conflict") {
        _tasks = tasks;
        _compilation = compilation;
        _name = name;
    }

    string IMetaConstraintGroup.Name => _name;
    bool IMetaConstraintGroup.Lazy => false;

    /// <inheritdoc/>
    /// <remarks>
    /// **未接线就失败，不要静默成功。** 一个返回 `Ok` 的空壳等于宣称"该约束已施加且未违反"——
    /// 而实际什么都没做。这类缺陷不报错、不崩、只是结果偏掉，是最难查的一种。
    ///
    /// **Fails while unwired; never silently succeeds.** An empty stub returning `Ok` claims "this constraint was applied and is satisfied" while doing nothing at all. The
    /// defect raises nothing, crashes nothing and merely skews the result — the hardest kind to find.
    /// </remarks>
    public Try Invoke(object model) => Results.Failed<Success>(new Err<ErrorCode>(
        ErrorCode.ApplicationError,
        "TaskStepConflictConstraint 尚未接线，不能当作已施加 / not wired yet; it must not be treated as applied"));
}
