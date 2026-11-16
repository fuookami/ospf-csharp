// 迭代回收的判据 / Criteria for iterative column retirement.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Tests.BunchCompilation;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.TaskCompilation;

/// <summary>
/// **回收走的是"整池"而不是"本轮"** / Retirement walks the whole pool, not just this round.
/// </summary>
/// <remarks>
/// 这里对**所有**已移除任务做"固定为 0 + 摘变量"，不是只处理本轮新删的。
/// 只处理本轮的话，早先被删的列仍在模型里可被选中——它们只是从聚合表里消失了。
///
/// The code pins and drops **every** removed task, not only this round's. Handling only this round leaves earlier retirements still selectable in the model — they merely
/// vanished from the aggregate tables.
/// </remarks>
public class IterativeRemoveColumnsTests {
    private static (IterativeTaskCompilation<TestIterativeTask, TestTask, TestExecutor, TestAssignmentPolicy> Compilation,
        LinearMetaModel<Flt64> Model,
        List<TestExecutor> Executors) MakeWithOneRound() {
        var executors = new List<TestExecutor> { new("E1", "E1") };
        var compilation = new IterativeTaskCompilation<TestIterativeTask, TestTask, TestExecutor, TestAssignmentPolicy>(
            new List<TestTask> { new("T1", "T1"), new("T2", "T2") }, executors);
        var model = new LinearMetaModel<Flt64>();
        compilation.Register(model).IsOk.Should().BeTrue();
        compilation.AddColumns(
                0,
                new[] {
                    new TestIterativeTask(new TestTask("T1", "T1") { Executor = executors[0] }),
                    new TestIterativeTask(new TestTask("T2", "T2") { Executor = executors[0] }),
                },
                model,
                _ => new Flt64(1.0),
                (_, _) => false)
            .IsOk.Should().BeTrue();
        return (compilation, model, executors);
    }

    private static IEnumerable<CombinationVariableItem<UInt8, Binary>> ColumnVariables(LinearMetaModel<Flt64> model) =>
        model.Tokens.Tokens
            .Select(token => token.Variable)
            .OfType<CombinationVariableItem<UInt8, Binary>>()
            .Where(variable => variable.Name.StartsWith("x_0_", System.StringComparison.Ordinal));

    /// **未加列就回收必须失败** / Retiring before any round must fail.
    [Fact]
    public void RemoveColumns_ShouldFailBeforeAnyRound() {
        var compilation = new IterativeTaskCompilation<TestIterativeTask, TestTask, TestExecutor, TestAssignmentPolicy>(
            new List<TestTask> { new("T1", "T1") }, new List<TestExecutor> { new("E1", "E1") });
        using var model = new LinearMetaModel<Flt64>();
        compilation.Register(model).IsOk.Should().BeTrue();

        compilation.RemoveColumns(
                new Flt64(5.0),
                100,
                _ => new Flt64(9.0),
                new HashSet<TestIterativeTask>(),
                new HashSet<TestIterativeTask>(),
                model)
            .IsOk.Should().BeFalse("no round has been added yet");
    }

    /// **回收非模型必须失败** / Retiring into a non-model must fail.
    [Fact]
    public void RemoveColumns_ShouldRejectANonModel() {
        (IterativeTaskCompilation<TestIterativeTask, TestTask, TestExecutor, TestAssignmentPolicy> compilation,
            _,
            _) = MakeWithOneRound();

        compilation.RemoveColumns(
                new Flt64(5.0),
                100,
                _ => new Flt64(9.0),
                new HashSet<TestIterativeTask>(),
                new HashSet<TestIterativeTask>(),
                new object())
            .IsOk.Should().BeFalse("columns can only be retired from a linear meta model");
    }

    /// <summary>
    /// **回收的列既被固定为 0，也被摘出模型** / A retired column is both pinned to 0 and dropped from the model.
    /// </summary>
    /// <remarks>
    /// 只摘变量不固定的话，`remove` 若未生效该列仍可被选中；只固定不摘的话，模型里留着一个已知取 0 的列，
    /// 每轮求解都要带着它。两件事都要做，判据也都要看。
    ///
    /// Dropping without pinning leaves the column selectable if `remove` did not take effect; pinning without dropping leaves a known-zero column in the model that every solve
    /// carries. Both are required, so the criterion checks both.
    /// </remarks>
    [Fact]
    public void RemoveColumns_ShouldPinAndDropTheRetiredColumn() {
        (IterativeTaskCompilation<TestIterativeTask, TestTask, TestExecutor, TestAssignmentPolicy> compilation,
            LinearMetaModel<Flt64> model,
            _) = MakeWithOneRound();
        var before = ColumnVariables(model).ToList();
        before.Should().HaveCount(2);
        CombinationVariableItem<UInt8, Binary> retired = before.Single(variable => variable.Name == "x_0_T1");

        compilation.RemoveColumns(
                new Flt64(5.0),
                100,
                task => task.Id == "T1" ? new Flt64(9.0) : new Flt64(1.0),
                new HashSet<TestIterativeTask>(),
                new HashSet<TestIterativeTask>(),
                model)
            .IsOk.Should().BeTrue();

        retired.Range.Fixed.Should().BeTrue("the retired column is pinned");
        retired.LowerBound!.Value.ToFlt64().ToDouble().Should().BeApproximately(0.0, 1e-12);
        ColumnVariables(model).Should().ContainSingle("only the kept column remains in the model");
        ColumnVariables(model).Single().Name.Should().Be("x_0_T2");
    }

    /// **fixed 的任务不被回收** / A fixed task is not retired.
    [Fact]
    public void RemoveColumns_ShouldKeepFixedTasks() {
        (IterativeTaskCompilation<TestIterativeTask, TestTask, TestExecutor, TestAssignmentPolicy> compilation,
            LinearMetaModel<Flt64> model,
            _) = MakeWithOneRound();

        compilation.RemoveColumns(
                new Flt64(5.0),
                100,
                _ => new Flt64(9.0),
                new HashSet<TestIterativeTask> { compilation.Tasks[0] },
                new HashSet<TestIterativeTask>(),
                model)
            .IsOk.Should().BeTrue();

        ColumnVariables(model).Should().ContainSingle();
        ColumnVariables(model).Single().Name.Should().Be("x_0_T1", "the first task is fixed");
    }

    /// **kept 的任务不被回收** / A kept task is not retired.
    [Fact]
    public void RemoveColumns_ShouldKeepKeptTasks() {
        (IterativeTaskCompilation<TestIterativeTask, TestTask, TestExecutor, TestAssignmentPolicy> compilation,
            LinearMetaModel<Flt64> model,
            _) = MakeWithOneRound();

        compilation.RemoveColumns(
                new Flt64(5.0),
                100,
                _ => new Flt64(9.0),
                new HashSet<TestIterativeTask>(),
                new HashSet<TestIterativeTask> { compilation.Tasks[0] },
                model)
            .IsOk.Should().BeTrue();

        ColumnVariables(model).Should().ContainSingle();
        ColumnVariables(model).Single().Name.Should().Be("x_0_T1", "the first task is kept this round");
    }

    /// **低于上限的列不被回收** / Columns below the cutoff are not retired.
    [Fact]
    public void RemoveColumns_ShouldKeepColumnsBelowTheCutoff() {
        (IterativeTaskCompilation<TestIterativeTask, TestTask, TestExecutor, TestAssignmentPolicy> compilation,
            LinearMetaModel<Flt64> model,
            _) = MakeWithOneRound();

        compilation.RemoveColumns(
                new Flt64(5.0),
                100,
                _ => new Flt64(4.9),
                new HashSet<TestIterativeTask>(),
                new HashSet<TestIterativeTask>(),
                model)
            .IsOk.Should().BeTrue();

        ColumnVariables(model).Should().HaveCount(2, "both columns are below the cutoff");
    }

    /// <summary>
    /// **回收按任务自己的轮次找变量** / Retirement finds the variable in the task's own round.
    /// </summary>
    /// <remarks>
    /// 第 0 轮 1 列、第 1 轮 2 列。第二轮的第一列在第 1 轮的下标 0，而第 0 轮的下标 0 是**另一列**。
    /// 按下标跨轮取，会固定并摘掉第 0 轮的那一列，而真正该回收的列仍可被选中。
    ///
    /// Round 0 has one column, round 1 has two. Round 1's first column is index 0 there, while index 0 in round 0 is **a different column**. Taking the index across rounds pins
    /// and drops round 0's column while the column that should be retired stays selectable.
    /// </remarks>
    [Fact]
    public void RemoveColumns_ShouldFindTheVariableInTheTasksOwnRound() {
        (IterativeTaskCompilation<TestIterativeTask, TestTask, TestExecutor, TestAssignmentPolicy> compilation,
            LinearMetaModel<Flt64> model,
            List<TestExecutor> executors) = MakeWithOneRound();

        compilation.AddColumns(
                1,
                new[] {
                    new TestIterativeTask(new TestTask("T3", "T3") { Executor = executors[0] }, iteration: 1),
                    new TestIterativeTask(new TestTask("T4", "T4") { Executor = executors[0] }, iteration: 1),
                },
                model,
                _ => new Flt64(1.0),
                (_, _) => false)
            .IsOk.Should().BeTrue();

        compilation.RemoveColumns(
                new Flt64(5.0),
                100,
                task => task.Id == "T3" ? new Flt64(9.0) : new Flt64(1.0),
                new HashSet<TestIterativeTask>(),
                new HashSet<TestIterativeTask>(),
                model)
            .IsOk.Should().BeTrue();

        var remaining = model.Tokens.Tokens
            .Select(token => token.Variable)
            .OfType<CombinationVariableItem<UInt8, Binary>>()
            .Where(variable => variable.Name.StartsWith("x_", System.StringComparison.Ordinal))
            .Select(variable => variable.Name)
            .OrderBy(name => name)
            .ToList();
        remaining.Should().Equal(
            new[] { "x_0_T1", "x_0_T2", "x_1_T4" },
            "T3 was retired from round 1; round 0 is untouched");
    }

    /// <summary>
    /// **回收遍历整个已移除集合，不只是最新一轮** / Retirement walks the whole removed set, not just the newest round.
    /// </summary>
    /// <remarks>
    /// 第 0 轮建了 `T1`，第 1 轮建了 `T2`。回收时 `T1` 属于第 0 轮。
    /// 若只处理"最新一轮"的已移除任务（`task.Iteration == _x.Count - 1`），`T1` 会从聚合表里消失，
    /// 但**它的变量仍留在模型里可被选中**——一个"已回收"的列照旧出现在解里，且没有任何报错。
    ///
    /// Round 0 built `T1`, round 1 built `T2`. At retirement `T1` belongs to round 0. Handling only the **newest** round's removed tasks (`task.Iteration == _x.Count - 1`) makes `T1`
    /// vanish from the aggregates while **its variable stays selectable in the model** — a "retired" column still shows up in the solution, with no error at all.
    /// </remarks>
    [Fact]
    public void RemoveColumns_ShouldRetireColumnsFromEarlierRounds() {
        (IterativeTaskCompilation<TestIterativeTask, TestTask, TestExecutor, TestAssignmentPolicy> compilation,
            LinearMetaModel<Flt64> model,
            List<TestExecutor> executors) = MakeWithOneRound();

        compilation.AddColumns(
                1,
                new[] { new TestIterativeTask(new TestTask("T3", "T3") { Executor = executors[0] }, iteration: 1) },
                model,
                _ => new Flt64(1.0),
                (_, _) => false)
            .IsOk.Should().BeTrue();

        compilation.RemoveColumns(
                new Flt64(5.0),
                100,
                task => task.Id == "T1" ? new Flt64(9.0) : new Flt64(1.0),
                new HashSet<TestIterativeTask>(),
                new HashSet<TestIterativeTask>(),
                model)
            .IsOk.Should().BeTrue();

        var remaining = model.Tokens.Tokens
            .Select(token => token.Variable)
            .OfType<CombinationVariableItem<UInt8, Binary>>()
            .Where(variable => variable.Name.StartsWith("x_", System.StringComparison.Ordinal))
            .Select(variable => variable.Name)
            .OrderBy(name => name)
            .ToList();
        remaining.Should().Equal(
            new[] { "x_0_T2", "x_1_T3" },
            "T1 belongs to round 0 and must still be pinned and dropped there");
    }

    /// **列数仍超上限时上限衰减** / The cutoff decays while the column count still exceeds the ceiling.
    [Fact]
    public void RemoveColumns_ShouldDecayTheCutoffWhenOverTheCeiling() {
        (IterativeTaskCompilation<TestIterativeTask, TestTask, TestExecutor, TestAssignmentPolicy> compilation,
            LinearMetaModel<Flt64> model,
            _) = MakeWithOneRound();

        Result<Flt64, ErrorCode, Error<ErrorCode>> result = compilation.RemoveColumns(
            new Flt64(9.0), 1, _ => new Flt64(1.0), new HashSet<TestIterativeTask>(), new HashSet<TestIterativeTask>(), model);

        result.IsOk.Should().BeTrue();
        ((Ok<Flt64, ErrorCode, Error<ErrorCode>>)result).Value.ToDouble()
            .Should().BeApproximately(6.0, 1e-9, "9 → floor 9 * 2 / 3 = 6 with two columns over a ceiling of one");
    }

    /// **未超上限时上限不变** / The cutoff stays when not over the ceiling.
    [Fact]
    public void RemoveColumns_ShouldKeepTheCutoffUnderTheCeiling() {
        (IterativeTaskCompilation<TestIterativeTask, TestTask, TestExecutor, TestAssignmentPolicy> compilation,
            LinearMetaModel<Flt64> model,
            _) = MakeWithOneRound();

        Result<Flt64, ErrorCode, Error<ErrorCode>> result = compilation.RemoveColumns(
            new Flt64(9.0), 100, _ => new Flt64(1.0), new HashSet<TestIterativeTask>(), new HashSet<TestIterativeTask>(), model);

        result.IsOk.Should().BeTrue();
        ((Ok<Flt64, ErrorCode, Error<ErrorCode>>)result).Value.ToDouble().Should().BeApproximately(9.0, 1e-9);
    }
}
