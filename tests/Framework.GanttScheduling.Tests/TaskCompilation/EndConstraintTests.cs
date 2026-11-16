// 结束时间偏移约束的判据 / Criteria for the end-time deviation constraints.

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
/// **两个结束时间偏移约束从"返回 `Failed`"变成真的行** / The two end-time deviation constraints go from "returns `Failed`" to real rows.
/// </summary>
/// <remarks>
/// 行是 `estEnd ≤ 最晚结束` / `estEnd ≥ 最早结束`——**镜像的一对**，方向写反不会让模型不可行，只会让计划悄悄偏到另一侧。
///
/// 三种静默失效：
/// <list type="number">
/// <item>**方向写反**：`≤` 变 `≥`，两边通常都有可行解，求解成功、结果偏了；</item>
/// <item>**约束的符号错了**：行绑的是 `estEnd`，绑成 `est` 就等于在约束开始时间，工期一变结论全错；</item>
/// <item>**任务筛选反了**：开关开着时基线保留的是"**不**允许移动"的任务。</item>
/// </list>
///
/// The rows are `estEnd ≤ lastEnd` / `estEnd ≥ earliestEnd` — a **mirrored pair**; swapping them keeps the model feasible and merely slides the plan. Three silent failures:
/// (1) **inverted direction** — both sides usually feasible, the solve succeeds and the answer is skewed; (2) **wrong symbol** — the row binds `estEnd`; binding `est` constrains the
/// start instead and breaks as soon as the duration changes; (3) **inverted task filter** — with the switch on the baseline keeps the tasks that may **not** move.
/// </remarks>
public class EndConstraintTests {
    private static readonly DateTimeOffset Origin = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static TimeWindow<Flt64> Window() => new(
        new TimeRange(Origin, Origin.AddHours(24)),
        true,
        TimeSpan.FromHours(1));

    private sealed record Setup(
        LinearMetaModel<Flt64> Model,
        List<TestTask> Tasks,
        TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy> TaskTime);

    private static Setup Prepare(
        bool lastEndTime,
        bool earliestEndTime,
        params (string Id, bool Delay, bool Advance, DateTimeOffset? Last, DateTimeOffset? Earliest)[] specs) {
        TimeWindow<Flt64> window = Window();
        var executors = new List<TestExecutor> { new("E1", "E1") };
        var tasks = specs
            .Select(spec => new TestTask(spec.Id, spec.Id, new TimeRange(Origin, Origin.AddHours(2))) {
                DelayEnabled = spec.Delay,
                AdvanceEnabled = spec.Advance,
                ScheduledTime = new TimeRange(Origin.AddHours(2)),
                LastEndTime = spec.Last,
                EarliestEndTime = spec.Earliest,
            })
            .ToList();
        foreach (TestTask task in tasks) {
            task.EnabledExecutorSet.Add(executors[0]);
        }
        var model = new LinearMetaModel<Flt64>();
        var compilation = new TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy>(tasks, executors);
        compilation.Register(model).IsOk.Should().BeTrue();
        var taskTime = new TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy>(
            window, tasks, compilation,
            advanceEnabled: true, delayEnabled: true,
            delayLastEndTimeEnabled: lastEndTime,
            advanceEarliestEndTimeEnabled: earliestEndTime);
        taskTime.Register(model).IsOk.Should().BeTrue();
        return new Setup(model, tasks, taskTime);
    }

    private static List<(string Name, ConstraintRelation Sign, double Rhs)> Rows(LinearMetaModel<Flt64> model) =>
        ((IMechanismModel<Flt64>)LinearMechanismModel<Flt64>.InvokeAsync(model).GetAwaiter().GetResult().Value)
            .Constraints
            .Where(constraint => constraint.Name.StartsWith("task_delay_last_end_time", StringComparison.Ordinal)
                || constraint.Name.StartsWith("task_advance_earliest_end_time", StringComparison.Ordinal))
            .Select(constraint => (constraint.Name, constraint.Sign, constraint.Rhs.ToDouble()))
            .ToList();

    /// <summary>**开关关着 → 两个方向都约束，且方向是镜像的** / With the switches off both directions are constrained, as a mirrored pair.</summary>
    /// <remarks>
    /// 行是 `estEnd ≤ 最晚结束`（`LessEqual`），右端 14400 秒，名字带任务 id。再看**成对**的提前行是 `GreaterEqual`——
    /// 只断言其中一条会让"两条都写成 `≤`"通过。
    ///
    /// The row is `estEnd ≤ lastEnd` (`LessEqual`) with 14400 on the right and the task id in the name. The **paired** advance row is `GreaterEqual` — asserting only one would let
    /// "both are `≤`" pass.
    /// </remarks>
    [Fact]
    public void Row_ShouldBoundTheEndInBothDirectionsWhenTheSwitchesAreOff() {
        Setup setup = Prepare(lastEndTime: false, earliestEndTime: false,
            ("T1", true, true, Origin.AddHours(4), null),
            ("T2", true, true, null, Origin.AddHours(6)));

        new TaskDelayLastEndTimeConstraint<TestExecutor, TestAssignmentPolicy>(
            Window(), setup.Tasks, setup.TaskTime).Invoke(setup.Model).IsOk.Should().BeTrue();
        new TaskAdvanceEarliestEndTimeConstraint<TestExecutor, TestAssignmentPolicy>(
            Window(), setup.Tasks, setup.TaskTime).Invoke(setup.Model).IsOk.Should().BeTrue();

        List<(string Name, ConstraintRelation Sign, double Rhs)> rows = Rows(setup.Model);
        rows.Should().HaveCount(2);
        rows.Single(row => row.Name == "task_delay_last_end_time_T1").Sign.Should().Be(ConstraintRelation.LessEqual);
        // 归一化后的右端 = 目标时间 − 工期：`estEnd = est + 工期`，把工期这个常量搬到右边就得到 `est ≤ 目标 − 工期`。
        // 断言归一化后的值而不是"目标时间"本身，才是真的在查"行绑的是 estEnd、工期被算进去了"。
        // The normalised right-hand side is "target − duration": `estEnd = est + duration`, and moving that constant across gives `est ≤ target − duration`. Asserting the
        // normalised value rather than the raw target is what pins "the row binds estEnd with the duration included".
        rows.Single(row => row.Name == "task_delay_last_end_time_T1").Rhs.Should().BeApproximately(4 * 3600.0 - 2 * 3600.0, 1e-9);
        rows.Single(row => row.Name == "task_advance_earliest_end_time_T2").Sign.Should().Be(ConstraintRelation.GreaterEqual);
        rows.Single(row => row.Name == "task_advance_earliest_end_time_T2").Rhs.Should().BeApproximately(6 * 3600.0 - 2 * 3600.0, 1e-9);
    }

    /// <summary>**开关开着 → 只保留"不允许移动"的任务** / With the switch on only the immovable tasks are kept.</summary>
    /// <remarks>
    /// 与 的超最大约束同一处反直觉：开关打开反而收紧范围。但与那一批不同，这里**不跳过**任何行——`estEnd` 对不动任务照样存在。
    /// 判据：开关开着时只有 T2（不许延迟）拿到行，T1（允许延迟）没有。
    ///
    /// The same counter-intuitive point as : turning the switch on narrows the set. Unlike that batch, however, **no row is skipped** — `estEnd` exists for an immovable task
    /// too. The criterion: with the switch on only T2 (may not delay) gets a row, T1 (may delay) does not.
    /// </remarks>
    [Fact]
    public void Row_ShouldKeepOnlyTheImmovableTasksWhenTheSwitchIsOn() {
        Setup setup = Prepare(lastEndTime: true, earliestEndTime: false,
            ("T1", true, true, Origin.AddHours(4), null),
            ("T2", false, false, Origin.AddHours(5), null));

        new TaskDelayLastEndTimeConstraint<TestExecutor, TestAssignmentPolicy>(
            Window(), setup.Tasks, setup.TaskTime).Invoke(setup.Model).IsOk.Should().BeTrue();

        List<(string Name, ConstraintRelation Sign, double Rhs)> rows = Rows(setup.Model);
        rows.Should().ContainSingle("T1 may delay so it is dropped, T2 may not so it is kept");
        rows[0].Name.Should().Be("task_delay_last_end_time_T2");
        rows[0].Rhs.Should().BeApproximately(5 * 3600.0 - 2 * 3600.0, 1e-9, "the normalised right-hand side subtracts the 2-hour duration");
    }

    /// <summary>**没有目标结束时间的任务没有行** / A task without a target end gets no row.</summary>
    [Fact]
    public void Row_ShouldSkipATaskWithoutATargetEnd() {
        Setup setup = Prepare(lastEndTime: false, earliestEndTime: false,
            ("T1", true, true, null, null));

        new TaskDelayLastEndTimeConstraint<TestExecutor, TestAssignmentPolicy>(
            Window(), setup.Tasks, setup.TaskTime).Invoke(setup.Model).IsOk.Should().BeTrue();

        Rows(setup.Model).Should().BeEmpty("no target end means nothing to bound");
    }

    /// <summary>**非模型被拒** / A non-model is rejected.</summary>
    [Fact]
    public void Constraint_ShouldRejectANonModel() {
        Setup setup = Prepare(lastEndTime: true, earliestEndTime: true,
            ("T1", true, true, Origin.AddHours(4), Origin.AddHours(4)));

        new TaskDelayLastEndTimeConstraint<TestExecutor, TestAssignmentPolicy>(
            Window(), setup.Tasks, setup.TaskTime).Invoke(new object()).IsOk.Should().BeFalse();
        new TaskAdvanceEarliestEndTimeConstraint<TestExecutor, TestAssignmentPolicy>(
            Window(), setup.Tasks, setup.TaskTime).Invoke(new object()).IsOk.Should().BeFalse();
    }

    /// <summary>**预估结束时间未注册时失败，不静默跳行** / An unregistered estimated end fails instead of skipping.</summary>
    [Fact]
    public void Constraint_ShouldFailWhenTheEstimatedEndIsNotRegistered() {
        TimeWindow<Flt64> window = Window();
        var executors = new List<TestExecutor> { new("E1", "E1") };
        var task = new TestTask("T1", "T1", new TimeRange(Origin, Origin.AddHours(2))) {
            DelayEnabled = true,
            ScheduledTime = new TimeRange(Origin.AddHours(2)),
            LastEndTime = Origin.AddHours(4),
        };
        task.EnabledExecutorSet.Add(executors[0]);
        var tasks = new List<TestTask> { task };
        var model = new LinearMetaModel<Flt64>();
        var compilation = new TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy>(tasks, executors);
        compilation.Register(model).IsOk.Should().BeTrue();
        var taskTime = new TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy>(
            window, tasks, compilation, advanceEnabled: true, delayEnabled: true);

        Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success,
            Fuookami.Ospf.Utils.Error.ErrorCode,
            Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>> result =
            new TaskDelayLastEndTimeConstraint<TestExecutor, TestAssignmentPolicy>(
                window, tasks, taskTime).Invoke(model);

        result.IsOk.Should().BeFalse("a missing estimated end must not quietly drop the row");
    }

#if SCIP_INTEGRATION
    /// <summary>
    /// **真实 SCIP：最晚结束早于任务最早能结束的时间 → 不可行；去掉那条行 → 可行** / Real SCIP: a last end earlier than the earliest possible finish is infeasible; without the
    /// row it is feasible.
    /// </summary>
    /// <remarks>
    /// 任务窗口 02:00 起且**不允许提前**，所以最早结束就是 02:00 之后；把最晚结束写成 00:00，那条 `estEnd ≤ 00:00` 直接矛盾。
    /// "有行不可行、没行可行"是"这条行真的进了模型"最强的证据——比任何数值断言都硬，也不依赖工期是多少。
    ///
    /// 开关**关着**才收得进这个任务（开着时只保留不许移动的任务，那条是另一支）。
    ///
    /// The task window starts at 02:00 with **advance forbidden**, so the earliest finish is after 02:00; a last end of 00:00 makes `estEnd ≤ 00:00` contradictory.
    /// "Infeasible with the row, feasible without" is the strongest evidence that the row reached the model — stronger than any numeric assertion and independent of the actual
    /// duration. The switch is **off** so the task is covered (with it on only immovable tasks are kept, which is the other branch).
    /// </remarks>
    [Fact]
    public async System.Threading.Tasks.Task DelayLastEnd_ShouldMakeATooEarlyTargetInfeasibleUnderRealScip() {
        (await IsFeasible(withRow: true)).Should().BeFalse("the finish cannot be at or before 00:00");
        (await IsFeasible(withRow: false)).Should().BeTrue("without the row the plan is perfectly fine");
    }

    private static async System.Threading.Tasks.Task<bool> IsFeasible(bool withRow) {
        TimeWindow<Flt64> window = Window();
        var executors = new List<TestExecutor> { new("E1", "E1") };
        var task = new TestTask("T1", "T1", new TimeRange(Origin.AddHours(2), Origin.AddHours(4))) {
            DelayEnabled = true,
            AdvanceEnabled = false,
            ScheduledTime = new TimeRange(Origin.AddHours(2), Origin.AddHours(4)),
            LastEndTime = Origin,
        };
        task.EnabledExecutorSet.Add(executors[0]);
        var tasks = new List<TestTask> { task };
        var model = new LinearMetaModel<Flt64>();
        var compilation = new TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy>(tasks, executors);
        compilation.Register(model).IsOk.Should().BeTrue();
        var taskTime = new TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy>(
            window, tasks, compilation, advanceEnabled: false, delayEnabled: true);
        taskTime.Register(model).IsOk.Should().BeTrue();
        if (withRow) {
            new TaskDelayLastEndTimeConstraint<TestExecutor, TestAssignmentPolicy>(
                window, tasks, taskTime).Invoke(model).IsOk.Should().BeTrue();
        }

        Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Core.Solver.Output.FeasibleSolverOutput<Flt64>,
            Fuookami.Ospf.Utils.Error.ErrorCode,
            Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>> result =
            await new Fuookami.Ospf.Core.Plugin.Scip.ScipColumnGenerationSolver()
                .SolveMILPAsync("gantt-end-constraint", model);
        return result.IsOk;
    }
#endif
}
