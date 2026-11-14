// 航班变更最小化（B765）/ The flight-change objective (B765).

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Passenger.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Passenger.Service.Limits;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **航班变更最小化（B765）** / The flight-change objective (B765).
/// </summary>
/// <remarks>
/// 比舱位变更那层多一维：**乘客 × 目标航班 × 舱位**。两个"空表即空目标"的入口也要钉住——没开开关、没有可替换航班时，
/// 目标根本不存在，**不是"目标为零"**。
///
/// One dimension more than the cabin-change layer: **passenger by target flight by cabin**. The two "empty table means empty objective" entries must be pinned as well — with the switch
/// off, or with no alternative flight, the objective does not exist at all, **it is not "zero"**.
/// </remarks>
public class Demo4PassengerFlightChangeMinimizationTests {
    private static readonly DateTimeOffset Origin = new(2024, 1, 1, 8, 0, 0, TimeSpan.Zero);

    private static readonly Airport Beijing = new(new ICAO("ZBAA"), AirportType.International);

    private static readonly Airport Shanghai = new(new ICAO("ZSPD"), AirportType.Domestic);

    private static readonly PassengerClassId Economy = new("Y");
    private static readonly PassengerClassId Business = new("J");

    private static Aircraft AircraftFixture() {
        var type = AircraftType.GetOrAdd(new AircraftTypeCode("T765"));
        var minor = new AircraftMinorType(
            Type: type,
            Code: new AircraftMinorTypeCode("M765"),
            CostPerHour: new Flt64(1000),
            RouteFlyTime: new Dictionary<Route, TimeSpan>(),
            ConnectionTime: new Dictionary<Airport, TimeSpan>());
        var aircraft = new Aircraft(
            RegNo: new AircraftRegisterNumber("B765"),
            MinorType: minor,
            Capacity: new AircraftCapacity.PassengerCapacity(new Dictionary<PassengerClassId, ulong> {
                [Economy] = 100UL,
                [Business] = 10UL,
            }));
        aircraft.SetUsability(new AircraftUsability(null, Beijing, Origin));
        return aircraft;
    }

    private static FlightLeg Task(Aircraft aircraft, string no, int hourOffset) => FlightLeg.Create(new FlightLegPlan(
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

    private static (List<FlightTask> Flights, List<FlightPassenger> Passengers, PassengerCancel Cancel, LinearMetaModel<Flt64> Model) Ready(bool withFlightChange = true) {
        Aircraft aircraft = AircraftFixture();
        FlightLeg first = Task(aircraft, "CA765", 0);
        FlightLeg later = Task(aircraft, "CA766", 4);
        FlightLeg evenLater = Task(aircraft, "CA768", 8);
        var flights = new List<FlightTask> { first, later, evenLater };
        var passenger = new Passenger(3UL, new List<(FlightTask, PassengerClassId)> { (first, Economy) });
        var passengers = new List<FlightPassenger> { new(first, passenger) };
        var cancel = new PassengerCancel(passengers);
        var change = new PassengerChange(flights, passengers, new[] { Economy, Business }, withFlightChange);
        var model = new LinearMetaModel<Flt64>();
        cancel.Register(model).IsOk.Should().BeTrue();
        change.Register(model).IsOk.Should().BeTrue();
        return (flights, passengers, cancel, model);
    }

    private static PassengerChange ChangeOf(List<FlightTask> flights, List<FlightPassenger> passengers, bool withFlightChange = true) =>
        new(flights, passengers, new[] { Economy, Business }, withFlightChange);

    /// <summary>
    /// **每一维都参与：乘客 × 目标航班 × 舱位** / Every dimension participates: passenger by target flight by cabin.
    /// </summary>
    /// <remarks>
    /// 四点成对：一位乘客、一个替代航班、两个舱位 → **两项** / 名字是 `passenger flight change` / 分类是最小化 /
    /// **同一舱位换个目标航班就换价**（`J` 收 5、`Y` 收 9）。
    ///
    /// Four paired points: one passenger, one alternative flight, two cabins → **two terms**; the part is named `passenger flight change`; its category is minimisation; and **the same
    /// cabin costs differently on a different target flight** (`J` sees 5, `Y` sees 9).
    /// </remarks>
    [Fact]
    public void Invoke_ShouldAddOneTermPerTargetFlightAndCabin() {
        (List<FlightTask> flights, List<FlightPassenger> passengers, PassengerCancel _, LinearMetaModel<Flt64> model) = Ready();
        using LinearMetaModel<Flt64> unused = model;
        PassengerChange change = ChangeOf(flights, passengers);
        change.Register(model).IsOk.Should().BeTrue();
        // **价格按"目标航班 × 舱位"两点给**：只有这样才分得清"用的是目标航班"和"用的是原航班"。
        // **The price is keyed by target flight and cabin**: only then can "used the target flight" be told apart from "used the original one".
        var objective = new PassengerFlightChangeMinimization(
            passengers,
            change,
            // **按引用认目标航班，不按 `Id`**：`FlightTask.Id` 不是航班号（`No`），照航班号比永远落进同一个分支。
            // **Recognise the target flight by reference, not by `Id`**: `FlightTask.Id` is not the flight number (`No`), so comparing flight numbers silently lands in one branch.
            (_, toFlight, cls) => new Flt64(cls == Business
                ? (ReferenceEquals(toFlight, flights[1]) ? 5 : 11)
                : (ReferenceEquals(toFlight, flights[1]) ? 9 : 13)));
        int before = model.MetaSubObjects.Count;

        objective.Invoke(model).IsOk.Should().BeTrue();

        model.MetaSubObjects.Count.Should().Be(before + 1);
        MetaSubObject<Flt64> added = model.MetaSubObjects[^1];
        added.Name.Should().Be("passenger flight change");
        added.Category.Should().Be(ObjectCategory.Minimum);
        added.Polynomial.Monomials.Should().HaveCount(4, "two target flights times two cabins");
        added.Polynomial.Monomials.Select(monomial => monomial.Coefficient.ToDouble()).Order().ToList()
            .Should().Equal(new List<double> { 5.0, 9.0, 11.0, 13.0 }, "each target flight and cabin keeps its own price");
    }

    /// <summary>
    /// **空表就是空目标，不是错误** / An empty table means an empty objective, not an error.
    /// </summary>
    /// <remarks>
    /// 两点成对：没开航班变更 → 返回成功且不加目标 / **没有可替换航班**（只有一班同航路）→ 同样不加。
    /// 这两种情况报错会让"这批数据不需要改签"变成一次失败——**那就把业务结论当成了故障**。
    ///
    /// Two paired points: with flight change off the call succeeds and adds nothing; and **with no alternative flight** (only one flight on the route) it adds nothing either. Failing on
    /// these would turn "this data set needs no re-booking" into an error — **mistaking a business conclusion for a fault**.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldAddNothingWhenThereIsNothingToChange() {
        (List<FlightTask> flights, List<FlightPassenger> passengers, PassengerCancel _, LinearMetaModel<Flt64> model) = Ready();
        using LinearMetaModel<Flt64> unused = model;
        int before = model.MetaSubObjects.Count;

        PassengerChange off = ChangeOf(flights, passengers, withFlightChange: false);
        off.Register(model).IsOk.Should().BeTrue();
        new PassengerFlightChangeMinimization(passengers, off).Invoke(model).IsOk.Should().BeTrue();
        model.MetaSubObjects.Count.Should().Be(before, "no flight change means no objective");

        PassengerChange lonely = new(new[] { flights[0] }, passengers, new[] { Economy, Business }, withFlightChange: true);
        lonely.Register(model).IsOk.Should().BeTrue();
        lonely.ToFlights[flights[0]].Should().BeEmpty("there is only one flight on this route");
        new PassengerFlightChangeMinimization(passengers, lonely).Invoke(model).IsOk.Should().BeTrue();
        model.MetaSubObjects.Count.Should().Be(before, "no alternative flight means no objective");

        PassengerChange open = ChangeOf(flights, passengers);
        open.Register(model).IsOk.Should().BeTrue();
        new PassengerFlightChangeMinimization(passengers, open, (_, _, _) => Flt64.Zero).Invoke(model).IsOk.Should().BeTrue();
        model.MetaSubObjects.Count.Should().Be(before, "an all-zero objective is not worth carrying");
    }

#if SCIP_INTEGRATION
    /// <summary>
    /// **真实求解：价格按"目标航班 × 舱位"取** / Real solve: the price follows the target flight and cabin.
    /// </summary>
    /// <remarks>
    /// 三点成对：强制 `passengerFlightChange[p, later, J] ≥ 1` / 该组合系数 5（`Y` 是 9）/ **`Obj == 5.0`**。
    /// 系数若按舱位取反或统一成 1，都会立刻红——**三维的系数只有解出来的值能验证**。
    ///
    /// Three paired points: `passengerFlightChange[p, later, J]` is forced to at least one; that pair costs 5 (`Y` costs 9); and **`Obj == 5.0`**. Swapping the cabins or flattening to 1
    /// turns it red at once — **only the solved value can verify a three-dimensional coefficient**.
    /// </remarks>
    [Fact]
    public async System.Threading.Tasks.Task Invoke_ShouldPriceTheFlightChangePerTargetAndCabin() {
        (List<FlightTask> flights, List<FlightPassenger> passengers, PassengerCancel _, LinearMetaModel<Flt64> model) = Ready();
        using LinearMetaModel<Flt64> unused = model;
        PassengerChange change = ChangeOf(flights, passengers);
        change.Register(model).IsOk.Should().BeTrue();
        Fuookami.Ospf.Core.Variable.UIntVar toLaterBusiness = change.PassengerFlightChange![passengers[0]][flights[1]][Business];
        toLaterBusiness.Range.Geq(Fuookami.Ospf.Math.Algebra.Number.UInt64.One);
        new PassengerFlightChangeMinimization(
            passengers,
            change,
            (_, _, cls) => cls == Business ? new Flt64(5) : new Flt64(9)).Invoke(model).IsOk.Should().BeTrue();

        var result = await new Fuookami.Ospf.Core.Plugin.Scip.ScipColumnGenerationSolver()
            .SolveMILPAsync("passenger_flight_change_objective", model);

        result.IsOk.Should().BeTrue("the forced flight change leaves a feasible region");
        result.Value!.Obj.ToDouble().Should().Be(5.0, "changing to the later flight in J costs five, in Y it costs nine");
    }
#endif
}
