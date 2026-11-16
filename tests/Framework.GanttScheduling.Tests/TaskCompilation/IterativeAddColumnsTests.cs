// 迭代加列的判据 / Criteria for adding iterative columns.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Tests.BunchCompilation;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.TaskCompilation;

/// <summary>
/// **加列的五步顺序不能换** / The five steps of adding columns are order-sensitive.
/// </summary>
/// <remarks>
/// 基线的 `addColumns` 依次：建 `x_iteration` → `taskCost.flush` 后加本轮代价 →
/// 重写 `taskAssignment` → 重写 `taskCompilation` → 加 `xor` 与跨轮冲突约束。
///
/// 顺序错一步的后果都不报错：约束引用了还不存在的列、代价加到了会被清空的表上、
/// 或者跨轮冲突漏掉了本轮自己的列。
///
/// The baseline's `addColumns` runs: build `x_iteration` → `taskCost.flush` then add this round's cost → rewrite `taskAssignment` → rewrite `taskCompilation` → add the `xor` and
/// cross-round conflict constraints. Getting the order wrong raises nothing: constraints reference columns that do not exist yet, cost lands on a table about to be cleared, or the
/// cross-round conflicts miss this round's own columns.
/// </remarks>
public class IterativeAddColumnsTests {
    private static (IterativeTaskCompilation<TestIterativeTask, TestTask, TestExecutor, TestAssignmentPolicy> Compilation,
        LinearMetaModel<Flt64> Model,
        List<TestExecutor> Executors) MakeRegistered() {
        var executors = new List<TestExecutor> { new("E1", "E1"), new("E2", "E2") };
        var origin = new List<TestTask> { new("T1", "T1"), new("T2", "T2") };
        var compilation = new IterativeTaskCompilation<TestIterativeTask, TestTask, TestExecutor, TestAssignmentPolicy>(
            origin, executors);
        var model = new LinearMetaModel<Flt64>();
        compilation.Register(model).IsOk.Should().BeTrue();
        return (compilation, model, executors);
    }

    private static TestIterativeTask Task(string id, TestExecutor executor, long iteration = 0) =>
        new(new TestTask(id, id) { Executor = executor }, iteration);

    /// **本轮列进模型，且表里能查到** / This round's columns enter the model and are findable.
    [Fact]
    public void AddColumns_ShouldAddOneVariablePerColumn() {
        (IterativeTaskCompilation<TestIterativeTask, TestTask, TestExecutor, TestAssignmentPolicy> compilation,
            LinearMetaModel<Flt64> model,
            List<TestExecutor> executors) = MakeRegistered();
        int before = model.Tokens.Tokens.Count;

        Result<IReadOnlyList<TestIterativeTask>, ErrorCode, Error<ErrorCode>> result =
            compilation.AddColumns(0, new[] { Task("T1", executors[0]) }, model, _ => new Flt64(2.0), (_, _) => false);

        result.IsOk.Should().BeTrue();
        ((Ok<IReadOnlyList<TestIterativeTask>, ErrorCode, Error<ErrorCode>>)result).Value.Should().ContainSingle();
        model.Tokens.Tokens.Count.Should().BeGreaterThan(before, "the round's variable enters the model");
    }

    /// **未注册就加列必须失败** / Adding columns before registration must fail.
    [Fact]
    public void AddColumns_ShouldFailBeforeRegistration() {
        var compilation = new IterativeTaskCompilation<TestIterativeTask, TestTask, TestExecutor, TestAssignmentPolicy>(
            new List<TestTask> { new("T1", "T1") }, new List<TestExecutor> { new("E1", "E1") });
        using var model = new LinearMetaModel<Flt64>();

        compilation.AddColumns(0, new[] { Task("T1", new TestExecutor("E1", "E1")) }, model, _ => new Flt64(1.0), (_, _) => false)
            .IsOk.Should().BeFalse("columns cannot be added before registration");
    }

    /// **加列到非模型必须失败** / Adding columns into a non-model must fail.
    [Fact]
    public void AddColumns_ShouldRejectANonModel() {
        (IterativeTaskCompilation<TestIterativeTask, TestTask, TestExecutor, TestAssignmentPolicy> compilation, _, List<TestExecutor> executors) = MakeRegistered();

        compilation.AddColumns(0, new[] { Task("T1", executors[0]) }, new object(), _ => new Flt64(1.0), (_, _) => false)
            .IsOk.Should().BeFalse("columns can only be added to a linear meta model");
    }

    /// <summary>
    /// **代价提取失败就失败，不当成零** / A failed cost extraction fails; it does not become zero.
    /// </summary>
    /// <remarks>
    /// 基线用无穷大表示"不可定价"。`Flt64` 没有无穷，
    /// 当成零会让"排不起的任务"在目标里**免费**，于是求解器会优先选它。
    ///
    /// The baseline uses infinity for "cannot be priced". `Flt64` has none (the same limit); treating it as zero makes an unaffordable task **free** in the objective, and the
    /// solver then prefers it.
    /// </remarks>
    [Fact]
    public void AddColumns_ShouldFailWhenAColumnCannotBePriced() {
        (IterativeTaskCompilation<TestIterativeTask, TestTask, TestExecutor, TestAssignmentPolicy> compilation,
            LinearMetaModel<Flt64> model,
            List<TestExecutor> executors) = MakeRegistered();

        Result<IReadOnlyList<TestIterativeTask>, ErrorCode, Error<ErrorCode>> result =
            compilation.AddColumns(0, new[] { Task("T1", executors[0]) }, model, _ => null, (_, _) => false);

        result.Should().BeOfType<Failed<IReadOnlyList<TestIterativeTask>, ErrorCode, Error<ErrorCode>>>();
    }

    /// **每列一条 `xor[e] ≥ x[t]`** / One `xor[e] ≥ x[t]` per column.
    [Fact]
    public void AddColumns_ShouldAddOneXorConstraintPerColumn() {
        (IterativeTaskCompilation<TestIterativeTask, TestTask, TestExecutor, TestAssignmentPolicy> compilation,
            LinearMetaModel<Flt64> model,
            List<TestExecutor> executors) = MakeRegistered();
        int before = model.Constraints.Count;

        compilation.AddColumns(
            0,
            new[] { Task("T1", executors[0]), Task("T2", executors[1]) },
            model,
            _ => new Flt64(1.0),
            (_, _) => false).IsOk.Should().BeTrue();

        model.Constraints.Count.Should().Be(before + 2, "one xor constraint per new column");
    }

    /// <summary>
    /// **跨轮冲突真的加上，且不与本轮自己比** / Cross-round conflicts are added, and a column is not compared with itself.
    /// </summary>
    /// <remarks>
    /// 第一轮加一列，第二轮加一列冲突的：只应多出**一条**冲突约束。
    /// 若把本轮自己也算进去（`task1 != task2` 那步漏了），会多出两条——而两条里有一条是
    /// `x + x ≤ 1`，它把 `x` 逼成 0，于是**那一列永远选不上**，而模型仍然可解。
    ///
    /// Round 1 adds a column, round 2 adds a conflicting one: exactly **one** conflict constraint must appear. Counting this round's own columns would add a second one —
    /// `x + x ≤ 1` — forcing `x` to 0, so **that column can never be selected**, while the model still solves.
    /// </remarks>
    [Fact]
    public void AddColumns_ShouldAddCrossRoundConflictsOnlyAgainstEarlierRounds() {
        (IterativeTaskCompilation<TestIterativeTask, TestTask, TestExecutor, TestAssignmentPolicy> compilation,
            LinearMetaModel<Flt64> model,
            List<TestExecutor> executors) = MakeRegistered();

        compilation.AddColumns(
            0, new[] { Task("T1", executors[0]) }, model, _ => new Flt64(1.0), (_, _) => false).IsOk.Should().BeTrue();
        int afterFirst = model.Constraints.Count;

        compilation.AddColumns(
            1, new[] { Task("T2", executors[1]) }, model, _ => new Flt64(1.0), (_, _) => true).IsOk.Should().BeTrue();

        model.Constraints.Count.Should().Be(
            afterFirst + 2,
            "one xor constraint plus exactly one cross-round conflict");
    }

    /// **冲突函数返回假时一条冲突都不加** / No conflict constraint appears when the predicate is false.
    [Fact]
    public void AddColumns_ShouldSkipNonConflictingColumns() {
        (IterativeTaskCompilation<TestIterativeTask, TestTask, TestExecutor, TestAssignmentPolicy> compilation,
            LinearMetaModel<Flt64> model,
            List<TestExecutor> executors) = MakeRegistered();

        compilation.AddColumns(
            0, new[] { Task("T1", executors[0]) }, model, _ => new Flt64(1.0), (_, _) => false).IsOk.Should().BeTrue();
        int afterFirst = model.Constraints.Count;

        compilation.AddColumns(
            1, new[] { Task("T2", executors[1]) }, model, _ => new Flt64(1.0), (_, _) => false).IsOk.Should().BeTrue();

        model.Constraints.Count.Should().Be(afterFirst + 1, "only the xor constraint");
    }

    /// <summary>
    /// **加列后两张聚合表真的被重写** / Both aggregate tables are really rewritten after adding columns.
    /// </summary>
    /// <remarks>
    /// 只断言"约束多了几条"看不出聚合有没有被重写——聚合是**表达式**，不是约束。
    /// 判据直接读两个符号的多项式：分配表里 `(T1, E1)` 应含本轮那一列，编译表里 `T1` 也应含它。
    /// 不重写的后果是聚合**恒为空**：跨轮冲突与 `xor` 都还在，但"该任务被完成"这句话在模型里没有内容。
    ///
    /// Counting constraints cannot see whether the aggregates were rewritten — aggregates are **expressions**, not constraints. The criterion reads the two symbols' polynomials
    /// directly: assignment `(T1, E1)` should contain this round's column, and compilation `T1` too. Not rewriting leaves the aggregates **identically empty**: the conflicts and
    /// `xor` are still there, but "this task is done" has no content in the model.
    /// </remarks>
    [Fact]
    public void AddColumns_ShouldRewriteBothAggregates() {
        (IterativeTaskCompilation<TestIterativeTask, TestTask, TestExecutor, TestAssignmentPolicy> compilation,
            LinearMetaModel<Flt64> model,
            List<TestExecutor> executors) = MakeRegistered();

        compilation.AddColumns(
            0, new[] { Task("T1", executors[0]) }, model, _ => new Flt64(1.0), (_, _) => false).IsOk.Should().BeTrue();

        var assignment = (LinearExpressionSymbol)compilation.TaskAssignment[
            (compilation.OriginTasks[0], executors[0])];
        assignment.Polynomial.Monomials.Should().ContainSingle(
            "the assignment aggregate groups by (key, executor)");
        assignment.Polynomial.Monomials[0].Symbol.Name.Should().Be("x_0_T1");

        var taskCompilation = (LinearExpressionSymbol)compilation.TaskCompilationTable[
            compilation.OriginTasks[0]];
        taskCompilation.Polynomial.Monomials.Should().ContainSingle(
            "the compilation aggregate groups by key alone");
        taskCompilation.Polynomial.Monomials[0].Symbol.Name.Should().Be("x_0_T1");
    }

    /// <summary>
    /// **后续轮次的空组不得清掉之前轮次的项** / An empty group in a later round must not wipe an earlier round's terms.
    /// </summary>
    /// <remarks>
    /// 第 0 轮给 `(T1, E1)` 加了一列，第 1 轮只给 `(T2, E2)` 加。`(T1, E1)` 在第 1 轮是**空组**。
    /// 若无条件重写（丢掉 `ShouldRewrite` 守卫），它会被 `flush` 成空——第 0 轮那一列仍在模型里，
    /// 但它在聚合表里**消失了**，于是"该执行器承担了 T1"这件事不再有内容，而模型仍然可解。
    ///
    /// Round 0 adds a column for `(T1, E1)`; round 1 only adds `(T2, E2)`. `(T1, E1)` is an **empty group** in round 1. An unconditional rewrite (dropping the `ShouldRewrite`
    /// guard) flushes it to empty — round 0's column stays in the model but **vanishes** from the aggregate, so "this executor carries T1" loses its content while the model still
    /// solves.
    /// </remarks>
    [Fact]
    public void AddColumns_ShouldNotClearEarlierRoundsThroughEmptyGroups() {
        (IterativeTaskCompilation<TestIterativeTask, TestTask, TestExecutor, TestAssignmentPolicy> compilation,
            LinearMetaModel<Flt64> model,
            List<TestExecutor> executors) = MakeRegistered();

        compilation.AddColumns(
            0, new[] { Task("T1", executors[0]) }, model, _ => new Flt64(1.0), (_, _) => false).IsOk.Should().BeTrue();
        compilation.AddColumns(
            1, new[] { Task("T2", executors[1]) }, model, _ => new Flt64(1.0), (_, _) => false).IsOk.Should().BeTrue();

        var assignment = (LinearExpressionSymbol)compilation.TaskAssignment[
            (compilation.OriginTasks[0], executors[0])];
        assignment.Polynomial.Monomials.Should().ContainSingle(
            "round 0's column survives a round that adds nothing for this group");
        assignment.Polynomial.Monomials[0].Symbol.Name.Should().Be("x_0_T1");

        var taskCompilation = (LinearExpressionSymbol)compilation.TaskCompilationTable[
            compilation.OriginTasks[0]];
        taskCompilation.Polynomial.Monomials.Should().ContainSingle(
            "round 0's column survives in the compilation aggregate too");
        taskCompilation.Polynomial.Monomials[0].Symbol.Name.Should().Be("x_0_T1");
    }

    /// **空组不动聚合** / An empty group leaves its aggregate untouched.
    [Fact]
    public void AddColumns_ShouldLeaveAggregatesOfUnrelatedTasksAlone() {
        (IterativeTaskCompilation<TestIterativeTask, TestTask, TestExecutor, TestAssignmentPolicy> compilation,
            LinearMetaModel<Flt64> model,
            List<TestExecutor> executors) = MakeRegistered();

        compilation.AddColumns(
            0, new[] { Task("T1", executors[0]) }, model, _ => new Flt64(1.0), (_, _) => false).IsOk.Should().BeTrue();

        var untouched = (LinearExpressionSymbol)compilation.TaskAssignment[
            (compilation.OriginTasks[1], executors[1])];
        untouched.Polynomial.Monomials.Should().BeEmpty(
            "no column was added for (T2, E2), so its aggregate stays empty");
    }

    /// **去重后的列才进模型** / Only deduplicated columns enter the model.
    [Fact]
    public void AddColumns_ShouldOnlyAddUnduplicatedColumns() {
        (IterativeTaskCompilation<TestIterativeTask, TestTask, TestExecutor, TestAssignmentPolicy> compilation,
            LinearMetaModel<Flt64> model,
            List<TestExecutor> executors) = MakeRegistered();
        TestIterativeTask first = Task("T1", executors[0]);

        compilation.AddColumns(0, new[] { first }, model, _ => new Flt64(1.0), (_, _) => false).IsOk.Should().BeTrue();
        int afterFirst = model.Constraints.Count;

        Result<IReadOnlyList<TestIterativeTask>, ErrorCode, Error<ErrorCode>> second =
            compilation.AddColumns(1, new[] { first }, model, _ => new Flt64(1.0), (_, _) => false);

        second.IsOk.Should().BeTrue();
        ((Ok<IReadOnlyList<TestIterativeTask>, ErrorCode, Error<ErrorCode>>)second).Value.Should().BeEmpty(
            "the column already exists");
        model.Constraints.Count.Should().Be(afterFirst, "nothing new was added");
    }
}
