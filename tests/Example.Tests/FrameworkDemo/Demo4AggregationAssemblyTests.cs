// 装配链真的闭合/ The assembly chain really closes.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchCompilation;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Rule.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **装配链真的闭合** / The assembly chain really closes.
/// </summary>
/// <remarks>
/// / / / 各自把一块补齐了，但它们**只在各自的判据里单独跑过**——装配链有没有闭合，只有一次注册能回答。
/// `register` 顺序是 taskTime → flow → fleetBalance → flightLink → flightCapacity，**任何一步失败就整体失败**：
/// 半装的聚合在求解时表现为"约束莫名不紧"，比直接失败难查得多。
///
/// Both / / / each filled one piece, but each was **only exercised inside its own criteria**; whether the chain closes can only be answered by a single registration.
/// The code's `register` order is taskTime, flow, fleetBalance, flightLink, flightCapacity, and **a failure anywhere aborts the whole registration**: a half-wired aggregation shows
/// up at solve time as "the constraint is mysteriously slack", much harder to diagnose than a straight failure.
/// </remarks>
public class Demo4AggregationAssemblyTests {
    private static readonly DateTimeOffset Origin = new(2024, 1, 1, 8, 0, 0, TimeSpan.Zero);

    private static readonly Airport Beijing = new(new ICAO("ZBAA"), AirportType.International);

    private static readonly Airport Shanghai = new(new ICAO("ZSPD"), AirportType.Domestic);

    private static TimeWindow<Flt64> Window() => new(new TimeRange(Origin, Origin.AddDays(1)), false, TimeSpan.FromHours(8));

    private static Aircraft AircraftFixture() {
        var type = AircraftType.GetOrAdd(new AircraftTypeCode("T748"));
        var minor = new AircraftMinorType(
            Type: type,
            Code: new AircraftMinorTypeCode("M748"),
            CostPerHour: new Flt64(1000),
            RouteFlyTime: new Dictionary<Route, TimeSpan>(),
            ConnectionTime: new Dictionary<Airport, TimeSpan>());
        var aircraft = new Aircraft(
            RegNo: new AircraftRegisterNumber("B748"),
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

    private static Flow FlowFixture() => new("F748", Beijing, FlowControlScene.Departure, new List<FlowControl> {
        new("R748", Beijing, new TimeRange(Origin, Origin.AddHours(8)), FlowControlScene.Departure,
            new FlowControlCapacity(2UL, TimeSpan.FromHours(8))),
    });

    private static List<string> SymbolNames(LinearMetaModel<Flt64> model) =>
        model.Tokens.Symbols.Select(symbol => symbol.Name ?? string.Empty).ToList();

    /// <summary>
    /// **一次注册之后五样都在模型里** / After one registration all five live in the model.
    /// </summary>
    /// <remarks>
    /// 五点成对：注册成功 / 任务时间行在 / **流量数量行在** / **机队符号与它的松弛在** / **链接符号与它的松弛在** /
    /// **航班容量符号在**。少任何一样都说明那一步没接上——而单看各自的判据全是绿的。
    ///
    /// Five paired points: registration succeeds; the task-time rows are there; **the flow quantity rows are there**; **the fleet symbols and their slacks are there**; **the link symbols
    /// and their slacks are there**; **the flight capacity symbols are there**. Missing any one means that step was never wired — while each piece's own criteria stay green.
    /// </remarks>
    [Fact]
    public void Register_ShouldWireAllFiveLayers() {
        Aircraft aircraft = AircraftFixture();
        FlightLeg first = Task(aircraft, "CA501");
        FlightLeg second = Task(aircraft, "CA502");
        var tasks = new List<FlightTask> { first, second };
        var aircrafts = new List<Aircraft> { aircraft };
        var bunches = new List<FlightTaskBunch> { new(aircraft, new List<FlightTask> { first }, 0L) };
        var links = new List<Link> { new ConnectingLink(first, second, new Flt64(10.0)) };
        using var model = new LinearMetaModel<Flt64>();
        var aggregation = new Aggregation(
            Window(), aircrafts, tasks,
            flows: new List<Flow> { FlowFixture() },
            originBunches: bunches,
            links: links);

        aggregation.Register(model).IsOk.Should().BeTrue();

        List<string> names = SymbolNames(model);
        string joined = string.Join(" | ", names);
        joined.Should().Contain("flow_quantity_", "flow usage must be wired");
        joined.Should().Contain("fleet_0", "fleet balance must be wired");
        joined.Should().Contain("fleet_slack_0", "fleet balance's slack must be wired");
        joined.Should().Contain("link_0", "task links must be wired");
        joined.Should().Contain("link_slack_0", "task links' slack must be wired");
        joined.Should().Contain("_capacity_Y", "flight capacity must be wired");
        names.Should().Contain(name => name.Contains("task_time", StringComparison.Ordinal) || name.Contains("start", StringComparison.Ordinal),
            "task time must be wired");
    }

    /// <summary>
    /// **没有输入的三样不建符号** / The three without input build no symbols.
    /// </summary>
    /// <remarks>
    /// 三点成对：只有任务与飞机时注册成功 / 模型里**没有** `link_`、`flow_quantity_` 这些名字 / **`fleet_0` 照样在**——
    /// 机队层看的是"有哪些飞机"，不是"有哪些束"。
    /// "没有输入"不该变成"建一堆恒零符号"——那会让每条判据、每轮求解都多背一堆无用的项。
    ///
    /// Three paired points: with only tasks and aircraft the registration succeeds; the model holds **none** of `link_` or `flow_quantity_`; and **`fleet_0` is still there** — the fleet
    /// layer looks at "which aircraft exist", not "which bunches exist". "No input" must not become
    /// "a pile of always-zero symbols", which would weigh every criterion and every solve down with useless terms.
    /// </remarks>
    [Fact]
    public void Register_ShouldBuildNothingWithoutInput() {
        Aircraft aircraft = AircraftFixture();
        FlightLeg task = Task(aircraft, "CA601");
        using var model = new LinearMetaModel<Flt64>();
        var aggregation = new Aggregation(Window(), new List<Aircraft> { aircraft }, new List<FlightTask> { task });

        aggregation.Register(model).IsOk.Should().BeTrue();

        string joined = string.Join(" | ", SymbolNames(model));
        joined.Should().NotContain("flow_quantity_");
        joined.Should().NotContain("link_");
        // **机队符号照样会建**：`FleetBalance` 的输入是"有哪些飞机"，不是"有哪些束"——飞机在，检查点就在。
        // 这条一开始写成了"没有束就什么都没有"，判据立刻指出我读错了模型的输入。
        //
        // **The fleet symbols still appear**: `FleetBalance`'s input is "which aircraft exist", not "which bunches exist" — the aircraft exist, so the checkpoints do. This was
        // first written as "no bunches means nothing", and the criterion immediately showed that the model's input had been misread.
        joined.Should().Contain("fleet_0");
        joined.Should().Contain("_capacity_Y", "the capacity layer still runs from the tasks alone");
    }
}
