// 换模型后约束不搬的判据 / Criteria for constraints not following a model switch.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Tests.BunchCompilation;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.TaskCompilation;

/// <summary>
/// **符号跟着编译走，约束只跟着模型走** / Symbols follow the compilation; constraints only follow the model.
/// </summary>
/// <remarks>
/// `ReattachTo`（B540）把**符号**搬到新模型上，但 `AddColumns` 加的 `xor[e] ≥ x[t]` 与跨轮冲突是**模型的行**，
/// 不会跟着搬。于是"先在第 0 个模型上加列、再注册到第 1 个模型"会得到一个
/// **有全部列、却一条列相关约束都没有**的模型。
///
/// 这个模型**可解**——只是每列都可以随便选：`xor` 不再被下界约束住（执行器使用免费）、
/// 同一任务的两轮列可以同时被选中（跨轮冲突消失）。答案合法，代价低得离谱。
///
/// 基线同样如此（`addColumns` 的约束只加进传给它的那个模型），所以本批**不修**，只钉住：
/// 调用方换模型之后必须自己再跑一遍 `AddColumns`（或等下一批提供重放）。
///
/// `ReattachTo` (B540) moves the **symbols** to a new model, but the `xor[e] ≥ x[t]` rows and the cross-round conflicts added by `AddColumns` are **model rows** and do not follow.
/// So "add columns on model 0, then register into model 1" yields a model with **every column and not one column-related constraint**.
///
/// That model **solves** — every column is simply free to choose: `xor` is no longer bounded below (executor usage is free), and two rounds' columns of the same task can be selected
/// together (the cross-round conflicts are gone). The answer is legal and absurdly cheap.
///
/// The baseline behaves the same way (its `addColumns` constraints go only into the model handed to it), so this batch **does not fix it** — it pins it: after switching models the caller
/// must run `AddColumns` again (or wait for a replay in a later batch).
/// </remarks>
public class IterativeConstraintReattachmentTests {
    private static (IterativeTaskCompilation<TestIterativeTask, TestTask, TestExecutor, TestAssignmentPolicy> Compilation,
        LinearMetaModel<Flt64> Model,
        List<TestExecutor> Executors) MakeWithOneRound() {
        var executors = new List<TestExecutor> { new("E1", "E1"), new("E2", "E2") };
        var compilation = new IterativeTaskCompilation<TestIterativeTask, TestTask, TestExecutor, TestAssignmentPolicy>(
            new List<TestTask> { new("T1", "T1"), new("T2", "T2") }, executors);
        var model = new LinearMetaModel<Flt64>();
        compilation.Register(model).IsOk.Should().BeTrue();
        compilation.AddColumns(
                0,
                new[] {
                    new TestIterativeTask(new TestTask("T1", "T1") { Executor = executors[0] }),
                    new TestIterativeTask(new TestTask("T2", "T2") { Executor = executors[1] }),
                },
                model,
                _ => new Flt64(1.0),
                (_, _) => true)
            .IsOk.Should().BeTrue();
        return (compilation, model, executors);
    }

    /// **加列确实在当期模型上留下约束** / Adding columns really leaves constraints on the model it was given.
    [Fact]
    public void AddColumns_ShouldAddItsConstraintsToTheGivenModel() {
        (_, LinearMetaModel<Flt64> model, _) = MakeWithOneRound();

        model.Constraints.Should().NotBeEmpty("`xor` and the cross-round conflicts are model rows");
        model.Constraints.Select(constraint => constraint.Name)
            .Should().Contain(name => name.StartsWith("xor_", System.StringComparison.Ordinal));
    }

    /// <summary>
    /// **换模型后那些约束也在新模型上** / After switching models those constraints are on the new model too.
    /// </summary>
    /// <remarks>
    /// 没有这一条时，新模型会**有全部列、却一条列相关约束都没有**：`xor` 不再被下界约束住（执行器使用免费），
    /// 同一任务的两轮列可以同时被选中（跨轮冲突消失）。答案合法，代价低得离谱。
    ///
    /// 基线把 `addColumns` 的约束只加进传给它的那个模型，所以它**没有**这个问题需要的搬运；
    /// 本实现按 B540 的 `ReattachTo` 一并重放——否则"换模型"这个已落地能力只搬了一半。
    ///
    /// Without this, the new model would have **every column and not one column-related constraint**: `xor` is no longer bounded below (executor usage is free) and two rounds' columns
    /// of the same task can be selected together (the cross-round conflicts are gone). The answer is legal and absurdly cheap.
    ///
    /// The baseline's `addColumns` adds its constraints only to the model handed to it, so it has no carrying logic; this implementation replays them as part of B540's `ReattachTo` —
    /// otherwise the already-landed "switch model" capability would carry only half of what it must.
    /// </remarks>
    [Fact]
    public void Register_ShouldCarryTheColumnConstraintsIntoASecondModel() {
        (IterativeTaskCompilation<TestIterativeTask, TestTask, TestExecutor, TestAssignmentPolicy> compilation,
            LinearMetaModel<Flt64> first,
            _) = MakeWithOneRound();
        int xorOnFirst = first.Constraints.Count(constraint =>
            constraint.Name.StartsWith("xor_", System.StringComparison.Ordinal));
        xorOnFirst.Should().Be(2, "one `xor` row per column");
        int conflictsOnFirst = first.Constraints.Count(constraint =>
            constraint.Name.StartsWith("task_conflict_", System.StringComparison.Ordinal));
        conflictsOnFirst.Should().BeGreaterThan(0, "round 0's own conflicting pairs produce rows");
        using var second = new LinearMetaModel<Flt64>();

        compilation.Register(second).IsOk.Should().BeTrue();

        second.Tokens.Tokens.Select(token => token.Variable.Name)
            .Should().Contain("x_0_T1", "the columns themselves do follow the compilation");
        second.Constraints.Count(constraint =>
                constraint.Name.StartsWith("xor_", System.StringComparison.Ordinal))
            .Should().Be(2, "the `xor` rows are replayed");
        second.Constraints.Count(constraint =>
                constraint.Name.StartsWith("task_conflict_", System.StringComparison.Ordinal))
            .Should().Be(conflictsOnFirst, "the cross-round conflicts are replayed too");
    }

    /// <summary>
    /// **已回收的列不会被换模型复活** / A retired column is not revived by a model switch.
    /// </summary>
    /// <remarks>
    /// `RemoveColumns` 把列固定为 0 并从模型里摘掉。若 `ReattachTo` 把它再搬进新模型，
    /// 那条"已回收"的列又回来了——它仍被固定为 0，所以选不上，但**模型里多了一批永远取 0 的列**，
    /// 而引用它的约束也一起被搬过去。
    ///
    /// `RemoveColumns` pins the column to 0 and drops it from the model. If `ReattachTo` moves it into a new model, that "retired" column is back — still pinned to 0, so unselectable,
    /// but **the model now carries a batch of permanently zero columns**, and the constraints referring to it move along too.
    /// </remarks>
    [Fact]
    public void Register_ShouldNotReviveRetiredColumns() {
        (IterativeTaskCompilation<TestIterativeTask, TestTask, TestExecutor, TestAssignmentPolicy> compilation,
            LinearMetaModel<Flt64> model,
            _) = MakeWithOneRound();
        compilation.RemoveColumns(
                new Flt64(5.0),
                100,
                _ => new Flt64(9.0),
                new HashSet<TestIterativeTask>(),
                new HashSet<TestIterativeTask>(),
                model)
            .IsOk.Should().BeTrue();
        using var second = new LinearMetaModel<Flt64>();

        compilation.Register(second).IsOk.Should().BeTrue();

        var names = second.Tokens.Tokens.Select(token => token.Variable.Name).ToList();
        names.Should().NotContain("x_0_T1", "the retired column must stay retired");
        names.Should().NotContain("x_0_T2");
        second.Constraints.Select(constraint => constraint.Name)
            .Should().NotContain(name => name.StartsWith("xor_", System.StringComparison.Ordinal),
                "the constraints over retired columns are not replayed");
    }

    /// **重放的约束指向的是同一批变量** / The replayed constraints refer to the same variables.
    [Fact]
    public void Register_ShouldReplayConstraintsOverTheSameVariables() {
        (IterativeTaskCompilation<TestIterativeTask, TestTask, TestExecutor, TestAssignmentPolicy> compilation,
            LinearMetaModel<Flt64> first,
            _) = MakeWithOneRound();
        using var second = new LinearMetaModel<Flt64>();
        compilation.Register(second).IsOk.Should().BeTrue();

        string firstXor = first.Constraints
            .Select(constraint => constraint.Name)
            .First(name => name.StartsWith("xor_", System.StringComparison.Ordinal));
        string secondXor = second.Constraints
            .Select(constraint => constraint.Name)
            .First(name => name.StartsWith("xor_", System.StringComparison.Ordinal));

        secondXor.Should().Be(firstXor, "the same row, replayed under the same name");
    }
}
