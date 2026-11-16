// 延迟/提前时间目标的判据 / Criteria for the delay and advance time objectives.

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
/// **延迟/提前两条目标接线** / The delay and advance objectives get wired.
/// </summary>
/// <remarks>
/// 两条目标原来是同一句 `Failed(... 尚未接线...)`。这里把它们接成
/// `Σ coefficient(task) × delayTime/advanceTime[task]`，任务集由
/// <see cref="TimeDeviationSelection"/>（全局开关 + 任务开关 + **有排定时间**）筛出。
///
/// 这条链上有四处会静默错：
/// <list type="number">
/// <item>**方向用反**：延迟目标用 `advanceEnabled` 筛任务，得到的任务集**同样合理**，钱却记在了错误的任务上；</item>
/// <item>**没有排定时间也计费**：偏移量相对什么？那是无定义的量；</item>
/// <item>**系数丢掉**：目标里"有那一项"，权重只是变成 1；</item>
/// <item>**两个方向的映射互换**：延迟实际收的是提前的正部。</item>
/// </list>
///
/// Both objectives used to be the same `Failed(... not wired yet...)` line. wires them to
/// `Σ coefficient(task) × delayTime/advanceTime[task]` over the task set from <see cref="TimeDeviationSelection"/>. Four silent failures: (1) **direction swapped** — filtering the delay
/// objective with `advanceEnabled` gives an **equally plausible** task set with the money charged to the wrong tasks; (2) **billing without a scheduled time** — deviation relative to
/// what? that quantity is undefined; (3) **coefficient dropped** — the objective still "has the term", only weighted 1; (4) **the two maps swapped** — delay actually collects the
/// advance part.
/// </remarks>
public class DelayAdvanceObjectiveTests {
    private static TimeWindow<Flt64> Window() {
        var start = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        return new TimeWindow<Flt64>(
            new TimeRange(start, start.AddHours(24)), true, TimeSpan.FromHours(1));
    }

    private static TestTask TaskAt(
        TimeWindow<Flt64> window,
        string id,
        int startHours,
        bool advance = false,
        bool delay = false,
        bool scheduled = true) =>
        new(id, id) {
            Time = new TimeRange(window.Start.AddHours(startHours), window.Start.AddHours(startHours + 2)),
            ScheduledTime = scheduled
                ? new TimeRange(window.Start.AddHours(startHours), window.Start.AddHours(startHours + 2))
                : null,
            AdvanceEnabled = advance,
            DelayEnabled = delay,
        };

    private static TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy> Time(
        TimeWindow<Flt64> window,
        IReadOnlyList<TestTask> tasks,
        bool advance = false,
        bool delay = false) {
        var compilation = new TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy>(
            tasks, new List<TestExecutor> { new("E1", "E1") });
        return new TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy>(
            window, tasks, compilation, advance, delay);
    }

    /// <summary>**延迟只出现在允许延迟的任务上** / The delay time exists only for tasks that may be delayed.</summary>
    /// <remarks>
    /// 两个任务都在同一个窗口、都允许提前——只有一个允许延迟。`DelayTime` 里因此**只有它一个**，
    /// 而 `AdvanceTime` 里两个都在。只查"映射非空"会让"两个方向混在一起"通过。
    ///
    /// Two tasks in the same window, both allowed to advance, only one allowed to delay. `DelayTime` therefore holds **only that one** while `AdvanceTime` holds both. Checking "the map
    /// is non-empty" alone would let the two directions collapse together through.
    /// </remarks>
    [Fact]
    public void DelayAndAdvanceMaps_ShouldFollowTheirOwnDirection() {
        TimeWindow<Flt64> window = Window();
        var tasks = new List<TestTask> {
            TaskAt(window, "T1", 3, advance: true, delay: true),
            TaskAt(window, "T2", 6, advance: true),
        };
        TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy> taskTime = Time(window, tasks, advance: true, delay: true);
        using var model = new LinearMetaModel<Flt64>();

        taskTime.Register(model).IsOk.Should().BeTrue();

        taskTime.DelayTime.Should().ContainKey(tasks[0]);
        taskTime.DelayTime.Should().NotContainKey(tasks[1], "T2 may not delay");
        taskTime.AdvanceTime.Should().ContainKey(tasks[0]);
        taskTime.AdvanceTime.Should().ContainKey(tasks[1]);
    }

    /// <summary>**延迟目标里带系数，且只对允许延迟的任务** / The delay objective carries the coefficient and only covers tasks that may delay.</summary>
    /// <remarks>
    /// 系数 5 → 目标项必须带上 5。丢掉系数时目标里"还有那一项"，只是权重从 5 变成 1。
    /// 同时 `T2`（不允许延迟）不能出现在目标里——两个条件一起才钉得住"筛对 + 加权对"。
    ///
    /// With a coefficient of 5 the objective term must carry 5; dropping it leaves "the term" weighted 1 instead. And `T2` (may not delay) must not appear — only both conditions
    /// together pin "correct selection **and** correct weight".
    /// </remarks>
    [Fact]
    public void DelayMinimization_ShouldWeightOnlyTheDelayedTasks() {
        TimeWindow<Flt64> window = Window();
        var tasks = new List<TestTask> {
            TaskAt(window, "T1", 3, advance: true, delay: true),
            TaskAt(window, "T2", 6, advance: true),
        };
        TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy> taskTime = Time(window, tasks, advance: true, delay: true);
        using var model = new LinearMetaModel<Flt64>();
        taskTime.Register(model).IsOk.Should().BeTrue();
        int before = model.MetaSubObjects.Count;

        var minimization = new TaskDelayTimeMinimization<TestTask, TestExecutor, TestAssignmentPolicy>(
            window, tasks, taskTime, coefficient: _ => new Flt64(5.0));

        minimization.Invoke(model).IsOk.Should().BeTrue();
        model.MetaSubObjects.Should().HaveCount(before + 1);
        MetaSubObject<Flt64> objective = model.MetaSubObjects.Last();
        objective.Category.Should().Be(ObjectCategory.Minimum);
        objective.Name.Should().Be("task delay time");
        objective.Polynomial.Monomials.Should().ContainSingle("only T1 may delay");
        objective.Polynomial.Monomials[0].Coefficient.ToDouble().Should().BeApproximately(5.0, 1e-9);
        objective.Polynomial.Monomials[0].Symbol.Name.Should().Be("est_slack_T1_pos");
    }

    /// <summary>**提前目标收的是负部，不是正部** / The advance objective collects the negative part, not the positive one.</summary>
    /// <remarks>
    /// 两个方向的映射互换后，目标里出现的符号名会从 `est_slack_T1_neg` 变成 `est_slack_T1_pos`——
    /// 两者都是一个非负变量，任何"目标非空 / 系数对"的判据都看不出区别。**只有符号名能分辨。**
    ///
    /// With the two maps swapped the symbol name in the objective changes from `est_slack_T1_neg` to `est_slack_T1_pos` — both are non-negative variables, so no criterion on
    /// "the objective is non-empty" or "the coefficient is right" can tell. **Only the name can.**
    /// </remarks>
    [Fact]
    public void AdvanceMinimization_ShouldCollectTheNegativePart() {
        TimeWindow<Flt64> window = Window();
        var tasks = new List<TestTask> { TaskAt(window, "T1", 3, advance: true) };
        TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy> taskTime = Time(window, tasks, advance: true);
        using var model = new LinearMetaModel<Flt64>();
        taskTime.Register(model).IsOk.Should().BeTrue();
        int before = model.MetaSubObjects.Count;

        var minimization = new TaskAdvanceTimeMinimization<TestTask, TestExecutor, TestAssignmentPolicy>(
            window, tasks, taskTime);

        minimization.Invoke(model).IsOk.Should().BeTrue();
        model.MetaSubObjects.Should().HaveCount(before + 1);
        MetaSubObject<Flt64> objective = model.MetaSubObjects.Last();
        objective.Category.Should().Be(ObjectCategory.Minimum);
        objective.Name.Should().Be("task advance time");
        objective.Polynomial.Monomials.Should().ContainSingle();
        objective.Polynomial.Monomials[0].Symbol.Name.Should().Be("est_slack_T1_neg");
        objective.Polynomial.Monomials[0].Coefficient.ToDouble().Should().BeApproximately(1.0, 1e-9,
            "the default coefficient is 1");
    }

    /// <summary>**没有排定时间就不计费** / No scheduled time means no charge.</summary>
    /// <remarks>
    /// 两个任务都允许延迟，只有一个有排定时间。目标里因此**只有它一个**——把"没有基准"当成零会让偏移量静默少算。
    ///
    /// Two tasks may both delay, only one has a scheduled time. The objective therefore holds **only that one** — treating "no reference" as zero silently undercounts the deviation.
    /// </remarks>
    [Fact]
    public void DelayMinimization_ShouldSkipTasksWithoutAScheduledTime() {
        TimeWindow<Flt64> window = Window();
        var tasks = new List<TestTask> {
            TaskAt(window, "T1", 3, delay: true),
            TaskAt(window, "T2", 6, delay: true, scheduled: false),
        };
        TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy> taskTime = Time(window, tasks, delay: true);
        using var model = new LinearMetaModel<Flt64>();
        taskTime.Register(model).IsOk.Should().BeTrue();

        var minimization = new TaskDelayTimeMinimization<TestTask, TestExecutor, TestAssignmentPolicy>(
            window, tasks, taskTime);

        minimization.Invoke(model).IsOk.Should().BeTrue();
        MetaSubObject<Flt64> objective = model.MetaSubObjects.Last();
        objective.Polynomial.Monomials.Should().ContainSingle();
        objective.Polynomial.Monomials[0].Symbol.Name.Should().Be("est_slack_T1_pos",
            "T2 has no reference, so its deviation is not a number");
    }

    /// <summary>**全局开关关掉时整条目标不存在，返回 `Ok` 而不加分对象** / With the global switch off the objective does not exist: `Ok` with no sub-objective.</summary>
    /// <remarks>
    /// `delayEnabled` 为假时列表是空的，于是最小化一个空多项式——**这里选择不加目标**。
    /// 区别在于"凭空多出一条恒零目标"会让后续读目标数量的人困惑。
    ///
    /// list is empty when `delayEnabled` is false, so it minimises an empty polynomial — **here no objective is added**. The difference is that a
    /// phantom constant-zero objective confuses whoever counts the objectives later.
    /// </remarks>
    [Fact]
    public void Minimizations_ShouldAddNothingWhenTheGlobalSwitchIsOff() {
        TimeWindow<Flt64> window = Window();
        var tasks = new List<TestTask> { TaskAt(window, "T1", 3, advance: true, delay: true) };
        TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy> taskTime = Time(window, tasks);
        using var model = new LinearMetaModel<Flt64>();
        taskTime.Register(model).IsOk.Should().BeTrue();
        int before = model.MetaSubObjects.Count;

        new TaskDelayTimeMinimization<TestTask, TestExecutor, TestAssignmentPolicy>(window, tasks, taskTime)
            .Invoke(model).IsOk.Should().BeTrue();
        new TaskAdvanceTimeMinimization<TestTask, TestExecutor, TestAssignmentPolicy>(window, tasks, taskTime)
            .Invoke(model).IsOk.Should().BeTrue();

        model.MetaSubObjects.Should().HaveCount(before, "neither direction is modelled, so neither objective exists");
    }

    /// <summary>**系数提取失败必须报错，不能静默用 1** / A failed extraction must fail, not silently use 1.</summary>
    /// <remarks>
    /// `Flt64.infinity` 兜底；`Flt64` 没有无穷。**静默退回 1 会把"计费未知"变成"计费 1"**，
    /// 而目标看上去完全正常。
    ///
 /// The implementation falls back to `Flt64.infinity`; `Flt64` has none (the same limit). **Silently falling back to 1 turns "unknown charge" into "charge 1"** while the objective
    /// looks perfectly normal.
    /// </remarks>
    [Fact]
    public void DelayMinimization_ShouldFailWhenTheCoefficientIsUnavailable() {
        TimeWindow<Flt64> window = Window();
        var tasks = new List<TestTask> { TaskAt(window, "T1", 3, delay: true) };
        TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy> taskTime = Time(window, tasks, delay: true);
        using var model = new LinearMetaModel<Flt64>();
        taskTime.Register(model).IsOk.Should().BeTrue();
        int before = model.MetaSubObjects.Count;

        var minimization = new TaskDelayTimeMinimization<TestTask, TestExecutor, TestAssignmentPolicy>(
            window, tasks, taskTime, coefficient: _ => null);

        minimization.Invoke(model).IsOk.Should().BeFalse("an unknown charge is not a charge of 1");
        model.MetaSubObjects.Should().HaveCount(before, "a failed invocation leaves no half-built objective");
    }

    /// <summary>**提前目标的系数提取失败也必须报错** / A failed coefficient extraction must fail for the advance objective too.</summary>
    /// <remarks>
    /// 两条目标各写一份系数检查——**只测其中一条，另一条的 `?? Flt64.One` 兜底会活下来**，而且看起来完全正常。
    /// 这条判据就是为那条兜底准备的：它把"计费未知"变成"计费 1"，目标里那一项照样存在。
    ///
    /// The two objectives each carry their own coefficient check — **testing only one lets the other's `?? Flt64.One` fallback survive** while looking perfectly normal. This
    /// criterion exists for that fallback: it turns "unknown charge" into "charge 1" and the objective term still exists.
    /// </remarks>
    [Fact]
    public void AdvanceMinimization_ShouldFailWhenTheCoefficientIsUnavailable() {
        TimeWindow<Flt64> window = Window();
        var tasks = new List<TestTask> { TaskAt(window, "T1", 3, advance: true) };
        TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy> taskTime = Time(window, tasks, advance: true);
        using var model = new LinearMetaModel<Flt64>();
        taskTime.Register(model).IsOk.Should().BeTrue();
        int before = model.MetaSubObjects.Count;

        var minimization = new TaskAdvanceTimeMinimization<TestTask, TestExecutor, TestAssignmentPolicy>(
            window, tasks, taskTime, coefficient: _ => null);

        minimization.Invoke(model).IsOk.Should().BeFalse("an unknown charge is not a charge of 1");
        model.MetaSubObjects.Should().HaveCount(before, "a failed invocation leaves no half-built objective");
    }

    /// <summary>**非模型必须失败** / A non-model must fail.</summary>
    [Fact]
    public void Minimizations_ShouldRejectANonModel() {
        TimeWindow<Flt64> window = Window();
        var tasks = new List<TestTask> { TaskAt(window, "T1", 3, advance: true, delay: true) };
        TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy> taskTime = Time(window, tasks, advance: true, delay: true);

        new TaskDelayTimeMinimization<TestTask, TestExecutor, TestAssignmentPolicy>(window, tasks, taskTime)
            .Invoke(new object()).IsOk.Should().BeFalse();
        new TaskAdvanceTimeMinimization<TestTask, TestExecutor, TestAssignmentPolicy>(window, tasks, taskTime)
            .Invoke(new object()).IsOk.Should().BeFalse();
    }

    /// <summary>**取消与延迟同时启用 → 偏移量乘上 `task_compilation` 掩码** / Cancellation plus delay → the deviation is masked by `task_compilation`.</summary>
    /// <remarks>
    /// 被取消掉的任务不该累计偏移量，所以这里写 `delay_time = pos × taskCompilation`：`MaskingFunction` 负责把掩码乘上去。
    ///
    /// 这条判据盯的是"**掩码真的乘上去了**"：符号必须是 `MaskingWithPolyMaskFunction` 的适配器（内部自带一个二值 `m`），
    /// 而不是裸的 `est_slack_*_pos`。只看"注册成功"会让"忘了乘掩码"通过——而忘了乘之后解同样合法，只是被取消的任务也在计费。
    ///
 /// A cancelled task must not accrue deviation, so this implementation writes `delay_time = pos × taskCompilation`. C#'s `MaskingFunction` only became real. The criterion
    /// targets "**the mask is really applied**": the symbol must be a `MaskingWithPolyMaskFunction` adapter (carrying its own binary `m`), not a bare `est_slack_*_pos`. Checking
    /// only "registration succeeded" would let a missing mask through — and without it the solution is still legal, just with cancelled tasks being charged too.
    /// </remarks>
    [Fact]
    public void TaskTime_ShouldMaskTheDeviationWhenCancellationIsEnabled() {
        TimeWindow<Flt64> window = Window();
        var tasks = new List<TestTask> { TaskAt(window, "T1", 3, delay: true) };
        var executors = new List<TestExecutor> { new("E1", "E1") };
        foreach (TestTask task in tasks) {
            task.EnabledExecutorSet.Add(executors[0]);
        }
        var compilation = new TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy>(
            tasks, executors, taskCancelEnabled: true);
        var taskTime = new TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy>(
            window, tasks, compilation, delayEnabled: true);
        using var model = new LinearMetaModel<Flt64>();
        compilation.Register(model).IsOk.Should().BeTrue("the mask column only becomes real after registration");

        taskTime.Register(model).IsOk.Should().BeTrue();

        object symbol = taskTime.DelayTime![tasks[0]];
        symbol.Should().BeOfType<Fuookami.Ospf.Core.Symbol.Function.LinearFunctionSymbolAdapter<Flt64>>();
        Fuookami.Ospf.Core.Symbol.Function.IMathFunctionSymbol<Flt64> function =
            ((Fuookami.Ospf.Core.Symbol.Function.LinearFunctionSymbolAdapter<Flt64>)symbol).Function;
        function.Should().BeOfType<Fuookami.Ospf.Core.Symbol.Function.MaskingWithPolyMaskFunction<Flt64>>(
            "the deviation must be masked, not raw");
        var masking = (Fuookami.Ospf.Core.Symbol.Function.MaskingWithPolyMaskFunction<Flt64>)function;
        masking.MaskPolynomial.Monomials.Should().NotBeEmpty("task_compilation is a sum of columns");
        masking.HelperVariables.Should().HaveCount(2, "the internal binary mask plus the result variable");
    }
}
