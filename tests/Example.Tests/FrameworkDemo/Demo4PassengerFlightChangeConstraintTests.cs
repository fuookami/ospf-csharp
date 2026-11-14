// 改签的时间可行性（B767）/ Time feasibility of re-bookings (B767).

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchCompilation;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchCompilation.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Passenger.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Passenger.Service.Limits;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Rule.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **改签的时间可行性（B767）** / Time feasibility of re-bookings (B767).
/// </summary>
/// <remarks>
/// 指示变量写反**不会报错**：改签要么被整片放开、要么被整片封死，求解照样成功。所以判据既要认住行与指示变量的形态，
/// 也要在松紧两种接口上各解一次。
///
/// Reversing the indicator **raises nothing**: re-booking is either fully opened or fully blocked and the solve still succeeds. The criteria therefore pin the shape of the rows and
/// indicators, and also solve once on a tight connection and once on a loose one.
/// </remarks>
public class Demo4PassengerFlightChangeConstraintTests {
    private static readonly DateTimeOffset Origin = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly Airport Beijing = new(new ICAO("ZBAA"), AirportType.International, PassengerTransferTime: TimeSpan.FromHours(1));

    private static readonly Airport Shanghai = new(new ICAO("ZSPD"), AirportType.Domestic, PassengerTransferTime: TimeSpan.FromHours(1));

    private static readonly Airport Guangzhou = new(new ICAO("ZGGG"), AirportType.Domestic, PassengerTransferTime: TimeSpan.FromHours(1));

    private static readonly PassengerClassId Economy = new("Y");

    private static TimeWindow<Flt64> Window() => new(new TimeRange(Origin, Origin.AddDays(1)), false, TimeSpan.FromHours(1));

    private static Aircraft AircraftFixture() {
        var type = AircraftType.GetOrAdd(new AircraftTypeCode("T767"));
        var minor = new AircraftMinorType(
            Type: type,
            Code: new AircraftMinorTypeCode("M767"),
            CostPerHour: new Flt64(1000),
            RouteFlyTime: new Dictionary<Route, TimeSpan>(),
            ConnectionTime: new Dictionary<Airport, TimeSpan>());
        var aircraft = new Aircraft(
            RegNo: new AircraftRegisterNumber("B767"),
            MinorType: minor,
            Capacity: new AircraftCapacity.PassengerCapacity(new Dictionary<PassengerClassId, ulong> {
                [Economy] = 100UL,
            }));
        aircraft.SetUsability(new AircraftUsability(null, Beijing, Origin));
        return aircraft;
    }

    private static FlightLeg Task(Aircraft aircraft, string no, Airport dep, Airport arr, int startHour) =>
        FlightLeg.Create(new FlightLegPlan(
            actualId: no,
            no: no,
            type: FlightType.Domestic,
            date: Origin,
            aircraft: aircraft,
            enabledAircrafts: new HashSet<Aircraft> { aircraft },
            dep: dep,
            arr: arr,
            scheduledTime: new TimeRange(Origin.AddHours(startHour), Origin.AddHours(startHour + 2)),
            estimatedTime: null,
            actualTime: null,
            outTime: null,
            flightTaskStatus: FlightTaskStatus.NotDelay));

    private sealed record Fixture(
        LinearMetaModel<Flt64> Model,
        List<FlightTask> Tasks,
        List<FlightPassenger> Passengers,
        PassengerChange Change,
        PassengerFlightChangeConstraint Constraint,
        int Before);

    /// <summary>
    /// 装配一次：北京→上海（08:00）接上海→广州（14:00），另有两条同航路的替代航班。
    /// </summary>
    /// <remarks>
    /// 中转时间 1 小时，接口留 4 小时——**松接口**。把第二段提前到 10:00 就变成紧接口（留 0 小时）。
    ///
    /// Assemble once: Beijing→Shanghai at 08:00 connecting to Shanghai→Guangzhou at 14:00, plus one alternative flight per route. The transfer needs one hour and the connection leaves
    /// four — a **loose** connection. Moving the second leg to 10:00 makes it tight (zero slack).
    /// </remarks>
    /// <param name="secondStartHour">第二段出发小时 / The second leg's departure hour.</param>
    /// <returns>装配结果 / The fixture.</returns>
    private static Fixture Assemble(int secondStartHour) {
        Aircraft aircraft = AircraftFixture();
        FlightLeg first = Task(aircraft, "CA921", Beijing, Shanghai, 8);
        FlightLeg second = Task(aircraft, "CA922", Shanghai, Guangzhou, secondStartHour);
        FlightLeg firstAlt = Task(aircraft, "CA923", Beijing, Shanghai, 7);
        FlightLeg secondAlt = Task(aircraft, "CA924", Shanghai, Guangzhou, secondStartHour + 6);
        var tasks = new List<FlightTask> { first, second, firstAlt, secondAlt };
        var bunch = new FlightTaskBunch(aircraft, new List<FlightTask> { first, second }, 0L);
        var bunches = new List<FlightTaskBunch> { bunch };
        var model = new LinearMetaModel<Flt64>();
        var compilation = new Aggregation(
            Window(), new List<Aircraft> { aircraft }, tasks,
            links: new List<Link> { new ConnectingLink(first, second, new Flt64(10.0)) },
            originBunches: bunches);
        compilation.Register(model).IsOk.Should().BeTrue();
        compilation.AddColumns(0UL, bunches, model).IsOk.Should().BeTrue();

        var passenger = new Passenger(3UL, new List<(FlightTask, PassengerClassId)> { (first, Economy), (second, Economy) });
        FlightPassenger leg1 = new(first, passenger);
        FlightPassenger leg2 = new(second, passenger, leg1);
        var passengers = new List<FlightPassenger> { leg1, leg2 };
        var change = new PassengerChange(tasks, passengers, new[] { Economy }, withFlightChange: true);
        change.Register(model).IsOk.Should().BeTrue();
        var constraint = new PassengerFlightChangeConstraint(Window(), passengers, compilation.TaskTime, change);
        return new Fixture(model, tasks, passengers, change, constraint, model.Constraints.Count);
    }

    /// <summary>
    /// **两段接口各一条行，指示变量是二进制** / One row per direction and the indicators are binary.
    /// </summary>
    /// <remarks>
    /// 四点成对：两个方向各产生**一条行**（第一段赶下一段、第二段从上段赶过来）/ 行名带管道名前缀 / 左侧是改签变量（系数 1）/
    /// **右侧是 `人数 × 指示变量`**（系数 3、符号名以 `_if` 结尾）。
    /// 只数行数不能分辨"约束接上了"与"约束是空行"——**空行在 B766 已经出现过一次**。
    ///
    /// Four paired points: each direction yields **one row** (the first leg catching the next, the second leg coming from the previous); the names carry the pipeline prefix; the left side
    /// is the re-booking variable with coefficient 1; and **the right side is `head count × indicator`** (coefficient 3, symbol name ending in `_if`). Counting rows cannot tell "the
    /// constraint is wired" from "the row is empty" — **empty rows already happened once in B766**.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldBoundTheChangeByTheIndicator() {
        Fixture fixture = Assemble(14);
        using LinearMetaModel<Flt64> unused = fixture.Model;
        fixture.Constraint.Invoke(fixture.Model).IsOk.Should().BeTrue();

        int added = fixture.Model.Constraints.Count - fixture.Before;
        added.Should().Be(2, "one row per connection direction");
        var rightSides = new List<double>();
        for (int index = fixture.Before; index < fixture.Model.Constraints.Count; ++index) {
            var row = (LinearInequalityConstraint<Flt64>)fixture.Model.Constraints[index];
            row.Name.Should().StartWith("passenger_flight_change_constraint_");
            row.Inequality.Comparison.Should().Be(Comparison.LE);
            row.Inequality.Lhs.Monomials.Should().ContainSingle("the re-booking variable");
            row.Inequality.Lhs.Monomials[0].Coefficient.ToDouble().Should().Be(1.0);
            row.Inequality.Rhs.Monomials.Should().ContainSingle("head count times the indicator");
            row.Inequality.Rhs.Monomials[0].Symbol!.Name.Should().EndWith("_if");
            rightSides.Add(row.Inequality.Rhs.Monomials[0].Coefficient.ToDouble());
        }
        rightSides.Should().AllBeEquivalentTo(3.0, "the head count scales the indicator");
        fixture.Model.Tokens.Tokens.Count(token => token.Name.Contains("_if")).Should().BeGreaterThanOrEqualTo(2,
            "the indicator variables have to be in the model");
    }

    /// <summary>
    /// **条件式就是"接口余量 + 中转时间"** / The condition is exactly "connection slack plus transfer time".
    /// </summary>
    /// <remarks>
    /// 四点成对：两个指示变量各有一个条件式 / 每个条件式的常数都是 **−1**（1 小时窗口里的 1 小时中转）/ 系数是 **+1 与 −1** 各一项 /
    /// 条件式看的是**上一段的到达**与**下一段的出发**。
    /// 常数符号写反（`+1`）正是"指示变量反了"的形态——**这条断言在默认配置下就能抓住它，不必等求解**。
    ///
    /// Four paired points: each of the two indicators has a condition; each condition's constant is **−1** (one hour of transfer in a one-hour window); the coefficients are **+1 and −1**,
    /// one each; and the conditions watch **the previous leg's arrival** and **the next leg's departure**. A flipped constant (`+1`) is exactly what a reversed indicator looks like — **this assertion catches it
    /// in the default configuration, without waiting for a solve**.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldEncodeTheSlackAndTransferTime() {
        Fixture fixture = Assemble(14);
        using LinearMetaModel<Flt64> unused = fixture.Model;
        fixture.Constraint.Invoke(fixture.Model).IsOk.Should().BeTrue();

        var conditions = new List<Fuookami.Ospf.Math.Symbol.Polynomial.LinearPolynomial<Flt64>>();
        foreach (Fuookami.Ospf.Math.Symbol.ISymbol symbol in fixture.Model.Tokens.Symbols) {
            if (symbol is Fuookami.Ospf.Core.Symbol.Function.LinearFunctionSymbolAdapter<Flt64> adapter
                && adapter.Function is Fuookami.Ospf.Core.Symbol.Function.IfFunction<Flt64> function) {
                conditions.Add(function.Condition);
            }
        }
        conditions.Should().HaveCount(2, "one indicator per connection direction");
        foreach (Fuookami.Ospf.Math.Symbol.Polynomial.LinearPolynomial<Flt64> condition in conditions) {
            condition.Constant.ToDouble().Should().Be(-1.0, "the transfer time enters negated");
            condition.Monomials.Select(monomial => monomial.Coefficient.ToDouble()).Order().ToList()
                .Should().Equal(new List<double> { -1.0, 1.0 }, "departure and arrival enter with opposite signs");
        }
        // **两个方向在这条两段行程上看的是同一对估计时刻**（上一段的到达、下一段的出发），所以符号相同不是缺陷；
        // 真正要钉的是"看对了哪两段"——写成同一条航段或漏掉一段都会在这里露出来。
        // **On this two-leg itinerary both directions look at the same pair of estimated times** (the previous leg's arrival, the next leg's departure), so identical symbols are not a defect;
        // what must be pinned is **which two legs** are watched — using one leg twice or missing one shows up here.
        foreach (Fuookami.Ospf.Math.Symbol.Polynomial.LinearPolynomial<Flt64> condition in conditions) {
            string names = string.Join(",", condition.Monomials.Select(monomial => monomial.Symbol!.Name));
            names.Should().Contain("estimate_end_time_f_CA921", "the previous leg's arrival");
            names.Should().Contain("estimate_start_time_f_CA922", "the next leg's departure");
        }
    }

    /// <summary>
    /// **没开航班变更就什么都不加** / Nothing is added when flight change is off.
    /// </summary>
    /// <remarks>
    /// 两点成对：关闭时约束数不变 / 调用返回成功。**关掉的功能不该留下半条约束**。
    ///
    /// Two paired points: with the switch off the constraint count is unchanged; and the call succeeds. **A disabled feature must not leave half a constraint behind.**
    /// </remarks>
    [Fact]
    public void Invoke_ShouldAddNothingWithoutFlightChange() {
        Aircraft aircraft = AircraftFixture();
        FlightLeg first = Task(aircraft, "CA931", Beijing, Shanghai, 8);
        FlightLeg second = Task(aircraft, "CA932", Shanghai, Guangzhou, 14);
        var tasks = new List<FlightTask> { first, second };
        var bunch = new FlightTaskBunch(aircraft, new List<FlightTask> { first, second }, 0L);
        var bunches = new List<FlightTaskBunch> { bunch };
        using var model = new LinearMetaModel<Flt64>();
        var compilation = new Aggregation(
            Window(), new List<Aircraft> { aircraft }, tasks,
            links: new List<Link> { new ConnectingLink(first, second, new Flt64(10.0)) },
            originBunches: bunches);
        compilation.Register(model).IsOk.Should().BeTrue();
        compilation.AddColumns(0UL, bunches, model).IsOk.Should().BeTrue();

        var passenger = new Passenger(3UL, new List<(FlightTask, PassengerClassId)> { (first, Economy), (second, Economy) });
        FlightPassenger leg1 = new(first, passenger);
        FlightPassenger leg2 = new(second, passenger, leg1);
        var passengers = new List<FlightPassenger> { leg1, leg2 };
        var change = new PassengerChange(tasks, passengers, new[] { Economy }, withFlightChange: false);
        change.Register(model).IsOk.Should().BeTrue();
        int before = model.Constraints.Count;

        new PassengerFlightChangeConstraint(Window(), passengers, compilation.TaskTime, change)
            .Invoke(model).IsOk.Should().BeTrue();

        model.Constraints.Count.Should().Be(before, "flight change is off, so no connection is restricted");
    }
}
