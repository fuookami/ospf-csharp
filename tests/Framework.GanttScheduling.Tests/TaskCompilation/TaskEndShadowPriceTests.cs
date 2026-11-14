// 对偶回程判据：行上的键 → 对偶解 → 影子价格表 → 提取器 / The dual round trip: row key → dual solution → price map → extractor.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Comparison = Fuookami.Ospf.Math.Symbol.Inequality.Comparison;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service.Limits;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Tests.BunchCompilation;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.TaskCompilation;

/// <summary>
/// **影子价格的回程（B571）** / The shadow-price round trip (B571).
/// </summary>
/// <remarks>
/// "对偶 → 价格 → 约简成本"这条链有三段：**行上带键**、**从对偶解认回该行**、**按任务取价**。
/// 之前三段全断：行上没有 `args`、`Refresh` 返回 `Ok` 不动表、提取器读一张永远空的表。
/// 于是约简成本恒等于列自身的成本——列生成看起来在跑，其实按零价格在挑列，而且**不报错**。
///
/// 本批把延后/提前两侧的结束时间约束接通，并**分段**验证：行上有没有键、价格有没有落表、提取器按任务对不对得上。
///
/// The chain "dual → price → reduced cost" has three links: **the row carries a key**, **the pipeline recognises that row in the dual solution**, and **the extractor resolves the
/// price by task**. All three were broken: rows carried no `args`, `Refresh` returned `Ok` without touching the map, and the extractor read a forever-empty table. Reduced cost
/// then degenerated to the column's own cost — column generation looked like it ran, selected at zero prices, and **raised nothing**. This batch wires the delay/advance end-time
/// constraints and verifies each link **separately**: is there a key on the row, did the price land in the map, and does the extractor answer per task.
/// </remarks>
public class TaskEndShadowPriceTests {
    private const string AdvanceName = "task_advance_earliest_end_time";
    private const string DelayName = "task_delay_last_end_time";
    private static readonly DateTimeOffset Origin = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static (List<TestTask> Tasks, List<TestExecutor> Executors,
        TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy> Compilation,
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
        return (tasks, executors, compilation, taskTime, window);
    }

    /// <summary>
    /// **第一段：行上带键** / Link one: the row carries a key.
    /// </summary>
    /// <remarks>
    /// 键必须**落在模型的行上**，不是留在管线里。管线自己记一份键没有用：对偶是由求解器按行返回的，管线要认回那一行只能靠行上带的键。
    /// 成对检查：有目标结束时间的任务有键，且键里的任务就是那个任务；行数与非空键数一致（不能多也不能少）。
    ///
    /// The key must **land on the model's row**, not stay inside the pipeline: duals come back from the solver per row, so recognising its own row requires the key on the row.
    /// Paired check: tasks with a target end get a key naming that very task, and the number of keys equals the number of rows (neither more nor fewer).
    /// </remarks>
    [Fact]
    public void AdvanceConstraint_ShouldCarryItsKeyOnTheRow() {
        (List<TestTask> tasks, List<TestExecutor> _, TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy> compilation,
            TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy> taskTime, TimeWindow<Flt64> window) = Prepare();
        LinearMetaModel<Flt64> model = new();
        compilation.Register(model).IsOk.Should().BeTrue();
        taskTime.Register(model).IsOk.Should().BeTrue();
        var pipeline = new TaskAdvanceEarliestEndTimeConstraint<TestExecutor, TestAssignmentPolicy>(
            window, tasks, taskTime);

        pipeline.Invoke(model).IsOk.Should().BeTrue();

        IReadOnlyList<MathConstraint> rows = pipelineConstraints(model, AdvanceName);
        rows.Should().NotBeEmpty();
        rows.Should().OnlyContain(row => row.Args is TaskAdvanceEarliestEndTimeShadowPriceKey<TestExecutor, TestAssignmentPolicy>);
        rows.Select(row => ((TaskAdvanceEarliestEndTimeShadowPriceKey<TestExecutor, TestAssignmentPolicy>)row.Args!).Task)
            .Should().BeEquivalentTo(tasks.Where(task => task.EarliestEndTime is not null));
    }

    /// <summary>
    /// **第二段 + 第三段：对偶解落表，按任务取价** / Links two and three: the dual lands in the map, the extractor answers per task.
    /// </summary>
    /// <remarks>
    /// 喂一份**手工构造**的对偶解：本管线的行给 4.5，别的管线的行给 99。要求本行落表为 4.5、别的行**不进**这张表，
    /// 并且 `map.Invoke(args)`（走全部已注册提取器）对 T1 得 4.5、对没被定价的 T2 得 0。
    /// 只查"表非空"会让"把别人的价格也塞进来"通过。
    ///
    /// Feeds a **hand-built** dual solution: 4.5 for this pipeline's row and 99 for a foreign row. The map must hold 4.5, must **not** take the foreign row, and
    /// `map.Invoke(args)` (through the registered extractors) must give 4.5 for T1 and 0 for the unpriced T2. "The map is non-empty" alone would pass if foreign prices leaked in.
    /// </remarks>
    [Fact]
    public void AdvanceConstraint_ShouldTurnDualsIntoPricesPerTask() {
        (List<TestTask> tasks, List<TestExecutor> executors,
            TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy> compilation,
            TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy> taskTime, TimeWindow<Flt64> window) = Prepare();
        var model = new LinearMetaModel<Flt64>();
        compilation.Register(model).IsOk.Should().BeTrue();
        taskTime.Register(model).IsOk.Should().BeTrue();
        var pipeline = new TaskAdvanceEarliestEndTimeConstraint<TestExecutor, TestAssignmentPolicy>(
            window, tasks, taskTime);
        pipeline.Invoke(model).IsOk.Should().BeTrue();

        MathConstraint ownRow = pipelineConstraints(model, AdvanceName)[0];
        var foreignKey = new ExecutorCompilationShadowPriceKey<TestExecutor>(executors[0]);
        MathConstraint foreignRow = new LinearInequalityConstraint<Flt64>(
            new LinearInequality<Flt64>(
                new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero),
                new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero),
                Comparison.LE,
                "foreign_row"),
            args: foreignKey);
        var duals = new MetaDualSolution(
            new Dictionary<MathConstraint, Flt64> { [ownRow] = new Flt64(4.5), [foreignRow] = new Flt64(99) },
            new Dictionary<IIntermediateSymbol, IReadOnlyList<(IConstraint<Flt64, LinearCategory> Constraint, Flt64 Price)>>());
        var map = new GanttSchedulingShadowPriceMap<TestExecutor, TestAssignmentPolicy>();

        pipeline.Refresh(map, model, duals).IsOk.Should().BeTrue();

        var ownKey = (TaskAdvanceEarliestEndTimeShadowPriceKey<TestExecutor, TestAssignmentPolicy>)ownRow.Args!;
        map.Map.Should().ContainKey(ownKey).WhoseValue.Price.ToDouble().Should().BeApproximately(4.5, 1e-9);
        map.Map.Should().NotContainKey(foreignKey, "another pipeline's row is not this pipeline's business");

        ShadowPriceExtractor<IGanttSchedulingShadowPriceArguments<TestExecutor, TestAssignmentPolicy>,
            GanttSchedulingShadowPriceMap<TestExecutor, TestAssignmentPolicy>> extractor = pipeline.Extractor()!;
        map.Put(extractor);
        map.Invoke(new TaskGanttSchedulingShadowPriceArguments<TestExecutor, TestAssignmentPolicy>(executors[0], tasks[0]))
            .ToDouble().Should().BeApproximately(4.5, 1e-9);
        map.Invoke(new TaskGanttSchedulingShadowPriceArguments<TestExecutor, TestAssignmentPolicy>(executors[0], tasks[1]))
            .ToDouble().Should().BeApproximately(0, 1e-9, "a row that got no price must not inherit another row's");
        map.Invoke(new TaskGanttSchedulingShadowPriceArguments<TestExecutor, TestAssignmentPolicy>(executors[0]))
            .ToDouble().Should().BeApproximately(0, 1e-9, "an executor-level query is not answered with a task price");
    }

    /// <summary>
    /// **延后侧对称接线** / The delay side is wired symmetrically.
    /// </summary>
    /// <remarks>
    /// 两侧共用 `EndConstraintRows`，但键类型不同。只测一侧会让"两边都用同一个键"通过——那时两侧的价格会互相串味。
    ///
    /// Both sides share `EndConstraintRows` but use different key types. Testing one side would pass even if both used the same key — their prices would then cross-contaminate.
    /// </remarks>
    [Fact]
    public void DelayConstraint_ShouldUseItsOwnKeyType() {
        (List<TestTask> tasks, List<TestExecutor> executors,
            TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy> compilation,
            TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy> taskTime, TimeWindow<Flt64> window) = Prepare();
        LinearMetaModel<Flt64> model = new();
        compilation.Register(model).IsOk.Should().BeTrue();
        taskTime.Register(model).IsOk.Should().BeTrue();
        var pipeline = new TaskDelayLastEndTimeConstraint<TestExecutor, TestAssignmentPolicy>(
            window, tasks, taskTime);
        pipeline.Invoke(model).IsOk.Should().BeTrue();

        IReadOnlyList<MathConstraint> rows = pipelineConstraints(model, DelayName);
        rows.Should().NotBeEmpty();
        rows.Should().OnlyContain(row => row.Args is TaskDelayLastEndTimeShadowPriceKey<TestExecutor, TestAssignmentPolicy>);

        var duals = new MetaDualSolution(
            new Dictionary<MathConstraint, Flt64> { [rows[0]] = new Flt64(2.25) },
            new Dictionary<IIntermediateSymbol, IReadOnlyList<(IConstraint<Flt64, LinearCategory> Constraint, Flt64 Price)>>());
        var map = new GanttSchedulingShadowPriceMap<TestExecutor, TestAssignmentPolicy>();
        pipeline.Refresh(map, model, duals).IsOk.Should().BeTrue();

        Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model.IAbstractTask<TestExecutor, TestAssignmentPolicy> askTask =
            ((TaskDelayLastEndTimeShadowPriceKey<TestExecutor, TestAssignmentPolicy>)rows[0].Args!).Task;
        ShadowPriceExtractor<IGanttSchedulingShadowPriceArguments<TestExecutor, TestAssignmentPolicy>,
            GanttSchedulingShadowPriceMap<TestExecutor, TestAssignmentPolicy>> extractor = pipeline.Extractor()!;
        map.Put(extractor);
        map.Invoke(new TaskGanttSchedulingShadowPriceArguments<TestExecutor, TestAssignmentPolicy>(executors[0], askTask))
            .ToDouble().Should().BeApproximately(2.25, 1e-9);
    }

    ///
    /// <remarks>
    /// 按**名字前缀**取行：`EndConstraintRows.Add` 用 `group: null` 加行，所以它们不在管线的约束组里。
    /// 这不是缺陷（行名自带管线前缀），但判据必须按真实归属去找，否则会得出"一行都没加"的错误结论。
    ///
    /// Rows are found by **name prefix**: `EndConstraintRows.Add` adds them with `group: null`, so they are not in the pipeline's constraint group. That is not a defect (the row
    /// names carry the pipeline prefix), but a criterion must look them up the way they are actually attributed or it will wrongly conclude that nothing was added.
    /// </remarks>
    private static IReadOnlyList<MathConstraint> pipelineConstraints(
        LinearMetaModel<Flt64> model,
        string prefix) =>
        ((IMetaModel<Flt64>)model).Constraints.Where(row => row.Name.StartsWith(prefix, StringComparison.Ordinal)).ToList();
}
