// 容量表达式真的按任务算/ The capacity expressions really follow the tasks.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchCompilation.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **容量表达式真的按任务算** / The capacity expressions really follow the tasks.
/// </summary>
/// <remarks>
/// 留下两条没判据的规则：**零容量的束不进表达式**、**列变量按去重后的顺序取**。两条都需要真的任务实例，
/// 而造一个 `FlightTask` 要 `FlightLegPlan`（14 个参数）+ 飞机 + 机场。这里把那套最小夹具立起来，两条规则就能钉了。
///
/// 判据的关键是**看系数**：`Y` 舱 120 座的飞机，束的列变量系数就该是 120；同一航班用 0 座的飞机构造的束，
/// **一项都不该进表达式**——而"进了一项系数为 0 的单项式"是最容易发生的错误形态：
/// 解上完全等价，展开却多一项、目标里多一个列变量。
///
/// left two unpinned rules: **zero-capacity bunches stay out of the expression** and **column variables follow the deduplicated order**. Both need real task instances,
/// and building a `FlightTask` takes a `FlightLegPlan` (14 parameters) plus an aircraft and an airport. stands that minimal fixture up so the two rules become pinnable.
///
/// The criterion turns on **the coefficient**: an aircraft with 120 seats in cabin `Y` must give the bunch's column variable a coefficient of 120, while a bunch built for the same
/// flight on a 0-seat aircraft must contribute **nothing at all** — and "one monomial with a zero coefficient" is the easiest wrong form to ship: identical in the solution, but one
/// extra term in the expansion and one extra column variable in the objective.
/// </remarks>
public class Demo4FlightCapacityTaskTests {
    private static readonly DateTimeOffset Origin = new(2024, 1, 1, 8, 0, 0, TimeSpan.Zero);

    private static readonly Airport Dep = new(new ICAO("ZBAA"), AirportType.International);

    private static readonly Airport Arr = new(new ICAO("ZSPD"), AirportType.Domestic);

    private static Aircraft Seater(ulong seats) {
        var aircraft = new Aircraft(
            RegNo: new AircraftRegisterNumber("B" + seats),
            MinorType: Minor(),
            Capacity: new AircraftCapacity.PassengerCapacity(new Dictionary<PassengerClassId, ulong> {
                [new PassengerClassId("Y")] = seats,
            }));
        aircraft.SetUsability(new AircraftUsability(null, Dep, Origin));
        return aircraft;
    }

    private static Aircraft Freighter(double payload) {
        var aircraft = new Aircraft(
            RegNo: new AircraftRegisterNumber("F" + (int)payload),
            MinorType: Minor(),
            Capacity: new AircraftCapacity.CargoCapacity(new Flt64(payload)));
        aircraft.SetUsability(new AircraftUsability(null, Dep, Origin));
        return aircraft;
    }

    private static AircraftMinorType Minor() => new(
        Type: AircraftType.GetOrAdd(new AircraftTypeCode("T744")),
        Code: new AircraftMinorTypeCode("M744"),
        CostPerHour: new Flt64(1000),
        RouteFlyTime: new Dictionary<Route, TimeSpan>(),
        ConnectionTime: new Dictionary<Airport, TimeSpan>());

    private static FlightLeg Task(Aircraft aircraft, string no) => FlightLeg.Create(new FlightLegPlan(
        actualId: no,
        no: no,
        type: Dep.Icao.Code == Arr.Icao.Code ? FlightType.Domestic : FlightType.Domestic,
        date: Origin,
        aircraft: aircraft,
        enabledAircrafts: new HashSet<Aircraft> { aircraft },
        dep: Dep,
        arr: Arr,
        scheduledTime: new TimeRange(Origin, Origin.AddHours(2)),
        estimatedTime: null,
        actualTime: null,
        outTime: null,
        flightTaskStatus: FlightTaskStatus.NotDelay));

    /// <summary>
    /// **有座位的束进表达式、系数就是座位数；零座位的束一项都不进** / A bunch with seats enters with the seat count as coefficient; a zero-seat bunch contributes nothing.
    /// </summary>
    /// <remarks>
    /// 四点成对：客运容量注册成功 / 有座位的束给 `Y` 舱表达式带来**恰好一个**单项式、系数 120 /
    /// **零座位的束一个单项式都不带来**（不是系数为 0 的单项式）/ 货运开关关闭时 `Cargo` 为 `null`。
    ///
    /// 第二、三点是这里的正题：**"系数为 0 的单项式"和"没有单项式"在解上等价，在展开上不同**。
    ///
    /// Four paired points: the passenger capacity registers; the bunch with seats contributes **exactly one** monomial to the `Y` expression with coefficient 120; **the zero-seat bunch
    /// contributes no monomial at all** (not one with a zero coefficient); and the cargo table is null when the switch is off. The second and third are's point:
    /// **"a zero-coefficient monomial" and "no monomial" are equivalent in the solution but differ in the expansion.**
    /// </remarks>
    [Fact]
    public void AddColumns_ShouldCountSeatsAndSkipZeroCapacityBunches() {
        Aircraft seated = Seater(120UL);
        Aircraft empty = Seater(0UL);
        FlightLeg withSeats = Task(seated, "CA101");
        FlightLeg withoutSeats = Task(empty, "CA102");
        var tasks = new List<FlightTask> { withSeats };
        var aircrafts = new List<Aircraft> { seated, empty };
        using var model = new LinearMetaModel<Flt64>();
        var compilation = new BunchCompilation<FlightTaskBunch, FlightTask, Aircraft, FlightTaskAssignment>(tasks, aircrafts);
        compilation.Register(model).IsOk.Should().BeTrue();

        var bunches = new List<FlightTaskBunch> {
            new(seated, new List<FlightTask> { withSeats }, 0L),
            new(empty, new List<FlightTask> { withoutSeats }, 0L),
        };
        compilation.AddColumns(0UL, bunches, model).IsOk.Should().BeTrue();

        var capacity = new FlightCapacity(tasks, compilation);
        capacity.Register(model).IsOk.Should().BeTrue();
        capacity.AddColumns(0UL, bunches).IsOk.Should().BeTrue();

        capacity.Passenger.Should().NotBeNull();
        Fuookami.Ospf.Core.Symbol.LinearExpressionSymbol cabin = capacity.Passenger![withSeats][new PassengerClassId("Y")];
        cabin.Polynomial.Monomials.Should().HaveCount(1, "one seated bunch contributes exactly one term");
        cabin.Polynomial.Monomials[0].Coefficient.ToDouble().Should().Be(120.0);
        cabin.Polynomial.Monomials[0].Symbol.Should().NotBeNull();

        capacity.Cargo.Should().BeNull("no cargo aircraft means the cargo switch stays off");
    }

    /// <summary>
    /// **第二轮换掉第一轮，而不是叠上去** / The second round replaces the first instead of stacking on it.
    /// </summary>
    /// <remarks>
    /// 三点成对：第一轮之后一项（系数 120）/ **第二轮之后仍是一项** / 系数还是 120。追加版本在这里给两项，
    /// 四轮之后给五项——**跑十轮同一个束在表达式里出现十次**，解上未必立刻错，展开是十倍的项。
    ///
    /// Three paired points: one term after the first round (coefficient 120); **still one term after the second**; and the coefficient stays 120. An appending implementation would show
    /// two here and five after four rounds — **after ten rounds the same bunch appears ten times**, which may not break the solution immediately but makes the expansion tenfold.
    /// </remarks>
    [Fact]
    public void AddColumns_ShouldReplaceThePreviousRound() {
        // **两轮的束必须不同**：同一个束在第二轮会命中同一个列变量，追加与替换就分不开了。
        // **The two rounds must use different bunches**: the same bunch hits the same column variable in round two, and appending becomes indistinguishable from replacing.
        Aircraft wide = Seater(120UL);
        FlightLeg task = Task(wide, "CA701");
        Aircraft narrow = Seater(300UL);
        var tasks = new List<FlightTask> { task };
        var aircrafts = new List<Aircraft> { wide, narrow };
        using var model = new LinearMetaModel<Flt64>();
        var compilation = new BunchCompilation<FlightTaskBunch, FlightTask, Aircraft, FlightTaskAssignment>(tasks, aircrafts);
        compilation.Register(model).IsOk.Should().BeTrue();
        var firstRound = new List<FlightTaskBunch> { new(wide, new List<FlightTask> { task }, 0L) };
        var secondRound = new List<FlightTaskBunch> { new(narrow, new List<FlightTask> { task }, 1L) };
        var capacity = new FlightCapacity(tasks, compilation);
        capacity.Register(model).IsOk.Should().BeTrue();

        compilation.AddColumns(0UL, firstRound, model).IsOk.Should().BeTrue();
        capacity.AddColumns(0UL, firstRound).IsOk.Should().BeTrue();
        Fuookami.Ospf.Core.Symbol.LinearExpressionSymbol cabin = capacity.Passenger![task][new PassengerClassId("Y")];
        cabin.Polynomial.Monomials.Should().HaveCount(1);
        cabin.Polynomial.Monomials[0].Coefficient.ToDouble().Should().Be(120.0);

        compilation.AddColumns(1UL, secondRound, model).IsOk.Should().BeTrue();
        capacity.AddColumns(1UL, secondRound).IsOk.Should().BeTrue();

        cabin.Polynomial.Monomials.Should().HaveCount(1, "the second round replaces the first");
        cabin.Polynomial.Monomials[0].Coefficient.ToDouble().Should().Be(300.0, "the second round's aircraft is the one that counts");
    }

    /// <summary>
    /// **货运路径也按轮替换** / The cargo path also replaces per round.
    /// </summary>
    /// <remarks>
    /// 三点成对：注册后有 `Cargo` 表、`Passenger` 为 `null` / 第一轮之后一项、系数是货量 / **第二轮之后仍是一项、系数换成第二轮的货量**。
    /// 客运路径有判据不代表货运路径也有——**两条分支各写了一遍，就各自都得有判据**。
    ///
    /// Three paired points: registration yields a `Cargo` table with `Passenger` null; one term after the first round with the payload as coefficient; and **still one term after the second,
    /// with the second aircraft's payload**. Covering the passenger path does not cover the cargo path — **each branch is written separately, so each needs its own criterion**.
    /// </remarks>
    [Fact]
    public void AddColumns_ShouldReplaceOnTheCargoPathToo() {
        Aircraft light = Freighter(15.0);
        Aircraft heavy = Freighter(40.0);
        FlightLeg task = Task(light, "CA801");
        var tasks = new List<FlightTask> { task };
        var aircrafts = new List<Aircraft> { light, heavy };
        using var model = new LinearMetaModel<Flt64>();
        var compilation = new BunchCompilation<FlightTaskBunch, FlightTask, Aircraft, FlightTaskAssignment>(tasks, aircrafts);
        compilation.Register(model).IsOk.Should().BeTrue();
        var firstRound = new List<FlightTaskBunch> { new(light, new List<FlightTask> { task }, 0L) };
        var secondRound = new List<FlightTaskBunch> { new(heavy, new List<FlightTask> { task }, 1L) };
        var capacity = new FlightCapacity(tasks, compilation);
        capacity.Register(model).IsOk.Should().BeTrue();

        capacity.Passenger.Should().BeNull("a freighter has no cabins");
        capacity.Cargo.Should().NotBeNull();

        compilation.AddColumns(0UL, firstRound, model).IsOk.Should().BeTrue();
        capacity.AddColumns(0UL, firstRound).IsOk.Should().BeTrue();
        Fuookami.Ospf.Core.Symbol.LinearExpressionSymbol cargo = capacity.Cargo![task];
        cargo.Polynomial.Monomials.Should().HaveCount(1);
        cargo.Polynomial.Monomials[0].Coefficient.ToDouble().Should().Be(15.0);

        compilation.AddColumns(1UL, secondRound, model).IsOk.Should().BeTrue();
        capacity.AddColumns(1UL, secondRound).IsOk.Should().BeTrue();

        cargo.Polynomial.Monomials.Should().HaveCount(1, "the second round replaces the first on the cargo path as well");
        cargo.Polynomial.Monomials[0].Coefficient.ToDouble().Should().Be(40.0);
    }

    /// <summary>
    /// **同一任务用零座位飞机构造的束不参与** / A bunch built for the same task on a zero-seat aircraft does not take part.
    /// </summary>
    /// <remarks>
    /// 三点成对：同一航班两架飞机（120 座与 0 座），表达式里**只有一个单项式** / 系数仍是 120 / 加入的束数量是 2——
    /// **证明"少了那一项"是因为规则生效，而不是因为束没进编译**。
    ///
    /// Three paired points: the same flight on two aircraft (120 and 0 seats) yields **exactly one monomial**; the coefficient is still 120; and the number of bunches added is 2 —
    /// proving **the missing term comes from the rule, not from a bunch that never entered the compilation**.
    /// </remarks>
    [Fact]
    public void AddColumns_ShouldIgnoreTheZeroSeatBunchForTheSameTask() {
        Aircraft seated = Seater(120UL);
        Aircraft empty = Seater(0UL);
        FlightLeg task = Task(seated, "CA201");
        var tasks = new List<FlightTask> { task };
        var aircrafts = new List<Aircraft> { seated, empty };
        using var model = new LinearMetaModel<Flt64>();
        var compilation = new BunchCompilation<FlightTaskBunch, FlightTask, Aircraft, FlightTaskAssignment>(tasks, aircrafts);
        compilation.Register(model).IsOk.Should().BeTrue();

        var bunches = new List<FlightTaskBunch> {
            new(seated, new List<FlightTask> { task }, 0L),
            new(empty, new List<FlightTask> { task }, 1L),
        };
        Fuookami.Ospf.Utils.Functional.Result<IReadOnlyList<FlightTaskBunch>, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>> unduplicated =
            compilation.AddColumns(0UL, bunches, model);
        unduplicated.IsOk.Should().BeTrue();
        ((Fuookami.Ospf.Utils.Functional.Ok<IReadOnlyList<FlightTaskBunch>, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>)unduplicated)
            .Value.Should().HaveCount(2, "two distinct bunches enter the round");

        var capacity = new FlightCapacity(tasks, compilation);
        capacity.Register(model).IsOk.Should().BeTrue();
        capacity.AddColumns(0UL, bunches).IsOk.Should().BeTrue();

        Fuookami.Ospf.Core.Symbol.LinearExpressionSymbol cabin = capacity.Passenger![task][new PassengerClassId("Y")];
        cabin.Polynomial.Monomials.Should().HaveCount(1, "the zero-seat bunch must not add a term");
        cabin.Polynomial.Monomials[0].Coefficient.ToDouble().Should().Be(120.0);
    }
}
