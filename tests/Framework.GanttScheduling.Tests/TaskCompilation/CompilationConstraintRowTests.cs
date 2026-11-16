// 编译约束真的加行/ The compilation constraints really add rows.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service.Limits;
using Fuookami.Ospf.Framework.GanttScheduling.Tests.BunchCompilation;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.MultiArray;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;
using LinearCategory = Fuookami.Ospf.Math.Symbol.LinearCategory;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.TaskCompilation;

/// <summary>
/// **编译约束真的加行** / The compilation constraints really add rows.
/// </summary>
/// <remarks>
/// 两条编译约束以前**只做校验、不加任何行**：检查表里有没有每个条目，然后返回 `Ok`。
/// `invoke` 是 `model.addConstraint(x[item] eq 1, args =...ShadowPriceKey(item))`——也就是"每个条目恰好编译一次"这条要求本身。
/// 少了它，编译量可以取任意值（包括零），排程照样"成功"，只在产出上悄悄缩水。
///
/// 两条都还带着一个**已记录的垫层**：`CompilationTable.Build` 填的是 `new object`，占位对象进不了模型，
/// 所以真正建行要等那张表交出真变量。判据因此**自己造真符号**——能力本身必须被钉住，不能靠垫层缺席来"通过"。
///
/// Both constraints used to **validate only and add no row**: check that the table has every item, then return `Ok`. `invoke` is
/// `model.addConstraint(x[item] eq 1, args =...ShadowPriceKey(item))` — the requirement itself, that **each item is compiled exactly once**. Without it the compiled quantity may take any
/// value (zero included) while the schedule still "succeeds", quietly shrinking output. Both also carry a **recorded ceiling**: `CompilationTable.Build` fills `new object`, and a placeholder
/// cannot enter a model, so real rows await a table that hands out real variables. The criteria therefore **mint real symbols themselves** — the capability must be pinned rather than "passing"
/// because the ceiling happened to be absent.
/// </remarks>
public class CompilationConstraintRowTests {
    private sealed class FakeCompilation<T>
        : Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model.ICompilation,
          Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model.ITaskCompilation<TestExecutor, TestAssignmentPolicy>,
          Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model.IExecutorCompilation<TestExecutor>
        where T : IAbstractTask<TestExecutor, TestAssignmentPolicy> {
        public FakeCompilation(
            IReadOnlyDictionary<IAbstractTask<TestExecutor, TestAssignmentPolicy>, object> tasks,
            IReadOnlyDictionary<TestExecutor, object> executors) {
            TaskCompilationTable = tasks;
            ExecutorCompilation = executors;
        }

        public bool TaskCancelEnabled => false;

        public bool WithExecutorLeisure => false;

        public IReadOnlyDictionary<IAbstractTask<TestExecutor, TestAssignmentPolicy>, object> TaskCompilationTable { get; }

        public IReadOnlyDictionary<TestExecutor, object> ExecutorCompilation { get; }

        public Try Register(object model) => Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>造 n 个真正的线性表达式符号 / Mint n real linear expression symbols.</summary>
    private static IReadOnlyList<LinearExpressionSymbol> Symbols(string name, int count) {
        var combination = new SymbolCombination<LinearExpressionSymbol, Shape1>(
            name, Shape1.Invoke(count), (_, _) => new LinearExpressionSymbol(new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero)));
        var symbols = new List<LinearExpressionSymbol>(count);
        for (int index = 0; index < count; ++index) {
            symbols.Add(combination[index]);
        }
        return symbols;
    }

    private static TestTask Task(string id) => new(id, id);

    private static IReadOnlyList<MathConstraint> Rows(LinearMetaModel<Flt64> model, string prefix) =>
        ((IMetaModel<Flt64>)model).Constraints
            .Where(row => row.Name.StartsWith(prefix, StringComparison.Ordinal))
            .ToList();

    /// <summary>
    /// 行的左端只有一个单元格 / The row's left side holds exactly one cell.
    /// </summary>
    /// <remarks>
    /// `MathConstraint` 不暴露原始的 `LinearInequality`，只给 `Lhs` / `Sign` / `Rhs`。单元格的**身份**足以区分
    /// "每行用各自的符号"和"每行都用第一个符号"——后者会让所有任务的编译量绑在同一个变量上。
    ///
    /// `MathConstraint` does not expose the original `LinearInequality`, only `Lhs` / `Sign` / `Rhs`. The cell's **identity** is enough to tell "each row uses its own symbol" from
    /// "every row uses the first" — the latter would tie every task's compiled quantity to one variable.
    /// </remarks>
    private static Fuookami.Ospf.Math.Symbol.Polynomial.LinearPolynomial<Flt64> Left(MathConstraint row) =>
        ((LinearInequalityConstraint<Flt64>)row).Inequality.Lhs;

    /// <summary>
    /// **每个任务一行 `x = 1`，且行上带键** / One `x = 1` row per task, keyed.
    /// </summary>
    /// <remarks>
    /// 五点成对：
    ///
    /// 1. 行数**等于任务数**——少一行就有任务可以不被编译。
    /// 2. 每行左端**只有一个单项式、系数为 1**，且那个符号就是该任务的表达式（`x = 1`，不是 `2x = 1` 也不是 `x ≤ 1`）。
    /// 3. 比较方向是 **EQ**。
    /// 4. 右端常数是 **1**。
    /// 5. 每行都带 `TaskCompilationShadowPriceKey`，键里的任务就是该行对应的任务——没有键就取不到对偶，提取器读的是一张空表。
    ///
    /// Five paired points: the **row count equals the task count**, since one missing row means a task may go uncompiled; each row's left side holds **one monomial with coefficient 1**, that
    /// symbol being the task's expression (`x = 1`, not `2x = 1` and not `x <= 1`); the comparison is **EQ**; the right-hand constant is **1**; and every row carries a
    /// `TaskCompilationShadowPriceKey` naming that task, without which no dual can be collected and the extractor reads an empty table.
    /// </remarks>
    [Fact]
    public void TaskCompilation_ShouldAddOneEqualityRowPerTask() {
        var tasks = new List<TestTask> { Task("T1"), Task("T2") };
        IReadOnlyList<LinearExpressionSymbol> symbols = Symbols("task_compilation_x", tasks.Count);
        var table = new Dictionary<IAbstractTask<TestExecutor, TestAssignmentPolicy>, object>();
        for (int index = 0; index < tasks.Count; ++index) {
            table[tasks[index]] = symbols[index];
        }
        var compilation = new FakeCompilation<TestTask>(table, new Dictionary<TestExecutor, object>());
        var constraint = new TaskCompilationConstraint<TestExecutor, TestAssignmentPolicy>(tasks, compilation);
        using var model = new LinearMetaModel<Flt64>();

        constraint.Invoke(model).IsOk.Should().BeTrue();

        IReadOnlyList<MathConstraint> rows = Rows(model, "task_compilation");
        rows.Should().HaveCount(tasks.Count, "a task without its row may go uncompiled");
        Left(rows[0]).Monomials[0].Symbol.Should().NotBeSameAs(Left(rows[1]).Monomials[0].Symbol,
            "each row must use its own task's symbol");
        for (int index = 0; index < rows.Count; ++index) {
            LinearPolynomial<Flt64> left = Left(rows[index]);
            left.Monomials.Should().ContainSingle("the row is `x = 1`, not a sum");
            left.Monomials[0].Coefficient.ToDouble().Should().Be(1.0);
            left.Monomials[0].Symbol.Should().BeSameAs(symbols[index]);
            var inequality = (LinearInequalityConstraint<Flt64>)rows[index];
            inequality.Inequality.Comparison.Should().Be(Comparison.EQ);
            inequality.Inequality.Rhs.Constant.ToDouble().Should().Be(1.0);

            rows[index].Args.Should().BeOfType<TaskCompilationShadowPriceKey<TestExecutor, TestAssignmentPolicy>>();
            var key = (TaskCompilationShadowPriceKey<TestExecutor, TestAssignmentPolicy>)rows[index].Args!;
            key.Task.Should().BeSameAs(tasks[index]);
        }
    }

    /// <summary>
    /// **每个执行器一行 `x = 1`，且行上带键** / One `x = 1` row per executor, keyed.
    /// </summary>
    /// <remarks>
    /// 四点成对：行数等于执行器数 / 左端单项式系数 1 / 方向 EQ、右端 1 / 行带 `ExecutorCompilationShadowPriceKey`。
    /// 少一行的后果与任务侧不同——那个班次**一个人都不用**也能排上，只在产出上缩水。
    ///
    /// Four paired points: the row count equals the executor count, the left monomial has coefficient 1, the direction is EQ with right side 1, and the row carries an
    /// `ExecutorCompilationShadowPriceKey`. A missing row hurts differently from the task side: that shift may run **using nobody** and still be scheduled, shrinking output only.
    /// </remarks>
    [Fact]
    public void ExecutorCompilation_ShouldAddOneEqualityRowPerExecutor() {
        var executors = new List<TestExecutor> { new("E1", "E1"), new("E2", "E2") };
        IReadOnlyList<LinearExpressionSymbol> symbols = Symbols("executor_compilation_x", executors.Count);
        var table = new Dictionary<TestExecutor, object>();
        for (int index = 0; index < executors.Count; ++index) {
            table[executors[index]] = symbols[index];
        }
        var compilation = new FakeCompilation<TestTask>(new Dictionary<IAbstractTask<TestExecutor, TestAssignmentPolicy>, object>(), table);
        var constraint = new ExecutorCompilationConstraint<TestExecutor, TestAssignmentPolicy>(executors, compilation);
        using var model = new LinearMetaModel<Flt64>();

        constraint.Invoke(model).IsOk.Should().BeTrue();

        IReadOnlyList<MathConstraint> rows = Rows(model, "executor_compilation");
        rows.Should().HaveCount(executors.Count, "an executor without its row may be left unused");
        Left(rows[0]).Monomials[0].Symbol.Should().NotBeSameAs(Left(rows[1]).Monomials[0].Symbol,
            "each row must use its own executor's symbol");
        for (int index = 0; index < rows.Count; ++index) {
            LinearPolynomial<Flt64> left = Left(rows[index]);
            left.Monomials.Should().ContainSingle("the row is `x = 1`, not a sum");
            left.Monomials[0].Coefficient.ToDouble().Should().Be(1.0);
            left.Monomials[0].Symbol.Should().BeSameAs(symbols[index]);
            var inequality = (LinearInequalityConstraint<Flt64>)rows[index];
            inequality.Inequality.Comparison.Should().Be(Comparison.EQ);
            inequality.Inequality.Rhs.Constant.ToDouble().Should().Be(1.0);

            rows[index].Args.Should().BeOfType<ExecutorCompilationShadowPriceKey<TestExecutor>>();
            ((ExecutorCompilationShadowPriceKey<TestExecutor>)rows[index].Args!).Executor.Should().BeSameAs(executors[index]);
        }
    }

    /// <summary>
    /// **占位表达式不加行，但也不算失败** / A placeholder adds no row and is not a failure either.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 表里是 `new object` 时 `Invoke` 返回 **`Ok`**——占位是 `CompilationTable.Build` 的当前正常状态，改成失败会把所有分配打回。
    /// 2. 模型里**一行都没有**，不能凭空造一条约束。
    /// 3. 换成真符号后同一个调用就建出真行——所以"没有行"是因为垫层，不是因为这条路径不存在。
    ///
    /// Three paired points: with `new object` in the table `Invoke` returns **`Ok`**, since a placeholder is `CompilationTable.Build`'s normal state today and failing would reject every
    /// assignment; the model has **no rows**, and no constraint may be invented; and switching to real symbols makes the same call build real rows, so "no rows" is the ceiling rather than a
    /// missing path.
    /// </remarks>
    [Fact]
    public void TaskCompilation_ShouldSkipAPlaceholderWithoutFailing() {
        var tasks = new List<TestTask> { Task("T1") };
        var placeholder = new Dictionary<IAbstractTask<TestExecutor, TestAssignmentPolicy>, object> {
            [tasks[0]] = new object(),
        };
        var constraint = new TaskCompilationConstraint<TestExecutor, TestAssignmentPolicy>(
            tasks, new FakeCompilation<TestTask>(placeholder, new Dictionary<TestExecutor, object>()));
        using var model = new LinearMetaModel<Flt64>();

        constraint.Invoke(model).IsOk.Should().BeTrue("a placeholder is the normal state today");
        Rows(model, "task_compilation").Should().BeEmpty();

        IReadOnlyList<LinearExpressionSymbol> symbols = Symbols("task_compilation_x", 1);
        var real = new Dictionary<IAbstractTask<TestExecutor, TestAssignmentPolicy>, object> {
            [tasks[0]] = symbols[0],
        };
        var second = new TaskCompilationConstraint<TestExecutor, TestAssignmentPolicy>(
            tasks, new FakeCompilation<TestTask>(real, new Dictionary<TestExecutor, object>()));
        second.Invoke(model).IsOk.Should().BeTrue();
        Rows(model, "task_compilation").Should().ContainSingle("real symbols do build the row");
    }

    /// <summary>
    /// **`Refresh` 按键收价，提取器按任务 / 执行器取回** / `Refresh` collects by key and the extractors answer per task / executor.
    /// </summary>
    /// <remarks>
    /// 四点成对：两条管线各把自己的行写进映射 / 互不认领 / 提取器认自己的键得自己的价 / 没被定价的查询得零。
    ///
    /// Four paired points: each pipeline writes its own row into the map, neither claims the other's, each extractor resolves its own key to its own price, and an unpriced query reads zero.
    /// </remarks>
    [Fact]
    public void BothCompilationConstraints_ShouldTurnDualsIntoPrices() {
        var tasks = new List<TestTask> { Task("T1") };
        var executors = new List<TestExecutor> { new("E1", "E1") };
        IReadOnlyList<LinearExpressionSymbol> taskSymbols = Symbols("task_compilation_x", 1);
        IReadOnlyList<LinearExpressionSymbol> executorSymbols = Symbols("executor_compilation_x", 1);
        var compilation = new FakeCompilation<TestTask>(
            new Dictionary<IAbstractTask<TestExecutor, TestAssignmentPolicy>, object> { [tasks[0]] = taskSymbols[0] },
            new Dictionary<TestExecutor, object> { [executors[0]] = executorSymbols[0] });
        var taskConstraint = new TaskCompilationConstraint<TestExecutor, TestAssignmentPolicy>(tasks, compilation);
        var executorConstraint = new ExecutorCompilationConstraint<TestExecutor, TestAssignmentPolicy>(executors, compilation);
        using var model = new LinearMetaModel<Flt64>();
        taskConstraint.Invoke(model).IsOk.Should().BeTrue();
        executorConstraint.Invoke(model).IsOk.Should().BeTrue();

        MathConstraint taskRow = Rows(model, "task_compilation").Single();
        MathConstraint executorRow = Rows(model, "executor_compilation").Single();
        var map = new GanttSchedulingShadowPriceMap<TestExecutor, TestAssignmentPolicy>();

        taskConstraint.Refresh(map, model, Dual((taskRow, 3.0))).IsOk.Should().BeTrue();
        executorConstraint.Refresh(map, model, Dual((taskRow, 99.0))).IsOk.Should().BeTrue();
        map.Map.Should().HaveCount(1, "the executor side must not claim the task row");
        map.Map[(ShadowPriceKey)taskRow.Args!].Price.ToDouble().Should().BeApproximately(3.0, 1e-9);

        executorConstraint.Refresh(map, model, Dual((executorRow, 2.0))).IsOk.Should().BeTrue();
        map.Map.Should().HaveCount(2);

        // **反方向也探一次**：只喂执行器行给任务侧，任务侧不许认领。只探一个方向会让另一侧的"认领所有行"存活。
        // **Probe the reverse direction too**: feed only the executor row to the task side, which must not claim it. Probing one direction only lets the other side's "claim every row" survive.
        taskConstraint.Refresh(map, model, Dual((executorRow, 42.0))).IsOk.Should().BeTrue();
        map.Map.Should().HaveCount(2, "the task side must not claim the executor row");
        map.Map[(ShadowPriceKey)executorRow.Args!].Price.ToDouble().Should().BeApproximately(2.0, 1e-9,
            "the executor price must survive the task refresh untouched");

        taskConstraint.Extractor()!(map, new TaskGanttSchedulingShadowPriceArguments<TestExecutor, TestAssignmentPolicy>(executors[0], tasks[0]))
            .ToDouble().Should().BeApproximately(3.0, 1e-9);
        executorConstraint.Extractor()!(map, new TaskGanttSchedulingShadowPriceArguments<TestExecutor, TestAssignmentPolicy>(executors[0]))
            .ToDouble().Should().BeApproximately(2.0, 1e-9);
        executorConstraint.Extractor()!(map, new TaskGanttSchedulingShadowPriceArguments<TestExecutor, TestAssignmentPolicy>(null!, tasks[0]))
            .ToDouble().Should().BeApproximately(0, 1e-9, "a task-level query is not answered with an executor price");
    }

    private static MetaDualSolution Dual(params (MathConstraint Row, double Price)[] rows) =>
        new(
            rows.ToDictionary(entry => entry.Row, entry => new Flt64(entry.Price)),
            new Dictionary<IIntermediateSymbol, IReadOnlyList<(IConstraint<Flt64, LinearCategory> Constraint, Flt64 Price)>>());
}
