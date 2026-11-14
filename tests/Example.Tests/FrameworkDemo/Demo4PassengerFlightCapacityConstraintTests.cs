// 航班容量约束（B766）/ The flight capacity constraint (B766).

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
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
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **航班容量约束（B766）** / The flight capacity constraint (B766).
/// </summary>
/// <remarks>
/// 这是乘客域与机队层之间**唯一的接缝**：左边是人数表达式（B760），右边是按机型分档的容量表达式（B744）。
///
/// **表达式符号不能直接塞进约束**：把两个表达式符号各包成一项交给 `AddConstraint`，存下来的行会是**空的**
/// （两侧单项式与常数都被抹平），模型永远可行而求解成功——这一批的第一次实现就是这样，靠"130 人 120 座也能解"抓出来。
/// 正确写法是把两侧的多项式**展开**再交进去。
///
/// This is **the only seam** between the passenger domain and the fleet layer: the left side is the amount expression (B760), the right side the per-cabin capacity expression (B744).
///
/// **An expression symbol cannot be dropped into a constraint as a term**: wrapping each expression in one monomial and handing it to `AddConstraint` stores an **empty** row (both sides'
/// monomials and constants are wiped), the model stays always feasible and the solve still succeeds — that is exactly what this batch's first implementation did, and the "130 passengers
/// into 120 seats still solves" case caught it. The correct form expands both polynomials before adding the row.
/// </remarks>
public class Demo4PassengerFlightCapacityConstraintTests {
    private static readonly DateTimeOffset Origin = new(2024, 1, 1, 8, 0, 0, TimeSpan.Zero);

    private static readonly Airport Beijing = new(new ICAO("ZBAA"), AirportType.International);

    private static readonly Airport Shanghai = new(new ICAO("ZSPD"), AirportType.Domestic);

    private static readonly PassengerClassId Economy = new("Y");

    private static TimeWindow<Flt64> Window() => new(new TimeRange(Origin, Origin.AddDays(1)), false, TimeSpan.FromHours(8));

    private static Aircraft AircraftFixture() {
        var type = AircraftType.GetOrAdd(new AircraftTypeCode("T766"));
        var minor = new AircraftMinorType(
            Type: type,
            Code: new AircraftMinorTypeCode("M766"),
            CostPerHour: new Flt64(1000),
            RouteFlyTime: new Dictionary<Route, TimeSpan>(),
            ConnectionTime: new Dictionary<Airport, TimeSpan>());
        var aircraft = new Aircraft(
            RegNo: new AircraftRegisterNumber("B766"),
            MinorType: minor,
            Capacity: new AircraftCapacity.PassengerCapacity(new Dictionary<PassengerClassId, ulong> {
                [Economy] = 120UL,
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

    private sealed record Fixture(
        LinearMetaModel<Flt64> Model,
        List<FlightTask> Tasks,
        List<FlightPassenger> Passengers,
        PassengerAmount Amount,
        PassengerCancel Cancel,
        FlightCapacity Capacity,
        Fuookami.Ospf.Core.Variable.IVariableItem Column);

    /// <summary>
    /// 装配一次：一架 120 座的飞机、一束、一位乘客 / Assemble once: one 120-seat aircraft, one bunch, one passenger.
    /// </summary>
    /// <param name="headCount">乘客人数 / The head count.</param>
    /// <returns>装配结果 / The fixture.</returns>
    private static Fixture Assemble(ulong headCount) {
        Aircraft aircraft = AircraftFixture();
        FlightLeg first = Task(aircraft, "CA911");
        FlightLeg second = Task(aircraft, "CA912");
        var tasks = new List<FlightTask> { first, second };
        var aircrafts = new List<Aircraft> { aircraft };
        var bunch = new FlightTaskBunch(aircraft, new List<FlightTask> { first, second }, 0L);
        var bunches = new List<FlightTaskBunch> { bunch };
        var model = new LinearMetaModel<Flt64>();
        var compilation = new Aggregation(
            Window(), aircrafts, tasks,
            links: new List<Link> { new ConnectingLink(first, second, new Flt64(10.0)) },
            originBunches: bunches);
        compilation.Register(model).IsOk.Should().BeTrue();
        compilation.AddColumns(0UL, bunches, model).IsOk.Should().BeTrue();

        var passenger = new Passenger(headCount, new List<(FlightTask, PassengerClassId)> { (first, Economy) });
        var passengers = new List<FlightPassenger> { new(first, passenger) };
        var passengerAggregation = new Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Passenger.Aggregation(
            tasks, passengers, new[] { Economy });
        passengerAggregation.Register(model).IsOk.Should().BeTrue();

        FlightCapacity? capacity = compilation.FlightCapacity;
        capacity.Should().NotBeNull("a passenger aircraft means the capacity layer exists");
        Fuookami.Ospf.Core.Variable.IVariableItem? column = compilation.Compilation.ColumnItem(0UL, bunch);
        column.Should().NotBeNull("the round must have produced a column variable for this bunch");
        return new Fixture(model, tasks, passengers, passengerAggregation.Amount, passengerAggregation.Cancel, capacity!, column!);
    }

    /// <summary>
    /// **约束的两侧真的是人数与容量** / The row's two sides really are the amount and the capacity.
    /// </summary>
    /// <remarks>
    /// 五点成对：航班 × 舱位各一条行（两班一个舱位 → **两条**）/ 行名带前缀 / 比较符是小于等于 / **左侧带人数常数与取消项** /
    /// **右侧带 120 座的容量系数**。
    /// 第一次实现把两侧各包成一个表达式符号交给约束，存下来的行是**空的**：行数对、比较符对，**而模型永远可行**。
    ///
    /// Five paired points: one row per flight and cabin (two flights, one cabin → **two**); the names carry the prefix; the comparison is a less-or-equal; **the left side carries the head
    /// count and the cancellation term**; and **the right side carries the capacity column** (a name ending in `_capacity_Y`). The first implementation wrapped each side in one expression
    /// symbol, and the stored row came out **empty**: row count right, comparison right, **and the model always feasible**. The expanded right side carries the coefficient 120.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldPutTheAmountAgainstTheCapacity() {
        Fixture fixture = Assemble(100UL);
        using LinearMetaModel<Flt64> unused = fixture.Model;
        int before = fixture.Model.Constraints.Count;
        var constraint = new PassengerFlightCapacityConstraint(
            fixture.Tasks, fixture.Amount, fixture.Capacity, new[] { Economy });

        constraint.Invoke(fixture.Model).IsOk.Should().BeTrue();

        int added = fixture.Model.Constraints.Count - before;
        added.Should().Be(2, "two flights times one cabin");
        var leftConstants = new List<double>();
        var leftCounts = new List<int>();
        for (int index = before; index < fixture.Model.Constraints.Count; ++index) {
            var row = (LinearInequalityConstraint<Flt64>)fixture.Model.Constraints[index];
            row.Name.Should().StartWith("passenger_flight_capacity_constraint_");
            row.Inequality.Comparison.Should().Be(Comparison.LE);
            leftCounts.Add(row.Inequality.Lhs.Monomials.Count);
            row.Inequality.Rhs.Monomials.Should().ContainSingle("the capacity column");
            // 展开之后右侧是"容量系数 × 列变量"：**系数 120 才是座位数**，符号名是列变量自己的名字。
            // After expansion the right side is "capacity coefficient times the column variable": **the coefficient 120 is the seat count**, the symbol name belongs to the column.
            row.Inequality.Rhs.Monomials[0].Coefficient.ToDouble().Should().Be(120.0, "the 120-seat aircraft's cabin capacity");
            row.Inequality.Rhs.Constant.ToDouble().Should().Be(0.0);
            leftConstants.Add(row.Inequality.Lhs.Constant.ToDouble());
        }
        leftConstants.Order().Should().Equal(new List<double> { 0.0, 100.0 },
            "the flight with the passenger carries the head count, the other one carries nothing");
        leftCounts.Order().Should().Equal(new List<int> { 0, 1 },
            "only the loaded flight has a cancellation term on the left");
    }

#if SCIP_INTEGRATION
    /// <summary>
    /// **超售的那些人只能被取消掉** / The overbooked passengers can only be cancelled.
    /// </summary>
    /// <remarks>
    /// 三点成对：100 人 120 座 → 取消 **0** / 130 人 120 座 → 取消 **10** / 两例的束都钉成 1（否则容量读作 0）。
    /// 注意这里**不是"不可行"**：多出来的 10 个人由取消变量消化——**这正是容量约束与取消变量配合的真实语义**，
    /// 而第一次实现把"不可行"当成期望值，那是对模型的误解。
    ///
    /// Three paired points: 100 passengers with 120 seats cancels **0**; 130 passengers with 120 seats cancels **10**; and the bunch is pinned to one in both cases (otherwise capacity reads
    /// 0). Note this is **not "infeasible"**: the extra ten are absorbed by the cancellation variable — **that is the real semantics of the capacity row working together with cancellation**,
    /// and the first implementation took "infeasible" as the expectation, which was a misreading of the model.
    /// </remarks>
    [Theory]
    [InlineData(100UL, 0.0)]
    [InlineData(130UL, 10.0)]
    public async System.Threading.Tasks.Task Invoke_ShouldForceCancellationWhenOverbooked(ulong headCount, double cancelled) {
        Fixture fixture = Assemble(headCount);
        using LinearMetaModel<Flt64> unused = fixture.Model;
        new PassengerFlightCapacityConstraint(
            fixture.Tasks, fixture.Amount, fixture.Capacity, new[] { Economy })
            .Invoke(fixture.Model).IsOk.Should().BeTrue();
        new PassengerCancelMinimization(fixture.Passengers, fixture.Cancel).Invoke(fixture.Model).IsOk.Should().BeTrue();
        fixture.Model.AddConstraint(
            new LinearInequality<Flt64>(
                new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, fixture.Column) }, Flt64.Zero),
                new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.One),
                Comparison.EQ),
            group: null,
            name: "pin_bunch",
            args: null).IsOk.Should().BeTrue();

        var solved = await new Fuookami.Ospf.Core.Plugin.Scip.ScipColumnGenerationSolver()
            .SolveMILPAsync("passenger_flight_capacity", fixture.Model);

        solved.IsOk.Should().BeTrue("cancellation can absorb the overbooking, so the model stays feasible");
        Fuookami.Ospf.Core.Token.Token<Flt64>? token = fixture.Model.Tokens.Find(fixture.Cancel.Of(fixture.Passengers[0]));
        token.Should().NotBeNull("the cancellation variable has to be in the model");
        token!.Result.Value.ToDouble().Should().Be(cancelled,
            "the capacity row leaves exactly the excess to be cancelled");
    }
#endif
}
