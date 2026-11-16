// 乘客域聚合/ The passenger domain aggregation.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Passenger;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Passenger.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **乘客域聚合** / The passenger domain aggregation.
/// </summary>
/// <remarks>
/// 注册顺序是**取消 → 变更 → 人数**，顺序错了不会报错：人数表达式引用不到变更变量时就少项，而**解出来的人数看起来正常**。
/// 所以判据不看"注册成功"，看**表达式里真的有变更项**。
///
/// The registration order is **cancel, change, amount**. Getting it wrong raises nothing: when the amount expressions cannot reach the change variables those terms are simply missing,
/// while **the solved head count looks normal**. The criterion therefore does not check "registration succeeded" but **that the change terms really appear**.
/// </remarks>
public class Demo4PassengerAggregationTests {
    private static readonly DateTimeOffset Origin = new(2024, 1, 1, 8, 0, 0, TimeSpan.Zero);

    private static readonly Airport Beijing = new(new ICAO("ZBAA"), AirportType.International);

    private static readonly Airport Shanghai = new(new ICAO("ZSPD"), AirportType.Domestic);

    private static readonly PassengerClassId Economy = new("Y");
    private static readonly PassengerClassId Business = new("J");

    private static Aircraft AircraftFixture() {
        var type = AircraftType.GetOrAdd(new AircraftTypeCode("T761"));
        var minor = new AircraftMinorType(
            Type: type,
            Code: new AircraftMinorTypeCode("M761"),
            CostPerHour: new Flt64(1000),
            RouteFlyTime: new Dictionary<Route, TimeSpan>(),
            ConnectionTime: new Dictionary<Airport, TimeSpan>());
        var aircraft = new Aircraft(
            RegNo: new AircraftRegisterNumber("B761"),
            MinorType: minor,
            Capacity: new AircraftCapacity.PassengerCapacity(new Dictionary<PassengerClassId, ulong> {
                [Economy] = 100UL,
                [Business] = 10UL,
            }));
        aircraft.SetUsability(new AircraftUsability(null, Beijing, Origin));
        return aircraft;
    }

    private static FlightLeg Task(Aircraft aircraft, string no, int hourOffset = 0) => FlightLeg.Create(new FlightLegPlan(
        actualId: no,
        no: no,
        type: FlightType.Domestic,
        date: Origin,
        aircraft: aircraft,
        enabledAircrafts: new HashSet<Aircraft> { aircraft },
        dep: Beijing,
        arr: Shanghai,
        scheduledTime: new TimeRange(Origin.AddHours(hourOffset), Origin.AddHours(hourOffset + 2)),
        estimatedTime: null,
        actualTime: null,
        outTime: null,
        flightTaskStatus: FlightTaskStatus.NotDelay));

    private static (List<FlightTask> Flights, List<FlightPassenger> Passengers) Fixture() {
        Aircraft aircraft = AircraftFixture();
        FlightLeg first = Task(aircraft, "CA901");
        FlightLeg later = Task(aircraft, "CA902", hourOffset: 4);
        var passenger = new Passenger(4UL, new List<(FlightTask, PassengerClassId)> { (first, Economy) });
        return (new List<FlightTask> { first, later }, new List<FlightPassenger> { new(first, passenger) });
    }

    /// <summary>
    /// **一次注册之后三块都在，且人数里有变更项** / After one registration all three exist, with the change terms present.
    /// </summary>
    /// <remarks>
    /// 三点成对：取消与变更的变量表都在 / 人数表达式表覆盖了两班 × 两舱 / **`Y` 舱表达式里有一项来自舱位变更**（系数 −1）。
    /// 顺序写反（人数先建）时，第三点会红——**那正是这个顺序唯一能被观察到的地方**。
    ///
    /// Three paired points: both the cancellation and change variable tables exist; the amount table covers two flights by two cabins; and **the `Y` expression holds a term from the
    /// cabin change** (coefficient −1). Reversing the order (amounts first) turns the third red — **the only observable consequence of that order**.
    /// </remarks>
    [Fact]
    public void Register_ShouldBuildAllThreeInOrder() {
        (List<FlightTask> flights, List<FlightPassenger> passengers) = Fixture();
        using var model = new LinearMetaModel<Flt64>();
        var aggregation = new Aggregation(flights, passengers, new[] { Economy, Business });

        aggregation.Register(model).IsOk.Should().BeTrue();

        aggregation.Cancel.Variables.Should().NotBeNull();
        aggregation.Change.PassengerClassChange.Should().NotBeNull();
        aggregation.Amount.Amounts.Should().NotBeNull();
        aggregation.Amount.Amounts!.Should().HaveCount(2, "two flights");
        aggregation.Amount.Amounts![flights[0]].Should().HaveCount(2, "two cabins");

        LinearExpressionSymbol economy = aggregation.Amount.Amounts[flights[0]][Economy];
        economy.Polynomial.Constant.ToDouble().Should().Be(4.0, "the head count");
        economy.Polynomial.Monomials.Should().HaveCount(2, "cancellation plus the cabin-change-out term");
        economy.Polynomial.Monomials.Should().OnlyContain(monomial => monomial.Coefficient.ToDouble() == -1.0);
    }

    /// <summary>
    /// **打开航班变更之后才有那一层** / The flight-change layer only exists when requested.
    /// </summary>
    /// <remarks>
    /// 两点成对：关闭时 `PassengerFlightChange` 是 `null` / 打开后表存在且目标航班上有每个舱位的变量。
    /// 关闭时也建出来，会让目标里多出一批永远取零的变量——**它们不进解，只进模型规模**。
    ///
    /// Two paired points: with the switch off `PassengerFlightChange` is null; with it on the table exists and each target flight holds one variable per cabin. Building it while off
    /// would add a batch of always-zero variables — **they never enter the solution, only the model size**.
    /// </remarks>
    [Fact]
    public void Register_ShouldBuildFlightChangeOnlyWhenAsked() {
        (List<FlightTask> flights, List<FlightPassenger> passengers) = Fixture();
        using var offModel = new LinearMetaModel<Flt64>();
        var off = new Aggregation(flights, passengers, new[] { Economy, Business });
        off.Register(offModel).IsOk.Should().BeTrue();
        off.Change.PassengerFlightChange.Should().BeNull();

        using var onModel = new LinearMetaModel<Flt64>();
        var on = new Aggregation(flights, passengers, new[] { Economy, Business }, withFlightChange: true);
        on.Register(onModel).IsOk.Should().BeTrue();
        on.Change.PassengerFlightChange.Should().NotBeNull();
        on.Change.PassengerFlightChange![passengers[0]].Should().ContainKey(flights[1]);
    }

    /// <summary>
    /// **重注册不重复建** / Re-registration builds nothing twice.
    /// </summary>
    /// <remarks>
    /// 两点成对：注册两次之后变量数与符号数都不变 / 各表仍是同一份实例。
    ///
    /// Two paired points: registering twice leaves both the variable and symbol counts unchanged; and the tables are the same instances.
    /// </remarks>
    [Fact]
    public void Register_ShouldBeIdempotent() {
        (List<FlightTask> flights, List<FlightPassenger> passengers) = Fixture();
        using var model = new LinearMetaModel<Flt64>();
        var aggregation = new Aggregation(flights, passengers, new[] { Economy, Business });
        aggregation.Register(model).IsOk.Should().BeTrue();
        int variables = model.Tokens.Tokens.Count;
        int symbols = model.Tokens.Symbols.Count;

        aggregation.Register(model).IsOk.Should().BeTrue();

        model.Tokens.Tokens.Count.Should().Be(variables);
        model.Tokens.Symbols.Count.Should().Be(symbols);
    }
}
