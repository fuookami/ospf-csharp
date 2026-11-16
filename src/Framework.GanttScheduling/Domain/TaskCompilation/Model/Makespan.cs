#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service.Limits;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
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
public class Makespan<T, E, A> : IMakespan
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

    /// <summary>最大完工时间符号；`null` 表示尚未注册 / The makespan symbol; null means not registered yet.</summary>
    public object? MakespanSymbol => _makespan;

    /// <summary>注册最大完工时间到模型 / Register makespan to model</summary>
    /// <param name="model">元模型 / Meta model</param>
    /// <returns>操作结果 / Operation result</returns>
    /// <remarks>
    /// 完工时间用 `MaxFunction.fromSymbols(tasks.map { taskTime.estimateEndTime[it] })`（`extra` 时换 `MinMaxFunction`，**取值相同**，
    /// 只是多出选择变量以便事后指认是哪个任务决定完工时间——见 <see cref="MakespanValue"/>）。
    ///
    /// 两个必须先失败的情况：
    /// <list type="number">
    /// <item>任务时间没提供结束时间表——**没有结束时间，完工时间是空的**；</item>
    /// <item>某个任务在表里查不到——跳过它会让完工时间偏小而看不出来。</item>
    /// </list>
    ///
    /// The makespan is `MaxFunction.fromSymbols(tasks.map { taskTime.estimateEndTime[it] })` (`MinMaxFunction` when `extra`, **same value**, only adding selector variables to
    /// identify afterwards which task sets the makespan — see <see cref="MakespanValue"/>). Two cases must fail first: the task time offers no end-time table (**no end times, no
    /// makespan**), and a task missing from that table (skipping it shrinks the makespan invisibly).
    /// </remarks>
    public Try Register(object model) {
        ArgumentNullException.ThrowIfNull(model);

        if (model is not IAbstractLinearMetaModel<Flt64> linear) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "最大完工时间只能注册到线性元模型 / the makespan can only be registered into a linear meta model"));
        }
        if (_makespan is not null) {
            return Results.Ok<Success>(Results.SuccessInstance);
        }
        if (_taskTime is not IEstimateEndTime<T> endTimes || endTimes.EstimateEndTime is not { } table) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                "任务时间尚未注册结束时间，无法计算完工时间 / the task time has not registered its end times, so the makespan cannot be computed"));
        }

        var polynomials = new List<LinearPolynomial<Flt64>>(_tasks.Count);
        foreach (T task in _tasks) {
            if (!table.TryGetValue(task, out object? endTime)) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ApplicationError,
                    "任务没有预估结束时间，不能计入完工时间：" + task.Id
                    + " / the task has no estimated end time: " + task.Id));
            }
            polynomials.Add(new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, (ISymbol)endTime) },
                Flt64.Zero));
        }
        if (polynomials.Count == 0) {
            // 没有任务时建不出 `MaxFunction`（它要求至少一个多项式）。空排程的完工时间就是零。
            // With no tasks a `MaxFunction` cannot be built either (it requires at least one polynomial). An empty schedule finishes at zero.
            _makespan = new LinearExpressionSymbol(new MutableLinearPolynomial<Flt64>(null, Flt64.Zero), name: "makespan");
            return linear.Add((IIntermediateSymbol)_makespan);
        }

        // `extra` 只决定是否建选择变量，**取值不变**——用同一个 MaxFunction 谱系表达两者。
        // `extra` only decides whether selector variables are built; **the value is identical** — both branches go through the same MaxFunction family.
        // Big-M：**必须显式给**。`LinearExpressionSymbol.Range` 每次访问都新建对象，写进去的上界读不到，
        // 极值函数于是拿不到有限上下界，一路失败到 `极值函数 Big-M 不可推导或无效`（第一版就栽在这里）。
        // 完工时间的天然上界是时间窗结束值：任何结束时间都在 `[0, endValue]` 内。
        // Big-M: **must be explicit**. `LinearExpressionSymbol.Range` returns a fresh object each access, so the upper bound written there is unreadable and the extremum function
        // fails all the way down with `Extremum Big-M is unavailable or invalid` (where the first version died). The makespan's natural bound is the window end: every end time
        // lies in `[0, endValue]`.
        if (_taskTime.TimeBoundary is not { } boundary) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                "任务时间没有时间窗边界，无法给完工时间定 Big-M / the task time exposes no time-window boundary, so the makespan has no Big-M"));
        }
        // 结束时间 = 开始时间 + 时长，而开始时间最高可到窗口结束——所以 Big-M 必须**再加上最长时长**。
        // 只取窗口结束值会让 Big-M 小于真实的结束时间，极值函数于是给出错误的下界（解偏小且看不出来）。
        //
        // End time = start + duration, and the start may reach the window end — so the Big-M must **include the longest duration**. Taking the window end alone makes the Big-M
        // smaller than a real end time, and the extremum then yields a wrong bound (too small, invisibly).
        Flt64 longest = Flt64.Zero;
        foreach (T task in _tasks) {
            if (task.Duration is { } duration) {
                Flt64 value = boundary.ValueOf(duration);
                if (value.Gr(longest)) {
                    longest = value;
                }
            }
        }
        Flt64 bigM = boundary.EndValue + longest;
        if (!bigM.Gr(Flt64.Zero)) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                "时间窗没有正长度，完工时间没有可用的 Big-M / the time window has no positive length, so there is no usable Big-M"));
        }
        IMathFunctionSymbol<Flt64> function = _extra
            ? new MinMaxFunction<Flt64>(polynomials, IIntoValue<Flt64>.Identity, bigM: bigM, name: "makespan")
            : new MaxFunction<Flt64>(polynomials, IIntoValue<Flt64>.Identity, bigM: bigM, name: "makespan");
        // **完工时间就是那个 `MaxFunction` 本身**，不再另建一个同名符号——第一版那样做撞了
        // `SymbolRepetitive: Repeated "makespan"`，而报错里老符号叫 `LinearFunctionSymbolAdapter`、
        // 新符号叫 `makespan`，看上去像"两个不同的东西"，其实是同一个名字。
        //
        // **The makespan is that `MaxFunction` itself**, not a second symbol of the same name — the first version tripped `SymbolRepetitive: Repeated "makespan"`, and the message
        // named the old symbol `LinearFunctionSymbolAdapter` and the new one `makespan`, which reads like "two different things" but is one name.
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        Try addedFunction = linear.Add(adapter);
        if (!addedFunction.IsOk) {
            return addedFunction;
        }
        _makespan = adapter;
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    private object? _makespan;
}
