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
/// 任务未准时最小化 / Task not-on-time minimization.
/// 最小化未按时完成的任务数量。
/// Minimizes the number of tasks that are not completed on time.
/// </summary>
/// <typeparam name="T">任务类型 / Task type</typeparam>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public class TaskNotOnTimeMinimization<T, E, A>
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
    /// 任务未准时最小化构造 / Task not-on-time minimization constructor
    /// </summary>
    /// <param name="tasks">任务列表 / List of tasks</param>
    /// <param name="taskTime">任务时间 / Task time</param>
    /// <param name="threshold">阈值数量；0 表示无阈值 / Threshold quantity; 0 means no threshold</param>
    /// <param name="coefficient">成本系数；`null` 表示 1 / Cost coefficient; null means 1</param>
    /// <param name="name">管道名称 / Pipeline name</param>
    public TaskNotOnTimeMinimization(
        IReadOnlyList<T> tasks,
        TaskTime taskTime,
        ulong threshold = 0UL,
        Flt64? coefficient = null,
        string name = "task_not_on_time_minimization") {
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
    /// 基线：最小化 `coefficient × Σ_t NotOnTime[t]`，目标名 `task not on time`。任务集就是构造时给的那批——**不再按任务开关过滤**，
    /// 因为"允许移动"与"准时"是两件事：一个允许延迟的任务照样该被奖励准时完工，它的 `on_time` 在开关关着时只是常量零。
    ///
    /// 基线还有 `threshold` 分支（先建 `thresholdSlack` 再优化它的截断部分）。C# 的构造函数**没有 threshold 参数**，
    /// `ThresholdSlack` 只是纯函数，所以只有阈值 0 这一支。
    ///
    /// The baseline minimises `coefficient × Σ_t NotOnTime[t]`, objective name `task not on time`. The task set is the one given at construction — **no per-task re-filtering**,
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
        if (_taskTime is not IOnTime<T> { NotOnTime: IReadOnlyDictionary<T, object> table }) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                "任务时间尚未注册不准时表 / the task time has not registered its not-on-time table yet"));
        }

        var monomials = new List<Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>>();
        Flt64 constant = Flt64.Zero;
        foreach (T task in _tasks) {
            if (!table.TryGetValue(task, out object? symbol)) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ApplicationError,
                    "不准时表缺项：" + task.Id
                    + " / the not-on-time table is missing an entry: " + task.Id));
            }
            var polynomial = (Fuookami.Ospf.Core.Symbol.ILinearIntermediateSymbol<Flt64>)symbol;
            monomials.AddRange(polynomial.Polynomial.Monomials);
            constant = constant + polynomial.Polynomial.Constant;
        }

        // 阈值非零：先建一条阈值松弛并加到模型，再最小化**它的正部**（"不准时得超过阈值才算"）。
        // 系数**在松弛之外**乘：基线是 `coefficient * slack.positiveSlackPolynomial`，所以松弛建在未加权的和上，
        // 否则同一个解会得出相同的目标值，但符号系数与基线不同、判据也就钉不住系数这一环。
        // A non-zero threshold builds a threshold slack, adds it to the model, and minimises **its positive part** — "not on time counts only beyond the threshold". The coefficient is multiplied
        // **outside** the slack, matching the baseline's `coefficient * slack.positiveSlackPolynomial`: the slack is built on the unweighted sum, otherwise the same solution yields the same
        // objective value while the symbol coefficients differ from the baseline and the criteria could not pin the weighting.
        if (_threshold != 0UL) {
            Fuookami.Ospf.Math.Symbol.Polynomial.LinearPolynomial<Flt64> total =
            new(monomials.ToArray(), constant);
            Try registered = ThresholdSlack.Register(
                linear, total, new Flt64(_threshold), "task_not_on_time_threshold", out Fuookami.Ospf.Core.Symbol.Function.LinearFunctionSymbolAdapter<Flt64> slack);
            if (!registered.IsOk) {
                return registered;
            }
            Fuookami.Ospf.Math.Symbol.Polynomial.LinearPolynomial<Flt64> positive =
                ThresholdSlack.Scale(ThresholdSlack.PositivePart(slack), _coefficient);
            return linear.AddObject(
                Fuookami.Ospf.Core.Model.Basic.ObjectCategory.Minimum,
                new Fuookami.Ospf.Core.Symbol.Flatten.LinearFlattenData<Flt64>(positive.Monomials.ToArray(), positive.Constant),
                "task not on time",
                displayName: null);
        }

        Fuookami.Ospf.Math.Symbol.Polynomial.LinearPolynomial<Flt64> weightedTotal = ThresholdSlack.Scale(
            new Fuookami.Ospf.Math.Symbol.Polynomial.LinearPolynomial<Flt64>(monomials.ToArray(), constant),
            _coefficient);
        return linear.AddObject(
            Fuookami.Ospf.Core.Model.Basic.ObjectCategory.Minimum,
            new Fuookami.Ospf.Core.Symbol.Flatten.LinearFlattenData<Flt64>(weightedTotal.Monomials.ToArray(), weightedTotal.Constant),
            "task not on time",
            displayName: null);
    }
}
