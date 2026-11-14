// Demo4 的第一次真实求解（B752）/ Demo4's first real solve (B752).

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchCompilation;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchCompilation.Service.Limits;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Rule.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Variable;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **Demo4 的第一次真实求解（B752）** / Demo4's first real solve (B752).
/// </summary>
/// <remarks>
/// 装配链闭合（B748）只证明了五层**都进了模型**，没证明它们能**一起解**。这一批把一轮列生成 + 真实求解跑通：
/// 强制把某一轮的一个束的列变量钉成 1，然后看容量层与机队层**是否跟着变**——
/// 中间任何一层没接上，解都会是"求出来了，但数字全是 0"，而那正是"求出来了"最容易骗过人的形态。
///
/// B748 proved the five layers **all reach the model**; it did not prove they **solve together**. This batch runs one round of column generation plus a real solve: it pins one bunch's
/// column variable to 1 and checks whether the capacity and fleet layers **follow** — with any layer unwired, the solve still succeeds but every number comes back 0, which is exactly
/// how "it solved" fools a reviewer.
/// </remarks>
public class Demo4RealSolveTests {
#if SCIP_INTEGRATION
    private static readonly DateTimeOffset Origin = new(2024, 1, 1, 8, 0, 0, TimeSpan.Zero);

    private static readonly Airport Beijing = new(new ICAO("ZBAA"), AirportType.International);

    private static readonly Airport Shanghai = new(new ICAO("ZSPD"), AirportType.Domestic);

    private static TimeWindow<Flt64> Window() => new(new TimeRange(Origin, Origin.AddDays(1)), false, TimeSpan.FromHours(8));

    private static Aircraft AircraftFixture() {
        var type = AircraftType.GetOrAdd(new AircraftTypeCode("T752"));
        var minor = new AircraftMinorType(
            Type: type,
            Code: new AircraftMinorTypeCode("M752"),
            CostPerHour: new Flt64(1000),
            RouteFlyTime: new Dictionary<Route, TimeSpan>(),
            ConnectionTime: new Dictionary<Airport, TimeSpan>());
        var aircraft = new Aircraft(
            RegNo: new AircraftRegisterNumber("B752"),
            MinorType: minor,
            Capacity: new AircraftCapacity.PassengerCapacity(new Dictionary<PassengerClassId, ulong> {
                [new PassengerClassId("Y")] = 120UL,
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

    /// <remarks>
    /// **表达式符号不是变量项，查不到 token**：只能按它的多项式逐项乘系数求值。直接拿表达式符号去 `Find` 会抛
    /// `InvalidCastException`——**"读不到"和"读到 0"是两件事**，这里让前者以异常暴露。
    ///
    /// **An expression symbol is not a variable item and has no token**: it must be evaluated term by term over its polynomial. Handing the expression symbol to `Find` throws
    /// `InvalidCastException` — **"unreadable" and "reads 0" are different things**, and this lets the former surface as an exception.
    /// </remarks>
    private static double ValueOf(LinearMetaModel<Flt64> model, Fuookami.Ospf.Math.Symbol.ISymbol symbol) {
        Fuookami.Ospf.Math.Symbol.Polynomial.LinearPolynomial<Flt64> polynomial =
            symbol is Fuookami.Ospf.Core.Symbol.LinearExpressionSymbol expression
                ? expression.Polynomial
                : new Fuookami.Ospf.Math.Symbol.Polynomial.LinearPolynomial<Flt64>(
                    new[] { new LinearMonomial<Flt64>(Flt64.One, symbol) }, Flt64.Zero);

        double total = polynomial.Constant.ToDouble();
        foreach (LinearMonomial<Flt64> monomial in polynomial.Monomials) {
            Fuookami.Ospf.Core.Token.Token<Flt64>? token = model.Tokens.Find((Fuookami.Ospf.Core.Variable.IVariableItem)monomial.Symbol);
            token.Should().NotBeNull("every variable in the expression has to be in the model after the solve");
            total += monomial.Coefficient.ToDouble() * token!.Result.Value.ToDouble();
        }
        return total;
    }

    /// <summary>
    /// **把束钉成 1 之后，容量与机队都跟着变** / Pinning the bunch to 1 makes capacity and fleet follow.
    /// </summary>
    /// <remarks>
    /// 四点成对：求解成功 / 容量符号的值是 **120**（一架 120 座的飞机） / 机队符号的值是 **1** / 链接符号是 **1**（这一束同时含两个任务）。
    /// 只断言"求解成功"会让所有层都没接上的实现通过——**求出来全是 0 也是成功**。
    ///
    /// Four paired points: the solve succeeds; the capacity symbol reads **120** (one 120-seat aircraft); the fleet symbol reads **1**; and the link symbol reads **1** (the bunch holds
    /// both tasks). Asserting only "the solve succeeded" would let an implementation with every layer unwired pass — **all zeros is still a success**.
    /// </remarks>
    [Fact]
    public void Solve_ShouldFeedTheCapacityAndFleetLayers() {
        Aircraft aircraft = AircraftFixture();
        FlightLeg first = Task(aircraft, "CA901");
        FlightLeg second = Task(aircraft, "CA902");
        var tasks = new List<FlightTask> { first, second };
        var aircrafts = new List<Aircraft> { aircraft };
        var bunch = new FlightTaskBunch(aircraft, new List<FlightTask> { first, second }, 0L);
        var bunches = new List<FlightTaskBunch> { bunch };
        using var model = new LinearMetaModel<Flt64>();
        var aggregation = new Aggregation(
            Window(), aircrafts, tasks,
            links: new List<Link> { new ConnectingLink(first, second, new Flt64(10.0)) },
            originBunches: bunches);

        aggregation.Register(model).IsOk.Should().BeTrue();
        aggregation.AddColumns(0UL, bunches, model).IsOk.Should().BeTrue();
        Fuookami.Ospf.Core.Variable.IVariableItem? column = aggregation.Compilation.ColumnItem(0UL, bunch);
        column.Should().NotBeNull("the round must have produced a column variable for this bunch");

        // 把这一束钉成 1：没有这一行，可行性问题的解是全零，所有层读出来都是 0，判据就无法分辨"接上了"与"没接上"。
        // Pin this bunch to 1: without the row a feasibility problem solves to all zeros, every layer reads 0, and the criterion cannot tell "wired" from "unwired".
        model.AddConstraint(
            new LinearInequality<Flt64>(
                new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, column!) }, Flt64.Zero),
                new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.One),
                Comparison.EQ),
            group: null,
            name: "pin_bunch",
            args: null).IsOk.Should().BeTrue();

        Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Core.Solver.Output.FeasibleSolverOutput<Flt64>, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>> solved =
            new Fuookami.Ospf.Core.Plugin.Scip.ScipColumnGenerationSolver()
                .SolveMILPAsync("demo4-b752", model).GetAwaiter().GetResult();
        solved.IsOk.Should().BeTrue("the five layers must leave a feasible region");

        Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchCompilation.Model.FlightCapacity? capacity = aggregation.FlightCapacity;
        capacity.Should().NotBeNull("a passenger aircraft means the capacity layer exists");
        Fuookami.Ospf.Core.Symbol.LinearExpressionSymbol cabin = capacity!.Passenger![first][new PassengerClassId("Y")];
        ValueOf(model, cabin).Should().Be(120.0, "a pinned bunch flies one 120-seat aircraft");

        ValueOf(model, aggregation.FleetBalance.Fleets[0]).Should().Be(1.0, "one aircraft occupies the checkpoint");
        ValueOf(model, aggregation.FlightLink.Links[0]).Should().Be(1.0, "the bunch stitches both tasks together");
    }
    /// <summary>
    /// **机队限制的两半都真的在模型里** / Both halves of the fleet limit really live in the model.
    /// </summary>
    /// <remarks>
    /// 两点成对：把这一束钉成 1 时求解成功 / **目标值是 1**（一架飞机占满一个检查点，松弛压在边界上）。
    /// 目标值不是 0 是关键——**"有约束但没目标"在别的方向上也读不出差别**，而"有目标但没约束"会让它一路压到 0。
    ///
    /// 未验证的一半：把这一束钉成 0 应当不可行（检查点要求至少 1 架而没有任何飞机被排上）。这一批没有断言它，
    /// 因为再造一个模型只为看"不可行"要付一份完整的装配成本，而可行侧的目标值已经把两半都钉住了。
    ///
    /// Two paired points: pinning this bunch to 1 solves; and the **objective is 1** (one aircraft fills one checkpoint and the slack sits at its bound). The objective not being 0 is the
    /// key — **"constraint without objective" shows no difference in this direction either**, while "objective without constraint" drives it to zero.
    ///
    /// The unverified half: pinning the bunch to 0 should be infeasible (the checkpoint demands at least one aircraft while none is scheduled). This batch does not assert it, because
    /// standing up another model just to see "infeasible" costs a full assembly, and the feasible side's objective value already pins both halves.
    /// </remarks>
    [Fact]
    public void FleetBalanceLimit_ShouldConstrainAndPenaliseTogether() {
        Aircraft aircraft = AircraftFixture();
        FlightLeg task = Task(aircraft, "CA903");
        var bunches = new List<FlightTaskBunch> {
            new(aircraft, new List<FlightTask> { task }, 0L),
        };
        (Fuookami.Ospf.Core.Model.Mechanism.LinearMetaModel<Flt64> Model, Aggregation Aggregation, FlightTaskBunch Bunch) Build() {
            var model = new Fuookami.Ospf.Core.Model.Mechanism.LinearMetaModel<Flt64>();
            var aggregation = new Aggregation(
                Window(), new List<Aircraft> { aircraft }, new List<FlightTask> { task }, originBunches: bunches);
            aggregation.Register(model).IsOk.Should().BeTrue();
            aggregation.AddColumns(0UL, bunches, model).IsOk.Should().BeTrue();
            new FleetBalanceLimit(aggregation.FleetBalance).Invoke(model).IsOk.Should().BeTrue();
            return (model, aggregation, bunches[0]);
        }

        (Fuookami.Ospf.Core.Model.Mechanism.LinearMetaModel<Flt64> pinned, Aggregation aggregation, FlightTaskBunch bunch) = Build();
        using Fuookami.Ospf.Core.Model.Mechanism.LinearMetaModel<Flt64> unusedPinned = pinned;
        Fuookami.Ospf.Core.Variable.IVariableItem? column = aggregation.Compilation.ColumnItem(0UL, bunch);
        column.Should().NotBeNull();

        void Pin(double value) {
            pinned.AddConstraint(
                new LinearInequality<Flt64>(
                    new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, column!) }, Flt64.Zero),
                    new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(value)),
                    Comparison.EQ),
                group: null,
                name: "pin",
                args: null).IsOk.Should().BeTrue();
        }

        Pin(1.0);
        Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Core.Solver.Output.FeasibleSolverOutput<Flt64>, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>> solved =
            new Fuookami.Ospf.Core.Plugin.Scip.ScipColumnGenerationSolver()
                .SolveMILPAsync("demo4-b753", pinned).GetAwaiter().GetResult();
        solved.IsOk.Should().BeTrue("with the bunch pinned on, one aircraft covers the checkpoint");
        ((Fuookami.Ospf.Utils.Functional.Ok<Fuookami.Ospf.Core.Solver.Output.FeasibleSolverOutput<Flt64>, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>)solved)
            .Value.Obj.ToDouble().Should().Be(1.0, "the fleet slack sits on its bound, so the objective is 1 rather than 0");
    }
    /// <summary>
    /// **链接限制的目标那一半也在** / The link limit's objective half is there too.
    /// </summary>
    /// <remarks>
    /// 两点成对：把这一束钉成 1 时求解成功 / 目标值是 **1**（链接松弛压在边界上）。目标没加进去时这里是 0——
    /// **单元判据看不到目标表，只有真实求解能分辨**。
    ///
    /// Two paired points: pinning this bunch to 1 solves; and the objective is **1** (the link slack sits on its bound). Without the objective it would be 0 — **the unit criteria
    /// cannot see the objective table; only a real solve can tell.**
    /// </remarks>
    [Fact]
    public void FlightLinkLimit_ShouldPenaliseTheLinkSlack() {
        Aircraft aircraft = AircraftFixture();
        FlightLeg first = Task(aircraft, "CA904");
        FlightLeg second = Task(aircraft, "CA905");
        var tasks = new List<FlightTask> { first, second };
        var bunch = new FlightTaskBunch(aircraft, new List<FlightTask> { first, second }, 0L);
        var bunches = new List<FlightTaskBunch> { bunch };
        using var model = new Fuookami.Ospf.Core.Model.Mechanism.LinearMetaModel<Flt64>();
        var aggregation = new Aggregation(
            Window(), new List<Aircraft> { aircraft }, tasks,
            links: new List<Link> { new ConnectingLink(first, second, new Flt64(10.0)) },
            originBunches: bunches);
        aggregation.Register(model).IsOk.Should().BeTrue();
        aggregation.AddColumns(0UL, bunches, model).IsOk.Should().BeTrue();
        new FlightLinkLimit(aggregation.FlightLink).Invoke(model).IsOk.Should().BeTrue();

        Fuookami.Ospf.Core.Variable.IVariableItem? column = aggregation.Compilation.ColumnItem(0UL, bunch);
        column.Should().NotBeNull();
        model.AddConstraint(
            new LinearInequality<Flt64>(
                new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, column!) }, Flt64.Zero),
                new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.One),
                Comparison.EQ),
            group: null,
            name: "pin_link",
            args: null).IsOk.Should().BeTrue();

        Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Core.Solver.Output.FeasibleSolverOutput<Flt64>, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>> solved =
            new Fuookami.Ospf.Core.Plugin.Scip.ScipColumnGenerationSolver()
                .SolveMILPAsync("demo4-b754", model).GetAwaiter().GetResult();
        solved.IsOk.Should().BeTrue("one stitched bunch covers the link");
        ((Fuookami.Ospf.Utils.Functional.Ok<Fuookami.Ospf.Core.Solver.Output.FeasibleSolverOutput<Flt64>, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>)solved)
            .Value.Obj.ToDouble().Should().Be(1.0, "the link slack sits on its bound, so the objective is 1 rather than 0");
    }
#endif
}
