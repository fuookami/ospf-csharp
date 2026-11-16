// 换型指示、换型时间与两个目标的判据 / Criteria for the switch indicators, switch times and both objectives.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service.Limits;
using Fuookami.Ospf.Framework.GanttScheduling.Tests.BunchCompilation;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.TaskCompilation;

/// <summary>
/// **换型从空壳变成真表** / The switch goes from an empty stub to real tables.
/// </summary>
/// <remarks>
/// `TaskSchedulingSwitch.Register` 原来直接返回 `Ok`——两张表都是 `null`，而 `SwitchCostMinimization` / `SwitchTimeMinimization`
/// 也是空壳。于是"换型成本"这条目标从来不存在，"注册成功"的绿灯亮在什么都没做上。
///
/// 这里的三种静默失效：
/// <list type="number">
/// <item>**跳过对角线**：`switch[e, t, t]` 若在数据里非零（含准备时间），跳过就少罚了一部分；</item>
/// <item>**"紧跟"判据漏掉中间任务**：`t1` 与 `t3` 之间还排着 `t2` 时仍然算换型，换型次数被高估；</item>
/// <item>**换型时间没乘掩码**：间隔直接当时间，于是"没有换型"也在计费。</item>
/// </list>
///
/// `TaskSchedulingSwitch.Register` used to return `Ok` outright — both tables were `null` and both objectives were stubs. The "switch cost" objective therefore never existed,
/// and the green light shone on doing nothing. Three silent failures targets: (1) **skipping the diagonal** — a non-zero `switch[e, t, t]` (setup time) then goes
/// under-penalised; (2) **a "immediately precedes" test that misses a middle task** — `t1` and `t3` still count as a switch while `t2` sits between them, over-estimating the
/// switch count; (3) **switch time without the mask** — the gap is charged even when nothing switches.
/// </remarks>
public class SwitchTableTests {
    private static readonly DateTimeOffset Origin = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static TimeWindow<Flt64> Window() => new(
        new TimeRange(Origin, Origin.AddHours(24)),
        true,
        TimeSpan.FromHours(1));

    private static LinearMetaModel<Flt64> Build(
        out TaskSchedulingSwitch<TestTask, TestExecutor, TestAssignmentPolicy> @switch,
        out List<TestTask> tasks,
        out List<TestExecutor> executors,
        params (string Id, int StartHour, int EndHour)[] specs) {
        TimeWindow<Flt64> window = Window();
        executors = new List<TestExecutor> { new("E1", "E1") };
        tasks = specs
            .Select(spec => new TestTask(
                spec.Id,
                spec.Id,
                new TimeRange(Origin.AddHours(spec.StartHour), Origin.AddHours(spec.EndHour))))
            .ToList();
        foreach (TestTask task in tasks) {
            task.EnabledExecutorSet.Add(executors[0]);
        }
        var compilation = new TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy>(tasks, executors);
        var model = new LinearMetaModel<Flt64>();
        compilation.Register(model).IsOk.Should().BeTrue();
        @switch = new TaskSchedulingSwitch<TestTask, TestExecutor, TestAssignmentPolicy>(window, tasks, executors, compilation);
        return model;
    }

    /// <summary>**对角线是常量零，且参与求和** / The diagonal is constant zero and takes part in the sum.</summary>
    /// <remarks>
    /// 基线在 `switch` 里对对角线返回 `empty`，但 `SwitchCostTriples` **不跳过**它。若判据只看"表里有没有这一项"，
    /// 跳过对角线和填零都通过；所以我断言**项在表里**而且**多项式为空**——两件事一起才算对。
    ///
    /// The baseline returns `empty` for the diagonal in `switch` while `SwitchCostTriples` **does not** skip it. Checking only "is the entry present" passes both skipping and
    /// zero-filling, so the assertion demands **the entry is present** *and* **its polynomial is empty**.
    /// </remarks>
    [Fact]
    public void SwitchTable_ShouldCarryTheDiagonalAsConstantZero() {
        using LinearMetaModel<Flt64> model = Build(out TaskSchedulingSwitch<TestTask, TestExecutor, TestAssignmentPolicy> @switch,
            out List<TestTask> tasks, out List<TestExecutor> executors, ("T1", 0, 2), ("T2", 3, 5));

        @switch.Register(model).IsOk.Should().BeTrue();

        @switch.SwitchTable.Should().NotBeNull();
        @switch.SwitchTable!.Should().HaveCount(4, "executors × tasks × tasks, diagonal included");
        var diagonal = (ILinearIntermediateSymbol<Flt64>)@switch.SwitchTable![(executors[0], tasks[0], tasks[0])];
        diagonal.Polynomial.Monomials.Should().BeEmpty("switching a task to itself is constant zero");
        diagonal.Polynomial.Constant.ToDouble().Should().BeApproximately(0.0, 1e-9);
    }

    /// <summary>**只有计划上紧邻的一对才有指示** / Only a pair adjacent in the plan gets an indicator.</summary>
    /// <remarks>
    /// 三个任务排在 0 点、1 点、2 点。`T1 → T3` 之间夹着 `T2`，不该算换型；`T1 → T2` 该算。
    /// 漏掉"中间任务"检查时两者都是 `AND`，换型次数从 1 变 2——而解仍然可行，只是成本翻倍。
    ///
    /// Three tasks at hours 0, 1 and 2. `T1 → T3` has `T2` in between and must not count; `T1 → T2` must. Without the middle-task check both become `AND`, the switch count
    /// goes from 1 to 2 — and the solution stays feasible, only the cost doubles.
    /// </remarks>
    [Fact]
    public void SwitchTable_ShouldOnlyIndicateImmediatelyAdjacentPairs() {
        using LinearMetaModel<Flt64> model = Build(out TaskSchedulingSwitch<TestTask, TestExecutor, TestAssignmentPolicy> @switch,
            out List<TestTask> tasks, out List<TestExecutor> executors, ("T1", 0, 2), ("T2", 1, 4), ("T3", 2, 6));

        @switch.Register(model).IsOk.Should().BeTrue();

        object adjacent = @switch.SwitchTable![(executors[0], tasks[0], tasks[1])];
        object skipped = @switch.SwitchTable![(executors[0], tasks[0], tasks[2])];
        ((ILinearIntermediateSymbol<Flt64>)adjacent).Polynomial.Monomials.Should().NotBeEmpty(
            "T1 immediately precedes T2, so the indicator is a real AND");
        ((ILinearIntermediateSymbol<Flt64>)skipped).Polynomial.Monomials.Should().BeEmpty(
            "T2 starts between T1 and T3, so T1 does not immediately precede T3");
    }

    /// <summary>**换型时间是"间隔 × 指示"的掩码函数** / A switch time is a masked gap.</summary>
    /// <remarks>
    /// 只查"表里有项"会让"直接返回间隔"通过，而那样每个任务对都在计费换型时间。判据因此要求符号是
    /// `MaskingWithPolyMaskFunction`，且**掩码多项式非空**（多台机器的指示之和）。
    ///
    /// Checking only "the entry exists" lets "return the raw gap" pass, which charges switch time for every pair. The criterion demands a `MaskingWithPolyMaskFunction` whose
    /// **mask polynomial is non-empty** (the sum of the per-executor indicators).
    /// </remarks>
    [Fact]
    public void SwitchTimeTable_ShouldMaskTheGapWithTheIndicators() {
        using LinearMetaModel<Flt64> model = Build(out TaskSchedulingSwitch<TestTask, TestExecutor, TestAssignmentPolicy> @switch,
            out List<TestTask> tasks, out _, ("T1", 0, 2), ("T2", 3, 5));

        @switch.Register(model).IsOk.Should().BeTrue();

        object symbol = @switch.SwitchTimeTable![(tasks[0], tasks[1])];
        symbol.Should().BeOfType<LinearFunctionSymbolAdapter<Flt64>>();
        IMathFunctionSymbol<Flt64> function = ((LinearFunctionSymbolAdapter<Flt64>)symbol).Function;
        function.Should().BeOfType<MaskingWithPolyMaskFunction<Flt64>>("the gap must be masked, not raw");
        var masking = (MaskingWithPolyMaskFunction<Flt64>)function;
        masking.MaskPolynomial.Monomials.Should().NotBeEmpty("the mask is the sum of the executor indicators");
        masking.Input.Monomials.Should().BeEmpty("the input is the constant gap, not a variable");
        masking.Input.Constant.ToDouble().Should().BeApproximately(3600.0, 1e-9,
            "T1 ends at 02:00 and T2 starts at 03:00");
    }

    /// <summary>
    /// **带任务时间时顺序由 `est` 决定，两个方向都可能有换型** / With a task time the order comes from `est`, so both directions may switch.
    /// </summary>
    /// <remarks>
    /// 无任务时间时只有"计划上紧跟"的那一对有指示，反方向是常量零。有任务时间时**两个方向都必须是真的 `AND`**：
    /// 允许提前/延迟之后，`T2` 完全可能排到 `T1` 前面。判据同时检查两个方向，只查一个会让"照抄计划顺序"通过。
    ///
    /// Without a task time only the plan-adjacent pair carries an indicator and the reverse is constant zero. With a task time **both directions must be real `AND`s**: once
    /// advance/delay is allowed, `T2` may well run before `T1`. Both directions are checked; one alone would let "just copy the plan order" pass.
    /// </remarks>
    [Fact]
    public void SwitchTable_ShouldUseTheEstimatedOrderWhenTaskTimeIsGiven() {
        (LinearMetaModel<Flt64> model, TaskSchedulingSwitch<TestTask, TestExecutor, TestAssignmentPolicy> @switch, List<TestTask> tasks, List<TestExecutor> executors) =
            RegisteredWithTaskTime(("T1", 0, 2), ("T2", 3, 5));

        @switch.Register(model).IsOk.Should().BeTrue();

        ((ILinearIntermediateSymbol<Flt64>)@switch.SwitchTable![(executors[0], tasks[0], tasks[1])])
            .Polynomial.Monomials.Should().NotBeEmpty("T1 may precede T2");
        ((ILinearIntermediateSymbol<Flt64>)@switch.SwitchTable![(executors[0], tasks[1], tasks[0])])
            .Polynomial.Monomials.Should().NotBeEmpty("T2 may also precede T1 once advance/delay is on");
        // 对角线仍是常量零：`frontOf` 要求"不晚于"，自己与自己的那一对由 `ReferenceEquals` 直接排除。
        // The diagonal is still constant zero: `frontOf` needs "not later", and the self pair is excluded by `ReferenceEquals`.
        ((ILinearIntermediateSymbol<Flt64>)@switch.SwitchTable![(executors[0], tasks[0], tasks[0])])
            .Polynomial.Monomials.Should().BeEmpty();
    }

    /// <summary>
    /// **前后序判定是"不晚于"，不是"早于"** / The ordering test is "not later", not "earlier".
    /// </summary>
    /// <remarks>
    /// 相等时两边都该是 1。写成"严格早于"会让两个同时开始的任务**两个方向都是 0**，换型指示整片消失——而模型依旧可行、
    /// 目标依旧是 0，读起来是"这台机器没有换型"。
    ///
    /// On a tie both sides must be 1. Writing it as "strictly earlier" makes two tasks starting together **zero in both directions**, the indicators vanish wholesale — while the
    /// model stays feasible and the objective stays 0, reading as "this machine never switches".
    /// </remarks>
    [Fact]
    public void FrontOf_ShouldUseNotLaterNotEarlier() {
        (LinearMetaModel<Flt64> model, TaskSchedulingSwitch<TestTask, TestExecutor, TestAssignmentPolicy> @switch, List<TestTask> tasks, _) =
            RegisteredWithTaskTime(("T1", 0, 2), ("T2", 3, 5));

        @switch.Register(model).IsOk.Should().BeTrue();

        @switch.FrontOfTable.Should().NotBeNull();
        IMathFunctionSymbol<Flt64> function =
            ((LinearFunctionSymbolAdapter<Flt64>)@switch.FrontOfTable![(tasks[0], tasks[1])]).Function;
        function.Should().BeOfType<IfFunction<Flt64>>("the ordering test is a decision variable, not a static comparison");
        ((IfFunction<Flt64>)function).Relation.Should().Be(
            Fuookami.Ospf.Math.Symbol.Inequality.Comparison.LE,
            "a tie must count as not-later in both directions");
    }

    /// <summary>
    /// **每个多出来的任务都给换型加一条 `not_between`** / Every extra task adds one `not_between` conjunct.
    /// </summary>
    /// <remarks>
    /// 换型指示的 `AND` 条件数 = 2 条分配 + 1 条 `frontOf` + (任务数 − 2) 条 `not_between`。
    /// 少乘这一条会让"夹在中间的那一对"也算换型：3 个任务同机时换型次数从 2 变 3。
    /// 判据给出两个规模（2 个任务 → 3 条，3 个任务 → 4 条），只测一个规模会让"永远 3 条"通过。
    ///
    /// The `AND` carries 2 assignment columns + 1 `frontOf` + (tasks − 2) `not_between` conjuncts. Dropping them counts a middle pair as a switch too: three tasks on one
    /// machine would show 3 switches instead of 2. The criterion pins two sizes (2 tasks → 3 conditions, 3 tasks → 4), since one size alone lets "always 3" pass.
    /// </remarks>
    [Fact]
    public void Switch_ShouldAddOneNotBetweenConjunctPerMiddleTask() {
        (LinearMetaModel<Flt64> pairModel, TaskSchedulingSwitch<TestTask, TestExecutor, TestAssignmentPolicy> pairSwitch, List<TestTask> pairTasks, List<TestExecutor> pairExecutors) =
            RegisteredWithTaskTime(("T1", 0, 2), ("T2", 3, 5));
        pairSwitch.Register(pairModel).IsOk.Should().BeTrue();
        ConditionCount(pairSwitch, pairExecutors[0], pairTasks[0], pairTasks[1])
            .Should().Be(3, "two tasks leave no middle task to exclude");

        (LinearMetaModel<Flt64> tripleModel, TaskSchedulingSwitch<TestTask, TestExecutor, TestAssignmentPolicy> tripleSwitch, List<TestTask> tripleTasks, List<TestExecutor> tripleExecutors) =
            RegisteredWithTaskTime(("T1", 0, 2), ("T2", 1, 4), ("T3", 2, 6));
        tripleSwitch.Register(tripleModel).IsOk.Should().BeTrue();
        ConditionCount(tripleSwitch, tripleExecutors[0], tripleTasks[0], tripleTasks[2])
            .Should().Be(4, "T2 sits between T1 and T3, so one not_between conjunct is added");
    }

    private static int ConditionCount(
        TaskSchedulingSwitch<TestTask, TestExecutor, TestAssignmentPolicy> @switch,
        TestExecutor executor,
        TestTask from,
        TestTask to) {
        IMathFunctionSymbol<Flt64> function =
            ((LinearFunctionSymbolAdapter<Flt64>)@switch.SwitchTable![(executor, from, to)]).Function;
        function.Should().BeOfType<AndFunction>("an indicator is an AND of its conditions");
        return ((AndFunction)function).Polynomials.Count;
    }

    /// <summary>
    /// **任务时间未注册时换型失败**（不是抛异常） / An unregistered task time fails the switch instead of throwing.
    /// </summary>
    /// <remarks>
    /// 顺序判定要读 `est`，而 `est` 只在任务时间**注册之后**才存在。原来是运行时的 `InvalidOperationException`，
    /// 调用方看不出是"还没注册"还是别的什么。
    ///
    /// The ordering test reads `est`, which only exists **after the task time is registered**. It used to be a runtime `InvalidOperationException`, leaving the caller unable to tell
    /// "not registered yet" from anything else.
    /// </remarks>
    [Fact]
    public void Register_ShouldFailWhenTheTaskTimeIsNotRegistered() {
        TimeWindow<Flt64> window = Window();
        var executors = new List<TestExecutor> { new("E1", "E1") };
        var tasks = new List<TestTask> {
            new("T1", "T1", new TimeRange(Origin, Origin.AddHours(2))),
        };
        tasks[0].EnabledExecutorSet.Add(executors[0]);
        var compilation = new TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy>(tasks, executors);
        var taskTime = new TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy>(window, tasks, compilation);
        using var model = new LinearMetaModel<Flt64>();
        compilation.Register(model).IsOk.Should().BeTrue();
        var @switch = new TaskSchedulingSwitch<TestTask, TestExecutor, TestAssignmentPolicy>(window, tasks, executors, compilation, taskTime);

        @switch.Register(model).IsOk.Should().BeFalse("the estimated start/end tables do not exist yet");
        @switch.SwitchTable.Should().BeNull("a failed registration leaves no partial table");
    }

    /// <summary>**非模型被拒** / A non-model is rejected.</summary>
    [Fact]
    public void Register_ShouldRejectANonModel() {
        using LinearMetaModel<Flt64> ignored = Build(out TaskSchedulingSwitch<TestTask, TestExecutor, TestAssignmentPolicy> @switch,
            out _, out _, ("T1", 0, 2));

        @switch.Register(new object()).IsOk.Should().BeFalse();
    }


    private static (LinearMetaModel<Flt64> Model, TaskSchedulingSwitch<TestTask, TestExecutor, TestAssignmentPolicy> Switch, List<TestTask> Tasks, List<TestExecutor> Executors) RegisteredWithTaskTime(
        params (string Id, int StartHour, int EndHour)[] specs) {
        TimeWindow<Flt64> window = Window();
        var executors = new List<TestExecutor> { new("E1", "E1") };
        var tasks = specs
            .Select(spec => new TestTask(
                spec.Id,
                spec.Id,
                new TimeRange(Origin.AddHours(spec.StartHour), Origin.AddHours(spec.EndHour))))
            .ToList();
        foreach (TestTask task in tasks) {
            task.EnabledExecutorSet.Add(executors[0]);
            // 全局开关只是"允许允许"，**每个任务自己的开关才是真的闸门**：任务级不开时 `est` 被钉在计划开始上，
            // 换型时间就退化成常量间隔，判据测不到"读了 est"。
            // The global switch only permits; **the per-task switch is the real gate** — with it off `est` is pinned to the planned start and the switch time collapses to the
            // constant gap, so the criterion could not observe "reads est".
            task.AdvanceEnabled = true;
            task.DelayEnabled = true;
        }
        var model = new LinearMetaModel<Flt64>();
        var compilation = new TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy>(tasks, executors);
        compilation.Register(model).IsOk.Should().BeTrue();
        var taskTime = new TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy>(
            window, tasks, compilation, advanceEnabled: true, delayEnabled: true);
        taskTime.Register(model).IsOk.Should().BeTrue();
        return (model,
            new TaskSchedulingSwitch<TestTask, TestExecutor, TestAssignmentPolicy>(window, tasks, executors, compilation, taskTime),
            tasks,
            executors);
    }

    // ===== 目标 =====

    private static (LinearMetaModel<Flt64> Model, TaskSchedulingSwitch<TestTask, TestExecutor, TestAssignmentPolicy> Switch, List<TestTask> Tasks, List<TestExecutor> Executors) Registered(
        params (string Id, int StartHour, int EndHour)[] specs) {
        LinearMetaModel<Flt64> model = Build(out TaskSchedulingSwitch<TestTask, TestExecutor, TestAssignmentPolicy> @switch,
            out List<TestTask> tasks, out List<TestExecutor> executors, specs);
        @switch.Register(model).IsOk.Should().BeTrue();
        return (model, @switch, tasks, executors);
    }

    /// <summary>**换型成本目标按三元组加权求和** / The switch-cost objective sums the triples with weights.</summary>
    /// <remarks>
    /// 判据要求**权重真的乘上去了**：给 `E1/T1/T2` 系数 5，目标里对应单项的系数必须是 5；
    /// 只查"目标非空"会让"忘了乘系数"通过。
    ///
    /// The criterion demands **the weight is really applied**: a coefficient of 5 on `E1/T1/T2` must show as a coefficient of 5 on the matching term. Checking only "the objective
    /// is non-empty" would let a missing weight through.
    /// </remarks>
    [Fact]
    public void SwitchCostMinimization_ShouldWeightTheIndicators() {
        (LinearMetaModel<Flt64> model, TaskSchedulingSwitch<TestTask, TestExecutor, TestAssignmentPolicy> @switch, List<TestTask> tasks, List<TestExecutor> executors) =
            Registered(("T1", 0, 2), ("T2", 3, 5));
        var pipeline = new SwitchCostMinimization<TestTask, TestExecutor, TestAssignmentPolicy>(
            executors, tasks, @switch,
            coefficient: triple => triple.Executor.Id == "E1" && triple.From.Id == "T1" && triple.To.Id == "T2"
                ? new Flt64(5)
                : Flt64.One);

        pipeline.Invoke(model).IsOk.Should().BeTrue();

        model.MetaSubObjects.Should().ContainSingle();
        Fuookami.Ospf.Core.Model.Mechanism.MetaSubObject<Flt64> objective = model.MetaSubObjects[0];
        objective.Category.Should().Be(ObjectCategory.Minimum);
        objective.Name.Should().Be("switch cost");
        LinearPolynomial<Flt64> polynomial = objective.Polynomial;
        polynomial.Monomials.Should().NotBeEmpty();
        polynomial.Monomials.Max(monomial => monomial.Coefficient.ToDouble()).Should().BeApproximately(5.0, 1e-9,
            "the T1→T2 indicator carries weight 5");
    }

    /// <summary>**换型时间目标收时间表，不是指示表** / The switch-time objective collects the time table, not the indicator table.</summary>
    /// <remarks>
    /// 两张表混用时"成本"变成"秒数"——量纲变了而目标仍然是个正数。判据查目标里出现**时间符号的结果变量**
    /// （`switch_time_*_masking`），而不是 `switch_*_and`。
    ///
    /// Mixing the two tables turns "cost" into "seconds" — the dimension changes while the objective still looks like a positive number. The criterion checks that the objective
    /// carries the **time symbol's result variable** (`switch_time_*_masking`), not `switch_*_and`.
    /// </remarks>
    [Fact]
    public void SwitchTimeMinimization_ShouldCollectTheTimeTable() {
        (LinearMetaModel<Flt64> model, TaskSchedulingSwitch<TestTask, TestExecutor, TestAssignmentPolicy> @switch, List<TestTask> tasks, _) =
            Registered(("T1", 0, 2), ("T2", 3, 5));
        var pipeline = new SwitchTimeMinimization<TestTask, TestExecutor, TestAssignmentPolicy>(Window(), tasks, @switch);

        pipeline.Invoke(model).IsOk.Should().BeTrue();

        model.MetaSubObjects.Should().ContainSingle();
        Fuookami.Ospf.Core.Model.Mechanism.MetaSubObject<Flt64> objective = model.MetaSubObjects[0];
        objective.Category.Should().Be(ObjectCategory.Minimum);
        objective.Name.Should().Be("switch time");
        var names = objective.Polynomial.Monomials.Select(monomial => monomial.Symbol.Name).ToList();
        names.Should().NotBeEmpty("an empty objective would satisfy any OnlyContain vacuously");
        names.Should().OnlyContain(name => name.Contains("masking"),
            "the objective must collect the masked switch times: " + string.Join(",", names));
    }

    /// <summary>**未注册时两个目标都失败** / Both objectives fail before the switch is registered.</summary>
    /// <remarks>
    /// 返回 `Ok` 会让调用方以为目标已加。空表下"求和为零"是个看起来合法的答案。
    ///
    /// Returning `Ok` makes the caller believe the objective was added; with an empty table, "the sum is zero" looks like a legitimate answer.
    /// </remarks>
    [Fact]
    public void Objectives_ShouldFailBeforeRegistration() {
        using LinearMetaModel<Flt64> model = Build(out TaskSchedulingSwitch<TestTask, TestExecutor, TestAssignmentPolicy> @switch,
            out List<TestTask> tasks, out List<TestExecutor> executors, ("T1", 0, 2), ("T2", 3, 5));

        new SwitchCostMinimization<TestTask, TestExecutor, TestAssignmentPolicy>(executors, tasks, @switch).Invoke(model).IsOk
            .Should().BeFalse("the indicator table is not registered yet");
        new SwitchTimeMinimization<TestTask, TestExecutor, TestAssignmentPolicy>(Window(), tasks, @switch).Invoke(model).IsOk
            .Should().BeFalse("the switch-time table is not registered yet");
    }

    /// <summary>**系数提取失败必须报错** / A failed coefficient extraction must fail.</summary>
    /// <remarks>
    /// 基线在系数为 `null` 时用无穷大；`Flt64` 没有无穷，所以只能失败。悄悄当成 1 会让"这个换型不计成本"变成"计 1"，
    /// 而两个数看起来都正常。
    ///
    /// The baseline uses infinity when the coefficient is `null`; `Flt64` has none, so it must fail. Treating it as 1 silently turns "this switch is free" into "it costs 1", and
    /// both numbers look normal.
    /// </remarks>
    [Fact]
    public void SwitchCostMinimization_ShouldFailWhenTheCoefficientIsUnavailable() {
        (LinearMetaModel<Flt64> model, TaskSchedulingSwitch<TestTask, TestExecutor, TestAssignmentPolicy> @switch, List<TestTask> tasks, List<TestExecutor> executors) =
            Registered(("T1", 0, 2), ("T2", 3, 5));
        var pipeline = new SwitchCostMinimization<TestTask, TestExecutor, TestAssignmentPolicy>(
            executors, tasks, @switch, coefficient: _ => null);

        pipeline.Invoke(model).IsOk.Should().BeFalse();
    }

    /// <summary>**非模型被拒** / A non-model is rejected by both objectives.</summary>
    [Fact]
    public void Objectives_ShouldRejectANonModel() {
        (_, TaskSchedulingSwitch<TestTask, TestExecutor, TestAssignmentPolicy> @switch, List<TestTask> tasks, List<TestExecutor> executors) =
            Registered(("T1", 0, 2), ("T2", 3, 5));

        new SwitchCostMinimization<TestTask, TestExecutor, TestAssignmentPolicy>(executors, tasks, @switch).Invoke(new object()).IsOk
            .Should().BeFalse();
        new SwitchTimeMinimization<TestTask, TestExecutor, TestAssignmentPolicy>(Window(), tasks, @switch).Invoke(new object()).IsOk
            .Should().BeFalse();
    }

#if SCIP_INTEGRATION
    /// <summary>
    /// **真实 SCIP：带任务时间的换型时间会被 `est` 拉到 0，也能被拉大** / Real SCIP: with a task time the switch time can be driven to zero and also widened.
    /// </summary>
    /// <remarks>
    /// 换型时间是 `(est[to] − end[from]) × 指示`，而目标对**所有有序对**求和。于是：
    /// <list type="bullet">
    /// <item>最小化会把某个方向压成**负值**——它把后一个任务排到前一个任务之前，那一对的间隔就是负的。基线不夹这个负值，
    /// 照抄，不偷偷 `max(0, ·)`。</item>
    /// <item>最大化会被推到**大于计划里的 3600 秒**。</item>
    /// </list>
    /// 这两条一起才钉得住"`x` 真的读了 `est`"：若 `x` 是常量间隔，求和只可能落在 `0` 或 `3600`，**永远出不了负值、也超不过 3600**。
    /// 第一版判据断言最小值是 0，实测 −14400——断言写的是我以为会发生的，不是实现做的。
    ///
    /// The switch time is `(est[to] − end[from]) × indicator` and the objective sums over **all ordered pairs**, so minimising drives one direction **negative** (it orders the
    /// later task before the earlier one, whose gap is negative). The baseline does not clamp that (the earlier ceiling) and this copies it rather than quietly taking `max(0, ·)`.
    /// Maximising is pushed **above** the planned 3600 s. Together these pin "`x` really reads `est`": with a constant gap the sum could only be `0` or `3600`, never negative and
    /// never above 3600. The first version of this criterion asserted the minimum was 0 and measured −14400 — the assertion described what I expected, not what the code does.
    /// </remarks>
    [Fact]
    public void SwitchTime_ShouldFollowTheEstimatedStartUnderRealScip() {
        double minimum = SolveSwitchTime(new Flt64(1));
        double maximum = -SolveSwitchTime(new Flt64(-1));

        (minimum < 0.0).Should().BeTrue(
            "reversing the order makes one pair's gap negative, which a constant gap could never produce: " + minimum);
        (maximum > 3600.0).Should().BeTrue(
            "the gap can be widened beyond the planned 3600 s, which a constant gap could not do: " + maximum);
    }

    private static double SolveSwitchTime(Flt64 coefficient) {
        (LinearMetaModel<Flt64> model, TaskSchedulingSwitch<TestTask, TestExecutor, TestAssignmentPolicy> @switch, List<TestTask> tasks, List<TestExecutor> executors) =
            RegisteredWithTaskTime(("T1", 0, 2), ("T2", 3, 5));
        @switch.Register(model).IsOk.Should().BeTrue();
        var pipeline = new SwitchTimeMinimization<TestTask, TestExecutor, TestAssignmentPolicy>(
            Window(), tasks, @switch, coefficient: _ => coefficient);
        pipeline.Invoke(model).IsOk.Should().BeTrue();

        Fuookami.Ospf.Utils.Functional.Result<
            Fuookami.Ospf.Core.Solver.Output.FeasibleSolverOutput<Flt64>,
            Fuookami.Ospf.Utils.Error.ErrorCode,
            Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>> result =
            new Fuookami.Ospf.Core.Plugin.Scip.ScipColumnGenerationSolver()
                .SolveMILPAsync("gantt-switch-time", model).GetAwaiter().GetResult();
        result.IsOk.Should().BeTrue("the frontOf/betweenIn layers must keep the model feasible: " + result);
        return result.Value.Obj.ToDouble();
    }
#endif

    /// <summary>
    /// **真实 SCIP：换型成本可被压到零，也可被推到一** / Real SCIP: the switch cost can be pushed to zero or up to one.
    /// </summary>
    /// <remarks>
    /// 两台执行器、两个任务，两个任务都能上任意一台。最小化换型成本必须**把两个任务分到不同机器**（成本 0）；
    /// 反向（系数取 -1 再最小化，等价于最大化）必须**挤到同一台**（成本 1）。
    /// 两边都跑是必要的：只测最小值会让"指示恒为 0"通过，只测最大值会让"指示恒为 1"通过。
    ///
    /// Two executors, two tasks, each task allowed on either. Minimising the switch cost must **split the tasks across machines** (cost 0); the reverse (coefficient -1 and
    /// minimise, i.e. maximise) must **push them onto the same one** (cost 1). Both directions are necessary: the minimum alone lets "the indicator is always 0" pass, the
    /// maximum alone lets "always 1" pass.
    /// </remarks>
    [Fact]
    public async System.Threading.Tasks.Task SwitchCost_ShouldReachZeroWhenSplitAndOneWhenSharedUnderRealScip() {
        double minimum = SolveSwitchCost(new Flt64(1));
        double maximum = -SolveSwitchCost(new Flt64(-1));

        minimum.Should().BeApproximately(0.0, 1e-6, "two executors let the tasks avoid the switch entirely");
        maximum.Should().BeApproximately(1.0, 1e-6, "the indicator is a 0/1 variable, so the maximum is 1");
    }

    private static double SolveSwitchCost(Flt64 coefficient) {
        TimeWindow<Flt64> window = Window();
        var executors = new List<TestExecutor> { new("E1", "E1"), new("E2", "E2") };
        var tasks = new List<TestTask> {
            new("T1", "T1", new TimeRange(Origin, Origin.AddHours(2))),
            new("T2", "T2", new TimeRange(Origin.AddHours(3), Origin.AddHours(5))),
        };
        foreach (TestTask task in tasks) {
            foreach (TestExecutor executor in executors) {
                task.EnabledExecutorSet.Add(executor);
            }
        }
        var compilation = new TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy>(tasks, executors);
        var @switch = new TaskSchedulingSwitch<TestTask, TestExecutor, TestAssignmentPolicy>(window, tasks, executors, compilation);

        using var model = new LinearMetaModel<Flt64>();
        compilation.Register(model).IsOk.Should().BeTrue();
        @switch.Register(model).IsOk.Should().BeTrue();
        var pipeline = new SwitchCostMinimization<TestTask, TestExecutor, TestAssignmentPolicy>(
            executors, tasks, @switch, coefficient: _ => coefficient);
        pipeline.Invoke(model).IsOk.Should().BeTrue();

        Fuookami.Ospf.Utils.Functional.Result<
            Fuookami.Ospf.Core.Solver.Output.FeasibleSolverOutput<Flt64>,
            Fuookami.Ospf.Utils.Error.ErrorCode,
            Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>> result =
            new Fuookami.Ospf.Core.Plugin.Scip.ScipColumnGenerationSolver()
                .SolveMILPAsync("gantt-switch-cost", model).GetAwaiter().GetResult();
        result.IsOk.Should().BeTrue("the switch indicators must keep the model feasible: " + result);
        return result.Value.Obj.ToDouble();
    }
}
