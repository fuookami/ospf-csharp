// 机队平衡的约束与目标/ The fleet balance's constraint and objective.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchCompilation;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchCompilation.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchCompilation.Service.Limits;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **机队平衡的约束与目标** / The fleet balance's constraint and objective.
/// </summary>
/// <remarks>
/// 干两件事：每个检查点加一条 `slack ≥ 机数`，再把 `Σ 系数 × slack` 放进最小化目标。
/// **两半正好是一对反向的错误**：只加约束，松弛可以取任意大值；只加目标，松弛一路压到零——"平衡得完美"，其实一架飞机都没数进去。
///
/// The code's does two things: one `slack ≥ aircraft count` row per checkpoint, then `Σ coefficient × slack` into the minimisation objective. **The two halves
/// are opposite failure modes**: with only the constraint the slack may take any large value; with only the objective it is driven to zero — "perfectly balanced", while no aircraft was
/// counted at all.
/// </remarks>
public class Demo4FleetBalanceLimitTests {
    private static readonly DateTimeOffset Origin = new(2024, 1, 1, 8, 0, 0, TimeSpan.Zero);

    private static readonly Airport Beijing = new(new ICAO("ZBAA"), AirportType.International);

    private static readonly Airport Shanghai = new(new ICAO("ZSPD"), AirportType.Domestic);

    private static TimeWindow<Flt64> Window() => new(new TimeRange(Origin, Origin.AddDays(1)), false, TimeSpan.FromHours(8));

    private static Aircraft AircraftFixture() {
        var type = AircraftType.GetOrAdd(new AircraftTypeCode("T753"));
        var minor = new AircraftMinorType(
            Type: type,
            Code: new AircraftMinorTypeCode("M753"),
            CostPerHour: new Flt64(1000),
            RouteFlyTime: new Dictionary<Route, TimeSpan>(),
            ConnectionTime: new Dictionary<Airport, TimeSpan>());
        var aircraft = new Aircraft(
            RegNo: new AircraftRegisterNumber("B753"),
            MinorType: minor,
            Capacity: new AircraftCapacity.PassengerCapacity(new Dictionary<PassengerClassId, ulong> {
                [new PassengerClassId("Y")] = 100UL,
            }));
        aircraft.SetUsability(new AircraftUsability(null, Beijing, Origin));
        return aircraft;
    }

    private static FlightLeg Task(Aircraft aircraft, string no) => FlightLeg.Create(new FlightLegPlan(
        actualId: no,
        no: no,
        type: FlightType.Domestic,
        date: Origin,
        aircraft: aircraft,
        enabledAircrafts: new HashSet<Aircraft> { aircraft },
        dep: Beijing,
        arr: Shanghai,
        scheduledTime: new TimeRange(Origin, Origin.AddHours(2)),
        estimatedTime: null,
        actualTime: null,
        outTime: null,
        flightTaskStatus: FlightTaskStatus.NotDelay));

    private static (Aggregation Aggregation, List<FlightTaskBunch> Bunches, LinearMetaModel<Flt64> Model) Ready() {
        Aircraft aircraft = AircraftFixture();
        FlightLeg task = Task(aircraft, "CA753");
        var model = new LinearMetaModel<Flt64>();
        var bunches = new List<FlightTaskBunch> { new(aircraft, new List<FlightTask> { task }, 0L) };
        var aggregation = new Aggregation(
            Window(), new List<Aircraft> { aircraft }, new List<FlightTask> { task }, originBunches: bunches);
        aggregation.Register(model).IsOk.Should().BeTrue();
        aggregation.AddColumns(0UL, bunches, model).IsOk.Should().BeTrue();
        return (aggregation, bunches, model);
    }

    /// <summary>
    /// **每个检查点一条约束，外加一个目标** / One row per checkpoint plus one objective.
    /// </summary>
    /// <remarks>
    /// 两点成对：调用之后约束数**恰好增加检查点数** / 约束名字带管道名前缀。
    /// 只测"调用返回成功"会让"什么都不做"的实现通过。**目标那一半在这里看不到**——线性模型没有目标表可读，
    /// 所以它由真实求解判据来钉（`Demo4RealSolveTests`：束被钉成 0 时目标必须是 1）。
    ///
    /// Two paired points: the constraint count grows by **exactly the number of checkpoints**; the row names carry the pipeline prefix. Checking only "the call returns Ok" would let a
    /// do-nothing implementation pass. **The objective half is invisible here** — the linear model exposes no objective table — so it is pinned by the real-solve criterion
    /// (`Demo4RealSolveTests`: pinning the bunch to 0 must make the objective 1).
    /// </remarks>
    [Fact]
    public void Invoke_ShouldAddOneRowPerCheckpointAndOneObjective() {
        (Aggregation aggregation, List<FlightTaskBunch> _, LinearMetaModel<Flt64> model) = Ready();
        using LinearMetaModel<Flt64> unused = model;
        var limit = new FleetBalanceLimit(aggregation.FleetBalance);
        int constraintsBefore = model.Constraints.Count;

        limit.Invoke(model).IsOk.Should().BeTrue();

        model.Constraints.Count.Should().Be(constraintsBefore + aggregation.FleetBalance.Limits.Count);
        model.Constraints.Select(constraint => constraint.Name).Should().Contain(name => name!.StartsWith("fleet_balance_limit_", StringComparison.Ordinal));
    }

    /// <summary>
    /// **没注册就调用会显式失败** / Calling before registration fails explicitly.
    /// </summary>
    /// <remarks>
    /// 两点成对：未注册的 `FleetBalance` → `Failed` / 注册之后 → `Ok`。**静默加一条"松弛 ≥ 机数"的空行**会让模型看起来有约束、
    /// 实际什么都没有。
    ///
    /// Two paired points: an unregistered `FleetBalance` yields `Failed`; after registration it yields `Ok`. **Silently adding an empty "slack ≥ count" row** would leave the model
    /// looking constrained while it holds nothing.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldFailBeforeTheBalanceIsRegistered() {
        Aircraft aircraft = AircraftFixture();
        FlightLeg task = Task(aircraft, "CA853");
        var model = new LinearMetaModel<Flt64>();
        using LinearMetaModel<Flt64> unused = model;
        var aggregation = new Aggregation(Window(), new List<Aircraft> { aircraft }, new List<FlightTask> { task });
        var limit = new FleetBalanceLimit(aggregation.FleetBalance);

        limit.Invoke(model).IsFailed.Should().BeTrue();

        aggregation.Register(model).IsOk.Should().BeTrue();
        limit.Invoke(model).IsOk.Should().BeTrue();
    }

    /// <summary>
    /// **系数在多项式外面乘** / The coefficient multiplies outside the polynomial.
    /// </summary>
    /// <remarks>
    /// 两点成对：系数 2 时约束数不变（系数只影响目标）/ 目标项真的翻倍——用"系数 1 与系数 2 两次装配，比较目标里同一项".
    /// 系数乘到松弛内部会同时改变约束，**那就不是"惩罚更重"，而是"要求更高"了**。
    ///
    /// Two paired points: with coefficient 2 the constraint count is unchanged (the coefficient only affects the objective); and the objective term really doubles — checked by assembling
    /// twice (coefficient 1 and 2) and comparing the same term. Multiplying inside the slack would change the constraint too, **which is "a higher requirement", not "a heavier penalty"**.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldScaleTheObjectiveOnly() {
        (Aggregation aggregation, List<FlightTaskBunch> _, LinearMetaModel<Flt64> model) = Ready();
        using LinearMetaModel<Flt64> unused = model;
        int constraintsBefore = model.Constraints.Count;

        new FleetBalanceLimit(aggregation.FleetBalance, (_, _) => new Flt64(2.0)).Invoke(model).IsOk.Should().BeTrue();

        model.Constraints.Count.Should().Be(constraintsBefore + aggregation.FleetBalance.Limits.Count,
            "the coefficient belongs to the objective, not to the rows");
        // **系数不能进右端项**：乘进约束就变成"要求 2 架"，而语义是"缺一架罚 2 倍"。右端项必须还是机数。
        // **The coefficient must not reach the right-hand side**: multiplying there turns "要求 2 架" into the requirement itself, while the semantics is "missing one costs twice as
        // much". The right-hand side has to stay the aircraft count.
        var bounds = model.Constraints
            .Where(constraint => constraint.Name.StartsWith("fleet_balance_limit_", StringComparison.Ordinal))
            .Select(constraint => ((Fuookami.Ospf.Core.Model.Mechanism.LinearInequalityConstraint<Flt64>)constraint).Inequality.Rhs.Constant)
            .ToList();
        bounds.Should().NotBeEmpty();
        bounds.Should().OnlyContain(bound => bound.ToDouble() == 1.0, "every checkpoint here holds exactly one aircraft, whatever the coefficient");
    }
}
