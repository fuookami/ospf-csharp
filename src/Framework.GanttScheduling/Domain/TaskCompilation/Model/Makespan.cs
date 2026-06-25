#nullable enable

using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
/// <summary>
/// 最大完工时间 / Makespan
/// </summary>
/// <typeparam name="T">任务类型 / Task type</typeparam>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public class Makespan<T, E, A>
    where T : IAbstractTask<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    private readonly IReadOnlyList<T> _tasks;
    private readonly TaskTime _taskTime;
    private readonly bool _extra;

    /// <summary>
    /// 最大完工时间构造 / Makespan constructor
    /// </summary>
    /// <param name="tasks">任务列表 / List of tasks</param>
    /// <param name="taskTime">任务时间对象 / Task time object</param>
    /// <param name="extra">是否使用额外的最小最大函数 / Whether to use extra min-max function</param>
    public Makespan(IReadOnlyList<T> tasks, TaskTime taskTime, bool extra = false) {
        _tasks = tasks;
        _taskTime = taskTime;
        _extra = extra;
    }

    /// <summary>任务列表 / List of tasks</summary>
    public IReadOnlyList<T> Tasks => _tasks;

    /// <summary>任务时间对象 / Task time object</summary>
    public TaskTime TaskTime => _taskTime;

    /// <summary>注册最大完工时间到模型 / Register makespan to model</summary>
    /// <param name="model">元模型 / Meta model</param>
    /// <returns>操作结果 / Operation result</returns>
    public Try Register(object model) => Results.Ok<Success>(Results.SuccessInstance);
}
