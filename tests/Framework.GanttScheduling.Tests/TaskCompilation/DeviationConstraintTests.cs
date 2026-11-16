// 两个偏移约束的判据 / Criteria for the two deviation constraints.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service.Limits;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Tests.BunchCompilation;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.TaskCompilation;

/// <summary>
/// **两个偏移约束从空壳变成真约束行** / The two deviation constraints go from stubs to real rows.
/// </summary>
/// <remarks>
/// `TaskDelayTimeConstraint` 与 `TaskAdvanceTimeConstraint` 原来都直接返回 `Failed`。它们的行也是同一种，只差比较方向：
/// 不允许延迟 → `est ≤ 计划开始`；不允许提前 → `est ≥ 计划开始`。
///
/// 三种静默失效：
/// <list type="number">
/// <item>**方向写反**：把"不许晚"写成"不许早"，两个方向通常都有可行解，计划悄悄偏到另一边；</item>
/// <item>**全局开关开着时不过滤**：把"允许晚一点"的任务也钉死，等于把延迟开关作废；</item>
/// <item>**收进没有排定时间的任务**：`est` 相对什么钉住无从定义。</item>
/// </list>
///
/// Both used to return `Failed`. Their rows are again the same, differing only in direction: "may not delay" → `est ≤ planned start`, "may not advance" → `est ≥ planned start`.
/// Three silent failures: (1) **an inverted direction** — both directions usually stay feasible and the plan slides to the other side; (2) **no filtering while the global switch is
/// on** — pinning even the tasks allowed to move, voiding the switch; (3) **collecting tasks without a scheduled time** — "pinned relative to what" is undefined.
/// </remarks>
public class DeviationConstraintTests {
    private static readonly DateTimeOffset Origin = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static TimeWindow<Flt64> Window() => new(
        new TimeRange(Origin, Origin.AddHours(24)),
        true,
        TimeSpan.FromHours(1));

    private sealed record Setup(
        LinearMetaModel<Flt64> Model,
        List<TestTask> Tasks,
        TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy> Compilation,
        TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy> TaskTime);

    private static Setup Prepare(
        bool globalDelay,
        bool globalAdvance,
        params (string Id, int PlannedHour, bool Delay, bool Advance, bool WithScheduled)[] specs) {
        TimeWindow<Flt64> window = Window();
        var executors = new List<TestExecutor> { new("E1", "E1") };
        var tasks = specs
            .Select(spec => new TestTask(spec.Id, spec.Id, new TimeRange(Origin, Origin.AddHours(2))) {
                DelayEnabled = spec.Delay,
                AdvanceEnabled = spec.Advance,
                ScheduledTime = spec.WithScheduled
                    ? new TimeRange(Origin.AddHours(spec.PlannedHour), Origin.AddHours(spec.PlannedHour + 2))
                    : null,
            })
            .ToList();
        foreach (TestTask task in tasks) {
            task.EnabledExecutorSet.Add(executors[0]);
        }
        var model = new LinearMetaModel<Flt64>();
        var compilation = new TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy>(tasks, executors);
        compilation.Register(model).IsOk.Should().BeTrue();
        var taskTime = new TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy>(
            window, tasks, compilation, globalAdvance, globalDelay);
        taskTime.Register(model).IsOk.Should().BeTrue();
        return new Setup(model, tasks, compilation, taskTime);
    }

    private static List<Fuookami.Ospf.Core.Model.Mechanism.IConstraint<Flt64, Fuookami.Ospf.Math.Symbol.LinearCategory>> Rows(
        LinearMetaModel<Flt64> model,
        string prefix) =>
        ((IMechanismModel<Flt64>)LinearMechanismModel<Flt64>.InvokeAsync(model).GetAwaiter().GetResult().Value)
            .Constraints.Where(row => row.Name.StartsWith(prefix)).ToList();

    /// <summary>**延迟约束是 `est ≤ 计划开始`** / The delay constraint is `est ≤ planned start`.</summary>
    /// <remarks>
    /// 方向是这条判据的全部内容：写反了模型**仍然可行**（任务不许早而已），所以只查"有行"永远看不出问题。
    ///
    /// The direction is the whole criterion: inverted, the model **stays feasible** (tasks may just not start early), so checking "there is a row" never notices.
    /// </remarks>
    [Fact]
    public void DelayConstraint_ShouldPinTheStartNoLaterThanPlanned() {
        Setup setup = Prepare(
            globalDelay: true, globalAdvance: false,
            ("T1", 3, false, false, true));
        var constraint = new TaskDelayTimeConstraint<TestExecutor, TestAssignmentPolicy>(
            Window(), setup.Tasks, setup.TaskTime);

        constraint.Invoke(setup.Model).IsOk.Should().BeTrue();

        List<Fuookami.Ospf.Core.Model.Mechanism.IConstraint<Flt64, Fuookami.Ospf.Math.Symbol.LinearCategory>> rows =
            Rows(setup.Model, "task_delay_time");
        rows.Should().ContainSingle();
        rows[0].Name.Should().Be("task_delay_time_T1");
        rows[0].Sign.Should().Be(Fuookami.Ospf.Core.Model.Basic.ConstraintRelation.LessEqual,
            "not allowed to delay means the start may not be later");
        rows[0].Rhs.ToDouble().Should().BeApproximately(3 * 3600.0, 1e-6, "the planned start is 03:00");
    }

    /// <summary>**提前约束是 `est ≥ 计划开始`** / The advance constraint is `est ≥ planned start`.</summary>
    [Fact]
    public void AdvanceConstraint_ShouldPinTheStartNoEarlierThanPlanned() {
        Setup setup = Prepare(
            globalDelay: false, globalAdvance: true,
            ("T1", 3, false, false, true));
        var constraint = new TaskAdvanceTimeConstraint<TestExecutor, TestAssignmentPolicy>(
            Window(), setup.Tasks, setup.TaskTime);

        constraint.Invoke(setup.Model).IsOk.Should().BeTrue();

        List<Fuookami.Ospf.Core.Model.Mechanism.IConstraint<Flt64, Fuookami.Ospf.Math.Symbol.LinearCategory>> rows =
            Rows(setup.Model, "task_advance_time");
        rows.Should().ContainSingle();
        rows[0].Sign.Should().Be(Fuookami.Ospf.Core.Model.Basic.ConstraintRelation.GreaterEqual,
            "not allowed to advance means the start may not be earlier");
        rows[0].Rhs.ToDouble().Should().BeApproximately(3 * 3600.0, 1e-6);
    }

    /// <summary>**全局开关开着时要按任务开关过滤** / With the global switch on, the per-task switch filters.</summary>
    /// <remarks>
    /// `T1` 允许延迟、`T2` 不允许。只该有 `T2` 一行——把 `T1` 也钉住等于让延迟开关失效，而模型依旧可行。
    /// 反向：全局开关关着时**两个都钉**（它们本来就被钉住，这行冗余但无害）。两条一起才钉得住"只在全局开着时过滤"。
    ///
    /// `T1` may delay, `T2` may not. Only `T2` gets a row — pinning `T1` too would void the delay switch while staying feasible. The reverse: with the global switch off **both**
    /// are pinned (redundant but harmless). Together they pin "filter only while the global switch is on".
    /// </remarks>
    [Fact]
    public void DelayConstraint_ShouldFilterByTheTaskSwitchOnlyWhenTheGlobalSwitchIsOn() {
        Setup filtered = Prepare(
            globalDelay: true, globalAdvance: false,
            ("T1", 3, true, false, true),
            ("T2", 5, false, false, true));
        new TaskDelayTimeConstraint<TestExecutor, TestAssignmentPolicy>(Window(), filtered.Tasks, filtered.TaskTime)
            .Invoke(filtered.Model).IsOk.Should().BeTrue();
        Rows(filtered.Model, "task_delay_time").Select(row => row.Name)
            .Should().Equal(new[] { "task_delay_time_T2" }, "T1 is allowed to delay");

        Setup unfiltered = Prepare(
            globalDelay: false, globalAdvance: false,
            ("T1", 3, true, false, true),
            ("T2", 5, false, false, true));
        new TaskDelayTimeConstraint<TestExecutor, TestAssignmentPolicy>(Window(), unfiltered.Tasks, unfiltered.TaskTime)
            .Invoke(unfiltered.Model).IsOk.Should().BeTrue();
        Rows(unfiltered.Model, "task_delay_time").Should().HaveCount(2,
            "with the global switch off every scheduled task is pinned anyway");
    }

    /// <summary>**没有排定时间的任务被跳过** / A task without a scheduled time is skipped.</summary>
    [Fact]
    public void DelayConstraint_ShouldSkipTasksWithoutAScheduledTime() {
        Setup setup = Prepare(
            globalDelay: true, globalAdvance: false,
            ("T1", 3, false, false, false));
        var constraint = new TaskDelayTimeConstraint<TestExecutor, TestAssignmentPolicy>(
            Window(), setup.Tasks, setup.TaskTime);

        constraint.Invoke(setup.Model).IsOk.Should().BeTrue();
        Rows(setup.Model, "task_delay_time").Should().BeEmpty();
    }

    /// <summary>**非模型被拒** / A non-model is rejected.</summary>
    [Fact]
    public void Constraints_ShouldRejectANonModel() {
        Setup setup = Prepare(globalDelay: true, globalAdvance: true, ("T1", 3, false, false, true));

        new TaskDelayTimeConstraint<TestExecutor, TestAssignmentPolicy>(Window(), setup.Tasks, setup.TaskTime)
            .Invoke(new object()).IsOk.Should().BeFalse();
        new TaskAdvanceTimeConstraint<TestExecutor, TestAssignmentPolicy>(Window(), setup.Tasks, setup.TaskTime)
            .Invoke(new object()).IsOk.Should().BeFalse();
    }

    /// <summary>**任务时间未注册 est 时失败** / Fails when the task time has no est table.</summary>
    /// <remarks>
    /// 未注册时 `est` 根本不存在，行无从写起。返回 `Ok` 会让调用方以为任务已被钉住。
    ///
    /// Without registration there is no `est` to pin. Returning `Ok` would let the caller believe the task was pinned.
    /// </remarks>
    [Fact]
    public void DelayConstraint_ShouldFailWhenTheEstimateIsNotRegistered() {
        TimeWindow<Flt64> window = Window();
        var executors = new List<TestExecutor> { new("E1", "E1") };
        var tasks = new List<TestTask> {
            new("T1", "T1", new TimeRange(Origin, Origin.AddHours(2))) {
                ScheduledTime = new TimeRange(Origin.AddHours(3), Origin.AddHours(5)),
            },
        };
        tasks[0].EnabledExecutorSet.Add(executors[0]);
        using var model = new LinearMetaModel<Flt64>();
        var compilation = new TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy>(tasks, executors);
        compilation.Register(model).IsOk.Should().BeTrue();
        var taskTime = new TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy>(window, tasks, compilation);
        var constraint = new TaskDelayTimeConstraint<TestExecutor, TestAssignmentPolicy>(window, tasks, taskTime);

        constraint.Invoke(model).IsOk.Should().BeFalse("the estimated start time does not exist yet");
    }

#if SCIP_INTEGRATION
    /// <summary>
    /// **真实 SCIP：计划时间落在任务窗口之外时这条约束会让模型不可行** / Real SCIP: when the planned time falls outside the task window the row makes the model infeasible.
    /// </summary>
    /// <remarks>
    /// **这里实测到的一个 ceiling**：`est` 在注册时就被钉在**任务自身窗口**的开始上（不允许延迟时 `est ≤ time.Start`），
    /// 所以 `est ≤ scheduledTime.Start` 这条约束只有在**计划时间早于任务窗口**时才是**紧的**；其余情况它一律更松，等于冗余行。
    /// 基线的行为一致（它也只写这一行），所以照抄，不擅自把行换成 `min(time.Start, scheduled.Start)`。
    ///
    /// 判据取"紧"的那一侧：任务窗口 03:00–05:00、计划 01:00 → `est ≥ 10800` 与 `est ≤ 3600` 直接矛盾。
    /// 有约束 → 不可行；去掉 → 可行。两边都跑，因为只测不可行会让"模型本来别处坏掉"通过。
    ///
    /// **A ceiling measured in**: `est` is pinned to the **task's own window** start at registration (`est ≤ time.Start` when delay is forbidden), so the row
    /// `est ≤ scheduledTime.Start` is only **tight** when the planned time is earlier than the task window; otherwise it is looser and therefore redundant. The baseline behaves
    /// the same way (it writes only this row), so this copies it rather than quietly replacing it with `min(time.Start, scheduled.Start)`.
    ///
    /// The criterion takes the **tight** side: task window 03:00–05:00, planned 01:00 → `est ≥ 10800` and `est ≤ 3600` contradict. With the row → infeasible; without → feasible.
    /// Both halves are run, since "infeasible" alone would pass if the model were broken elsewhere.
    /// </remarks>
    [Fact]
    public void DelayConstraint_ShouldMakeAnInconsistentPlanInfeasibleUnderRealScip() {
        bool pinned = SolveDelay(applyConstraint: true);
        bool free = SolveDelay(applyConstraint: false);

        pinned.Should().BeFalse("the planned 01:00 sits before the 03:00 window, so the row is tight and contradicts the est bounds");
        free.Should().BeTrue("without the row the same model is feasible, so the infeasibility comes from it");
    }

    private static bool SolveDelay(bool applyConstraint) {
        TimeWindow<Flt64> window = Window();
        var executors = new List<TestExecutor> { new("E1", "E1") };
        var tasks = new List<TestTask> {
            new("T1", "T1", new TimeRange(Origin.AddHours(3), Origin.AddHours(5))) {
                ScheduledTime = new TimeRange(Origin.AddHours(1), Origin.AddHours(3)),
            },
        };
        tasks[0].EnabledExecutorSet.Add(executors[0]);
        using var model = new LinearMetaModel<Flt64>();
        var compilation = new TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy>(tasks, executors);
        compilation.Register(model).IsOk.Should().BeTrue();
        var taskTime = new TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy>(
            window, tasks, compilation, advanceEnabled: true, delayEnabled: true);
        taskTime.Register(model).IsOk.Should().BeTrue();
        if (applyConstraint) {
            new TaskDelayTimeConstraint<TestExecutor, TestAssignmentPolicy>(window, tasks, taskTime)
                .Invoke(model).IsOk.Should().BeTrue();
        }

        Fuookami.Ospf.Utils.Functional.Result<
            Fuookami.Ospf.Core.Solver.Output.FeasibleSolverOutput<Flt64>,
            Fuookami.Ospf.Utils.Error.ErrorCode,
            Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>> result =
            new Fuookami.Ospf.Core.Plugin.Scip.ScipColumnGenerationSolver()
                .SolveMILPAsync("gantt-deviation", model).GetAwaiter().GetResult();
        return result.IsOk;
    }
#endif
}
