// 结束时间偏移（拖后 / 提前）与其最小化目标的判据 / Criteria for the end-time deviations and their minimisation objectives.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service.Limits;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Tests.BunchCompilation;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.TaskCompilation;

/// <summary>
/// **"相对结束时间的偏移量"从不存在变成真的单边量（B558）** / The end-time deviations go from absent to real one-sided amounts (B558).
/// </summary>
/// <remarks>
/// 之前只有相对**开始时间**的 `delay_time` / `advance_time`。两者不能互相代替：同一任务可以开始准时、结束却迟早了（工期被拉长或压缩）。
///
/// 三种静默失效：
/// <list type="number">
/// <item>**提前取错了边**：`advance_earliest_end_time` 是 `max(最早结束 − estEnd, 0)`，取正部会把它算成"拖后多少"——
///   两个量单位相同、都在 0 附近，从数值上看不出来；</item>
/// <item>**拿开始时间代替结束时间**：`x` 用了 `est` 而不是 `estEnd`，工期一变答案就错；</item>
/// <item>**漏掉开关或目标时间**：不许移动、或没给目标结束时间的任务应当是常量零，否则每个任务都被计费。</item>
/// </list>
///
/// Only start-relative `delay_time` / `advance_time` existed. The two cannot replace each other: a task can start on time and still finish early or late when its duration changes.
/// Three silent failures: (1) **taking the wrong side for advance** — `advance_earliest_end_time` is `max(earliest end − estEnd, 0)`, and the positive part would compute "how
/// late" instead, in the same unit and near zero, invisible numerically; (2) **using the start instead of the end** — an `est`-based `x` breaks the moment the duration changes;
/// (3) **missing the switch or the target** — tasks that may not move or carry no target end must be constant zero, or every task gets charged.
/// </remarks>
public class EndDeviationTests {
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
            .Select(spec => new TestTask(spec.Id, spec.Id, new TimeRange(Origin.AddHours(2), Origin.AddHours(4))) {
                DelayEnabled = spec.Delay,
                AdvanceEnabled = spec.Advance,
                ScheduledTime = new TimeRange(Origin.AddHours(2), Origin.AddHours(4)),
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

    /// <summary>**两个开关关着 → 两张表都不存在** / With both switches off neither table exists.</summary>
    [Fact]
    public void EndDeviation_ShouldBeAbsentWhenBothSwitchesAreOff() {
        Setup setup = Prepare(lastEndTime: false, earliestEndTime: false,
            ("T1", true, true, Origin.AddHours(4), Origin.AddHours(4)));

        setup.TaskTime.DelayLastEndTime.Should().BeNull();
        setup.TaskTime.AdvanceEarliestEndTime.Should().BeNull();
    }

    /// <summary>**拖后量取正部、提前量取负部** / The late amount takes the positive part, the early amount the negative part.</summary>
    /// <remarks>
    /// 判据查符号名的后缀：拖后是 `delay_last_end_time_slack_pos`，提前是 `advance_earliest_end_time_slack_neg`。
    /// 取错边后缀会换掉一个词，而两个量单位相同、都在 0 附近——只断言"非空"永远发现不了。
    ///
    /// The criterion pins the symbol-name suffix: late ends in `delay_last_end_time_slack_pos`, early in `advance_earliest_end_time_slack_neg`. Taking the wrong side swaps one
    /// word while both amounts share the unit and both sit near zero — asserting "non-empty" would never notice.
    /// </remarks>
    [Fact]
    public void LateSideAndEarlySide_ShouldUseOppositeSlackParts() {
        Setup setup = Prepare(lastEndTime: true, earliestEndTime: true,
            ("T1", true, true, Origin.AddHours(4), Origin.AddHours(4)));

        var late = (ILinearIntermediateSymbol<Flt64>)setup.TaskTime.DelayLastEndTime![setup.Tasks[0]];
        var early = (ILinearIntermediateSymbol<Flt64>)setup.TaskTime.AdvanceEarliestEndTime![setup.Tasks[0]];
        late.Polynomial.Monomials.Should().ContainSingle();
        late.Polynomial.Monomials[0].Symbol.Name.Should().EndWith("_pos");
        early.Polynomial.Monomials.Should().ContainSingle();
        early.Polynomial.Monomials[0].Symbol.Name.Should().EndWith("_neg");
    }

    /// <summary>**没给目标结束时间 → 常量零** / No target end → constant zero.</summary>
    /// <remarks>
    /// "没有目标"不等于"目标是零"：前者永远不会拖后或提前，后者每次都算偏移。写错会让**所有**任务都被计费。
    ///
    /// "No target" is not "target zero": the former never deviates, the latter always does. Getting it wrong charges **every** task.
    /// </remarks>
    [Fact]
    public void EndDeviation_ShouldBeConstantZeroWithoutATarget() {
        Setup setup = Prepare(lastEndTime: true, earliestEndTime: true,
            ("T1", true, true, null, null));

        var late = (ILinearIntermediateSymbol<Flt64>)setup.TaskTime.DelayLastEndTime![setup.Tasks[0]];
        var early = (ILinearIntermediateSymbol<Flt64>)setup.TaskTime.AdvanceEarliestEndTime![setup.Tasks[0]];
        late.Polynomial.Monomials.Should().BeEmpty();
        late.Polynomial.Constant.ToDouble().Should().BeApproximately(0.0, 1e-9);
        early.Polynomial.Monomials.Should().BeEmpty();
        early.Polynomial.Constant.ToDouble().Should().BeApproximately(0.0, 1e-9);
    }

    /// <summary>**不许移动 → 常量零** / A task that may not move gets a constant zero.</summary>
    [Fact]
    public void EndDeviation_ShouldBeConstantZeroForAnImmovableTask() {
        Setup setup = Prepare(lastEndTime: true, earliestEndTime: true,
            ("T1", false, false, Origin.AddHours(4), Origin.AddHours(4)));

        var late = (ILinearIntermediateSymbol<Flt64>)setup.TaskTime.DelayLastEndTime![setup.Tasks[0]];
        late.Polynomial.Monomials.Should().BeEmpty("the task cannot delay, so it is never late");
    }

    /// <summary>**两个目标按系数加权、只收合格任务** / Both objectives weight the sum over eligible tasks only.</summary>
    [Fact]
    public void Objectives_ShouldWeightTheEligibleTasks() {
        Setup late = Prepare(lastEndTime: true, earliestEndTime: false,
            ("T1", true, false, Origin.AddHours(4), null),
            ("T2", true, false, null, null));
        new TaskDelayLastEndTimeMinimization<TestTask, TestExecutor, TestAssignmentPolicy>(
            Window(), late.Tasks, late.TaskTime, coefficient: task => task.Id == "T1" ? new Flt64(5) : Flt64.One)
            .Invoke(late.Model).IsOk.Should().BeTrue();
        late.Model.MetaSubObjects.Should().ContainSingle();
        MetaSubObject<Flt64> lateObjective = late.Model.MetaSubObjects[0];
        lateObjective.Category.Should().Be(ObjectCategory.Minimum);
        lateObjective.Name.Should().Be("task delay last end time");
        lateObjective.Polynomial.Monomials.Should().ContainSingle("T2 has no target end, so it adds a constant zero");
        lateObjective.Polynomial.Monomials[0].Coefficient.ToDouble().Should().BeApproximately(5.0, 1e-9);

        Setup early = Prepare(lastEndTime: false, earliestEndTime: true,
            ("T1", false, true, null, Origin.AddHours(4)));
        new TaskAdvanceEarliestEndTimeMinimization<TestTask, TestExecutor, TestAssignmentPolicy>(
            Window(), early.Tasks, early.TaskTime).Invoke(early.Model).IsOk.Should().BeTrue();
        early.Model.MetaSubObjects.Should().ContainSingle();
        early.Model.MetaSubObjects[0].Name.Should().Be("task advance earliest end time");
        early.Model.MetaSubObjects[0].Category.Should().Be(ObjectCategory.Minimum);
    }

    /// <summary>**开关关着 → 目标不存在；非模型 → 失败** / With the switch off there is no objective; a non-model fails.</summary>
    [Fact]
    public void Objectives_ShouldBeAbsentWhenTheSwitchIsOffAndRejectANonModel() {
        Setup off = Prepare(lastEndTime: false, earliestEndTime: false,
            ("T1", true, true, Origin.AddHours(4), Origin.AddHours(4)));
        new TaskDelayLastEndTimeMinimization<TestTask, TestExecutor, TestAssignmentPolicy>(
            Window(), off.Tasks, off.TaskTime).Invoke(off.Model).IsOk.Should().BeTrue();
        off.Model.MetaSubObjects.Should().BeEmpty("the objective does not exist without the switch");

        Setup on = Prepare(lastEndTime: true, earliestEndTime: true,
            ("T1", true, true, Origin.AddHours(4), Origin.AddHours(4)));
        new TaskDelayLastEndTimeMinimization<TestTask, TestExecutor, TestAssignmentPolicy>(
            Window(), on.Tasks, on.TaskTime).Invoke(new object()).IsOk.Should().BeFalse();
        new TaskAdvanceEarliestEndTimeMinimization<TestTask, TestExecutor, TestAssignmentPolicy>(
            Window(), on.Tasks, on.TaskTime).Invoke(new object()).IsOk.Should().BeFalse();
    }

#if SCIP_INTEGRATION
    /// <summary>
    /// **真实 SCIP：目标点每后移 2 小时，最大拖后量就少 2 小时** / Real SCIP: a target 2 h later leaves 2 h less of maximum lateness.
    /// </summary>
    /// <remarks>
    /// 第一版断言"最大值 > 0"，实测 **79200**——那个数字只说明"很大"，说明不了它是相对谁算的。
    /// 改成**两个目标点相减**：`MaxLate(4 点) − MaxLate(6 点)` 必须恰为 7200 秒。最大化把结束时间推到同一个上界，
    /// 所以差值只能来自"减去目标点"这一步；若减的是别的量（比如开始时间），差值会是 0。再配一条 `MaxLate(null) == 0`。
    ///
    /// The first version asserted "maximum > 0" and measured **79200** — that only says "large", not what it is measured against. The criterion now **subtracts two targets**:
    /// `MaxLate(4 h) − MaxLate(6 h)` must be exactly 7200 s. Maximising pushes the finish to the same upper bound, so the difference can only come from the "minus the target"
    /// step; subtracting something else (the start, say) would give 0. Paired with `MaxLate(null) == 0`.
    /// </remarks>
    [Fact]
    public async System.Threading.Tasks.Task DelayLastEnd_ShouldTrackTheTargetUnderRealScip() {
        double earlier = await MaxLate(Origin.AddHours(4));
        double later = await MaxLate(Origin.AddHours(6));
        double none = await MaxLate(null);

        (earlier - later).Should().BeApproximately(2 * 3600.0, 1e-6,
            "a target 2 hours later leaves exactly 2 hours less of maximum lateness");
        none.Should().BeApproximately(0.0, 1e-6, "with no target the amount is constant zero");
    }

    private static async System.Threading.Tasks.Task<double> MaxLate(DateTimeOffset? lastEnd) {
        Setup setup = Prepare(lastEndTime: true, earliestEndTime: false,
            ("T1", true, false, lastEnd, null));
        new TaskDelayLastEndTimeMinimization<TestTask, TestExecutor, TestAssignmentPolicy>(
            Window(), setup.Tasks, setup.TaskTime, coefficient: _ => new Flt64(-1)).Invoke(setup.Model)
            .IsOk.Should().BeTrue();

        Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Core.Solver.Output.FeasibleSolverOutput<Flt64>,
            Fuookami.Ospf.Utils.Error.ErrorCode,
            Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>> result =
            await new Fuookami.Ospf.Core.Plugin.Scip.ScipColumnGenerationSolver()
                .SolveMILPAsync("gantt-end-deviation", setup.Model);
        result.IsOk.Should().BeTrue("the model must stay feasible: " + result);
        return -result.Value.Obj.ToDouble();
    }
#endif
}
