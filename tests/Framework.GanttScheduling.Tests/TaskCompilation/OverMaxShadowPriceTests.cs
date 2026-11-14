// 超越上限两侧的影子价格提取器（B706）/ Shadow-price extractors for the two over-max sides (B706).

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
using LinearCategory = Fuookami.Ospf.Math.Symbol.LinearCategory;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.TaskCompilation;

/// <summary>
/// **超越上限两侧的提取器（B706）** / The two over-max extractors (B706).
/// </summary>
/// <remarks>
/// 这两条约束的行早已挂键、`Refresh` 也已按行的 `Args` 收价，**只有提取器没接线**——于是列生成按零价格挑列，
/// 而且不报错。有意思的是文件里原本写着"取价需要求解器的对偶通道（全局未接线项）"：这句话是**假的**，
/// 同一个目录里的 `TaskDelayTimeConstraint` 早就用 `map.Map` 取价了。
///
/// **"有理由地缺着"要能被证伪**：这两条的理由被一条同目录的反例直接推翻。
///
/// These two constraints already carried keys on their rows and `Refresh` already collected prices by the row's `Args`; **only the extractor was unwired**, so column generation selected at zero prices
/// and raised nothing. Tellingly the file used to claim "reading prices needs the solver's dual channel (a global open item)" — that claim was **false**, since `TaskDelayTimeConstraint` in the same
/// directory had been reading `map.Map` all along. **A justified gap must be falsifiable**, and a counterexample in the same directory falsified this one.
/// </remarks>
public class OverMaxShadowPriceTests {
    private const string DelayName = "task_over_max_delay_time";
    private const string AdvanceName = "task_over_max_advance_time";
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
                EarliestEndTime = Origin.AddHours(3),
                LastEndTime = Origin.AddHours(5),
                AdvanceEnabled = true,
                DelayEnabled = true,
                MaxDelay = TimeSpan.FromHours(1),
                MaxAdvance = TimeSpan.FromHours(1),
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
        var taskTime = new TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy>(
            window, tasks, compilation, advanceEnabled: true, delayEnabled: true);
        // **全局超上限开关必须关着**：`OverMaxConstraintRows.Add` 里 `globalEnabled && mayMove` 会**跳过**可移动的任务，
        // 于是开关打开时一行都不生成。要拿到行，只能让"可移动"与"全局关"同时成立。
        // **The global over-max switch must stay off**: `OverMaxConstraintRows.Add` **skips** a movable task when `globalEnabled && mayMove`, so switching it on emits no rows at all. Rows only appear when
        // "may move" and "global off" hold together.
        return (tasks, executors, taskTime, window);
    }

    private static IReadOnlyList<MathConstraint> Rows(LinearMetaModel<Flt64> model, string prefix) =>
        ((IMetaModel<Flt64>)model).Constraints
            .Where(row => row.Name.StartsWith(prefix, StringComparison.Ordinal))
            .ToList();

    /// <summary>
    /// **行上必须带键** / The rows must carry keys.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 本批之前行是**加了但不带键**的——`OverMaxConstraintRows.Add` 没传 `args`。那时提取器读一张永远空的表，
    ///    列生成按零价格挑列**且不报错**，因此"行数对不对"完全看不出问题。
    /// 2. 现在每一行都带本侧的键类型，键里的任务**就是**该行对应的任务。
    /// 3. 两侧的键类型**不同**——只用一侧的键类会让另一侧的价格串味。
    ///
    /// Three paired points: before this batch the rows were **added without a key** (no `args` from `OverMaxConstraintRows.Add`), so the extractor read a forever-empty table and column generation
    /// selected at zero prices **without raising**, which is why "is the row count right" could never reveal it; now every row carries its side's key type naming that very task; and the two sides'
    /// key types differ, since sharing one would cross-contaminate their prices.
    /// </remarks>
    [Fact]
    public void OverMaxRows_ShouldCarryTheirOwnKeys() {
        (List<TestTask> tasks, List<TestExecutor> _,
            TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy> taskTime,
            TimeWindow<Flt64> window) = Prepare();
        using var model = new LinearMetaModel<Flt64>();
        taskTime.Register(model).IsOk.Should().BeTrue();

        new TaskOverMaxDelayTimeConstraint<TestExecutor, TestAssignmentPolicy>(window, tasks, taskTime)
            .Invoke(model).IsOk.Should().BeTrue();
        new TaskOverMaxAdvanceTimeConstraint<TestExecutor, TestAssignmentPolicy>(window, tasks, taskTime)
            .Invoke(model).IsOk.Should().BeTrue();

        IReadOnlyList<MathConstraint> delayRows = Rows(model, DelayName);
        delayRows.Should().NotBeEmpty();
        delayRows.Should().OnlyContain(row => row.Args is TaskOverMaxDelayShadowPriceKey<TestExecutor, TestAssignmentPolicy>);
        delayRows.Should().NotContain(row => row.Args is TaskOverMaxAdvanceShadowPriceKey<TestExecutor, TestAssignmentPolicy>);

        IReadOnlyList<MathConstraint> advanceRows = Rows(model, AdvanceName);
        advanceRows.Should().NotBeEmpty();
        advanceRows.Should().OnlyContain(row => row.Args is TaskOverMaxAdvanceShadowPriceKey<TestExecutor, TestAssignmentPolicy>);
        advanceRows.Should().NotContain(row => row.Args is TaskOverMaxDelayShadowPriceKey<TestExecutor, TestAssignmentPolicy>);

        // 键里的任务就是该行名字末尾的那个任务的 `Id`。
        foreach (MathConstraint row in delayRows) {
            var key = (TaskOverMaxDelayShadowPriceKey<TestExecutor, TestAssignmentPolicy>)row.Args!;
            row.Name.Should().Be(DelayName + "_" + key.Task.Id);
        }
    }

    /// <summary>
    /// **超越上限的两侧都有提取器，且各用自己的键类型** / Both over-max sides have an extractor, each with its own key type.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 两条管线的 `Extractor()` 都**不为 `null`**——接线之前那个方法根本不存在，这是最直接的证伪。
    /// 2. 各自的**行**挂各自的键类型（延后侧 `TaskOverMaxDelayShadowPriceKey`、提前侧 `TaskOverMaxAdvanceShadowPriceKey`）。
    /// 3. 对偶值落表后，`map.Invoke(该任务)` 得**那个价**。
    /// 4. 另一侧**拿不到**这个价：两侧键类型不同，所以把价写进延后表之后，提前侧的查询是零。
    ///    只测一侧会让"两边共用同一个键"通过。
    ///
    /// Four paired points: both pipelines' `Extractor()` are **not null** — the method did not exist at all before this batch, which is the most direct falsification; each side's **rows** carry its own
    /// key type; after the dual lands in the map, `map.Invoke(thatTask)` returns **that price**; and the other side **cannot** see it, since the key types differ, so a price written on the delay side reads
    /// zero on the advance side. Testing one side would pass even if both shared one key.
    /// </remarks>
    [Fact]
    public void BothOverMaxSides_ShouldResolveTheirOwnPrice() {
        (List<TestTask> tasks, List<TestExecutor> executors,
            TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy> taskTime,
            TimeWindow<Flt64> window) = Prepare();
        using var model = new LinearMetaModel<Flt64>();
        taskTime.Register(model).IsOk.Should().BeTrue();

        var delay = new TaskOverMaxDelayTimeConstraint<TestExecutor, TestAssignmentPolicy>(window, tasks, taskTime);
        var advance = new TaskOverMaxAdvanceTimeConstraint<TestExecutor, TestAssignmentPolicy>(window, tasks, taskTime);
        delay.Extractor().Should().NotBeNull("B706 wires it; before that the method did not exist");
        advance.Extractor().Should().NotBeNull();

        delay.Invoke(model).IsOk.Should().BeTrue();
        advance.Invoke(model).IsOk.Should().BeTrue();

        IReadOnlyList<MathConstraint> delayRows = Rows(model, DelayName);
        delayRows.Should().NotBeEmpty();
        delayRows.Should().OnlyContain(row => row.Args is TaskOverMaxDelayShadowPriceKey<TestExecutor, TestAssignmentPolicy>);
        Rows(model, AdvanceName).Should().OnlyContain(
            row => row.Args is TaskOverMaxAdvanceShadowPriceKey<TestExecutor, TestAssignmentPolicy>);

        MathConstraint row = delayRows[0];
        var key = (TaskOverMaxDelayShadowPriceKey<TestExecutor, TestAssignmentPolicy>)row.Args!;
        var duals = new MetaDualSolution(
            new Dictionary<MathConstraint, Flt64> { [row] = new Flt64(6.5) },
            new Dictionary<IIntermediateSymbol, IReadOnlyList<(IConstraint<Flt64, LinearCategory> Constraint, Flt64 Price)>>());
        var map = new GanttSchedulingShadowPriceMap<TestExecutor, TestAssignmentPolicy>();
        delay.Refresh(map, model, duals).IsOk.Should().BeTrue();
        map.Map.Should().ContainKey(key).WhoseValue.Price.ToDouble().Should().BeApproximately(6.5, 1e-9);

        map.Put(delay.Extractor()!);
        map.Invoke(new TaskGanttSchedulingShadowPriceArguments<TestExecutor, TestAssignmentPolicy>(executors[0], key.Task))
            .ToDouble().Should().BeApproximately(6.5, 1e-9);

        // **提前侧也要真的回填**：只喂延后侧的对偶，会让"提前侧 `Refresh` 什么都不写"照样通过。
        // **The advance side must really be filled too**: feeding only the delay dual would let "the advance `Refresh` writes nothing" pass.
        MathConstraint advanceRow = Rows(model, AdvanceName)[0];
        var advanceKey = (TaskOverMaxAdvanceShadowPriceKey<TestExecutor, TestAssignmentPolicy>)advanceRow.Args!;
        delay.Refresh(map, model, new MetaDualSolution(
            new Dictionary<MathConstraint, Flt64> { [advanceRow] = new Flt64(9.0) },
            new Dictionary<IIntermediateSymbol, IReadOnlyList<(IConstraint<Flt64, LinearCategory> Constraint, Flt64 Price)>>()))
            .IsOk.Should().BeTrue();
        // 延后侧不认提前侧的键：这里用**提前侧管线**才该认回来，所以先确认延后侧没写进去。
        map.Map.Should().NotContainKey(advanceKey, "the delay pipeline must not claim the advance row");
        advance.Refresh(map, model, new MetaDualSolution(
            new Dictionary<MathConstraint, Flt64> { [advanceRow] = new Flt64(9.0) },
            new Dictionary<IIntermediateSymbol, IReadOnlyList<(IConstraint<Flt64, LinearCategory> Constraint, Flt64 Price)>>()))
            .IsOk.Should().BeTrue();
        map.Map.Should().ContainKey(advanceKey).WhoseValue.Price.ToDouble().Should().BeApproximately(9.0, 1e-9);
        // **直接调委托，不走 `map.Invoke`**：`Invoke` 会把**所有**已注册提取器的结果相加，
        // 两条提取器都认同一个任务时相加得 15.5——那是聚合的语义，不是本文要看的"这条提取器认自己的键"。
        // **Call the delegate directly rather than `map.Invoke`**: `Invoke` sums **every** registered extractor, so with both extracting for the same task it returns 15.5 — that is the aggregation
        // semantics, not what this criterion is about, namely "this extractor resolves its own key".
        advance.Extractor()!(map, new TaskGanttSchedulingShadowPriceArguments<TestExecutor, TestAssignmentPolicy>(executors[0], advanceKey.Task))
            .ToDouble().Should().BeApproximately(9.0, 1e-9, "the advance extractor resolves its own key");
        TestTask unpriced = tasks.Single(task => !ReferenceEquals(task, key.Task));
        advance.Extractor()!(map, new TaskGanttSchedulingShadowPriceArguments<TestExecutor, TestAssignmentPolicy>(executors[0], unpriced))
            .ToDouble().Should().BeApproximately(0, 1e-9, "a task that got no price must not inherit another task's");
        delay.Extractor()!(map, new TaskGanttSchedulingShadowPriceArguments<TestExecutor, TestAssignmentPolicy>(executors[0], key.Task))
            .ToDouble().Should().BeApproximately(6.5, 1e-9, "the delay price is still there");

        // **另一侧拿不到这个价**：价格在延后侧的键上，提前侧的查询走的是自己的键。
        // **The other side cannot see it**: the price sits on the delay key while the advance query uses its own.
        map.Invoke(new TaskGanttSchedulingShadowPriceArguments<TestExecutor, TestAssignmentPolicy>(executors[0]))
            .ToDouble().Should().BeApproximately(0, 1e-9, "an executor-level query is not answered with a task price");
        map.Invoke(new TaskGanttSchedulingShadowPriceArguments<TestExecutor, TestAssignmentPolicy>(executors[0], unpriced))
            .ToDouble().Should().BeApproximately(0, 1e-9, "a task that got no price must not inherit another task's");
    }
}
