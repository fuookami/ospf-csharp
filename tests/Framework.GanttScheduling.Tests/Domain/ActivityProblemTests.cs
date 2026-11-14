// 活动排程问题（B686）的判据 / Criteria for the activity scheduling problem (B686).

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Activity.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.Domain;

/// <summary>
/// **活动排程问题（B686）** / The activity scheduling problem (B686).
/// </summary>
/// <remarks>
/// 判据的重点：**除四项必填外全部可省且访问器返回空**、**行的上下界各自可空**、
/// **冻结与改期成本互相独立**、**附加成本是委托而不是静态表**。
///
/// The criteria focus on **everything beyond the four mandatory fields being optional with empty accessors**, on **each row bound being nullable on its own**, on **frozenness and change cost
/// being independent**, and on **the additional cost being a delegate rather than a static table**.
/// </remarks>
public class ActivityProblemTests {
    private static Quantity<Flt64> Q(double value) => new(new Flt64(value), SIBaseUnits.Meter);
    private static TimeRange Horizon() =>
        new(new DateTimeOffset(2026, 11, 9, 0, 0, 0, TimeSpan.FromHours(8.0)),
            new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.FromHours(8.0)));

    private static Activity<Flt64> OneActivity() {
        var use = new ActivityResourceUse<Flt64>("m1", Q(1.0));
        var mode = new ActivityMode<Flt64>("m", TimeSpan.FromHours(1.0),
            new List<DateTimeOffset> { Horizon().Start }, new List<ActivityResourceUse<Flt64>> { use }, Q(10.0));
        return new Activity<Flt64>("a1", new List<ActivityMode<Flt64>> { mode });
    }

    /// <summary>
    /// **最小问题：四项必填，其余访问器为空** / A minimal problem needs four fields, with every other accessor empty.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 只给区间、活动、执行者、成本量纲 → 六个可选集合的访问器**全为空**。
    /// 2. `AdditionalCost` **为 null**（不是给一个恒返回 0 的默认实现）。
    /// 3. `ActivityRow` 只给下界时，**上界为 null**，不兜底成 0。
    /// 4. 行的系数与取消系数访问器为空字典。
    ///
    /// Four paired points: supplying only the horizon, activities, executors and cost unit leaves **all six optional-collection accessors empty**; `AdditionalCost` **is null** rather than a default
    /// returning zero; an `ActivityRow` with only a lower bound has **a null upper bound**, not zero; and the row's coefficient accessors are empty dictionaries.
    /// </remarks>
    [Fact]
    public void MinimalProblem_ShouldLeaveOptionalsEmpty() {
        var problem = new ActivitySchedulingProblem<Flt64>(
            Horizon: Horizon(),
            Activities: new List<Activity<Flt64>> { OneActivity() },
            Resources: new List<ActivityResource<Flt64>> { new("m1", Q(8.0)) },
            Dependencies: null,
            Baselines: null,
            Executions: null,
            Rows: null,
            CostUnit: SIBaseUnits.Meter);

        problem.DependenciesValue.Should().BeEmpty();
        problem.BaselinesValue.Should().BeEmpty();
        problem.ExecutionsValue.Should().BeEmpty();
        problem.RowsValue.Should().BeEmpty();
        problem.MaterialsValue.Should().BeEmpty();
        problem.MaterialEventsValue.Should().BeEmpty();
        problem.AdditionalCost.Should().BeNull("no additional cost is not the same as a zero cost");

        var row = new ActivityRow<Flt64>("cap", SIBaseUnits.Meter, Lower: Q(3.0));
        row.Upper.Should().BeNull("a one-sided row stays one-sided");
        row.Lower!.Value.ToFlt64().ToDouble().Should().BeApproximately(3.0, 1e-9);
        row.CoefficientsValue.Should().BeEmpty();
        row.CancellationCoefficientsValue.Should().BeEmpty();
    }

    /// <summary>
    /// **冻结与成本独立，附加成本是委托** / Frozenness is independent of cost, and the additional cost is a delegate.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 冻结的基线**仍带成本**（`0` 与 `5`），冻结不是"成本无穷大"。
    /// 2. 附加成本**按选择计算**：同一个委托对两个不同选择给出两个不同成本。
    /// 3. 委托为 null 与委托恒返回 0 是两回事——前者表示"没有附加成本"。
    ///
    /// Three paired points: a frozen baseline **still carries costs** (`0` and `5`), frozenness not being "infinite cost"; the additional cost **is computed per choice**, one delegate yielding two
    /// different costs for two different choices; and a null delegate differs from one returning zero, the former meaning there is no additional cost at all.
    /// </remarks>
    [Fact]
    public void BaselineAndAdditionalCost_ShouldStayExplicit() {
        var baseline = new ActivityBaseline<Flt64>(
            Choice: new ActivityChoiceKey("a1", "m", Horizon().Start),
            Frozen: true,
            StartChangeCost: Q(0.0),
            ModeChangeCost: Q(5.0));
        baseline.Frozen.Should().BeTrue();
        baseline.StartChangeCost.Value.ToFlt64().ToDouble().Should().BeApproximately(0.0, 1e-9,
            "frozen is a permission, not a price");
        baseline.ModeChangeCost.Value.ToFlt64().ToDouble().Should().BeApproximately(5.0, 1e-9);

        var problem = new ActivitySchedulingProblem<Flt64>(
            Horizon: Horizon(),
            Activities: new List<Activity<Flt64>> { OneActivity() },
            Resources: new List<ActivityResource<Flt64>> { new("m1", Q(8.0)) },
            Dependencies: null, Baselines: null, Executions: null, Rows: null,
            CostUnit: SIBaseUnits.Meter,
            AdditionalCost: placement => Results.Ok(new Quantity<Flt64>(
                new Flt64(placement.Key.Mode == "m" ? 2.0 : 9.0), SIBaseUnits.Meter)));

        problem.AdditionalCost.Should().NotBeNull();
        var first = new ActivityPlacement<Flt64>(
            new ActivityChoiceKey("a1", "m", Horizon().Start), Horizon(), Array.Empty<ActivityReservation<Flt64>>());
        var second = new ActivityPlacement<Flt64>(
            new ActivityChoiceKey("a1", "other", Horizon().Start), Horizon(), Array.Empty<ActivityReservation<Flt64>>());

        Quantity<Flt64>? firstCost = problem.AdditionalCost!(first) is Ok<Quantity<Flt64>, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>> ok1 ? ok1.Value : null;
        Quantity<Flt64>? secondCost = problem.AdditionalCost!(second) is Ok<Quantity<Flt64>, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>> ok2 ? ok2.Value : null;
        firstCost!.Value.ToFlt64().ToDouble().Should().BeApproximately(2.0, 1e-9);
        secondCost!.Value.ToFlt64().ToDouble().Should().BeApproximately(9.0, 1e-9);
    }
}
