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
using System.Linq;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service.Limits;
/// <summary>
/// 任务准时最大化 / Task on-time maximization.
/// 最大化按时完成的任务数量。
/// Maximizes the number of tasks completed on time.
/// </summary>
/// <typeparam name="T">任务类型 / Task type</typeparam>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public class TaskOnTimeMaximization<T, E, A>
    : ICGPipeline<IGanttSchedulingShadowPriceArguments<E, A>, object, GanttSchedulingShadowPriceMap<E, A>>
    where T : IAbstractTask<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    private readonly IReadOnlyList<T> _tasks;
    private readonly TaskTime _taskTime;
    private readonly ulong _threshold;
    private readonly Flt64 _coefficient;
    private readonly string _name;

    /// <summary>
    /// 任务准时最大化构造 / Task on-time maximization constructor
    /// </summary>
    /// <param name="tasks">任务列表 / List of tasks</param>
    /// <param name="taskTime">任务时间 / Task time</param>
    /// <param name="threshold">阈值数量；0 表示无阈值 / Threshold quantity; 0 means no threshold</param>
    /// <param name="coefficient">成本系数；`null` 表示 1 / Cost coefficient; null means 1</param>
    /// <param name="name">管道名称 / Pipeline name</param>
    public TaskOnTimeMaximization(
        IReadOnlyList<T> tasks,
        TaskTime taskTime,
        ulong threshold = 0UL,
        Flt64? coefficient = null,
        string name = "task_on_time_maximization") {
        _tasks = tasks;
        _taskTime = taskTime;
        _threshold = threshold;
        _coefficient = coefficient ?? Flt64.One;
        _name = name;
    }

    string IMetaConstraintGroup.Name => _name;
    bool IMetaConstraintGroup.Lazy => false;

    /// <inheritdoc/>
    /// <remarks>
    /// 基线：最大化 `coefficient × Σ_t OnTime[t]`，目标名 `task on time`。任务集就是构造时给的那批——**不再按任务开关过滤**，
    /// 因为"允许移动"与"准时"是两件事：一个允许延迟的任务照样该被奖励准时完工，它的 `on_time` 在开关关着时只是常量零。
    ///
    /// 基线还有 `threshold` 分支（先建 `thresholdSlack` 再优化它的截断部分）。C# 的构造函数**没有 threshold 参数**，
    /// `ThresholdSlack` 只是纯函数，所以只有阈值 0 这一支。
    ///
    /// The baseline maximises `coefficient × Σ_t OnTime[t]`, objective name `task on time`. The task set is the one given at construction — **no per-task re-filtering**,
    /// because "may move" and "on time" are different things: a task allowed to delay still deserves credit for finishing on time, its `on_time` merely being constant zero when
    /// the switch is off. The baseline's `threshold` branch (build a `thresholdSlack`, optimise its capped part) has **no threshold parameter** here — `ThresholdSlack` is a pure
    /// function — so only the zero-threshold path exists.
    /// </remarks>
    public Try Invoke(object model) {
        ArgumentNullException.ThrowIfNull(model);

        if (model is not Fuookami.Ospf.Core.Model.Mechanism.IAbstractLinearMetaModel<Flt64> linear) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "准时目标只能施加到线性元模型 / the on-time objective can only be applied to a linear meta model"));
        }
        if (_taskTime is not IOnTime<T> { OnTime: IReadOnlyDictionary<T, object> table }) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                "任务时间尚未注册准时表 / the task time has not registered its on-time table yet"));
        }

        var monomials = new List<Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>>();
        Flt64 constant = Flt64.Zero;
        foreach (T task in _tasks) {
            if (!table.TryGetValue(task, out object? symbol)) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ApplicationError,
                    "准时表缺项：" + task.Id
                    + " / the on-time table is missing an entry: " + task.Id));
            }
            var polynomial = (Fuookami.Ospf.Core.Symbol.ILinearIntermediateSymbol<Flt64>)symbol;
            monomials.AddRange(polynomial.Polynomial.Monomials);
            constant = constant + polynomial.Polynomial.Constant;
        }

        // 阈值非零：先建一条阈值松弛并加到模型，再最大化**它的截断部**——"奖到阈值为止"。
        // 正部与截断部在这里不是一回事：正部在阈值以下是 0，截断部在阈值以下就是准时数本身。奖励类目标要的是后者。
        // A non-zero threshold builds a threshold slack, adds it to the model, and maximises **its capped part** — "rewarded up to the threshold". The positive and capped parts differ here: the
        // positive part is zero below the threshold while the capped part is the on-time count itself. A reward objective wants the latter.
        if (_threshold != 0UL) {
            Fuookami.Ospf.Math.Symbol.Polynomial.LinearPolynomial<Flt64> total =
            new(monomials.ToArray(), constant);
            Try registered = ThresholdSlack.Register(
                linear, total, new Flt64(_threshold), "task_on_time_threshold", out Fuookami.Ospf.Core.Symbol.Function.LinearFunctionSymbolAdapter<Flt64> slack);
            if (!registered.IsOk) {
                return registered;
            }
            Fuookami.Ospf.Math.Symbol.Polynomial.LinearPolynomial<Flt64> capped =
                ThresholdSlack.Scale(ThresholdSlack.CappedPart(slack, total), _coefficient);
            return linear.AddObject(
                Fuookami.Ospf.Core.Model.Basic.ObjectCategory.Maximum,
                new Fuookami.Ospf.Core.Symbol.Flatten.LinearFlattenData<Flt64>(capped.Monomials.ToArray(), capped.Constant),
                "task on time",
                displayName: null);
        }

        Fuookami.Ospf.Math.Symbol.Polynomial.LinearPolynomial<Flt64> weightedTotal = ThresholdSlack.Scale(
            new Fuookami.Ospf.Math.Symbol.Polynomial.LinearPolynomial<Flt64>(monomials.ToArray(), constant),
            _coefficient);
        return linear.AddObject(
            Fuookami.Ospf.Core.Model.Basic.ObjectCategory.Maximum,
            new Fuookami.Ospf.Core.Symbol.Flatten.LinearFlattenData<Flt64>(weightedTotal.Monomials.ToArray(), weightedTotal.Constant),
            "task on time",
            displayName: null);
    }
}
