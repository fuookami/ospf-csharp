// 四条目标的阈值分支的判据 / Criteria for the four objectives' threshold branches.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service.Limits;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Tests.BunchCompilation;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.TaskCompilation;

/// <summary>
/// **四条目标的阈值分支** / The four objectives' threshold branches.
/// </summary>
/// <remarks>
/// 判据的重点：**阈值非零时才建松弛**、**延迟/提前用正部而准时用截断部**、**三个开关各自钉住**。
///
/// The criteria focus on **the slack being built only for a non-zero threshold**, on **delay/advance using the positive part while on-time uses the capped part**, and on **each of the three switches
/// being pinned separately**.
/// </remarks>
public class ThresholdObjectiveTests {
    private static TimeWindow<Flt64> Window() {
        var start = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        return new TimeWindow<Flt64>(
            new TimeRange(start, start.AddHours(24)), true, TimeSpan.FromHours(1));
    }

    // **`ScheduledTime` 必须给**：`TimeDeviationSelection.Select` 筛的是"有排定时间"的任务，没有它就一个都不选，
    // 阈值分支根本没机会跑到。
    // **`ScheduledTime` is required**: `TimeDeviationSelection.Select` filters on "has a scheduled time", and without one no task is selected at all, so the threshold branch never runs.
    private static TestTask TaskAt(string id, TimeWindow<Flt64> window, int hours, bool advance = false, bool delay = false) =>
        new(id, id, new TimeRange(window.Start, window.Start.AddHours(2))) {
            ScheduledTime = new TimeRange(window.Start, window.Start.AddHours(hours)),
            // **准时/不准时表要有 `LastEndTime` 才建得出来**：那两张表是"最晚结束时间"指标的产物。
            // **The on-time tables need `LastEndTime`**: both are built from the latest-end-time indicator.
            LastEndTime = window.Start.AddHours(hours),
            AdvanceEnabled = advance,
            DelayEnabled = delay,
        };

    // **任务必须挂执行器**：准时表的建立要遍历"任务 × 可用执行器"，没有执行器就一行都不建，
    // 于是准时目标在"表缺项"上失败——那不是这里要测的失败。
    // **Tasks need executors**: the on-time tables are built over "task x eligible executor", so with none no row is built and the objectives fail on a missing table entry, which is not the failure
    // is about.
    private static TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy> Time(
        LinearMetaModel<Flt64> model,
        TimeWindow<Flt64> window,
        IReadOnlyList<TestTask> tasks,
        bool advance = false,
        bool delay = false) {
        var executors = new List<TestExecutor> { new("E1", "E1") };
        foreach (TestTask task in tasks) {
            task.EnabledExecutorSet.Add(executors[0]);
        }
        var compilation = new TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy>(tasks, executors);
        compilation.Register(model).IsOk.Should().BeTrue();
        return new TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy>(
            window, tasks, compilation,
            advanceEnabled: advance,
            delayEnabled: delay,
            // **"最晚结束时间"判定必须开**：准时 / 不准时两张表只有在那条指标存在时才建。
            // **The last-end-time criterion must be on**: the on-time and not-on-time tables exist only once that indicator does.
            delayLastEndTimeEnabled: true);
    }

    /// <summary>
    /// **阈值非零才建松弛；阈值 0 时一个都不建** / A slack is built only for a non-zero threshold; a zero threshold builds none.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 延迟目标阈值 `0`（显式传 `TimeSpan.Zero` 与不传一致）→ 模型里**没有** `delay_time_threshold_*`，目标只有装载项。
    /// 2. 阈值非零 → 每个被计费任务**恰好一条** `delay_time_threshold_<任务>`。
    /// 3. 提前目标同理，符号名是 `advance_time_threshold_*`——**两条目标的松弛名不能串**。
    /// 4. 松弛带的是**正部变量**（`_pos`），不是负部；`withNegative = false` 让负部根本不存在。
    ///
    /// Four paired points: a `0` delay threshold (an explicit `TimeSpan.Zero` agreeing with the default) leaves **no** `delay_time_threshold_*` in the model with the objective holding only the load
    /// term; a non-zero threshold yields **exactly one** `delay_time_threshold_<task>` per charged task; the advance objective works the same way under the name `advance_time_threshold_*`, so **the two
    /// objectives' slack names must not cross**; and the slack carries the **positive** variable (`_pos`), no negative part existing at all with `withNegative = false`.
    /// </remarks>
    [Fact]
    public void DelayAndAdvance_ShouldBuildASlackOnlyWhenTheThresholdIsNonZero() {
        TimeWindow<Flt64> window = Window();
        var tasks = new List<TestTask> { TaskAt("T1", window, 3, advance: true, delay: true) };

        foreach (Func<TestTask, TimeSpan?> extractor in new Func<TestTask, TimeSpan?>[] { _ => null, _ => TimeSpan.Zero }) {
            using var plainModel = new LinearMetaModel<Flt64>();
            // **同一个 `TaskTime` 实例**：注册用 A、目标用 B 的话，B 里没有延迟时间符号，目标必然失败。
            // **One `TaskTime` instance**: registering with one and minimising with another leaves the second without delay-time symbols, so the objective must fail.
            TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy> plainTime =
                Time(plainModel, window, tasks, advance: true, delay: true);
            plainTime.Register(plainModel).IsOk.Should().BeTrue();
            var plain = new TaskDelayTimeMinimization<TestTask, TestExecutor, TestAssignmentPolicy>(
                window, tasks, plainTime, threshold: extractor);
            plain.Invoke(plainModel).IsOk.Should().BeTrue();
            plainModel.Tokens.Symbols.Select(symbol => symbol.Name).Should().NotContain("delay_time_threshold_T1");
        }

        using var model = new LinearMetaModel<Flt64>();
        TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy> taskTime = Time(model, window, tasks, advance: true, delay: true);
        taskTime.Register(model).IsOk.Should().BeTrue();

        var delay = new TaskDelayTimeMinimization<TestTask, TestExecutor, TestAssignmentPolicy>(
            window, tasks, taskTime, threshold: _ => TimeSpan.FromHours(1));
        delay.Invoke(model).IsOk.Should().BeTrue();
        model.Tokens.Symbols.Select(symbol => symbol.Name).Should().Contain("delay_time_threshold_T1");
        model.MetaSubObjects.Last().Polynomial.Monomials.Should().ContainSingle(
            "the objective prices the slack's positive part alone");

        using var advanceModel = new LinearMetaModel<Flt64>();
        TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy> advanceTime = Time(advanceModel, window, tasks, advance: true, delay: true);
        advanceTime.Register(advanceModel).IsOk.Should().BeTrue();
        var advance = new TaskAdvanceTimeMinimization<TestTask, TestExecutor, TestAssignmentPolicy>(
            window, tasks, advanceTime, threshold: _ => TimeSpan.FromHours(1));
        advance.Invoke(advanceModel).IsOk.Should().BeTrue();
        advanceModel.Tokens.Symbols.Select(symbol => symbol.Name).Should().Contain("advance_time_threshold_T1");
        advanceModel.Tokens.Symbols.Select(symbol => symbol.Name).Should().NotContain("delay_time_threshold_T1",
            "the two objectives must not share slack names");
    }

    /// <summary>
    /// **准时最大化用截断部，不是正部** / The on-time maximisation uses the capped part, not the positive part.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 阈值非零时目标里**同时有两类项**：准时数本身（正系数）与松弛正部（**负系数**）。
    ///    `x - pos` 正是这个形状；若误用正部（只有 `pos`），正系数项会整个消失，目标变成"奖励不准时"。
    /// 2. 目标仍是 `Maximum`，名字仍是 `task on time`。
    /// 3. 阈值 `0` 时目标里**只有准时数的项**，没有任何 `task_on_time_threshold` 符号。
    ///
    /// Three paired points: with a non-zero threshold the objective holds **both kinds of term** — the on-time counts themselves with positive coefficients and the slack's positive variable with a
    /// **negative** coefficient, which is exactly `x - pos`; mistakenly using the positive part alone would drop the positive-coefficient terms entirely and turn the objective into "reward being late".
    /// It stays `Maximum` under the name `task on time`. With a `0` threshold the objective holds **only** the on-time terms and no `task_on_time_threshold` symbol at all.
    /// </remarks>
    [Fact]
    public void OnTimeMaximization_ShouldUseTheCappedPart() {
        TimeWindow<Flt64> window = Window();
        // **`delay: true`**：准时 / 不准时指标只对"允许延迟"的任务建（那是"最晚结束时间"指标的一部分）。
        // **`delay: true`**: the on-time indicators are built only for delay-eligible tasks, as part of the latest-end-time indicator.
        var tasks = new List<TestTask> { TaskAt("T1", window, 3, delay: true) };

        using var plainModel = new LinearMetaModel<Flt64>();
        TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy> plainTime = Time(plainModel, window, tasks, delay: true);
        plainTime.Register(plainModel).IsOk.Should().BeTrue();
        var plain = new TaskOnTimeMaximization<TestTask, TestExecutor, TestAssignmentPolicy>(tasks, plainTime);
        plain.Invoke(plainModel).IsOk.Should().BeTrue();
        plainModel.Tokens.Symbols.Select(symbol => symbol.Name).Should().NotContain("task_on_time_threshold");

        using var model = new LinearMetaModel<Flt64>();
        TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy> taskTime = Time(model, window, tasks);
        taskTime.Register(model).IsOk.Should().BeTrue();
        var maximization = new TaskOnTimeMaximization<TestTask, TestExecutor, TestAssignmentPolicy>(
            tasks, taskTime, threshold: 2UL);

        maximization.Invoke(model).IsOk.Should().BeTrue();

        model.Tokens.Symbols.Select(symbol => symbol.Name).Should().Contain("task_on_time_threshold");
        MetaSubObject<Flt64> objective = model.MetaSubObjects.Last();
        objective.Category.Should().Be(ObjectCategory.Maximum);
        objective.Name.Should().Be("task on time");
        objective.Polynomial.Monomials.Should().HaveCountGreaterThan(1,
            "the capped part keeps the on-time terms as well as the negative slack term");
        objective.Polynomial.Monomials.Any(monomial => monomial.Coefficient.ToDouble() < 0.0).Should().BeTrue(
            "the slack's positive variable enters with a negative coefficient, which is what caps the reward");
        objective.Polynomial.Monomials.Any(monomial => monomial.Symbol.Name == "task_on_time_threshold_pos").Should().BeTrue();
    }

    /// <summary>
    /// **不准时最小化用正部** / The not-on-time minimisation uses the positive part.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 阈值非零时目标里**只有**松弛正部一项（正系数），不准时数本身**不再出现**——减的是"超出阈值的部分"。
    /// 2. 目标仍是 `Minimum`，名字仍是 `task not on time`。
    /// 3. 阈值 `0` 时没有 `task_not_on_time_threshold` 符号。
    ///
    /// Three paired points: with a non-zero threshold the objective holds **only** the slack's positive variable with a positive coefficient, the not-on-time counts themselves **no longer
    /// appearing**, since what is penalised is "the part above the threshold"; it stays `Minimum` under the name `task not on time`; and a `0` threshold produces no `task_not_on_time_threshold` symbol.
    /// </remarks>
    [Fact]
    public void NotOnTimeMinimization_ShouldUseThePositivePart() {
        TimeWindow<Flt64> window = Window();
        var tasks = new List<TestTask> { TaskAt("T1", window, 3, delay: true) };

        using var model = new LinearMetaModel<Flt64>();
        TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy> taskTime = Time(model, window, tasks);
        taskTime.Register(model).IsOk.Should().BeTrue();
        var minimization = new TaskNotOnTimeMinimization<TestTask, TestExecutor, TestAssignmentPolicy>(
            tasks, taskTime, threshold: 2UL, coefficient: new Flt64(4.0));

        minimization.Invoke(model).IsOk.Should().BeTrue();

        model.Tokens.Symbols.Select(symbol => symbol.Name).Should().Contain("task_not_on_time_threshold");
        MetaSubObject<Flt64> objective = model.MetaSubObjects.Last();
        objective.Category.Should().Be(ObjectCategory.Minimum);
        objective.Name.Should().Be("task not on time");
        objective.Polynomial.Monomials.Should().ContainSingle();
        objective.Polynomial.Monomials[0].Symbol.Name.Should().Be("task_not_on_time_threshold_pos");
        objective.Polynomial.Monomials[0].Coefficient.ToDouble().Should().BeApproximately(4.0, 1e-6);
    }

    /// <summary>
    /// **三个开关各自钉住** / Each of the three switches is pinned separately.
    /// </summary>
    /// <remarks>
    /// 三点成对：四条目标建出的松弛**都是** `SlackFunction`，且 `Threshold = true`、`WithNegative = false`、`WithPositive = true`。
    /// 三个开关各自错一个，行都照样建得出来，只是含义变了——所以要分别断言，不能只查"有个松弛"。
    ///
    /// Three paired points: every slack the four objectives build is a `SlackFunction` with `Threshold = true`, `WithNegative = false` and `WithPositive = true`. Getting any one switch wrong still
    /// builds the rows, merely with a different meaning, so each must be asserted separately rather than checking that "a slack exists".
    /// </remarks>
    [Fact]
    public void EveryThresholdObjective_ShouldUseTheSameSlackSwitches() {
        TimeWindow<Flt64> window = Window();
        var tasks = new List<TestTask> { TaskAt("T1", window, 3, advance: true, delay: true) };

        using var model = new LinearMetaModel<Flt64>();
        TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy> taskTime = Time(model, window, tasks, advance: true, delay: true);
        taskTime.Register(model).IsOk.Should().BeTrue();

        new TaskDelayTimeMinimization<TestTask, TestExecutor, TestAssignmentPolicy>(
            window, tasks, taskTime, threshold: _ => TimeSpan.FromHours(1)).Invoke(model).IsOk.Should().BeTrue();
        new TaskAdvanceTimeMinimization<TestTask, TestExecutor, TestAssignmentPolicy>(
            window, tasks, taskTime, threshold: _ => TimeSpan.FromHours(1)).Invoke(model).IsOk.Should().BeTrue();
        new TaskNotOnTimeMinimization<TestTask, TestExecutor, TestAssignmentPolicy>(
            tasks, taskTime, threshold: 2UL).Invoke(model).IsOk.Should().BeTrue();
        new TaskOnTimeMaximization<TestTask, TestExecutor, TestAssignmentPolicy>(
            tasks, taskTime, threshold: 2UL).Invoke(model).IsOk.Should().BeTrue();

        var slacks = model.Tokens.Symbols
            .OfType<LinearFunctionSymbolAdapter<Flt64>>()
            .Select(adapter => adapter.Function)
            .OfType<SlackFunction<Flt64>>()
            // **只数这里四条目标的松弛**：`TaskTime` 自己也会建松弛（时间偏差那套），不加名字过滤会多数出来。
            // **Count only the four objectives' slacks**: `TaskTime` builds its own (time-deviation) slacks, so without a name filter the count overshoots.
            .Where(slack => slack.Name.Contains("_threshold"))
            .ToList();
        slacks.Should().HaveCount(4, "one threshold slack per objective");
        slacks.Should().OnlyContain(slack => slack.Threshold);
        slacks.Should().OnlyContain(slack => !slack.WithNegative);
        slacks.Should().OnlyContain(slack => slack.WithPositive);
    }
}
