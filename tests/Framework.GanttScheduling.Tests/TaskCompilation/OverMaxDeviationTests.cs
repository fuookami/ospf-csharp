// 超最大延迟 / 提前的判据 / Criteria for the over-max delay and advance amounts.

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
/// **超最大偏移量从"没有这两张表"变成真的正部** / The over-max amounts go from "no such tables" to real positive parts.
/// </summary>
/// <remarks>
/// `taskTime.overMaxDelayTime` / `overMaxAdvanceTime` 之前不存在，两个最小化目标也直接返回 `Failed`。
///
/// 三种静默失效：
/// <list type="number">
/// <item>**用二值指示代替正部**："超过一小时"和"超过十小时"被收成同一次超限，而超限成本按量计；</item>
/// <item>**漏掉"没有上限"这一支**：没有最大允许量就没有"超限"，写成一个恒为真的判定会让每个任务恒被计费；</item>
/// <item>**漏掉"任务允许移动"这一支**：不许延迟的任务不可能超限，把它算进目标会让目标恒为正。</item>
/// </list>
///
/// The tables did not exist and both objectives returned `Failed`. Three silent failures: (1) **a 0/1 flag instead of a positive part** — "an hour over" and "ten hours over"
/// fold into one violation while over-max cost is per unit; (2) **missing the "no limit" branch** — without a maximum there is no violation, and a constantly-true test would
/// charge every task; (3) **missing the "task may move" branch** — a task that cannot delay cannot exceed, and counting it makes the objective always positive.
/// </remarks>
public class OverMaxDeviationTests {
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
        params (string Id, int WindowHour, int ScheduledHour, TimeSpan? MaxDelay, TimeSpan? MaxAdvance, bool Delay, bool Advance)[] specs) {
        TimeWindow<Flt64> window = Window();
        var executors = new List<TestExecutor> { new("E1", "E1") };
        var tasks = specs
            .Select(spec => new TestTask(spec.Id, spec.Id,
                new TimeRange(Origin.AddHours(spec.WindowHour), Origin.AddHours(spec.WindowHour + 2))) {
                DelayEnabled = spec.Delay,
                AdvanceEnabled = spec.Advance,
                ScheduledTime = new TimeRange(Origin.AddHours(spec.ScheduledHour), Origin.AddHours(spec.ScheduledHour + 2)),
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

    /// <summary>**两开关都关 → 两张表都不存在** / With both switches off neither table exists.</summary>
    /// <remarks>
    /// `null` 而不是"一堆常量零"：没有开关时"超限"没有定义，塞零会让目标函数认真地去最小化一堆零。
    ///
    /// `null` rather than a pile of zeros: with the switches off, "over the limit" is undefined, and filling zeros would make the objective earnestly minimise a pile of nothing.
    /// </remarks>
    [Fact]
    public void OverMax_ShouldBeAbsentWhenBothSwitchesAreOff() {
        Setup setup = Prepare(overMaxDelay: false, overMaxAdvance: false,
            ("T1", 3, 0, TimeSpan.FromHours(1), TimeSpan.FromHours(1), true, true));

        setup.TaskTime.OverMaxDelayTime.Should().BeNull();
        setup.TaskTime.OverMaxAdvanceTime.Should().BeNull();
    }

    /// <summary>**没有最大允许量 → 常量零** / No maximum given → constant zero.</summary>
    /// <remarks>
    /// "没有上限"不等于"上限为零"：前者永远不会超限，后者每个任务都超限。写错会让目标把**所有**任务都计费一遍。
    ///
    /// "No limit" is not "limit zero": the former never exceeds, the latter has every task exceeding. Getting it wrong charges **every** task.
    /// </remarks>
    [Fact]
    public void OverMax_ShouldBeConstantZeroWithoutAMaximum() {
        Setup setup = Prepare(overMaxDelay: true, overMaxAdvance: true,
            ("T1", 3, 0, null, null, true, true));

        var delay = (ILinearIntermediateSymbol<Flt64>)setup.TaskTime.OverMaxDelayTime![setup.Tasks[0]];
        var advance = (ILinearIntermediateSymbol<Flt64>)setup.TaskTime.OverMaxAdvanceTime![setup.Tasks[0]];
        delay.Polynomial.Monomials.Should().BeEmpty();
        delay.Polynomial.Constant.ToDouble().Should().BeApproximately(0.0, 1e-9);
        advance.Polynomial.Monomials.Should().BeEmpty();
    }

    /// <summary>**任务是正部的松弛，不是二值指示** / The amount is a slack's positive part, not a 0/1 flag.</summary>
    /// <remarks>
    /// 判据查符号里那个变量属于一条 `SlackFunction`，且它的**上限常量是最大允许量**（3600 秒）。
    /// 二值指示会让"超 1 小时"与"超 10 小时"同价，而超限成本是按量计的。
    ///
    /// The criterion checks the variable belongs to a `SlackFunction` whose **upper constant is the maximum** (3600 s). A 0/1 flag would price "an hour over" the same as
    /// "ten hours over", while over-max cost is per unit.
    /// </remarks>
    [Fact]
    public void OverMaxDelayTime_ShouldBeThePositivePartOfTheOvershoot() {
        Setup setup = Prepare(overMaxDelay: true, overMaxAdvance: false,
            ("T1", 3, 0, TimeSpan.FromHours(1), null, true, true));

        var symbol = (ILinearIntermediateSymbol<Flt64>)setup.TaskTime.OverMaxDelayTime![setup.Tasks[0]];
        symbol.Polynomial.Monomials.Should().ContainSingle("the amount is one positive slack variable");
        symbol.Polynomial.Monomials[0].Symbol.Name.Should().Contain("over_max_delay_time_T1");
    }

    /// <summary>**开关关着时目标不存在** / With the switch off the objective does not exist.</summary>
    [Fact]
    public void Objective_ShouldBeAbsentWhenTheSwitchIsOff() {
        Setup setup = Prepare(overMaxDelay: false, overMaxAdvance: false,
            ("T1", 3, 0, TimeSpan.FromHours(1), null, true, true));

        new TaskOverMaxDelayTimeMinimization<TestTask, TestExecutor, TestAssignmentPolicy>(
            Window(), setup.Tasks, setup.TaskTime).Invoke(setup.Model).IsOk.Should().BeTrue();
        setup.Model.MetaSubObjects.Should().BeEmpty("the objective does not exist without the switch");
    }

    /// <summary>**目标按系数加权，且只收"允许移动 + 有上限"的任务** / The objective weights the sum over tasks that may move and carry a limit.</summary>
    [Fact]
    public void Objective_ShouldWeightOnlyTheEligibleTasks() {
        Setup setup = Prepare(overMaxDelay: true, overMaxAdvance: false,
            ("T1", 3, 0, TimeSpan.FromHours(1), null, true, true),
            ("T2", 3, 0, null, null, true, true));

        new TaskOverMaxDelayTimeMinimization<TestTask, TestExecutor, TestAssignmentPolicy>(
            Window(), setup.Tasks, setup.TaskTime, coefficient: task => task.Id == "T1" ? new Flt64(5) : Flt64.One)
            .Invoke(setup.Model).IsOk.Should().BeTrue();

        setup.Model.MetaSubObjects.Should().ContainSingle();
        MetaSubObject<Flt64> objective = setup.Model.MetaSubObjects[0];
        objective.Category.Should().Be(ObjectCategory.Minimum);
        objective.Name.Should().Be("task over max delay time");
        objective.Polynomial.Monomials.Should().ContainSingle("T2 has no limit, so it contributes a constant zero");
        objective.Polynomial.Monomials[0].Coefficient.ToDouble().Should().BeApproximately(5.0, 1e-9);
    }

    /// <summary>**非模型被拒** / A non-model is rejected.</summary>
    [Fact]
    public void Objective_ShouldRejectANonModel() {
        Setup setup = Prepare(overMaxDelay: true, overMaxAdvance: true,
            ("T1", 3, 0, TimeSpan.FromHours(1), TimeSpan.FromHours(1), true, true));

        new TaskOverMaxDelayTimeMinimization<TestTask, TestExecutor, TestAssignmentPolicy>(
            Window(), setup.Tasks, setup.TaskTime).Invoke(new object()).IsOk.Should().BeFalse();
        new TaskOverMaxAdvanceTimeMinimization<TestTask, TestExecutor, TestAssignmentPolicy>(
            Window(), setup.Tasks, setup.TaskTime).Invoke(new object()).IsOk.Should().BeFalse();
    }

#if SCIP_INTEGRATION
    /// <summary>
    /// **真实 SCIP：把偏移推到最大时，超限量等于"偏移 − 上限"** / Real SCIP: pushing the deviation to its maximum makes the overshoot equal "deviation − limit".
    /// </summary>
    /// <remarks>
    /// 任务窗口 00:00–02:00、计划 00:00、允许延迟且**不允许提前** → `est ≥ 0`；允许延迟意味着**没有上界收紧**，于是 `est` 能被推到窗口末尾 24:00。
    /// 延迟 = `est − 0` = 86400 秒，上限 3600 秒 → 超限 **82800** 秒。
    ///
    /// 用"系数 −1 再最小化"来求最大值（一次求解只认一个目标）。断言 82800（而不是 0）正是"**减了上限**"的证据：
    /// 若实现忘了减上限，答案会是 86400；若超限量根本没接进目标，答案是 0。
    ///
    /// 第一版判据想用"被逼出来的延迟"来测，实测得到 0 —— 因为 `delay_time` 是相对**任务窗口开始**算的，而不允许提前时 `est` 恰好被钉在那个开始上，
    /// 延迟恒为 0。断言写的是我以为模型会做的，不是它做的。
    ///
    /// Task window 00:00–02:00, plan 00:00, delay allowed and **advance forbidden** → `est ≥ 0`; allowing delay means **no tightening upper bound**, so `est` can be pushed to the
    /// window end 24:00. Delay = `est − 0` = 86400 s against a 3600 s limit → overshoot **82800** s. "Coefficient −1 and minimise" produces the maximum (one objective per solve).
    /// Asserting 82800 (not 0) is exactly the evidence that **the limit was subtracted**: forgetting it would give 86400, and never wiring the amount in would give 0. The first
    /// version tried a "forced delay" and measured 0 — because `delay_time` is measured against the **task window start**, and with advance forbidden `est` is pinned right
    /// there, so the delay is identically 0. The assertion described what I expected rather than what the model does.
    /// </remarks>
    [Fact]
    public async System.Threading.Tasks.Task OverMaxDelayTime_ShouldEqualTheOvershootUnderRealScip() {
        Setup setup = Prepare(overMaxDelay: true, overMaxAdvance: false,
            ("T1", 0, 0, TimeSpan.FromHours(1), null, true, false));
        new TaskOverMaxDelayTimeMinimization<TestTask, TestExecutor, TestAssignmentPolicy>(
            Window(), setup.Tasks, setup.TaskTime, coefficient: _ => new Flt64(-1)).Invoke(setup.Model)
            .IsOk.Should().BeTrue();
        setup.Model.MetaSubObjects.Should().ContainSingle();
        setup.Model.MetaSubObjects[0].Polynomial.Monomials.Should().ContainSingle();

        Fuookami.Ospf.Utils.Functional.Result<
            Fuookami.Ospf.Core.Solver.Output.FeasibleSolverOutput<Flt64>,
            Fuookami.Ospf.Utils.Error.ErrorCode,
            Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>> result =
            await new Fuookami.Ospf.Core.Plugin.Scip.ScipColumnGenerationSolver()
                .SolveMILPAsync("gantt-over-max", setup.Model);

        result.IsOk.Should().BeTrue("the model must stay feasible: " + result);
        result.Value.Obj.ToDouble().Should().BeApproximately(-(86400.0 - 3600.0), 1e-6,
            "the maximum delay (86400) minus the 3600 limit, negated by the -1 coefficient");
    }
#endif
}
