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
/// 任务超过最大提前时间最小化 / Task over-max advance time minimization
/// </summary>
/// <typeparam name="T">任务类型 / Task type</typeparam>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public class TaskOverMaxAdvanceTimeMinimization<T, E, A>
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
    /// 任务超过最大提前时间最小化构造 / Task over-max advance time minimization constructor
    /// </summary>
    /// <param name="timeWindow">时间窗口 / Time window</param>
    /// <param name="tasks">任务列表 / List of tasks</param>
    /// <param name="taskTime">任务时间 / Task time</param>
    /// <param name="coefficient">成本系数提取器；`null` 表示 1 / Extractor for the cost; null means 1</param>
    /// <param name="name">管道名称 / Pipeline name</param>
    public TaskOverMaxAdvanceTimeMinimization(
        TimeWindow<Flt64> timeWindow,
        IReadOnlyList<T> tasks,
        TaskTime taskTime,
        Func<T, Flt64?>? coefficient = null,
        string name = "task_over_max_advance_time_minimization") {
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
    /// <param name="taskTime">任务时间 / Task time</param>
    /// <param name="coefficient">成本系数提取器；`null` 表示 1 / Extractor for the cost; null means 1</param>
    /// <param name="name">管道名称 / Pipeline name</param>
    public TaskOverMaxAdvanceTimeMinimization(
        SolverTimeWindowBoundary timeBoundary,
        IReadOnlyList<T> tasks,
        TaskTime taskTime,
        Func<T, Flt64?>? coefficient = null,
        string name = "task_over_max_advance_time_minimization") {
        _timeBoundary = timeBoundary;
        _tasks = tasks;
        _taskTime = taskTime;
        _coefficient = coefficient ?? (_ => Flt64.One);
        _name = name;
    }

    /// <inheritdoc/>
    string IMetaConstraintGroup.Name => _name;

    /// <inheritdoc/>
    bool IMetaConstraintGroup.Lazy => false;

    /// <inheritdoc/>
    /// <remarks>
    /// 基线：`overMaxAdvanceEnabled` 时最小化 `Σ coefficient(task) × over_max_advance[task]`，任务集是
    /// **允许提前且给了最大允许量**的那些；开关关着时任务集为空，目标不存在。
    ///
    /// 系数提取失败时无穷大；`Flt64` 没有无穷（的同一处限制），所以这里失败。
    /// `threshold` 分支仍未实现（构造函数没有 threshold 参数，`ThresholdSlack` 只是纯函数）。
    ///
    /// The code minimises `Σ coefficient(task) × over_max_advance[task]` when `overMaxAdvanceEnabled`, over the tasks that **may advance and carry a maximum**; with the
    /// switch off the set is empty and the objective does not exist. An extraction failure uses infinity in the code and `Flt64` has none (the same limit), so it fails
    /// here. The `threshold` branch is still unimplemented (no threshold parameter; `ThresholdSlack` is a pure function).
    /// </remarks>
    public Try Invoke(object model) {
        ArgumentNullException.ThrowIfNull(model);

        if (!_taskTime.OverMaxAdvanceEnabled) {
            return Results.Ok<Success>(Results.SuccessInstance);
        }
        if (model is not Fuookami.Ospf.Core.Model.Mechanism.IAbstractLinearMetaModel<Flt64> linear) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "超最大提前最小化只能施加到线性元模型 / over-max advance minimisation can only be applied to a linear meta model"));
        }
        if (_taskTime is not IOverMaxDeviation<T> { OverMaxAdvanceTime: IReadOnlyDictionary<T, object> table }) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                "任务时间尚未注册超最大提前量表 / the task time has not registered its over-max advance table"));
        }

        var monomials = new List<Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>>();
        Flt64 constant = Flt64.Zero;
        foreach (T task in _tasks) {
            if (!task.AdvanceEnabled || task.MaxAdvance is null) {
                continue;
            }
            if (!table.TryGetValue(task, out object? symbol)) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ApplicationError,
                    "超最大提前量表缺项：" + task.Id
                    + " / the over-max advance table is missing an entry: " + task.Id));
            }
            Flt64? weight = _coefficient(task);
            if (weight is not { } coefficient) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ApplicationError,
                    "超最大提前系数提取失败：" + task.Id
                    + " / the over-max advance coefficient could not be extracted: " + task.Id));
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
            "task over max advance time",
            displayName: null);
    }
}
