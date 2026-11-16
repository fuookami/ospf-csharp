// 完工时间最小化的阈值分支的判据 / Criteria for the makespan minimisation's threshold branch.

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
/// **完工时间最小化的阈值分支** / The makespan minimisation's threshold branch.
/// </summary>
/// <remarks>
/// 判据的重点：**阈值在窗口起点时**直接最小化完工时间、**阈值在窗口内时**先建阈值松弛再最小化其正部、
/// 且**两条分支的目标名都是 `makespan`**。
///
/// The criteria focus on **a threshold at the window start** minimising the makespan directly, on **a threshold inside the window** first building a threshold slack and then minimising its positive
/// part, and on **both branches naming the objective `makespan`**.
/// </remarks>
public class MakespanThresholdTests {
    private static TimeWindow<Flt64> Window() {
        var start = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        return new TimeWindow<Flt64>(
            new TimeRange(start, start.AddHours(24)), true, TimeSpan.FromHours(1));
    }

    private static TestTask TaskEndingAt(string id, TimeWindow<Flt64> window, int hours) =>
        new(id, id) { Time = new TimeRange(window.Start, window.Start.AddHours(hours)) };

    private static (LinearMetaModel<Flt64> Model, Makespan<TestTask, TestExecutor, TestAssignmentPolicy> Makespan)
        Prepared() {
        TimeWindow<Flt64> window = Window();
        var tasks = new List<TestTask> { TaskEndingAt("T1", window, 3) };
        var compilation = new TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy>(
            tasks, new List<TestExecutor> { new("E1", "E1") });
        var taskTime = new TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy>(
            window, tasks, compilation);
        var makespan = new Makespan<TestTask, TestExecutor, TestAssignmentPolicy>(tasks, taskTime);
        var model = new LinearMetaModel<Flt64>();
        taskTime.Register(model).IsOk.Should().BeTrue();
        makespan.Register(model).IsOk.Should().BeTrue();
        return (model, makespan);
    }

    /// <summary>
    /// **阈值在窗口起点：直接最小化完工时间** / A threshold at the window start minimises the makespan directly.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 阈值取 `null`（默认窗口起点）与**显式传窗口起点**结果一致——**两条入口不是两条语义**。
    /// 2. 目标里**只有一项**，符号就是完工时间 `makespan_max`，系数是 `coefficient`。
    /// 3. 模型里**没有多出** `makespan_threshold` 符号——阈值分支没被误触发。
    ///
    /// Three paired points: a `null` threshold (the window start by default) and **an explicit window start** agree, so the two entry points are not two semantics; the objective has **exactly one
    /// term**, the makespan symbol `makespan_max`, with `coefficient` as its weight; and **no** `makespan_threshold` symbol appears in the model, so the threshold branch was not triggered by mistake.
    /// </remarks>
    [Fact]
    public void MakespanMinimization_ShouldMinimiseTheMakespanWhenTheThresholdIsTheWindowStart() {
        foreach (DateTimeOffset? threshold in new DateTimeOffset?[] { null, Window().Start }) {
            (LinearMetaModel<Flt64> model, Makespan<TestTask, TestExecutor, TestAssignmentPolicy> makespan) = Prepared();
            var minimization = new MakespanMinimization<TestExecutor, TestAssignmentPolicy>(
                Window(), makespan, threshold: threshold, coefficient: new Flt64(3.0));

            minimization.Invoke(model).IsOk.Should().BeTrue();

            MetaSubObject<Flt64> objective = model.MetaSubObjects.Last();
            objective.Name.Should().Be("makespan");
            objective.Polynomial.Monomials.Should().ContainSingle();
            objective.Polynomial.Monomials[0].Symbol.Name.Should().Be("makespan_max");
            objective.Polynomial.Monomials[0].Coefficient.ToDouble().Should().BeApproximately(3.0, 1e-6);
            model.Tokens.Symbols.Select(symbol => symbol.Name).Should().NotContain("makespan_threshold",
                "the threshold branch builds no slack when the threshold is the window start");
        }
    }

    /// <summary>
    /// **阈值在窗口内：先建松弛，再最小化它的正部** / A threshold inside the window builds the slack first and minimises its positive part.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 模型里**多出一条** `makespan_threshold` 符号——松弛真被加进去了。
    /// 没有被约束的松弛去最小化是恒为零的空操作，所以"松弛在模型里"是这条分支成立的前提。
    /// 2. 目标是 `Minimum` 且名仍是 `makespan`——**两条分支同名**，调用方按名字找目标时不会漏。
    /// 3. 目标的**唯一一项**引用的是松弛的**正部变量**（`makespan_threshold_pos`），不是完工时间本身，
    /// 也不是负部——`withNegative = false` 让负部根本不存在。
    /// 4. 系数仍是 `coefficient`（这里取 5）。
    ///
    /// Four paired points: **one more** `makespan_threshold` symbol appears in the model, so the slack really went in — an unconstrained slack would minimise a constant zero, so its presence is
    /// the precondition for this branch to mean anything; the objective is `Minimum` and still named `makespan`, since **both branches share the name** so a caller looking it up misses neither;
    /// the objective's **single term** references the slack's **positive** variable (`makespan_threshold_pos`) rather than the makespan itself and rather than a negative part, which does not exist
    /// with `withNegative = false`; and the weight is still `coefficient`, here 5.
    /// </remarks>
    [Fact]
    public void MakespanMinimization_ShouldMinimiseTheSlacksPositivePartWhenTheThresholdIsInside() {
        (LinearMetaModel<Flt64> model, Makespan<TestTask, TestExecutor, TestAssignmentPolicy> makespan) = Prepared();
        TimeWindow<Flt64> window = Window();
        var minimization = new MakespanMinimization<TestExecutor, TestAssignmentPolicy>(
            window, makespan, threshold: window.Start.AddHours(2), coefficient: new Flt64(5.0));

        minimization.Invoke(model).IsOk.Should().BeTrue();

        model.Tokens.Symbols.Select(symbol => symbol.Name).Should().Contain("makespan_threshold");
        MetaSubObject<Flt64> objective = model.MetaSubObjects.Last();
        objective.Category.Should().Be(ObjectCategory.Minimum);
        objective.Name.Should().Be("makespan", "both branches name the objective the same");
        objective.Polynomial.Monomials.Should().ContainSingle();
        objective.Polynomial.Monomials[0].Symbol.Name.Should().Be("makespan_threshold_pos");
        objective.Polynomial.Monomials[0].Coefficient.ToDouble().Should().BeApproximately(5.0, 1e-6);
        model.Tokens.Symbols.Select(symbol => symbol.Name).Should().NotContain("makespan_threshold_neg",
            "withNegative = false means there is no negative part at all");
    }

    /// <summary>
    /// **阈值开关真的被当成阈值用，而不是普通松弛** / The threshold flag is used as a threshold, not as a plain slack.
    /// </summary>
    /// <remarks>
    /// 两点成对：加进模型的那个符号是 `SlackFunction`，且它的 `Threshold` 为 `true`、`WithPositive` 为 `true`、`WithNegative` 为 `false`。
    /// 只看"有个叫 `makespan_threshold` 的符号"分不出这三者的任一；三个开关各自错一个，行都会照样建出来，只是含义变了。
    ///
    /// Two paired points: the symbol added to the model is a `SlackFunction` whose `Threshold` is `true`, `WithPositive` is `true` and `WithNegative` is `false`. Merely finding a symbol named
    /// `makespan_threshold` distinguishes none of the three, and getting any one flag wrong still builds the rows — with a different meaning.
    /// </remarks>
    [Fact]
    public void MakespanMinimization_ShouldBuildTheSlackWithTheThresholdSwitches() {
        (LinearMetaModel<Flt64> model, Makespan<TestTask, TestExecutor, TestAssignmentPolicy> makespan) = Prepared();
        TimeWindow<Flt64> window = Window();
        var minimization = new MakespanMinimization<TestExecutor, TestAssignmentPolicy>(
            window, makespan, threshold: window.Start.AddHours(2));

        minimization.Invoke(model).IsOk.Should().BeTrue();

        // 加进模型的不是 `SlackFunction` 本身，而是它的适配器——要穿过一层才看得到开关。
        // What goes into the model is the adapter, not the `SlackFunction` itself, so the switches sit one layer in.
        SlackFunction<Flt64> slack = model.Tokens.Symbols
            .OfType<LinearFunctionSymbolAdapter<Flt64>>()
            .Select(adapter => adapter.Function)
            .OfType<SlackFunction<Flt64>>()
            .Should().ContainSingle("exactly one threshold slack is built").Subject;
        slack.Threshold.Should().BeTrue("the threshold form is used, not a plain slack");
        slack.WithNegative.Should().BeFalse("a lower side would invert the penalty direction");
        slack.WithPositive.Should().BeTrue("only the part above the threshold is penalised");
    }
}
