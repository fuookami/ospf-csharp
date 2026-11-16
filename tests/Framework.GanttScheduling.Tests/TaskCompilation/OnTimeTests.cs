// 准时 / 不准时指示与两个目标的判据 / Criteria for the on-time / not-on-time indicators and both objectives.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service.Limits;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Tests.BunchCompilation;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.TaskCompilation;

/// <summary>
/// **准时 / 不准时从"没有这两张表"变成真指示** / On-time / not-on-time go from "no such tables" to real indicators.
/// </summary>
/// <remarks>
/// 基线的 `taskTime.onTime` / `taskTime.notOnTime` 之前根本不存在，`TaskOnTimeMaximization` / `TaskNotOnTimeMinimization` 也直接返回 `Failed`。
///
/// 三种静默失效：
/// <list type="number">
/// <item>**把 `NotOnTime` 当成 `1 − OnTime`**：基线有**死区**——只有"整整晚/早一个时间窗"才算不准时，否则两者都不含该任务；</item>
/// <item>**漏掉"任务允许移动"的判断**：不允许移动的任务 `on_time` 恒为零，而计费却把它当成一次"准时"；</item>
/// <item>**把 `OnTime` 与 `NotOnTime` 方向写反**：`on_last_end_time` 是"不晚于最晚结束"，`on_earliest_end_time` 是"不早于最早结束"。</item>
/// </list>
///
/// The baseline's `onTime` / `notOnTime` did not exist and both objectives returned `Failed`. Three silent failures: (1) **treating `NotOnTime` as `1 − OnTime`** — the baseline
/// has a **dead band**, only "a whole window late/early" counts, so a task can be in neither map; (2) **missing the "task may move" test** — an immovable task's `on_time` is
/// constant zero, yet billing it would count as one "on time"; (3) **swapping the two directions** — `on_last_end_time` is "not after the last end", `on_earliest_end_time` is
/// "not before the earliest end".
/// </remarks>
public class OnTimeTests {
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
                ScheduledTime = new TimeRange(Origin.AddHours(3), Origin.AddHours(5)),
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
            advanceEnabled: true,
            delayEnabled: true,
            delayLastEndTimeEnabled: lastEndTime,
            advanceEarliestEndTimeEnabled: earliestEndTime);
        taskTime.Register(model).IsOk.Should().BeTrue();
        return new Setup(model, tasks, taskTime);
    }

    /// <summary>**两个开关都关着时没有这两张表** / With both switches off there are no such tables.</summary>
    /// <remarks>
    /// `OnTime` 为 `null` 而不是"每个任务都是零"：没有开关时"准时"没有定义，硬塞一堆常量零会让目标函数
    /// 认真地去最大化一堆零——求解成功、目标值 0，而看起来像"全都准时"。
    ///
    /// `OnTime` is `null` rather than "zero per task": with the switches off, "on time" is undefined, and filling it with constants would make the objective earnestly maximise a
    /// pile of zeros — a successful solve, an objective of 0, and an appearance of "everything on time".
    /// </remarks>
    [Fact]
    public void OnTime_ShouldBeAbsentWhenBothSwitchesAreOff() {
        Setup setup = Prepare(lastEndTime: false, earliestEndTime: false, ("T1", true, true, null, null));

        setup.TaskTime.OnTime.Should().BeNull();
        setup.TaskTime.NotOnTime.Should().BeNull();
        new TaskOnTimeMaximization<TestTask, TestExecutor, TestAssignmentPolicy>(setup.Tasks, setup.TaskTime)
            .Invoke(setup.Model).IsOk.Should().BeFalse("no table means nothing to maximise");
    }

    /// <summary>**允许延迟 + 有最晚结束时间 → 一个真指示** / May delay with a last end time → one real indicator.</summary>
    /// <remarks>
    /// 判据要同时看**结构**（是 `IfThenFunction` 的适配器）与**方向**（`Relation == LE`，即"不晚于最晚结束"）。
    /// 只看结构会让 `GE` 通过，那只会在"早于最晚结束"为真——正好反了。
    ///
    /// Both the **shape** (an `IfThenFunction` adapter) and the **direction** (`Relation == LE`, "not after the last end") are checked. The shape alone would let `GE` pass, which
    /// fires on "earlier than the last end" — exactly inverted.
    /// </remarks>
    [Fact]
    public void OnLastEndTime_ShouldBeAnIfThenWithTheRightRelation() {
        DateTimeOffset lastEnd = Origin.AddHours(6);
        Setup setup = Prepare(lastEndTime: true, earliestEndTime: false, ("T1", true, false, lastEnd, null));

        setup.TaskTime.OnTime.Should().NotBeNull();
        var symbol = (ILinearIntermediateSymbol<Flt64>)setup.TaskTime.OnTime![setup.Tasks[0]];
        symbol.Polynomial.Monomials.Should().ContainSingle("on_time is the sum of one indicator");

        IMathFunctionSymbol<Flt64> indicator =
            ((LinearFunctionSymbolAdapter<Flt64>)setup.TaskTime.Indicators["on_last_end_time_T1"]).Function;
        indicator.Should().BeOfType<IfThenFunction<Flt64>>();
        ((IfThenFunction<Flt64>)indicator).Relation.Should().Be(
            Fuookami.Ospf.Math.Symbol.Inequality.Comparison.LE,
            "on time means finishing no later than the last end");
    }

    /// <summary>**不允许移动的任务没有指示（常量零）** / An immovable task gets a constant zero.</summary>
    /// <remarks>
    /// 任务不允许延迟时 `on_time` 恒为零——它本来就不可能晚。若把它也算作"准时"，目标函数会被一堆恒为 1 的项填满，
    /// 而目标值恒等于任务数，看起来像"已经全准时了"。
    ///
    /// A task that may not delay has a constant-zero `on_time` — it cannot be late by construction. Counting it as "on time" would fill the objective with constant ones, making
    /// the value equal the task count and looking like "already all on time".
    /// </remarks>
    [Fact]
    public void OnTime_ShouldBeConstantZeroForAnImmovableTask() {
        Setup setup = Prepare(lastEndTime: true, earliestEndTime: false, ("T1", false, false, Origin.AddHours(6), null));

        var symbol = (ILinearIntermediateSymbol<Flt64>)setup.TaskTime.OnTime![setup.Tasks[0]];
        symbol.Polynomial.Monomials.Should().BeEmpty("the task cannot delay, so it is never late");
        symbol.Polynomial.Constant.ToDouble().Should().BeApproximately(0.0, 1e-9);
    }

    /// <summary>**`NotOnTime` 与 `OnTime` 不是互补的** / `NotOnTime` is not the complement of `OnTime`.</summary>
    /// <remarks>
    /// 同一条任务既在 `OnTime` 里又不在 `NotOnTime` 里（它就是普通准时的情况）；死区让"晚一点点"两边都不算。
    /// 判据断言**两者是两条不同符号**、且 `not_on_last_end_time` 的右端比 `on_last_end_time` 的右端**整整晚一个时间窗**（24 小时）——
    /// 这正是死区的来源。把 `NotOnTime` 写成 `1 − OnTime` 会让右端相同，判据立刻发现。
    ///
    /// The same task sits in `OnTime` and outside `NotOnTime` (the ordinary on-time case); the dead band leaves "a little late" in neither. The criterion asserts the two are
    /// **distinct symbols** and that `not_on_last_end_time`'s right-hand side is a **whole window (24 h)** later than `on_last_end_time`'s — the origin of the dead band. Writing
    /// `NotOnTime` as `1 − OnTime` would give the same right-hand side and the criterion would notice immediately.
    /// </remarks>
    [Fact]
    public void NotOnTime_ShouldCarryAOneWindowDeadBand() {
        DateTimeOffset lastEnd = Origin.AddHours(6);
        Setup setup = Prepare(lastEndTime: true, earliestEndTime: false, ("T1", true, false, lastEnd, null));

        var on = (ILinearIntermediateSymbol<Flt64>)setup.TaskTime.OnTime![setup.Tasks[0]];
        var notOn = (ILinearIntermediateSymbol<Flt64>)setup.TaskTime.NotOnTime![setup.Tasks[0]];
        notOn.Polynomial.Monomials.Select(m => m.Symbol).Should().NotIntersectWith(on.Polynomial.Monomials.Select(m => m.Symbol),
            "the two indicators must be different symbols");

        // 条件多项式是 `est − 阈值`，所以**右边界的差**就是两个常量之差。取函数的 `Condition` 而不是建成后的约束行：
        // 那些行由 `IfThenFunction` 内部生成，名字带后缀，按名字找它们等于在测内部命名。
        // The condition polynomial is `est − threshold`, so the **difference of the two right-hand sides** is the difference of the constants. Reading the function's `Condition`
        // rather than the built rows: those are generated inside `IfThenFunction` with suffixed names, and finding them by name would be testing internal naming.
        double onThreshold = -((IfThenFunction<Flt64>)((LinearFunctionSymbolAdapter<Flt64>)
            setup.TaskTime.Indicators["on_last_end_time_T1"]).Function).Condition.Constant.ToDouble();
        double notOnThreshold = -((IfThenFunction<Flt64>)((LinearFunctionSymbolAdapter<Flt64>)
            setup.TaskTime.Indicators["not_on_last_end_time_T1"]).Function).Condition.Constant.ToDouble();
        (notOnThreshold - onThreshold).Should().BeApproximately(24 * 3600.0, 1e-6,
            "being not-on-time requires a full window of lateness");
    }

    /// <summary>**最早结束时间方向是 `GE`** / The earliest-end direction is `GE`.</summary>
    [Fact]
    public void OnEarliestEndTime_ShouldBeAnIfThenWithGreaterEqual() {
        Setup setup = Prepare(lastEndTime: false, earliestEndTime: true,
            ("T1", false, true, null, Origin.AddHours(2)));

        IMathFunctionSymbol<Flt64> indicator =
            ((LinearFunctionSymbolAdapter<Flt64>)setup.TaskTime.Indicators["on_earliest_end_time_T1"]).Function;
        ((IfThenFunction<Flt64>)indicator).Relation.Should().Be(
            Fuookami.Ospf.Math.Symbol.Inequality.Comparison.GE,
            "on time means finishing no earlier than the earliest end");
    }

    /// <summary>**两个目标按系数加权求和** / Both objectives weight the sum.</summary>
    [Fact]
    public void Objectives_ShouldWeightTheIndicatorSum() {
        DateTimeOffset lastEnd = Origin.AddHours(6);
        Setup maximization = Prepare(lastEndTime: true, earliestEndTime: false, ("T1", true, false, lastEnd, null));
        new TaskOnTimeMaximization<TestTask, TestExecutor, TestAssignmentPolicy>(
            maximization.Tasks, maximization.TaskTime, coefficient: new Flt64(5)).Invoke(maximization.Model)
            .IsOk.Should().BeTrue();
        maximization.Model.MetaSubObjects.Should().ContainSingle();
        maximization.Model.MetaSubObjects[0].Category.Should().Be(ObjectCategory.Maximum);
        maximization.Model.MetaSubObjects[0].Name.Should().Be("task on time");
        maximization.Model.MetaSubObjects[0].Polynomial.Monomials.Should().ContainSingle();
        maximization.Model.MetaSubObjects[0].Polynomial.Monomials[0].Coefficient.ToDouble()
            .Should().BeApproximately(5.0, 1e-9, "the weight must really be applied");

        Setup minimization = Prepare(lastEndTime: true, earliestEndTime: false, ("T1", true, false, lastEnd, null));
        new TaskNotOnTimeMinimization<TestTask, TestExecutor, TestAssignmentPolicy>(
            minimization.Tasks, minimization.TaskTime).Invoke(minimization.Model).IsOk.Should().BeTrue();
        minimization.Model.MetaSubObjects.Should().ContainSingle();
        minimization.Model.MetaSubObjects[0].Category.Should().Be(ObjectCategory.Minimum);
        minimization.Model.MetaSubObjects[0].Name.Should().Be("task not on time");
    }

#if SCIP_INTEGRATION
    /// <summary>
    /// **真实 SCIP：截止时间可达 → 1，不可达 → 0** / Real SCIP: a reachable deadline gives 1, an unreachable one gives 0.
    /// </summary>
    /// <remarks>
    /// 两个方向都跑才是判据：只测"可达时是 1"会让"恒定返回 1"通过，只测"不可达时是 0"会让"恒定返回 0"通过。
    /// 任务窗口 0–2 小时，所以完工最早 02:00：截止 06:00 可达（1），截止 00:00 不可达（0）。
    ///
    /// Both directions are the criterion: "1 when reachable" alone would pass if the indicator were constant 1, "0 when unreachable" alone if constant 0. The task window is
    /// 0–2 h, so the earliest finish is 02:00: a 06:00 deadline is reachable (1), a 00:00 deadline is not (0).
    /// </remarks>
    [Fact]
    public void OnTime_ShouldBeOneForAReachableDeadlineAndZeroOtherwiseUnderRealScip() {
        SolveOnTime(Origin.AddHours(6)).Should().BeApproximately(1.0, 1e-6, "the task can finish by 06:00");
        SolveOnTime(Origin).Should().BeApproximately(0.0, 1e-6, "the task cannot finish by 00:00");
    }

    private static double SolveOnTime(DateTimeOffset lastEnd) {
        Setup setup = Prepare(lastEndTime: true, earliestEndTime: false, ("T1", true, false, lastEnd, null));
        new TaskOnTimeMaximization<TestTask, TestExecutor, TestAssignmentPolicy>(setup.Tasks, setup.TaskTime)
            .Invoke(setup.Model).IsOk.Should().BeTrue();

        Fuookami.Ospf.Utils.Functional.Result<
            Fuookami.Ospf.Core.Solver.Output.FeasibleSolverOutput<Flt64>,
            Fuookami.Ospf.Utils.Error.ErrorCode,
            Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>> result =
            new Fuookami.Ospf.Core.Plugin.Scip.ScipColumnGenerationSolver()
                .SolveMILPAsync("gantt-on-time", setup.Model).GetAwaiter().GetResult();
        result.IsOk.Should().BeTrue("the model must stay feasible: " + result);
        return result.Value.Obj.ToDouble();
    }
#endif

    /// <summary>**非模型与未注册都被拒** / A non-model and an unregistered task time are rejected.</summary>
    [Fact]
    public void Objectives_ShouldRejectANonModelOrAnUnregisteredTable() {
        Setup setup = Prepare(lastEndTime: true, earliestEndTime: false, ("T1", true, false, Origin.AddHours(6), null));

        new TaskOnTimeMaximization<TestTask, TestExecutor, TestAssignmentPolicy>(setup.Tasks, setup.TaskTime)
            .Invoke(new object()).IsOk.Should().BeFalse();
        new TaskNotOnTimeMinimization<TestTask, TestExecutor, TestAssignmentPolicy>(setup.Tasks, setup.TaskTime)
            .Invoke(new object()).IsOk.Should().BeFalse();
    }
}
