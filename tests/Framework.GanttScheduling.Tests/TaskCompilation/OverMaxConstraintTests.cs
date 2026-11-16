// 超最大偏移约束的判据 / Criteria for the over-max deviation constraints.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
/// **两个超最大偏移约束从"返回 `Failed`"变成真的行** / The two over-max constraints go from "returns `Failed`" to real rows.
/// </summary>
/// <remarks>
/// 三种静默失效：
/// <list type="number">
/// <item>**行写成 `over_max_*` 而不是偏移量**：两者是"违反量"与"量"的关系，约束后者才有意义；</item>
/// <item>**任务筛选反了**：全局开关开着时保留的是"**不**允许移动"的任务（对它们偏移恒为零，行恒真），关着时才约束所有有上限的任务；</item>
/// <item>**取不到偏移量时静默跳过**：行数变少、解照样可行，看起来一切正常。</item>
/// </list>
///
/// Three silent failures: (1) **the row bounding `over_max_*` instead of the deviation** — those are "violation" vs "amount", and bounding the former is a different statement;
/// (2) **inverting the task filter** — with the global switch on the code keeps the tasks that may **not** move (deviation identically zero, row always true), and only with
/// the switch off does it constrain every task with a limit; (3) **silently skipping when the deviation is unavailable** — fewer rows, still feasible, looks fine.
/// </remarks>
public class OverMaxConstraintTests {
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
        bool overMaxDelay,
        bool overMaxAdvance,
        params (string Id, bool Delay, bool Advance, TimeSpan? MaxDelay, TimeSpan? MaxAdvance)[] specs) {
        TimeWindow<Flt64> window = Window();
        var executors = new List<TestExecutor> { new("E1", "E1") };
        var tasks = specs
            .Select(spec => new TestTask(spec.Id, spec.Id, new TimeRange(Origin, Origin.AddHours(2))) {
                DelayEnabled = spec.Delay,
                AdvanceEnabled = spec.Advance,
                ScheduledTime = new TimeRange(Origin, Origin.AddHours(3)),
                MaxDelay = spec.MaxDelay,
                MaxAdvance = spec.MaxAdvance,
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
            overMaxDelayEnabled: overMaxDelay,
            overMaxAdvanceEnabled: overMaxAdvance);
        taskTime.Register(model).IsOk.Should().BeTrue();
        return new Setup(model, tasks, taskTime);
    }

    private static List<(string Name, Fuookami.Ospf.Core.Model.Basic.ConstraintRelation Sign, double Rhs, int Monomials)> Rows(LinearMetaModel<Flt64> model) =>
        ((IMechanismModel<Flt64>)LinearMechanismModel<Flt64>.InvokeAsync(model).GetAwaiter().GetResult().Value)
            .Constraints
            .Where(constraint => constraint.Name.StartsWith("task_over_max", StringComparison.Ordinal))
            .Select(constraint => (
                constraint.Name,
                constraint.Sign,
                constraint.Rhs.ToDouble(),
                constraint.Lhs.Count))
            .ToList();

    /// <summary>**全局开关关着 → 所有给了上限的任务都被约束** / With the switch off every task with a limit is constrained.</summary>
    /// <remarks>
    /// 行是 `偏移 ≤ 上限`（`LessEqual`），右边是 3600 秒，名字带任务 id。这两点一起才排除"约束的是 `over_max_*`"：
    /// `over_max_*` 的右边也是 3600，但它的左边是一个松弛变量，而这里的左边是偏移量本身——判据用"左边只有一项"分辨不出来，
    /// 所以额外断言**右端等于上限**且**这条行在开关关着时出现**（`over_max_*` 表在开关关着时根本不存在，而约束仍要能加）。
    ///
    /// The row is `deviation ≤ limit` (`LessEqual`) with 3600 on the right and the task id in the name. Together they rule out "bounding `over_max_*`": the latter also has 3600
    /// on the right, but a slack variable on the left — and the discriminator here is that the **row exists while the switch is off** (the `over_max_*` tables do not exist at
    /// all then, yet the constraint must still be addable).
    /// </remarks>
    [Fact]
    public void Row_ShouldBoundTheDeviationByTheMaximumWhenTheSwitchIsOff() {
        Setup setup = Prepare(overMaxDelay: false, overMaxAdvance: false,
            ("T1", true, true, TimeSpan.FromHours(1), null));

        new TaskOverMaxDelayTimeConstraint<TestExecutor, TestAssignmentPolicy>(
            Window(), setup.Tasks, setup.TaskTime).Invoke(setup.Model).IsOk.Should().BeTrue();

        List<(string Name, Fuookami.Ospf.Core.Model.Basic.ConstraintRelation Sign, double Rhs, int Monomials)> rows = Rows(setup.Model);
        rows.Should().ContainSingle();
        rows[0].Name.Should().Be("task_over_max_delay_time_T1");
        rows[0].Sign.Should().Be(ConstraintRelation.LessEqual);
        rows[0].Rhs.Should().BeApproximately(3600.0, 1e-9);
        rows[0].Monomials.Should().Be(1, "the row has one deviation term");
    }

    /// <summary>**全局开关开着 → 行全部变空；关着 → 才真的约束** / With the switch on every row goes vacuous, only with it off does a row bite.</summary>
    /// <remarks>
    /// 这是看起来最反的一处：开关打开反而**收紧**到"不允许移动"的任务，而对它们偏移量恒为零，`0 ≤ 上限` 恒真，于是一行都不生成。
    /// 关掉开关才约束所有有上限的任务。判据把两种情况**成对**跑：只测一种会让"两种行为相同"也通过。
    ///
    /// 顺手"修"成"只保留允许移动的任务"会让开关打开时多出一行、关掉时少一行，而解依旧可行。
    ///
    /// This is the most inverted-looking part of the code: turning the switch **on** narrows to the immovable tasks, whose deviation is identically zero, so `0 ≤ limit` is
    /// always true and **no row is emitted at all**. Only with the switch off does every task with a limit get constrained. The criterion runs both cases **as a pair**: testing
    /// one alone would also pass if the two behaviours were identical. "Fixing" it silently would produce one extra row with the switch on and one fewer with it off, while the
    /// solution stays feasible.
    /// </remarks>
    [Fact]
    public void Row_ShouldGoVacuousWhenTheSwitchIsOnAndBiteWhenItIsOff() {
        (string Id, bool Delay, bool Advance, TimeSpan? MaxDelay, TimeSpan? MaxAdvance)[] specs = {
            ("T1", true, true, TimeSpan.FromHours(1), null),
            ("T2", false, false, TimeSpan.FromHours(2), null),
        };

        Setup enabled = Prepare(overMaxDelay: true, overMaxAdvance: false, specs);
        new TaskOverMaxDelayTimeConstraint<TestExecutor, TestAssignmentPolicy>(
            Window(), enabled.Tasks, enabled.TaskTime).Invoke(enabled.Model).IsOk.Should().BeTrue();
        Rows(enabled.Model).Should().BeEmpty("only immovable tasks are kept, and their rows are tautologies");

        Setup disabled = Prepare(overMaxDelay: false, overMaxAdvance: false, specs);
        new TaskOverMaxDelayTimeConstraint<TestExecutor, TestAssignmentPolicy>(
            Window(), disabled.Tasks, disabled.TaskTime).Invoke(disabled.Model).IsOk.Should().BeTrue();
        List<(string Name, ConstraintRelation Sign, double Rhs, int Monomials)> rows = Rows(disabled.Model);
        rows.Should().ContainSingle("exactly the movable task with a limit is constrained");
        rows[0].Name.Should().Be("task_over_max_delay_time_T1");
        rows[0].Rhs.Should().BeApproximately(3600.0, 1e-9);
    }

    /// <summary>**没有上限的任务没有行** / A task without a limit gets no row.</summary>
    [Fact]
    public void Row_ShouldSkipATaskWithoutALimit() {
        Setup setup = Prepare(overMaxDelay: false, overMaxAdvance: false,
            ("T1", true, true, null, null));

        new TaskOverMaxDelayTimeConstraint<TestExecutor, TestAssignmentPolicy>(
            Window(), setup.Tasks, setup.TaskTime).Invoke(setup.Model).IsOk.Should().BeTrue();

        Rows(setup.Model).Should().BeEmpty("no maximum means no bound to impose");
    }

    /// <summary>**提前方向同样接线** / The advance direction is wired the same way.</summary>
    [Fact]
    public void AdvanceRow_ShouldBoundTheAdvanceByTheMaximum() {
        Setup setup = Prepare(overMaxDelay: false, overMaxAdvance: false,
            ("T1", true, true, null, TimeSpan.FromHours(3)));

        new TaskOverMaxAdvanceTimeConstraint<TestExecutor, TestAssignmentPolicy>(
            Window(), setup.Tasks, setup.TaskTime).Invoke(setup.Model).IsOk.Should().BeTrue();

        List<(string Name, Fuookami.Ospf.Core.Model.Basic.ConstraintRelation Sign, double Rhs, int Monomials)> rows = Rows(setup.Model);
        rows.Should().ContainSingle();
        rows[0].Name.Should().Be("task_over_max_advance_time_T1");
        rows[0].Rhs.Should().BeApproximately(10800.0, 1e-9);
    }

    /// <summary>**非模型被拒** / A non-model is rejected.</summary>
    [Fact]
    public void Constraint_ShouldRejectANonModel() {
        Setup setup = Prepare(overMaxDelay: false, overMaxAdvance: false,
            ("T1", true, true, TimeSpan.FromHours(1), TimeSpan.FromHours(1)));

        new TaskOverMaxDelayTimeConstraint<TestExecutor, TestAssignmentPolicy>(
            Window(), setup.Tasks, setup.TaskTime).Invoke(new object()).IsOk.Should().BeFalse();
        new TaskOverMaxAdvanceTimeConstraint<TestExecutor, TestAssignmentPolicy>(
            Window(), setup.Tasks, setup.TaskTime).Invoke(new object()).IsOk.Should().BeFalse();
    }

    /// <summary>**偏移量表未注册时失败，不静默跳行** / An unregistered deviation map fails instead of skipping.</summary>
    [Fact]
    public void Constraint_ShouldFailWhenTheDeviationIsNotRegistered() {
        TimeWindow<Flt64> window = Window();
        var executors = new List<TestExecutor> { new("E1", "E1") };
        var task = new TestTask("T1", "T1", new TimeRange(Origin, Origin.AddHours(2))) {
            DelayEnabled = true,
            ScheduledTime = new TimeRange(Origin.AddHours(3)),
            MaxDelay = TimeSpan.FromHours(1),
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
            new TaskOverMaxDelayTimeConstraint<TestExecutor, TestAssignmentPolicy>(
                window, tasks, taskTime).Invoke(model);

        result.IsOk.Should().BeFalse("a missing deviation must not quietly drop the row");
        result.Should().BeOfType<Fuookami.Ospf.Utils.Functional.Failed<Fuookami.Ospf.Utils.Functional.Success,
            Fuookami.Ospf.Utils.Error.ErrorCode,
            Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>>();
    }

#if SCIP_INTEGRATION
    /// <summary>
    /// **真实 SCIP：有行时最大延迟被压到上限，没有行时能拖到窗口末尾** / Real SCIP: with the row the maximum delay is capped at the limit, without it it reaches the window end.
    /// </summary>
    /// <remarks>
    /// "有行不可行、没行可行"在这里换成"有行解被压到 3600、没行能到 86400"——同一个模型两次求解，唯一差别是那条行，
    /// 所以 3600 只可能来自这条行。断言 3600 而不是"小于 86400"：后者对"行把延迟压到 7200"也会通过。
    ///
    /// 全局超最大开关**关着**：这样这条行才收得进可移动的任务（见上一条判据）。请求最大值用"系数 −1 再最小化"。
    ///
    /// "Infeasible with the row, feasible without" becomes "capped at 3600 with the row, 86400 without" — the same model solved twice with the row as the only difference, so
    /// 3600 can only come from that row. Asserting 3600 rather than "below 86400": the latter would also pass if the row capped the delay at 7200. The global over-max switch is
    /// **off** so the row covers the movable task (see the criterion above). The maximum is requested via "coefficient −1 and minimise".
    /// </remarks>
    [Fact]
    public async System.Threading.Tasks.Task OverMaxDelay_ShouldCapTheDelayUnderRealScip() {
        (await MaxDelay(withRow: false)).Should().BeApproximately(86400.0, 1e-6, "nothing caps the delay");
        (await MaxDelay(withRow: true)).Should().BeApproximately(3600.0, 1e-6, "the row caps it at the 1-hour limit");
    }

    private static async System.Threading.Tasks.Task<double> MaxDelay(bool withRow) {
        Setup setup = Prepare(overMaxDelay: false, overMaxAdvance: false,
            ("T1", true, false, TimeSpan.FromHours(1), null));
        if (withRow) {
            new TaskOverMaxDelayTimeConstraint<TestExecutor, TestAssignmentPolicy>(
                Window(), setup.Tasks, setup.TaskTime).Invoke(setup.Model).IsOk.Should().BeTrue();
        }
        new TaskDelayTimeMinimization<TestTask, TestExecutor, TestAssignmentPolicy>(
            Window(), setup.Tasks, setup.TaskTime, coefficient: _ => new Flt64(-1)).Invoke(setup.Model)
            .IsOk.Should().BeTrue();

        Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Core.Solver.Output.FeasibleSolverOutput<Flt64>,
            Fuookami.Ospf.Utils.Error.ErrorCode,
            Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>> result =
            await new Fuookami.Ospf.Core.Plugin.Scip.ScipColumnGenerationSolver()
                .SolveMILPAsync("gantt-over-max-constraint", setup.Model);
        result.IsOk.Should().BeTrue("the model must stay feasible: " + result);
        return -result.Value.Obj.ToDouble();
    }
#endif
}
