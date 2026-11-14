// 两个冲突约束的判据 / Criteria for the two conflict constraints.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service.Limits;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Tests.BunchCompilation;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.TaskCompilation;

/// <summary>
/// **两个冲突约束从空壳变成真约束行（B553）** / The two conflict constraints go from stubs to real rows (B553).
/// </summary>
/// <remarks>
/// `TaskConflictConstraint` 与 `TaskTimeConflictConstraint` 原来都直接返回 `Failed`（比返回 `Ok` 好，但依然什么都没做）。
/// 两者的行是**同一种**：`x[左, e] + x[右, e] &lt;= 1`，差别只在"哪些对算冲突"。
///
/// 三种静默失效：
/// <list type="number">
/// <item>**漏掉某一半**：`(i, j)` 与 `(j, i)` 双重循环里"只写了一半"看不出来；</item>
/// <item>**把可移动任务也算冲突**：静态时间窗重叠并不代表真的重叠，凭空禁掉合法的并行；</item>
/// <item>**行名不带任务对**：两条不同的对被叫成同一个名字，出问题时无法定位。</item>
/// </list>
///
/// Both used to return `Failed` outright (better than `Ok`, but still doing nothing). Their rows are **the same**
/// (`x[left, e] + x[right, e] &lt;= 1`); only "which pairs conflict" differs. Three silent failures: (1) **a missed half** — invisible in an `(i, j)`/`(j, i)` double loop;
/// (2) **counting movable tasks as conflicting** — a static window overlap is not a real one and would forbid legal parallelism; (3) **rows not named by pair** — two distinct
/// pairs sharing one name leaves nothing to trace.
/// </remarks>
public class ConflictConstraintTests {
    private static readonly DateTimeOffset Origin = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static TimeRange At(int startHour, int endHour) =>
        new(Origin.AddHours(startHour), Origin.AddHours(endHour));

    private static (LinearMetaModel<Flt64> Model, List<TestTask> Tasks, List<TestExecutor> Executors,
        TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy> Compilation) Setup(
        params (string Id, int StartHour, int EndHour, bool Advance, bool Delay)[] specs) {
        var executors = new List<TestExecutor> { new("E1", "E1") };
        var tasks = specs
            .Select(spec => new TestTask(spec.Id, spec.Id, At(spec.StartHour, spec.EndHour)) {
                AdvanceEnabled = spec.Advance,
                DelayEnabled = spec.Delay,
            })
            .ToList();
        foreach (TestTask task in tasks) {
            task.EnabledExecutorSet.Add(executors[0]);
        }
        var model = new LinearMetaModel<Flt64>();
        var compilation = new TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy>(tasks, executors);
        compilation.Register(model).IsOk.Should().BeTrue();
        return (model, tasks, executors, compilation);
    }

    private static List<string> ConstraintNames(LinearMetaModel<Flt64> model) =>
        Built(model).Constraints.Select(constraint => constraint.Name).ToList();

#if SCIP_INTEGRATION
    /// <summary>
    /// **真实 SCIP：两个重叠的固定任务挤一台机器 → 不可行；去掉约束 → 可行** / Real SCIP: two overlapping fixed tasks on one machine are infeasible, and removing the constraint makes them feasible.
    /// </summary>
    /// <remarks>
    /// 两边都跑是这条判据的全部意义：只测"不可行"会让"模型本来就有别处坏掉"通过；只测"可行"会让"约束根本没加上"通过。
    /// 而且不可行恰好证明行**真的进了模型**——一个恒真的行不会让任何模型不可行。
    ///
    /// Both halves are the point: "infeasible" alone would pass if the model were broken elsewhere, "feasible" alone would pass if the constraint were never added. And the
    /// infeasibility is exactly the proof that the row **reached the model** — a vacuously true row can never make a model infeasible.
    /// </remarks>
    [Fact]
    public void TimeConflict_ShouldMakeAnOverlappingSingleMachinePlanInfeasibleUnderRealScip() {
        bool withConstraint = SolveConflict(withConstraint: true);
        bool withoutConstraint = SolveConflict(withConstraint: false);

        withConstraint.Should().BeFalse("two overlapping fixed tasks cannot share the only machine");
        withoutConstraint.Should().BeTrue("without the rows the same plan is feasible, so the infeasibility comes from them");
    }

    private static bool SolveConflict(bool withConstraint) {
        var executors = new List<TestExecutor> { new("E1", "E1") };
        var tasks = new List<TestTask> {
            new("T1", "T1", At(0, 5)),
            new("T2", "T2", At(3, 8)),
        };
        foreach (TestTask task in tasks) {
            task.EnabledExecutorSet.Add(executors[0]);
        }
        using var model = new LinearMetaModel<Flt64>();
        var compilation = new TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy>(tasks, executors);
        compilation.Register(model).IsOk.Should().BeTrue();
        if (withConstraint) {
            new TaskTimeConflictConstraint<TestTask, TestExecutor, TestAssignmentPolicy>(tasks, executors, compilation)
                .Invoke(model).IsOk.Should().BeTrue();
        }

        Fuookami.Ospf.Utils.Functional.Result<
            Fuookami.Ospf.Core.Solver.Output.FeasibleSolverOutput<Flt64>,
            Fuookami.Ospf.Utils.Error.ErrorCode,
            Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>> result =
            new Fuookami.Ospf.Core.Plugin.Scip.ScipColumnGenerationSolver()
                .SolveMILPAsync("gantt-conflict", model).GetAwaiter().GetResult();
        return result.IsOk;
    }
#endif

    private static IMechanismModel<Flt64> Built(LinearMetaModel<Flt64> model) =>
        LinearMechanismModel<Flt64>.InvokeAsync(model).GetAwaiter().GetResult().Value;

    /// <summary>**相交的固定任务对得到一行，不相交的得不到** / Intersecting fixed pairs get a row, disjoint ones do not.</summary>
    /// <remarks>
    /// 三个任务：`T1` 0–5、`T2` 3–8（与 `T1` 相交）、`T3` 10–12（不相交）。只该有 `T1/T2` 一行。
    /// 只测"至少一行"会让"每对都加"通过——而那会把合法的并行全禁掉。
    ///
    /// Three tasks: `T1` 0–5, `T2` 3–8 (intersects `T1`), `T3` 10–12 (disjoint). Only `T1/T2` may appear. Checking "at least one row" would let "a row for every pair" pass,
    /// and that forbids legal parallelism wholesale.
    /// </remarks>
    [Fact]
    public void TimeConflict_ShouldOnlyCoverIntersectingFixedPairs() {
        (LinearMetaModel<Flt64> model, List<TestTask> tasks, List<TestExecutor> executors, TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy> compilation) =
            Setup(("T1", 0, 5, false, false), ("T2", 3, 8, false, false), ("T3", 10, 12, false, false));
        var constraint = new TaskTimeConflictConstraint<TestTask, TestExecutor, TestAssignmentPolicy>(
            tasks, executors, compilation);

        constraint.Invoke(model).IsOk.Should().BeTrue();

        var names = ConstraintNames(model).Where(name => name.StartsWith("task_time_conflict")).ToList();
        names.Should().ContainSingle("only T1 and T2 overlap");
        names[0].Should().Be("task_time_conflict_T1_T2", "the row must name the pair it belongs to");
    }

    /// <summary>**可移动的任务被排除** / Movable tasks are excluded.</summary>
    /// <remarks>
    /// `T2` 允许提前：它的静态时间窗与 `T1` 相交，但真实开工时刻可以挪开，所以不算冲突。
    /// 把"允许移动"漏掉会让可并行的计划被禁掉——而模型仍然可解，只是排得比实际需要更差。
    ///
    /// `T2` allows advance: its static window overlaps `T1`, but its real start can move, so it is not a conflict. Missing the "movable" test forbids a parallel plan that
    /// would have been legal — the model still solves, just worse than necessary.
    /// </remarks>
    [Fact]
    public void TimeConflict_ShouldSkipMovableTasks() {
        (LinearMetaModel<Flt64> model, List<TestTask> tasks, List<TestExecutor> executors, TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy> compilation) =
            Setup(("T1", 0, 5, false, false), ("T2", 3, 8, true, false));
        var constraint = new TaskTimeConflictConstraint<TestTask, TestExecutor, TestAssignmentPolicy>(
            tasks, executors, compilation);

        constraint.Tasks.Should().ContainSingle("only the fixed-time task qualifies");
        constraint.Tasks[0].Id.Should().Be("T1");
        constraint.Invoke(model).IsOk.Should().BeTrue();

        ConstraintNames(model).Should().NotContain(name => name.StartsWith("task_time_conflict"),
            "a movable task must not be treated as conflicting");
    }

    /// <summary>**筛选后的任务按开始时间排序** / The filtered tasks are sorted by start.</summary>
    [Fact]
    public void TimeConflict_ShouldSortTheTasksByStart() {
        (LinearMetaModel<Flt64> model, List<TestTask> tasks, List<TestExecutor> executors, TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy> compilation) =
            Setup(("Late", 10, 12, false, false), ("Early", 0, 2, false, false));
        var constraint = new TaskTimeConflictConstraint<TestTask, TestExecutor, TestAssignmentPolicy>(
            tasks, executors, compilation);

        constraint.Tasks.Select(task => task.Id).Should().Equal(new[] { "Early", "Late" });
        constraint.Invoke(model).IsOk.Should().BeTrue();
    }

    /// <summary>**谓词版按谓词给行** / The predicate version follows its predicate.</summary>
    /// <remarks>
    /// 谓词只对 `T1/T3` 说"冲突"，于是只能有一行且带这两个名字。判据同时查**行数**与**行名**：
    /// 只查行数会让"总是取前一对"通过。
    ///
    /// The predicate says "conflict" only for `T1/T3`, so exactly one row must appear, named after that pair. Both the **count** and the **name** are checked; the count alone
    /// would let "always take the first pair" pass.
    /// </remarks>
    [Fact]
    public void PredicateConflict_ShouldFollowThePredicate() {
        (LinearMetaModel<Flt64> model, List<TestTask> tasks, List<TestExecutor> executors, TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy> compilation) = Setup(
            ("T1", 0, 2, false, false),
            ("T2", 3, 5, false, false),
            ("T3", 6, 8, false, false));
        var constraint = new TaskConflictConstraint<TestTask, TestExecutor, TestAssignmentPolicy>(
            tasks, executors, compilation,
            conflict: (_, left, right) => left.Id == "T1" && right.Id == "T3");

        constraint.Invoke(model).IsOk.Should().BeTrue();

        var names = ConstraintNames(model).Where(name => name.StartsWith("task_conflict")).ToList();
        names.Should().ContainSingle();
        names[0].Should().Be("task_conflict_T1_T3");
    }

    /// <summary>**不相交的行成对覆盖，且不重复** / Unordered pairs: each conflict appears exactly once.</summary>
    /// <remarks>
    /// 谓词对**每一对**都说冲突 → 3 个任务、1 台机器应得 3 行（`T1T2`、`T1T3`、`T2T3`）。写成双向循环会是 6 行，
    /// 而"写了一半"会是 2 行或 1 行——判据钉死 3。
    ///
    /// The predicate conflicts on **every** pair → three tasks on one machine give 3 rows (`T1T2`, `T1T3`, `T2T3`). A bidirectional loop gives 6, a half-written one gives 2
    /// or 1 — the criterion pins 3.
    /// </remarks>
    [Fact]
    public void PredicateConflict_ShouldEmitOneRowPerUnorderedPair() {
        (LinearMetaModel<Flt64> model, List<TestTask> tasks, List<TestExecutor> executors, TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy> compilation) = Setup(
            ("T1", 0, 2, false, false),
            ("T2", 3, 5, false, false),
            ("T3", 6, 8, false, false));
        var constraint = new TaskConflictConstraint<TestTask, TestExecutor, TestAssignmentPolicy>(
            tasks, executors, compilation,
            conflict: (_, _, _) => true);

        constraint.Invoke(model).IsOk.Should().BeTrue();

        var names = ConstraintNames(model).Where(name => name.StartsWith("task_conflict")).ToList();
        names.Should().HaveCount(3);
        names.Should().BeEquivalentTo(new[] {
            "task_conflict_T1_T2", "task_conflict_T1_T3", "task_conflict_T2_T3",
        });
    }

    /// <summary>**行里是两条分配列之和 ≤ 1** / The row is the sum of the two assignment columns ≤ 1.</summary>
    /// <remarks>
    /// 只查"有行"会让"每行都写成 `x[i,e] ≤ 1`"通过——那是一条恒真（列本身就是二值）的约束，等于什么都没加。
    /// 判据查行里**两个不同变量**的系数都是 1，右端是 1。
    ///
    /// Checking "there is a row" would let "every row is `x[i,e] ≤ 1`" pass — vacuously true since the column is binary, i.e. nothing added. The criterion demands **two
    /// distinct variables** with coefficient 1 and a right-hand side of 1.
    /// </remarks>
    [Fact]
    public void PredicateConflict_ShouldSumTwoColumnsAgainstOne() {
        (LinearMetaModel<Flt64> model, List<TestTask> tasks, List<TestExecutor> executors, TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy> compilation) = Setup(
            ("T1", 0, 2, false, false),
            ("T2", 3, 5, false, false));
        var constraint = new TaskConflictConstraint<TestTask, TestExecutor, TestAssignmentPolicy>(
            tasks, executors, compilation,
            conflict: (_, _, _) => true);

        constraint.Invoke(model).IsOk.Should().BeTrue();

        IMechanismModel<Flt64> built = Built(model);
        Fuookami.Ospf.Core.Model.Mechanism.IConstraint<Flt64, Fuookami.Ospf.Math.Symbol.LinearCategory> row =
            built.Constraints.Single(c => c.Name == "task_conflict_T1_T2");
        IReadOnlyList<Fuookami.Ospf.Core.Model.Intermediate.ILinearCell<Flt64>> cells =
            row.Lhs.Cast<Fuookami.Ospf.Core.Model.Intermediate.ILinearCell<Flt64>>().ToList();
        cells.Should().HaveCount(2, "two distinct assignment columns");
        cells.Select(cell => cell.Token.Variable).Distinct().Should().HaveCount(2);
        cells.Should().OnlyContain(cell => cell.Coefficient.ToDouble() == 1.0);
        row.Rhs.ToDouble().Should().Be(1.0);
    }

    /// <summary>**非模型被拒** / A non-model is rejected.</summary>
    [Fact]
    public void Constraints_ShouldRejectANonModel() {
        (LinearMetaModel<Flt64> model, List<TestTask> tasks, List<TestExecutor> executors, TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy> compilation) =
            Setup(("T1", 0, 2, false, false));

        new TaskConflictConstraint<TestTask, TestExecutor, TestAssignmentPolicy>(
            tasks, executors, compilation, conflict: (_, _, _) => true)
            .Invoke(new object()).IsOk.Should().BeFalse();
        new TaskTimeConflictConstraint<TestTask, TestExecutor, TestAssignmentPolicy>(
            tasks, executors, compilation)
            .Invoke(new object()).IsOk.Should().BeFalse();
    }

}
