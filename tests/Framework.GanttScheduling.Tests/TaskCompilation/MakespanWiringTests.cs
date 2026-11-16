// 完工时间接线的判据 / Criteria for the makespan wiring.

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
/// **完工时间从空壳变成真符号** / The makespan goes from an empty stub to a real symbol.
/// </summary>
/// <remarks>
/// `Makespan.Register` 原来返回 `Ok` 却什么都没做。这里：任务时间建 `estimate_end_time_*`（各任务时间窗结束点），
/// 完工时间对它们取 `MaxFunction`，最小化把它加进目标。
///
/// 这条链上每一环都能静默地错：
/// <list type="bullet">
/// <item>少了结束时间表 → 完工时间是常量 0，目标最小化 0，解看起来完全正常；</item>
/// <item>跳过没有时间窗的任务 → 完工时间偏小，仍然"看着合理"；</item>
/// <item>取 `Min` 而不是 `Max` → 变成"最早完成的那个任务"，正好把排序反了。</item>
/// </list>
///
/// `Makespan.Register` used to return `Ok` and do nothing. builds `estimate_end_time_*` (each task's window end) in the task time, takes a `MaxFunction` over them,
/// and the minimisation adds it to the objective. Every link can fail silently: no end-time table → the makespan is the constant 0 and the objective minimises 0; skipping tasks
/// without a window → a smaller makespan that still looks plausible; taking `Min` instead of `Max` → "the earliest finished task", exactly backwards.
/// </remarks>
public class MakespanWiringTests {
    private static TimeWindow<Flt64> Window() {
        var start = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        return new TimeWindow<Flt64>(
            new TimeRange(start, start.AddHours(24)), true, TimeSpan.FromHours(1));
    }

    private static TestTask TaskEndingAt(string id, TimeWindow<Flt64> window, int hours) =>
        new(id, id) { Time = new TimeRange(window.Start, window.Start.AddHours(hours)) };

    // ===== 任务时间：结束时间表 =====

    /// <summary>**每个任务一条结束时间，值是它自己时间窗结束点** / One end-time symbol per task, valued at its own window end.</summary>
    /// <remarks>
    /// 两个任务在 3 小时和 8 小时结束 → 两条符号，值是 10800 和 28800 秒。
    /// 只数条数会让"两个都是同一个值"通过；只看一个值会让"复制粘贴写错任务"通过。
    ///
    /// Two tasks end at 3h and 8h → two symbols valued 10800 and 28800 seconds. Counting alone would let "both the same value" through; reading one value alone would let
    /// "copy-paste the wrong task" through.
    /// </remarks>
    [Fact]
    public void TaskTime_ShouldBuildOneEndTimePerTask() {
        TimeWindow<Flt64> window = Window();
        var tasks = new List<TestTask> {
            TaskEndingAt("T1", window, 3), TaskEndingAt("T2", window, 8),
        };
        var compilation = new TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy>(
            tasks, new List<TestExecutor> { new("E1", "E1") });
        var taskTime = new TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy>(
            window, tasks, compilation);
        using var model = new LinearMetaModel<Flt64>();

        taskTime.Register(model).IsOk.Should().BeTrue();

        taskTime.EstimateEndTime.Should().NotBeNull();
        IReadOnlyDictionary<TestTask, object> endTimes = taskTime.EstimateEndTime!;
        endTimes.Should().HaveCount(2);
        endTimes[tasks[0]].Should().BeOfType<Fuookami.Ospf.Core.Symbol.LinearExpressionSymbol>()
            .Which.Name.Should().Be("estimate_end_time_T1");
        endTimes[tasks[1]].Should().BeOfType<Fuookami.Ospf.Core.Symbol.LinearExpressionSymbol>()
            .Which.Name.Should().Be("estimate_end_time_T2");
        ((Fuookami.Ospf.Core.Symbol.LinearExpressionSymbol)endTimes[tasks[0]])
            .Polynomial.Constant.ToDouble().Should().BeApproximately(3 * 3600.0, 1e-6);
        ((Fuookami.Ospf.Core.Symbol.LinearExpressionSymbol)endTimes[tasks[1]])
            .Polynomial.Constant.ToDouble().Should().BeApproximately(8 * 3600.0, 1e-6);
    }

    /// <summary>**没有时间窗的任务必须失败，不能跳过** / A task without a window must fail, not be skipped.</summary>
    /// <remarks>
    /// 跳过它会让完工时间偏小——而且偏得很自然，因为剩下的任务本来就结束得更早。**偏差在结果里读不出来。**
    ///
    /// Skipping it shrinks the makespan, naturally so — the remaining tasks do end earlier. **The bias is invisible in the result.**
    /// </remarks>
    [Fact]
    public void TaskTime_ShouldFailWhenATaskHasNoWindow() {
        TimeWindow<Flt64> window = Window();
        var tasks = new List<TestTask> { TaskEndingAt("T1", window, 3), new("T2", "T2") };
        var compilation = new TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy>(
            tasks, new List<TestExecutor> { new("E1", "E1") });
        var taskTime = new TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy>(
            window, tasks, compilation);
        using var model = new LinearMetaModel<Flt64>();

        taskTime.Register(model).IsOk.Should().BeFalse("T2 has no window, so its end time is unknown");
        taskTime.EstimateEndTime.Should().BeNull("a failed registration must not leave a partial table");
    }

    /// <summary>**重复注册是幂等的** / Re-registering is idempotent.</summary>
    /// <remarks>
    /// 在 `Compilation.Register` 上栽过一次：第二次注册撞 `SymbolRepetitive`。
    /// 这里同样要有闸——否则"注册两次"这条最普通的使用方式直接炸。
    ///
    /// hit this on `Compilation.Register`: the second registration tripped `SymbolRepetitive`. The same guard is needed here, or the most ordinary "register twice" blows up.
    /// </remarks>
    [Fact]
    public void TaskTime_ShouldBeIdempotent() {
        TimeWindow<Flt64> window = Window();
        var tasks = new List<TestTask> { TaskEndingAt("T1", window, 3) };
        var compilation = new TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy>(
            tasks, new List<TestExecutor> { new("E1", "E1") });
        var taskTime = new TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy>(
            window, tasks, compilation);
        using var model = new LinearMetaModel<Flt64>();

        taskTime.Register(model).IsOk.Should().BeTrue();
        object first = taskTime.EstimateEndTime![tasks[0]];
        taskTime.Register(model).IsOk.Should().BeTrue("re-registering is an idempotent no-op");
        taskTime.EstimateEndTime[tasks[0]].Should().BeSameAs(first, "the second call must reuse the symbol");
    }

    // ===== 完工时间 =====

    /// <summary>**完工时间对结束时间取最大值，不是最小值** / The makespan is the maximum end time, not the minimum.</summary>
    /// <remarks>
    /// 3 小时与 8 小时结束 → 完工时间必须是 8 小时。取反了就是 3 小时，而 3 小时同样是"一个完工时间"，
    /// 任何只检查"是个正数"的判据都发现不了。
    ///
    /// End times of 3h and 8h → the makespan must be 8h. Reversed it is 3h, which is also "a makespan" — no criterion that merely checks "it is positive" would notice.
    /// </remarks>
    [Fact]
    public void Makespan_ShouldBeTheLatestEndTime() {
        TimeWindow<Flt64> window = Window();
        var tasks = new List<TestTask> {
            TaskEndingAt("T1", window, 3), TaskEndingAt("T2", window, 8),
        };
        var compilation = new TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy>(
            tasks, new List<TestExecutor> { new("E1", "E1") });
        var taskTime = new TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy>(
            window, tasks, compilation);
        var makespan = new Makespan<TestTask, TestExecutor, TestAssignmentPolicy>(tasks, taskTime);
        using var model = new LinearMetaModel<Flt64>();

        taskTime.Register(model).IsOk.Should().BeTrue();
        makespan.Register(model).IsOk.Should().BeTrue();

        makespan.MakespanSymbol.Should().NotBeNull();
        var symbol = (Fuookami.Ospf.Core.Symbol.ILinearIntermediateSymbol<Flt64>)makespan.MakespanSymbol!;
        symbol.Name.Should().Be("makespan");
        // 结束时间符号都进了 MaxFunction，所以结果多项式引用的不是常量，而是那个最大值变量。
        // The end-time symbols feed a MaxFunction, so the result polynomial references the max variable rather than a constant.
        symbol.Polynomial.Monomials.Should().ContainSingle("the makespan is the max function's result variable");
        symbol.Polynomial.Monomials[0].Symbol.Name.Should().Be("makespan_max");
    }

    /// <summary>**任务时间的结束时间还没注册，完工时间必须失败** / Without registered end times the makespan must fail.</summary>
    [Fact]
    public void Makespan_ShouldFailBeforeTheTaskTimeIsRegistered() {
        TimeWindow<Flt64> window = Window();
        var tasks = new List<TestTask> { TaskEndingAt("T1", window, 3) };
        var compilation = new TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy>(
            tasks, new List<TestExecutor> { new("E1", "E1") });
        var taskTime = new TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy>(
            window, tasks, compilation);
        var makespan = new Makespan<TestTask, TestExecutor, TestAssignmentPolicy>(tasks, taskTime);
        using var model = new LinearMetaModel<Flt64>();

        makespan.Register(model).IsOk.Should().BeFalse("there are no end times to take the maximum of");
        makespan.MakespanSymbol.Should().BeNull();
    }

    // ===== 目标 =====

    /// <summary>**最小化把完工时间加进目标，且带上系数** / The minimisation adds the makespan to the objective, weighted.</summary>
    /// <remarks>
    /// 系数取 3，目标里就必须出现 `3 × makespan_max`。系数被丢掉时目标仍然"有这一项"，
    /// 只是权重从 3 变成 1——目标值差三倍，而解的形状可能完全一样。
    ///
    /// With a coefficient of 3 the objective must carry `3 × makespan_max`. Dropping the coefficient still leaves "a term", only weighted 1 instead of 3 — the objective value is off by
    /// a factor of three while the solution shape may be identical.
    /// </remarks>
    [Fact]
    public void MakespanMinimization_ShouldAddTheWeightedObjective() {
        TimeWindow<Flt64> window = Window();
        var tasks = new List<TestTask> { TaskEndingAt("T1", window, 3) };
        var compilation = new TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy>(
            tasks, new List<TestExecutor> { new("E1", "E1") });
        var taskTime = new TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy>(
            window, tasks, compilation);
        var makespan = new Makespan<TestTask, TestExecutor, TestAssignmentPolicy>(tasks, taskTime);
        using var model = new LinearMetaModel<Flt64>();
        taskTime.Register(model).IsOk.Should().BeTrue();
        makespan.Register(model).IsOk.Should().BeTrue();
        int before = model.MetaSubObjects.Count;

        var minimization = new MakespanMinimization<TestExecutor, TestAssignmentPolicy>(
            window, makespan, coefficient: new Flt64(3.0));

        minimization.Invoke(model).IsOk.Should().BeTrue();
        model.MetaSubObjects.Should().HaveCount(before + 1);
        MetaSubObject<Flt64> objective = model.MetaSubObjects.Last();
        objective.Category.Should().Be(ObjectCategory.Minimum);
        objective.Name.Should().Be("makespan");
        objective.Polynomial.Monomials.Should().ContainSingle();
        objective.Polynomial.Monomials[0].Coefficient.ToDouble().Should().BeApproximately(3.0, 1e-6);
        objective.Polynomial.Monomials[0].Symbol.Name.Should().Be("makespan_max");
    }

    /// <summary>**完工时间没注册，最小化必须失败** / Without a registered makespan the minimisation must fail.</summary>
    [Fact]
    public void MakespanMinimization_ShouldFailBeforeRegistration() {
        TimeWindow<Flt64> window = Window();
        var tasks = new List<TestTask> { TaskEndingAt("T1", window, 3) };
        var compilation = new TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy>(
            tasks, new List<TestExecutor> { new("E1", "E1") });
        var taskTime = new TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy>(
            window, tasks, compilation);
        var makespan = new Makespan<TestTask, TestExecutor, TestAssignmentPolicy>(tasks, taskTime);
        using var model = new LinearMetaModel<Flt64>();
        int before = model.MetaSubObjects.Count;

        var minimization = new MakespanMinimization<TestExecutor, TestAssignmentPolicy>(window, makespan);

        minimization.Invoke(model).IsOk.Should().BeFalse("there is no makespan to minimise");
        model.MetaSubObjects.Should().HaveCount(before, "a failed invocation leaves no half-built objective");
    }

    /// <summary>**非模型必须失败** / A non-model must fail.</summary>
    [Fact]
    public void MakespanMinimization_ShouldRejectANonModel() {
        TimeWindow<Flt64> window = Window();
        var tasks = new List<TestTask> { TaskEndingAt("T1", window, 3) };
        var compilation = new TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy>(
            tasks, new List<TestExecutor> { new("E1", "E1") });
        var taskTime = new TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy>(
            window, tasks, compilation);
        var makespan = new Makespan<TestTask, TestExecutor, TestAssignmentPolicy>(tasks, taskTime);

        var minimization = new MakespanMinimization<TestExecutor, TestAssignmentPolicy>(window, makespan);

        minimization.Invoke(new object()).IsOk.Should().BeFalse(
            "a makespan objective can only be applied to a linear meta model");
    }
}
