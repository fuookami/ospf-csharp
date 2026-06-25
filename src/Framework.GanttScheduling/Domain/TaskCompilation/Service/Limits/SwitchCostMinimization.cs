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
/// 切换成本最小化 / Switch cost minimization
/// </summary>
/// <typeparam name="T">任务类型 / Task type</typeparam>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public class SwitchCostMinimization<T, E, A>
    : ICGPipeline<IGanttSchedulingShadowPriceArguments<E, A>, object, GanttSchedulingShadowPriceMap<E, A>>
    where T : IAbstractTask<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    private readonly IReadOnlyList<E> _executors;
    private readonly IReadOnlyList<T> _tasks;
    private readonly ISwitch _switch;
    private readonly string _name;

    /// <summary>
    /// 切换成本最小化构造 / Switch cost minimization constructor
    /// </summary>
    /// <param name="executors">执行器列表 / List of executors</param>
    /// <param name="tasks">任务列表 / List of tasks</param>
    /// <param name="switch">切换对象 / Switch object</param>
    /// <param name="name">管道名称 / Pipeline name</param>
    public SwitchCostMinimization(
        IReadOnlyList<E> executors,
        IReadOnlyList<T> tasks,
        ISwitch @switch,
        string name = "switch_cost_minimization") {
        _executors = executors;
        _tasks = tasks;
        _switch = @switch;
        _name = name;
    }

    string IMetaConstraintGroup.Name => _name;
    bool IMetaConstraintGroup.Lazy => false;

    /// <inheritdoc/>
    public Try Invoke(object model) => Results.Ok<Success>(Results.SuccessInstance);
}
