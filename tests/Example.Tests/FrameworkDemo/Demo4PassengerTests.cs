// 乘客行程/ Passenger itineraries.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Passenger.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **乘客行程** / Passenger itineraries.
/// </summary>
/// <remarks>
/// 三条规则组成一条行程：非空、全是航班任务、**相邻航段首尾相接**。`assert` 在 release 构建里整个消失——
/// 也就是生产上一条不连通的行程会被安静接受。这里改成抛异常，**因为这里是外部数据进来的地方**。
///
/// Three rules make an itinerary: non-empty, all flight tasks, and **adjacent legs connect**. An `assert` vanishes in release builds — so in production a
/// disconnected itinerary is quietly accepted. This implementation throws instead, because **this is where external data enters**.
/// </remarks>
public class Demo4PassengerTests {
    private static readonly DateTimeOffset Origin = new(2024, 1, 1, 8, 0, 0, TimeSpan.Zero);

    private static readonly Airport Beijing = new(new ICAO("ZBAA"), AirportType.International);

    private static readonly Airport Shanghai = new(new ICAO("ZSPD"), AirportType.Domestic);

    private static readonly Airport Guangzhou = new(new ICAO("ZGGG"), AirportType.Domestic);

    private static Aircraft AircraftFixture() {
        var type = AircraftType.GetOrAdd(new AircraftTypeCode("T758"));
        var minor = new AircraftMinorType(
            Type: type,
            Code: new AircraftMinorTypeCode("M758"),
            CostPerHour: new Flt64(1000),
            RouteFlyTime: new Dictionary<Route, TimeSpan>(),
            ConnectionTime: new Dictionary<Airport, TimeSpan>());
        var aircraft = new Aircraft(
            RegNo: new AircraftRegisterNumber("B758"),
            MinorType: minor,
            Capacity: new AircraftCapacity.PassengerCapacity(new Dictionary<PassengerClassId, ulong> {
                [new PassengerClassId("Y")] = 100UL,
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

    /// <summary>
    /// **路线由航段拼出来，中转按"多于两站"判** / The route is stitched from the legs, and a transfer means more than two stops.
    /// </summary>
    /// <remarks>
    /// 四点成对：两段相接 → 路线是 **三站**、出发/到达取首尾 / **是中转** / 直飞一段 → 路线两站、**不是中转** /
    /// 舱位按航班查得回来。`Route.Count > 1` 会把所有直飞都算成中转——**一站的差别，报告里却全错**。
    ///
    /// Four paired points: two connected legs give a **three-stop** route with departure and arrival at the ends; it **is** a transfer; a single direct leg gives two stops and is
    /// **not** a transfer; and the cabin is retrievable per flight. Using `Route.Count > 1` would call every direct flight a transfer — **a one-stop difference that makes every report
    /// wrong**.
    /// </remarks>
    [Fact]
    public void Passenger_ShouldStitchTheRouteAndJudgeTransfer() {
        Aircraft aircraft = AircraftFixture();
        FlightLeg first = Task(aircraft, "CA801", Beijing, Shanghai);
        FlightLeg second = Task(aircraft, "CA802", Shanghai, Guangzhou);
        var cls = new PassengerClassId("Y");

        var connecting = new Passenger(2UL, new List<(FlightTask, PassengerClassId)> { (first, cls), (second, cls) });
        connecting.Route.Should().HaveCount(3);
        connecting.Dep.Should().Be(Beijing);
        connecting.Arr.Should().Be(Guangzhou);
        connecting.Transfer.Should().BeTrue();
        connecting.Get(first).Should().Be(cls);
        connecting.Contains(second).Should().BeTrue();

        var direct = new Passenger(1UL, new List<(FlightTask, PassengerClassId)> { (first, cls) });
        direct.Route.Should().HaveCount(2);
        direct.Transfer.Should().BeFalse();
        direct.Get(second).Should().BeNull();
    }

    /// <summary>
    /// **不连通的行程被拒绝** / A disconnected itinerary is rejected.
    /// </summary>
    /// <remarks>
    /// 三点成对：北京→上海接 北京→广州（不连通）→ **抛 `ArgumentException`** / 空行程 → 抛 / 连通的两段 → 正常构造。
    /// `assert` 在 release 构建里不执行——**"生产上悄悄放过"正是这条判据存在的理由**。
    ///
    /// Three paired points: 北京→上海 followed by 北京→广州 (disconnected) **throws `ArgumentException`**; an empty itinerary throws; and two connected legs construct fine. An
    /// `assert` does not run in release builds — **"quietly accepted in production" is exactly why this criterion exists**.
    /// </remarks>
    [Fact]
    public void Passenger_ShouldRejectABrokenItinerary() {
        Aircraft aircraft = AircraftFixture();
        FlightLeg first = Task(aircraft, "CA803", Beijing, Shanghai);
        FlightLeg wrong = Task(aircraft, "CA804", Beijing, Guangzhou);
        var cls = new PassengerClassId("Y");

        Action disconnected = () => new Passenger(1UL, new List<(FlightTask, PassengerClassId)> { (first, cls), (wrong, cls) });
        disconnected.Should().Throw<ArgumentException>();

        // **空行程要"自己"报错**：去掉这道检查时构造函数会因为 `flights[0]` 抛 `ArgumentOutOfRangeException`，
        // 那也是 `ArgumentException` 的子类，只看类型照样绿。所以要认消息——**"碰巧也抛了异常"不是检查**。
        // **An empty itinerary must report itself**: without the check the constructor throws `ArgumentOutOfRangeException` from `flights[0]`, which is also an `ArgumentException`
        // subclass, so checking the type alone stays green. The message has to be matched — **"it happened to throw too" is not a check**.
        Action empty = () => new Passenger(1UL, new List<(FlightTask, PassengerClassId)>());
        empty.Should().Throw<ArgumentException>().WithMessage("*at least one flight*");

        Action connected = () => new Passenger(1UL, new List<(FlightTask, PassengerClassId)> { (first, cls) });
        connected.Should().NotThrow();
    }

    /// <summary>
    /// **航班乘客要求行程里有这一段** / A flight passenger requires that leg on the itinerary.
    /// </summary>
    /// <remarks>
    /// 三点成对：行程里的航段 → 构造成功且舱位对 / 人数取自乘客 / **不在行程里的航段 → 抛异常**。
    /// 少这条检查，航班乘客会带着 `null` 舱位流进后面的容量约束——**那时候再报错已经不知道该怪谁**。
    ///
    /// Three paired points: a leg on the itinerary constructs with the right cabin; the head count comes from the passenger; and **a leg not on the itinerary throws**. Without that
    /// check a flight passenger flows into the capacity constraints with a null cabin — **and by then it is impossible to tell whose fault it was**.
    /// </remarks>
    [Fact]
    public void FlightPassenger_ShouldRequireTheLeg() {
        Aircraft aircraft = AircraftFixture();
        FlightLeg first = Task(aircraft, "CA805", Beijing, Shanghai);
        FlightLeg second = Task(aircraft, "CA806", Shanghai, Guangzhou);
        var cls = new PassengerClassId("Y");
        var passenger = new Passenger(3UL, new List<(FlightTask, PassengerClassId)> { (first, cls), (second, cls) });

        var flightPassenger = new FlightPassenger(first, passenger);
        flightPassenger.Cls.Should().Be(cls);
        flightPassenger.Amount.Should().Be(3UL);
        flightPassenger.Indexed.Should().BeFalse();

        flightPassenger.SetIndexed();
        flightPassenger.Indexed.Should().BeTrue();

        Aircraft other = AircraftFixture();
        FlightLeg stranger = Task(other, "CA807", Beijing, Guangzhou);
        Action rejected = () => new FlightPassenger(stranger, passenger);
        rejected.Should().Throw<ArgumentException>();
    }
}
