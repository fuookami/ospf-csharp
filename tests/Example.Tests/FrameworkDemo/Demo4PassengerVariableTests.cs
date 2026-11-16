// 乘客取消与变更变量/ Passenger cancellation and change variables.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Passenger.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **乘客取消与变更变量** / Passenger cancellation and change variables.
/// </summary>
/// <remarks>
/// 三层变量都有一条共同的上界：**不超过这一位乘客的人数**。取消量或变更量超过人数在现实里不存在，
/// 而少了这条上界，最小化会把它们压到零的同时，让别的约束通过它们拿到任意自由度。
///
/// All three layers share one bound: **no more than the passenger's head count**. Cancelling or changing more people than exist is unreal, and without the bound the minimisation drives
/// these variables to zero while other constraints gain arbitrary freedom through them.
/// </remarks>
public class Demo4PassengerVariableTests {
    private static readonly DateTimeOffset Origin = new(2024, 1, 1, 8, 0, 0, TimeSpan.Zero);

    private static readonly Airport Beijing = new(new ICAO("ZBAA"), AirportType.International);

    private static readonly Airport Shanghai = new(new ICAO("ZSPD"), AirportType.Domestic);

    private static readonly Airport Guangzhou = new(new ICAO("ZGGG"), AirportType.Domestic);

    private static Aircraft AircraftFixture() {
        var type = AircraftType.GetOrAdd(new AircraftTypeCode("T759"));
        var minor = new AircraftMinorType(
            Type: type,
            Code: new AircraftMinorTypeCode("M759"),
            CostPerHour: new Flt64(1000),
            RouteFlyTime: new Dictionary<Route, TimeSpan>(),
            ConnectionTime: new Dictionary<Airport, TimeSpan>());
        var aircraft = new Aircraft(
            RegNo: new AircraftRegisterNumber("B759"),
            MinorType: minor,
            Capacity: new AircraftCapacity.PassengerCapacity(new Dictionary<PassengerClassId, ulong> {
                [new PassengerClassId("Y")] = 100UL,
            }));
        aircraft.SetUsability(new AircraftUsability(null, Beijing, Origin));
        return aircraft;
    }

    private static FlightLeg Task(Aircraft aircraft, string no, Airport dep, Airport arr, int hourOffset = 0) =>
        FlightLeg.Create(new FlightLegPlan(
            actualId: no,
            no: no,
            type: FlightType.Domestic,
            date: Origin,
            aircraft: aircraft,
            enabledAircrafts: new HashSet<Aircraft> { aircraft },
            dep: dep,
            arr: arr,
            scheduledTime: new TimeRange(Origin.AddHours(hourOffset), Origin.AddHours(hourOffset + 2)),
            estimatedTime: null,
            actualTime: null,
            outTime: null,
            flightTaskStatus: FlightTaskStatus.NotDelay));

    private static readonly PassengerClassId Economy = new("Y");
    private static readonly PassengerClassId Business = new("J");

    private static (List<FlightTask> Flights, List<FlightPassenger> Passengers) Fixture() {
        Aircraft aircraft = AircraftFixture();
        FlightLeg first = Task(aircraft, "CA901", Beijing, Shanghai);
        FlightLeg later = Task(aircraft, "CA902", Beijing, Shanghai, hourOffset: 4);
        FlightLeg other = Task(aircraft, "CA903", Shanghai, Guangzhou, hourOffset: 8);
        var flights = new List<FlightTask> { first, later, other };
        var passenger = new Passenger(4UL, new List<(FlightTask, PassengerClassId)> { (first, Economy) });
        var passengers = new List<FlightPassenger> { new(first, passenger) };
        return (flights, passengers);
    }

    /// <summary>
    /// **每位乘客一个取消变量，上界是人数** / One cancellation variable per passenger, bounded by the head count.
    /// </summary>
    /// <remarks>
    /// 三点成对：注册后变量表存在且长度等于乘客数 / 模型里的符号数增加 / **上界等于人数**。
    /// 只数变量条数会让"上界写成无穷"的实现通过——**那条上界才是这个变量唯一的约束**。
    ///
    /// Three paired points: the variable table exists with one entry per passenger; the model's symbol count grows; and **the upper bound equals the head count**. Counting variables alone
    /// would let an implementation with an unbounded variable pass — **that bound is the only constraint on this variable**.
    /// </remarks>
    [Fact]
    public void PassengerCancel_ShouldBoundEachVariableByTheHeadCount() {
        (List<FlightTask> _, List<FlightPassenger> passengers) = Fixture();
        using var model = new LinearMetaModel<Flt64>();
        var cancel = new PassengerCancel(passengers);
        int before = model.Tokens.Tokens.Count;

        cancel.Register(model).IsOk.Should().BeTrue();

        cancel.Variables.Should().NotBeNull();
        model.Tokens.Tokens.Count.Should().BeGreaterThan(before, "a variable that never reaches the model is not a variable");
        model.Tokens.Find(cancel.Of(passengers[0])).Should().NotBeNull("the cancellation variable must be findable in the model");
        cancel.Of(passengers[0]).Range.UpperBound.Should().NotBeNull();
        cancel.Of(passengers[0]).Range.UpperBound!.Value.ToFlt64().ToDouble().Should().Be(4.0, "the passenger's head count");

        int afterFirst = model.Tokens.Tokens.Count;
        cancel.Register(model).IsOk.Should().BeTrue();
        model.Tokens.Tokens.Count.Should().Be(afterFirst, "re-registration must not add the variables twice");
    }

    /// <summary>
    /// **自己那一档没有变更变量** / The passenger's own cabin gets no change variable.
    /// </summary>
    /// <remarks>
    /// 三点成对：舱位表里**只有除自己以外的舱位**（`Y` 乘客只剩 `J`）/ 变更量的上界是人数 / 航班变更关闭时那张表是 `null`。
    /// 给"留在原舱位"也建一个变量，会让目标里多出一个恒零的项，**还会让"变更量之和"多算一份**。
    ///
    /// Three paired points: the cabin table holds **only the other cabins** (a `Y` passenger keeps just `J`); the change bound is the head count; and with flight change off that table is
    /// null. Building a variable for "stay in the same cabin" would add an always-zero objective term **and inflate the sum of changes by one share**.
    /// </remarks>
    [Fact]
    public void PassengerChange_ShouldSkipTheOwnCabin() {
        (List<FlightTask> flights, List<FlightPassenger> passengers) = Fixture();
        using var model = new LinearMetaModel<Flt64>();
        var change = new PassengerChange(flights, passengers, new[] { Economy, Business });

        change.Register(model).IsOk.Should().BeTrue();

        change.PassengerClassChange.Should().NotBeNull();
        IReadOnlyDictionary<PassengerClassId, Fuookami.Ospf.Core.Variable.UIntVar> row = change.PassengerClassChange![passengers[0]];
        row.Should().ContainKey(Business);
        row.Should().NotContainKey(Economy, "staying in the same cabin is not a change");
        row[Business].Range.UpperBound!.Value.ToFlt64().ToDouble().Should().Be(4.0);
        change.PassengerFlightChange.Should().BeNull("flight change was not requested");
    }

    /// <summary>
    /// **航班变更只指向同航路的其它航班** / Flight changes only point at other flights on the same route.
    /// </summary>
    /// <remarks>
    /// 四点成对：`ToFlights` 给出**同航路的另一班** / **不含自己** / **不含不同航路的航班** / 打开开关之后每位乘客的目标航班上都有每个舱位的变量。
    /// 把不同航路也放进来，等于允许"改签到飞不到目的地的航班"——**那不是变更，是把行程打断**。
    ///
    /// Four paired points: `ToFlights` lists **the other flight on the same route**; **not the flight itself**; **not the flight on a different route**; and with the switch on, every
    /// cabin gets a variable on each target flight. Including other routes would allow "moving to a flight that does not reach the destination" — **not a change but a broken itinerary**.
    /// </remarks>
    [Fact]
    public void PassengerChange_ShouldOnlyOfferSameRouteAlternatives() {
        (List<FlightTask> flights, List<FlightPassenger> passengers) = Fixture();
        using var model = new LinearMetaModel<Flt64>();
        var change = new PassengerChange(flights, passengers, new[] { Economy, Business }, withFlightChange: true);
        FlightTask origin = passengers[0].Flight;

        IReadOnlyList<FlightTask> alternatives = change.ToFlights[origin];
        alternatives.Should().ContainSingle();
        alternatives.Should().NotContain(origin, "moving to the same flight is not a change");
        alternatives[0].Arr.Should().Be(origin.Arr);
        alternatives[0].Dep.Should().Be(origin.Dep);

        change.Register(model).IsOk.Should().BeTrue();

        change.PassengerFlightChange.Should().NotBeNull();
        IReadOnlyDictionary<FlightTask, IReadOnlyDictionary<PassengerClassId, Fuookami.Ospf.Core.Variable.UIntVar>> byFlight =
            change.PassengerFlightChange![passengers[0]];
        byFlight.Should().ContainKey(alternatives[0]);
        byFlight[alternatives[0]].Should().ContainKeys(Economy, Business);
    }
}
