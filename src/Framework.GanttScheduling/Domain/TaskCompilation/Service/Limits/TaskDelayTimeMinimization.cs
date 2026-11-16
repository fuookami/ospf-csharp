#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
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
/// 任务延迟时间最小化 / Task delay time minimization
/// </summary>
/// <typeparam name="T">任务类型 / Task type</typeparam>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public class TaskDelayTimeMinimization<T, E, A>
    : ICGPipeline<IGanttSchedulingShadowPriceArguments<E, A>, object, GanttSchedulingShadowPriceMap<E, A>>
    where T : IAbstractTask<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    private readonly SolverTimeWindowBoundary _timeBoundary;
    private readonly IReadOnlyList<T> _tasks;
    private readonly TaskTime _taskTime;
    private readonly Func<T, TimeSpan?> _threshold;
    private readonly Func<T, Flt64?> _coefficient;
    private readonly string _name;

    /// <summary>
    /// 任务延迟时间最小化构造 / Task delay time minimization constructor
    /// </summary>
    /// <param name="timeWindow">时间窗口 / Time window</param>
    /// <param name="tasks">任务列表 / List of tasks</param>
    /// <param name="taskTime">任务时间对象 / Task time object</param>
    /// <param name="threshold">阈值提取器；`null` 表示每任务 0 / Threshold extractor; null means zero per task</param>
    /// <param name="coefficient">延迟成本系数提取器；`null` 表示每任务 1 / Extractor for the delay cost; null means 1 per task</param>
    /// <param name="name">管道名称 / Pipeline name</param>
    public TaskDelayTimeMinimization(
        TimeWindow<Flt64> timeWindow,
        IReadOnlyList<T> tasks,
        TaskTime taskTime,
        Func<T, TimeSpan?>? threshold = null,
        Func<T, Flt64?>? coefficient = null,
        string name = "task_delay_time_minimization") {
        _timeBoundary = new SolverTimeWindowBoundary(timeWindow);
        _tasks = tasks;
        _taskTime = taskTime;
        _threshold = threshold ?? (_ => TimeSpan.Zero);
        _coefficient = coefficient ?? (_ => Flt64.One);
        _name = name;
    }

    /// <summary>
    /// 通过 solver 时间窗口边界创建 / Create from solver time-window boundary
    /// </summary>
    /// <param name="timeBoundary">solver 时间窗口边界 / Solver time-window boundary</param>
    /// <param name="tasks">任务列表 / List of tasks</param>
    /// <param name="taskTime">任务时间对象 / Task time object</param>
    /// <param name="threshold">阈值提取器；`null` 表示每任务 0 / Threshold extractor; null means zero per task</param>
    /// <param name="coefficient">延迟成本系数提取器；`null` 表示每任务 1 / Extractor for the delay cost; null means 1 per task</param>
    /// <param name="name">管道名称 / Pipeline name</param>
    public TaskDelayTimeMinimization(
        SolverTimeWindowBoundary timeBoundary,
        IReadOnlyList<T> tasks,
        TaskTime taskTime,
        Func<T, TimeSpan?>? threshold = null,
        Func<T, Flt64?>? coefficient = null,
        string name = "task_delay_time_minimization") {
        _timeBoundary = timeBoundary;
        _tasks = tasks;
        _taskTime = taskTime;
        _threshold = threshold ?? (_ => TimeSpan.Zero);
        _coefficient = coefficient ?? (_ => Flt64.One);
        _name = name;
    }

    string IMetaConstraintGroup.Name => _name;
    bool IMetaConstraintGroup.Lazy => false;

    /// <inheritdoc/>
    /// <remarks>
    /// 基线：`taskTime.delayEnabled` 时最小化 `Σ coefficient(task) * delayTime[task]`，任务集由
    /// `TimeDeviationSelection.Select`（全局开关 + 任务开关 + **有排定时间**）筛出。
    ///
    /// 三处与基线不同，都是"无法表达"而不是"换个说法"：
    /// <list type="number">
    /// <item>延迟未启用时返回 `Ok`——**保留**：那条目标本就不存在，无事可做不是"假装做了"；</item>
    /// <item>系数提取失败时无穷大；`Flt64` 没有无穷（的同一处限制），所以这里失败；</item>
    /// <item>`threshold` 分支（阈值非零时先建 `thresholdSlack` 再最小化其正部）。C# 这边的构造函数**没有
    ///   threshold 参数**，`ThresholdSlack` 也只是个纯函数——所以只有阈值 0 这一支，阈值那条路径没有可表达的入口。</item>
    /// </list>
    ///
    /// The code minimises `Σ coefficient(task) * delayTime[task]` when `taskTime.delayEnabled`, over the task set from `TimeDeviationSelection.Select` (global switch + task
    /// switch + **has a scheduled time**). Three departures, all "cannot express": delay disabled returns `Ok` (**kept** — the objective does not exist); an extraction failure uses
    /// infinity in the code and `Flt64` has none (the same limit), so it fails; and its `threshold` branch (build a `thresholdSlack`, minimise its positive part)
    /// has **no threshold parameter** in the C# constructor — `ThresholdSlack` is a pure function only, so only the zero-threshold path is expressible.
    /// </remarks>
    public Try Invoke(object model) {
        ArgumentNullException.ThrowIfNull(model);

        if (!_taskTime.DelayEnabled) {
            return Results.Ok<Success>(Results.SuccessInstance);
        }
        if (model is not IAbstractLinearMetaModel<Flt64> linear) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "延迟时间最小化只能施加到线性元模型 / delay-time minimisation can only be applied to a linear meta model"));
        }
        if (_taskTime is not ITimeDeviation<T> { DelayTime: IReadOnlyDictionary<T, object> delayTime }) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                "任务时间尚未注册延迟时间，无法最小化它 / the task time has not registered its delay times, so they cannot be minimised"));
        }

        IReadOnlyList<T> selected = TimeDeviationSelection.Select(
            globalEnabled: _taskTime.DelayEnabled,
            tasks: _tasks,
            directionEnabled: task => task.DelayEnabled,
            hasScheduledTime: task => task.ScheduledTime is not null);

        var monomials = new List<Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>>();
        Flt64 constant = Flt64.Zero;
        foreach (T task in selected) {
            if (!delayTime.TryGetValue(task, out object? symbol)) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ApplicationError,
                    "任务没有延迟时间符号：" + task.Id
                    + " / the task has no delay-time symbol: " + task.Id));
            }
            Flt64? weight = _coefficient(task);
            if (weight is not { } coefficient) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ApplicationError,
                    "延迟时间系数提取失败：" + task.Id
                    + " / the delay-time coefficient could not be extracted: " + task.Id));
            }
            var polynomial = (Fuookami.Ospf.Core.Symbol.ILinearIntermediateSymbol<Flt64>)symbol;
            Flt64 thisThreshold = _timeBoundary.ValueOf(_threshold(task) ?? TimeSpan.Zero);
            Fuookami.Ospf.Math.Symbol.Polynomial.LinearPolynomial<Flt64> priced;
            if (thisThreshold.ToDouble() == 0.0) {
                priced = polynomial.Polynomial;
            } else {
                // 阈值非零：先建一条阈值松弛并加到模型，再对**它的正部**计费。
                // A non-zero threshold builds a threshold slack, adds it to the model, and prices **its positive part**.
                Try registered = ThresholdSlack.Register(
                    linear, polynomial.Polynomial, thisThreshold, "delay_time_threshold_" + task.Id, out Fuookami.Ospf.Core.Symbol.Function.LinearFunctionSymbolAdapter<Flt64> slack);
                if (!registered.IsOk) {
                    return registered;
                }
                priced = ThresholdSlack.PositivePart(slack);
            }
            Fuookami.Ospf.Math.Symbol.Polynomial.LinearPolynomial<Flt64> weighted =
                ThresholdSlack.Scale(priced, coefficient);
            monomials.AddRange(weighted.Monomials);
            constant = constant + weighted.Constant;
        }
        if (monomials.Count == 0 && constant.Eq(Flt64.Zero)) {
            // 没有可计费的任务 → 目标恒为零。加一条恒零目标只会白白占一个目标位。
            // No chargeable task → the objective is identically zero; adding it only occupies an objective slot for nothing.
            return Results.Ok<Success>(Results.SuccessInstance);
        }

        return linear.AddObject(
            ObjectCategory.Minimum,
            new Fuookami.Ospf.Core.Symbol.Flatten.LinearFlattenData<Flt64>(monomials.ToArray(), constant),
            "task delay time",
            displayName: null);
    }
}
