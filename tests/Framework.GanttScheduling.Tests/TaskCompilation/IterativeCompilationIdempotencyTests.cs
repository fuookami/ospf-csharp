// 迭代编译重复注册的判据 / Criteria for registering an iterative compilation twice.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Tests.BunchCompilation;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.TaskCompilation;

/// <summary>
/// **迭代编译换模型时一个符号都不能漏** / Switching an iterative compilation's model must miss no symbol.
/// </summary>
/// <remarks>
/// 迭代编译的符号在第一次注册时全部建好：`y`、`bunch_cost`、两张聚合表、`xor`、`z`、执行器编译，以及每轮的 `x`。
/// 换模型时漏掉哪个都**不报错**：
///
/// | 漏掉的 | 后果 |
/// | --- | --- |
/// | `xor` | 执行器使用不受约束 |
/// | `bunch_cost` | 目标里没有代价项，第二个模型"更便宜" |
/// | 某一轮的 `x` | 那轮的列在新模型里不存在，冲突约束指向空 |
///
/// 三种都会让第二个模型的解**看起来可行而且更好**。
///
/// Every iterative symbol is built on the first registration: `y`, `bunch_cost`, the two aggregate tables, `xor`, `z`, the executor compilation, and each round's `x`. Missing any of
/// them when switching models **raises nothing**:
///
/// | Missing | Consequence |
/// | --- | --- |
/// | `xor` | executor usage is unconstrained |
/// | `bunch_cost` | no cost term in the objective; the second model is "cheaper" |
/// | one round's `x` | that round's columns do not exist there, and the conflict constraints point at nothing |
///
/// All three make the second model's solution **look feasible and better**.
/// </remarks>
public class IterativeCompilationIdempotencyTests {
    private static (IterativeTaskCompilation<TestIterativeTask, TestTask, TestExecutor, TestAssignmentPolicy> Compilation,
        LinearMetaModel<Flt64> Model,
        List<TestExecutor> Executors) MakeRegistered() {
        var executors = new List<TestExecutor> { new("E1", "E1"), new("E2", "E2") };
        var compilation = new IterativeTaskCompilation<TestIterativeTask, TestTask, TestExecutor, TestAssignmentPolicy>(
            new List<TestTask> { new("T1", "T1"), new("T2", "T2") }, executors);
        var model = new LinearMetaModel<Flt64>();
        compilation.Register(model).IsOk.Should().BeTrue();
        return (compilation, model, executors);
    }

    /// **同模型重复注册不多出符号** / Re-registering on the same model adds no symbols.
    [Fact]
    public void Register_ShouldNotDuplicateTheSymbolsOnTheSameModel() {
        (IterativeTaskCompilation<TestIterativeTask, TestTask, TestExecutor, TestAssignmentPolicy> compilation,
            LinearMetaModel<Flt64> model,
            _) = MakeRegistered();
        int tokensAfterFirst = model.Tokens.Tokens.Count;

        compilation.Register(model).IsOk.Should().BeTrue();

        model.Tokens.Tokens.Count.Should().Be(tokensAfterFirst, "re-registering must not build a second symbol set");
    }

    /// **同模型重复注册仍返回成功** / A second registration still returns Ok.
    [Fact]
    public void Register_ShouldReturnOkOnASecondCall() {
        (IterativeTaskCompilation<TestIterativeTask, TestTask, TestExecutor, TestAssignmentPolicy> compilation,
            LinearMetaModel<Flt64> model,
            _) = MakeRegistered();

        compilation.Register(model).IsOk.Should().BeTrue();
        compilation.Register(model).IsOk.Should().BeTrue("re-registering is an idempotent no-op, not an error");
    }

    /// **注册到第二个模型会带上全部符号** / A second model receives every symbol.
    [Fact]
    public void Register_ShouldCarryEverySymbolIntoASecondModel() {
        (IterativeTaskCompilation<TestIterativeTask, TestTask, TestExecutor, TestAssignmentPolicy> compilation,
            _,
            _) = MakeRegistered();
        using var second = new LinearMetaModel<Flt64>();

        compilation.Register(second).IsOk.Should().BeTrue();

        var names = second.Tokens.Tokens.Select(token => token.Variable.Name).ToList();
        names.Should().Contain(name => name.StartsWith("y_", System.StringComparison.Ordinal), "the cancellation columns");
        names.Should().Contain(name => name.StartsWith("xor_", System.StringComparison.Ordinal), "the executor usage columns");
        names.Should().Contain(name => name.StartsWith("z_", System.StringComparison.Ordinal), "the leisure columns");
        second.Tokens.Symbols.Select(symbol => symbol.Name).Should().Contain(
            "bunch_cost",
            "the objective term is an intermediate symbol, not a variable");
    }

    /// **多轮之后换模型，每轮的 `x` 都在** / After several rounds, every round's `x` reaches the new model.
    [Fact]
    public void Register_ShouldCarryEveryRoundIntoASecondModel() {
        (IterativeTaskCompilation<TestIterativeTask, TestTask, TestExecutor, TestAssignmentPolicy> compilation,
            LinearMetaModel<Flt64> model,
            List<TestExecutor> executors) = MakeRegistered();

        compilation.AddColumns(
                0,
                new[] { new TestIterativeTask(new TestTask("T1", "T1") { Executor = executors[0] }) },
                model,
                _ => new Flt64(1.0),
                (_, _) => false)
            .IsOk.Should().BeTrue();
        compilation.AddColumns(
                1,
                new[] { new TestIterativeTask(new TestTask("T2", "T2") { Executor = executors[1] }, iteration: 1) },
                model,
                _ => new Flt64(1.0),
                (_, _) => false)
            .IsOk.Should().BeTrue();

        using var second = new LinearMetaModel<Flt64>();
        compilation.Register(second).IsOk.Should().BeTrue();

        var names = second.Tokens.Tokens.Select(token => token.Variable.Name).ToList();
        names.Should().Contain("x_0_T1", "round 0's column");
        names.Should().Contain("x_1_T2", "round 1's column");
    }

    /// **换模型后同一批对象复用** / The same objects are reused across models.
    [Fact]
    public void Register_ShouldReuseTheSameSymbolsOnASecondModel() {
        (IterativeTaskCompilation<TestIterativeTask, TestTask, TestExecutor, TestAssignmentPolicy> compilation,
            _,
            List<TestExecutor> executors) = MakeRegistered();
        object executorCompilationBefore = compilation.ExecutorCompilation[executors[0]];
        using var second = new LinearMetaModel<Flt64>();

        compilation.Register(second).IsOk.Should().BeTrue();

        compilation.ExecutorCompilation[executors[0]]
            .Should().BeSameAs(executorCompilationBefore, "no second `executor_compilation` set is built");
    }
}
