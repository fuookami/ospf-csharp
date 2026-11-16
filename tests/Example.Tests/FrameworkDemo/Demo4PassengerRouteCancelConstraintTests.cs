// 行程取消单调性/ Cancellation monotonicity along an itinerary.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Passenger.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Passenger.Service.Limits;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **行程取消单调性** / Cancellation monotonicity along an itinerary.
/// </summary>
/// <remarks>
/// 行程上相邻两段之间：`cancel[前一段] ≤ cancel[后一段]`。方向写反**不会报错**——模型照样可行，只是解里会出现
/// "前一段只取消 1 人、后一段取消 4 人"这种现实中不存在的组合。所以判据必须**读出左右两侧各是谁**。
///
/// Between adjacent legs: `cancel[previous] ≤ cancel[next]`. Reversing the direction **raises nothing** — the model stays feasible and only the solution contains combinations that cannot
/// happen. The criterion must therefore **read which variable sits on each side**.
/// </remarks>
public class Demo4PassengerRouteCancelConstraintTests {
    private static readonly DateTimeOffset Origin = new(2024, 1, 1, 8, 0, 0, TimeSpan.Zero);

    private static readonly Airport Beijing = new(new ICAO("ZBAA"), AirportType.International);

    private static readonly Airport Shanghai = new(new ICAO("ZSPD"), AirportType.Domestic);

    private static readonly Airport Guangzhou = new(new ICAO("ZGGG"), AirportType.Domestic);

    private static readonly PassengerClassId Economy = new("Y");

    private static Aircraft AircraftFixture() {
        var type = AircraftType.GetOrAdd(new AircraftTypeCode("T762"));
        var minor = new AircraftMinorType(
            Type: type,
            Code: new AircraftMinorTypeCode("M762"),
            CostPerHour: new Flt64(1000),
            RouteFlyTime: new Dictionary<Route, TimeSpan>(),
            ConnectionTime: new Dictionary<Airport, TimeSpan>());
        var aircraft = new Aircraft(
            RegNo: new AircraftRegisterNumber("B762"),
            MinorType: minor,
            Capacity: new AircraftCapacity.PassengerCapacity(new Dictionary<PassengerClassId, ulong> {
                [Economy] = 100UL,
            }));
        aircraft.SetUsability(new AircraftUsability(null, Beijing, Origin));
        return aircraft;
    }

    private static FlightLeg Task(Aircraft aircraft, string no, Airport dep, Airport arr) => FlightLeg.Create(new FlightLegPlan(
        actualId: no,
        no: no,
        type: FlightType.Domestic,
        date: Origin,
        aircraft: aircraft,
        enabledAircrafts: new HashSet<Aircraft> { aircraft },
        dep: dep,
        arr: arr,
        scheduledTime: new TimeRange(Origin, Origin.AddHours(2)),
        estimatedTime: null,
        actualTime: null,
        outTime: null,
        flightTaskStatus: FlightTaskStatus.NotDelay));

    private static (List<FlightPassenger> Passengers, PassengerCancel Cancel, LinearMetaModel<Flt64> Model) Ready() {
        Aircraft aircraft = AircraftFixture();
        FlightLeg first = Task(aircraft, "CA901", Beijing, Shanghai);
        FlightLeg second = Task(aircraft, "CA902", Shanghai, Guangzhou);
        var passenger = new Passenger(2UL, new List<(FlightTask, PassengerClassId)> { (first, Economy), (second, Economy) });
        FlightPassenger leg1 = new(first, passenger);
        FlightPassenger leg2 = new(second, passenger, leg1);
        var passengers = new List<FlightPassenger> { leg1, leg2 };
        var cancel = new PassengerCancel(passengers);
        var model = new LinearMetaModel<Flt64>();
        cancel.Register(model).IsOk.Should().BeTrue();
        return (passengers, cancel, model);
    }

    /// <summary>
    /// **只在有前一段的地方加行，且方向是"前 ≤ 后"** / Rows only where a previous leg exists, and the direction is "previous ≤ next".
    /// </summary>
    /// <remarks>
    /// 四点成对：两段行程 → **一条**行（第一段没有前一段）/ 行名带管道名前缀 / **左侧是前一腿的取消变量** /
    /// **右侧是后一腿的取消变量**。只数行数会让方向写反的实现通过——**方向就是这条约束的全部内容**。
    ///
    /// Four paired points: a two-leg itinerary yields **one** row (the first leg has no previous); the row name carries the pipeline prefix; **the left side is the previous leg's
    /// cancellation variable**; and **the right side is the next leg's**. Counting rows alone would let a reversed-direction implementation pass — **the direction is the whole content of
    /// this constraint**.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldOrderTheCancellationAlongTheItinerary() {
        (List<FlightPassenger> passengers, PassengerCancel cancel, LinearMetaModel<Flt64> model) = Ready();
        using LinearMetaModel<Flt64> unused = model;
        var constraint = new PassengerRouteCancelConstraint(passengers, cancel);
        int before = model.Constraints.Count;

        constraint.Invoke(model).IsOk.Should().BeTrue();

        model.Constraints.Count.Should().Be(before + 1, "only the second leg has a previous leg");
        var row = (LinearInequalityConstraint<Flt64>)model.Constraints[before];
        row.Name.Should().StartWith("passenger_route_cancel_constraint_");
        row.Inequality.Comparison.Should().Be(Comparison.LE);
        row.Inequality.Lhs.Monomials.Should().ContainSingle()
            .Which.Symbol.Should().BeSameAs(cancel.Of(passengers[0]), "the previous leg sits on the left");
        row.Inequality.Rhs.Monomials.Should().ContainSingle()
            .Which.Symbol.Should().BeSameAs(cancel.Of(passengers[1]), "the next leg sits on the right");
    }

    /// <summary>
    /// **单段行程一行都不加** / A single-leg itinerary adds no row.
    /// </summary>
    /// <remarks>
    /// 两点成对：只有一腿时约束数不变 / 调用返回成功。**"没有前一段"不是错误**，但如果实现给第一腿也加一条
    /// `cancel ≤ cancel` 的恒真行，模型里就会多出与行程数同量级的废行。
    ///
    /// Two paired points: with a single leg the constraint count is unchanged; and the call succeeds. **A missing previous leg is not an error**, but an implementation that adds a
    /// tautological `cancel ≤ cancel` row for the first leg inflates the model with one useless row per itinerary.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldAddNothingForASingleLegItinerary() {
        (List<FlightPassenger> passengers, PassengerCancel cancel, LinearMetaModel<Flt64> model) = Ready();
        using LinearMetaModel<Flt64> unused = model;
        var constraint = new PassengerRouteCancelConstraint(new[] { passengers[0] }, cancel);
        int before = model.Constraints.Count;

        constraint.Invoke(model).IsOk.Should().BeTrue();

        model.Constraints.Count.Should().Be(before);
    }
}
