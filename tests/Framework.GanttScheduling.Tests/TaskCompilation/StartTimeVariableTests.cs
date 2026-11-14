// 开始时间变量 `est` 的判据 / Criteria for the start-time variable `est`.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Tests.BunchCompilation;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.TaskCompilation;

/// <summary>
/// **结束时间从常量变成"开始 + 时长"（B547）** / End times go from constants to "start + duration" (B547).
/// </summary>
/// <remarks>
/// B546 的结束时间是**窗口结束点的常量**——完工时间因此只反映"计划什么时候结束"，最小化它等于什么都没做。
/// 本批引入基线里的 `est`（开始时间变量），结束时间变成 `est + 时长`，完工时间才第一次**可被排程推动**。
///
/// 这条链上有三处特别容易反：
/// <list type="number">
/// <item>不启用提前/延迟时 `est` 必须被**钉在**时间窗开始——写成"放开"就等于允许任意挪动，而解看起来更优；</item>
/// <item>`est` 必须**无符号**——有符号时"允许提前"那一支会降到负无穷，倒推出负的完工时间；</item>
/// <item>离散时间窗的整数变量**尚未接线**，必须显式失败——塞一个 `UInt64` 变量出去会被求解器在注册阶段拒，或者更糟，静默失真。</item>
/// </list>
///
/// B546's end times were **constants at the window end**, so the makespan only reflected "when the plan says it ends" and minimising it did nothing. This batch introduces the
/// baseline's `est` (start-time variable), making the end time `est + duration` and the makespan **pushable by the schedule** for the first time. Three links invert easily:
/// (1) with advance/delay disabled `est` must be **pinned** to the window start — "relaxed" would allow arbitrary movement while looking better; (2) `est` must be **unsigned** —
/// signed, the "advance allowed" branch sinks to negative infinity and yields a negative makespan; (3) the discrete-window integer variable is **not wired** and must fail
/// explicitly — shipping a `UInt64` variable gets rejected at registration, or worse, silently distorts.
/// </remarks>
public class StartTimeVariableTests {
    private static TimeWindow<Flt64> Window() {
        var start = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        return new TimeWindow<Flt64>(
            new TimeRange(start, start.AddHours(24)), true, TimeSpan.FromHours(1));
    }

    private static TimeWindow<Flt64> DiscreteWindow() {
        var start = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        return new TimeWindow<Flt64>(
            new TimeRange(start, start.AddHours(24)), false, TimeSpan.FromHours(1));
    }

    /// <summary>造一个 3 小时后开始、持续 2 小时的任务 / A task starting at 3h lasting 2h.</summary>
    private static TestTask TaskAt(
        TimeWindow<Flt64> window,
        string id,
        int startHours,
        int durationHours,
        bool advance = false,
        bool delay = false) =>
        new(id, id) {
            Time = new TimeRange(window.Start.AddHours(startHours), window.Start.AddHours(startHours + durationHours)),
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

    // ===== 结束时间 = 开始 + 时长 =====

    /// <summary>**结束时间对开始时间取系数 1，常量恰是时长** / The end time carries coefficient 1 on the start and a constant equal to the duration.</summary>
    /// <remarks>
    /// 3 小时开始、持续 2 小时 → `estimate_end_time_T1` 必须是 `1 × est_T1 + 7200`。
    /// 只查"有常量"会让"常量写成窗口结束"通过；只查"有单项"会让"系数写成 0"通过。
    ///
    /// Start at 3h, duration 2h → `estimate_end_time_T1` must be `1 × est_T1 + 7200`. Checking "a constant exists" alone would let "constant = window end" through; checking "one
    /// monomial exists" alone would let "coefficient 0" through.
    /// </remarks>
    [Fact]
    public void EndTime_ShouldBeTheStartPlusTheDuration() {
        TimeWindow<Flt64> window = Window();
        var tasks = new List<TestTask> { TaskAt(window, "T1", 3, 2) };
        TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy> taskTime = Time(window, tasks);
        using var model = new LinearMetaModel<Flt64>();

        taskTime.Register(model).IsOk.Should().BeTrue();

        var end = (ILinearIntermediateSymbol<Flt64>)taskTime.EstimateEndTime![tasks[0]];
        end.Name.Should().Be("estimate_end_time_T1");
        end.Polynomial.Monomials.Should().ContainSingle();
        end.Polynomial.Monomials[0].Coefficient.ToDouble().Should().BeApproximately(1.0, 1e-9);
        end.Polynomial.Monomials[0].Symbol.Name.Should().Be("est_T1");
        end.Polynomial.Constant.ToDouble().Should().BeApproximately(2 * 3600.0, 1e-6, "the duration is 2h");
    }

    /// <summary>**开始时间本身也有符号** / The start time has its own symbol.</summary>
    /// <remarks>
    /// `estimate_start_time_T1` 与 `est_T1` 是**两个不同的东西**：前者是"被引用时的名字"，后者是变量本身。
    /// 判据看的是符号名与它引用的变量名，两者都对才算接上。
    ///
    /// `estimate_start_time_T1` and `est_T1` are **two different things**: the former is the name used when referenced, the latter is the variable itself. The criterion checks
    /// both the symbol name and the variable it references.
    /// </remarks>
    [Fact]
    public void StartTime_ShouldReferenceTheStartVariable() {
        TimeWindow<Flt64> window = Window();
        var tasks = new List<TestTask> { TaskAt(window, "T1", 3, 2), TaskAt(window, "T2", 5, 1) };
        TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy> taskTime = Time(window, tasks);
        using var model = new LinearMetaModel<Flt64>();

        taskTime.Register(model).IsOk.Should().BeTrue();

        taskTime.EstimateStartTime.Should().NotBeNull();
        taskTime.EstimateStartTime!.Should().HaveCount(2);
        var first = (ILinearIntermediateSymbol<Flt64>)taskTime.EstimateStartTime![tasks[0]];
        first.Name.Should().Be("estimate_start_time_T1");
        first.Polynomial.Monomials.Should().ContainSingle();
        first.Polynomial.Monomials[0].Symbol.Name.Should().Be("est_T1");
        ((ILinearIntermediateSymbol<Flt64>)taskTime.EstimateStartTime[tasks[1]])
            .Polynomial.Monomials[0].Symbol!.Name.Should().Be("est_T2", "each task gets its own variable");
    }

    // ===== est 的边界 =====

    /// <summary>**不启用提前也不启用延迟 → 开始时间被钉在时间窗开始** / Neither advance nor delay → the start is pinned to the window start.</summary>
    /// <remarks>
    /// 这是整批最容易被写反的一条：把守卫写成"启用就钉住"会让默认行为变成**任意挪动**——而挪动通常让目标更好看，
    /// 于是错误的方向恰好是"结果变优"的方向，几乎不会被怀疑。
    ///
    /// This is the easiest line in the batch to invert: writing the guard as "pin when enabled" makes the default **arbitrary movement** — and movement usually improves the
    /// objective, so the wrong direction is exactly the one that looks better and is almost never suspected.
    /// </remarks>
    [Fact]
    public void StartVariable_ShouldBePinnedWhenNeitherAdvanceNorDelayIsEnabled() {
        TimeWindow<Flt64> window = Window();
        var tasks = new List<TestTask> { TaskAt(window, "T1", 3, 2) };
        TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy> taskTime = Time(window, tasks);
        using var model = new LinearMetaModel<Flt64>();

        taskTime.Register(model).IsOk.Should().BeTrue();

        var start = (ILinearIntermediateSymbol<Flt64>)taskTime.EstimateStartTime![tasks[0]];
        var variable = (Fuookami.Ospf.Core.Variable.CombinationVariableItem<Flt64, Fuookami.Ospf.Core.Variable.UContinuous>)start.Polynomial.Monomials[0].Symbol;
        variable.Range.Fixed.Should().BeTrue("neither advance nor delay is enabled, so the task starts when planned");
        variable.Range.FixedValue!.Value.ToDouble().Should().BeApproximately(3 * 3600.0, 1e-6);
    }

    /// <summary>**允许提前 → 下界不再钉在时间窗开始，而是 0（无符号）** / Advance allowed → the lower bound is 0 (unsigned), not the window start.</summary>
    /// <remarks>
    /// 有符号变量在这里会给出 `-1e308` 一类的下界，于是"最小化完工时间"可以一路推到负无穷。
    /// 判据因此**同时**断言"下界不是窗口开始"和"下界恰是 0"——只断言前者会让有符号变量通过。
    ///
    /// A signed variable would give a lower bound around `-1e308`, letting "minimise the makespan" push all the way to negative infinity. The criterion therefore asserts **both**
    /// "the lower bound is not the window start" and "the lower bound is exactly 0" — the former alone would let a signed variable pass.
    /// </remarks>
    [Fact]
    public void StartVariable_ShouldDropToZeroWhenAdvanceIsEnabled() {
        TimeWindow<Flt64> window = Window();
        var tasks = new List<TestTask> { TaskAt(window, "T1", 3, 2, advance: true) };
        TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy> taskTime = Time(window, tasks, advance: true);
        using var model = new LinearMetaModel<Flt64>();

        taskTime.Register(model).IsOk.Should().BeTrue();

        var start = (ILinearIntermediateSymbol<Flt64>)taskTime.EstimateStartTime![tasks[0]];
        var variable = (Fuookami.Ospf.Core.Variable.CombinationVariableItem<Flt64, Fuookami.Ospf.Core.Variable.UContinuous>)start.Polynomial.Monomials[0].Symbol;
        variable.Range.Fixed.Should().BeFalse("the start may move earlier");
        variable.Range.LowerBound!.Value.Unwrap().ToDouble().Should().BeApproximately(0.0, 1e-9,
            "an unsigned start cannot go below zero");
        // 只放开**提前**，延迟仍然禁止——所以上界还是时间窗开始（10800 秒）。这条正是"两个边界各管各的"的证据：
        // 把两条规则写成"任一启用就都放开"，这里会看到 86400。
        // Only **advance** is relaxed; delay is still forbidden — so the upper bound stays at the window start (10800 s). That is exactly the evidence that the two bounds are
        // independent: collapsing the rules into "either enabled → relax both" would show 86400 here.
        variable.Range.UpperBound!.Value.Unwrap().ToDouble().Should().BeApproximately(3 * 3600.0, 1e-6,
            "delay is still disabled, so the start cannot run late");
    }

    /// <summary>**允许延迟 → 上界不再是时间窗开始，而仍是窗口结束** / Delay allowed → the upper bound is the window end, not the start.</summary>
    /// <remarks>
    /// 只改延迟、不改提前：下界仍钉在窗口开始（不能提前），上界放开到窗口结束。
    /// 两个边界一个松一个紧，任何"一起放开"或"一起钉住"的实现都会被这条逮到。
    ///
    /// Delay only, advance untouched: the lower bound stays pinned (cannot run early) and the upper bound relaxes to the window end. One bound loose, one tight — any
    /// implementation that relaxes or pins both together is caught here.
    /// </remarks>
    [Fact]
    public void StartVariable_ShouldRelaxOnlyTheUpperBoundWhenDelayIsEnabled() {
        TimeWindow<Flt64> window = Window();
        var tasks = new List<TestTask> { TaskAt(window, "T1", 3, 2, delay: true) };
        TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy> taskTime = Time(window, tasks, delay: true);
        using var model = new LinearMetaModel<Flt64>();

        taskTime.Register(model).IsOk.Should().BeTrue();

        var start = (ILinearIntermediateSymbol<Flt64>)taskTime.EstimateStartTime![tasks[0]];
        var variable = (Fuookami.Ospf.Core.Variable.CombinationVariableItem<Flt64, Fuookami.Ospf.Core.Variable.UContinuous>)start.Polynomial.Monomials[0].Symbol;
        variable.Range.Fixed.Should().BeFalse();
        variable.Range.LowerBound!.Value.Unwrap().ToDouble().Should().BeApproximately(3 * 3600.0, 1e-6,
            "advance is still disabled, so the start cannot run early");
        variable.Range.UpperBound!.Value.Unwrap().ToDouble().Should().BeApproximately(24 * 3600.0, 1e-6);
    }

    // ===== 必须失败的两处 =====

    /// <summary>**离散时间窗必须失败，不能塞一个整数变量出去** / A discrete window must fail rather than ship an integer variable.</summary>
    /// <remarks>
    /// 基线在离散窗口上用 `UIntVariable1`。`UIntVar` 的缺省上界是 `ulong.MaxValue`（换成 double 是 1.8e19，
    /// 超过 binary64 精确整数上界 2^53），求解器注册阶段就会拒。在边界换算做对之前，**宁可报错**。
    ///
    /// The baseline uses `UIntVariable1` on discrete windows. `UIntVar`'s default upper bound is `ulong.MaxValue` (1.8e19 as a double, beyond binary64's exact-integer limit of
    /// 2^53), which the solver rejects at registration. Until the bound conversion is right, **failing is better**.
    /// </remarks>
    [Fact]
    public void StartVariable_ShouldFailOnADiscreteWindow() {
        TimeWindow<Flt64> window = DiscreteWindow();
        var tasks = new List<TestTask> { TaskAt(window, "T1", 3, 2) };
        TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy> taskTime = Time(window, tasks);
        using var model = new LinearMetaModel<Flt64>();

        taskTime.Register(model).IsOk.Should().BeFalse("the discrete start-time variable is not wired yet");
        taskTime.EstimateStartTime.Should().BeNull("a failed registration leaves no partial table");
        taskTime.EstimateEndTime.Should().BeNull();
    }

    /// <summary>**任务没有时间窗 → 失败** / A task without a window fails.</summary>
    [Fact]
    public void StartVariable_ShouldFailWhenATaskHasNoWindow() {
        TimeWindow<Flt64> window = Window();
        var tasks = new List<TestTask> { TaskAt(window, "T1", 3, 2), new("T2", "T2") };
        TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy> taskTime = Time(window, tasks);
        using var model = new LinearMetaModel<Flt64>();

        taskTime.Register(model).IsOk.Should().BeFalse("T2 has no window");
        taskTime.EstimateStartTime.Should().BeNull();
    }

    /// <summary>**重复注册幂等，`est` 不会重复添加** / Re-registering is idempotent and does not re-add `est`.</summary>
    /// <remarks>
    /// B539 在 `Compilation.Register` 上栽过一次：第二次注册撞 `SymbolRepetitive`。
    ///
    /// B539 once tripped `SymbolRepetitive` on the second `Compilation.Register`.
    /// </remarks>
    [Fact]
    public void StartVariable_ShouldBeIdempotent() {
        TimeWindow<Flt64> window = Window();
        var tasks = new List<TestTask> { TaskAt(window, "T1", 3, 2) };
        TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy> taskTime = Time(window, tasks);
        using var model = new LinearMetaModel<Flt64>();

        taskTime.Register(model).IsOk.Should().BeTrue();
        object start = taskTime.EstimateStartTime![tasks[0]];
        object end = taskTime.EstimateEndTime![tasks[0]];

        taskTime.Register(model).IsOk.Should().BeTrue("re-registering is an idempotent no-op");
        taskTime.EstimateStartTime[tasks[0]].Should().BeSameAs(start);
        taskTime.EstimateEndTime[tasks[0]].Should().BeSameAs(end);
    }
}
