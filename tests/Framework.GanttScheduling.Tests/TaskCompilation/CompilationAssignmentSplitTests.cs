// `task_assignment` 中间符号与 `x` 变量分开的判据 / Criteria for keeping `task_assignment` and `x` apart.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Tests.BunchCompilation;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.TaskCompilation;

/// <summary>
/// **同名不同物：`TaskAssignment` 是中间符号，`Assignment` 是 `x` 变量** / Same name, different things: `TaskAssignment` is the intermediate symbol, `Assignment` is the `x` variable.
/// </summary>
/// <remarks>
/// 基线 `Compilation` 同时有 `x: BinVariable2` 与 `taskAssignment: LinearIntermediateSymbols2`。
/// 把两者挂在同一个名字上，任何"按任务取分配列"的调用方都会在**跨轮聚合**时分道扬镳——
/// 那一轮的真变量根本不存在，而中间符号是 `Σ x`。
///
/// 两者在 0/1 世界里读起来一样，所以判据必须钉住**类型**与**多项式成分**，不能只数条数。
///
/// The code `Compilation` has both `x: BinVariable2` and `taskAssignment: LinearIntermediateSymbols2`. Hanging both on one name makes every "get the assignment column for a task"
/// caller part ways under **cross-round aggregation** — the real variable does not exist for that round, while the intermediate symbol is `Σ x`.
///
/// The two read alike in a 0/1 world, so the criteria must pin the **type** and the **polynomial make-up**, not just the entry count.
/// </remarks>
public class CompilationAssignmentSplitTests {
    private static (TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy> Compilation, List<TestTask> Tasks, List<TestExecutor> Executors) MakeRegistered() {
        var tasks = new List<TestTask> { new("T1", "T1"), new("T2", "T2") };
        var executors = new List<TestExecutor> { new("E1", "E1"), new("E2", "E2") };
        var compilation = new TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy>(tasks, executors);
        using var model = new LinearMetaModel<Flt64>();
        compilation.Register(model).IsOk.Should().BeTrue();
        return (compilation, tasks, executors);
    }

    /// **`Assignment` 全是真变量** / `Assignment` holds nothing but real variables.
    [Fact]
    public void Assignment_ShouldHoldTheXVariables() {
        (TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy> compilation, List<TestTask> tasks, List<TestExecutor> executors) = MakeRegistered();

        compilation.Assignment.Should().HaveCount(4, "two tasks × two executors");
        compilation.Assignment.Values.Should().AllBeOfType<CombinationVariableItem<UInt8, Binary>>();
        compilation.Assignment[(tasks[0], executors[0])].Should().BeOfType<CombinationVariableItem<UInt8, Binary>>();
    }

    /// <summary>
    /// **`TaskAssignment` 是 `task_assignment` 中间符号** / `TaskAssignment` holds the `task_assignment` intermediate symbols.
    /// </summary>
    /// <remarks>
    /// 普通编译里 `task_assignment[t, e]` 恰是 `1·x[t, e]`，所以**多项式成分**才是分辨它与 `x` 变量的依据：
    /// 中间符号有 `Polynomial`，变量没有。
    ///
    /// In a plain compilation `task_assignment[t, e]` is exactly `1·x[t, e]`, so the **polynomial make-up** is what tells it apart from the `x` variable: the intermediate symbol has
    /// a `Polynomial`, the variable does not.
    /// </remarks>
    [Fact]
    public void TaskAssignment_ShouldHoldTheIntermediateSymbols() {
        (TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy> compilation, List<TestTask> tasks, List<TestExecutor> executors) = MakeRegistered();

        compilation.TaskAssignment.Should().HaveCount(4);
        compilation.TaskAssignment.Values.Should().AllBeOfType<LinearExpressionSymbol>();

        var symbol = (LinearExpressionSymbol)compilation.TaskAssignment[(tasks[0], executors[0])];
        symbol.Polynomial.Monomials.Should().ContainSingle("`task_assignment[t, e]` is `1·x[t, e]`");
        symbol.Polynomial.Monomials[0].Symbol.Should().Be(
            compilation.Assignment[(tasks[0], executors[0])],
            "the intermediate symbol wraps the very same `x` variable");
    }

    /// **两张表的键集合一致** / Both tables share the same key set.
    [Fact]
    public void TaskAssignment_ShouldCoverTheSameKeysAsAssignment() {
        (TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy> compilation, _, _) = MakeRegistered();

        compilation.TaskAssignment.Keys.Should().BeEquivalentTo(compilation.Assignment.Keys);
    }

    /// **构造期两张表都只有占位对象** / Both tables hold placeholders at construction.
    [Fact]
    public void TaskAssignment_ShouldBePlaceholdersBeforeRegistration() {
        var compilation = new TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy>(
            new List<TestTask> { new("T1", "T1") }, new List<TestExecutor> { new("E1", "E1") });

        compilation.TaskAssignment.Values.Should().NotContainItemsAssignableTo<LinearExpressionSymbol>(
            "no intermediate symbol exists before registration");
        compilation.Assignment.Values.Should().NotContainItemsAssignableTo<IVariableItem>(
            "no variable exists before registration");
    }

    /// <summary>
    /// **迭代编译的 `Assignment` 取最新一轮** / The iterative `Assignment` takes the latest round.
    /// </summary>
    /// <remarks>
    /// 迭代编译的 `x` 按轮存在，所以"任务 → 变量"不唯一。判据给两轮、只给第 1 轮加列，
    /// 断言取到的是第 1 轮的变量——按轮 0 取会拿到**不存在的变量**（`_x[0]` 只有第 0 轮的列）。
    ///
    /// The iterative `x` exists per round, so "task → variable" is not unique. The criterion adds two rounds but only fills round 1, then asserts round 1's variable is returned —
    /// taking round 0 would hand back **a variable that does not exist** (`_x[0]` only holds round 0's columns).
    /// </remarks>
    [Fact]
    public void Assignment_ShouldTakeTheLatestIterativeRound() {
        var executors = new List<TestExecutor> { new("E1", "E1") };
        var compilation = new IterativeTaskCompilation<TestIterativeTask, TestTask, TestExecutor, TestAssignmentPolicy>(
            new List<TestTask> { new("T1", "T1") }, executors);
        using var model = new LinearMetaModel<Flt64>();
        compilation.Register(model).IsOk.Should().BeTrue();
        compilation.Assignment.Should().BeEmpty("no round has been added yet");

        var task = new TestIterativeTask(new TestTask("T1", "T1") { Executor = executors[0] }, iteration: 1);
        compilation.AddColumns(1, new[] { task }, model, _ => new Flt64(1.0), (_, _) => false).IsOk.Should().BeTrue();

        compilation.Assignment.Should().ContainKey(((IAbstractTask<TestExecutor, TestAssignmentPolicy>)task, executors[0]));
        compilation.Assignment.Values.Should().AllBeOfType<CombinationVariableItem<UInt8, Binary>>();
    }
}
