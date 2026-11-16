#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service.Limits;
/// <summary>
/// 任务提前最早结束时间最小化 / Task advance earliest end time minimization
/// </summary>
/// <typeparam name="T">任务类型 / Task type</typeparam>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public class TaskAdvanceEarliestEndTimeMinimization<T, E, A>
    : ICGPipeline<IGanttSchedulingShadowPriceArguments<E, A>, object, GanttSchedulingShadowPriceMap<E, A>>
    where T : IAbstractTask<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    private readonly SolverTimeWindowBoundary _timeBoundary;
    private readonly IReadOnlyList<T> _tasks;
    private readonly TaskTime _taskTime;
    private readonly Func<T, Flt64?> _coefficient;
    private readonly string _name;

    /// <summary>
    /// 任务提前最早结束时间最小化构造 / Task advance earliest end time minimization constructor
    /// </summary>
    /// <param name="timeWindow">时间窗口 / Time window</param>
    /// <param name="tasks">任务列表 / List of tasks</param>
    /// <param name="taskTime">任务时间对象 / Task time object</param>
    /// <param name="coefficient">成本系数提取器；`null` 表示 1 / Extractor for the cost; null means 1</param>
    /// <param name="name">管道名称 / Pipeline name</param>
    public TaskAdvanceEarliestEndTimeMinimization(
        TimeWindow<Flt64> timeWindow,
        IReadOnlyList<T> tasks,
        TaskTime taskTime,
        Func<T, Flt64?>? coefficient = null,
        string name = "task_advance_earliest_end_time_minimization") {
        _timeBoundary = new SolverTimeWindowBoundary(timeWindow);
        _tasks = tasks;
        _taskTime = taskTime;
        _coefficient = coefficient ?? (_ => Flt64.One);
        _name = name;
    }

    /// <summary>
    /// 通过 solver 时间窗口边界创建 / Create from solver time-window boundary
    /// </summary>
    /// <param name="timeBoundary">solver 时间窗口边界 / Solver time-window boundary</param>
    /// <param name="tasks">任务列表 / List of tasks</param>
    /// <param name="taskTime">任务时间对象 / Task time object</param>
    /// <param name="coefficient">成本系数提取器；`null` 表示 1 / Extractor for the cost; null means 1</param>
    /// <param name="name">管道名称 / Pipeline name</param>
    public TaskAdvanceEarliestEndTimeMinimization(
        SolverTimeWindowBoundary timeBoundary,
        IReadOnlyList<T> tasks,
        TaskTime taskTime,
        Func<T, Flt64?>? coefficient = null,
        string name = "task_advance_earliest_end_time_minimization") {
        _timeBoundary = timeBoundary;
        _tasks = tasks;
        _taskTime = taskTime;
        _coefficient = coefficient ?? (_ => Flt64.One);
        _name = name;
    }

    string IMetaConstraintGroup.Name => _name;
    bool IMetaConstraintGroup.Lazy => false;

    /// <inheritdoc/>
    /// <remarks>
    /// 基线：`AdvanceEarliestEndTimeEnabled` 时最小化 `Σ coefficient(task) × AdvanceEarliestEndTime[task]`，目标名 `task advance earliest end time`；开关关着时任务集为空，目标不存在。
    /// 任务集是构造时给的那批里"允许提前且给了最早结束时间"的那些——两项都不满足的任务贡献的是常量零。
    ///
    /// 系数提取失败时无穷大；`Flt64` 没有无穷，所以这里失败。`threshold` 分支仍未实现（构造函数没有 threshold 参数）。
    ///
    /// The code minimises `Σ coefficient(task) × AdvanceEarliestEndTime[task]` when `AdvanceEarliestEndTimeEnabled`, objective name `task advance earliest end time`; with the switch off the set is empty and the objective
    /// does not exist. It covers the tasks that may early and carry a earliest end time — the rest contribute a constant zero. An extraction failure uses infinity in the code and
    /// `Flt64` has none, so it fails here. The `threshold` branch is still unimplemented (no threshold parameter).
    /// </remarks>
    public Try Invoke(object model) {
        ArgumentNullException.ThrowIfNull(model);

        if (!_taskTime.AdvanceEarliestEndTimeEnabled) {
            return Results.Ok<Success>(Results.SuccessInstance);
        }
        if (model is not Fuookami.Ospf.Core.Model.Mechanism.IAbstractLinearMetaModel<Flt64> linear) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "结束时间偏移最小化只能施加到线性元模型 / the end-time deviation minimisation can only be applied to a linear meta model"));
        }
        if (_taskTime is not IEndDeviation<T> { AdvanceEarliestEndTime: IReadOnlyDictionary<T, object> table }) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                "任务时间尚未注册提前结束量表 / the task time has not registered its early-finish table"));
        }

        var monomials = new List<Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>>();
        Flt64 constant = Flt64.Zero;
        foreach (T task in _tasks) {
            bool movable = task.AdvanceEnabled;
            if (!movable || task.EarliestEndTime is null) {
                continue;
            }
            if (!table.TryGetValue(task, out object? symbol)) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ApplicationError,
                    "提前结束量表缺项：" + task.Id
                    + " / the early-finish table is missing an entry: " + task.Id));
            }
            Flt64? weight = _coefficient(task);
            if (weight is not { } coefficient) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ApplicationError,
                    "提前结束量系数提取失败：" + task.Id
                    + " / the early-finish coefficient could not be extracted: " + task.Id));
            }
            var polynomial = (Fuookami.Ospf.Core.Symbol.ILinearIntermediateSymbol<Flt64>)symbol;
            foreach (Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64> monomial in polynomial.Polynomial.Monomials) {
                monomials.Add(new Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>(
                    coefficient * monomial.Coefficient, monomial.Symbol));
            }
            constant = constant + coefficient * polynomial.Polynomial.Constant;
        }
        if (monomials.Count == 0 && constant.Eq(Flt64.Zero)) {
            // 没有可计费的任务 → 目标恒零，加它只占一个目标位。
            // No chargeable task → the objective is identically zero; adding it only occupies an objective slot.
            return Results.Ok<Success>(Results.SuccessInstance);
        }

        return linear.AddObject(
            Fuookami.Ospf.Core.Model.Basic.ObjectCategory.Minimum,
            new Fuookami.Ospf.Core.Symbol.Flatten.LinearFlattenData<Flt64>(monomials.ToArray(), constant),
            "task advance earliest end time",
            displayName: null);
    }
}
