// 乘客取消最小化/ The passenger cancellation objective.

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
/// **乘客取消最小化** / The passenger cancellation objective.
/// </summary>
/// <remarks>
/// 目标与上界必须成对：取消变量有"不超过人数"的上界，没有这条目标它会被压到上界（**取消能给别的约束腾地方**）；
/// 反过来只有目标没有上界，取消能压到零、上界就没有意义。
///
/// The objective and the bound must come as a pair: the cancellation variables carry a "no more than the head count" bound, and without this objective they are pushed to that
/// bound (**cancelling frees room for other constraints**); conversely an objective without a bound would let cancellation go to zero and make the bound pointless.
/// </remarks>
public class Demo4PassengerCancelMinimizationTests {
    private static readonly DateTimeOffset Origin = new(2024, 1, 1, 8, 0, 0, TimeSpan.Zero);

    private static readonly Airport Beijing = new(new ICAO("ZBAA"), AirportType.International);

    private static readonly Airport Shanghai = new(new ICAO("ZSPD"), AirportType.Domestic);

    private static readonly PassengerClassId Economy = new("Y");

    private static Aircraft AircraftFixture() {
        var type = AircraftType.GetOrAdd(new AircraftTypeCode("T763"));
        var minor = new AircraftMinorType(
            Type: type,
            Code: new AircraftMinorTypeCode("M763"),
            CostPerHour: new Flt64(1000),
            RouteFlyTime: new Dictionary<Route, TimeSpan>(),
            ConnectionTime: new Dictionary<Airport, TimeSpan>());
        var aircraft = new Aircraft(
            RegNo: new AircraftRegisterNumber("B763"),
            MinorType: minor,
            Capacity: new AircraftCapacity.PassengerCapacity(new Dictionary<PassengerClassId, ulong> {
                [Economy] = 100UL,
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

    private static (List<FlightPassenger> Passengers, PassengerCancel Cancel, LinearMetaModel<Flt64> Model) Ready() {
        Aircraft aircraft = AircraftFixture();
        FlightLeg flight = Task(aircraft, "CA763");
        var passenger = new Passenger(4UL, new List<(FlightTask, PassengerClassId)> { (flight, Economy) });
        var passengers = new List<FlightPassenger> { new(flight, passenger) };
        var cancel = new PassengerCancel(passengers);
        var model = new LinearMetaModel<Flt64>();
        cancel.Register(model).IsOk.Should().BeTrue();
        return (passengers, cancel, model);
    }

    /// <summary>
    /// **目标项真的进了模型，系数按乘客走** / The objective term really reaches the model, with the per-passenger coefficient.
    /// </summary>
    /// <remarks>
    /// 四点成对：调用后模型多出一个目标零件 / 名字是 `passenger cancel` / 分类是最小化 / **系数就是传进去的那个 2**
    /// （缺省值是 1，所以 2 能区分"用了系数"和"用了缺省"）。
    ///
    /// Four paired points: the call adds one objective part; it is named `passenger cancel`; its category is minimisation; and **the coefficient is the 2 that was passed in** (the default
    /// is 1, so 2 distinguishes "used the coefficient" from "used the default").
    /// </remarks>
    [Fact]
    public void Invoke_ShouldAddTheObjectiveWithTheCoefficient() {
        (List<FlightPassenger> passengers, PassengerCancel cancel, LinearMetaModel<Flt64> model) = Ready();
        using LinearMetaModel<Flt64> unused = model;
        var objective = new PassengerCancelMinimization(passengers, cancel, _ => new Flt64(2));
        int before = model.MetaSubObjects.Count;

        objective.Invoke(model).IsOk.Should().BeTrue();

        model.MetaSubObjects.Count.Should().Be(before + 1, "the objective is one part");
        MetaSubObject<Flt64> added = model.MetaSubObjects[^1];
        added.Name.Should().Be("passenger cancel");
        added.Category.Should().Be(ObjectCategory.Minimum);
        added.Polynomial.Monomials.Should().ContainSingle();
        added.Polynomial.Monomials[0].Coefficient.ToDouble().Should().Be(2.0, "the coefficient comes from the caller");
        added.Polynomial.Constant.ToDouble().Should().Be(0.0);
    }

    /// <summary>
    /// **系数为零的目标不落进模型** / A zero-coefficient objective does not reach the model.
    /// </summary>
    /// <remarks>
    /// 两点成对：全零系数 → 目标零件数不变 / 空乘客表 → 也不加。
    /// 加一个"零系数目标"不会错，只会让模型多出一批永远为零的项——**而目标里的零项会一直陪你到最后一句解释里**。
    ///
    /// Two paired points: all-zero coefficients leave the objective part count unchanged; an empty passenger list adds nothing either. A zero-coefficient objective is not wrong, it only
    /// Adds </remarks>
    [Fact]
    public void Invoke_ShouldSkipAZeroCoefficientObjective() {
        (List<FlightPassenger> passengers, PassengerCancel cancel, LinearMetaModel<Flt64> model) = Ready();
        using LinearMetaModel<Flt64> unused = model;
        int before = model.MetaSubObjects.Count;

        new PassengerCancelMinimization(passengers, cancel, _ => Flt64.Zero).Invoke(model).IsOk.Should().BeTrue();
        model.MetaSubObjects.Count.Should().Be(before);

        new PassengerCancelMinimization(Array.Empty<FlightPassenger>(), cancel).Invoke(model).IsOk.Should().BeTrue();
        model.MetaSubObjects.Count.Should().Be(before);
    }

#if SCIP_INTEGRATION
    /// <summary>
    /// **真实求解：取消被压到"不得不取消"的那一点** / Real solve: cancellation is pushed to exactly the required point.
    /// </summary>
    /// <remarks>
    /// 三点成对：约束 "cancel ≥ 1" 逼出取消量 / 目标系数 2 / **`Obj == 2.0`**。
    /// 只看"目标零件进了模型"会让"系数写成 1"通过——**目标系数只有解出来的值能证明**。
    ///
    /// Three paired points: a "cancel ≥ 1" row forces some cancellation; the coefficient is 2; and **`Obj == 2.0`**. Checking only "the objective part reached the model" would let a
    /// coefficient of 1 pass — **only the solved value proves the objective coefficient**.
    /// </remarks>
    [Fact]
    public async System.Threading.Tasks.Task Invoke_ShouldDriveTheObjectiveValue() {
        (List<FlightPassenger> passengers, PassengerCancel cancel, LinearMetaModel<Flt64> model) = Ready();
        using LinearMetaModel<Flt64> unused = model;
        cancel.Of(passengers[0]).Range.Geq(Fuookami.Ospf.Math.Algebra.Number.UInt64.One);
        new PassengerCancelMinimization(passengers, cancel, _ => new Flt64(2)).Invoke(model).IsOk.Should().BeTrue();

        var solver = new Fuookami.Ospf.Core.Plugin.Scip.ScipColumnGenerationSolver();
        var result = await solver.SolveMILPAsync("passenger_cancel_objective", model);

        result.IsOk.Should().BeTrue("the forced cancellation leaves a feasible region");
        result.Value!.Obj.ToDouble().Should().Be(2.0, "the cancellation is forced to one and costs two per unit");
    }
#endif
}
