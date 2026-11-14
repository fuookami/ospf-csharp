// 机队按检查点计数（B747）/ The fleet counts by checkpoint (B747).

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchCompilation.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **机队按检查点计数（B747）** / The fleet counts by checkpoint (B747).
/// </summary>
/// <remarks>
/// `FleetBalance` 算的是"今晚有多少架这种机型落在/停在某个机场"。检查点取法的两条分支不能合并：
/// **在飞的算落点（`bunch.arr`），不在飞的算当前位置（`usability.location`）**——合并成一条，"能落在浦东的机数"就无从算起。
///
/// `FleetBalance` answers "how many aircraft of this type land at or sit at some airport tonight". Its two checkpoint branches cannot be merged: **airborne aircraft count at their
/// arrival airport (`bunch.arr`), idle ones at their current location (`usability.location`)** — merged into one, "how many can land at ZSPD" becomes unanswerable.
/// </remarks>
public class Demo4FleetBalanceTests {
    private static readonly DateTimeOffset Origin = new(2024, 1, 1, 8, 0, 0, TimeSpan.Zero);

    private static readonly Airport Beijing = new(new ICAO("ZBAA"), AirportType.International);

    private static readonly Airport Shanghai = new(new ICAO("ZSPD"), AirportType.Domestic);

    private static Aircraft AircraftFixture(string no, Airport location) {
        var type = AircraftType.GetOrAdd(new AircraftTypeCode("T747"));
        var minor = new AircraftMinorType(
            Type: type,
            Code: new AircraftMinorTypeCode("M747"),
            CostPerHour: new Flt64(1000),
            RouteFlyTime: new Dictionary<Route, TimeSpan>(),
            ConnectionTime: new Dictionary<Airport, TimeSpan>());
        var aircraft = new Aircraft(
            RegNo: new AircraftRegisterNumber(no),
            MinorType: minor,
            Capacity: new AircraftCapacity.PassengerCapacity(new Dictionary<PassengerClassId, ulong> {
                [new PassengerClassId("Y")] = 100UL,
            }));
        aircraft.SetUsability(new AircraftUsability(null, location, Origin));
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

    private static (List<Aircraft> Aircrafts, List<FlightTaskBunch> Bunches, List<FlightTask> Tasks, LinearMetaModel<Flt64> Model, BunchCompilation<FlightTaskBunch, FlightTask, Aircraft, FlightTaskAssignment> Compilation) Ready() {
        Aircraft flying = AircraftFixture("B747A", Beijing);
        Aircraft idle = AircraftFixture("B747B", Beijing);
        FlightLeg leg = Task(flying, "CA401");
        var tasks = new List<FlightTask> { leg };
        var aircrafts = new List<Aircraft> { flying, idle };
        var bunches = new List<FlightTaskBunch> {
            new(flying, new List<FlightTask> { leg }, 0L),
        };
        var model = new LinearMetaModel<Flt64>();
        var compilation = new BunchCompilation<FlightTaskBunch, FlightTask, Aircraft, FlightTaskAssignment>(tasks, aircrafts);
        compilation.Register(model).IsOk.Should().BeTrue();
        return (aircrafts, bunches, tasks, model, compilation);
    }

    /// <summary>
    /// **在飞的算落点、不在飞的算当前位置** / Airborne counts at arrival, idle at its location.
    /// </summary>
    /// <remarks>
    /// 三点成对：两台同型飞机 → **两个检查点**（北京与上海），因为一台在飞一台不在 / 各限额 1 架 / 两个检查点里装的是**不同的**飞机。
    /// 把两条分支合并成一条（都按当前位置），就会只剩**一个**检查点、限额 2——**那正好把"能落在浦东的机数"抹掉了**。
    ///
    /// Three paired points: two aircraft of the same type give **two checkpoints** (Beijing and Shanghai) because one is airborne and one is not; each bound is 1; and the two
    /// checkpoints hold **different** aircraft. Merging the branches (both by current location) leaves **one** checkpoint with a bound of 2 — which **erases exactly the
    /// answer "how many can land at ZSPD"**.
    /// </remarks>
    [Fact]
    public void Limits_ShouldSplitAirborneFromIdleAircraft() {
        (List<Aircraft> aircrafts, List<FlightTaskBunch> bunches, List<FlightTask> _, LinearMetaModel<Flt64> model, BunchCompilation<FlightTaskBunch, FlightTask, Aircraft, FlightTaskAssignment> compilation) = Ready();
        using LinearMetaModel<Flt64> unused = model;
        var balance = new FleetBalance(aircrafts, bunches, compilation);

        balance.Limits.Should().HaveCount(2, "the airborne aircraft lands at ZSPD while the other sits at ZBAA");
        balance.Limits.Select(pair => pair.Key.Airport).Should().Contain(Beijing).And.Contain(Shanghai);
        balance.Limits.Select(pair => pair.Value.Amount).Should().OnlyContain(amount => amount == 1UL);
        balance.Limits.SelectMany(pair => pair.Value.Aircrafts).Should().HaveCount(2, "each aircraft belongs to exactly one checkpoint");
    }

    /// <summary>
    /// **每个检查点的机队表达式按飞机各占一项** / Every checkpoint's fleet expression holds one term per aircraft.
    /// </summary>
    /// <remarks>
    /// 四点成对：注册成功 / 两个检查点各一个 `fleet` 符号 / 每项系数都是 **1**（一台飞机算一架）/ 符号名是 `fleet_0`、`fleet_1`。
    /// 系数写成别的值（比如按座位数加权）会让"机数上限"变成"座位上限"——**名字还叫 fleet，语义已经换了**。
    ///
    /// Four paired points: registration succeeds; each checkpoint gets one `fleet` symbol whose terms all have coefficient **1** (one aircraft counts as one); and the names are
    /// `fleet_0`, `fleet_1`. Writing any other coefficient (say, weighting by seats) turns a "bound on aircraft" into a "bound on seats" — **still named fleet, semantically replaced**.
    /// </remarks>
    [Fact]
    public void Register_ShouldBuildOneFleetSymbolPerCheckpoint() {
        (List<Aircraft> aircrafts, List<FlightTaskBunch> bunches, List<FlightTask> _, LinearMetaModel<Flt64> model, BunchCompilation<FlightTaskBunch, FlightTask, Aircraft, FlightTaskAssignment> compilation) = Ready();
        using LinearMetaModel<Flt64> unused = model;
        var balance = new FleetBalance(aircrafts, bunches, compilation);

        balance.Register(model).IsOk.Should().BeTrue();

        balance.Fleets.Should().HaveCount(2);
        balance.Slacks.Should().HaveCount(2);
        // **松弛也必须真的进模型**：只查内存里的表会让"建了符号但没加进模型"通过。
        // **The slacks must really reach the model too**: checking only the in-memory table lets "built the symbol but never added it" pass.
        string joined = string.Join(" | ", model.Tokens.Symbols.Select(symbol => symbol.Name ?? string.Empty));
        joined.Should().Contain("fleet_0");
        joined.Should().Contain("fleet_slack_0");

        foreach (KeyValuePair<int, LinearExpressionSymbol> fleet in balance.Fleets) {
            fleet.Value.Name.Should().Be("fleet_" + fleet.Key);
            fleet.Value.Polynomial.Monomials.Should().HaveCount(1, "each checkpoint holds exactly one aircraft here");
            fleet.Value.Polynomial.Monomials[0].Coefficient.ToDouble().Should().Be(1.0);
        }
    }

    /// <summary>
    /// **加列是"换掉"而不是"追加"** / Adding columns replaces rather than appends.
    /// </summary>
    /// <remarks>
    /// 四点成对：注册后 `fleet` 里是 `z` 项 / 加列之后**只剩列变量一项**（`z` 被清掉）/ 系数是 1 / 再加一轮仍是**一项**。
    /// 追加版本的实现在这里会给出两项、四轮之后给五项——**跑十轮同一个束在表达式里出现十次**，解上未必立刻错，展开却是十倍的项。
    ///
    /// Four paired points: after registration the fleet holds the `z` terms; after adding columns **only the column term remains** (the `z` terms are cleared); the coefficient is 1;
    /// and adding another round still leaves **one** term. An appending implementation would show two terms here and five after four rounds — **after ten rounds the same bunch appears ten
    /// times in the expression**, which may not break the solution immediately but makes the expansion ten times longer.
    /// </remarks>
    [Fact]
    public void AddColumns_ShouldReplaceTheRegisteredTerms() {
        (List<Aircraft> aircrafts, List<FlightTaskBunch> bunches, List<FlightTask> _, LinearMetaModel<Flt64> model, BunchCompilation<FlightTaskBunch, FlightTask, Aircraft, FlightTaskAssignment> compilation) = Ready();
        using LinearMetaModel<Flt64> unused = model;
        var balance = new FleetBalance(aircrafts, bunches, compilation);
        balance.Register(model).IsOk.Should().BeTrue();
        int checkedIndex = balance.Limits
            .Select((pair, index) => (pair, index))
            .First(item => item.pair.Value.Aircrafts.Contains(aircrafts[0]))
            .index;

        compilation.AddColumns(0UL, bunches, model).IsOk.Should().BeTrue();
        balance.AddColumns(0UL, bunches).IsOk.Should().BeTrue();

        LinearExpressionSymbol fleet = balance.Fleets[checkedIndex];
        fleet.Polynomial.Monomials.Should().HaveCount(1, "the registered z terms are cleared before this round's columns are added");
        fleet.Polynomial.Monomials[0].Coefficient.ToDouble().Should().Be(1.0);

        compilation.AddColumns(1UL, bunches, model).IsOk.Should().BeTrue();
        balance.AddColumns(1UL, bunches).IsOk.Should().BeTrue();
        fleet.Polynomial.Monomials.Should().HaveCount(1, "a second round replaces the first, it does not stack on top of it");
    }

    /// <summary>
    /// **重注册不重建符号** / Re-registration does not rebuild the symbols.
    /// </summary>
    /// <remarks>
    /// 两点成对：注册两次之后符号数不变 / 表还是同一份。重建会让同一个检查点在模型里出现两个符号，求解时各自取值。
    ///
    /// Two paired points: registering twice leaves the symbol count unchanged; the tables are the same. Rebuilding would put two symbols for one checkpoint into the model, each
    /// taking its own value at solve time.
    /// </remarks>
    [Fact]
    public void Register_ShouldBeIdempotent() {
        (List<Aircraft> aircrafts, List<FlightTaskBunch> bunches, List<FlightTask> _, LinearMetaModel<Flt64> model, BunchCompilation<FlightTaskBunch, FlightTask, Aircraft, FlightTaskAssignment> compilation) = Ready();
        using LinearMetaModel<Flt64> unused = model;
        var balance = new FleetBalance(aircrafts, bunches, compilation);

        balance.Register(model).IsOk.Should().BeTrue();
        int afterFirst = model.Tokens.Symbols.Count;
        LinearExpressionSymbol fleet = balance.Fleets[0];
        balance.Register(model).IsOk.Should().BeTrue();

        model.Tokens.Symbols.Count.Should().Be(afterFirst, "no symbol may be added twice");
        balance.Fleets[0].Should().BeSameAs(fleet, "the tables must not be rebuilt");
    }
}
