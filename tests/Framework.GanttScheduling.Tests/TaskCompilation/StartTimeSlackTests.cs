// 开始时间松弛 `est_slack` 的判据 / Criteria for the start-time slack `est_slack`.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Tests.BunchCompilation;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.TaskCompilation;

/// <summary>
/// **开始时间松弛** / The start-time slack.
/// </summary>
/// <remarks>
/// `est_slack` 量的不是"开始时间是多少"，而是"**开始时间比计划早/晚多少**"。这个差别是整个延迟/提前成本的计费基础：
/// 没有它，`SwitchTimeMinimization` 那批 limit 无从下手。
///
/// 三处会静默错的地方：
/// <list type="number">
/// <item>不能挪动的任务也建真松弛 → 恒零的符号读回来像"没有偏差"，**恰好是对的答案**，于是没人会去查；</item>
/// <item>只开一个方向时写成两个方向的绝对值 → 超出计划也开始计费；</item>
/// <item>方向写反 → 提前被当成延迟，而两者都是正数。</item>
/// </list>
///
/// `est_slack` measures not "what the start time is" but "**how far the start is from plan**". That difference is the whole basis for advance/delay costing: without it the
/// `SwitchTimeMinimization` limits have nowhere to start. Three silent failures: (1) building a real slack for a task that cannot move → a constant-zero symbol reads back as
/// "no deviation", which is **the correct answer**, so nobody investigates; (2) using the two-direction absolute value when only one direction is open → exceeding the plan is
/// charged too; (3) an inverted direction → advance is billed as delay, and both are positive numbers.
/// </remarks>
public class StartTimeSlackTests {
    private static TimeWindow<Flt64> Window() {
        var start = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        return new TimeWindow<Flt64>(
            new TimeRange(start, start.AddHours(24)), true, TimeSpan.FromHours(1));
    }

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

    /// <summary>**不能挪动的任务是常量零，不是真松弛** / A task that cannot move gets a constant zero, not a real slack.</summary>
    /// <remarks>
    /// 这条判据的价值在"**没有辅助变量**"上：常量零的符号没有 `neg`/`pos`，也就不会往模型里塞任何东西。
    /// 只看"读回来的值是 0"完全分不出这两种实现——而两者在模型里的大小差三个变量。
    ///
    /// The value of this criterion is "**no helper variables**": a constant-zero symbol has no `neg`/`pos` and therefore adds nothing to the model. Checking only "the value reads
    /// back as 0" cannot tell the two implementations apart — and they differ by three variables in the model.
    /// </remarks>
    [Fact]
    public void Slack_ShouldBeAConstantZeroWhenTheTaskCannotMove() {
        TimeWindow<Flt64> window = Window();
        var tasks = new List<TestTask> { TaskAt(window, "T1", 3, 2) };
        TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy> taskTime = Time(window, tasks);
        using var model = new LinearMetaModel<Flt64>();

        taskTime.Register(model).IsOk.Should().BeTrue();

        taskTime.EstimateSlack.Should().NotBeNull();
        object slack = taskTime.EstimateSlack![tasks[0]];
        slack.Should().BeOfType<LinearExpressionSymbol>("a task that cannot move needs no slack variables");
        ((LinearExpressionSymbol)slack).Polynomial.Monomials.Should().BeEmpty();
        ((LinearExpressionSymbol)slack).Polynomial.Constant.ToDouble().Should().BeApproximately(0.0, 1e-12);
    }

    /// <summary>**允许提前 → 只有 `neg`，没有 `pos`** / Advance only → `neg` exists, `pos` does not.</summary>
    /// <remarks>
    /// 辅助变量落在**变量表**里，不在符号表里。看错表会让"变量确实建了"这条判据永远为假——而它看起来只是"没建"。
    ///
    /// The helper variables live in the **variable** table, not the symbol table. Reading the wrong table makes "the variable was built" permanently false — and it merely looks
    /// "not built".
    /// </remarks>
    [Fact]
    public void Slack_ShouldCarryOnlyTheNegativePartWhenOnlyAdvanceIsAllowed() {
        TimeWindow<Flt64> window = Window();
        var tasks = new List<TestTask> { TaskAt(window, "T1", 3, 2, advance: true) };
        TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy> taskTime = Time(window, tasks, advance: true);
        using var model = new LinearMetaModel<Flt64>();

        taskTime.Register(model).IsOk.Should().BeTrue();

        object slack = taskTime.EstimateSlack![tasks[0]];
        slack.Should().BeOfType<LinearFunctionSymbolAdapter<Flt64>>();
        var function = (SlackFunction<Flt64>)((LinearFunctionSymbolAdapter<Flt64>)slack).Function;
        function.NegVar.Should().NotBeNull("advance means the start may run earlier than planned");
        function.NegVar!.Name.Should().Be("est_slack_T1_neg");
        function.PosVar.Should().BeNull("delay is not allowed, so there is no positive part");
        function.SideVar.Should().BeNull("one direction needs no branch variable");
    }

    /// <summary>**允许延迟 → 只有 `pos`** / Delay only → only `pos`.</summary>
    [Fact]
    public void Slack_ShouldCarryOnlyThePositivePartWhenOnlyDelayIsAllowed() {
        TimeWindow<Flt64> window = Window();
        var tasks = new List<TestTask> { TaskAt(window, "T1", 3, 2, delay: true) };
        TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy> taskTime = Time(window, tasks, delay: true);
        using var model = new LinearMetaModel<Flt64>();

        taskTime.Register(model).IsOk.Should().BeTrue();

        var function = (SlackFunction<Flt64>)((LinearFunctionSymbolAdapter<Flt64>)taskTime.EstimateSlack![tasks[0]]).Function;
        function.PosVar.Should().NotBeNull("delay means the start may run later than planned");
        function.PosVar!.Name.Should().Be("est_slack_T1_pos");
        function.NegVar.Should().BeNull("advance is not allowed, so there is no negative part");
    }

    /// <summary>**两个方向都开 → 三个变量，且走四行绝对值公式** / Both directions → three variables and the four-row absolute-value rows.</summary>
    [Fact]
    public void Slack_ShouldCarryBothPartsAndABranchWhenBothAreAllowed() {
        TimeWindow<Flt64> window = Window();
        var tasks = new List<TestTask> { TaskAt(window, "T1", 3, 2, advance: true, delay: true) };
        TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy> taskTime = Time(window, tasks, advance: true, delay: true);
        using var model = new LinearMetaModel<Flt64>();

        taskTime.Register(model).IsOk.Should().BeTrue();

        var function = (SlackFunction<Flt64>)((LinearFunctionSymbolAdapter<Flt64>)taskTime.EstimateSlack![tasks[0]]).Function;
        function.HelperVariables.Should().HaveCount(3);
        function.SideVar.Should().NotBeNull("two directions need the branch variable");
        function.SideVar!.Name.Should().Be("est_slack_T1_side");
    }

    /// <summary>**全局开关关掉时，任务级开关不生效** / A task-level flag does nothing while the global switch is off.</summary>
    /// <remarks>
    /// 代码写的是 `advanceEnabled && task.advanceEnabled`——**两个都要开**。写成 `||` 会让"任务允许提前"单独生效，
    /// 于是全局开关形同虚设；而结果只是多了几个变量，看不出错。
    ///
 /// This implementation writes `advanceEnabled && task.advanceEnabled` — **both must be on**. Writing `||` lets a task-level flag alone take effect, making the global switch
    /// pointless; the only visible trace is a few extra variables.
    /// </remarks>
    [Fact]
    public void Slack_ShouldIgnoreTheTaskFlagWhenTheGlobalSwitchIsOff() {
        TimeWindow<Flt64> window = Window();
        var tasks = new List<TestTask> { TaskAt(window, "T1", 3, 2, advance: true, delay: true) };
        TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy> taskTime = Time(window, tasks);
        using var model = new LinearMetaModel<Flt64>();

        taskTime.Register(model).IsOk.Should().BeTrue();

        taskTime.EstimateSlack![tasks[0]].Should().BeOfType<LinearExpressionSymbol>(
            "the global switch is off, so no slack variables are built");
    }

    /// <summary>**多个任务各自一套松弛变量** / Each task gets its own slack variables.</summary>
    [Fact]
    public void Slack_ShouldBePerTask() {
        TimeWindow<Flt64> window = Window();
        var tasks = new List<TestTask> {
            TaskAt(window, "T1", 3, 2, advance: true),
            TaskAt(window, "T2", 5, 1, delay: true),
        };
        TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy> taskTime = Time(window, tasks, advance: true, delay: true);
        using var model = new LinearMetaModel<Flt64>();

        taskTime.Register(model).IsOk.Should().BeTrue();

        var first = (SlackFunction<Flt64>)((LinearFunctionSymbolAdapter<Flt64>)taskTime.EstimateSlack![tasks[0]]).Function;
        var second = (SlackFunction<Flt64>)((LinearFunctionSymbolAdapter<Flt64>)taskTime.EstimateSlack[tasks[1]]).Function;
        first.NegVar.Should().NotBeNull();
        first.PosVar.Should().BeNull();
        second.PosVar.Should().NotBeNull();
        second.NegVar.Should().BeNull();
        first.NegVar!.Name.Should().Be("est_slack_T1_neg");
        second.PosVar!.Name.Should().Be("est_slack_T2_pos");
    }
}
