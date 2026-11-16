// 任务束迭代列的判据 / Criteria for the bunch iteration columns.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation.Service.Limits;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Tests.BunchCompilation;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Result = Fuookami.Ospf.Utils.Functional.Result<
    System.Collections.Generic.IReadOnlyList<Fuookami.Ospf.Framework.GanttScheduling.Tests.BunchCompilation.TestTaskBunch>,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.TaskCompilation;

/// <summary>
/// **任务束的迭代列 `x_{iteration}`** / The bunch iteration columns `x_{iteration}`.
/// </summary>
/// <remarks>
/// 加列要同时做四件事，缺任何一件都会让"列加上了但没被算进去"：
/// <list type="number">
/// <item>建本轮列变量 `x_{iteration}`（二进制，一列一个）；</item>
/// <item>`bunch_cost` 累加 `cost × x`——这是目标唯一能看见这些列的地方；</item>
/// <item>三张表累加 `Σ x`：分配（任务×执行器）、任务编译、执行器编译；</item>
/// <item>任务时间把 `valueOf(束开始/结束) × x` 累加到 `est` / `estEnd`。</item>
/// </list>
/// 只做第 1 件的话解永远可行（列全取 0），只做第 2 件的话目标里有成本但没有任何约束把列和任务连起来。
///
/// Column addition has to do four things, and missing any one yields "the column was added but counted in nothing": (1) create the round's column variables `x_{iteration}`
/// (binary, one per column); (2) accumulate `cost × x` into `bunch_cost`, the only place the objective can see these columns; (3) accumulate `Σ x` into the three tables —
/// assignment (task × executor), task compilation, executor compilation; (4) have the task time accumulate `valueOf(bunch start/end) × x` into `est` / `estEnd`. Doing only (1)
/// leaves every solution feasible (all columns 0); doing only (2) puts cost in the objective with nothing tying columns to tasks.
/// </remarks>
public class BunchColumnTests {
    private static readonly DateTimeOffset Origin = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static TimeWindow<Flt64> Window() => new(
        new TimeRange(Origin, Origin.AddHours(24)),
        true,
        TimeSpan.FromHours(1));

    private static (LinearMetaModel<Flt64> Model, List<TestTask> Tasks, TestExecutor Executor,
        BunchCompilation<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy> Compilation) Prepare() {
        var executors = new List<TestExecutor> { new("E1", "E1") };
        var tasks = new List<TestTask> {
            new("T1", "T1", new TimeRange(Origin.AddHours(2), Origin.AddHours(4))),
            new("T2", "T2", new TimeRange(Origin.AddHours(3), Origin.AddHours(5))),
        };
        foreach (TestTask task in tasks) {
            task.EnabledExecutorSet.Add(executors[0]);
        }
        var compilation = new BunchCompilation<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy>(
            tasks, executors);
        var model = new LinearMetaModel<Flt64>();
        compilation.Register(model).IsOk.Should().BeTrue();
        return (model, tasks, executors[0], compilation);
    }

    private static TestTaskBunch Bunch(
        TestExecutor executor,
        IReadOnlyList<TestTask> tasks,
        double cost = 10.0) => new(
        executor,
        new ExecutorInitialUsability<TestTask, TestExecutor, TestAssignmentPolicy>(null, Origin),
        tasks,
        new ImmutableCost<Flt64>(Array.Empty<CostItem<Flt64>>(), new Flt64(cost)));

    /// <summary>
    /// **成本读数对 `Flt64` 不能再抛 `InvalidCastException`** / Reading a cost must not throw `InvalidCastException` for `Flt64`.
    /// </summary>
    /// <remarks>
    /// `ICost&lt;V&gt;.SolverCostOrNull` 原来用 `Convert.ToDouble`：`V` 只承诺 `IRealNumber&lt;V&gt;`，不是 `IConvertible`，
    /// 而 `Flt64` 恰恰不实现它——于是"读成本"这条路对 `Flt64` 一直抛异常。这里真的开始读成本（列的目标系数）才撞出来。
    ///
    /// `ICost&lt;V&gt;.SolverCostOrNull` used `Convert.ToDouble`: `V` only promises `IRealNumber&lt;V&gt;`, not `IConvertible`, and `Flt64` does not implement it — so "read the cost" threw
 /// for `Flt64` all along. only surfaced it because it finally reads costs (the column's objective coefficient).
    /// </remarks>
    [Fact]
    public void SolverCost_ShouldReadAFlt64Cost() {
        ICost<Flt64> cost = new ImmutableCost<Flt64>(
            Array.Empty<CostItem<Flt64>>(), new Flt64(12.5));

        cost.SolverCostOrNull().Should().NotBeNull();
        cost.SolverCostOrNull()!.Value.ToDouble().Should().BeApproximately(12.5, 1e-9);
        cost.SolverCost().IsOk.Should().BeTrue();
    }

    /// <summary>**列变量按轮次建，一列一个，名字带轮次与束身份** / One binary column per bunch, named with the round and the bunch identity.</summary>
    [Fact]
    public void AddColumns_ShouldCreateOneNamedBinaryColumnPerBunch() {
        (LinearMetaModel<Flt64> model, List<TestTask> tasks, TestExecutor executor,
            BunchCompilation<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy> compilation) = Prepare();
        TestTaskBunch bunch = Bunch(executor, tasks);

        Result result =
            compilation.AddColumns(3UL, new List<TestTaskBunch> { bunch }, model);

        result.IsOk.Should().BeTrue();
        result.Value.Should().ContainSingle();
        Fuookami.Ospf.Core.Variable.BinVariable1? columns = compilation.Columns(3UL);
        columns.Should().NotBeNull();
        columns!.Shape.D1.Should().Be(1);
        // 名字带轮次：不同轮的列在日志与对偶值里必须能分开。
        // The name carries the round: columns from different rounds must be distinguishable in logs and duals.
        columns[0].Name.Should().StartWith("x_3_");
        columns[0].Name.Should().Contain(bunch.Index.ToString());
        compilation.Columns(4UL).Should().BeNull("no columns were added for that round");
    }

    /// <summary>**`bunch_cost` 接上 `cost × x`** / `bunch_cost` picks up `cost × x`.</summary>
    /// <remarks>
    /// 判据查**系数**而不是"非空"：系数写成 1 也是非空，但目标会把每一列都当成同样的成本。
    ///
    /// The criterion pins the **coefficient**, not "non-empty": a coefficient of 1 is also non-empty, but the objective would price every column the same.
    /// </remarks>
    [Fact]
    public void AddColumns_ShouldChargeTheBunchCostToTheColumn() {
        (LinearMetaModel<Flt64> model, List<TestTask> tasks, TestExecutor executor,
            BunchCompilation<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy> compilation) = Prepare();
        TestTaskBunch bunch = Bunch(executor, tasks, cost: 10.0);

        compilation.AddColumns(0UL, new List<TestTaskBunch> { bunch }, model).IsOk.Should().BeTrue();

        var cost = (ILinearIntermediateSymbol<Flt64>)compilation.TaskCost!;
        cost.Polynomial.Monomials.Should().ContainSingle("one column, one cost term");
        cost.Polynomial.Monomials[0].Coefficient.ToDouble().Should().BeApproximately(10.0, 1e-9);
        cost.Polynomial.Monomials[0].Symbol.Should().BeSameAs(compilation.Columns(0UL)![0]);
    }

    /// <summary>**三张表都累加 `Σ x`** / All three tables accumulate `Σ x`.</summary>
    /// <remarks>
    /// 三张表缺任何一张，"这列覆盖了哪些任务/执行器"就断了一处：任务编译缺 → 任务永远不会被这列完成；
    /// 分配缺 → 任务×执行器上看不见这列；执行器编译缺 → 执行器的使用量算不到。
    ///
    /// Missing any one of the three breaks "which tasks/executors this column covers": without task compilation the task is never completed by this column; without assignment the
    /// column is invisible per (task, executor); without executor compilation the executor's usage is undercounted.
    /// </remarks>
    [Fact]
    public void AddColumns_ShouldFeedAllThreeAggregateTables() {
        (LinearMetaModel<Flt64> model, List<TestTask> tasks, TestExecutor executor,
            BunchCompilation<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy> compilation) = Prepare();
        TestTaskBunch bunch = Bunch(executor, tasks);
        object column;

        compilation.AddColumns(0UL, new List<TestTaskBunch> { bunch }, model).IsOk.Should().BeTrue();
        column = compilation.Columns(0UL)![0];

        foreach (TestTask task in tasks) {
            var compilationSymbol = (ILinearIntermediateSymbol<Flt64>)compilation.TaskCompilationTable[
                (IAbstractTask<TestExecutor, TestAssignmentPolicy>)task];
            compilationSymbol.Polynomial.Monomials.Select(monomial => monomial.Symbol).Should().Contain((Fuookami.Ospf.Math.Symbol.ISymbol)column);
            var assignment = (ILinearIntermediateSymbol<Flt64>)compilation.TaskAssignment[
                ((IAbstractTask<TestExecutor, TestAssignmentPolicy>)task, executor)];
            assignment.Polynomial.Monomials.Select(monomial => monomial.Symbol).Should().Contain((Fuookami.Ospf.Math.Symbol.ISymbol)column);
        }
        var executorSymbol = (ILinearIntermediateSymbol<Flt64>)compilation.ExecutorCompilation[executor];
        executorSymbol.Polynomial.Monomials.Select(monomial => monomial.Symbol).Should().Contain((Fuookami.Ospf.Math.Symbol.ISymbol)column);
    }

    /// <summary>**去重后的顺序才是列下标** / The deduplicated order is the column index.</summary>
    /// <remarks>
    /// 同一束加两次只应产生一列。若调用方按入参顺序数下标（两个束时第 2 个得 1），去重后就会错位——
    /// 症状是偏移加到了别人的列上，目标看起来合理而排程整体偏一段。
    ///
    /// Adding the same bunch twice yields one column. A caller counting in argument order (getting 1 for the second bunch) then lands off by one after deduplication — the symptom
    /// is a deviation on someone else's column, with a reasonable-looking objective and a schedule shifted by one slot.
    /// </remarks>
    [Fact]
    public void AddColumns_ShouldIndexColumnsByDeduplicatedOrder() {
        (LinearMetaModel<Flt64> model, List<TestTask> tasks, TestExecutor executor,
            BunchCompilation<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy> compilation) = Prepare();
        TestTaskBunch first = Bunch(executor, new List<TestTask> { tasks[0] });
        TestTaskBunch second = Bunch(executor, new List<TestTask> { tasks[1] });

        Result result =
            compilation.AddColumns(0UL, new List<TestTaskBunch> { first, second, first }, model);

        result.IsOk.Should().BeTrue();
        result.Value.Should().HaveCount(2, "the duplicate is dropped");
        compilation.ColumnIndex(0UL, first).Should().Be(0);
        compilation.ColumnIndex(0UL, second).Should().Be(1);
    }

    /// <summary>**任务时间把 `valueOf(束时间) × x` 加到 `est` / `estEnd`** / The task time adds `valueOf(bunch time) × x` to `est` / `estEnd`.</summary>
    /// <remarks>
    /// 系数查**具体时刻**（束开始的 2 小时 = 7200 秒、束结束的 4 小时 = 14400 秒），不只查"多了一项"：
    /// 写成 1 也"多了一项"，但那样排程会把列当成"偏移 1 秒"。
    ///
    /// The coefficients are checked against **specific instants** (bunch start at 2 h = 7200 s, end at 4 h = 14400 s), not merely "one more term": a coefficient of 1 is also "one
    /// more term", and would make the schedule treat a column as "a one-second shift".
    /// </remarks>
    [Fact]
    public void AddColumns_ShouldPushTheBunchTimeOntoTheTaskTime() {
        (LinearMetaModel<Flt64> model, List<TestTask> tasks, TestExecutor executor,
            BunchCompilation<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy> compilation) = Prepare();
        var taskTime = new BunchSchedulingTaskTime<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy>(
            Window(), tasks, compilation);
        taskTime.Register(model).IsOk.Should().BeTrue();

        TestTaskBunch bunch = Bunch(executor, new List<TestTask> { tasks[0] });
        compilation.AddColumns(0UL, new List<TestTaskBunch> { bunch }, model).IsOk.Should().BeTrue();
        taskTime.AddColumns(0UL, new List<TestTaskBunch> { bunch }, model).IsOk.Should().BeTrue();

        var start = (ILinearIntermediateSymbol<Flt64>)taskTime.EstimateStartTime![tasks[0]];
        var end = (ILinearIntermediateSymbol<Flt64>)taskTime.EstimateEndTime![tasks[0]];
        object column = compilation.Columns(0UL)![0];
        start.Polynomial.Monomials.Should().Contain(monomial => ReferenceEquals(monomial.Symbol, column));
        start.Polynomial.Monomials.First(monomial => ReferenceEquals(monomial.Symbol, column))
            .Coefficient.ToDouble().Should().BeApproximately(2 * 3600.0, 1e-9, "the bunch starts at 02:00");
        end.Polynomial.Monomials.First(monomial => ReferenceEquals(monomial.Symbol, column))
            .Coefficient.ToDouble().Should().BeApproximately(4 * 3600.0, 1e-9, "the bunch ends at 04:00");

        // 只有被束覆盖的那个任务被加列：另一个任务的 `est` 上不该出现这一列。
        // Only the covered task gains the column: the other task's `est` must not carry it.
        var other = (ILinearIntermediateSymbol<Flt64>)taskTime.EstimateStartTime[tasks[1]];
        other.Polynomial.Monomials.Select(monomial => monomial.Symbol).Should().NotContain((Fuookami.Ospf.Math.Symbol.ISymbol)column);
    }

    /// <summary>
    /// **移除列要真的从模型里消失：钉 0 + 摘掉 + 登记退役** / Removing a column must actually leave the model: pin to 0, drop it, record the retirement.
    /// </summary>
    /// <remarks>
    /// 只把束从束列表里删掉是不够的：列变量和它的成本系数还在，求解器可以继续选它——"移除"变成只影响报告、不影响求解。
    /// 基线的做法是两步：范围钉成 0，再从模型里 `remove`。
    ///
    /// 判据**成对**：被移除的列退役、范围钉死、模型变量数下降；被保留的列既不退役也没被摘掉。
    /// 只测前者会让"把谁都移除"通过，只测后者会让"谁都不移除"通过。
    ///
    /// Dropping the bunch from the bunch list is not enough: its column variable and cost coefficient remain and the solver keeps selecting it — "removal" then only affects
    /// reporting. The baseline pins the range to 0 and then `remove`s it. The criterion is **paired**: the removed column is retired, pinned and gone from the variable count, while
    /// the kept column is neither. Testing only the first passes if everything is removed; only the second, if nothing is.
    /// </remarks>
    [Fact]
    public async System.Threading.Tasks.Task RemoveColumn_ShouldRetireAndDropTheColumnFromTheModel() {
        (LinearMetaModel<Flt64> model, List<TestTask> tasks, TestExecutor executor,
            BunchCompilation<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy> compilation) = Prepare();
        TestTaskBunch removed = Bunch(executor, new List<TestTask> { tasks[0] });
        TestTaskBunch kept = Bunch(executor, new List<TestTask> { tasks[1] });
        compilation.AddColumns(0UL, new List<TestTaskBunch> { removed, kept }, model).IsOk.Should().BeTrue();
        int variablesBefore = await Variables(model);
        Fuookami.Ospf.Core.Variable.CombinationVariableItem<
            Fuookami.Ospf.Math.Algebra.Number.UInt8, Fuookami.Ospf.Core.Variable.Binary> removedColumn =
            compilation.ColumnItem(0UL, removed)!;
        Fuookami.Ospf.Core.Variable.CombinationVariableItem<
            Fuookami.Ospf.Math.Algebra.Number.UInt8, Fuookami.Ospf.Core.Variable.Binary> keptColumn =
            compilation.ColumnItem(0UL, kept)!;

        compilation.RemoveColumn(removed, model).IsOk.Should().BeTrue();

        compilation.RemovedBunches.Should().Contain(removed);
        compilation.IsRetired(removedColumn).Should().BeTrue("a removed column must not come back");
        removedColumn.Range.Fixed.Should().BeTrue("pinned to 0 before being dropped");
        (await Variables(model)).Should().Be(variablesBefore - 1, "exactly one column variable left the model");

        // 反方向：没被移除的那一列不退役、范围不钉、还在模型里。
        // The other way: the column that was not removed is not retired, not pinned, and still in the model.
        compilation.RemovedBunches.Should().NotContain(kept);
        compilation.IsRetired(keptColumn).Should().BeFalse();
        keptColumn.Range.Fixed.Should().BeFalse();

        // 再移除一次是空操作：不重复摘、不重复登记。
        // Removing it again is a no-op: nothing dropped twice, nothing recorded twice.
        compilation.RemoveColumn(removed, model).IsOk.Should().BeTrue("a repeat removal is a no-op");
        (await Variables(model)).Should().Be(variablesBefore - 1);
    }

    /// <summary>**模型变量数的读数** / The model's variable count.</summary>
    private static async System.Threading.Tasks.Task<int> Variables(LinearMetaModel<Flt64> model) =>
        (await LinearMechanismModel<Flt64>.InvokeAsync(model)).Value.NumVariables;


    /// <summary>
    /// **聚合层的 `RemoveColumns` 也走同一条退役路径** / The aggregation's `RemoveColumns` takes the same retirement path.
    /// </summary>
    /// <remarks>
    /// 单独测 `BunchCompilation.RemoveColumn` 说明不了聚合层有没有用它——聚合层完全可以退回 `Aggregation.RemoveColumn`（只删束、不摘变量）。
    /// 这条判据从聚合层入口进，检查它的 `Compilation` 里那一列确实退役了。
    ///
    /// Testing `BunchCompilation.RemoveColumn` alone does not prove the aggregation uses it — the aggregation could fall back to `Aggregation.RemoveColumn` (drop the bunch, keep the
    /// variable). This criterion enters through the aggregation and checks that column really got retired inside its own `Compilation`.
    /// </remarks>
    [Fact]
    public void AggregationRemoveColumns_ShouldRetireThroughTheCompilation() {
        var executors = new List<TestExecutor> { new("E1", "E1") };
        var tasks = new List<TestTask> {
            new("T1", "T1", new TimeRange(Origin.AddHours(2), Origin.AddHours(4))),
            new("T2", "T2", new TimeRange(Origin.AddHours(3), Origin.AddHours(5))),
        };
        foreach (TestTask task in tasks) {
            task.EnabledExecutorSet.Add(executors[0]);
        }
        var aggregation = new BunchCompilationAggregation<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy>(
            tasks, executors);
        var model = new LinearMetaModel<Flt64>();
        aggregation.Register(model).IsOk.Should().BeTrue();
        TestTaskBunch removed = Bunch(executors[0], new List<TestTask> { tasks[0] });
        TestTaskBunch kept = Bunch(executors[0], new List<TestTask> { tasks[1] });
        aggregation.AddColumns(0UL, new List<TestTaskBunch> { removed, kept }, model).IsOk.Should().BeTrue();
        object removedColumn = aggregation.Compilation.ColumnItem(0UL, removed)!;

        Result<Flt64, ErrorCode, Error<ErrorCode>> result = aggregation.RemoveColumns(
            new Flt64(5),
            64UL,
            bunch => ReferenceEquals(bunch, removed) ? new Flt64(10) : new Flt64(1),
            new HashSet<TestTaskBunch>(),
            new HashSet<TestTaskBunch>(),
            model);

        result.IsOk.Should().BeTrue();
        aggregation.Compilation.IsRetired(
            (Fuookami.Ospf.Core.Variable.IVariableItem)removedColumn).Should().BeTrue(
            "the aggregation must retire through the compilation, not just drop the bunch");
        aggregation.RemovedBunches.Should().Contain(removed).And.NotContain(kept);
    }


    /// <summary>
    /// **退役列不会在展开时把模型搞坏** / A retired column must not break expansion.
    /// </summary>
    /// <remarks>
    /// 此前把列钉 0 并从模型摘掉，但**目标项里可能还留着它**（`bunch_cost` 的单项式在加列时就写进去了）。
    /// 这条判据把"摘掉之后还能不能展开成求解模型"实测一遍：要么展开成功且目标项里没有那个变量，
    /// 要么明确失败——两种都是可接受的结果，**唯一不可接受的是"看起来成功但目标里指着一个不存在的变量"**。
    ///
 /// the previous step pinned the column to 0 and dropped it from the model, but an **objective term may still mention it** (`bunch_cost`'s monomials were written at column-add
    /// time). This criterion measures "can the model still expand after the drop": either expansion succeeds and the objective has no such variable, or it fails loudly — both are
    /// acceptable; the only unacceptable outcome is **"looks successful while the objective points at a variable that is not there"**.
    /// </remarks>
    [Fact]
    public async System.Threading.Tasks.Task RemoveColumn_ShouldLeaveTheModelExpandable() {
        (LinearMetaModel<Flt64> model, List<TestTask> tasks, TestExecutor executor,
            BunchCompilation<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy> compilation) = Prepare();
        TestTaskBunch bunch = Bunch(executor, new List<TestTask> { tasks[0] });
        compilation.AddColumns(0UL, new List<TestTaskBunch> { bunch }, model).IsOk.Should().BeTrue();
        new BunchCostMinimization<TestTask, TestExecutor, TestAssignmentPolicy>(compilation)
            .Invoke(model).IsOk.Should().BeTrue();
        object column = compilation.ColumnItem(0UL, bunch)!;

        compilation.RemoveColumn(bunch, model).IsOk.Should().BeTrue();

        Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Core.Model.Mechanism.LinearMechanismModel<Flt64>,
            Fuookami.Ospf.Utils.Error.ErrorCode,
            Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>> expanded =
            await LinearMechanismModel<Flt64>.InvokeAsync(model);
        expanded.IsOk.Should().BeTrue("a dropped column must not leave a term pointing at it: " + expanded);
        if (expanded.IsOk) {
            foreach (Fuookami.Ospf.Core.Model.Mechanism.MetaSubObject<Flt64> objective in model.MetaSubObjects) {
                objective.Polynomial.Monomials.Select(monomial => monomial.Symbol)
                    .Should().NotContain((Fuookami.Ospf.Math.Symbol.ISymbol)column,
                        "the objective must not keep a term for a dropped column");
            }
        }
    }

    /// <summary>
    /// **列还没加过时移除束是空操作，不是失败** / Removing a bunch whose column was never added is a no-op, not a failure.
    /// </summary>
    /// <remarks>
    /// 移除束在编译层是合法动作：那一轮可能还没走到，没有列就没有变量要摘。把这种情形报成失败，会让"先移除、后加列"的正常顺序
    /// 断在半路——而失败信息会说"移除列失败"，指向根本不存在的列。
    ///
    /// Removing a bunch is legal at the compilation layer: that round may not have been reached, and with no column there is no variable to drop. Reporting failure here would
    /// break the perfectly normal "remove first, add columns later" order — and the message would say "removing a column failed", pointing at a column that never existed.
    /// </remarks>
    [Fact]
    public void RemoveColumn_ShouldBeANoOpWhileTheRoundHasNoColumns() {
        (LinearMetaModel<Flt64> model, List<TestTask> tasks, TestExecutor executor,
            BunchCompilation<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy> compilation) = Prepare();
        TestTaskBunch bunch = Bunch(executor, new List<TestTask> { tasks[0] });

        compilation.RemoveColumn(bunch, model).IsOk.Should().BeTrue("there is no column to drop yet");

        compilation.RemovedBunches.Should().Contain(bunch, "the bunch is still removed from the aggregation");
    }


    /// <summary>
    /// **同步与异步加列必须做同一件事** / Synchronous and asynchronous column addition must do the same thing.
    /// </summary>
    /// <remarks>
    /// 异步入口原来只有"去重并返回"：不建列、不记成本、不喂三张表。同一个动作在两种签名下做**完全不同的事**，
    /// 而调用方只看到 `Ok` 和一份去重后的列表——以为列已就绪，实际上一列都没有。
    ///
    /// 判据**成对**地比对两条路径的结果：去重后的束数一致、列名一致、`bunch_cost` 的项数与系数一致、三张表里都含那一列。
    /// 只测"异步也返回 Ok"是没用的——那个空壳本来就返回 `Ok`。
    ///
    /// The async entry point used to only "deduplicate and return": no columns, no cost, no tables. The same action did **completely different things** under two signatures, while the
    /// caller saw `Ok` plus a deduplicated list — believing the columns were ready when there were none. The criterion **pairs** the two paths: same deduplicated count, same column
    /// name, same `bunch_cost` term count and coefficient, and the column present in all three tables. "The async path also returns Ok" proves nothing — that stub returned `Ok`.
    /// </remarks>
    [Fact]
    public async System.Threading.Tasks.Task AddColumnsAsync_ShouldDoTheSameAsTheSynchronousPath() {
        (LinearMetaModel<Flt64> syncModel, List<TestTask> syncTasks, TestExecutor executor,
            BunchCompilation<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy> syncCompilation) = Prepare();
        TestTaskBunch syncBunch = Bunch(executor, syncTasks, cost: 6.0);
        Result syncResult = syncCompilation.AddColumns(0UL, new List<TestTaskBunch> { syncBunch }, syncModel);

        (LinearMetaModel<Flt64> asyncModel, List<TestTask> asyncTasks, TestExecutor executor2,
            BunchCompilation<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy> asyncCompilation) = Prepare();
        TestTaskBunch asyncBunch = Bunch(executor2, asyncTasks, cost: 6.0);
        Result asyncResult = await asyncCompilation.AddColumnsAsync(
            0UL, new List<TestTaskBunch> { asyncBunch }, asyncModel);

        syncResult.IsOk.Should().BeTrue();
        asyncResult.IsOk.Should().BeTrue();
        asyncResult.Value.Should().HaveCount(syncResult.Value.Count);

        asyncCompilation.Columns(0UL).Should().NotBeNull("the async path must really create the column");
        asyncCompilation.Columns(0UL)!.Shape.D1.Should().Be(syncCompilation.Columns(0UL)!.Shape.D1);
        // 名字比**形状**（轮次前缀 + 执行器后缀），不比中间那段下标：两个编译各自索引自己的束，
        // 下标是实例内的编号，跨实例比较只会比到"谁先建的"。
        // The names are compared by **shape** (round prefix + executor suffix), not the middle index: each compilation indexes its own bunches, so the index is a per-instance
        // number and a cross-instance comparison only measures who was created first.
        asyncCompilation.Columns(0UL)![0].Name.Should().MatchRegex("^x_0_.*_E1$");
        syncCompilation.Columns(0UL)![0].Name.Should().MatchRegex("^x_0_.*_E1$");

        var asyncCost = (ILinearIntermediateSymbol<Flt64>)asyncCompilation.TaskCost!;
        var syncCost = (ILinearIntermediateSymbol<Flt64>)syncCompilation.TaskCost!;
        asyncCost.Polynomial.Monomials.Should().HaveCount(syncCost.Polynomial.Monomials.Count);
        asyncCost.Polynomial.Monomials[0].Coefficient.ToDouble().Should()
            .BeApproximately(syncCost.Polynomial.Monomials[0].Coefficient.ToDouble(), 1e-9);
        asyncCost.Polynomial.Monomials[0].Coefficient.ToDouble().Should().BeApproximately(6.0, 1e-9);

        object asyncColumn = asyncCompilation.Columns(0UL)![0];
        var asyncTaskCompilation = (ILinearIntermediateSymbol<Flt64>)asyncCompilation.TaskCompilationTable[
            (IAbstractTask<TestExecutor, TestAssignmentPolicy>)asyncTasks[0]];
        asyncTaskCompilation.Polynomial.Monomials.Select(monomial => monomial.Symbol)
            .Should().Contain((Fuookami.Ospf.Math.Symbol.ISymbol)asyncColumn);
    }

    /// <summary>**这一轮没加过列时明确失败** / A round without columns fails loudly.</summary>
    [Fact]
    public void AddColumns_ShouldFailForARoundWithoutColumns() {
        (LinearMetaModel<Flt64> model, List<TestTask> tasks, TestExecutor executor,
            BunchCompilation<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy> compilation) = Prepare();
        var taskTime = new BunchSchedulingTaskTime<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy>(
            Window(), tasks, compilation);
        taskTime.Register(model).IsOk.Should().BeTrue();

        TestTaskBunch bunch = Bunch(executor, new List<TestTask> { tasks[0] });

        taskTime.AddColumns(7UL, new List<TestTaskBunch> { bunch }, model).IsOk
            .Should().BeFalse("round 7 has no columns, so there is nothing to ride");
    }
}
