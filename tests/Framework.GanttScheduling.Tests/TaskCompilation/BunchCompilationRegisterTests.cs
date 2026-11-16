// 任务束编译注册的判据 / Criteria for the bunch compilation registration.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Tests.BunchCompilation;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.TaskCompilation;

/// <summary>
/// **任务束编译的 `Register` 从"静默 `Ok`"变成真的六样东西** / The bunch compilation's `Register` goes from a silent `Ok` to six real things.
/// </summary>
/// <remarks>
/// 之前 `Register` 直接返回 `Ok`：模型里什么都没有。而任务束调度时间的偏移掩码要读 `task_compilation` 那张表，
/// 于是整条路径只能失败——这里把这张表（连同 `y` / `bunch_cost` / 分配列占位 / `z` / `executor_compilation`）建出来。
///
/// 三种静默失效：
/// <list type="number">
/// <item>**`task_compilation` 里塞了 `Σ_e x[t,e]`**：任务束的分配列是**每轮**建的，注册时还不存在；</item>
/// <item>**锁定的取消任务用加约束实现**：**钉在范围上**，加约束会在每轮建模时多一条行；</item>
/// <item>**没有空闲时 `executor_compilation` 写成 `null`**：它是"这个执行器被用了多少"的载体，应当是常量零。</item>
/// </list>
///
/// Previously `Register` returned `Ok` outright with nothing in the model. The bunch task time's deviation masks read the `task_compilation` table, so that whole path could only
/// fail — builds it (together with `y`, `bunch_cost`, the assignment placeholders, `z` and `executor_compilation`). Three silent failures: (1) **putting `Σ_e x[t,e]`
/// into `task_compilation`** — a bunch's assignment columns are created **per round** and do not exist at registration; (2) **pinning locked cancellation tasks with a constraint** —
/// the code pins the **range**, and a constraint would add one more row per round; (3) **making `executor_compilation` `null` without leisure** — it is the carrier of "how
/// much this executor is used" and must be a constant zero.
/// </remarks>
public class BunchCompilationRegisterTests {
    private static BunchCompilation<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy> Prepare(
        out List<TestTask> tasks,
        out List<TestExecutor> executors,
        bool withExecutorLeisure = true,
        params string[] lockedTaskIds) {
        executors = new List<TestExecutor> { new("E1", "E1") };
        tasks = new List<TestTask> {
            new("T1", "T1", new TimeRange(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2024, 1, 1, 2, 0, 0, TimeSpan.Zero))),
            new("T2", "T2", new TimeRange(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2024, 1, 1, 2, 0, 0, TimeSpan.Zero))),
        };
        foreach (TestTask task in tasks) {
            task.EnabledExecutorSet.Add(executors[0]);
        }
        var locked = new HashSet<TestTask>(tasks.Where(task => lockedTaskIds.Contains(task.Id)));
        return new BunchCompilation<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy>(
            tasks, executors, locked, withExecutorLeisure);
    }

    /// <summary>**锁定的取消列被钉成 1，其余不钉** / A locked cancellation column is pinned to 1, the rest are not.</summary>
    /// <remarks>
    /// 判据把两种情况**成对**跑：只测"锁定的钉了"会让"全都钉"通过，只测"没锁的不钉"会让"全都不钉"通过。
    ///
    /// The criterion runs both cases **as a pair**: "the locked one is pinned" alone passes if all are pinned, and "the unlocked one is not" alone passes if none are.
    /// </remarks>
    [Fact]
    public void Register_ShouldPinOnlyTheLockedCancellationColumn() {
        BunchCompilation<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy> compilation =
            Prepare(out List<TestTask> tasks, out _, lockedTaskIds: "T1");
        var model = new LinearMetaModel<Flt64>();

        compilation.Register(model).IsOk.Should().BeTrue();

        // `y` 是 `BinVariable1` 的一个分量，取出来的静态类型是 `CombinationVariableItem<UInt8, Binary>`，不是 `BinVar`
        // （后者是独立变量）。按实际类型取范围。
        // A component of `BinVariable1` is statically a `CombinationVariableItem<UInt8, Binary>`, not a `BinVar` (that one is an independent variable). Read ranges off the
        // actual type.
        var locked = (CombinationVariableItem<UInt8, Binary>)compilation.Cancellation[(IAbstractTask<TestExecutor, TestAssignmentPolicy>)tasks[0]];
        var free = (CombinationVariableItem<UInt8, Binary>)compilation.Cancellation[(IAbstractTask<TestExecutor, TestAssignmentPolicy>)tasks[1]];
        locked.Range.Fixed.Should().BeTrue("a locked cancellation must be pinned");
        locked.Range.LowerBound!.Value.Unwrap().Should().Be(locked.Range.UpperBound!.Value.Unwrap());
        free.Range.Fixed.Should().BeFalse("an unlocked cancellation stays a choice");
    }

    /// <summary>**`task_compilation` 只有 `y`，没有分配列** / `task_compilation` is just `y`, with no assignment columns.</summary>
    /// <remarks>
    /// 分配列是每轮建的 `x_{iteration}`，注册时还不存在。把它们塞进来会让"任务被完成"这条语义在注册阶段就指向一个空列，
    /// 而读它的人（偏移掩码）拿到的掩码恒为零——被取消和不被取消的任务掩码一样。
    ///
    /// The assignment columns are the per-round `x_{iteration}` and do not exist at registration. Putting them in would make "the task is done" point at an empty column from the
    /// start, and a reader (the deviation mask) would get an identically zero mask — cancelled and non-cancelled tasks would mask alike.
    /// </remarks>
    [Fact]
    public void Register_ShouldBuildTaskCompilationFromTheCancellationColumnOnly() {
        BunchCompilation<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy> compilation =
            Prepare(out List<TestTask> tasks, out _);
        var model = new LinearMetaModel<Flt64>();

        compilation.Register(model).IsOk.Should().BeTrue();

        var symbol = (ILinearIntermediateSymbol<Flt64>)compilation.TaskCompilationTable[
            (IAbstractTask<TestExecutor, TestAssignmentPolicy>)tasks[0]];
        symbol.Polynomial.Monomials.Should().ContainSingle("`task_compilation_t = y[t]`");
        symbol.Polynomial.Monomials[0].Symbol.Should().BeSameAs(
            compilation.Cancellation[(IAbstractTask<TestExecutor, TestAssignmentPolicy>)tasks[0]]);
        symbol.Polynomial.Constant.ToDouble().Should().BeApproximately(0.0, 1e-9);
    }

    /// <summary>**有空闲 → `executor_compilation` 指向 `z`；没空闲 → 常量零** / With leisure it rides `z`; without it is constant zero.</summary>
    /// <remarks>
    /// 必须**成对**测：只测"有休闲时指向 z"会让"总是指向 z"通过（没有 z 时就会炸），只测"没休闲时是零"会让"总是零"通过。
    ///
    /// Must be tested **as a pair**: "rides `z` when leisure is on" alone passes for "always rides `z`" (which would blow up without one), and "zero without leisure" alone passes
    /// for "always zero".
    /// </remarks>
    [Fact]
    public void Register_ShouldTieTheExecutorCompilationToTheLeisureColumnOnlyWhenItExists() {
        BunchCompilation<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy> withLeisure =
            Prepare(out _, out List<TestExecutor> executors, withExecutorLeisure: true);
        withLeisure.Register(new LinearMetaModel<Flt64>()).IsOk.Should().BeTrue();
        var withSymbol = (ILinearIntermediateSymbol<Flt64>)withLeisure.ExecutorCompilation[executors[0]];
        withSymbol.Polynomial.Monomials.Should().ContainSingle();
        withSymbol.Polynomial.Monomials[0].Symbol.Should().BeSameAs(withLeisure.Leisure[executors[0]]);

        BunchCompilation<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy> withoutLeisure =
            Prepare(out _, out List<TestExecutor> executors2, withExecutorLeisure: false);
        withoutLeisure.Register(new LinearMetaModel<Flt64>()).IsOk.Should().BeTrue();
        var withoutSymbol = (ILinearIntermediateSymbol<Flt64>)withoutLeisure.ExecutorCompilation[executors2[0]];
        withoutSymbol.Polynomial.Monomials.Should().BeEmpty("without leisure there is no column to ride");
        withoutSymbol.Polynomial.Constant.ToDouble().Should().BeApproximately(0.0, 1e-9);
    }

    /// <summary>**`bunch_cost` 建出来并挂到 `TaskCost`** / `bunch_cost` exists and is exposed as `TaskCost`.</summary>
    [Fact]
    public void Register_ShouldBuildTheBunchCostCarrier() {
        BunchCompilation<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy> compilation =
            Prepare(out _, out _);

        compilation.Register(new LinearMetaModel<Flt64>()).IsOk.Should().BeTrue();

        compilation.TaskCost.Should().NotBeNull("the objective needs a carrier");
        ((ILinearIntermediateSymbol<Flt64>)compilation.TaskCost!).Name.Should().Be("bunch_cost");
    }

    /// <summary>**同一模型上重复注册是空操作** / Re-registering on the same model is a no-op.</summary>
    /// <remarks>
    /// `model.add` 是无条件执行的：重复注册会撞上重名符号而**失败**。这条判据钉住"按模型实例记住"这个守卫。
    ///
    /// `model.add` is unconditional: a repeat call hits a repeated symbol and **fails**. This criterion pins the "remember the model instance" guard.
    /// </remarks>
    [Fact]
    public void Register_ShouldBeANoOpWhenRepeatedOnTheSameModel() {
        BunchCompilation<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy> compilation =
            Prepare(out _, out _);
        var model = new LinearMetaModel<Flt64>();

        compilation.Register(model).IsOk.Should().BeTrue();
        compilation.Register(model).IsOk.Should().BeTrue("re-registering on the same model must not hit a repeated symbol");
    }
}
