// 迭代任务调度时间的判据 / Criteria for the iterative task scheduling time.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Tests.BunchCompilation;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.TaskCompilation;

/// <summary>
/// **迭代任务调度时间的 `Register` 从"静默 `Ok`"变成真的八张表（B562）** / The iterative task time's `Register` goes from a silent `Ok` to the eight real tables (B562).
/// </summary>
/// <remarks>
/// 之前 `Register` 直接返回 `Ok`：模型里什么都没有。它与任务调度时间**共用全部八张表**——那些表只依赖"每个任务的 `est` 是什么"。
/// 本批把 est 的来源抽成虚方法（`BuildEstimatePolynomials`），迭代这支重写成**常量零**（基线在这一支就是这样，且 `estEnd` 不加工期）。
///
/// 三种静默失效：
/// <list type="number">
/// <item>**`est` 用了任务调度时间那套独立变量**：迭代任务没有独立开始变量，那样等于凭空给每个任务一个可挪的开始时间；</item>
/// <item>**`estEnd` 加了工期**：基线在迭代这支不加，两者混起来会让结束时间凭空多一个工期；</item>
/// <item>**冗余范围被静默忽略**：基线的这一支返回失败（"暂未实现 withRedundancy 路径"），忽略会让调用方以为冗余已建模。</item>
/// </list>
///
/// Previously `Register` returned `Ok` outright with nothing in the model. It **shares all eight tables** with task scheduling time — those depend only on "what is each task's
/// `est`". This batch extracts the source of `est` into a virtual method (`BuildEstimatePolynomials`) and rewrites the iterative branch as **constant zero** (the baseline's shape
/// there, where `estEnd` does not add the duration either). Three silent failures: (1) **using the independent-variable scheme** — an iterative task has no independent start
/// variable, so that would conjure a movable start for every task; (2) **adding the duration to `estEnd`** — the baseline does not in this branch, and mixing them inflates every
/// end time by one duration; (3) **silently ignoring the redundancy range** — the baseline fails there ("the withRedundancy path is not implemented"), and ignoring it would let
/// the caller believe redundancy was modelled.
/// </remarks>
public class IterativeTaskTimeTests {
    private static readonly DateTimeOffset Origin = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static int ConstraintCount(LinearMetaModel<Flt64> model) =>
        ((IMechanismModel<Flt64>)LinearMechanismModel<Flt64>.InvokeAsync(model).GetAwaiter().GetResult().Value)
            .Constraints.Count;

    private static TimeWindow<Flt64> Window() => new(
        new TimeRange(Origin, Origin.AddHours(24)),
        true,
        TimeSpan.FromHours(1));

    private static (List<TestTask> Tasks, List<TestIterativeTask> IterativeTasks,
        IterativeTaskCompilation<TestIterativeTask, TestTask, TestExecutor, TestAssignmentPolicy> Compilation) Prepare() {
        var executors = new List<TestExecutor> { new("E1", "E1") };
        var tasks = new List<TestTask> {
            new("T1", "T1", new TimeRange(Origin.AddHours(2), Origin.AddHours(4))) {
                DelayEnabled = true,
                AdvanceEnabled = true,
                ScheduledTime = new TimeRange(Origin.AddHours(2), Origin.AddHours(4)),
                LastEndTime = Origin.AddHours(4),
                EarliestEndTime = Origin.AddHours(2),
                MaxDelay = TimeSpan.FromHours(1),
                MaxAdvance = TimeSpan.FromHours(1),
            },
        };
        foreach (TestTask task in tasks) {
            task.EnabledExecutorSet.Add(executors[0]);
        }
        var iterative = tasks.Select(task => new TestIterativeTask(task)).ToList();
        var compilation = new IterativeTaskCompilation<TestIterativeTask, TestTask, TestExecutor, TestAssignmentPolicy>(
            tasks, executors);
        return (tasks, iterative, compilation);
    }

    /// <summary>
    /// **任务调度时间的 `estEnd` 是 `est` 加工期** / Task scheduling time's `estEnd` is `est` plus the duration.
    /// </summary>
    /// <remarks>
    /// 这一条盯的是**默认钩子**：结束时间 = 开始时间 + 工期。之前没有任何判据查过它，于是"忘了加工期"这个变异能活下来——
    /// 而漏掉工期会让每个任务的完工时间凭空少一截，解依旧可行。
    ///
    /// 判据查的都是多项式里的**常量项**（`est` 的常量是 0），所以差就是工期本身。
    ///
    /// This one pins the **default hook**: end = start + duration. Nothing asserted it before, so a "forgot the duration" mutant survived — and dropping it shortens every task's
    /// completion out of thin air while the solution stays feasible. Both checks read the **constant term** (the `est` polynomial has none), so the difference is the duration.
    /// </remarks>
    [Fact]
    public void TaskSchedulingEstimateEnd_ShouldBeTheStartPlusTheDuration() {
        var executors = new List<TestExecutor> { new("E1", "E1") };
        var tasks = new List<TestTask> {
            new("T1", "T1", new TimeRange(Origin, Origin.AddHours(4))) {
                DelayEnabled = true,
                AdvanceEnabled = true,
                ScheduledTime = new TimeRange(Origin, Origin.AddHours(4)),
            },
        };
        foreach (TestTask task in tasks) {
            task.EnabledExecutorSet.Add(executors[0]);
        }
        var model = new LinearMetaModel<Flt64>();
        var compilation = new TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy>(tasks, executors);
        compilation.Register(model).IsOk.Should().BeTrue();
        var taskTime = new TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy>(
            Window(), tasks, compilation, advanceEnabled: true, delayEnabled: true);
        taskTime.Register(model).IsOk.Should().BeTrue();

        var start = (ILinearIntermediateSymbol<Flt64>)taskTime.EstimateStartTime![tasks[0]];
        var end = (ILinearIntermediateSymbol<Flt64>)taskTime.EstimateEndTime![tasks[0]];
        (end.Polynomial.Constant.ToDouble() - start.Polynomial.Constant.ToDouble())
            .Should().BeApproximately(4 * 3600.0, 1e-9, "the window is 4 hours long");
        end.Polynomial.Monomials.Select(m => m.Symbol).Should()
            .BeEquivalentTo(start.Polynomial.Monomials.Select(m => m.Symbol), "both ride the same `est` variable");
    }

    /// <summary>
    /// **默认钩子要求工期：没有工期就明确失败** / The default hook requires a duration, and fails loudly without one.
    /// </summary>
    /// <remarks>
    /// 默认的结束时间是"开始 + 工期"。没有工期时**不能悄悄当 0**：那样每个任务的完工时间都会塌到开始时刻，
    /// 目标照样算得出数、解照样可行，只是所有任务都"零时长"。基线里结束时间也是从这里加时长的，
    /// 所以这条要求属于**用它的人**，不属于共用循环——任务束与迭代任务那两支不加工期，也就不该被挡住。
    ///
    /// The default end time is "start + duration". Without one it must **not** quietly become 0: every task's completion would collapse onto its start instant, the objective would
    /// still produce a number and the solution would stay feasible, only every task would be "zero-length". The baseline adds the duration here too, so this requirement belongs to
    /// **whoever uses it**, not to the shared loop — the bunch and iterative branches do not add it and must not be blocked by it.
    /// </remarks>
    [Fact]
    public void TaskSchedulingRegister_ShouldFailWhenTheTaskHasNoDuration() {
        var executors = new List<TestExecutor> { new("E1", "E1") };
        var tasks = new List<TestTask> { new("T1", "T1") };
        foreach (TestTask task in tasks) {
            task.EnabledExecutorSet.Add(executors[0]);
        }
        var model = new LinearMetaModel<Flt64>();
        var compilation = new TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy>(tasks, executors);
        compilation.Register(model).IsOk.Should().BeTrue();
        var taskTime = new TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy>(
            Window(), tasks, compilation, advanceEnabled: true, delayEnabled: true);

        Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success,
            Fuookami.Ospf.Utils.Error.ErrorCode,
            Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>> result = taskTime.Register(model);

        result.IsOk.Should().BeFalse("a missing duration must not collapse every completion onto its start");
        ((Fuookami.Ospf.Utils.Functional.Failed<Fuookami.Ospf.Utils.Functional.Success,
            Fuookami.Ospf.Utils.Error.ErrorCode,
            Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>)result).Error.Message
            .Should().Contain("duration", "the message says which field is missing");
    }

    /// <summary>**注册之后 `est` / `estEnd` 都在，且都是常量零** / After registration both `est` and `estEnd` exist and are constant zero.</summary>
    /// <remarks>
    /// 判据查"多项式里没有单项"而不只是"表非空"：用独立变量那套会让表也非空，但里面有单项——那正是要排除的那一种。
    ///
    /// The criterion pins "no monomials" rather than "table non-null": the independent-variable scheme also fills the table, but with a monomial — exactly what must be excluded.
    /// </remarks>
    [Fact]
    public void Register_ShouldBuildConstantZeroEstimateTimes() {
        (List<TestTask> tasks, List<TestIterativeTask> _, IterativeTaskCompilation<TestIterativeTask, TestTask, TestExecutor, TestAssignmentPolicy> compilation) =
            Prepare();
        var model = new LinearMetaModel<Flt64>();
        // 编译结果要先注册：取消开着时任务时间要给偏移量套 `task_compilation` 掩码列，那张表由编译结果的 `Register` 填。
        // The compilation must be registered first: with cancellation on, the task time masks deviations with the `task_compilation` column, and that table is filled by the
        // compilation's own `Register`.
        compilation.Register(model).IsOk.Should().BeTrue();
        var taskTime = new IterativeTaskSchedulingTaskTime<TestIterativeTask, TestTask, TestExecutor, TestAssignmentPolicy>(
            Window(), tasks, compilation);

        taskTime.Register(model).IsOk.Should().BeTrue();

        var start = (ILinearIntermediateSymbol<Flt64>)taskTime.EstimateStartTime![tasks[0]];
        var end = (ILinearIntermediateSymbol<Flt64>)taskTime.EstimateEndTime![tasks[0]];
        start.Polynomial.Monomials.Should().BeEmpty("an iterative task has no independent start variable");
        start.Polynomial.Constant.ToDouble().Should().BeApproximately(0.0, 1e-9);
        end.Polynomial.Monomials.Should().BeEmpty();
        end.Polynomial.Constant.ToDouble().Should().BeApproximately(0.0, 1e-9, "this branch does not add the duration");
    }

    /// <summary>**六个开关全开 → 八张表都在** / All six switches on → all eight tables exist.</summary>
    /// <remarks>
    /// 基线把迭代任务调度时间的六个开关全写死成 `true`。判据查每张表都非空——少一张就说明那一支没走到。
    /// 特别地 `DelayLastEndTime` / `AdvanceEarliestEndTime` / `OnTime` 只有开关开着才建，正是靠这四个开关。
    ///
    /// The baseline hard-codes all six switches to `true` for iterative task time. The criterion pins that every table is non-null — one missing means a branch was skipped. In
    /// particular `DelayLastEndTime`, `AdvanceEarliestEndTime` and `OnTime` only exist with those switches on, which is what the flags are for.
    /// </remarks>
    [Fact]
    public void Register_ShouldBuildAllEightTables() {
        (List<TestTask> tasks, List<TestIterativeTask> _, IterativeTaskCompilation<TestIterativeTask, TestTask, TestExecutor, TestAssignmentPolicy> compilation) =
            Prepare();
        var model = new LinearMetaModel<Flt64>();
        // 编译结果要先注册：取消开着时任务时间要给偏移量套 `task_compilation` 掩码列，那张表由编译结果的 `Register` 填。
        // The compilation must be registered first: with cancellation on, the task time masks deviations with the `task_compilation` column, and that table is filled by the
        // compilation's own `Register`.
        compilation.Register(model).IsOk.Should().BeTrue();
        var taskTime = new IterativeTaskSchedulingTaskTime<TestIterativeTask, TestTask, TestExecutor, TestAssignmentPolicy>(
            Window(), tasks, compilation);

        taskTime.Register(model).IsOk.Should().BeTrue();

        taskTime.EstimateStartTime.Should().NotBeNull();
        taskTime.EstimateEndTime.Should().NotBeNull();
        taskTime.EstimateSlack.Should().NotBeNull();
        taskTime.DelayTime.Should().NotBeNull("delay is enabled");
        taskTime.AdvanceTime.Should().NotBeNull("advance is enabled");
        taskTime.OverMaxDelayTime.Should().NotBeNull("over-max delay is enabled");
        taskTime.OverMaxAdvanceTime.Should().NotBeNull("over-max advance is enabled");
        taskTime.DelayLastEndTime.Should().NotBeNull("the last-end-time criterion is on");
        taskTime.AdvanceEarliestEndTime.Should().NotBeNull("the earliest-end-time criterion is on");
        taskTime.OnTime.Should().NotBeNull("on-time is defined while those criteria are on");
    }

    /// <summary>**冗余范围 → 失败（基线也没实现）** / A redundancy range fails (the baseline does not implement it either).</summary>
    /// <remarks>
    /// 而且必须**在建任何符号之前**失败：先建一半再报错会留下一个半成品模型，后续求解会看到一个缺了一半的模型。
    /// 判据因此同时断言"失败"与"模型里没有被塞进任何符号"。
    ///
    /// And it must fail **before adding any symbol**: failing halfway leaves a half-built model that a later solve would silently accept. The criterion therefore asserts both
    /// the failure and that nothing was added to the model.
    /// </remarks>
    [Fact]
    public void Register_ShouldFailOnARedundancyRangeWithoutTouchingTheModel() {
        (List<TestTask> tasks, List<TestIterativeTask> _, IterativeTaskCompilation<TestIterativeTask, TestTask, TestExecutor, TestAssignmentPolicy> compilation) =
            Prepare();
        var model = new LinearMetaModel<Flt64>();
        // 编译结果要先注册：取消开着时任务时间要给偏移量套 `task_compilation` 掩码列，那张表由编译结果的 `Register` 填。
        // The compilation must be registered first: with cancellation on, the task time masks deviations with the `task_compilation` column, and that table is filled by the
        // compilation's own `Register`.
        compilation.Register(model).IsOk.Should().BeTrue();
        var taskTime = new IterativeTaskSchedulingTaskTime<TestIterativeTask, TestTask, TestExecutor, TestAssignmentPolicy>(
            Window(), tasks, compilation, redundancyRange: TimeSpan.FromHours(1));

        int before = ConstraintCount(model);
        Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success,
            Fuookami.Ospf.Utils.Error.ErrorCode,
            Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>> result = taskTime.Register(model);

        result.IsOk.Should().BeFalse("the redundancy path is unimplemented");
        string message = ((Fuookami.Ospf.Utils.Functional.Failed<Fuookami.Ospf.Utils.Functional.Success,
            Fuookami.Ospf.Utils.Error.ErrorCode,
            Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>)result).Error.Message;
        message.Should().Contain("redundancyRange", "the message names what to drop");
        // "模型没被动过"要看**约束数的增量**：`est_slack` 会往模型里写行，多出一行就说明失败发生在建表之后。
        // 比"约束为空"稳：编译结果自己可能会加行，那不该被算成任务时间的账。
        // "The model is untouched" is checked as the **delta in constraint count**: `est_slack` writes rows, so one extra means the failure happened after building tables. This is
        // steadier than "constraints are empty", since the compilation itself may add rows and those must not be charged to the task time.
        ConstraintCount(model).Should().Be(before, "the failure must happen before anything is registered");
    }

    /// <summary>**非模型被拒** / A non-model is rejected.</summary>
    [Fact]
    public void Register_ShouldRejectANonModel() {
        (List<TestTask> tasks, List<TestIterativeTask> _, IterativeTaskCompilation<TestIterativeTask, TestTask, TestExecutor, TestAssignmentPolicy> compilation) =
            Prepare();
        var taskTime = new IterativeTaskSchedulingTaskTime<TestIterativeTask, TestTask, TestExecutor, TestAssignmentPolicy>(
            Window(), tasks, compilation);

        taskTime.Register(new object()).IsOk.Should().BeFalse();
    }
}
