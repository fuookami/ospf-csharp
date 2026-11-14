// 航班人数表达式（B760）/ The passenger-amount expressions (B760).

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Passenger.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **航班人数表达式（B760）** / The passenger-amount expressions (B760).
/// </summary>
/// <remarks>
/// 每个（航班，舱位）一个表达式，由四件事合成：原人数、取消、变更出去、变更进来。最容易写错的是第三点的两个分支——
/// **自己那一档是"减出去"，别的档是"加进来"**。写反会让人数在舱位之间凭空翻倍或归零，而**容量约束只看总数、照样能过**。
///
/// One expression per (flight, cabin) from four ingredients: the original head count, cancellation, changing out, changing in. The easiest mistake is the two branches of the third —
/// **the own cabin subtracts, other cabins add**. Swapping them makes the head count double or vanish between cabins while **the capacity constraints only look at the total and still
/// pass**.
/// </remarks>
public class Demo4PassengerAmountTests {
    private static readonly DateTimeOffset Origin = new(2024, 1, 1, 8, 0, 0, TimeSpan.Zero);

    private static readonly Airport Beijing = new(new ICAO("ZBAA"), AirportType.International);

    private static readonly Airport Shanghai = new(new ICAO("ZSPD"), AirportType.Domestic);

    private static readonly PassengerClassId Economy = new("Y");
    private static readonly PassengerClassId Business = new("J");

    private static Aircraft AircraftFixture() {
        var type = AircraftType.GetOrAdd(new AircraftTypeCode("T760"));
        var minor = new AircraftMinorType(
            Type: type,
            Code: new AircraftMinorTypeCode("M760"),
            CostPerHour: new Flt64(1000),
            RouteFlyTime: new Dictionary<Route, TimeSpan>(),
            ConnectionTime: new Dictionary<Airport, TimeSpan>());
        var aircraft = new Aircraft(
            RegNo: new AircraftRegisterNumber("B760"),
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

    private static (List<FlightTask> Flights, Dictionary<FlightTask, IReadOnlyList<FlightPassenger>> Passengers, PassengerCancel Cancel, PassengerChange Change) Ready(bool withFlightChange) {
        Aircraft aircraft = AircraftFixture();
        FlightLeg first = Task(aircraft, "CA760");
        FlightLeg later = Task(aircraft, "CA761", hourOffset: 4);
        var flights = new List<FlightTask> { first, later };
        var passenger = new Passenger(4UL, new List<(FlightTask, PassengerClassId)> { (first, Economy) });
        var flightPassengers = new List<FlightPassenger> { new(first, passenger) };
        var passengers = new Dictionary<FlightTask, IReadOnlyList<FlightPassenger>> {
            [first] = flightPassengers,
            [later] = Array.Empty<FlightPassenger>(),
        };
        var cancel = new PassengerCancel(flightPassengers);
        var change = new PassengerChange(flights, flightPassengers, new[] { Economy, Business }, withFlightChange);
        return (flights, passengers, cancel, change);
    }

    private static Demo4PassengerAmountTestsContext Build(bool withFlightChange) {
        (List<FlightTask> flights, Dictionary<FlightTask, IReadOnlyList<FlightPassenger>> passengers, PassengerCancel cancel, PassengerChange change) = Ready(withFlightChange);
        var model = new LinearMetaModel<Flt64>();
        cancel.Register(model).IsOk.Should().BeTrue();
        change.Register(model).IsOk.Should().BeTrue();
        var amount = new PassengerAmount(flights, passengers, new[] { Economy, Business }, cancel, change);
        amount.Register(model).IsOk.Should().BeTrue();
        return new Demo4PassengerAmountTestsContext(model, flights, passengers, cancel, change, amount);
    }

    private sealed record Demo4PassengerAmountTestsContext(
        LinearMetaModel<Flt64> Model,
        IReadOnlyList<FlightTask> Flights,
        IReadOnlyDictionary<FlightTask, IReadOnlyList<FlightPassenger>> Passengers,
        PassengerCancel Cancel,
        PassengerChange Change,
        PassengerAmount Amount);

    /// <summary>
    /// **原人数在常数项，取消项是一减** / The head count is the constant and cancellation is a minus-one term.
    /// </summary>
    /// <remarks>
    /// 三点成对：`Y` 舱表达式的常数项是 **4**（人数）/ **两个系数 −1 的单项式**（取消 + 改到 J 去）/ `J` 舱没有原人数（常数 0）、
    /// 但**有一个系数 +1 的变更项**（改到 J 来的人）。
    /// 把"取消"写成与舱位相关，或者漏掉"改到别的舱位"的加项，这三点里至少一点会红。
    ///
    /// Three paired points: the `Y` expression's constant is **4** (the head count); it holds **two** monomials with coefficient **−1** (cancellation plus moving to `J`); and the `J`
    /// expression has no original head count (constant 0) but **one change term with coefficient +1**. Writing cancellation as cabin-dependent, or dropping the "moved to another cabin"
    /// term, turns at least one of these red.
    /// </remarks>
    [Fact]
    public void Register_ShouldAddTheHeadCountAndSubtractCancellation() {
        Demo4PassengerAmountTestsContext context = Build(withFlightChange: false);
        using LinearMetaModel<Flt64> unused = context.Model;
        LinearExpressionSymbol economy = context.Amount.Amounts![context.Flights[0]][Economy];

        economy.Polynomial.Constant.ToDouble().Should().Be(4.0, "the passenger's head count is a constant");
        // **两项都减**：取消（与舱位无关）与"改到别的舱位去"。第一版判据只写了"一项"，被这两项当场纠出来。
        // **Both subtract**: cancellation (cabin-independent) and "moved to another cabin". The first version of this criterion said "one term" and was corrected on the spot.
        economy.Polynomial.Monomials.Should().HaveCount(2, "cancellation plus the cabin-change-out term");
        economy.Polynomial.Monomials.Should().OnlyContain(monomial => monomial.Coefficient.ToDouble() == -1.0,
            "leaving this cabin takes one person out, exactly like cancelling");

        // **取消项出现在每一个舱位的表达式里**——这是基线的写法：取消与舱位无关，所以每个分档都要减。
        // 只看"这一档没人"会以为这里该是零项，其实永远有一项取消。
        // **The cancellation term appears in every cabin's expression** — that is what the baseline does: cancelling is cabin-independent, so every split subtracts it. Reading "nobody is in
        // this cabin" as "no terms here" is wrong; the cancellation term is always present.
        LinearExpressionSymbol business = context.Amount.Amounts[context.Flights[0]][Business];
        business.Polynomial.Constant.ToDouble().Should().Be(0.0, "nobody starts in the business cabin");
        business.Polynomial.Monomials.Should().HaveCount(2, "the cancellation term plus the cabin-change target");
        business.Polynomial.Monomials.Should().ContainSingle(monomial => monomial.Coefficient.ToDouble() == 1.0,
            "a passenger moving here adds people");
        business.Polynomial.Monomials.Should().ContainSingle(monomial => monomial.Coefficient.ToDouble() == -1.0,
            "cancellation subtracts in this cabin too");
    }

    /// <summary>
    /// **没开航班变更就没有"变更进来"的项** / Without flight change there is no "changing in" term.
    /// </summary>
    /// <remarks>
    /// 两点成对：另一班的 `Y` 舱表达式**常数 0、没有单项式**（没人改签到那一班）/ 打开开关后**同一位置出现一项**。
    /// 关闭时凭空多出一项，或者打开时一项都没有，都说明"变更进来"的归集写错了方向。
    ///
    /// Two paired points: the other flight's `Y` expression has **constant 0 and no monomials** (nobody moves onto it); and with the switch on **a term appears in that very place**.
    /// Inventing a term while off, or missing it while on, means the "changing in" grouping is looking the wrong way.
    /// </remarks>
    [Fact]
    public void Register_ShouldOnlyMovePassengersInWhenFlightChangeIsOn() {
        Demo4PassengerAmountTestsContext off = Build(withFlightChange: false);
        using LinearMetaModel<Flt64> unusedOff = off.Model;
        LinearExpressionSymbol laterOff = off.Amount.Amounts![off.Flights[1]][Economy];
        laterOff.Polynomial.Constant.ToDouble().Should().Be(0.0);
        laterOff.Polynomial.Monomials.Should().BeEmpty("nobody moves onto the later flight while flight change is off");

        Demo4PassengerAmountTestsContext on = Build(withFlightChange: true);
        using LinearMetaModel<Flt64> unusedOn = on.Model;
        LinearExpressionSymbol laterOn = on.Amount.Amounts![on.Flights[1]][Economy];
        laterOn.Polynomial.Monomials.Should().NotBeEmpty("the later flight is a valid target for this passenger");
        laterOn.Polynomial.Monomials.Should().OnlyContain(monomial => monomial.Coefficient.ToDouble() == 1.0);
    }

    /// <summary>
    /// **表达式真的进了模型** / The expressions really reach the model.
    /// </summary>
    /// <remarks>
    /// 两点成对：模型里有 `passenger_amount_` 开头的符号 / 重注册不重复添加。
    /// 只查 `Amounts` 那张表会让"建了符号没加进模型"通过——**内存里的表和模型里的符号是两件事**（这一族缺陷在 B740 已经出现过一次）。
    ///
    /// Two paired points: the model holds symbols starting with `passenger_amount_`; and re-registration adds nothing. Checking only the `Amounts` table lets "built the symbols but
    /// never added them" pass — **an in-memory table and a model symbol are two different things** (this family of defects already appeared in B740).
    /// </remarks>
    [Fact]
    public void Register_ShouldReachTheModelOnce() {
        Demo4PassengerAmountTestsContext context = Build(withFlightChange: false);
        string joined = string.Join(" | ", context.Model.Tokens.Symbols.Select(symbol => symbol.Name ?? string.Empty));
        joined.Should().Contain("passenger_amount_");

        int afterFirst = context.Model.Tokens.Symbols.Count;
        context.Amount.Register(context.Model).IsOk.Should().BeTrue();
        context.Model.Tokens.Symbols.Count.Should().Be(afterFirst, "re-registration must not add the expressions twice");
    }
}
