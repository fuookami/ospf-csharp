#nullable enable

using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation.Model;
/// <summary>
/// 任务束调度时间 / Bunch scheduling task time
/// </summary>
/// <typeparam name="B">任务束类型 / Bunch type</typeparam>
/// <typeparam name="T">任务类型 / Task type</typeparam>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public class BunchSchedulingTaskTime<B, T, E, A> : TaskTime
    where B : AbstractTaskBunch<T, E, A>
    where T : IAbstractTask<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    private readonly IReadOnlyList<T> _tasks;
    private readonly SolverTimeWindowBoundary _timeBoundary;
    private readonly TimeSpan? _redundancyRange;

    /// <summary>
    /// 任务束调度时间构造 / Bunch scheduling task time constructor
    /// </summary>
    /// <param name="timeWindow">时间窗口 / Time window</param>
    /// <param name="tasks">任务列表 / List of tasks</param>
    /// <param name="compilation">任务束编译结果 / Bunch compilation result</param>
    /// <param name="redundancyRange">冗余范围 / Redundancy range</param>
    public BunchSchedulingTaskTime(
        TimeWindow<Flt64> timeWindow,
        IReadOnlyList<T> tasks,
        BunchCompilation<B, T, E, A> compilation,
        TimeSpan? redundancyRange = null) {
        _tasks = tasks;
        _timeBoundary = new SolverTimeWindowBoundary(timeWindow);
        _redundancyRange = redundancyRange;
        Compilation = compilation;
    }

    /// <summary>
    /// 通过 solver 时间窗口边界创建 / Create from solver time-window boundary
    /// </summary>
    /// <param name="timeBoundary">solver 时间窗口边界 / Solver time-window boundary</param>
    /// <param name="tasks">任务列表 / List of tasks</param>
    /// <param name="compilation">任务束编译结果 / Bunch compilation result</param>
    /// <param name="redundancyRange">冗余范围 / Redundancy range</param>
    public BunchSchedulingTaskTime(
        SolverTimeWindowBoundary timeBoundary,
        IReadOnlyList<T> tasks,
        BunchCompilation<B, T, E, A> compilation,
        TimeSpan? redundancyRange = null) {
        _tasks = tasks;
        _timeBoundary = timeBoundary;
        _redundancyRange = redundancyRange;
        Compilation = compilation;
    }

    /// <inheritdoc/>
    public override bool DelayEnabled => true;

    /// <inheritdoc/>
    public override bool AdvanceEnabled => true;

    /// <inheritdoc/>
    public override bool OverMaxDelayEnabled => true;

    /// <inheritdoc/>
    public override bool OverMaxAdvanceEnabled => true;

    /// <inheritdoc/>
    public override bool DelayLastEndTimeEnabled => true;

    /// <inheritdoc/>
    public override bool AdvanceEarliestEndTimeEnabled => true;

    /// <summary>任务束编译结果 / Bunch compilation result</summary>
    public BunchCompilation<B, T, E, A> Compilation { get; }

    /// <summary>任务列表 / List of tasks</summary>
    public IReadOnlyList<T> Tasks => _tasks;

    /// <summary>solver 时间窗口边界 / Solver time-window boundary</summary>
    public SolverTimeWindowBoundary TimeBoundary => _timeBoundary;

    /// <summary>是否有冗余 / Whether redundancy is present</summary>
    public bool WithRedundancy => _redundancyRange.HasValue;

    /// <inheritdoc/>
    public override Try Register(object model) => Results.Ok<Success>(Results.SuccessInstance);

    /// <summary>
    /// 添加列 / Add columns
    /// </summary>
    /// <param name="iteration">迭代次数 / Iteration count</param>
    /// <param name="bunches">任务束列表 / List of bunches</param>
    /// <param name="model">线性元模型 / Linear meta model</param>
    /// <returns>操作结果 / Operation result</returns>
    public virtual Try AddColumns(ulong iteration, IReadOnlyList<B> bunches, object model) => Results.Ok<Success>(Results.SuccessInstance);
}
