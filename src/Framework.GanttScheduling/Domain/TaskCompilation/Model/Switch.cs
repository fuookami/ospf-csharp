#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.ValueRange;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;

/// <summary>
/// 切换接口 / Switch interface
/// </summary>
public interface ISwitch {
    /// <summary>注册切换到模型 / Register switch to model</summary>
    /// <param name="model">元模型 / Meta model</param>
    /// <returns>操作结果 / Operation result</returns>
    Try Register(object model);
}

/// <summary>
/// 暴露换型指示与换型时间表的切换 / A switch that exposes its switch and switch-time tables.
/// </summary>
/// <remarks>
/// 两张表**不是同一件事**：
/// <list type="bullet">
/// <item>`Switch` 是 `(执行器, 前序, 后序) → 0/1` 的**指示**：这台机器上是否从前序做到后序；</item>
/// <item>`SwitchTime` 是 `(前序, 后序) → 秒` 的**时间**：两任务之间隔多久（换型时间 = 间隔 × 指示）。</item>
/// </list>
/// 换型成本收的是前者，换型时间收的是后者。把两者混用会让"成本"变成"秒数"——量纲变了而目标仍然是个正数。
///
/// The two tables are **not the same thing**: `Switch` is the `(executor, from, to) → 0/1` **indicator** (is that machine going from `from` to `to`), while `SwitchTime` is the
/// `(from, to) → seconds` **time** (the gap, i.e. switch time = gap × indicator). The cost objective collects the former and the time objective the latter. Mixing them turns
/// "cost" into "seconds" — the dimension changes while the objective still looks like a positive number.
/// </remarks>
/// <typeparam name="T">任务类型 / Task type.</typeparam>
/// <typeparam name="E">执行器类型 / Executor type.</typeparam>
public interface ISwitchTables<T, E> {
    /// <summary>换型指示表 / The switch indicator table.</summary>
    IReadOnlyDictionary<(E Executor, T From, T To), object>? SwitchTable { get; }

    /// <summary>换型时间表 / The switch-time table.</summary>
    IReadOnlyDictionary<(T From, T To), object>? SwitchTimeTable { get; }
}

/// <summary>
/// 任务调度切换 / Task scheduling switch
/// </summary>
/// <typeparam name="T">任务类型 / Task type</typeparam>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public class TaskSchedulingSwitch<T, E, A> : ISwitch, ISwitchTables<T, E>
    where T : IAbstractTask<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    private readonly TimeWindow<Flt64> _timeWindow;
    private readonly IReadOnlyList<T> _tasks;
    private readonly IReadOnlyList<E> _executors;
    private readonly TaskCompilation<T, E, A> _compilation;
    private readonly TaskTime? _taskTime;
    private readonly SolverTimeWindowBoundary _timeBoundary;
    private Dictionary<(T, T), object> _frontOf = new();
    private Dictionary<(T, T, T), object> _betweenIn = new();

    /// <summary>
    /// 任务调度切换构造 / Task scheduling switch constructor
    /// </summary>
    /// <param name="timeWindow">时间窗口 / Time window</param>
    /// <param name="tasks">任务列表 / List of tasks</param>
    /// <param name="executors">执行器列表 / List of executors</param>
    /// <param name="compilation">任务编译结果 / Task compilation result</param>
    /// <param name="taskTime">任务时间对象 / Task time object</param>
    public TaskSchedulingSwitch(
        TimeWindow<Flt64> timeWindow,
        IReadOnlyList<T> tasks,
        IReadOnlyList<E> executors,
        TaskCompilation<T, E, A> compilation,
        TaskTime? taskTime = null) {
        _timeWindow = timeWindow;
        _tasks = tasks;
        _executors = executors;
        _compilation = compilation;
        _taskTime = taskTime;
        _timeBoundary = new SolverTimeWindowBoundary(timeWindow);
    }

    /// <summary>
    /// 通过 solver 时间窗口边界创建任务调度切换 / Create task scheduling switch from a solver time-window boundary
    /// </summary>
    /// <param name="timeBoundary">solver 时间窗口边界 / Solver time-window boundary</param>
    /// <param name="tasks">任务列表 / List of tasks</param>
    /// <param name="executors">执行器列表 / Executor list</param>
    /// <param name="compilation">任务编译结果 / Task compilation result</param>
    /// <param name="taskTime">任务时间对象 / Task time object</param>
    public TaskSchedulingSwitch(
        SolverTimeWindowBoundary timeBoundary,
        IReadOnlyList<T> tasks,
        IReadOnlyList<E> executors,
        TaskCompilation<T, E, A> compilation,
        TaskTime? taskTime = null)
        : this(timeBoundary.Source, tasks, executors, compilation, taskTime) {
    }

    /// <summary>任务列表 / Task list</summary>
    public IReadOnlyList<T> Tasks => _tasks;

    /// <summary>执行器列表 / Executor list</summary>
    public IReadOnlyList<E> Executors => _executors;

    /// <inheritdoc/>
    public IReadOnlyDictionary<(E Executor, T From, T To), object>? SwitchTable { get; private set; }

    /// <inheritdoc/>
    public IReadOnlyDictionary<(T From, T To), object>? SwitchTimeTable { get; private set; }

    /// <summary>
    /// 前后序判定表（带任务时间时才有） / The ordering table (only with a task time).
    /// </summary>
    /// <remarks>
    /// `frontOf[from, to] = 1` 当且仅当 `est[from]` **不晚于** `est[to]`。"不晚于"而不是"早于"是关键：相等时两边都是 1，
    /// 于是 `not_between` 会把"三个任务同时开始"这种退化排法判成互相夹在中间——。把 `&lt;=` 写成 `&lt;` 会让相等时两边都是 0，
    /// 换型指示整片消失，而解依旧可行。
    ///
    /// `frontOf[from, to] = 1` exactly when `est[from]` is **not later than** `est[to]`. "Not later" rather than "earlier" is the point: with ties both sides are 1, so
 /// `not_between` treats a degenerate "all three start together" schedule as mutually sandwiched —. Writing `&lt;=` as `&lt;` makes both sides 0 on a tie,
    /// the indicators vanish wholesale, and the solution stays feasible.
    /// </remarks>
    public IReadOnlyDictionary<(T From, T To), object>? FrontOfTable { get; private set; }

    /// <summary>注册切换到模型 / Register switch to model</summary>
    /// <param name="model">元模型 / Meta model</param>
    /// <returns>操作结果 / Operation result</returns>
    /// <remarks>
    /// 这里只实现**没有任务时间**的那一支（`taskTime == null` 分支）：换型指示 = 两个分配列同时为 1，
    /// 且 `task1` 计划上紧跟在 `task2` 之前。有任务时间的那一支要用 `frontOf` / `betweenIn` 两层 `IfFunction` / `AndFunction`
    /// 组合，属于独立一批的量——**所以它显式失败**，而不是悄悄退化成无时间分支（那会假设计划顺序就是真实顺序）。
    ///
 /// This implements only the **no-task-time** branch (`taskTime == null` path): the indicator is "both assignment columns are 1 and `task1`
    /// immediately precedes `task2` in the plan". The task-time branch needs `frontOf` / `betweenIn` built from two layers of `IfFunction` / `AndFunction` and is a batch of its
    /// own — **so it fails explicitly** instead of quietly degrading to the no-time branch (which would assume the planned order is the real order).
    /// </remarks>
    public Try Register(object model) {
        ArgumentNullException.ThrowIfNull(model);

        if (model is not IAbstractLinearMetaModel<Flt64> linear) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "换型只能注册到线性元模型 / a switch can only be registered into a linear meta model"));
        }

        // 分配列**只在对编译注册之后**才存在；先建换型会让循环里抛 `InvalidOperationException`，
        // 那是一句"任务没有分配列"的运行时异常，而调用方需要的是"你还没注册编译"。
        // Assignment columns only exist **after the compilation is registered**; building the switch first throws `InvalidOperationException` from inside the loops — a runtime
        // complaint about a missing column, when the caller needs to be told "the compilation is not registered yet".
        foreach (T task in _tasks) {
            foreach (E executor in _executors) {
                // 只看"键在不在"不够：构造期表里放的是**占位对象**，键一直都在。要问的是"它是不是真符号"。
                // Key presence is not enough: at construction the table holds **placeholder objects** and the keys are all there. The real question is "is it a symbol".
                if (!_compilation.TaskAssignment.TryGetValue((task, executor), out object? column)
                    || column is not ILinearIntermediateSymbol<Flt64>) {
                    return Results.Failed<Success>(new Err<ErrorCode>(
                        ErrorCode.ApplicationError,
                        "编译尚未注册，取不到任务的分配列：" + task.Id + "@" + executor.Id
                        + " / the compilation has not been registered, so the task's assignment column is unavailable: " + task.Id + "@" + executor.Id));
                }
            }
        }

        // 有任务时间时，顺序由 `est` 决定而不是由计划决定，先建两张判定表。
        // With a task time the order is decided by `est` rather than by the plan, so the two decision tables come first.
        _frontOf = new Dictionary<(T, T), object>();
        _betweenIn = new Dictionary<(T, T, T), object>();
        if (_taskTime is not null) {
            // 有任务时间就必须先有它的两张表，否则后面 `EstimateStartOf` 会抛运行时异常。
            // A task time requires its two tables first, or `EstimateStartOf` throws later.
            if (_taskTime is not IEstimateStartTime<T> { EstimateStartTime: not null }
                || _taskTime is not IEstimateEndTime<T> { EstimateEndTime: not null }) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ApplicationError,
                    "任务时间尚未注册开始/结束时间表，换型顺序无从判定"
                    + " / the task time has not registered its start/end time tables, so the switch order cannot be decided"));
            }
            Try built = BuildFrontOf(linear);
            if (!built.IsOk) {
                return built;
            }
            built = BuildBetweenIn(linear);
            if (!built.IsOk) {
                return built;
            }
        }

        var switchTable = new Dictionary<(E, T, T), object>();
        foreach (E executor in _executors) {
            foreach (T from in _tasks) {
                foreach (T to in _tasks) {
                    switchTable[(executor, from, to)] = BuildSwitchSymbol(linear, executor, from, to);
                }
            }
        }

        var timeTable = new Dictionary<(T, T), object>();
        foreach (T from in _tasks) {
            foreach (T to in _tasks) {
                timeTable[(from, to)] = BuildSwitchTimeSymbol(linear, switchTable, from, to);
            }
        }

        SwitchTable = switchTable;
        SwitchTimeTable = timeTable;
        FrontOfTable = _taskTime is null ? null : _frontOf;
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 造一条换型指示 / Build one switch indicator.
    /// </summary>
    /// <remarks>
    /// 三种情况：
    /// <list type="number">
    /// <item>`from == to` → **常量零**（落在下一条里：开始时间严格早于自己不可能成立）。`switch` 里对对角线返回 `empty`，
    ///   而在 `SwitchCostTriples` 里**不跳过**对角线——两件事并不矛盾：对角线**参与求和**，只是它的值是零。</item>
    /// <item>`from` 在计划上不是紧跟 `to` → 常量零。"紧跟"= `from` 的开始严格早于 `to`，且没有第三个任务的开始时间落在两者之间。</item>
    /// <item>否则 → `AND(分配[执行器, from], 分配[执行器, to])`。</item>
    /// </list>
    ///
 /// Three cases: `from == to` → **constant zero** (this implementation returns `empty` for the diagonal in `switch` while `SwitchCostTriples` **does not** skip it — no contradiction:
    /// the diagonal takes part in the sum, its value is just zero); `from` does not immediately precede `to` in the plan → constant zero ("immediately" means `from` starts
    /// strictly earlier than `to` and no third task starts strictly in between); otherwise → `AND(assignment[executor, from], assignment[executor, to])`.
    /// </remarks>
    private object BuildSwitchSymbol(IAbstractLinearMetaModel<Flt64> linear, E executor, T from, T to) {
        string name = "switch_" + executor.Id + "_" + from.Id + "_" + to.Id;
        // 对角线**不需要单独一支**：`ImmediatelyPrecedes` 要求开始时间严格早于，自己不可能早于自己。同时也不能把"严格"改成
        // "不晚于"——那时对角线会变成一个真的 `AND`，`switch[e, t, t]` 变成 1，同机器上的每个任务都多罚一次换型。
        //
        // The diagonal needs **no separate branch**: `ImmediatelyPrecedes` requires a strictly earlier start and a task cannot precede itself. Nor may "strictly" become
        // "no later" — then the diagonal turns into a real `AND`, `switch[e, t, t]` becomes 1 and every task on a machine is charged one extra switch.
        if (_taskTime is null && !ImmediatelyPrecedes(from, to)) {
            return Constant(linear, name);
        }
        var conditions = new List<LinearPolynomial<Flt64>> {
            AssignmentOf(executor, from),
            AssignmentOf(executor, to),
        };
        if (_taskTime is not null) {
            // 顺序由 `est` 决定：`frontOf[from, to]` 说"from 不晚于 to"，再加上"没有第三个任务夹在中间"。
            // The order comes from `est`: `frontOf[from, to]` says "from is not later than to", plus "no third task sits in between".
            if (ReferenceEquals(from, to)) {
                return Constant(linear, name);
            }
            conditions.Add(PolynomialOf(_frontOf[(from, to)]));
            foreach (T middle in _tasks) {
                if (ReferenceEquals(middle, from) || ReferenceEquals(middle, to)) {
                    continue;
                }
                // `not_between = 1 - between_in`：夹在中间为真时这一项为 0，`AND` 就把这次换型否掉。
                // `not_between = 1 - between_in`: when a third task sits in between this term is 0 and the `AND` rejects the switch.
                LinearPolynomial<Flt64> between = PolynomialOf(_betweenIn[(middle, from, to)]);
                conditions.Add(new LinearPolynomial<Flt64>(
                    between.Monomials
                        .Select(monomial => new LinearMonomial<Flt64>(monomial.Coefficient.Negate(), monomial.Symbol))
                        .ToArray(),
                    Flt64.One - between.Constant));
            }
        }
        var and = new AndFunction(conditions, name: name);
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(and);
        // 注册失败由调用方（`Register`）在下一层看到——这里没有 `Try` 通道，所以把失败吞掉会让模型缺一条换型指示。
        // A registration failure would surface one level up; swallowing it here would leave the model missing a switch indicator, so the adapter is returned and `Register` adds it.
        linear.Add(adapter);
        return adapter;
    }

    /// <summary>`from` 在计划上是否紧跟 `to` / Whether `from` immediately precedes `to` in the plan.</summary>
    private bool ImmediatelyPrecedes(T from, T to) {
        if (from.Time is not { } left || to.Time is not { } right) {
            return false;
        }
        if (!(left.Start < right.Start)) {
            return false;
        }
        foreach (T middle in _tasks) {
            if (ReferenceEquals(middle, from) || ReferenceEquals(middle, to)) {
                continue;
            }
            if (middle.Time is not { } time) {
                continue;
            }
            if (left.Start < time.Start && time.Start < right.Start) {
                return false;
            }
        }
        return true;
    }

    /// <summary>取任务的分配列 / Read a task's assignment column.</summary>
    private LinearPolynomial<Flt64> AssignmentOf(E executor, T task) {
        if (!_compilation.TaskAssignment.TryGetValue((task, executor), out object? symbol)) {
            throw new InvalidOperationException(
                "任务没有分配列：" + task.Id + " / the task has no assignment column: " + task.Id);
        }
        return ((ILinearIntermediateSymbol<Flt64>)symbol).Polynomial;
    }

    /// <summary>
    /// 造一条换型时间 / Build one switch time.
    /// </summary>
    /// <remarks>
    /// 换型时间 = **间隔 × 换型指示**，所以走 `MaskingWithPolyMaskFunction`：掩码是多台机器的换型指示之和
    /// （任一机器停在这一对之间就算换型）。直接用间隔当时间会让"没有换型"也计入时间。
    ///
    /// Switch time = **gap × indicator**, hence `MaskingWithPolyMaskFunction`: the mask is the sum of the per-executor indicators (whichever machine sits on this pair
    /// contributes). Using the gap alone would charge time even when nothing switches.
    /// </remarks>
    private object BuildSwitchTimeSymbol(
        IAbstractLinearMetaModel<Flt64> linear,
        IReadOnlyDictionary<(E, T, T), object> switchTable,
        T from,
        T to) {
        string name = "switch_time_" + from.Id + "_" + to.Id;
        var maskPolynomial = new MutableLinearPolynomial<Flt64>(
            new List<LinearMonomial<Flt64>>(),
            Flt64.Zero);
        foreach (E executor in _executors) {
            if (switchTable[(executor, from, to)] is ILinearIntermediateSymbol<Flt64> indicator) {
                maskPolynomial.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, indicator));
            }
        }
        LinearPolynomial<Flt64> input;
        Flt64? bigM = null;
        if (_taskTime is not null) {
            // 有任务时间时 `x = est[to] - est_end[from]`：**结束到开始**的差才是"两个任务之间的空档"。
            // With a task time `x = est[to] - est_end[from]`: the **end-to-start** gap is what sits between two tasks.
            LinearPolynomial<Flt64> startOfTo = PolynomialOf(EstimateStartOf(to));
            LinearPolynomial<Flt64> endOfFrom = PolynomialOf(EstimateEndOf(from));
            input = new LinearPolynomial<Flt64>(
                startOfTo.Monomials
                    .Select(monomial => new LinearMonomial<Flt64>(monomial.Coefficient, monomial.Symbol))
                    .Concat(endOfFrom.Monomials.Select(monomial =>
                        new LinearMonomial<Flt64>(monomial.Coefficient.Negate(), monomial.Symbol)))
                    .ToArray(),
                startOfTo.Constant - endOfFrom.Constant);
        } else {
            if (from.Time is not { } left || to.Time is not { } right) {
                return Constant(linear, name);
            }
            Flt64 gap = _timeBoundary.DistanceValue(left.End, right.Start);
            input = new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), gap);
            // 常量输入没有"读不回来的值域"问题，Big-M 可以钉死成 `|间隔| + 1`；est 之差不能（`Range` 写入读不回来）。
            // A constant input has no unreadable range, so the Big-M can be pinned to `|gap| + 1`; the est difference cannot (its range writes are unreadable).
            bigM = gap.Abs() + Flt64.One;
        }
        var masked = new MaskingWithPolyMaskFunction<Flt64>(
            input: input,
            maskPolynomial: new LinearPolynomial<Flt64>(maskPolynomial.Monomials.ToArray(), maskPolynomial.Constant),
            converter: Fuookami.Ospf.Core.Solver.Value.IIntoValue<Flt64>.Identity,
            bigM: bigM,
            name: name);
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(masked);
        linear.Add(adapter);
        return adapter;
    }

    /// <summary>造一个常量零符号 / Build a constant-zero symbol.</summary>
    private static LinearExpressionSymbol Constant(IAbstractLinearMetaModel<Flt64> linear, string name) {
        var symbol = new LinearExpressionSymbol(
            new MutableLinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero),
            name: name);
        linear.Add(symbol);
        return symbol;
    }

    /// <summary>造 `frontOf` 表 / Build the `frontOf` table.</summary>
    /// <remarks>
    /// `frontOf[from, to] = 1` 当且仅当 `est[from]` 不晚于 `est[to]`。它写成 `IfFunction` 包住一条不等式，
    /// 于是"谁在前"变成一个**决策变量**而不是一个静态判断——这正是允许提前/延迟之后必须做的事。
    ///
 /// `frontOf[from, to] = 1` exactly when `est[from]` is not later than `est[to]`. The implementation wraps an inequality in an `IfFunction`, turning "who comes first" into a
    /// **decision variable** rather than a static test — exactly what advance/delay makes necessary.
    ///
    /// **这里 ceiling**：`estimateStartTime[task1].range.range!!` 当左侧值域传给约束输入，而 C# 里
    /// `LinearExpressionSymbol.Range` 的写入**读不回来**（既有 ceiling），所以这里只能传全域。后果是 `IfFunction`
    /// 少一层收紧，不影响正确性，只影响松弛强度。
    ///
 /// **Ceiling**: the implementation passes `estimateStartTime[task1].range.range!!` as the left-hand range, but in C# the writes to `LinearExpressionSymbol.Range` **cannot be read
    /// back** (an existing ceiling), so the full range is passed. The `IfFunction` loses one layer of tightening — no effect on correctness, only on relaxation strength.
    /// </remarks>
    private Try BuildFrontOf(IAbstractLinearMetaModel<Flt64> linear) {
        foreach (T from in _tasks) {
            foreach (T to in _tasks) {
                string name = "front_of_" + from.Id + "_" + to.Id;
                if (ReferenceEquals(from, to)) {
                    _frontOf[(from, to)] = Constant(linear, name);
                    continue;
                }
                LinearPolynomial<Flt64> startOfFrom = PolynomialOf(EstimateStartOf(from));
                LinearPolynomial<Flt64> startOfTo = PolynomialOf(EstimateStartOf(to));
                var condition = new LinearPolynomial<Flt64>(
                    startOfFrom.Monomials
                        .Select(monomial => new LinearMonomial<Flt64>(monomial.Coefficient, monomial.Symbol))
                        .Concat(startOfTo.Monomials.Select(monomial =>
                            new LinearMonomial<Flt64>(monomial.Coefficient.Negate(), monomial.Symbol)))
                        .ToArray(),
                    startOfFrom.Constant - startOfTo.Constant);
                var inequality = new LinearInequality<Flt64>(
                    condition,
                    new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero),
                    Comparison.LE,
                    name);
                Fuookami.Ospf.Utils.Functional.Result<LinearConstraintInput<Flt64>, ErrorCode, Error<ErrorCode>> input = LinearConstraintInput<Flt64>.From(
                    inequality,
                    ValueRange<Flt64>.Full(),
                    Flt64.Zero,
                    name);
                if (!input.IsOk) {
                    return Results.Failed<Success>(new Err<ErrorCode>(
                        ErrorCode.ApplicationError,
                        "造不出前后序判定的约束输入：" + name
                        + " / the ordering constraint input could not be built: " + name));
                }
                Fuookami.Ospf.Utils.Functional.Result<LinearFunctionSymbolAdapter<Flt64>, ErrorCode, Error<ErrorCode>> function = IfFunction<Flt64>.From(
                    input.Value,
                    IIntoValue<Flt64>.Identity,
                    StrictBoundary,
                    name: name);
                if (!function.IsOk) {
                    return Results.Failed<Success>(new Err<ErrorCode>(
                        ErrorCode.ApplicationError,
                        "造不出前后序判定函数：" + name
                        + " / the ordering function could not be built: " + name));
                }
                Try added = linear.Add(function.Value);
                if (!added.IsOk) {
                    return added;
                }
                _frontOf[(from, to)] = function.Value;
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>造 `betweenIn` 表 / Build the `betweenIn` table.</summary>
    /// <remarks>
    /// `betweenIn[middle, from, to] = frontOf[from, middle] AND frontOf[middle, to]`：`middle` 正好夹在两者之间。
    ///
    /// **一处越界**：`betweenIn` 是 3 维，但它的构造 lambda 读的是 `v[1] / v[2] / v[3]`（`v` 只有 0..2）。
    /// 语义（以及它自己的命名 `between_in_{task3}_{task1}_{task2}`）指向 `v[0]`，即"中间那个任务"，这里按**语义**实现并记录。
    ///
    /// **An out-of-range access**: `betweenIn` is 3-dimensional, and reading `v[1] / v[2] / v[3]` (with `v` holding only 0..2) goes out of range. Both the intent and its own
    /// naming `between_in_{task3}_{task1}_{task2}` point at `v[0]`, the middle task, so this follows the **intent** and records the deviation.
    /// </remarks>
    private Try BuildBetweenIn(IAbstractLinearMetaModel<Flt64> linear) {
        foreach (T middle in _tasks) {
            foreach (T from in _tasks) {
                foreach (T to in _tasks) {
                    string name = "between_in_" + middle.Id + "_" + from.Id + "_" + to.Id;
                    if (ReferenceEquals(from, middle) || ReferenceEquals(from, to) || ReferenceEquals(middle, to)) {
                        _betweenIn[(middle, from, to)] = Constant(linear, name);
                        continue;
                    }
                    var and = new AndFunction(
                        new[] { PolynomialOf(_frontOf[(from, middle)]), PolynomialOf(_frontOf[(middle, to)]) },
                        name: name);
                    var adapter = new LinearFunctionSymbolAdapter<Flt64>(and);
                    Try added = linear.Add(adapter);
                    if (!added.IsOk) {
                        return added;
                    }
                    _betweenIn[(middle, from, to)] = adapter;
                }
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>`IfFunction` 的正负分支间隔 / The true/false gap of an `IfFunction`.</summary>
    private static readonly Flt64 StrictBoundary = new Flt64(1e-9);

    /// <summary>读一条符号的多项式 / Read a symbol's polynomial.</summary>
    private static LinearPolynomial<Flt64> PolynomialOf(object symbol) =>
        ((ILinearIntermediateSymbol<Flt64>)symbol).Polynomial;

    /// <summary>读任务的预估开始时间符号 / Read a task's estimated start-time symbol.</summary>
    private object EstimateStartOf(T task) {
        if (_taskTime is not IEstimateStartTime<T> { EstimateStartTime: IReadOnlyDictionary<T, object> starts }
            || !starts.TryGetValue(task, out object? symbol)) {
            throw new InvalidOperationException(
                "任务时间尚未注册预估开始时间：" + task.Id
                + " / the task time has not registered its estimated start times: " + task.Id);
        }
        return symbol;
    }

    /// <summary>读任务的预估结束时间符号 / Read a task's estimated end-time symbol.</summary>
    private object EstimateEndOf(T task) {
        if (_taskTime is not IEstimateEndTime<T> { EstimateEndTime: IReadOnlyDictionary<T, object> ends }
            || !ends.TryGetValue(task, out object? symbol)) {
            throw new InvalidOperationException(
                "任务时间尚未注册预估结束时间：" + task.Id
                + " / the task time has not registered its estimated end times: " + task.Id);
        }
        return symbol;
    }
}
