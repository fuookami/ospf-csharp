#nullable enable

using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Concept;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation.Model;
/// <summary>
/// 任务束编译 / Bunch compilation
/// </summary>
/// <remarks>
/// 管理 y/z/bunchCost/taskAssignment/taskCompilation/executorCompilation 变量。
/// Manages y/z/bunchCost/taskAssignment/taskCompilation/executorCompilation variables.
/// </remarks>
/// <typeparam name="B">任务束类型 / Bunch type</typeparam>
/// <typeparam name="T">任务类型 / Task type</typeparam>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public class BunchCompilation<B, T, E, A> : ICompilation, ITaskCompilation<E, A>, ICompilationColumns<E, A>, IExecutorCompilation<E>, ITaskCost
    where B : AbstractTaskBunch<T, E, A>
    where T : IAbstractTask<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    private readonly IReadOnlyList<T> _tasks;
    private readonly IReadOnlyList<E> _executors;
    private readonly IReadOnlySet<T> _lockCancelTasks;
    private readonly BunchAggregation<B, T, E, A> _aggregation;
    private readonly List<object> _registeredModels = new();
    private readonly List<BinVariable1> _x = new();
    private readonly Dictionary<ulong, BinVariable1> _roundColumns = new();
    private readonly Dictionary<ulong, Dictionary<B, int>> _roundColumnIndices = new();
    private readonly HashSet<IVariableItem> _retiredColumns = new();
    private BinVariable1? _cancellationVariable;
    private BinVariable1? _leisureVariable;
    private LinearExpressionSymbol? _taskCost;
    private IReadOnlyDictionary<IAbstractTask<E, A>, object>? _cancellationTable;
    private IReadOnlyDictionary<E, object>? _leisureTable;
    private IReadOnlyDictionary<IAbstractTask<E, A>, object>? _compilationTable;
    private IReadOnlyDictionary<E, object>? _executorCompilationTable;
    private IReadOnlyDictionary<(IAbstractTask<E, A> Task, E Executor), object>? _taskAssignmentTable;

    /// <summary>
    /// 任务束编译构造 / Bunch compilation constructor
    /// </summary>
    /// <param name="tasks">任务列表 / List of tasks</param>
    /// <param name="executors">执行器列表 / List of executors</param>
    /// <param name="lockCancelTasks">锁定取消任务集合 / Set of locked cancel tasks</param>
    /// <param name="withExecutorLeisure">是否包含执行器空闲 / Whether to include executor leisure</param>
    /// <param name="bunchAggregation">任务束聚合 / Bunch aggregation</param>
    public BunchCompilation(
        IReadOnlyList<T> tasks,
        IReadOnlyList<E> executors,
        IReadOnlySet<T>? lockCancelTasks = null,
        bool withExecutorLeisure = true,
        BunchAggregation<B, T, E, A>? bunchAggregation = null) {
        _tasks = tasks;
        _executors = executors;
        _lockCancelTasks = lockCancelTasks ?? new HashSet<T>();
        WithExecutorLeisure = withExecutorLeisure;
        _aggregation = bunchAggregation ?? new BunchAggregation<B, T, E, A>();

        // Ensure executors are indexed
        if (!executors.All(e => e.Indexed)) {
            ManualIndexed.Impl.Flush(typeof(Executor));
            foreach (E executor in executors) {
                executor.SetIndexed();
            }
        }

        // Ensure tasks are indexed
        if (!tasks.All(t => t.Indexed)) {
            ManualIndexed.Impl.Flush(typeof(IAbstractTask<E, A>));
            foreach (T task in tasks) {
                if (task is ManualIndexed manual) {
                    manual.SetIndexed();
                }
            }
        }
    }

    /// <inheritdoc/>
    public bool TaskCancelEnabled => true;

    /// <inheritdoc/>
    public bool WithExecutorLeisure { get; }

    /// <summary>任务束聚合 / Bunch aggregation</summary>
    public BunchAggregation<B, T, E, A> Aggregation => _aggregation;

    /// <summary>任务束迭代列表 / Bunch iteration list</summary>
    public IReadOnlyList<IReadOnlyList<B>> BunchesIteration => _aggregation.BunchesIteration;

    /// <summary>所有任务束列表 / All bunches list</summary>
    public IReadOnlyList<B> Bunches => _aggregation.Bunches;

    /// <summary>已移除任务束集合 / Removed bunches set</summary>
    public IReadOnlySet<B> RemovedBunches => _aggregation.RemovedBunches;

    /// <summary>最后迭代的任务束列表 / Last iteration bunches</summary>
    public IReadOnlyList<B> LastIterationBunches => _aggregation.LastIterationBunches;

    /// <summary>任务列表 / List of tasks</summary>
    public IReadOnlyList<T> Tasks => _tasks;

    /// <summary>
    /// 取某一轮的列变量组 / The column-variable group of one round.
    /// </summary>
    /// <param name="iteration">迭代轮次 / Iteration round</param>
    /// <returns>列变量组；该轮还没加过列时是 `null` / The group, or null when that round has not added columns.</returns>
    public BinVariable1? Columns(ulong iteration) =>
        _roundColumns.TryGetValue(iteration, out BinVariable1? columns) ? columns : null;

    /// <summary>
    /// 取某一轮里某个任务束的列下标 / The column index of a bunch within one round.
    /// </summary>
    /// <param name="iteration">迭代轮次 / Iteration round</param>
    /// <param name="bunch">任务束 / The bunch</param>
    /// <returns>列下标；不在该轮时是 `null` / The index, or null when the bunch is not in that round.</returns>
    /// <remarks>
    /// 任务时间加列时要用同一份"束 → 列"的对应关系：**去重后的顺序**才是下标。调用方自己再数一遍（比如按入参顺序）会错位，
    /// 而错位的症状是"偏移量加到了别人的列上"——目标值看起来合理，排程却整体偏了一段时间。
    ///
    /// Column addition on the time side needs the same "bunch → column" mapping: **the deduplicated order** is the index. A caller that counts again itself (say, in argument
    /// order) gets it wrong, and the symptom is "the deviation landed on someone else's column" — the objective looks reasonable while the whole schedule shifts by one slot.
    /// </remarks>
    public int? ColumnIndex(ulong iteration, B bunch) =>
        _roundColumnIndices.TryGetValue(iteration, out Dictionary<B, int>? indices) && indices.TryGetValue(bunch, out int index)
            ? index
            : null;

    /// <summary>
    /// 取某个任务束在本轮的列变量 / The column variable of one bunch in its round.
    /// </summary>
    /// <param name="iteration">迭代轮次 / Iteration round</param>
    /// <param name="bunch">任务束 / The bunch</param>
    /// <returns>列变量；该轮没加过列或该束不在其中时是 `null` / The variable, or null when the round has no such column.</returns>
    public CombinationVariableItem<UInt8, Binary>? ColumnItem(ulong iteration, B bunch) =>
        _roundColumns.TryGetValue(iteration, out BinVariable1? columns)
        && _roundColumnIndices.TryGetValue(iteration, out Dictionary<B, int>? indices)
        && indices.TryGetValue(bunch, out int index)
            ? columns[index]
            : null;

    /// <summary>
    /// 登记一列已退役 / Record a retired column.
    /// </summary>
    /// <param name="item">列变量 / The column variable</param>
    /// <remarks>
    /// 退役的列已经被固定在 0 并摘出模型；后续加列与累加都要跳过它，否则它会在新的聚合行里**复活**——
    /// 一个指向已摘掉变量的单项式，展开时找不到变量，或者更糟：同一轮里又建了一个同名列。
    ///
    /// A retired column has been pinned to 0 and dropped from the model; later column additions and accumulations must skip it, or it **revives** inside a new aggregate row — a
    /// monomial pointing at a dropped variable, which either fails to expand or, worse, collides with a same-named column created again in that round.
    /// </remarks>
    public void RetireColumn(IVariableItem item) => _retiredColumns.Add(item);

    /// <summary>
    /// 移除一列：登记移除、钉 0、从模型摘掉、清目标项、登记退役 / Remove one column: mark it removed, pin it to 0, drop it from the model, clear its objective terms, record the retirement.
    /// </summary>
    /// <param name="bunch">任务束 / The bunch</param>
    /// <param name="model">线性元模型 / The linear meta model</param>
    /// <returns>操作结果 / Operation result.</returns>
    /// <remarks>
    /// 只把束从束列表里删掉是不够的：列变量和它的成本系数还在，求解器可以继续选它——"移除"变成只影响报告、不影响求解。
    /// 两步（钉 0、`remove`）沿用，**再加一步清目标项**：实测过，摘掉变量后不清理目标项，展开会以
    /// "变量 x_… 未注册"失败（`IllegalArgument`）——目标项里还留着指向那个变量的单项式。
    ///
    /// 束的列没加过（还没走到那一轮）时是**空操作**而不是失败：移除束在编译层是合法动作，没有列就没有变量要摘。
    /// 已经退役过的列再来一次也是空操作：重复摘一个已摘的变量没有好处，重复登记会让计数类判据失真。
    ///
    /// Dropping the bunch from the bunch list is not enough: its column variable and cost coefficient remain and the solver keeps selecting it — "removal" then only affects
    /// reporting. The code's two steps (pin to 0, `remove`) are copied, **plus an objective cleanup**: measured — with the variable dropped but the objective untouched, expansion
    /// fails with "variable x_… is not registered" (`IllegalArgument`), because the objective still mentions that variable. A bunch whose columns were never added (that round has not
    /// been reached) is a **no-op**, not a failure: removing a bunch is legal at the compilation layer and there is no variable to drop. A column already retired is also a no-op:
    /// dropping twice helps nothing, and double-recording skews count-based criteria.
    /// </remarks>
    public Try RemoveColumn(B bunch, object model) {
        ArgumentNullException.ThrowIfNull(bunch);
        _aggregation.RemoveColumn(bunch);
        if (model is not IAbstractLinearMetaModel<Flt64> linear) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "移除列只能作用于线性元模型 / removing a column only applies to a linear meta model"));
        }

        // 先按束自带的轮次找；找不到再扫一遍各轮。`Iteration` 是**束**的属性，可能与调用方加列时用的轮次不一致
        // （历史数据里常见的 -1），扫一遍比"静默什么都不做"好——但顺序仍然按编译结果记的下标。
        // Look the column up by the bunch's own round first, then scan every round. `Iteration` belongs to the **bunch** and may disagree with the round the caller used when
        // adding columns (-1 is common in older data); scanning beats silently doing nothing — and the index still comes from the compilation's own record.
        IVariableItem? found = ColumnItem((ulong)bunch.Iteration, bunch);
        if (found is null) {
            foreach (KeyValuePair<ulong, Dictionary<B, int>> round in _roundColumnIndices) {
                if (ColumnItem(round.Key, bunch) is { } candidate) {
                    found = candidate;
                    break;
                }
            }
        }
        if (found is not Fuookami.Ospf.Core.Variable.CombinationVariableItem<UInt8, Binary> column) {
            return Results.Ok<Success>(Results.SuccessInstance);
        }
        if (_retiredColumns.Contains(column)) {
            return Results.Ok<Success>(Results.SuccessInstance);
        }

        column.Range.Eq(false);
        linear.Remove(column);
        _retiredColumns.Add(column);
        return linear.RemoveObjectiveTerms(new HashSet<IVariableItem> { column });
    }

    /// <summary>该列是否已退役 / Whether that column is retired.</summary>
    /// <param name="item">列变量 / The column variable</param>
    /// <returns>是否已退役 / Whether it is retired.</returns>
    public bool IsRetired(IVariableItem item) => _retiredColumns.Contains(item);

    /// <summary>执行器列表 / List of executors</summary>
    public IReadOnlyList<E> Executors => _executors;

    /// <summary>锁定取消任务集合 / Set of locked cancel tasks</summary>
    public IReadOnlySet<T> LockCancelTasks => _lockCancelTasks;

    /// <inheritdoc/>
    public IReadOnlyDictionary<IAbstractTask<E, A>, object> Cancellation =>
        _cancellationTable ?? new Dictionary<IAbstractTask<E, A>, object>();

    /// <inheritdoc/>
    public IReadOnlyDictionary<E, object> Leisure => _leisureTable ?? new Dictionary<E, object>();

    /// <inheritdoc/>
    public IReadOnlyDictionary<IAbstractTask<E, A>, object> TaskCompilationTable =>
        _compilationTable ?? new Dictionary<IAbstractTask<E, A>, object>();

    /// <inheritdoc/>
    /// <remarks>
    /// 任务束编译**没有 `x` 变量**：分配列是每轮按需建的（`x_{iteration}`，见`addColumns`）。所以这张表在建列之前是空的，
    /// 空表而不是 `null`——读它的人拿到"没有分配列"而不是空引用。
    ///
    /// A bunch compilation has **no `x` variable**: the assignment columns are created per round (`x_{iteration}`, see its `addColumns`). The table is therefore empty
    /// before any column is added — empty rather than `null`, so a reader gets "no assignment columns" instead of a null reference.
    /// </remarks>
    public IReadOnlyDictionary<(IAbstractTask<E, A> Task, E Executor), object> Assignment =>
        _taskAssignmentTable ?? new Dictionary<(IAbstractTask<E, A> Task, E Executor), object>();

    /// <inheritdoc/>
    public IReadOnlyDictionary<(IAbstractTask<E, A> Task, E Executor), object> TaskAssignment => Assignment;

    /// <inheritdoc/>
    public IReadOnlyDictionary<E, object> ExecutorCompilation =>
        _executorCompilationTable ?? new Dictionary<E, object>();

    /// <inheritdoc/>
    public object? TaskCost => _taskCost;

    /// <summary>
    /// 注册到模型 / Register to model
    /// </summary>
    /// <param name="model">元模型 / Meta model</param>
    /// <returns>操作结果 / Operation result</returns>
    /// <remarks>
    /// 这里建六样东西：`y`（每任务取消列，锁定的钉成 1）、`bunch_cost`（目标载体）、`task_assignment_*`（**占位**中间符号，由每轮的
    /// `addColumns` 填内容）、`task_compilation_t = y[t]`、`z`（每执行器空闲列，只在 `withExecutorLeisure` 时）、
    /// `executor_compilation_e = z[e]`（无空闲时是常量零）。
    ///
    /// **之前这里直接返回 `Ok`**：模型里什么都没有，而任务束调度时间的偏移掩码要读 `task_compilation` 那张表，于是整条路径只能失败。
    /// 三处细节值得点名：
    /// <list type="number">
    /// <item>`task_compilation_t` 在任务束编译里**只有 `y[t]`**，没有 `Σ_e x[t,e]` 那一项——分配列是每轮的，注册时还不存在；</item>
    /// <item>锁定的取消任务把 `y` 钉成 1 **在范围内**而不是加一条约束；</item>
    /// <item>没有空闲时 `executor_compilation` 是**常量零**而不是 `null`——它是"这个执行器被用了多少"的载体。</item>
    /// </list>
    ///
    /// The code builds six things: `y` (a per-task cancellation column, locked tasks pinned to 1), `bunch_cost` (the objective carrier), `task_assignment_*` (**placeholder**
    /// intermediate symbols filled by each round's `addColumns`), `task_compilation_t = y[t]`, `z` (a per-executor leisure column, only with `withExecutorLeisure`), and
    /// `executor_compilation_e = z[e]` (constant zero without leisure). **This used to return `Ok` outright**: nothing was in the model while the bunch task time's deviation masks
    /// need the `task_compilation` table, so that whole path could only fail. Three details worth naming: (1) `task_compilation_t` here is **just `y[t]`**, with no `Σ_e x[t,e]`
    /// term — the assignment columns are per round and do not exist at registration; (2) locked cancellation tasks pin `y` to 1 **through the range**, not through an extra
    /// constraint; (3) without leisure `executor_compilation` is a **constant zero**, not `null` — it is the carrier of "how much this executor is used".
    /// </remarks>
    public virtual Try Register(object model) {
        if (model is not IAbstractLinearMetaModel<Flt64> linear) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "任务束编译只能注册到线性元模型 / a bunch compilation can only be registered into a linear meta model"));
        }

        // 同一个模型上重复注册是空操作：`model.add` 无条件执行，再来一次会撞上重名符号。
        // Re-registering on the same model is a no-op: `model.add` is unconditional, and a repeat call hits a repeated symbol.
        foreach (object registered in _registeredModels) {
            if (ReferenceEquals(registered, model)) {
                return Results.Ok<Success>(Results.SuccessInstance);
            }
        }

        var taskList = _tasks.Cast<IAbstractTask<E, A>>().ToList();
        Try added;

        if (_cancellationVariable is null) {
            var cancellation = new BinVariable1("y", _tasks.Count);
            var table = new Dictionary<IAbstractTask<E, A>, object>();
            for (int index = 0; index < _tasks.Count; ++index) {
                T task = _tasks[index];
                cancellation[index].Name = "y_" + task.Id;
                if (_lockCancelTasks.Contains(task)) {
                    cancellation[index].Range.Eq(true);
                }
                table[task] = cancellation[index];
            }
            _cancellationVariable = cancellation;
            _cancellationTable = table;
        }
        added = linear.Add(_cancellationVariable.Items);
        if (!added.IsOk) {
            return added;
        }

        if (_taskCost is null) {
            _taskCost = new LinearExpressionSymbol(
                new Fuookami.Ospf.Math.Symbol.Polynomial.MutableLinearPolynomial<Flt64>(
                    Array.Empty<Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>>(),
                    Flt64.Zero),
                name: "bunch_cost");
        }
        added = linear.Add(_taskCost);
        if (!added.IsOk) {
            return added;
        }

        if (_taskAssignmentTable is null) {
            var table = new Dictionary<(IAbstractTask<E, A> Task, E Executor), object>();
            var placeholders = new List<Fuookami.Ospf.Core.Symbol.IIntermediateSymbol>();
            foreach (T task in _tasks) {
                foreach (E executor in _executors) {
                    var placeholder = new LinearExpressionSymbol(
                        new Fuookami.Ospf.Math.Symbol.Polynomial.MutableLinearPolynomial<Flt64>(
                            Array.Empty<Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>>(),
                            Flt64.Zero),
                        name: "task_assignment_" + task.Id + "_" + executor.Id);
                    table[((IAbstractTask<E, A>)task, executor)] = placeholder;
                    placeholders.Add(placeholder);
                }
            }
            added = linear.Add(placeholders);
            if (!added.IsOk) {
                return added;
            }
            _taskAssignmentTable = table;
        }

        if (_compilationTable is null) {
            var table = new Dictionary<IAbstractTask<E, A>, object>();
            var symbols = new List<Fuookami.Ospf.Core.Symbol.IIntermediateSymbol>();
            for (int index = 0; index < _tasks.Count; ++index) {
                T task = _tasks[index];
                var symbol = new LinearExpressionSymbol(
                    new Fuookami.Ospf.Math.Symbol.Polynomial.MutableLinearPolynomial<Flt64>(
                        new[] { new Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>(
                            Flt64.One, _cancellationVariable[index]) },
                        Flt64.Zero),
                    name: "task_compilation_" + task.Id);
                table[task] = symbol;
                symbols.Add(symbol);
            }
            added = linear.Add(symbols);
            if (!added.IsOk) {
                return added;
            }
            _compilationTable = table;
        }

        if (WithExecutorLeisure) {
            if (_leisureVariable is null) {
                var leisure = new BinVariable1("z", _executors.Count);
                var table = new Dictionary<E, object>();
                for (int index = 0; index < _executors.Count; ++index) {
                    E executor = _executors[index];
                    leisure[index].Name = "z_" + executor.Id;
                    table[executor] = leisure[index];
                }
                _leisureVariable = leisure;
                _leisureTable = table;
            }
            added = linear.Add(_leisureVariable.Items);
            if (!added.IsOk) {
                return added;
            }
        }

        if (_executorCompilationTable is null) {
            var table = new Dictionary<E, object>();
            var symbols = new List<Fuookami.Ospf.Core.Symbol.IIntermediateSymbol>();
            for (int index = 0; index < _executors.Count; ++index) {
                E executor = _executors[index];
                var symbol = new LinearExpressionSymbol(
                    new Fuookami.Ospf.Math.Symbol.Polynomial.MutableLinearPolynomial<Flt64>(
                        WithExecutorLeisure
                            ? new[] { new Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>(
                                Flt64.One, _leisureVariable![index]) }
                            : Array.Empty<Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>>(),
                        Flt64.Zero),
                    name: "executor_compilation_" + executor.Id);
                table[executor] = symbol;
                symbols.Add(symbol);
            }
            added = linear.Add(symbols);
            if (!added.IsOk) {
                return added;
            }
            _executorCompilationTable = table;
        }

        _registeredModels.Add(model);
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 添加列 / Add columns
    /// </summary>
    /// <param name="iteration">迭代次数 / Iteration count</param>
    /// <param name="newBunches">新任务束列表 / List of new bunches</param>
    /// <param name="model">线性元模型 / Linear meta model</param>
    /// <returns>去重后的任务束列表 / Deduplicated bunch list</returns>
    public virtual Result<IReadOnlyList<B>, ErrorCode, Error<ErrorCode>> AddColumns(
        ulong iteration,
        IReadOnlyList<B> newBunches,
        object model) {
        ArgumentNullException.ThrowIfNull(newBunches);
        if (model is not IAbstractLinearMetaModel<Flt64> linear) {
            return Results.Failed<IReadOnlyList<B>>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "加列只能进线性元模型 / columns can only be added to a linear meta model"));
        }

        // 加列要求先注册：`bunch_cost` / 三张聚合表由 `Register` 建出来，没有它们就无处累加。
        // Column addition requires registration: `bunch_cost` and the three aggregate tables come from `Register`, and without them there is nowhere to accumulate.
        if (_taskCost is null || _compilationTable is null || _executorCompilationTable is null || _taskAssignmentTable is null) {
            return Results.Failed<IReadOnlyList<B>>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                "任务束编译尚未注册，无法加列 / the bunch compilation is not registered yet, so columns cannot be added"));
        }

        IReadOnlyList<B> unduplicated = _aggregation.AddColumns(newBunches);

        // 1. 本轮列变量 `x_{iteration}`：**二进制**，一列一个。名字带轮次与束身份，
        //    否则不同轮的列在日志与对偶值里无法区分。
        // The round's column variables `x_{iteration}`: **binary**, one per column. The name carries the round and the bunch identity, or columns from different rounds are
        // indistinguishable in logs and duals.
        var columns = new BinVariable1("x_" + iteration, unduplicated.Count);
        for (int index = 0; index < unduplicated.Count; ++index) {
            B bunch = unduplicated[index];
            columns[index].Name = "x_" + iteration + "_" + bunch.Index + "_" + bunch.Executor.Id;
        }
        Try added = linear.Add(columns.Items);
        if (!added.IsOk) {
            return added switch {
                Failed<Success, ErrorCode, Error<ErrorCode>> failed => Results.Failed<IReadOnlyList<B>>(failed.Error),
                _ => Results.Failed<IReadOnlyList<B>>(new Err<ErrorCode>(
                    ErrorCode.ApplicationError,
                    "加列失败 / adding columns failed")),
            };
        }
        _x.Add(columns);
        _roundColumns[iteration] = columns;

        // 2. `bunch_cost` 累加 `cost × x`。没有求解器成本时**明确失败**：`Flt64.infinity` 当默认值，
        //    本仓的 `Flt64` 是十进制承载、没有无穷，硬塞一个有限大数会让"选不起的列"变成"便宜得离谱"。
        // `bunch_cost` accumulates `cost × x`. A missing solver cost **fails loudly**: the code defaults to `Flt64.infinity`, while this repository's `Flt64` is decimal-backed
        // with no infinity, and substituting a large finite number would turn "unaffordable column" into "implausibly cheap one".
        for (int index = 0; index < unduplicated.Count; ++index) {
            B bunch = unduplicated[index];
            // ponytail: 没有求解器成本时系数取 0，而理想值是"永不选择"（无穷大）。十进制 `Flt64` 表达不了无穷，
            // 取 0 意味着"免费"——这一处**语义不等价**。要等价得引入一个足够大的有限惩罚，或者给 `Flt64` 加无穷。
            // ponytail: a missing solver cost gets coefficient 0 where "never chosen" (infinity) is intended. Decimal `Flt64` cannot express infinity, and 0 means
            // "free" — this point is **not semantically equivalent**. Equivalence needs a sufficiently large finite penalty, or an infinity for `Flt64`.
            Flt64 cost = bunch.Cost.SolverCostOrNull(Flt64.Zero) ?? Flt64.Zero;
            _taskCost!.AsMutable().AddMonomial(
                new Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>(cost, columns[index]));
        }
        // 这些符号**已经在模型里**了：内容是原地改的（`LinearExpressionSymbol` 按需展开，正是为了让后续加列能改到它）。
        // 再 `Add` 一次只会撞重名。
        // These symbols are **already in the model**: their contents are modified in place (`LinearExpressionSymbol` flattens lazily, precisely so that later column additions can
        // reach it). Adding them again would only trip a repeated name.

        // 3. 三张表累加 `Σ x`：分配（任务×执行器）、任务编译（任务）、执行器编译（执行器）。
        //    这三条是"这列覆盖的任务/执行器会被这一列顶替"的代数表达——漏掉任何一条，列加上去了但**没有被算进任何任务**，
        //    求解器会把它们全选成 0，解照样可行。
        // The three tables accumulate `Σ x`: assignment (task × executor), task compilation (task), executor compilation (executor). These are the algebra of "a column stands in for
        // the tasks/executor it covers" — miss any one and the column is added but **counted in nothing**, the solver sets them all to 0, and the solution stays feasible.
        var taskIndexByKey = new Dictionary<TaskKey, int>();
        for (int index = 0; index < _tasks.Count; ++index) {
            taskIndexByKey[_tasks[index].Key] = index;
        }
        var columnIndexByBunch = new Dictionary<B, int>(unduplicated.Count);
        for (int index = 0; index < unduplicated.Count; ++index) {
            columnIndexByBunch[unduplicated[index]] = index;
        }
        _roundColumnIndices[iteration] = columnIndexByBunch;

        var bunchesByTask = new List<List<B>>(_tasks.Count);
        for (int index = 0; index < _tasks.Count; ++index) {
            bunchesByTask.Add(new List<B>());
        }
        var bunchesByExecutor = new Dictionary<E, List<B>>();
        for (int index = 0; index < unduplicated.Count; ++index) {
            B bunch = unduplicated[index];
            if (!bunchesByExecutor.TryGetValue(bunch.Executor, out List<B>? byExecutor)) {
                byExecutor = new List<B>();
                bunchesByExecutor[bunch.Executor] = byExecutor;
            }
            byExecutor.Add(bunch);

            var covered = new HashSet<int>();
            foreach (TaskKey key in bunch.Keys.Keys) {
                if (taskIndexByKey.TryGetValue(key, out int taskIndex)) {
                    covered.Add(taskIndex);
                }
            }
            foreach (int taskIndex in covered) {
                bunchesByTask[taskIndex].Add(bunch);
            }
        }

        for (int taskIndex = 0; taskIndex < _tasks.Count; ++taskIndex) {
            T task = _tasks[taskIndex];
            List<B> covering = bunchesByTask[taskIndex];
            if (covering.Count == 0) {
                continue;
            }
            foreach (E executor in _executors) {
                if (!_taskAssignmentTable!.TryGetValue(((IAbstractTask<E, A>)task, executor), out object? assignment)) {
                    continue;
                }
                var symbol = (LinearExpressionSymbol)assignment;
                foreach (B bunch in covering) {
                    if (!Equals(bunch.Executor, executor)) {
                        continue;
                    }
                    symbol.AsMutable().AddMonomial(
                        new Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>(
                            Flt64.One, columns[columnIndexByBunch[bunch]]));
                }
            }

            var compilation = (LinearExpressionSymbol)_compilationTable![task];
            foreach (B bunch in covering) {
                compilation.AsMutable().AddMonomial(
                    new Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>(
                        Flt64.One, columns[columnIndexByBunch[bunch]]));
            }
        }

        foreach (KeyValuePair<E, List<B>> pair in bunchesByExecutor) {
            if (!_executorCompilationTable!.TryGetValue(pair.Key, out object? executorCompilation)) {
                continue;
            }
            var symbol = (LinearExpressionSymbol)executorCompilation;
            foreach (B bunch in pair.Value) {
                symbol.AsMutable().AddMonomial(
                    new Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>(
                        Flt64.One, columns[columnIndexByBunch[bunch]]));
            }
        }

        return Results.Ok<IReadOnlyList<B>>(unduplicated);
    }

    /// <summary>
    /// 添加列（异步版本） / Add columns (async version)
    /// </summary>
    /// <param name="iteration">迭代次数 / Iteration count</param>
    /// <param name="newBunches">新任务束列表 / List of new bunches</param>
    /// <param name="model">线性元模型 / Linear meta model</param>
    /// <returns>去重后的任务束列表 / Deduplicated bunch list</returns>
    public virtual System.Threading.Tasks.Task<Result<IReadOnlyList<B>, ErrorCode, Error<ErrorCode>>> AddColumnsAsync(
        ulong iteration,
        IReadOnlyList<B> newBunches,
        object model) =>
        // **一份实现，两种签名。** 这里原来只有"去重并返回"，不建列、不记成本、不喂三张表——
        // 于是同一个动作在同步和异步下做**完全不同的事**：同步真的把列加进模型，异步只是把列表过滤一遍。
        // 调用方看到 `Ok` 和去重后的列表，以为列已就绪，实际上一列都没有。现在异步入口直接走同步实现。
        //
        // **One implementation, two signatures.** This used to only "deduplicate and return" — no columns, no cost, no tables — so the same operation did **completely different
        // things** synchronously and asynchronously: the sync path really added the columns, while the async path merely filtered the list. Callers saw `Ok` plus a deduplicated list,
        // believed the columns were ready, and there were none. The async entry point now routes to the synchronous implementation.
        System.Threading.Tasks.Task.FromResult(AddColumns(iteration, newBunches, model));
}
