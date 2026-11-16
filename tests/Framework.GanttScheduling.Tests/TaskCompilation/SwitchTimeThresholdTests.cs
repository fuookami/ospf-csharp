// 换型时间最小化的阈值分支的判据 / Criteria for the switch-time minimisation's threshold branch.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service.Limits;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Tests.BunchCompilation;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.TaskCompilation;

/// <summary>
/// **换型时间最小化的阈值分支** / The switch-time minimisation's threshold branch.
/// </summary>
/// <remarks>
/// 判据的重点：**阈值非零才建松弛**、**松弛名带两侧任务**、**系数在松弛外面乘**。
///
/// The criteria focus on **a slack being built only for a non-zero threshold**, on **the slack name carrying both tasks**, and on **the coefficient multiplying outside the slack**.
/// </remarks>
public class SwitchTimeThresholdTests {
    private static TimeWindow<Flt64> Window() {
        var start = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        return new TimeWindow<Flt64>(
            new TimeRange(start, start.AddHours(24)), true, TimeSpan.FromHours(1));
    }

    private static (LinearMetaModel<Flt64> Model,
        TaskSchedulingSwitch<TestTask, TestExecutor, TestAssignmentPolicy> Switch,
        List<TestTask> Tasks) Prepared() {
        TimeWindow<Flt64> window = Window();
        var executors = new List<TestExecutor> { new("E1", "E1") };
        var tasks = new List<TestTask> {
            new("T1", "T1", new TimeRange(window.Start, window.Start.AddHours(2))),
            new("T2", "T2", new TimeRange(window.Start.AddHours(3), window.Start.AddHours(5))),
        };
        foreach (TestTask task in tasks) {
            task.EnabledExecutorSet.Add(executors[0]);
        }
        var model = new LinearMetaModel<Flt64>();
        var compilation = new TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy>(tasks, executors);
        compilation.Register(model).IsOk.Should().BeTrue();
        var taskTime = new TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy>(
            window, tasks, compilation, delayEnabled: true);
        taskTime.Register(model).IsOk.Should().BeTrue();
        var @switch = new TaskSchedulingSwitch<TestTask, TestExecutor, TestAssignmentPolicy>(
            window, tasks, executors, compilation, taskTime);
        @switch.Register(model).IsOk.Should().BeTrue();
        return (model, @switch, tasks);
    }

    /// <summary>
    /// **阈值非零才建松弛；阈值 0 时一个都不建** / A slack is built only for a non-zero threshold; a zero threshold builds none.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 阈值 `0`（显式 `TimeSpan.Zero` 与默认一致）→ 模型里**没有** `switch_time_threshold_*`，目标只有换型时间项。
    /// 2. 阈值非零 → 每对任务**各一条**，名字带两侧任务 ID（`switch_time_threshold_T1_T2`）。
    /// 3. 目标仍叫 `switch time`、仍是 `Minimum`——**两条分支同名**。
    ///
    /// Three paired points: a `0` threshold (an explicit `TimeSpan.Zero` agreeing with the default) leaves **no** `switch_time_threshold_*` and the objective holds only the switch-time terms; a
    /// non-zero threshold yields **one slack per task pair**, its name carrying both task ids; and the objective is still named `switch time` and still `Minimum`, since **both branches share the name**.
    /// </remarks>
    [Fact]
    public void SwitchTimeMinimization_ShouldBuildASlackOnlyWhenTheThresholdIsNonZero() {
        foreach (Func<(TestTask From, TestTask To), TimeSpan?> extractor
            in new Func<(TestTask From, TestTask To), TimeSpan?>[] { _ => null, _ => TimeSpan.Zero }) {
            (LinearMetaModel<Flt64> model,
                TaskSchedulingSwitch<TestTask, TestExecutor, TestAssignmentPolicy> @switch,
                List<TestTask> tasks) = Prepared();
            var pipeline = new SwitchTimeMinimization<TestTask, TestExecutor, TestAssignmentPolicy>(
                Window(), tasks, @switch, threshold: extractor);

            pipeline.Invoke(model).IsOk.Should().BeTrue();

            model.Tokens.Symbols.Select(symbol => symbol.Name).Should().NotContain(name => name.StartsWith("switch_time_threshold"));
            model.MetaSubObjects.Last().Name.Should().Be("switch time");
        }

        (LinearMetaModel<Flt64> thresholdModel,
            TaskSchedulingSwitch<TestTask, TestExecutor, TestAssignmentPolicy> thresholdSwitch,
            List<TestTask> thresholdTasks) = Prepared();
        var withThreshold = new SwitchTimeMinimization<TestTask, TestExecutor, TestAssignmentPolicy>(
            Window(), thresholdTasks, thresholdSwitch, threshold: _ => TimeSpan.FromHours(1));

        withThreshold.Invoke(thresholdModel).IsOk.Should().BeTrue();

        var names = thresholdModel.Tokens.Symbols.Select(symbol => symbol.Name).ToList();
        names.Should().Contain("switch_time_threshold_T1_T2", "the name carries both sides of the pair");
        names.Should().Contain("switch_time_threshold_T2_T1", "both orderings are charged");
        MetaSubObject<Flt64> objective = thresholdModel.MetaSubObjects.Last();
        objective.Category.Should().Be(ObjectCategory.Minimum);
        objective.Name.Should().Be("switch time", "both branches name the objective the same");
    }

    /// <summary>
    /// **系数在松弛外面乘，且松弛带的是正部** / The coefficient multiplies outside the slack, and the slack carries the positive part.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 目标里**每一项**的系数都含那个因子：传 `coefficient: _ => new Flt64(5.0)` 时每项系数都是 5 的倍数
    ///    （换型时间项本身也可能带别的系数，所以断言"能被 5 整除且非零"，不是断言"等于 5"）。
    /// 2. 目标里的符号**全是松弛的正部**（`*_pos`），没有换型时间本身的符号——那是"超出阈值的部分才罚"。
    /// 3. 松弛仍是三开关形状：`Threshold = true`、`WithNegative = false`、`WithPositive = true`。
    ///
    /// Three paired points: **every** objective term is weighted by that factor — passing `coefficient: _ => new Flt64(5.0)` makes each coefficient a multiple of 5, the switch-time terms themselves
    /// possibly carrying other factors, so the assertion is "a non-zero multiple of 5" rather than "equal to 5"; every symbol in the objective is a slack **positive** variable (`*_pos`) with no
    /// switch-time symbols left, since what is charged is "the part above the threshold"; and the slack still has the three-switch shape `Threshold = true`, `WithNegative = false`, `WithPositive = true`.
    /// </remarks>
    [Fact]
    public void SwitchTimeMinimization_ShouldWeightThePositivePartByTheCoefficient() {
        (LinearMetaModel<Flt64> model,
            TaskSchedulingSwitch<TestTask, TestExecutor, TestAssignmentPolicy> @switch,
            List<TestTask> tasks) = Prepared();
        var pipeline = new SwitchTimeMinimization<TestTask, TestExecutor, TestAssignmentPolicy>(
            Window(), tasks, @switch,
            threshold: _ => TimeSpan.FromHours(1),
            coefficient: _ => new Flt64(5.0));

        pipeline.Invoke(model).IsOk.Should().BeTrue();

        MetaSubObject<Flt64> objective = model.MetaSubObjects.Last();
        objective.Polynomial.Monomials.Should().NotBeEmpty();
        objective.Polynomial.Monomials.Should().OnlyContain(monomial =>
            monomial.Symbol.Name.EndsWith("_pos") && monomial.Coefficient.ToDouble() != 0.0);
        // **系数必须恰好乘一次**：阈值分支下 `pos` 的系数是 1，目标项系数就应**恰为 5**。
        // 断言"是 5 的倍数"放过了"乘了两次"（25 也是 5 的倍数），那是把系数揉进松弛又乘一遍的典型症状。
        // **The coefficient must be applied exactly once**: under the threshold branch the `pos` coefficient is 1, so each objective coefficient should be **exactly 5**. Asserting "a multiple of 5"
        // lets "applied twice" through (25 is also a multiple of 5), which is exactly the symptom of folding the coefficient into the slack and multiplying again.
        objective.Polynomial.Monomials.Should().OnlyContain(monomial =>
            monomial.Coefficient.ToDouble() == 5.0,
            "the coefficient multiplies the slack exactly once, from outside");

        var slacks = model.Tokens.Symbols
            .OfType<LinearFunctionSymbolAdapter<Flt64>>()
            .Select(adapter => adapter.Function)
            .OfType<SlackFunction<Flt64>>()
            .Where(slack => slack.Name.StartsWith("switch_time_threshold"))
            .ToList();
        // 两两包括自身配对（T1→T1、T2→T2），所以是 4 条而不是 2 条。
        // Pairs include the self pairs (T1->T1, T2->T2), so there are 4 rather than 2.
        slacks.Should().HaveCount(4, "one per ordered task pair, self pairs included");
        slacks.Should().OnlyContain(slack => slack.Threshold);
        slacks.Should().OnlyContain(slack => !slack.WithNegative);
        slacks.Should().OnlyContain(slack => slack.WithPositive);

        // **系数不能揉进松弛的 `x`**：那样目标项系数看起来一样（5 × 1 对 1 × 5），但阈值关系变成
        // `coefficient·x − pos ≤ threshold`，同一个阈值含义整个变了。查 `x` 有没有被缩放才分得开。
        // **The coefficient must not be folded into the slack's `x`**: the objective coefficients would look identical (5 x 1 versus 1 x 5) while the threshold relation became
        // `coefficient·x - pos <= threshold`, changing what the same threshold means. Checking whether `x` was scaled is what separates them.
        slacks.Should().OnlyContain(slack => slack.X.Monomials.All(monomial => monomial.Coefficient.ToDouble() == 1.0),
            "the slack is built on the unweighted switch-time polynomial");
    }
}
