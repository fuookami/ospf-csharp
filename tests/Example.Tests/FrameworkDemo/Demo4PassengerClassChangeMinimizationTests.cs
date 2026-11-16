// 舱位变更最小化/ The cabin-change objective.

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
/// **舱位变更最小化** / The cabin-change objective.
/// </summary>
/// <remarks>
/// 求和口径是这一层唯一容易错的地方：**不是"每位乘客一项"，而是"每位乘客 × 除自己以外的舱位"**。
/// 少算会让改舱变成免费的，多算（把自己那一档也算进去）会因为那个变量根本不存在而取不到值。
///
/// The summation is the only easy mistake here: **it is not "one term per passenger" but "one per passenger per other cabin"**. Under-counting makes a cabin change free; over-counting
/// (including the own cabin) fails to fetch a value because that variable does not exist.
/// </remarks>
public class Demo4PassengerClassChangeMinimizationTests {
    private static readonly DateTimeOffset Origin = new(2024, 1, 1, 8, 0, 0, TimeSpan.Zero);

    private static readonly Airport Beijing = new(new ICAO("ZBAA"), AirportType.International);

    private static readonly Airport Shanghai = new(new ICAO("ZSPD"), AirportType.Domestic);

    private static readonly PassengerClassId Economy = new("Y");
    private static readonly PassengerClassId Business = new("J");
    private static readonly PassengerClassId First = new("F");

    private static Aircraft AircraftFixture() {
        var type = AircraftType.GetOrAdd(new AircraftTypeCode("T764"));
        var minor = new AircraftMinorType(
            Type: type,
            Code: new AircraftMinorTypeCode("M764"),
            CostPerHour: new Flt64(1000),
            RouteFlyTime: new Dictionary<Route, TimeSpan>(),
            ConnectionTime: new Dictionary<Airport, TimeSpan>());
        var aircraft = new Aircraft(
            RegNo: new AircraftRegisterNumber("B764"),
            MinorType: minor,
            Capacity: new AircraftCapacity.PassengerCapacity(new Dictionary<PassengerClassId, ulong> {
                [Economy] = 100UL,
                [Business] = 10UL,
                [First] = 4UL,
            }));
        aircraft.SetUsability(new AircraftUsability(null, Beijing, Origin));
        return aircraft;
    }

    private static (List<FlightTask> Flights, List<FlightPassenger> Passengers, PassengerCancel Cancel, LinearMetaModel<Flt64> Model) Ready() {
        Aircraft aircraft = AircraftFixture();
        var flight = FlightLeg.Create(new FlightLegPlan(
            actualId: "CA764",
            no: "CA764",
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
        var flights = new List<FlightTask> { flight };
        var passenger = new Passenger(3UL, new List<(FlightTask, PassengerClassId)> { (flight, Economy) });
        var passengers = new List<FlightPassenger> { new(flight, passenger) };
        var cancel = new PassengerCancel(passengers);
        var change = new PassengerChange(flights, passengers, new[] { Economy, Business, First });
        var model = new LinearMetaModel<Flt64>();
        cancel.Register(model).IsOk.Should().BeTrue();
        change.Register(model).IsOk.Should().BeTrue();
        return (flights, passengers, cancel, model);
    }

    private static PassengerChange ChangeOf(List<FlightTask> flights, List<FlightPassenger> passengers) =>
        new(flights, passengers, new[] { Economy, Business, First });

    /// <summary>
    /// **一项对应"一位乘客 × 一个别的舱位"** / One term per passenger per other cabin.
    /// </summary>
    /// <remarks>
    /// 四点成对：`Y` 乘客、三个舱位 → **两项**（`J` 与 `F`，不含自己的 `Y`）/ 名字是 `passenger class change` / 分类是最小化 /
    /// **按舱位给的系数各归各位**（`J` 收 5、`F` 收 7）。
    /// 只数一项会让"把求和简化成每位乘客一项"通过——**那正好丢掉一半的改舱价格**。
    ///
    /// Four paired points: one `Y` passenger with three cabins yields **two terms** (`J` and `F`, not the own `Y`); the part is named `passenger class change`; its category is
    /// minimisation; and **the per-cabin coefficients land where they belong** (`J` sees 5, `F` sees 7). Counting a single term would let "simplified to one term per passenger" pass —
    /// **which loses exactly half of the change prices**.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldAddOneTermPerOtherCabin() {
        (List<FlightTask> flights, List<FlightPassenger> passengers, PassengerCancel _, LinearMetaModel<Flt64> model) = Ready();
        using LinearMetaModel<Flt64> unused = model;
        PassengerChange change = ChangeOf(flights, passengers);
        change.Register(model).IsOk.Should().BeTrue();
        var objective = new PassengerClassChangeMinimization(
            passengers,
            change,
            (_, cls) => cls == Business ? new Flt64(5) : new Flt64(7));
        int before = model.MetaSubObjects.Count;

        objective.Invoke(model).IsOk.Should().BeTrue();

        model.MetaSubObjects.Count.Should().Be(before + 1);
        MetaSubObject<Flt64> added = model.MetaSubObjects[^1];
        added.Name.Should().Be("passenger class change");
        added.Category.Should().Be(ObjectCategory.Minimum);
        added.Polynomial.Monomials.Should().HaveCount(2, "two other cabins, not three and not one");
        added.Polynomial.Monomials.Select(monomial => monomial.Coefficient.ToDouble())
            .Should().BeEquivalentTo(new[] { 5.0, 7.0 }, "each cabin keeps its own price");
    }

    /// <summary>
    /// **没注册变更变量就不是"零目标"，而是失败** / Without registered change variables this is a failure, not a zero objective.
    /// </summary>
    /// <remarks>
    /// 两点成对：未注册 → 返回失败 / 全零系数 → 不加目标零件。
    /// 未注册时静默返回成功，会让"目标根本没进模型"看起来像"没有改舱"——**而后者是业务结论，前者是漏接**。
    ///
    /// Two paired points: unregistered → failure; all-zero coefficients → no objective part. Returning success silently while unregistered makes "the objective never reached the model"
    /// look like "nobody changed cabin" — **the latter is a business conclusion, the former a missing wire**.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldFailWhenTheChangeVariablesAreMissing() {
        (List<FlightTask> flights, List<FlightPassenger> passengers, PassengerCancel _, LinearMetaModel<Flt64> model) = Ready();
        using LinearMetaModel<Flt64> unused = model;
        PassengerChange unregistered = ChangeOf(flights, passengers);

        new PassengerClassChangeMinimization(passengers, unregistered).Invoke(model).IsOk.Should().BeFalse();

        PassengerChange registered = ChangeOf(flights, passengers);
        registered.Register(model).IsOk.Should().BeTrue();
        int before = model.MetaSubObjects.Count;
        new PassengerClassChangeMinimization(passengers, registered, (_, _) => Flt64.Zero).Invoke(model).IsOk.Should().BeTrue();
        model.MetaSubObjects.Count.Should().Be(before, "an all-zero objective is not worth carrying");
    }

#if SCIP_INTEGRATION
    /// <summary>
    /// **真实求解：改舱价格按舱位算** / Real solve: the change price follows the cabin.
    /// </summary>
    /// <remarks>
    /// 三点成对：舱位变更强制到 1（`passengerClassChange[p, J] ≥ 1`）/ 系数按 `J` 给 5 / **`Obj == 5.0`**。
    /// 系数若被写成 7（`F` 的那一档）或统一成 1，都会立刻红——**这张表只有解出来的值能验证**。
    ///
    /// Three paired points: the cabin change is forced to one (`passengerClassChange[p, J] ≥ 1`); the coefficient for `J` is 5; and **`Obj == 5.0`**. A coefficient of 7 (the `F` price)
    /// or a flat 1 turns it red at once — **only the solved value can verify this table**.
    /// </remarks>
    [Fact]
    public async System.Threading.Tasks.Task Invoke_ShouldPriceTheClassChangePerCabin() {
        (List<FlightTask> flights, List<FlightPassenger> passengers, PassengerCancel _, LinearMetaModel<Flt64> model) = Ready();
        using LinearMetaModel<Flt64> unused = model;
        PassengerChange change = ChangeOf(flights, passengers);
        change.Register(model).IsOk.Should().BeTrue();
        Fuookami.Ospf.Core.Variable.UIntVar toBusiness = change.PassengerClassChange![passengers[0]][Business];
        toBusiness.Range.Geq(Fuookami.Ospf.Math.Algebra.Number.UInt64.One);
        new PassengerClassChangeMinimization(
            passengers,
            change,
            (_, cls) => cls == Business ? new Flt64(5) : new Flt64(7)).Invoke(model).IsOk.Should().BeTrue();

        var result = await new Fuookami.Ospf.Core.Plugin.Scip.ScipColumnGenerationSolver()
            .SolveMILPAsync("passenger_class_change_objective", model);

        result.IsOk.Should().BeTrue("the forced cabin change leaves a feasible region");
        result.Value!.Obj.ToDouble().Should().Be(5.0, "one passenger changing to J costs five, the F price is seven");
    }
#endif
}
