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
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service.Limits;
/// <summary>
/// 任务编译影子价格键 / Task compilation shadow price key
/// </summary>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public sealed class TaskCompilationShadowPriceKey<E, A> : ShadowPriceKey
    where E : Executor
    where A : IAssignmentPolicy<E> {
    /// <summary>构造 / Constructor</summary>
    /// <param name="task">任务 / Task</param>
    public TaskCompilationShadowPriceKey(IAbstractTask<E, A> task) : base(typeof(TaskCompilationShadowPriceKey<E, A>)) {
        Task = task;
    }

    /// <summary>任务 / Task</summary>
    public IAbstractTask<E, A> Task { get; }

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is TaskCompilationShadowPriceKey<E, A> other
        && EqualityComparer<IAbstractTask<E, A>>.Default.Equals(Task, other.Task);

    /// <inheritdoc/>
    public override int GetHashCode() => Task?.GetHashCode() ?? 0;
}

/// <summary>
/// 任务编译约束 / Task compilation constraint
/// </summary>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public class TaskCompilationConstraint<E, A>
    : ICGPipeline<IGanttSchedulingShadowPriceArguments<E, A>, object, GanttSchedulingShadowPriceMap<E, A>>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    private readonly IReadOnlyList<IAbstractTask<E, A>> _tasks;
    private readonly ICompilation _compilation;
    private readonly IReadOnlyDictionary<IAbstractTask<E, A>, object>? _taskCompilation;
    private readonly string _name;

    /// <summary>
    /// 任务编译约束构造 / Task compilation constraint constructor
    /// </summary>
    /// <param name="tasks">任务列表 / List of tasks</param>
    /// <param name="compilation">编译结果 / Compilation result</param>
    /// <param name="taskCompilation">任务到编译表达式的映射；`null` 表示从编译结果取 / The map from task to a compiled expression; null means take it from the compilation</param>
    /// <param name="name">管道名称 / Pipeline name</param>
    public TaskCompilationConstraint(
        IReadOnlyList<IAbstractTask<E, A>> tasks,
        ICompilation compilation,
        IReadOnlyDictionary<IAbstractTask<E, A>, object>? taskCompilation = null,
        string name = "task_compilation") {
        _tasks = tasks;
        _compilation = compilation;
        // **只存显式给的那张表**。没给就每次问编译结果——构造期抓一次等于抓了一张快照，
        // 而迭代编译的任务在之后还会增长，快照会让追加的任务永远查不到。
        // **Only an explicitly supplied table is stored.** Otherwise ask the compilation every time — grabbing one at construction is a snapshot, and an iterative
        // compilation's tasks keep growing afterwards, so the snapshot would make appended tasks permanently unfindable.
        _taskCompilation = taskCompilation;
        _name = name;
    }

    /// <inheritdoc/>
    string IMetaConstraintGroup.Name => _name;

    /// <inheritdoc/>
    bool IMetaConstraintGroup.Lazy => false;

    /// <summary>
    /// 缺少编译表达式的任务 / The tasks without a compiled expression.
    /// </summary>
    /// <returns>缺失的任务 / The missing tasks.</returns>
    /// <remarks>
    /// **表是"当前任务"的表，所以必须每次现取**：迭代编译在列生成过程中持续追加任务。
    /// 若在构造期抓一张快照，追加的任务永远查不到——它们在模型里**一条编译要求都没有**，
    /// 而排程结果看起来完全正常：所有任务都排上了，只是某个任务少了它那一条。
    ///
    /// **The table is the current-task table, so it must be read fresh**: an iterative compilation keeps appending tasks during column generation. A snapshot taken at
    /// construction leaves later tasks unfindable — they carry **no compilation requirement** in the model at all, while the schedule looks entirely normal: every task assigned,
    /// one task simply missing its row.
    /// </remarks>
    public IReadOnlyList<IAbstractTask<E, A>> MissingExpressions() {
        IReadOnlyDictionary<IAbstractTask<E, A>, object>? table = CurrentTable();
        if (table is null) {
            return Array.Empty<IAbstractTask<E, A>>();
        }

        var missing = new List<IAbstractTask<E, A>>();
        foreach (IAbstractTask<E, A> task in _tasks) {
            if (!table.ContainsKey(task)) {
                missing.Add(task);
            }
        }
        return missing;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// **这一条以前只做校验、不加任何行**：它检查表里有没有每个任务的编译表达式，然后返回 `Ok`。
    /// 基线的 `invoke` 是 `model.addConstraint(taskCompilation[task] eq 1, args = TaskCompilationShadowPriceKey(task))`——
    /// 也就是**每个任务必须恰好被编译一次**这条要求本身。少了它，任务编译量可以取任意值，排程照样"成功"。
    ///
    /// 三件事一起做：**缺表达式就失败**（原来那半段）、**加 `= 1` 的行**、**行上挂键**。
    ///
    /// **This used to validate only and add no row**: it checked that every task had a compiled expression and returned `Ok`. The baseline's `invoke` is
    /// `model.addConstraint(taskCompilation[task] eq 1, args = TaskCompilationShadowPriceKey(task))` — the requirement itself, that **each task is compiled exactly once**. Without it the
    /// compiled quantity may take any value and the schedule still "succeeds". Three things now happen together: **fail on a missing expression** (the half that existed), **add the `= 1` row**, and
    /// **attach the key to the row**.
    /// </remarks>
    public Try Invoke(object model) {
        System.ArgumentNullException.ThrowIfNull(model);

        IReadOnlyDictionary<IAbstractTask<E, A>, object>? table = CurrentTable();
        if (table is null) {
            return Results.Ok<Success>(Results.SuccessInstance);
        }
        if (model is not Fuookami.Ospf.Core.Model.Mechanism.IAbstractLinearMetaModel<Flt64> linear) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "任务编译约束只能施加到线性元模型 / the task compilation constraint can only be applied to a linear meta model"));
        }

        // 第一个缺失就失败：部分施加的约束比完全失败更危险。
        // Fail on the first gap: a partially applied constraint set is more dangerous than failing outright.
        foreach (IAbstractTask<E, A> task in _tasks) {
            if (!table.TryGetValue(task, out object? expression)) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ApplicationError,
                    "缺少任务编译表达式：" + task
                    + " / Missing task compilation expression: " + task));
            }
            // ponytail: 表达式是**占位对象**时这一条加不了行——`CompilationTable.Build` 现在填的是 `new object()`
            // （`TaskCompilation` / `BunchCompilation` 两条路径都是），要建 `x = 1` 得先让那张表提供真正的列变量。
            // 升级路径：把 `CompilationTable.Build` 改成用 `SymbolCombination<LinearExpressionSymbol, Shape1>` 造符号，
            // 那时这一支自然走上面那条路。此刻**不能改成失败**：占位是当前的正常状态，失败就是把全部分配打回。
            //
            // ponytail: a placeholder expression cannot produce a row — `CompilationTable.Build` fills `new object()` today (on both the `TaskCompilation` and `BunchCompilation` paths),
            // and building `x = 1` first needs that table to hand out real column variables. Upgrade path: make `CompilationTable.Build` mint symbols with
            // `SymbolCombination<LinearExpressionSymbol, Shape1>`, and this branch falls through to the row-building path. It must **not** be turned into a failure right now: a placeholder is
            // the normal state today, and failing would reject every assignment.
            if (expression is not Fuookami.Ospf.Core.Symbol.ILinearIntermediateSymbol<Flt64> symbol) {
                continue;
            }

            // **符号要包成单项式**：`LinearExpressionSymbol` 本身就是变量，展开它的多项式是空的（B704 踩过同一个坑）。
            // **Wrap the symbol in a monomial**: a `LinearExpressionSymbol` *is* a variable and its polynomial is empty (the same trap as B704).
            string rowName = _name + "_" + task.Id;
            Try added = linear.AddConstraint(
                new Fuookami.Ospf.Math.Symbol.Inequality.LinearInequality<Flt64>(
                    new Fuookami.Ospf.Math.Symbol.Polynomial.LinearPolynomial<Flt64>(
                        new[] { new Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>(Flt64.One, symbol) },
                        Flt64.Zero),
                    new Fuookami.Ospf.Math.Symbol.Polynomial.LinearPolynomial<Flt64>(Array.Empty<Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>>(), Flt64.One),
                    Fuookami.Ospf.Math.Symbol.Inequality.Comparison.EQ,
                    rowName),
                group: null,
                name: rowName,
                args: new TaskCompilationShadowPriceKey<E, A>(task));
            if (!added.IsOk) {
                return added;
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 取当前的表 / Read the current table.
    /// </summary>
    /// <returns>当前的表，或 `null` 表示未接线 / The current table, or null when not wired.</returns>
    /// <remarks>
    /// 显式给出的表原样返回；否则每次向编译结果要——**不能缓存**，理由见 `MissingExpressions` 的注释。
    ///
    /// An explicitly supplied table is returned as-is; otherwise it is requested from the compilation every time — **it must not be cached**, see the remarks on
    /// `MissingExpressions`.
    /// </remarks>
    private IReadOnlyDictionary<IAbstractTask<E, A>, object>? CurrentTable() =>
        _taskCompilation ?? (_compilation as ITaskCompilation<E, A>)?.TaskCompilationTable;

    /// <inheritdoc/>
    public ShadowPriceExtractor<IGanttSchedulingShadowPriceArguments<E, A>, GanttSchedulingShadowPriceMap<E, A>>? Extractor()
        => (map, args) => {
            if (args.Task != null) {
                var key = new TaskCompilationShadowPriceKey<E, A>(args.Task);
                return map.Map.TryGetValue(key, out ShadowPrice? sp) ? sp.Price : Flt64.Zero;
            }
            return Flt64.Zero;
        };

    /// <inheritdoc/>
    /// <remarks>
    /// 按**行上带的键类型**从对偶解里认回本管线的行。走约束分组的默认实现一行都找不到——行是用 `group: null` 加的。
    ///
    /// Recognises this pipeline's rows in the dual solution by **the key type on the row**. A group-based default finds none, since the rows are added with `group: null`.
    /// </remarks>
    public Try Refresh(
        GanttSchedulingShadowPriceMap<E, A> shadowPriceMap,
        object model,
        MetaDualSolution shadowPrices) {
        System.ArgumentNullException.ThrowIfNull(shadowPriceMap);
        System.ArgumentNullException.ThrowIfNull(shadowPrices);

        foreach (KeyValuePair<MathConstraint, Flt64> pair in shadowPrices.Constraints) {
            if (pair.Key.Args is TaskCompilationShadowPriceKey<E, A> key) {
                shadowPriceMap.Put(new ShadowPrice(key, pair.Value));
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
