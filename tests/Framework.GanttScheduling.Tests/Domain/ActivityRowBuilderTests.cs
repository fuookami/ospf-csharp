// 活动行构建的判据 / Criteria for the activity row building.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Activity.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.Domain;

/// <summary>
/// **活动行构建** / The activity row building.
/// </summary>
/// <remarks>
/// 判据的重点：**选择行上下界都是 1**、**冻结行只在冻结时产生且钉死那一个选择**、
/// **固定项进常数项而不是系数**、**依赖的端点由依赖自己指定**。
///
/// The criteria focus on **a selection row having both bounds at one**, on **a frozen row appearing only when frozen and pinning that one choice**, on **fixed terms going into the constant rather
/// than the coefficients**, and on **the dependency's endpoints coming from the dependency itself**.
/// </remarks>
public class ActivityRowBuilderTests {
    private static Quantity<Flt64> Q(double value) => new(new Flt64(value), SIBaseUnits.Meter);
    private static DateTimeOffset At(int hour) =>
        new(2026, 11, 9, hour, 0, 0, TimeSpan.FromHours(8.0));
    private static TimeRange Range(int startHour, int endHour) => new(At(startHour), At(endHour));

    private static CompiledActivityChoice<Flt64> Choice(string task, TimeRange? time) =>
        new(task,
            time is { } range ? new ActivityPlacement<Flt64>(
                new ActivityChoiceKey(task, "m", range.Start), range, Array.Empty<ActivityReservation<Flt64>>()) : null,
            Array.Empty<CompiledActivityReservation>(),
            new Flt64(1.0));

    private static CompiledActivityProblem<Flt64> Build(
        IReadOnlyList<CompiledActivityChoice<Flt64>> choices,
        IReadOnlyList<ActivityDependency>? dependencies = null,
        IReadOnlyList<ActivityBaseline<Flt64>>? baselines = null,
        IReadOnlyList<ActivityExecution<Flt64>>? executions = null) {
        var problem = new ActivitySchedulingProblem<Flt64>(
            Horizon: Range(0, 23),
            Activities: new List<Activity<Flt64>> {
                new("a1", new List<ActivityMode<Flt64>>()),
                new("a2", new List<ActivityMode<Flt64>>()),
            },
            Resources: new List<ActivityResource<Flt64>>(),
            Dependencies: dependencies,
            Baselines: baselines,
            Executions: executions,
            Rows: null,
            CostUnit: SIBaseUnits.Meter);
        return new CompiledActivityProblem<Flt64>(
            problem, choices,
            new Dictionary<string, CompiledActivityResource<Flt64>>(),
            Array.Empty<CompiledActivityReservation>(),
            Array.Empty<CompiledActivityRow>(),
            new Dictionary<string, CompiledActivityMaterial<Flt64>>(),
            Array.Empty<CompiledActivityMaterialEvent>());
    }

    /// <summary>
    /// **选择行上下界都是 1；冻结行钉死那一个选择** / A selection row has both bounds at one, and a frozen row pins that one choice.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 两个活动 → **两条选择行**，每条的下界与上界**都是 1**（不是 `<= 1`）。
    /// 2. 冻结行**只有一列，系数 1，上下界都是 1**。
    /// 3. **未冻结的基线不产生行**——冻结才是约束。
    /// 4. 冻结的选择若不在 `Indices` 里（没有排程）→ **不产生行**，而不是造一条引用不存在列的行。
    ///
    /// Four paired points: two activities give **two selection rows**, each with **both bounds at one** rather than `<= 1`; a frozen baseline's row has **one column with coefficient one and both
    /// bounds at one**; **an unfrozen baseline produces no row**, since only frozenness constrains; and a frozen choice absent from `Indices` produces **no row** rather than one referencing a column
    /// that does not exist.
    /// </remarks>
    [Fact]
    public void Rows_ShouldPinSelectionAndOnlyFrozenBaselines() {
        var choices = new List<CompiledActivityChoice<Flt64>> {
            Choice("a1", Range(8, 10)),
            Choice("a2", Range(10, 12)),
        };
        var baselines = new List<ActivityBaseline<Flt64>> {
            new(new ActivityChoiceKey("a1", "m", At(8)), Frozen: true, StartChangeCost: Q(0.0), ModeChangeCost: Q(0.0)),
            new(new ActivityChoiceKey("a2", "m", At(10)), Frozen: false, StartChangeCost: Q(0.0), ModeChangeCost: Q(0.0)),
            new(new ActivityChoiceKey("missing", "m", At(1)), Frozen: true, StartChangeCost: Q(0.0), ModeChangeCost: Q(0.0)),
        };

        IReadOnlyList<CompiledActivityRow> rows = ActivitySchedulingRowBuilder.Rows(Build(choices, baselines: baselines));

        CompiledActivityRow first = rows[0];
        first.Id.Should().Be("selection_0");
        first.Lower.GetValueOrDefault().ToDouble().Should().BeApproximately(1.0, 1e-12);
        first.Upper.GetValueOrDefault().ToDouble().Should().BeApproximately(1.0, 1e-12);
        first.Coefficients.Should().ContainSingle().Which.Key.Should().Be(0);

        CompiledActivityRow second = rows[1];
        second.Coefficients.Should().ContainSingle().Which.Key.Should().Be(1);

        rows.Should().Contain(r => r.Id == "frozen_0");
        CompiledActivityRow frozen = rows.First(r => r.Id == "frozen_0");
        frozen.Coefficients.Should().ContainSingle();
        frozen.Coefficients[0].ToDouble().Should().BeApproximately(1.0, 1e-12);
        frozen.Lower.GetValueOrDefault().ToDouble().Should().BeApproximately(1.0, 1e-12);
        frozen.Upper.GetValueOrDefault().ToDouble().Should().BeApproximately(1.0, 1e-12);

        rows.Should().NotContain(r => r.Id == "frozen_1", "an unfrozen baseline constrains nothing");
        rows.Should().NotContain(r => r.Id == "frozen_2", "a frozen choice without a column cannot be pinned");
    }

    /// <summary>
    /// **固定项进常数项，依赖端点由依赖自己指定** / Fixed terms go into the constant, and the dependency's endpoints come from the dependency.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 前驱是**已执行**的（固定，无决策列）→ 存在性行**没有前驱列**，固定项进 `Upper`。
    /// 2. 后继有两个选择 → 存在性行有**两列**，系数各 `-1`。
    /// 3. `Upper = -(1 - 0) = -1`：固定前驱存在时，后继至少选一个。
    /// 4. **端点由 `From`/`To` 决定**：`Finish→Start` 与 `Start→Start` 对同一对时段给出不同结论。
    ///
    /// Four paired points: a **fixed** predecessor (executed, no column) leaves the presence row **without a predecessor column** and puts the fixed term into `Upper`; the successor's two choices give
    /// **two columns** with coefficient `-1` each; `Upper = -(1 - 0) = -1`, meaning at least one successor must be chosen when the fixed predecessor is present; and **the endpoints come from
    /// `From`/`To`**, so `Finish→Start` and `Start→Start` reach different conclusions about the same pair of ranges.
    /// </remarks>
    [Fact]
    public void PresenceRows_ShouldPutFixedTermsInTheBound() {
        var choices = new List<CompiledActivityChoice<Flt64>> {
            Choice("a2", Range(10, 12)),
            Choice("a2", Range(14, 16)),
        };
        var executions = new List<ActivityExecution<Flt64>> {
            new(new ActivityPlacement<Flt64>(
                new ActivityChoiceKey("a1", "m", At(8)), Range(8, 9),
                Array.Empty<ActivityReservation<Flt64>>()), ActivityExecutionStatus.Completed),
        };
        var dependencies = new List<ActivityDependency> {
            new(Predecessor: "a1", Successor: "a2",
                From: ActivityEndpoint.Finish, To: ActivityEndpoint.Start,
                Presence: ActivityDependencyPresence.RequirePredecessor),
        };

        IReadOnlyList<CompiledActivityRow> rows = ActivitySchedulingRowBuilder.Rows(
            Build(choices, dependencies: dependencies, executions: executions));

        CompiledActivityRow presence = rows.First(r => r.Id == "presence_0_predecessor");
        presence.Coefficients.Should().HaveCount(2, "the fixed predecessor has no column of its own");
        // 方向是"后继被前驱上界"：`sum(后继) - sum(前驱) <= -(后继固定 - 前驱固定)`，
        // 固定前驱（1 个）给出 `succ1 + succ2 <= 1`——前驱已排时后继最多选一个。
        // The direction bounds the successors by the predecessors: `sum(successor) - sum(predecessor) <= -(fixedSucc - fixedPred)`,
        // and one fixed predecessor gives `succ1 + succ2 <= 1`, so at most one successor is chosen once the predecessor is placed.
        presence.Coefficients[0].ToDouble().Should().BeApproximately(1.0, 1e-12);
        presence.Coefficients[1].ToDouble().Should().BeApproximately(1.0, 1e-12);
        presence.Upper.GetValueOrDefault().ToDouble().Should().BeApproximately(1.0, 1e-12);
        presence.Lower.Should().BeNull();

        // **`RequireBoth` 的第二条行方向相反**，此时 `from` 侧是固定的前驱，
        // 固定项必须进常数项——把它写成系数会引用一个不存在的列。
        // **The second `RequireBoth` row runs the other way**, and there the fixed predecessor sits on the `from` side,
        // so the fixed term must go into the constant; writing it as a coefficient would reference a column that does not exist.
        var both = new ActivityDependency("a1", "a2",
            ActivityEndpoint.Finish, ActivityEndpoint.Start,
            Presence: ActivityDependencyPresence.RequireBoth);
        IReadOnlyList<CompiledActivityRow> bothRows = ActivitySchedulingRowBuilder.Rows(
            Build(choices, dependencies: new List<ActivityDependency> { both }, executions: executions));
        CompiledActivityRow reverse = bothRows.First(r => r.Id == "presence_0_successor");
        reverse.Coefficients.Should().HaveCount(2, "the fixed predecessor still contributes no column");
        reverse.Coefficients.Values.Should().OnlyContain(v => v.ToDouble() == -1.0);
        reverse.Upper.GetValueOrDefault().ToDouble().Should().BeApproximately(-1.0, 1e-12,
            "-(fixedFrom - fixedTo) = -(1 - 0), the fixed side now sitting in the `from` list");

        // 端点由依赖决定：Finish(9) → Start(10) 的 lag 是 1 小时，满足 minimum=0；
        // 但 Start(8) → Start(10) 的 lag 是 2 小时，也不违反；把 minimum 提到 2 小时才分得开。
        // The endpoints come from the dependency: Finish(9) to Start(10) is a one-hour lag, satisfying minimum=0,
        // while Start(8) to Start(10) is two hours; raising minimum to two hours separates them.
        var finishToStart = new ActivityDependency("a1", "a2",
            ActivityEndpoint.Finish, ActivityEndpoint.Start, Minimum: TimeSpan.FromHours(2.0));
        var startToStart = new ActivityDependency("a1", "a2",
            ActivityEndpoint.Start, ActivityEndpoint.Start, Minimum: TimeSpan.FromHours(2.0));
        CompiledActivityProblem<Flt64>.Compatible(finishToStart, Range(8, 9), Range(10, 12))
            .Should().BeFalse("9 to 10 is one hour, short of the two-hour minimum");
        CompiledActivityProblem<Flt64>.Compatible(startToStart, Range(8, 9), Range(10, 12))
            .Should().BeTrue("8 to 10 is two hours, meeting it");

        var noUpper = new ActivityDependency("a1", "a2",
            ActivityEndpoint.Finish, ActivityEndpoint.Start, Maximum: null);
        CompiledActivityProblem<Flt64>.Compatible(noUpper, Range(8, 9), Range(20, 22))
            .Should().BeTrue("a null maximum is no upper bound, not a zero one");

        // **上界真的会被检查**：同一个 lag（1 小时）在 maximum=30 分钟时不可接受。
        // **The upper bound is really checked**: the same one-hour lag is unacceptable at a 30-minute maximum.
        var tightUpper = new ActivityDependency("a1", "a2",
            ActivityEndpoint.Finish, ActivityEndpoint.Start, Maximum: TimeSpan.FromMinutes(30.0));
        CompiledActivityProblem<Flt64>.Compatible(tightUpper, Range(8, 9), Range(10, 12))
            .Should().BeFalse("one hour exceeds the thirty-minute maximum");
    }
}
