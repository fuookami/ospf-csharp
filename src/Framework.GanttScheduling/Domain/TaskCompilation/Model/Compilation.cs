#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.MultiArray;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
/// <summary>
/// 编译接口 / Compilation interface
/// </summary>
public interface ICompilation {
    /// <summary>是否启用任务取消 / Whether task cancellation is enabled</summary>
    bool TaskCancelEnabled { get; }

    /// <summary>是否包含执行器空闲 / Whether to include executor leisure</summary>
    bool WithExecutorLeisure { get; }

    /// <summary>注册到模型 / Register to model</summary>
    /// <param name="model">元模型 / Meta model</param>
    /// <returns>操作结果 / Operation result</returns>
    Try Register(object model);
}

/// <summary>
/// 执行器编译表 / The executor compilation table.
/// </summary>
/// <typeparam name="E">执行器类型 / Executor type.</typeparam>
/// <remarks>
/// 单独抽出来，是因为 `ICompilation` 是非泛型接口——它不知道 `E` 是什么，也就无法声明这张表。
/// 把表塞成 `Dictionary&lt;object, object&gt;` 会丢掉全部类型信息，调用方得自己转型，
/// 而转错的代价是**把约束加到了别的执行器上**。
///
/// It is a separate interface because `ICompilation` is non-generic: it has no knowledge of `E` and therefore cannot declare this table. Squeezing it into a
/// `Dictionary&lt;object, object&gt;` throws away all type information and forces the caller to cast, where a wrong cast **attaches the constraint to a different executor**.
/// </remarks>
public interface IExecutorCompilation<E> where E : Executor {
    /// <summary>执行器到编译表达式的映射 / The map from executor to a compiled expression.</summary>
    IReadOnlyDictionary<E, object> ExecutorCompilation { get; }
}

/// <summary>
/// 执行器编译表工厂 / Factory for executor compilation tables.
/// </summary>
/// <remarks>
/// 两个编译实现要填的是**同一张表、同一条规则**：每个执行器各一条。写成两处循环，
/// 日后改一处忘另一处，就会有一个编译实现的表比别人少一项——而少一项**不会报错**，
/// 只是那个执行器少一条约束。
///
/// Both compilation implementations fill **the same table by the same rule**: one entry per executor. Written as two loops, changing one and forgetting the other leaves one
/// implementation with a shorter table — and a missing entry **raises nothing**; one executor simply has one fewer constraint.
/// </remarks>
public static class CompilationTable {
    /// <summary>
    /// 构造（任务，执行器）组合表 / Build the (task, executor) combination table.
    /// </summary>
    /// <typeparam name="T">任务类型 / Task type.</typeparam>
    /// <typeparam name="E">执行器类型 / Executor type.</typeparam>
    /// <param name="tasks">任务列表 / The tasks.</param>
    /// <param name="executors">执行器列表 / The executors.</param>
    /// <returns>组合到占位表达式的映射 / The map from combination to a placeholder expression.</returns>
    public static IReadOnlyDictionary<(T Task, E Executor), object> BuildPairs<T, E>(
        IReadOnlyList<T> tasks,
        IReadOnlyList<E> executors)
        where T : notnull
        where E : notnull {
        System.ArgumentNullException.ThrowIfNull(tasks);
        System.ArgumentNullException.ThrowIfNull(executors);

        var table = new Dictionary<(T, E), object>();
        foreach (T task in tasks) {
            foreach (E executor in executors) {
                table[(task, executor)] = new object();
            }
        }
        return table;
    }

    /// <summary>
    /// 构造覆盖全部条目的表 / Build a table covering every item.
    /// </summary>
    /// <param name="items">条目列表 / The items.</param>
    /// <returns>条目到占位表达式的映射 / The map from item to a placeholder expression.</returns>
    public static IReadOnlyDictionary<T, object> Build<T>(IReadOnlyList<T> items) where T : notnull {
        System.ArgumentNullException.ThrowIfNull(items);

        var table = new Dictionary<T, object>();
        foreach (T item in items) {
            table[item] = new object();
        }
        return table;
    }

    /// <summary>
    /// 构造取消列（真实变量）/ Build the cancellation column as real variables.
    /// </summary>
    /// <typeparam name="T">条目类型 / The item type.</typeparam>
    /// <param name="items">条目列表 / The items.</param>
    /// <param name="name">变量名 / The variable name.</param>
    /// <returns>变量与表 / The variable and the table.</returns>
    /// <remarks>
    /// **这里必须给出真正的变量，不能是 `new object`。** 占位对象进不了模型，
    /// 于是任何以它为系数的目标函数都无处可加—— 之前那批空壳正是这么静默通过的。
    ///
    /// **These must be real variables, not `new object`.** A placeholder cannot enter the model, so any objective taking it as a coefficient has nowhere to go — exactly how the
    /// silent stubs before passed.
    /// </remarks>
    public static (BinVariable1 Variable, IReadOnlyDictionary<T, object> Table) BuildCancelColumns<T>(
        IReadOnlyList<T> items,
        string name)
        where T : notnull {
        System.ArgumentNullException.ThrowIfNull(items);

        var variable = new BinVariable1(name, items.Count);
        var table = new Dictionary<T, object>();
        for (int index = 0; index < items.Count; ++index) {
            table[items[index]] = variable[index];
        }
        return (variable, table);
    }

    /// <summary>
    /// 构造（任务，执行器）分配列（真实变量）/ Build the (task, executor) assignment columns as real variables.
    /// </summary>
    /// <typeparam name="T">任务类型 / Task type.</typeparam>
    /// <typeparam name="E">执行器类型 / Executor type.</typeparam>
    /// <param name="tasks">任务列表 / The tasks.</param>
    /// <param name="executors">执行器列表 / The executors.</param>
    /// <param name="name">变量名 / The variable name.</param>
    /// <returns>变量与表 / The variable and the table.</returns>
    public static (BinVariable2 Variable, IReadOnlyDictionary<(T Task, E Executor), object> Table) BuildAssignmentColumns<T, E>(
        IReadOnlyList<T> tasks,
        IReadOnlyList<E> executors,
        string name)
        where T : notnull
        where E : notnull {
        System.ArgumentNullException.ThrowIfNull(tasks);
        System.ArgumentNullException.ThrowIfNull(executors);

        var variable = new BinVariable2(name, tasks.Count, executors.Count);
        var table = new Dictionary<(T, E), object>();
        for (int taskIndex = 0; taskIndex < tasks.Count; ++taskIndex) {
            for (int executorIndex = 0; executorIndex < executors.Count; ++executorIndex) {
                table[(tasks[taskIndex], executors[executorIndex])] = variable[taskIndex, executorIndex];
            }
        }
        return (variable, table);
    }
}

/// <summary>
/// 任务编译表 / The task compilation table.
/// </summary>
/// <typeparam name="E">执行器类型 / Executor type.</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type.</typeparam>
/// <remarks>
/// **这张表是"当前任务的表"，不是构造时的快照**。迭代编译会在列生成过程中不断追加任务，
/// 若在构造期拍一张快照，之后追加的任务永远查不到——于是它们在模型里**一条编译要求都没有**，
/// 而排程结果看起来完全正常。
///
/// **This is the table of the current tasks, not a construction-time snapshot.** An iterative compilation keeps appending tasks during column generation; a snapshot taken at
/// construction means later tasks can never be found — so they carry **no compilation requirement** in the model at all, while the schedule looks entirely normal.
/// </remarks>
public interface ITaskCompilation<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    /// <summary>任务到编译表达式的映射 / The map from task to a compiled expression.</summary>
    IReadOnlyDictionary<IAbstractTask<E, A>, object> TaskCompilationTable { get; }
}

/// <summary>
/// 带代价项的编译 / A compilation carrying a cost term.
/// </summary>
/// <remarks>
/// `taskCost` 在**迭代编译**上，名字是 `bunch_cost`。它是目标函数的载体，
/// 由 `TaskCostMinimization` 取走。普通编译没有这一项——它的代价写在列变量的系数里。
///
/// Its `taskCost` lives on the **iterative** compilation and is named `bunch_cost` (a historical name copied verbatim). It carries the objective and is picked up by
/// `TaskCostMinimization`. A plain compilation has no such term — its cost sits in the column coefficients.
/// </remarks>
public interface ITaskCost {
    /// <summary>代价项符号；`null` 表示尚未注册 / The cost symbol; null means not registered yet.</summary>
    object? TaskCost { get; }
}

/// <summary>
/// 编译列 / Compilation columns.
/// </summary>
/// <typeparam name="E">执行器类型 / Executor type.</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type.</typeparam>
/// <remarks>
/// `Compilation` 有六张表：`y`（取消）、`z`（空闲）、`taskAssignment`（任务×执行器）、
/// `taskCompilation`、`executorCompilation`，加两个布尔开关。前两张与 `taskCompilation`、
/// `executorCompilation` 已分别落地；这里补齐**取消、空闲、分配**三张。
///
/// 三张表放在同一个接口里，因为它们是**同一次编译的产物**：拆成三个接口只会让每个调用方
/// 各自转型三次，而拿到的永远是同一个对象。
///
/// Its `Compilation` has six tables — `y` (cancel), `z` (leisure), `taskAssignment` (task × executor), `taskCompilation`, `executorCompilation` — plus two booleans.
/// Two of them plus `taskCompilation` and `executorCompilation` have landed already; this adds **cancel, leisure and assignment**.
///
/// They share one interface because they are **products of the same compilation**: splitting them into three would make every caller cast three times to reach the very same
/// object.
/// </remarks>
public interface ICompilationColumns<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    /// <summary>任务到取消列的映射 / The map from task to its cancellation column.</summary>
    IReadOnlyDictionary<IAbstractTask<E, A>, object> Cancellation { get; }

    /// <summary>执行器到空闲列的映射 / The map from executor to its leisure column.</summary>
    IReadOnlyDictionary<E, object> Leisure { get; }

    /// <summary>
    /// （任务，执行器）到 `task_assignment` 中间符号的映射 / The map from (task, executor) to the `task_assignment` intermediate symbol.
    /// </summary>
    /// <remarks>
    /// **这是中间符号，不是 `x` 变量**。`Compilation` 同时有 `x: BinVariable2`（真变量）与
    /// `taskAssignment: LinearIntermediateSymbols2`（`task_assignment_*` 中间符号），两者**同名不同物**。
    /// 把 `x` 挂在这个名字上会让"按任务取分配列"拿到真变量、按 `taskAssignment` 取到中间符号——
    /// 而两者在 0/1 世界里读起来一样，只在**跨轮聚合**时才分道扬镳（那时真变量根本不存在）。
    ///
 /// **This is the intermediate symbol, not the `x` variable.** Its `Compilation` has both `x: BinVariable2` (a real variable) and
    /// `taskAssignment: LinearIntermediateSymbols2` (a `task_assignment_*` intermediate symbol), **same name, different things**. Hanging `x` on this name means "get the assignment column
    /// for a task" hands back a real variable while `taskAssignment` hands back an intermediate symbol — and the two read alike in a 0/1 world, parting ways only under
    /// **cross-round aggregation**, where the real variable does not exist at all.
    /// </remarks>
    IReadOnlyDictionary<(IAbstractTask<E, A> Task, E Executor), object> TaskAssignment { get; }

    /// <summary>（任务，执行器）到 `x` 分配变量的映射 / The map from (task, executor) to the `x` assignment variable.</summary>
    IReadOnlyDictionary<(IAbstractTask<E, A> Task, E Executor), object> Assignment { get; }
}

/// <summary>
/// 任务编译 / Task compilation
/// </summary>
/// <typeparam name="T">任务类型 / Task type</typeparam>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public class TaskCompilation<T, E, A> : ICompilation, IExecutorCompilation<E>, ITaskCompilation<E, A>, ICompilationColumns<E, A>
    where T : IAbstractTask<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    private readonly IReadOnlyList<T> _tasks;
    private readonly IReadOnlyList<E> _executors;
    private readonly IReadOnlySet<T> _lockCancelTasks;
    private readonly List<object> _registeredModels = new();
    private BinVariable2? _assignmentVariable;
    private BinVariable1? _cancellationVariable;
    private BinVariable1? _leisureVariable;
    private SymbolCombination<LinearExpressionSymbol, Shape2>? _assignmentCombination;
    private SymbolCombination<LinearExpressionSymbol, Shape1>? _compilationCombination;
    private SymbolCombination<LinearExpressionSymbol, Shape1>? _executorCompilationCombination;

    /// <summary>
    /// 任务编译构造 / Task compilation constructor
    /// </summary>
    /// <param name="tasks">任务列表 / List of tasks</param>
    /// <param name="executors">执行器列表 / List of executors</param>
    /// <param name="lockCancelTasks">锁定取消任务集合 / Set of locked cancel tasks</param>
    /// <param name="taskCancelEnabled">是否启用任务取消 / Whether task cancellation is enabled</param>
    /// <param name="withExecutorLeisure">是否包含执行器空闲 / Whether to include executor leisure</param>
    public TaskCompilation(
        IReadOnlyList<T> tasks,
        IReadOnlyList<E> executors,
        IReadOnlySet<T>? lockCancelTasks = null,
        bool taskCancelEnabled = false,
        bool withExecutorLeisure = false) {
        _tasks = tasks;
        _executors = executors;
        _lockCancelTasks = lockCancelTasks ?? new HashSet<T>();
        TaskCancelEnabled = taskCancelEnabled;
        WithExecutorLeisure = withExecutorLeisure;

        // 每个执行器各一条占位表达式：构造期就能枚举完执行器，不需要模型。
        // 模型接线之后这些占位会被真正的列变量替换，但"每个执行器都有一条"这件事从一开始就成立。
        // One placeholder per executor: the executor list is fully known at construction, no model needed. Once the model is wired these placeholders become real
        // column variables, but "every executor has one" holds from the start.
        ExecutorCompilation = CompilationTable.Build(_executors);

        var taskList = _tasks.Cast<IAbstractTask<E, A>>().ToList();
        TaskCompilationTable = CompilationTable.Build(taskList);

        // 三张列在构造期一次建好：普通编译的任务与执行器之后都不变。
        // All three column tables are built once at construction: a plain compilation's tasks and executors never change.
        Cancellation = CompilationTable.Build(taskList);
        Leisure = CompilationTable.Build(_executors);
        Assignment = CompilationTable.BuildPairs(taskList, _executors);
        TaskAssignment = CompilationTable.BuildPairs(taskList, _executors);
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<E, object> ExecutorCompilation { get; private set; }

    /// <inheritdoc/>
    /// <remarks>
    /// **注册之后才是真的**：构造期这张表里放的是 `CompilationTable.Build` 造的占位对象，注册时 `task_compilation` 组合建好才被换成
    /// 真符号。在此之前读它的调用方拿到的是 `new object`——一个**看起来非空、转不成符号**的东西。
    /// 给偏移量加掩码时就撞在这里：`InvalidCastException`，而报错只说"不能把 `System.Object` 转成符号"。
    ///
    /// **Only real after registration**: at construction this table holds placeholder objects from `CompilationTable.Build`, replaced by the real symbols once the
    /// `task_compilation` combination is built. A caller reading it before that gets `new object` — **non-null and unconvertible**. The earlier deviation mask hit exactly this:
    /// `InvalidCastException` whose message only says the cast failed.
    /// </remarks>
    public IReadOnlyDictionary<IAbstractTask<E, A>, object> TaskCompilationTable { get; private set; }

    /// <inheritdoc/>
    public IReadOnlyDictionary<IAbstractTask<E, A>, object> Cancellation { get; private set; }

    /// <inheritdoc/>
    public IReadOnlyDictionary<E, object> Leisure { get; private set; }

    /// <inheritdoc/>
    public IReadOnlyDictionary<(IAbstractTask<E, A> Task, E Executor), object> TaskAssignment { get; private set; }

    /// <inheritdoc/>
    public IReadOnlyDictionary<(IAbstractTask<E, A> Task, E Executor), object> Assignment { get; private set; }

    /// <inheritdoc/>
    public bool TaskCancelEnabled { get; }

    /// <inheritdoc/>
    public bool WithExecutorLeisure { get; }

    /// <summary>任务列表 / List of tasks</summary>
    public IReadOnlyList<T> Tasks => _tasks;

    /// <summary>执行器列表 / List of executors</summary>
    public IReadOnlyList<E> Executors => _executors;

    /// <summary>注册到模型 / Register to model</summary>
    /// <param name="model">元模型 / Meta model</param>
    /// <returns>操作结果 / Operation result</returns>
    /// <remarks>
    /// 这里**第一次把占位列换成真变量**并送进模型。完整注册要建六张表，这里落地三张：
    ///
    /// | 表 | 这里 | 说明 |
    /// | --- | --- | --- |
    /// | `x`（任务 × 执行器） | **已落地** | 含 的值域收紧 |
    /// | `y`（取消） | **已落地** | `taskCancelEnabled` 才建 |
    /// | `z`（空闲） | **已落地** | `withExecutorLeisure` 才建 |
    /// | `taskAssignment` / `taskCompilation` | 未落地 | 需要线性中间符号组合 |
    /// | `executorCompilation` | 未落地 | 需要 `OrFunction` 适配器 |
    ///
    /// **值域收紧不是优化，是语义**：不许执行的组合固定为 0、已定死的执行器固定为 1，
    /// 与 `TaskAssignmentRange`同一条规则。漏掉它，模型只是"多搜了一些 0/1"，
    /// 结果仍然可行——但那条"任务只能由这个执行器做"的约束就**不存在**了。
    ///
 /// This is where the placeholder columns are first **replaced by real variables** and handed to the model. Its `register` does six things; three land here:
    ///
 /// | Table | | Note |
    /// | --- | --- | --- |
    /// | `x` (task × executor) | **landed** | with the earlier range tightening |
    /// | `y` (cancel) | **landed** | built only when `taskCancelEnabled` |
    /// | `z` (leisure) | **landed** | built only when `withExecutorLeisure` |
    /// | `taskAssignment` / `taskCompilation` | not landed | needs linear intermediate-symbol combinations |
    /// | `executorCompilation` | not landed | needs the `OrFunction` adapter |
    ///
    /// **Range tightening is semantics, not an optimisation**: forbidden combinations are pinned to 0 and a fixed executor to 1, the same rule as `TaskAssignmentRange` .
    /// Dropping it merely makes the model "search some more 0/1" and the answer stays feasible — but the "only this executor may do this task" constraint **does not exist**.
    /// </remarks>
    public Try Register(object model) {
        if (model is not IAbstractLinearMetaModel<Flt64> linear) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "任务编译只能注册到线性元模型 / a task compilation can only be registered into a linear meta model"));
        }

        // 0. 已经在**这个**模型上注册过：再注册一次是空操作。
        //    守卫只保证"变量只建一次"，`model.add` 是无条件的——对同一个模型重来会撞上重名符号。
        //    所以这里按**模型实例**记住，重复注册直接返回成功。
        //
 // Already registered on **this** model: registering again is a no-op. Its guards only ensure "the variables are built once", while `model.add` is
        //    unconditional — redoing it on the same model hits a repeated symbol. So the model instance is remembered and a repeat call succeeds outright.
        foreach (object registered in _registeredModels) {
            if (ReferenceEquals(registered, model)) {
                return Results.Ok<Success>(Results.SuccessInstance);
            }
        }

        var taskList = _tasks.Cast<IAbstractTask<E, A>>().ToList();
        Try added;

        // 1. `x`：**只建一次**。守卫是 `if (!::x.isInitialized)`。
 // `x`: build **once**. The implementation guards with `if (!::x.isInitialized)`.
        if (_assignmentVariable is null) {
            (BinVariable2 assignmentVariable, IReadOnlyDictionary<(IAbstractTask<E, A> Task, E Executor), object> assignmentTable) =
                CompilationTable.BuildAssignmentColumns(taskList, _executors, "x");
            TightenAssignmentRanges(assignmentVariable, taskList);
            _assignmentVariable = assignmentVariable;
            Assignment = assignmentTable;
        }
        added = linear.Add(_assignmentVariable.Items);
        if (!added.IsOk) {
            return added;
        }

        if (TaskCancelEnabled && _cancellationVariable is null) {
            (BinVariable1 cancelVariable, IReadOnlyDictionary<IAbstractTask<E, A>, object> cancelTable) =
                CompilationTable.BuildCancelColumns(taskList, "y");
            TightenCancelRanges(cancelVariable, taskList);
            _cancellationVariable = cancelVariable;
            Cancellation = cancelTable;
        }
        if (TaskCancelEnabled) {
            added = linear.Add(_cancellationVariable!.Items);
            if (!added.IsOk) {
                return added;
            }
        }

        if (WithExecutorLeisure && _leisureVariable is null) {
            (BinVariable1 leisureVariable, IReadOnlyDictionary<E, object> leisureTable) =
                CompilationTable.BuildCancelColumns(_executors, "z");
            _leisureVariable = leisureVariable;
            Leisure = leisureTable;
        }
        if (WithExecutorLeisure) {
            added = linear.Add(_leisureVariable!.Items);
            if (!added.IsOk) {
                return added;
            }
        }

        // 2. 两张聚合表**只建一次**：`taskCompilation == 1` 是约束，重复注册会把它加两遍。
        // The two aggregate tables are built **once**: `taskCompilation == 1` is a constraint, and re-registering would add it twice.
        if (_assignmentCombination is null) {
            Try aggregated = BuildAggregationTables(linear, taskList, _assignmentVariable);
            if (!aggregated.IsOk) {
                return aggregated;
            }
        } else {
            added = linear.Add(_assignmentCombination);
            if (!added.IsOk) {
                return added;
            }
            added = linear.Add(_compilationCombination!);
            if (!added.IsOk) {
                return added;
            }
            // 约束是**每个模型各自一份**的：换了模型就要在新模型上再加一次，
            // 否则松弛模型里没有"任务必须完成"这条，它的解永远可行。
            // Constraints are **per model**: a new model needs them added again, or the relaxed model lacks "a task must be done" and its solutions are always feasible.
            Try constrained = AddCompilationConstraints(linear, taskList);
            if (!constrained.IsOk) {
                return constrained;
            }
        }

        Try completed = _executorCompilationCombination is null
            ? BuildExecutorCompilationTable(linear, taskList, _assignmentVariable)
            : linear.Add(_executorCompilationCombination);
        if (completed.IsOk) {
            _registeredModels.Add(model);
        }
        return completed;
    }

    /// <summary>
    /// 构造两张线性中间符号表并送进模型 / Build the two linear intermediate-symbol tables and hand them to the model.
    /// </summary>
    /// <param name="linear">线性元模型 / The linear meta model.</param>
    /// <param name="taskList">任务列表 / The tasks.</param>
    /// <param name="assignment">分配变量 / The assignment variable.</param>
    /// <returns>操作结果 / Operation result.</returns>
    /// <remarks>
    /// 这两张表都是**列之和**，不是新变量：
    ///
    /// | 表 | 定义 |
    /// | --- | --- |
    /// | `taskAssignment[t, e]` | `x[t, e]`（值域 0..1） |
    /// | `taskCompilation[t]` | `y[t] + Σ_e x[t, e]`（取消开着时）或 `Σ_e x[t, e]`，**强制等于 1** |
    ///
    /// `taskCompilation[t] == 1` 就是"每个任务必须被完成或明确取消"这条约束本身。
    /// 少了它，模型可以**什么都不做**而完全可行——这比任何目标函数写错都更隐蔽：
    /// 答案合法、求解成功，只是所有任务都没排。
    ///
    /// Both tables are **sums of columns**, not new variables:
    ///
    /// | Table | Definition |
    /// | --- | --- |
    /// | `taskAssignment[t, e]` | `x[t, e]` (range 0..1) |
    /// | `taskCompilation[t]` | `y[t] + Σ_e x[t, e]` when cancelling is on, else `Σ_e x[t, e]`, **forced equal to 1** |
    ///
    /// `taskCompilation[t] == 1` *is* the "every task is either done or explicitly cancelled" constraint. Without it the model can **do nothing at all** and remain feasible —
    /// more insidious than any wrong objective, because the answer is legal, the solve succeeds, and every task is simply unscheduled.
    /// </remarks>
    private Try BuildAggregationTables(
        IAbstractLinearMetaModel<Flt64> linear,
        IReadOnlyList<IAbstractTask<E, A>> taskList,
        BinVariable2 assignment) {
        var assignmentCombination = new SymbolCombination<LinearExpressionSymbol, Shape2>(
            "task_assignment",
            Shape2.Invoke(taskList.Count, _executors.Count),
            (index, vector) => new LinearExpressionSymbol(
                new MutableLinearPolynomial<Flt64>(
                    new[] { new LinearMonomial<Flt64>(Flt64.One, assignment[vector[0], vector[1]]) },
                    Flt64.Zero),
                name: "task_assignment_" + string.Join("_", vector)));

        for (int taskIndex = 0; taskIndex < taskList.Count; ++taskIndex) {
            for (int executorIndex = 0; executorIndex < _executors.Count; ++executorIndex) {
                LinearExpressionSymbol symbol = assignmentCombination[taskIndex * _executors.Count + executorIndex];
                symbol.Range.Geq(Flt64.Zero);
                symbol.Range.Leq(Flt64.One);
            }
        }
        Try added = linear.Add(assignmentCombination);
        if (!added.IsOk) {
            return added;
        }

        var compilationCombination = new SymbolCombination<LinearExpressionSymbol, Shape1>(
            "task_compilation",
            Shape1.Invoke(taskList.Count),
            (index, _) => BuildCompilationSymbol(index, assignment));

        added = linear.Add(compilationCombination);
        if (!added.IsOk) {
            return added;
        }

        _compilationCombination = compilationCombination;
        Try constrained = AddCompilationConstraints(linear, taskList);
        if (!constrained.IsOk) {
            return constrained;
        }

        var assignmentTable = new Dictionary<(IAbstractTask<E, A>, E), object>();
        for (int taskIndex = 0; taskIndex < taskList.Count; ++taskIndex) {
            for (int executorIndex = 0; executorIndex < _executors.Count; ++executorIndex) {
                assignmentTable[(taskList[taskIndex], _executors[executorIndex])] =
                    assignmentCombination[taskIndex * _executors.Count + executorIndex];
            }
        }
        TaskAssignment = assignmentTable;

        var compilationTable = new Dictionary<IAbstractTask<E, A>, object>();
        for (int taskIndex = 0; taskIndex < taskList.Count; ++taskIndex) {
            compilationTable[taskList[taskIndex]] = compilationCombination[taskIndex];
        }
        TaskCompilationTable = compilationTable;

        _assignmentCombination = assignmentCombination;
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 在每个任务上强制 `taskCompilation[t] == 1` / Force `taskCompilation[t] == 1` on every task.
    /// </summary>
    /// <param name="linear">线性元模型 / The linear meta model.</param>
    /// <param name="taskList">任务列表 / The tasks.</param>
    /// <returns>操作结果 / Operation result.</returns>
    /// <remarks>
    /// 这条写成**符号的值域**（`taskCompilation[task].range.set(ValueRange(one, one))`），
    /// 那是挂在符号上的、跟着符号走；本实现写成**模型的约束行**，于是"换一个模型"要重新加一次。
    /// 差别不在解上，在**换了模型之后**：只加一次的话，第二个模型没有这条约束。
    ///
 /// This implementation writes this as a **symbol range** (`taskCompilation[task].range.set(ValueRange(one, one))`), which rides along with the symbol; this implementation writes it as a
    /// **model constraint row**, so "another model" needs it added again. The difference is not in the solution but **after switching models**: added only once, the second model lacks
    /// this constraint.
    /// </remarks>
    private Try AddCompilationConstraints(
        IAbstractLinearMetaModel<Flt64> linear,
        IReadOnlyList<IAbstractTask<E, A>> taskList) {
        var one = new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.One);
        for (int taskIndex = 0; taskIndex < taskList.Count; ++taskIndex) {
            // 强制等于 1：任务要么被完成，要么被明确取消。
            // Forced equal to 1: the task is either done or explicitly cancelled.
            Try added = linear.AddConstraint(
                _compilationCombination![taskIndex].Polynomial.Eq(one),
                group: null,
                name: "task_compilation_" + taskList[taskIndex].Id);
            if (!added.IsOk) {
                return added;
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 构造单个任务的编译表达式 / Build one task's compilation expression.
    /// </summary>
    /// <param name="taskIndex">任务下标 / Task index.</param>
    /// <param name="assignment">分配变量 / The assignment variable.</param>
    /// <returns>编译表达式 / The compilation expression.</returns>
    private LinearExpressionSymbol BuildCompilationSymbol(int taskIndex, BinVariable2 assignment) {
        var polynomial = new MutableLinearPolynomial<Flt64>(null, Flt64.Zero);
        if (_cancellationVariable is not null) {
            polynomial.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, _cancellationVariable[taskIndex]));
        }
        for (int executorIndex = 0; executorIndex < _executors.Count; ++executorIndex) {
            polynomial.AddMonomial(new LinearMonomial<Flt64>(
                Flt64.One,
                assignment[taskIndex, executorIndex]));
        }
        return new LinearExpressionSymbol(polynomial, name: "task_compilation_" + taskIndex);
    }

    /// <summary>
    /// 构造执行器编译表并送进模型 / Build the executor compilation table and hand it to the model.
    /// </summary>
    /// <param name="linear">线性元模型 / The linear meta model.</param>
    /// <param name="taskList">任务列表 / The tasks.</param>
    /// <param name="assignment">分配变量 / The assignment variable.</param>
    /// <returns>操作结果 / Operation result.</returns>
    /// <remarks>
    /// `executorCompilation[e] = OR(x[t, e] for t) + z[e]`（含空闲时）。
    ///
    /// **这里必须是 `OR`，不能是求和。** 一个执行器上排了 3 个任务，求和得到 3；`OR` 得到 1。
    /// "该执行器被用了"是一个布尔事实，而求和把它变成了"用了几个"——于是任何
    /// "每个执行器最多一项成本"的目标都会按任务数重复收费，而解仍然可行。
    ///
    /// **It must be `OR`, not a sum.** An executor carrying 3 tasks sums to 3 while `OR` gives 1. "This executor is used" is a boolean fact, and summing turns it into "how many" — so
    /// any "one cost per used executor" objective charges once per task, while the solution stays feasible.
    /// </remarks>
    private Try BuildExecutorCompilationTable(
        IAbstractLinearMetaModel<Flt64> linear,
        IReadOnlyList<IAbstractTask<E, A>> taskList,
        BinVariable2 assignment) {
        var orFunctions = new List<OrFunction>(_executors.Count);
        var combination = new SymbolCombination<LinearExpressionSymbol, Shape1>(
            "executor_compilation",
            Shape1.Invoke(_executors.Count),
            (executorIndex, _) => {
                var polynomials = new List<LinearPolynomial<Flt64>>(taskList.Count);
                for (int taskIndex = 0; taskIndex < taskList.Count; ++taskIndex) {
                    polynomials.Add(new LinearPolynomial<Flt64>(
                        new[] { new LinearMonomial<Flt64>(Flt64.One, assignment[taskIndex, executorIndex]) },
                        Flt64.Zero));
                }
                var or = new OrFunction(
                    polynomials: polynomials,
                    name: "executor_compilation_or_" + _executors[executorIndex].Id);
                orFunctions.Add(or);

                var polynomial = new MutableLinearPolynomial<Flt64>(
                    new[] { new LinearMonomial<Flt64>(Flt64.One, or.ResultVar) },
                    Flt64.Zero);
                if (_leisureVariable is not null) {
                    polynomial.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, _leisureVariable[executorIndex]));
                }
                return new LinearExpressionSymbol(
                    polynomial,
                    name: "executor_compilation_" + _executors[executorIndex].Id);
            });

        foreach (OrFunction or in orFunctions) {
            Try addedOr = linear.Add(new LinearFunctionSymbolAdapter<Flt64>(or));
            if (!addedOr.IsOk) {
                return addedOr;
            }
        }

        Try added = linear.Add(combination);
        if (!added.IsOk) {
            return added;
        }

        var table = new Dictionary<E, object>();
        for (int executorIndex = 0; executorIndex < _executors.Count; ++executorIndex) {
            table[_executors[executorIndex]] = combination[executorIndex];
        }
        ExecutorCompilation = table;
        _executorCompilationCombination = combination;
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 收紧分配变量的值域 / Tighten the assignment variables' ranges.
    /// </summary>
    /// <param name="variable">分配变量 / The assignment variable.</param>
    /// <param name="tasks">任务列表 / The tasks.</param>
    /// <remarks>逐条调用 `TaskAssignmentRange.For`，**规则只有一份**。 / Each entry goes through `TaskAssignmentRange.For` : **one rule, one place**.</remarks>
    private void TightenAssignmentRanges(BinVariable2 variable, IReadOnlyList<IAbstractTask<E, A>> tasks) {
        for (int taskIndex = 0; taskIndex < tasks.Count; ++taskIndex) {
            IAbstractTask<E, A> task = tasks[taskIndex];
            for (int executorIndex = 0; executorIndex < _executors.Count; ++executorIndex) {
                E executor = _executors[executorIndex];
                AssignmentRange range = TaskAssignmentRange.For(
                    executorEnabled: task.EnabledExecutors.Contains(executor),
                    executorFixed: task.Executor is not null && !task.ExecutorChangeEnabled,
                    isTaskExecutor: task.Executor is not null && task.Executor.Equals(executor),
                    cancelEnabled: task.CancelEnabled);
                if (range.IsFixed) {
                    variable[taskIndex, executorIndex].Range.Eq(range.Low == 1.0);
                }
            }
        }
    }

    /// <summary>
    /// 收紧取消变量的值域 / Tighten the cancellation variables' ranges.
    /// </summary>
    /// <param name="variable">取消变量 / The cancellation variable.</param>
    /// <param name="tasks">任务列表 / The tasks.</param>
    /// <remarks>
    /// 规则，且**后写的覆盖先写的**：不允许取消 → 固定 0；被锁定 → 固定 1。
    /// 一个任务同时"不允许取消"又"被锁定"时得到 **1**——锁定赢。
    ///
 /// The rule matches the implementation and **the later write wins**: cancel disabled → 0; locked → 1. A task that both forbids cancelling and is locked ends up **1** — the lock wins.
    /// </remarks>
    private void TightenCancelRanges(BinVariable1 variable, IReadOnlyList<IAbstractTask<E, A>> tasks) {
        for (int index = 0; index < tasks.Count; ++index) {
            IAbstractTask<E, A> task = tasks[index];
            if (!task.CancelEnabled) {
                variable[index].Range.Eq(false);
            }
            if (task is T typed && _lockCancelTasks.Contains(typed)) {
                variable[index].Range.Eq(true);
            }
        }
    }
}

/// <summary>
/// 迭代任务编译 / Iterative task compilation
/// </summary>
/// <typeparam name="IT">迭代任务类型 / Iterative task type</typeparam>
/// <typeparam name="T">任务类型 / Task type</typeparam>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public class IterativeTaskCompilation<IT, T, E, A> : ICompilation, IExecutorCompilation<E>, ITaskCompilation<E, A>, ICompilationColumns<E, A>, ITaskCost
    where IT : IIterativeAbstractTask<E, A>
    where T : IAbstractTask<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    private readonly IReadOnlyList<T> _originTasks;
    private readonly IReadOnlyList<E> _executors;
    private readonly IReadOnlySet<T> _lockedCancelTasks;
    private readonly TaskAggregation<IT, E, A> _aggregation = new();
    private BinVariable1? _cancellationVariable;
    private IReadOnlyDictionary<IAbstractTask<E, A>, object>? _cancellationTable;
    private LinearExpressionSymbol? _taskCost;
    private SymbolCombination<LinearExpressionSymbol, Shape2>? _assignmentCombination;
    private SymbolCombination<LinearExpressionSymbol, Shape1>? _compilationCombination;
    private BinVariable1? _xorVariable;
    private BinVariable1? _leisureVariable;
    private SymbolCombination<LinearExpressionSymbol, Shape1>? _executorCompilationCombination;
    private IReadOnlyDictionary<E, object>? _executorCompilationTable;
    private readonly List<BinVariable1> _x = new();
    private readonly List<object> _registeredModels = new();
    private readonly List<(LinearInequality<Flt64> Inequality, string Name)> _columnConstraints = new();
    private readonly HashSet<IVariableItem> _retiredVariables = new();

    /// <summary>
    /// 迭代任务编译构造 / Iterative task compilation constructor
    /// </summary>
    /// <param name="originTasks">原始任务列表 / List of origin tasks</param>
    /// <param name="executors">执行器列表 / List of executors</param>
    /// <param name="lockedCancelTasks">锁定取消任务集合 / Set of locked cancel tasks</param>
    public IterativeTaskCompilation(
        IReadOnlyList<T> originTasks,
        IReadOnlyList<E> executors,
        IReadOnlySet<T>? lockedCancelTasks = null) {
        _originTasks = originTasks;
        _executors = executors;
        _lockedCancelTasks = lockedCancelTasks ?? new HashSet<T>();

        // 构造期不填表：迭代编译的执行器编译表达式要等 `Register` 才有内容，
        // 拍一张占位快照只会让人以为它已经可用。
        // No construction-time table: the iterative executor compilation only has content after `Register`, and a placeholder snapshot would merely look usable.
    }

    /// <inheritdoc/>
    public bool TaskCancelEnabled => true;

    /// <inheritdoc/>
    public bool WithExecutorLeisure => true;

    /// <inheritdoc/>
    /// <inheritdoc/>
    /// <remarks>
    /// 注册前是占位对象，注册后是 `xor[e] + z[e]` 的中间符号。**不缓存构造期快照**——那张表里没有真表达式。
    ///
    /// Placeholders before registration; `xor[e] + z[e]` intermediate symbols after. **No construction-time snapshot is cached** — it holds no real expressions.
    /// </remarks>
    public IReadOnlyDictionary<E, object> ExecutorCompilation =>
        _executorCompilationTable ?? CompilationTable.Build(_executors);

    /// <inheritdoc/>
    /// <remarks>
    /// **每次读取都重新构造**：迭代编译的任务在列生成过程中持续增长，缓存下来就只剩构造那一刻的那批任务。
    /// 代价是每次读取分配一张表——与"追加的任务查不到"相比，这个代价可以接受。
    ///
    /// **Rebuilt on every read**: the iterative compilation's tasks keep growing during column generation, and a cache would freeze them at construction. The cost is one table
    /// allocation per read — acceptable next to "appended tasks cannot be found".
    /// </remarks>
    public IReadOnlyDictionary<IAbstractTask<E, A>, object> TaskCompilationTable =>
        _compilationCombination is null
            ? CompilationTable.Build(CurrentTasks())
            : BuildCombinationTable(_compilationCombination, _originTasks.Cast<IAbstractTask<E, A>>().ToList());

    /// <inheritdoc/>
    /// <remarks>任务会增长，所以每次现取。 / Tasks grow, so it is read fresh.</remarks>
    public IReadOnlyDictionary<IAbstractTask<E, A>, object> Cancellation => CompilationTable.Build(CurrentTasks());

    /// <inheritdoc/>
    public IReadOnlyDictionary<E, object> Leisure => CompilationTable.Build(_executors);

    /// <inheritdoc/>
    /// <remarks>任务会增长，组合表同样每次现取。 / Tasks grow, so the combination table is read fresh too.</remarks>
    public IReadOnlyDictionary<(IAbstractTask<E, A> Task, E Executor), object> TaskAssignment =>
        _assignmentCombination is null
            ? CompilationTable.BuildPairs(CurrentTasks(), _executors)
            : BuildPairTable(_assignmentCombination);

    /// <inheritdoc/>
    /// <remarks>
    /// 迭代编译的 `x` **按轮存在**（`x: List&lt;BinVariable1&gt;`），所以"任务 → 变量"不唯一：
    /// 同一任务可以在多轮里各有一列。这里给的是**最新一轮**的变量——`x` 是整张列表，
    /// 而任何"按 (任务, 执行器) 取一个变量"的用法都只在单轮里成立。
    ///
    /// The iterative `x` **exists per round** (its `x: List&lt;BinVariable1&gt;`), so "task → variable" is not unique: one task can have a column in several rounds. What is
    /// offered here is the **latest round's** variable — its `x` is the whole list, and any "one variable per (task, executor)" usage only holds within a single round.
    /// </remarks>
    public IReadOnlyDictionary<(IAbstractTask<E, A> Task, E Executor), object> Assignment {
        get {
            var table = new Dictionary<(IAbstractTask<E, A>, E), object>();
            if (_x.Count == 0) {
                return table;
            }
            IReadOnlyList<IT> roundTasks = _aggregation.TasksIteration[_x.Count - 1];
            BinVariable1 round = _x[_x.Count - 1];
            for (int index = 0; index < roundTasks.Count && index < round.Shape.D1; ++index) {
                if (roundTasks[index].Executor is { } executor) {
                    table[(roundTasks[index], executor)] = round[index];
                }
            }
            return table;
        }
    }

    /// <inheritdoc/>
    public object? TaskCost => _taskCost;

    /// <summary>取当前任务列表 / Read the current task list.</summary>
    /// <returns>当前任务 / The current tasks.</returns>
    private IReadOnlyList<IAbstractTask<E, A>> CurrentTasks() =>
        Tasks.Cast<IAbstractTask<E, A>>().ToList();

    /// <summary>按任务取一维组合表的条目 / Take the entries of a 1-D combination keyed by task.</summary>
    /// <param name="combination">一维组合 / The 1-D combination.</param>
    /// <param name="tasks">任务列表 / The tasks.</param>
    /// <returns>任务到符号的映射 / The map from task to symbol.</returns>
    private static IReadOnlyDictionary<IAbstractTask<E, A>, object> BuildCombinationTable(
        SymbolCombination<LinearExpressionSymbol, Shape1> combination,
        IReadOnlyList<IAbstractTask<E, A>> tasks) {
        var table = new Dictionary<IAbstractTask<E, A>, object>();
        for (int index = 0; index < tasks.Count && index < combination.Shape.D1; ++index) {
            table[tasks[index]] = combination[index];
        }
        return table;
    }

    /// <summary>按（任务，执行器）取二维组合表的条目 / Take the entries of a 2-D combination keyed by (task, executor).</summary>
    /// <param name="combination">二维组合 / The 2-D combination.</param>
    /// <returns>组合到符号的映射 / The map from combination to symbol.</returns>
    private IReadOnlyDictionary<(IAbstractTask<E, A> Task, E Executor), object> BuildPairTable(
        SymbolCombination<LinearExpressionSymbol, Shape2> combination) {
        var table = new Dictionary<(IAbstractTask<E, A>, E), object>();
        var tasks = _originTasks.Cast<IAbstractTask<E, A>>().ToList();
        for (int taskIndex = 0; taskIndex < tasks.Count && taskIndex < combination.Shape.D1; ++taskIndex) {
            for (int executorIndex = 0; executorIndex < _executors.Count && executorIndex < combination.Shape.D2; ++executorIndex) {
                table[(tasks[taskIndex], _executors[executorIndex])] =
                    combination[taskIndex * _executors.Count + executorIndex];
            }
        }
        return table;
    }


    /// <summary>原始任务列表 / List of origin tasks</summary>
    public IReadOnlyList<T> OriginTasks => _originTasks;

    /// <summary>执行器列表 / List of executors</summary>
    public IReadOnlyList<E> Executors => _executors;

    /// <summary>任务聚合 / Task aggregation</summary>
    public TaskAggregation<IT, E, A> Aggregation => _aggregation;

    /// <summary>任务迭代列表 / Task iteration list</summary>
    public IReadOnlyList<IReadOnlyList<IT>> TasksIteration => _aggregation.TasksIteration;

    /// <summary>所有任务列表 / All tasks list</summary>
    public IReadOnlyList<IT> Tasks => _aggregation.Tasks;

    /// <summary>注册到模型 / Register to model</summary>
    /// <param name="model">元模型 / Meta model</param>
    /// <returns>操作结果 / Operation result</returns>
    /// <remarks>
    /// 迭代编译与普通编译的六张表**结构相同、内容不同**，差别是本质的：
    ///
    /// | 表 | 普通编译 | 迭代编译 |
    /// | --- | --- | --- |
    /// | `x` | 任务 × 执行器，一次建好 | **每轮一组**（`x_0`、`x_1`…），由 `AddColumns` 加 |
    /// | `taskCompilation[t]` | `y[t] + Σ_e x[t, e]`，强制 `== 1` | **只有 `y[t]`** |
    /// | `executorCompilation[e]` | `OR(x[t, e] for t)` + `z[e]` | **`xor[e]` + `z[e]`** |
    ///
    /// 两处差别都是"列还没生成"的必然结果：
    ///
    /// - `taskCompilation` 里**不能**有 `Σ x`：`x` 是每轮追加的，注册时还不知道有几轮。
    ///   写成 `Σ x` 会在注册那一刻固化成空和，之后每轮新加的列都进不了这条等式。
    /// - `executorCompilation` 里**不能**是 `OR(x)`：同上。独立变量 `xor[e]`，
    ///   它的语义由跨轮冲突约束与 `xor` 下界来钉，而不是由 `OR` 函数直接算。
    ///
    /// 把迭代编译沿用普通编译的写法，模型**仍然可解**——只是 `taskCompilation` 恒等于 `y`，
    /// 于是"任务必须完成或取消"退化成"任务必须取消"，所有任务都被取消而没有任何报错。
    ///
    /// The iterative and plain compilations have **the same six tables with different contents**, and the differences are essential:
    ///
    /// | Table | Plain | Iterative |
    /// | --- | --- | --- |
    /// | `x` | task × executor, built once | **one set per round** (`x_0`, `x_1`, …), added by `AddColumns` |
    /// | `taskCompilation[t]` | `y[t] + Σ_e x[t, e]`, forced `== 1` | **`y[t]` alone** |
    /// | `executorCompilation[e]` | `OR(x[t, e] for t)` + `z[e]` | **`xor[e]` + `z[e]`** |
    ///
    /// Both differences follow from "the columns do not exist yet":
    ///
    /// - `taskCompilation` must **not** contain `Σ x`: `x` is appended per round and the round count is unknown at registration. Writing `Σ x` freezes an empty sum at
    ///   registration, and every later round's columns stay out of that equation.
 /// - `executorCompilation` must **not** be `OR(x)`, same reason. This implementation uses a standalone `xor[e]` whose meaning is pinned by the cross-round conflict constraints
    ///    and the `xor` lower bound , not computed by an `OR` function.
    ///
    /// Copying the plain compilation's shape into the iterative one still **solves** — `taskCompilation` just equals `y`, so "a task is done or cancelled" degrades to "a task is
    /// cancelled": every task gets cancelled with no error at all.
    /// </remarks>
    public Try Register(object model) {
        if (model is not IAbstractLinearMetaModel<Flt64> linear) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "任务编译只能注册到线性元模型 / a task compilation can only be registered into a linear meta model"));
        }

        // 已经在**这个**模型上注册过：再注册一次是空操作。见 的同一条规则。
        // 换模型则复用同一批符号，只在新模型上重新登记一次——否则第二个模型一个符号都没有。
        //
        // Already registered on **this** model: registering again is a no-op, the same rule. Another model reuses the very same symbols and merely registers them there —
        // otherwise the second model has no symbols at all.
        foreach (object registered in _registeredModels) {
            if (ReferenceEquals(registered, model)) {
                return Results.Ok<Success>(Results.SuccessInstance);
            }
        }
        if (_executorCompilationCombination is not null) {
            Try reattached = ReattachTo(linear);
            if (reattached.IsOk) {
                _registeredModels.Add(model);
            }
            return reattached;
        }

        var originList = _originTasks.Cast<IAbstractTask<E, A>>().ToList();

        (BinVariable1 cancelVariable, IReadOnlyDictionary<IAbstractTask<E, A>, object> cancelTable) =
            CompilationTable.BuildCancelColumns(originList, "y");
        for (int index = 0; index < originList.Count; ++index) {
            if (_originTasks[index] is { } locked && _lockedCancelTasks.Contains(locked)) {
                cancelVariable[index].Range.Eq(true);
            }
        }
        Try added = linear.Add(cancelVariable.Items);
        if (!added.IsOk) {
            return added;
        }
        _cancellationVariable = cancelVariable;
        _cancellationTable = cancelTable;

        var taskCost = new LinearExpressionSymbol(new MutableLinearPolynomial<Flt64>(null, Flt64.Zero), name: "bunch_cost");
        added = linear.Add(taskCost);
        if (!added.IsOk) {
            return added;
        }
        _taskCost = taskCost;

        var assignmentCombination = new SymbolCombination<LinearExpressionSymbol, Shape2>(
            "task_assignment",
            Shape2.Invoke(originList.Count, _executors.Count),
            (index, vector) => new LinearExpressionSymbol(
                new MutableLinearPolynomial<Flt64>(null, Flt64.Zero),
                name: "task_assignment_" + string.Join("_", vector)));
        added = linear.Add(assignmentCombination);
        if (!added.IsOk) {
            return added;
        }
        _assignmentCombination = assignmentCombination;

        var compilationCombination = new SymbolCombination<LinearExpressionSymbol, Shape1>(
            "task_compilation",
            Shape1.Invoke(originList.Count),
            (index, _) => new LinearExpressionSymbol(
                new MutableLinearPolynomial<Flt64>(
                    new[] { new LinearMonomial<Flt64>(Flt64.One, cancelVariable[index]) },
                    Flt64.Zero),
                name: "task_compilation_" + index));
        added = linear.Add(compilationCombination);
        if (!added.IsOk) {
            return added;
        }
        _compilationCombination = compilationCombination;

        var xor = new BinVariable1("xor", _executors.Count);
        added = linear.Add(xor.Items);
        if (!added.IsOk) {
            return added;
        }
        _xorVariable = xor;

        var leisure = new BinVariable1("z", _executors.Count);
        added = linear.Add(leisure.Items);
        if (!added.IsOk) {
            return added;
        }
        _leisureVariable = leisure;

        var executorCombination = new SymbolCombination<LinearExpressionSymbol, Shape1>(
            "executor_compilation",
            Shape1.Invoke(_executors.Count),
            (index, _) => new LinearExpressionSymbol(
                new MutableLinearPolynomial<Flt64>(
                    new[] {
                        new LinearMonomial<Flt64>(Flt64.One, xor[index]),
                        new LinearMonomial<Flt64>(Flt64.One, leisure[index]),
                    },
                    Flt64.Zero),
                name: "executor_compilation_" + _executors[index].Id));
        added = linear.Add(executorCombination);
        if (!added.IsOk) {
            return added;
        }

        var executorTable = new Dictionary<E, object>();
        for (int index = 0; index < _executors.Count; ++index) {
            executorTable[_executors[index]] = executorCombination[index];
        }
        _executorCompilationTable = executorTable;
        _executorCompilationCombination = executorCombination;

        _registeredModels.Add(model);
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 把已建好的符号重新登记到另一个模型 / Re-register the already-built symbols into another model.
    /// </summary>
    /// <param name="linear">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    /// <remarks>
    /// 迭代编译的符号在**第一次**注册时就都建好了（`y`、`bunch_cost`、两张聚合表、`xor`、`z`、执行器编译，以及每轮的 `x`）。
    /// 换模型时**一个都不能少**：漏掉 `xor` 会让执行器使用不受约束，漏掉 `bunch_cost` 会让目标里没有这一项——
    /// 而两者都**不报错**，只是第二个模型的解更便宜。
    ///
    /// Every iterative symbol is built on the **first** registration (`y`, `bunch_cost`, the two aggregate tables, `xor`, `z`, the executor compilation, and each round's `x`).
    /// Switching models must miss **none of them**: dropping `xor` leaves executor usage unconstrained, dropping `bunch_cost` leaves it out of the objective — **neither raises
    /// anything**, the second model's solution is just cheaper.
    /// </remarks>
    private Try ReattachTo(IAbstractLinearMetaModel<Flt64> linear) {
        Try added = linear.Add(_cancellationVariable!.Items);
        if (!added.IsOk) {
            return added;
        }
        added = linear.Add(_taskCost!);
        if (!added.IsOk) {
            return added;
        }
        added = linear.Add(_assignmentCombination!);
        if (!added.IsOk) {
            return added;
        }
        added = linear.Add(_compilationCombination!);
        if (!added.IsOk) {
            return added;
        }
        added = linear.Add(_xorVariable!.Items);
        if (!added.IsOk) {
            return added;
        }
        added = linear.Add(_leisureVariable!.Items);
        if (!added.IsOk) {
            return added;
        }
        added = linear.Add(_executorCompilationCombination!);
        if (!added.IsOk) {
            return added;
        }
        // 已回收的列**不实现**：它在原模型上被固定为 0 并摘掉了，实现到新模型上等于把它复活。
        // Retired columns are **not moved**: they were pinned to 0 and dropped in the original model, and moving them would revive them.
        foreach (BinVariable1 round in _x) {
            added = linear.Add(round.Items.Where(item => !_retiredVariables.Contains(item)));
            if (!added.IsOk) {
                return added;
            }
        }
        // 引用已回收变量的约束**也不实现**：实现出来就是一条指向不存在变量的约束。
        // Constraints referring to a retired variable are **not moved** either: moving them would be a row pointing at a variable that is not there.
        foreach ((LinearInequality<Flt64> inequality, string name) in _columnConstraints) {
            if (MentionsRetired(inequality)) {
                continue;
            }
            added = linear.AddConstraint(
                inequality,
                group: null,
                name: name);
            if (!added.IsOk) {
                return added;
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 约束是否引用了已回收的变量 / Whether the constraint refers to a retired variable.
    /// </summary>
    /// <param name="inequality">约束 / The constraint.</param>
    /// <returns>是否引用 / Whether it refers to one.</returns>
    /// <remarks>
    /// 判据必须看**两侧的多项式**，不能只看名字：约束名里带的是任务与轮次，与变量名不一定同形
    /// （`xor_E1_0_T1` vs `x_0_T1`）。按名字匹配会在改了命名以后**静默失效**。
    ///
    /// 右侧分支目前**走不到**：本类加的两族约束（`xor ≥ x`、`x + x' ≤ 1`）变量都在左侧、右侧是常数。
    /// 保留它是防御性的——将来若加一条把变量写在右侧的约束，按名字或只扫左侧都会漏。
    /// 这一分支因此是**等价变异**（见计划文档 的变异表），不是判据缺口。
    ///
    /// The right-hand branch is currently **unreachable**: both constraint families this class adds (`xor ≥ x`, `x + x' ≤ 1`) carry their variables on the left with a constant on the
    /// right. It is kept defensively — a future constraint writing variables on the right would slip past either a name match or a left-side-only scan. That branch is therefore an
    /// **equivalent mutant** (see the mutation table in the plan document), not a criterion gap.
    ///
    /// The test must look at **both sides' polynomials**, not the name: a constraint name carries the task and round, which need not match the variable name (`xor_E1_0_T1` vs
    /// `x_0_T1`). Matching by name would **fail silently** the moment the naming changes.
    /// </remarks>
    private bool MentionsRetired(LinearInequality<Flt64> inequality) =>
        inequality.Lhs.Monomials.Any(monomial =>
            monomial.Symbol is IVariableItem variable && _retiredVariables.Contains(variable))
        || inequality.Rhs.Monomials.Any(monomial =>
            monomial.Symbol is IVariableItem variable && _retiredVariables.Contains(variable));

    /// <summary>
    /// 加入一轮新列 / Add one round of new columns.
    /// </summary>
    /// <param name="iteration">迭代轮次 / The iteration number.</param>
    /// <param name="newTasks">本轮新任务 / This round's new tasks.</param>
    /// <param name="model">元模型 / The meta model.</param>
    /// <param name="costOf">每列代价，`null` 表示该列不可定价 / Per-column cost; null means the column cannot be priced.</param>
    /// <param name="conflict">两列是否冲突 / Whether two columns conflict.</param>
    /// <returns>去重后实际加入的任务 / The tasks actually added after deduplication.</returns>
    /// <remarks>
    /// `addColumns` 做五件事，顺序不能换：
    ///
    /// | 步骤 | 为什么这个顺序 |
    /// | --- | --- |
    /// | 1. 建 `x_iteration` 并进模型 | 后面所有式子都要引用它 |
    /// | 2. `taskCost.flush` 后加本轮代价 | **先清空**：目标里只保留最近一轮（已记录这条 ceiling） |
    /// | 3. 重写 `taskAssignment`（按 `(key, executor)`） | 分组键见 |
    /// | 4. 重写 `taskCompilation`（只按 `key`） | 同上 |
    /// | 5. `xor[e] ≥ x[t]` 与跨轮冲突 | 这两条是**约束**，必须在列进模型之后 |
    ///
    /// 第 2 步的 `flush` 与第 3、4 步的 `flush` 是同一个陷阱：**同一原始任务在新一轮拿到新列时，之前轮次的项会被清掉**。
    /// 本实现正是这么做的，记录在此而不擅自"修好"——修好会改变已固定的行为。
    ///
    /// `AddColumns` does five things, and the order is not free:
    ///
    /// | Step | Why this order |
    /// | --- | --- |
    /// | 1. build `x_iteration` and add it to the model | everything below references it |
    /// | 2. `taskCost.flush` then add this round's cost | **clear first**: the objective keeps only the latest round (ceiling already recorded) |
    /// | 3. rewrite `taskAssignment` (by `(key, executor)`) | grouping key per |
    /// | 4. rewrite `taskCompilation` (by `key` only) | same |
    /// | 5. `xor[e] ≥ x[t]` and the cross-round conflicts | these are **constraints** and must come after the columns enter the model |
    ///
    /// The `flush` in step 2 and those in steps 3–4 are the same trap: **when an origin task gains new columns in a later round, the earlier rounds' terms are cleared**. The
    /// implementation does exactly this and records it rather than "fixing" it, since fixing would change the pinned behaviour.
    /// </remarks>
    public Result<IReadOnlyList<IT>, ErrorCode, Error<ErrorCode>> AddColumns(
        ulong iteration,
        IReadOnlyList<IT> newTasks,
        object model,
        Func<IT, Flt64?> costOf,
        Func<IT, IT, bool> conflict) {
        ArgumentNullException.ThrowIfNull(newTasks);
        ArgumentNullException.ThrowIfNull(costOf);
        ArgumentNullException.ThrowIfNull(conflict);

        if (model is not IAbstractLinearMetaModel<Flt64> linear) {
            return Results.Failed<IReadOnlyList<IT>>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "列只能加入线性元模型 / columns can only be added to a linear meta model"));
        }
        if (_taskCost is null || _assignmentCombination is null || _compilationCombination is null || _xorVariable is null) {
            return Results.Failed<IReadOnlyList<IT>>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                "尚未注册就加列 / columns cannot be added before registration"));
        }

        IReadOnlyList<IT> unduplicated = _aggregation.AddColumns(newTasks);

        var xi = new BinVariable1("x_" + iteration, unduplicated.Count);
        for (int index = 0; index < unduplicated.Count; ++index) {
            xi[index].Name = xi.Name + "_" + unduplicated[index].Id;
        }
        Try added = linear.Add(xi.Items);
        if (!added.IsOk) {
            return Fail(added);
        }
        _x.Add(xi);

        // 2. 代价：先清空再加本轮。
        // Cost: clear, then add this round.
        MutableLinearPolynomial<Flt64> costPolynomial = _taskCost.AsMutable();
        costPolynomial.Clear();
        for (int index = 0; index < unduplicated.Count; ++index) {
            if (costOf(unduplicated[index]) is not { } weight) {
                // 无穷大，而 `Flt64` 没有无穷（的同一处限制），所以这里失败而不是静默当零。
 // This implementation uses infinity, which `Flt64` cannot represent (the same limit), so this fails instead of silently using zero.
                return Results.Failed<IReadOnlyList<IT>>(new Err<ErrorCode>(
                    ErrorCode.ApplicationError,
                    "任务代价提取失败，该列不可定价 / Task cost extraction failed; this column cannot be priced"));
            }
            costPolynomial.AddMonomial(new LinearMonomial<Flt64>(weight, xi[index]));
        }

        RewriteAggregates(unduplicated, xi);

        // 5. `xor[e] ≥ x[t]`：某列被选中意味着它的执行器被用过。
        //    `xor[e] ≥ x[t]`: selecting a column means its executor was used.
        for (int index = 0; index < unduplicated.Count; ++index) {
            int executorIndex = -1;
            for (int candidate = 0; candidate < _executors.Count; ++candidate) {
                if (_executors[candidate].Equals(unduplicated[index].Executor)) {
                    executorIndex = candidate;
                    break;
                }
            }
            if (executorIndex < 0) {
                return Results.Failed<IReadOnlyList<IT>>(new Err<ErrorCode>(
                    ErrorCode.ApplicationError,
                    "列的執行器不在执行器列表里 / the column's executor is not in the executor list"));
            }
            var row = new MutableLinearPolynomial<Flt64>(null, Flt64.Zero);
            row.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, _xorVariable[executorIndex]));
            row.AddMonomial(new LinearMonomial<Flt64>(Flt64.Zero.Minus(Flt64.One), xi[index]));
            var zero = new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero);
            LinearInequality<Flt64> inequality = row.ToLinearPolynomial().Ge(zero);
            Try constrained = linear.AddConstraint(
                inequality,
                group: null,
                name: "xor_" + _executors[executorIndex].Id + "_" + iteration + "_" + unduplicated[index].Id);
            if (!constrained.IsOk) {
                return Fail(constrained);
            }
            _columnConstraints.Add((inequality, "xor_" + _executors[executorIndex].Id + "_" + iteration + "_" + unduplicated[index].Id));
        }

        // 6. 跨轮冲突：本轮每列与**之前每一轮**的冲突列不能同时选中。
        // Cross-round conflicts: no column of this round may coexist with a conflicting column from any earlier round.
        for (int index = 0; index < unduplicated.Count; ++index) {
            for (int otherIteration = 0; otherIteration < _aggregation.TasksIteration.Count; ++otherIteration) {
                IReadOnlyList<IT> otherTasks = _aggregation.TasksIteration[otherIteration];
                for (int otherIndex = 0; otherIndex < otherTasks.Count; ++otherIndex) {
                    if (!CrossRoundConflict.ShouldAdd(
                            sameTask: ReferenceEquals(unduplicated[index], otherTasks[otherIndex])
                                || unduplicated[index].Equals(otherTasks[otherIndex]),
                            conflict: conflict(unduplicated[index], otherTasks[otherIndex]))) {
                        continue;
                    }
                    var row = new MutableLinearPolynomial<Flt64>(null, Flt64.Zero);
                    row.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, xi[index]));
                    row.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, _x[otherIteration][otherIndex]));
                    LinearInequality<Flt64> inequality = row.ToLinearPolynomial().Le(new LinearPolynomial<Flt64>(
                        Array.Empty<LinearMonomial<Flt64>>(),
                        new Flt64(CrossRoundConflict.RightHandSide)));
                    Try constrained = linear.AddConstraint(
                        inequality,
                        group: null,
                        name: "task_conflict_" + unduplicated[index].Id + "_" + otherIteration + "_" + otherTasks[otherIndex].Id);
                    if (!constrained.IsOk) {
                        return Fail(constrained);
                    }
                    _columnConstraints.Add((inequality, "task_conflict_" + unduplicated[index].Id + "_" + otherIteration + "_" + otherTasks[otherIndex].Id));
                }
            }
        }

        return Results.Ok<IReadOnlyList<IT>>(unduplicated);
    }

    /// <summary>
    /// 重写两张聚合表 / Rewrite the two aggregate tables.
    /// </summary>
    /// <param name="unduplicated">本轮新增的任务 / This round's new tasks.</param>
    /// <param name="xi">本轮变量 / This round's variable.</param>
    /// <remarks>
    /// 分组键由 的 `AssignmentAggregation` 决定，**没有第二份规则**：
    /// 分配按 `(key, executor)`，编译只按 `key`。空组不动聚合。
    ///
    /// The grouping keys come from the earlier `AssignmentAggregation`; there is **no second rule**: assignment by `(key, executor)`, compilation by `key` alone. An empty group leaves
    /// the aggregate untouched.
    /// </remarks>
    private void RewriteAggregates(IReadOnlyList<IT> unduplicated, BinVariable1 xi) {
        if (_assignmentCombination is null || _compilationCombination is null) {
            return;
        }

        for (int originIndex = 0; originIndex < _originTasks.Count; ++originIndex) {
            T origin = _originTasks[originIndex];
            for (int executorIndex = 0; executorIndex < _executors.Count; ++executorIndex) {
                E executor = _executors[executorIndex];
                var thisTasks = new List<int>();
                for (int index = 0; index < unduplicated.Count; ++index) {
                    if (AssignmentAggregation.MatchesAssignment(
                            originKey: origin.Key,
                            originExecutor: executor,
                            columnKey: unduplicated[index].Key,
                            columnExecutor: unduplicated[index].Executor!,
                            sameKey: static (left, right) => left == right,
                            sameExecutor: static (left, right) => left.Equals(right))) {
                        thisTasks.Add(index);
                    }
                }
                if (!AssignmentAggregation.ShouldRewrite(thisTasks.Count)) {
                    continue;
                }
                MutableLinearPolynomial<Flt64> polynomial =
                    _assignmentCombination[originIndex * _executors.Count + executorIndex].AsMutable();
                polynomial.Clear();
                foreach (int index in thisTasks) {
                    polynomial.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, xi[index]));
                }
            }

            var compilationTasks = new List<int>();
            for (int index = 0; index < unduplicated.Count; ++index) {
                if (AssignmentAggregation.MatchesCompilation(
                        originKey: origin.Key,
                        columnKey: unduplicated[index].Key,
                        sameKey: static (left, right) => left == right)) {
                    compilationTasks.Add(index);
                }
            }
            if (!AssignmentAggregation.ShouldRewrite(compilationTasks.Count)) {
                continue;
            }
            MutableLinearPolynomial<Flt64> compilation =
                _compilationCombination[originIndex].AsMutable();
            compilation.Clear();
            foreach (int index in compilationTasks) {
                compilation.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, xi[index]));
            }
        }
    }

    /// <summary>把 `Try` 失败转成结果失败 / Turn a `Try` failure into a failed result.</summary>
    /// <param name="tryResult">操作结果 / The operation result.</param>
    /// <returns>失败结果 / A failed result.</returns>
    private static Result<IReadOnlyList<IT>, ErrorCode, Error<ErrorCode>> Fail(Try tryResult) =>
        tryResult switch {
            Failed<Success, ErrorCode, Error<ErrorCode>> failed => Results.Failed<IReadOnlyList<IT>>(failed.Error),
            Fatal<Success, ErrorCode, Error<ErrorCode>> fatal =>
                new Fatal<IReadOnlyList<IT>, ErrorCode, Error<ErrorCode>>(fatal.Errors),
            _ => Results.Failed<IReadOnlyList<IT>>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                "加列失败 / adding columns failed")),
        };

    /// <summary>
    /// 回收无用列并给出下一轮的约简成本上限 / Retire useless columns and return the next round's reduced-cost cutoff.
    /// </summary>
    /// <param name="maximumReducedCost">当前约简成本上限 / The current reduced-cost cutoff.</param>
    /// <param name="maximumColumnAmount">列数上限 / The column-count ceiling.</param>
    /// <param name="reducedCost">每列的约简成本 / Per-column reduced cost.</param>
    /// <param name="fixedTasks">固定保留的任务 / Tasks pinned for good.</param>
    /// <param name="keptTasks">本轮要求保留的任务 / Tasks to keep this round.</param>
    /// <param name="model">元模型 / The meta model.</param>
    /// <returns>下一轮的约简成本上限 / the next round's reduced-cost cutoff.</returns>
    /// <remarks>
    /// `RemoveColumns` 的形状：
    ///
    /// | 步骤 | 关键点 |
    /// | --- | --- |
    /// | 1. 按检验数挑出可回收的列 | `fixed` 与 `kept` 双否决 |
    /// | 2. 对**所有**已移除任务先固定为 0、再摘变量 | 不是只处理本轮新删的：遍历整个 `removedTasks` |
    /// | 3. 按剩余列数决定下一轮上限 | 超上限才衰减（`IterativeColumnPruning`） |
    ///
    /// 第 2 步"先固定为 0 再 `remove`"是**顺序敏感**的：`remove` 只摘变量，不摘引用它的约束。
    /// 若 `remove` 因任何原因没生效，固定为 0 仍保证该列选不上；反过来写就留下一个可被选中的"已回收"列。
    ///
    /// The shape of `RemoveColumns`:
    ///
    /// | Step | Key point |
    /// | --- | --- |
    /// | 1. pick the retireable columns by reduced cost | `fixed` and `kept` both veto  |
    /// | 2. pin **every** removed task to 0, then drop the variable | not just this round's: the whole `removedTasks` is walked |
    /// | 3. derive the next cutoff from the remaining count | decay only above the ceiling (`IterativeColumnPruning`) |
    ///
    /// Step 2's "pin to 0 first, then `remove`" is **order-sensitive**: `remove` drops the variable, not the constraints referring to it. If `remove` fails to take effect for any
    /// reason, the pin still keeps the column unselectable; the other order leaves a "retired" column that can still be selected.
    /// </remarks>
    public Result<Flt64, ErrorCode, Error<ErrorCode>> RemoveColumns(
        Flt64 maximumReducedCost,
        ulong maximumColumnAmount,
        Func<IT, Flt64> reducedCost,
        IReadOnlySet<IT> fixedTasks,
        IReadOnlySet<IT> keptTasks,
        object model) {
        ArgumentNullException.ThrowIfNull(reducedCost);
        ArgumentNullException.ThrowIfNull(fixedTasks);
        ArgumentNullException.ThrowIfNull(keptTasks);

        if (model is not IAbstractLinearMetaModel<Flt64> linear) {
            return Results.Failed<Flt64>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "列只能从线性元模型回收 / columns can only be retired from a linear meta model"));
        }
        if (_x.Count == 0) {
            return Results.Failed<Flt64>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                "尚未加列就回收 / columns cannot be retired before any round was added"));
        }

        foreach (IT task in _aggregation.Tasks.ToList()) {
            if (ColumnRetirement.ShouldRetire(
                    reducedCost: reducedCost(task).ToDouble(),
                    maximumReducedCost: maximumReducedCost.ToDouble(),
                    @fixed: fixedTasks.Contains(task),
                    kept: keptTasks.Contains(task),
                    alreadyRemoved: _aggregation.RemovedTasks.Contains(task))) {
                _aggregation.RemoveColumn(task);
            }
        }

        foreach (IT task in _aggregation.RemovedTasks) {
            IReadOnlyList<IT> roundTasks = _aggregation.TasksIteration[(int)task.Iteration];
            BinVariable1 round = ColumnRetirement.ForIteration(_x, task.Iteration);
            int taskIndex = -1;
            for (int index = 0; index < roundTasks.Count; ++index) {
                if (roundTasks[index].Equals(task)) {
                    taskIndex = index;
                    break;
                }
            }
            if (taskIndex < 0) {
                return Results.Failed<Flt64>(new Err<ErrorCode>(
                    ErrorCode.ApplicationError,
                    "已移除任务不在自己轮次的列里 / a removed task is absent from its own round's columns"));
            }
            round[taskIndex].Range.Eq(false);
            linear.Remove(round[taskIndex]);
            _retiredVariables.Add(round[taskIndex]);
        }

        return Results.Ok<Flt64>(IterativeColumnPruning.NextMaximumReducedCost(
            remainingAmount: _aggregation.Tasks.Count,
            maximumColumnAmount: maximumColumnAmount,
            maximumReducedCost: maximumReducedCost));
    }
}
