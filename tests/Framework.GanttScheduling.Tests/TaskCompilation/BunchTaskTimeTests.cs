// 任务束调度时间的判据 / Criteria for the bunch scheduling time.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchGeneration.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Tests.BunchCompilation;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.TaskCompilation;

/// <summary>
/// **任务束调度时间的 `Register` 从"静默 `Ok`"变成真的四张表** / The bunch scheduling time's `Register` goes from a silent `Ok` to four real tables.
/// </summary>
/// <remarks>
/// 之前 `Register` 直接返回 `Ok`：模型里什么都没有，而调用方以为"时间已经注册好了"。
///
/// 与 `TaskSchedulingTaskTime` 的**根本差别**：那边每个任务各有独立的 `est` 变量；这边一个束只有**一个位移**（冗余量），
/// 所有成员共用——那正是"束"的意思。把两者混起来会让"束内任务同起同止"这条隐含约束消失，而解依旧可行、目标依旧好看。
///
/// 三种静默失效：
/// <list type="number">
/// <item>**没有冗余范围却仍建变量**：那时冗余量不存在，`est` 应当是常量零；</item>
/// <item>**没给任务时间却建松弛**：没有"相对什么"，松弛应当恒为零；</item>
/// <item>**固定用一个方向建松弛**：延迟/提前开关决定 `withPositive` / `withNegative`，取错边会把"晚到"和"早到"混为一谈。</item>
/// </list>
///
/// Previously `Register` returned `Ok` outright: nothing was in the model while the caller believed the time had been registered. The **fundamental difference** from
/// `TaskSchedulingTaskTime`: there each task owns an independent `est` variable, here a bunch has a **single displacement** (the redundancy amount) shared by all members —
/// which is what "bunch" means. Mixing them dissolves the implicit "members start and end together" constraint while the solution stays feasible and the objective looks fine.
/// Three silent failures: (1) **building variables without a redundancy range** — the amount does not exist then and `est` must be constant zero; (2) **building a slack without a
/// task time** — there is nothing to be relative to, so it must be identically zero; (3) **hard-coding one slack direction** — the delay/advance switches decide
/// `withPositive` / `withNegative`, and the wrong side conflates "late" with "early".
/// </remarks>
public class BunchTaskTimeTests {
    private static readonly DateTimeOffset Origin = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static TimeWindow<Flt64> Window() => new(
        new TimeRange(Origin, Origin.AddHours(24)),
        true,
        TimeSpan.FromHours(1));

    private static (LinearMetaModel<Flt64> Model, List<TestTask> Tasks,
        BunchCompilation<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy> Compilation) Prepare(
        params (string Id, bool WithTime, bool Delay, bool Advance)[] specs) {
        var executors = new List<TestExecutor> { new("E1", "E1") };
        var tasks = specs
            .Select(spec => new TestTask(spec.Id, spec.Id,
                spec.WithTime ? new TimeRange(Origin.AddHours(2), Origin.AddHours(4)) : null) {
                DelayEnabled = spec.Delay,
                AdvanceEnabled = spec.Advance,
            })
            .ToList();
        foreach (TestTask task in tasks) {
            task.EnabledExecutorSet.Add(executors[0]);
        }
        var compilation = new BunchCompilation<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy>(tasks, executors);
        // 编译结果要先注册：任务时间的偏移要给 `task_compilation` 掩码列，那张表由编译结果的 `Register` 填。
        // The compilation must be registered first: the deviations are masked with the `task_compilation` column, a table filled by the compilation's own `Register` (which did
        // not even exist before ).
        var model = new LinearMetaModel<Flt64>();
        compilation.Register(model).IsOk.Should().BeTrue();
        return (model, tasks, compilation);
    }

    /// <summary>**给了冗余范围 → 四张表都有，`est` 就是那个位移** / With a redundancy range all four tables exist and `est` is that displacement.</summary>
    /// <remarks>
    /// `estimate_start_time` 与 `estimate_end_time` 的多项式都指向 `est_redundancy_*`：判据查符号名，而不只是"非空"——
    /// 后者对"est 是一个独立变量"也会通过，而那是被束语义禁止的。
    ///
    /// Both `estimate_start_time` and `estimate_end_time` polynomials point at `est_redundancy_*`: the criterion pins the symbol name, not merely "non-empty" — the latter would
    /// also pass if `est` were an independent variable, which the bunch semantics forbid.
    /// </remarks>
    [Fact]
    public void Register_ShouldBuildTheFourTablesWhenRedundancyIsGiven() {
        (LinearMetaModel<Flt64> model, List<TestTask> tasks, BunchCompilation<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy> compilation) =
            Prepare(("T1", true, true, true), ("T2", true, true, true));
        var taskTime = new BunchSchedulingTaskTime<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy>(
            Window(), tasks, compilation, TimeSpan.FromHours(1));

        taskTime.Register(model).IsOk.Should().BeTrue();

        taskTime.EstRedundancy.Should().NotBeNull();
        taskTime.EstRedundancy!.Keys.Should().BeEquivalentTo(tasks);
        taskTime.EstimateStartTime.Should().NotBeNull();
        taskTime.EstimateEndTime.Should().NotBeNull();
        taskTime.EstimateSlack.Should().NotBeNull();

        var start = (ILinearIntermediateSymbol<Flt64>)taskTime.EstimateStartTime![tasks[0]];
        var end = (ILinearIntermediateSymbol<Flt64>)taskTime.EstimateEndTime![tasks[0]];
        start.Polynomial.Monomials.Should().ContainSingle();
        start.Polynomial.Monomials[0].Symbol.Name.Should().Be("est_redundancy_T1");
        end.Polynomial.Monomials[0].Symbol.Name.Should().Be("est_redundancy_T1", "start and end share the bunch displacement");

        // 冗余量的范围是 **±冗余值**（1 小时 = 3600 秒）：上面写着"这是个有界位移"，只断言"非空"会让无界位移通过。
        // The redundancy amount is bounded by **± the redundancy value** (1 h = 3600 s): the comment above says "a bounded displacement", and asserting only "non-empty"
        // would pass for an unbounded one.
        var redundancyVariable = (Fuookami.Ospf.Core.Variable.IndependentVariableItem<Flt64,
            Fuookami.Ospf.Core.Variable.Continuous>)start.Polynomial.Monomials[0].Symbol;
        redundancyVariable.Range.LowerBound!.Value.Unwrap().ToDouble().Should().BeApproximately(-3600.0, 1e-9);
        redundancyVariable.Range.UpperBound!.Value.Unwrap().ToDouble().Should().BeApproximately(3600.0, 1e-9);
    }

    /// <summary>**没有冗余范围 → 没有冗余量，`est` 是常量零** / No redundancy range → no amount, `est` is constant zero.</summary>
    /// <remarks>
    /// 判据把两种情况**成对**跑（有冗余 / 无冗余）：只测一种会让"永远建"或"永远不建"通过。
    ///
    /// The criterion runs both cases **as a pair** (with and without redundancy): testing one alone passes for "always build" or "never build".
    /// </remarks>
    [Fact]
    public void Register_ShouldUseConstantZeroWithoutRedundancy() {
        (LinearMetaModel<Flt64> model, List<TestTask> tasks, BunchCompilation<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy> compilation) =
            Prepare(("T1", true, true, true));
        var taskTime = new BunchSchedulingTaskTime<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy>(
            Window(), tasks, compilation);

        taskTime.Register(model).IsOk.Should().BeTrue();

        taskTime.EstRedundancy.Should().BeNull();
        var start = (ILinearIntermediateSymbol<Flt64>)taskTime.EstimateStartTime![tasks[0]];
        start.Polynomial.Monomials.Should().BeEmpty("without redundancy there is no displacement");
        start.Polynomial.Constant.ToDouble().Should().BeApproximately(0.0, 1e-9);
    }

    /// <summary>**没有任务时间 → 松弛恒为零** / No task time → the slack is identically zero.</summary>
    [Fact]
    public void EstSlack_ShouldBeConstantZeroWithoutATaskTime() {
        (LinearMetaModel<Flt64> model, List<TestTask> tasks, BunchCompilation<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy> compilation) =
            Prepare(("T1", false, true, true));
        var taskTime = new BunchSchedulingTaskTime<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy>(
            Window(), tasks, compilation, TimeSpan.FromHours(1));

        taskTime.Register(model).IsOk.Should().BeTrue();

        var slack = (ILinearIntermediateSymbol<Flt64>)taskTime.EstimateSlack![tasks[0]];
        slack.Polynomial.Monomials.Should().BeEmpty("there is nothing to be relative to");
    }

    /// <summary>**松弛方向由任务的延迟/提前开关决定** / The slack directions follow the task's delay/advance switches.</summary>
    /// <remarks>
    /// 一个只有延迟、不能提前的任务 → `SlackFunction` 只有正部；反过来只有负部。判据查 `PosVar` / `NegVar` 是否存在，
    /// 只查"松弛非空"会让"总是两边都开"通过——那等于允许往任何方向偏。
    ///
    /// A task that may only delay → the `SlackFunction` has just the positive part; the reverse gives just the negative part. The criterion pins whether `PosVar` / `NegVar`
    /// exist; checking only "slack is non-empty" would pass for "both always on", which allows deviating either way.
    /// </remarks>
    [Fact]
    public void EstSlack_ShouldFollowTheTaskSwitches() {
        (LinearMetaModel<Flt64> model, List<TestTask> tasks, BunchCompilation<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy> compilation) =
            Prepare(("T1", true, true, false), ("T2", true, false, true));
        var taskTime = new BunchSchedulingTaskTime<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy>(
            Window(), tasks, compilation, TimeSpan.FromHours(1));

        taskTime.Register(model).IsOk.Should().BeTrue();

        var delayOnly = (SlackFunction<Flt64>)((LinearFunctionSymbolAdapter<Flt64>)taskTime.EstimateSlack![tasks[0]]).Function;
        delayOnly.PosVar.Should().NotBeNull("the task may delay");
        delayOnly.NegVar.Should().BeNull("the task may not advance");

        var advanceOnly = (SlackFunction<Flt64>)((LinearFunctionSymbolAdapter<Flt64>)taskTime.EstimateSlack![tasks[1]]).Function;
        advanceOnly.PosVar.Should().BeNull("the task may not delay");
        advanceOnly.NegVar.Should().NotBeNull("the task may advance");
    }

    /// <summary>**非模型被拒** / A non-model is rejected.</summary>
    [Fact]
    public void Register_ShouldRejectANonModel() {
        (LinearMetaModel<Flt64> _, List<TestTask> tasks, BunchCompilation<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy> compilation) =
            Prepare(("T1", true, true, true));
        var taskTime = new BunchSchedulingTaskTime<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy>(
            Window(), tasks, compilation, TimeSpan.FromHours(1));

        taskTime.Register(new object()).IsOk.Should().BeFalse();
    }

    /// <summary>
    /// **迭代列仍然明确失败，不静默成功** / The iteration columns still fail loudly instead of silently succeeding.
    /// </summary>
    /// <remarks>
    /// `addColumns` 要把 `valueOf(time.start) * xi[bunch]` 累加到 `est` / `estEnd`，而 `xi` 是编译层的"每轮列变量"——
    /// 本仓的 `BunchCompilation` 没有那一层。之前这里返回 `Ok`，而唯一调用方又把返回值丢掉：于是"列没加上"与"加上了"在代码里完全一样。
    /// 现在明确失败，且调用方检查返回值。
    ///
    /// The code's `addColumns` accumulates `valueOf(time.start) * xi[bunch]` into `est` / `estEnd`, and `xi` is the compilation layer's per-round column variable — which
    /// `BunchCompilation` lacks. This used to return `Ok` while the only caller discarded the result, so "columns not added" and "added" were identical in code. It now fails
    /// loudly and the caller checks the result.
    /// </remarks>
    [Fact]
    public void AddColumns_ShouldFailLoudlyUntilTheColumnLayerExists() {
        (LinearMetaModel<Flt64> model, List<TestTask> tasks, BunchCompilation<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy> compilation) =
            Prepare(("T1", true, true, true));
        var taskTime = new BunchSchedulingTaskTime<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy>(
            Window(), tasks, compilation, TimeSpan.FromHours(1));
        taskTime.Register(model).IsOk.Should().BeTrue();

        var bunch = new TestTaskBunch(
            compilation.Executors[0],
            new ExecutorInitialUsability<TestTask, TestExecutor, TestAssignmentPolicy>(null, Origin),
            tasks);
        Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success,
            Fuookami.Ospf.Utils.Error.ErrorCode,
            Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>> result =
            taskTime.AddColumns(7, new List<TestTaskBunch> { bunch }, model);

        result.IsOk.Should().BeFalse("the column layer does not exist yet, so adding columns must fail");

    }

    /// <summary>
    /// **离散时间窗上的任务束时间可以注册** / Bunch time registers on a discrete window too.
    /// </summary>
    /// <remarks>
    /// 离散窗口的守卫属于"建开始时间变量"那一支——`UIntVariable1`，本仓还没有整数开始时间变量。
    /// 但任务束的 `est` 是**连续位移**（`est_redundancy`），与整数开始时刻无关；Demo4 的束时间窗恰恰是离散的。
    /// 把守卫放在共用循环里会把这条本来能注册的路径挡住，而且报的是"开始时间变量没接线"，指向完全无关的地方。
    ///
    /// The discrete-window guard belongs to the "build start-time variables" branch — the code uses `UIntVariable1`, and this repository has no integer start-time variable yet.
    /// A bunch's `est` is a **continuous displacement** (`est_redundancy`) unrelated to integer start instants, and Demo4's bunch window is discrete. Putting the guard in the
    /// shared loop blocks a path that does register, and reports "the start-time variable is not wired", pointing somewhere entirely unrelated.
    /// </remarks>
    [Fact]
    public void Register_ShouldWorkOnADiscreteWindow() {
        (LinearMetaModel<Flt64> _, List<TestTask> tasks, BunchCompilation<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy> compilation) =
            Prepare(("T1", true, true, true));
        var discrete = new TimeWindow<Flt64>(
            new TimeRange(Origin, Origin.AddHours(24)),
            false,
            TimeSpan.FromHours(1));
        var model = new LinearMetaModel<Flt64>();
        compilation.Register(model).IsOk.Should().BeTrue();
        var taskTime = new BunchSchedulingTaskTime<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy>(
            discrete, tasks, compilation, TimeSpan.FromHours(1));

        taskTime.Register(model).IsOk.Should().BeTrue("a bunch displacement is continuous even on a discrete window");

        // 反过来：普通任务调度时间在离散窗口上仍然明确失败——它要的是整数开始时刻。
        // The other way round: plain task scheduling time still fails loudly on a discrete window — it wants integer start instants.
        var plain = new Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model.TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy>(
            discrete, tasks, compilation, advanceEnabled: true, delayEnabled: true);
        plain.Register(new LinearMetaModel<Flt64>()).IsOk.Should().BeFalse();
    }

    /// <summary>
    /// **任务束时间现在有全部八张表** / The bunch time now carries all eight tables.
    /// </summary>
    /// <remarks>
    /// 之前这个类自己只建四张（`est` / `estEnd` / `est_slack` / 冗余量），偏移、超最大偏移、准时、结束时间偏移都不存在——
    /// 也就是说"任务束的延迟量""束的完工偏差"这些**能被目标读到**的量根本没有符号，目标只会算出零。
    /// 这与明确失败不同：目标值算出来是 0，看起来像"没有偏差"，而不是"没建"。
    ///
    /// Previously this class built only four (est, estEnd, est_slack, redundancy); deviation, over-max, on-time and end-time deviation did not exist — so the quantities an
    /// objective reads ("a bunch's delay", "a bunch's finish deviation") had no symbol at all and the objective computed zero. That differs from an outright failure: the objective
    /// comes out 0, which reads as "no deviation" rather than "not built".
    /// </remarks>
    [Fact]
    public void Register_ShouldExposeTheEightSharedTables() {
        (LinearMetaModel<Flt64> model, List<TestTask> tasks, BunchCompilation<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy> compilation) =
            Prepare(("T1", true, true, true));
        var taskTime = new BunchSchedulingTaskTime<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy>(
            Window(), tasks, compilation, TimeSpan.FromHours(1));

        taskTime.Register(model).IsOk.Should().BeTrue();

        taskTime.EstimateStartTime.Should().NotBeNull();
        taskTime.EstimateEndTime.Should().NotBeNull();
        taskTime.EstimateSlack.Should().NotBeNull();
        taskTime.DelayTime.Should().NotBeNull("delay is enabled for a bunch");
        taskTime.AdvanceTime.Should().NotBeNull("advance is enabled for a bunch");
        taskTime.OverMaxDelayTime.Should().NotBeNull();
        taskTime.OverMaxAdvanceTime.Should().NotBeNull();
        taskTime.DelayLastEndTime.Should().NotBeNull();
        taskTime.AdvanceEarliestEndTime.Should().NotBeNull();
        taskTime.OnTime.Should().NotBeNull();
    }

    /// <summary>
    /// **束的 `estEnd` 就是 `est`，不加工期** / A bunch's `estEnd` is its `est`, with no duration added.
    /// </summary>
    /// <remarks>
    /// 任务调度时间那支加，这一支不加（基线如此：束内成员的结束时间就是共用的开始时刻）。判据用的任务**自带工期**（时间窗 2 小时），再比两个多项式——
    /// 若这里也加工期，差值会正好是一个工期，而"束的完工时间"会在屏幕上多出一段。
    ///
    /// Task scheduling time adds the duration; this branch does not (its shape: members of a bunch finish at the shared start instant). The criterion gives the task a
    /// **duration** and then compares the two polynomials — if this branch added it too, the difference would be exactly one duration and every bunch's finish would grow on screen.
    /// </remarks>
    [Fact]
    public void Register_ShouldNotAddTheDurationToTheBunchEndTime() {
        (LinearMetaModel<Flt64> model, List<TestTask> tasks, BunchCompilation<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy> compilation) =
            Prepare(("T1", true, true, true));
        var taskTime = new BunchSchedulingTaskTime<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy>(
            Window(), tasks, compilation, TimeSpan.FromHours(1));

        taskTime.Register(model).IsOk.Should().BeTrue();

        var start = (ILinearIntermediateSymbol<Flt64>)taskTime.EstimateStartTime![tasks[0]];
        var end = (ILinearIntermediateSymbol<Flt64>)taskTime.EstimateEndTime![tasks[0]];
        end.Polynomial.Constant.ToDouble().Should().BeApproximately(start.Polynomial.Constant.ToDouble(), 1e-9,
            "a bunch ends when its shared displacement starts");
        end.Polynomial.Monomials.Select(monomial => monomial.Symbol).Should()
            .BeEquivalentTo(start.Polynomial.Monomials.Select(monomial => monomial.Symbol));
    }

    /// <summary>
    /// **没有时间窗的任务仍然拿到 `est` / `estEnd`** / A task without a time window still gets `est` / `estEnd`.
    /// </summary>
    /// <remarks>
    /// 这是这里修掉的一个缺陷：`est` / `estEnd` 来自钩子，与时间窗无关，所以"没有时间窗"只该跳过**偏离计划**那一族，不该连时间符号一起跳过。
    /// 反过来（连 `est` 也跳过）会让读取方拿到 `null`，而 `null` 在调用方那里通常被当成"没有这个任务"。
    ///
    /// This is a defect fixed in: `est` / `estEnd` come from the hook and do not depend on the window, so "no time window" should skip only the **deviation from plan**
    /// family, not the time symbols themselves. The other way round (skipping `est` too) hands readers `null`, and callers usually read `null` as "no such task".
    /// </remarks>
    [Fact]
    public void Register_ShouldStillBuildTheTimeSymbolsWithoutATaskWindow() {
        (LinearMetaModel<Flt64> model, List<TestTask> tasks, BunchCompilation<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy> compilation) =
            Prepare(("T1", false, true, true));

        var taskTime = new BunchSchedulingTaskTime<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy>(
            Window(), tasks, compilation, TimeSpan.FromHours(1));

        taskTime.Register(model).IsOk.Should().BeTrue();

        taskTime.EstimateStartTime.Should().ContainKey(tasks[0]);
        taskTime.EstimateEndTime.Should().ContainKey(tasks[0]);
        var slack = (ILinearIntermediateSymbol<Flt64>)taskTime.EstimateSlack![tasks[0]];
        slack.Polynomial.Monomials.Should().BeEmpty("there is nothing to be relative to");
        taskTime.DelayTime.Should().BeEmpty("no planned start means no deviation to measure");
    }
}
