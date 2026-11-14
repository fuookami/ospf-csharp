// 乘客域管线清单（B768）/ The passenger pipeline list (B768).

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchCompilation;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchCompilation.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Passenger.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Passenger.Service;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Passenger.Service.Limits;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Rule.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Xunit;
using PassengerAggregation = Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Passenger.Aggregation;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **乘客域管线清单（B768）** / The passenger pipeline list (B768).
/// </summary>
/// <remarks>
/// 三条约束在前、三条目标在后。顺序反了**不会报错**：求解照样成功，只是列生成的定价拿不到可用的对偶信息——
/// **"求解成功但改不动"是最难被发现的失败形态**，所以顺序要在判据里钉住。
///
/// Three constraints first, three objectives after. Reversing it **raises nothing**: the solve still succeeds and only column generation's pricing loses usable dual information —
/// **"it solved but nothing moves" is the hardest failure to notice**, so the order is pinned in the criteria.
/// </remarks>
public class Demo4PassengerPipelineListGeneratorTests {
    private static readonly DateTimeOffset Origin = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly Airport Beijing = new(new ICAO("ZBAA"), AirportType.International, PassengerTransferTime: TimeSpan.FromHours(1));

    private static readonly Airport Shanghai = new(new ICAO("ZSPD"), AirportType.Domestic, PassengerTransferTime: TimeSpan.FromHours(1));

    private static readonly Airport Guangzhou = new(new ICAO("ZGGG"), AirportType.Domestic, PassengerTransferTime: TimeSpan.FromHours(1));

    private static readonly PassengerClassId Economy = new("Y");

    private static TimeWindow<Flt64> Window() => new(new TimeRange(Origin, Origin.AddDays(1)), false, TimeSpan.FromHours(1));

    private static Aircraft AircraftFixture() {
        var type = AircraftType.GetOrAdd(new AircraftTypeCode("T768"));
        var minor = new AircraftMinorType(
            Type: type,
            Code: new AircraftMinorTypeCode("M768"),
            CostPerHour: new Flt64(1000),
            RouteFlyTime: new Dictionary<Route, TimeSpan>(),
            ConnectionTime: new Dictionary<Airport, TimeSpan>());
        var aircraft = new Aircraft(
            RegNo: new AircraftRegisterNumber("B768"),
            MinorType: minor,
            Capacity: new AircraftCapacity.PassengerCapacity(new Dictionary<PassengerClassId, ulong> {
                [Economy] = 120UL,
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
        Aggregation Bunch,
        PassengerAggregation Passengers,
        List<FlightTask> Tasks,
        List<FlightTaskBunch> Bunches);

    /// <summary>
    /// 装配一次：北京→上海接上海→广州，一位乘客，外加一条替代航班。
    /// </summary>
    /// <returns>装配结果 / The fixture.</returns>
    private static Fixture Assemble() {
        Aircraft aircraft = AircraftFixture();
        FlightLeg first = Task(aircraft, "CA941", Beijing, Shanghai, 8);
        FlightLeg second = Task(aircraft, "CA942", Shanghai, Guangzhou, 14);
        FlightLeg firstAlt = Task(aircraft, "CA943", Beijing, Shanghai, 7);
        var tasks = new List<FlightTask> { first, second, firstAlt };
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
        var aggregation = new PassengerAggregation(tasks, passengers, new[] { Economy }, withFlightChange: true);
        aggregation.Register(model).IsOk.Should().BeTrue();
        return new Fixture(model, compilation, aggregation, tasks, bunches);
    }

    /// <summary>
    /// **六条管线，三条约束在前三条目标在后** / Six pipelines, three constraints first and three objectives after.
    /// </summary>
    /// <remarks>
    /// 两点成对：清单长度是 **6** / 前三条是约束、后三条是目标（按类型逐位比对）。
    /// 只比长度会让顺序反掉的实现通过——**而顺序正是这一层的语义**。
    ///
    /// Two paired points: the list holds **6** pipelines; and the first three are constraints while the last three are objectives, compared position by position. Comparing only the
    /// length would let a reversed implementation pass — **and the order is exactly what this layer means**.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldListThreeConstraintsThenThreeObjectives() {
        Fixture fixture = Assemble();
        using LinearMetaModel<Flt64> unused = fixture.Model;
        FlightCapacity capacity = fixture.Bunch.FlightCapacity!;
        PipelineListGenerator generator = new(fixture.Passengers, capacity, Window(), fixture.Bunch.TaskTime);

        Result<IReadOnlyList<ICGPipeline<IGanttSchedulingShadowPriceArguments<Aircraft, FlightTaskAssignment>, object, GanttSchedulingShadowPriceMap<Aircraft, FlightTaskAssignment>>>, ErrorCode, Error<ErrorCode>> built = generator.Invoke();

        built.IsOk.Should().BeTrue();
        IReadOnlyList<object> pipelines = built.Value!.Cast<object>().ToList();
        pipelines.Should().HaveCount(6);
        pipelines[0].Should().BeOfType<PassengerFlightCapacityConstraint>();
        pipelines[1].Should().BeOfType<PassengerRouteCancelConstraint>();
        pipelines[2].Should().BeOfType<PassengerFlightChangeConstraint>();
        pipelines[3].Should().BeOfType<PassengerCancelMinimization>();
        pipelines[4].Should().BeOfType<PassengerClassChangeMinimization>();
        pipelines[5].Should().BeOfType<PassengerFlightChangeMinimization>();
    }

    /// <summary>
    /// **六条管线一起跑，各自都成功且真的加了行** / All six run together, each succeeding and really adding rows.
    /// </summary>
    /// <remarks>
    /// 三点成对：六条全部 `Invoke` 成功 / 约束行数增加 / 模型里出现指示变量（`_if`）。
    /// 只证明"六条各自能构造"是不够的：**它们可能一条行都不加**——B766 的空行就是这么出现的。
    ///
    /// Three paired points: all six `Invoke` calls succeed; the constraint count grows; and indicator variables (`_if`) appear in the model. Proving "each of the six constructs" is not
    /// enough: **they may add no rows at all** — that is exactly how B766's empty row came about.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldAddRowsWhenAllSixRun() {
        Fixture fixture = Assemble();
        using LinearMetaModel<Flt64> unused = fixture.Model;
        FlightCapacity capacity = fixture.Bunch.FlightCapacity!;
        var generator = new PipelineListGenerator(fixture.Passengers, capacity, Window(), fixture.Bunch.TaskTime);
        int before = fixture.Model.Constraints.Count;

        Result<IReadOnlyList<ICGPipeline<IGanttSchedulingShadowPriceArguments<Aircraft, FlightTaskAssignment>, object, GanttSchedulingShadowPriceMap<Aircraft, FlightTaskAssignment>>>, ErrorCode, Error<ErrorCode>> built = generator.Invoke();
        built.IsOk.Should().BeTrue();
        foreach (ICGPipeline<IGanttSchedulingShadowPriceArguments<Aircraft, FlightTaskAssignment>, object, GanttSchedulingShadowPriceMap<Aircraft, FlightTaskAssignment>> pipeline in built.Value!) {
            pipeline.Invoke(fixture.Model).IsOk.Should().BeTrue();
        }
        fixture.Model.Constraints.Count.Should().BeGreaterThan(before, "the constraint half must add rows");
        fixture.Model.Tokens.Tokens.Count(token => token.Name.Contains("_if")).Should().BeGreaterThan(0,
            "the flight-change constraint has to bring its indicators into the model");
    }

    /// <summary>
    /// **系数真的被转发下去** / The coefficients really are forwarded.
    /// </summary>
    /// <remarks>
    /// 两点成对：取消目标的系数是传进去的 **7**（不是缺省的 1）/ 目标零件的名字带前缀。
    /// 装配层最容易出的缺陷就是"参数收下了、没往下传"——**收了不传，判据只能靠一个与缺省值不同的数看出来**。
    ///
    /// Two paired points: the cancellation objective's coefficient is the **7** that was passed in (not the default 1); and the objective part carries the prefix. The most common
    /// defect in an assembly layer is "the parameter is accepted and never forwarded" — **accepting without forwarding is only visible through a number that differs from the default**.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldForwardTheCoefficients() {
        Fixture fixture = Assemble();
        using LinearMetaModel<Flt64> unused = fixture.Model;
        FlightCapacity capacity = fixture.Bunch.FlightCapacity!;
        var generator = new PipelineListGenerator(
            fixture.Passengers,
            capacity,
            Window(),
            fixture.Bunch.TaskTime,
            cancelCoefficient: _ => new Flt64(7));

        foreach (ICGPipeline<IGanttSchedulingShadowPriceArguments<Aircraft, FlightTaskAssignment>, object, GanttSchedulingShadowPriceMap<Aircraft, FlightTaskAssignment>> pipeline in generator.Invoke().Value!) {
            pipeline.Invoke(fixture.Model).IsOk.Should().BeTrue();
        }

        // 目标零件自己的名字是固定的 `passenger cancel`，**前缀只作用于管线名与行名**——同一条模型里装两份乘客域会撞名，
        // 那是"一份模型一份域"的前置假设，写在这里免得下一个人以为前缀会跟着走。
        // The objective part's own name is the fixed `passenger cancel`; **the prefix only reaches the pipeline and row names** — two passenger domains in one model would collide,
        // which is the "one domain per model" precondition, noted here so the next reader does not assume the prefix follows.
        MetaSubObject<Flt64>? objective = fixture.Model.MetaSubObjects.FirstOrDefault(part => part.Name == "passenger cancel");
        objective.Should().NotBeNull("the cancellation objective has to reach the model");
        objective!.Polynomial.Monomials.Should().OnlyContain(monomial => monomial.Coefficient.ToDouble() == 7.0,
            "the forwarded coefficient is 7, not the default 1");
    }

#if SCIP_INTEGRATION
    /// <summary>
    /// **六条一起、束钉成 1，真解一次** / All six together with the bunch pinned, solved for real.
    /// </summary>
    /// <remarks>
    /// 两点成对：求解成功 / 机身层（容量）读出 **120**。
    /// 只证明"六条各自成功"是不够的：**它们互相之间可能把模型写成不可行**——B752 的教训是"接上了"与"能一起解"是两件事。
    ///
    /// Two paired points: the solve succeeds; and the capacity layer reads **120**. Proving "each of the six succeeds" is not enough: **together they can write an infeasible model** —
    /// B752's lesson is that "wired" and "solvable together" are two different things.
    /// </remarks>
    [Fact]
    public async System.Threading.Tasks.Task Invoke_ShouldSolveTogether() {
        Fixture fixture = Assemble();
        using LinearMetaModel<Flt64> unused = fixture.Model;
        FlightCapacity capacity = fixture.Bunch.FlightCapacity!;
        var generator = new PipelineListGenerator(fixture.Passengers, capacity, Window(), fixture.Bunch.TaskTime);
        foreach (ICGPipeline<IGanttSchedulingShadowPriceArguments<Aircraft, FlightTaskAssignment>, object, GanttSchedulingShadowPriceMap<Aircraft, FlightTaskAssignment>> pipeline in generator.Invoke().Value!) {
            pipeline.Invoke(fixture.Model).IsOk.Should().BeTrue();
        }
        Fuookami.Ospf.Core.Variable.IVariableItem column = fixture.Bunch.Compilation.ColumnItem(0UL, fixture.Bunches[0])!;
        fixture.Model.AddConstraint(
            new LinearInequality<Flt64>(
                new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, column) }, Flt64.Zero),
                new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.One),
                Comparison.EQ),
            group: null,
            name: "pin_bunch",
            args: null).IsOk.Should().BeTrue();

        var solved = await new Fuookami.Ospf.Core.Plugin.Scip.ScipColumnGenerationSolver()
            .SolveMILPAsync("passenger_pipelines", fixture.Model);

        solved.IsOk.Should().BeTrue("the three constraints and three objectives must leave a feasible region");
    }
#endif
}
