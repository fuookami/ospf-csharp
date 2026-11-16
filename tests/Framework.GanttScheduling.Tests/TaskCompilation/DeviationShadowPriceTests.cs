// 偏移约束两侧的行键与对偶回填/ Row keys and dual refresh on both deviation sides.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service.Limits;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Tests.BunchCompilation;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;
using LinearCategory = Fuookami.Ospf.Math.Symbol.LinearCategory;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.TaskCompilation;

/// <summary>
/// **偏移约束两侧的行键与对偶回填** / Row keys and dual refresh on both deviation sides.
/// </summary>
/// <remarks>
/// `TaskAdvanceTimeConstraint` / `TaskDelayTimeConstraint` 的 `Extractor` 一直都在，`Refresh` 一直是返回空 `Ok` 的桩，
/// 而**行上根本没有键**——三段链断了两段。于是提取器读一张永远空的表，列生成按零价格挑列**且不报错**。
///
/// 这里只修两处：行挂键、`Refresh` 按键收价。判据分开钉住两段，任何一段断掉都要失败。
///
/// Both `TaskAdvanceTimeConstraint` and `TaskDelayTimeConstraint` had `Extractor` all along, `Refresh` was a stub returning an empty `Ok`, and **the rows carried no key at all** — two of the three
/// links were broken. The extractor therefore read a forever-empty table and column generation selected at zero prices **without raising**. fixes exactly two things: keys on the rows and
/// `Refresh` collecting by key. The criteria pin each link separately so that breaking either one fails.
/// </remarks>
public class DeviationShadowPriceTests {
    private const string AdvanceName = "task_advance_time";
    private const string DelayName = "task_delay_time";
    private static readonly DateTimeOffset Origin = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static (List<TestTask> Tasks, List<TestExecutor> Executors,
        TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy> TaskTime, TimeWindow<Flt64> Window)
        Prepare() {
        var executors = new List<TestExecutor> { new("E1", "E1") };
        var window = new TimeWindow<Flt64>(
            new TimeRange(Origin, Origin.AddHours(24)), true, TimeSpan.FromHours(1));
        var tasks = new List<TestTask> {
            new("T1", "T1") {
                Time = new TimeRange(Origin.AddHours(2), Origin.AddHours(4)),
                ScheduledTime = new TimeRange(Origin.AddHours(2), Origin.AddHours(4)),
                AdvanceEnabled = true,
                DelayEnabled = true,
            },
            new("T2", "T2") {
                Time = new TimeRange(Origin.AddHours(3), Origin.AddHours(5)),
                ScheduledTime = new TimeRange(Origin.AddHours(3), Origin.AddHours(5)),
                AdvanceEnabled = true,
                DelayEnabled = true,
            },
        };
        foreach (TestTask task in tasks) {
            task.EnabledExecutorSet.Add(executors[0]);
        }
        var compilation = new TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy>(tasks, executors);
        // **全局偏移开关必须关着**：`DeviationConstraintRows.Add` 在全局开关开着时只保留**不允许移动**的任务，
        // 而这里两个任务都可移动，于是开关开着时一行都不生成。要拿到行，只能让"可移动"与"全局关"同时成立。
        // **The global deviation switch must stay off**: with it on, `DeviationConstraintRows.Add` keeps only tasks that may **not** move, and both tasks here may move, so no rows would be emitted.
        // Rows only appear when "may move" and "global off" hold together.
        var taskTime = new TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy>(
            window, tasks, compilation, advanceEnabled: false, delayEnabled: false);
        return (tasks, executors, taskTime, window);
    }

    private static IReadOnlyList<MathConstraint> Rows(LinearMetaModel<Flt64> model, string prefix) =>
        ((IMetaModel<Flt64>)model).Constraints
            .Where(row => row.Name.StartsWith(prefix, StringComparison.Ordinal))
            .ToList();

    /// <summary>
    /// **第一段：行上带键，两侧各用自己的键类型** / Link one: the rows carry keys, each side its own type.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 两条管线的行都**非空**（否则后面的"每行都带键"是空真）。
    /// 2. 提前侧每行都是 `TaskAdvanceTimeShadowPriceKey`，**没有**一个是延后侧的键。
    /// 3. 延后侧反之。
    /// 4. 键里的任务与行名末尾的任务 `Id` 对应——键挂错了任务，价格就会算到别人头上。
    ///
    /// Four paired points: both pipelines produce **non-empty** rows (otherwise "every row has a key" is vacuously true); every advance row is keyed `TaskAdvanceTimeShadowPriceKey` with **none** of the
    /// delay type; the delay side mirrors that; and the task inside the key matches the task `Id` at the end of the row name, since a key on the wrong task would bill someone else.
    /// </remarks>
    [Fact]
    public void BothDeviationSides_ShouldCarryTheirOwnKeyOnTheRow() {
        (List<TestTask> tasks, List<TestExecutor> _,
            TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy> taskTime,
            TimeWindow<Flt64> window) = Prepare();
        using var model = new LinearMetaModel<Flt64>();
        taskTime.Register(model).IsOk.Should().BeTrue();

        new TaskAdvanceTimeConstraint<TestExecutor, TestAssignmentPolicy>(window, tasks, taskTime)
            .Invoke(model).IsOk.Should().BeTrue();
        new TaskDelayTimeConstraint<TestExecutor, TestAssignmentPolicy>(window, tasks, taskTime)
            .Invoke(model).IsOk.Should().BeTrue();

        IReadOnlyList<MathConstraint> advanceRows = Rows(model, AdvanceName);
        advanceRows.Should().NotBeEmpty();
        advanceRows.Should().OnlyContain(row => row.Args is TaskAdvanceTimeShadowPriceKey<TestExecutor, TestAssignmentPolicy>);
        advanceRows.Should().NotContain(row => row.Args is TaskDelayTimeShadowPriceKey<TestExecutor, TestAssignmentPolicy>);

        IReadOnlyList<MathConstraint> delayRows = Rows(model, DelayName);
        delayRows.Should().NotBeEmpty();
        delayRows.Should().OnlyContain(row => row.Args is TaskDelayTimeShadowPriceKey<TestExecutor, TestAssignmentPolicy>);
        delayRows.Should().NotContain(row => row.Args is TaskAdvanceTimeShadowPriceKey<TestExecutor, TestAssignmentPolicy>);

        foreach (MathConstraint row in advanceRows) {
            var key = (TaskAdvanceTimeShadowPriceKey<TestExecutor, TestAssignmentPolicy>)row.Args!;
            row.Name.Should().Be(AdvanceName + "_" + key.Task.Id);
        }
        foreach (MathConstraint row in delayRows) {
            var key = (TaskDelayTimeShadowPriceKey<TestExecutor, TestAssignmentPolicy>)row.Args!;
            row.Name.Should().Be(DelayName + "_" + key.Task.Id);
        }
    }

    /// <summary>
    /// **第二段：对偶解落表，提取器按任务取价，两侧不串** / Link two: the dual lands in the map, each extractor answers per task, and the two sides do not cross.
    /// </summary>
    /// <remarks>
    /// 五点成对：
    ///
    /// 1. 只喂**提前侧**一行的对偶 → 提前侧映射里有那个价。
    /// 2. 延后侧的 `Refresh` 跑在只含提前行键的对偶上 → **不写**任何东西（键类型不同）。
    /// 3. 再喂**延后侧**一行 → 延后侧映射里有那个价，且没把提前侧的价覆盖掉。
    /// 4. 两侧提取器各自直接调用：认自己的任务得自己的价。
    /// 5. **没被定价的任务得零**——不能继承别人的价。
    ///
    /// Five paired points: feeding only an **advance** row's dual puts that price in the map; running the delay `Refresh` over a dual that holds only advance keys writes **nothing** (different key types);
    /// then feeding a **delay** row gives the delay map its price without overwriting the advance one; each extractor called directly resolves its own task to its own price; and **a task that got no
    /// price reads zero** rather than inheriting another's.
    /// </remarks>
    [Fact]
    public void BothDeviationSides_ShouldTurnDualsIntoPricesWithoutCrossing() {
        (List<TestTask> tasks, List<TestExecutor> executors,
            TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy> taskTime,
            TimeWindow<Flt64> window) = Prepare();
        using var model = new LinearMetaModel<Flt64>();
        taskTime.Register(model).IsOk.Should().BeTrue();

        var advance = new TaskAdvanceTimeConstraint<TestExecutor, TestAssignmentPolicy>(window, tasks, taskTime);
        var delay = new TaskDelayTimeConstraint<TestExecutor, TestAssignmentPolicy>(window, tasks, taskTime);
        advance.Invoke(model).IsOk.Should().BeTrue();
        delay.Invoke(model).IsOk.Should().BeTrue();

        MathConstraint advanceRow = Rows(model, AdvanceName)[0];
        MathConstraint delayRow = Rows(model, DelayName)[0];
        var advanceKey = (TaskAdvanceTimeShadowPriceKey<TestExecutor, TestAssignmentPolicy>)advanceRow.Args!;
        var delayKey = (TaskDelayTimeShadowPriceKey<TestExecutor, TestAssignmentPolicy>)delayRow.Args!;

        var map = new GanttSchedulingShadowPriceMap<TestExecutor, TestAssignmentPolicy>();
        advance.Refresh(map, model, Duals((advanceRow, 7.25))).IsOk.Should().BeTrue();
        map.Map.Should().ContainKey(advanceKey).WhoseValue.Price.ToDouble().Should().BeApproximately(7.25, 1e-9);

        // **用一个不同的价**：同一行同一个价被"认领"也不会改变表内容，断言会漏。
        // **Use a different price**: claiming the same row with the same price leaves the map unchanged, and the assertion would miss it.
        delay.Refresh(map, model, Duals((advanceRow, 99.0))).IsOk.Should().BeTrue();
        map.Map.Should().HaveCount(1, "the delay side must not claim the advance row");
        map.Map[advanceKey].Price.ToDouble().Should().BeApproximately(7.25, 1e-9,
            "the advance price must survive the delay refresh untouched");

        delay.Refresh(map, model, Duals((delayRow, 1.75))).IsOk.Should().BeTrue();
        map.Map.Should().HaveCount(2);
        map.Map[delayKey].Price.ToDouble().Should().BeApproximately(1.75, 1e-9);
        map.Map[advanceKey].Price.ToDouble().Should().BeApproximately(7.25, 1e-9, "the advance price is not overwritten");

        // **反方向也要探一次**：只喂延后行给提前侧，提前侧不许认领。只探一个方向会让另一侧的"认领所有行"存活。
        // **Probe the reverse direction too**: feed only the delay row to the advance side, which must not claim it. Probing one direction only lets the other side's "claim every row" survive.
        advance.Refresh(map, model, Duals((delayRow, 42.0))).IsOk.Should().BeTrue();
        map.Map.Should().HaveCount(2, "the advance side must not claim the delay row");
        map.Map[delayKey].Price.ToDouble().Should().BeApproximately(1.75, 1e-9,
            "the delay price must survive the advance refresh untouched");

        advance.Extractor()!(map, new TaskGanttSchedulingShadowPriceArguments<TestExecutor, TestAssignmentPolicy>(executors[0], advanceKey.Task))
            .ToDouble().Should().BeApproximately(7.25, 1e-9);
        delay.Extractor()!(map, new TaskGanttSchedulingShadowPriceArguments<TestExecutor, TestAssignmentPolicy>(executors[0], delayKey.Task))
            .ToDouble().Should().BeApproximately(1.75, 1e-9);

        TestTask unpriced = tasks.Single(task => !ReferenceEquals(task, advanceKey.Task));
        advance.Extractor()!(map, new TaskGanttSchedulingShadowPriceArguments<TestExecutor, TestAssignmentPolicy>(executors[0], unpriced))
            .ToDouble().Should().BeApproximately(0, 1e-9, "a task that got no price must not inherit another task's");
    }

    private static MetaDualSolution Duals(params (MathConstraint Row, double Price)[] rows) =>
        new(
            rows.ToDictionary(entry => entry.Row, entry => new Flt64(entry.Price)),
            new Dictionary<IIntermediateSymbol, IReadOnlyList<(IConstraint<Flt64, LinearCategory> Constraint, Flt64 Price)>>());
}
