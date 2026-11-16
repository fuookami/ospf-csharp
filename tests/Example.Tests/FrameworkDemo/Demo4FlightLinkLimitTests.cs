// 任务链接的约束与目标/ The task links' constraint and objective.

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
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **任务链接的约束与目标** / The task links' constraint and objective.
/// </summary>
/// <remarks>
/// 与机队那一条**结构相同、右端项不同**：机队是"至少这么多架"，链接是"至少接上一次"——
/// 恒为 1。阈值写错（比如用机数或成本）约束就变成另一件事，而**名字还叫 link**。
///
/// It has **the same shape as the fleet limit with a different right-hand side**: the fleet says "at least this many aircraft", the link says
/// "stitched together at least once" — constantly 1. A wrong threshold (an aircraft count or a cost) turns the constraint into something else while **still being named link**.
/// </remarks>
public class Demo4FlightLinkLimitTests {
    private static readonly DateTimeOffset Origin = new(2024, 1, 1, 8, 0, 0, TimeSpan.Zero);

    private static readonly Airport Beijing = new(new ICAO("ZBAA"), AirportType.International);

    private static readonly Airport Shanghai = new(new ICAO("ZSPD"), AirportType.Domestic);

    private static TimeWindow<Flt64> Window() => new(new TimeRange(Origin, Origin.AddDays(1)), false, TimeSpan.FromHours(8));

    private static Aircraft AircraftFixture() {
        var type = AircraftType.GetOrAdd(new AircraftTypeCode("T754"));
        var minor = new AircraftMinorType(
            Type: type,
            Code: new AircraftMinorTypeCode("M754"),
            CostPerHour: new Flt64(1000),
            RouteFlyTime: new Dictionary<Route, TimeSpan>(),
            ConnectionTime: new Dictionary<Airport, TimeSpan>());
        var aircraft = new Aircraft(
            RegNo: new AircraftRegisterNumber("B754"),
            MinorType: minor,
            Capacity: new AircraftCapacity.PassengerCapacity(new Dictionary<PassengerClassId, ulong> {
                [new PassengerClassId("Y")] = 100UL,
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

    private static (Aggregation Aggregation, LinearMetaModel<Flt64> Model) Build(bool registerLinks) {
        Aircraft aircraft = AircraftFixture();
        FlightLeg first = Task(aircraft, "CA754");
        FlightLeg second = Task(aircraft, "CA755");
        var tasks = new List<FlightTask> { first, second };
        List<Link>? links = registerLinks
            ? new List<Link> { new ConnectingLink(first, second, new Flt64(10.0)) }
            : null;
        var model = new LinearMetaModel<Flt64>();
        var aggregation = new Aggregation(Window(), new List<Aircraft> { aircraft }, tasks, links: links);
        aggregation.Register(model).IsOk.Should().BeTrue();
        return (aggregation, model);
    }

    /// <summary>
    /// **每条链接一条约束，右端项恒为 1** / One row per link with a right-hand side of 1.
    /// </summary>
    /// <remarks>
    /// 三点成对：约束数恰好增加链接条数 / 行名带管道名前缀 / **右端项是 1**（不是成本 10、也不是机数）。
    /// 右端项写错不会让判据变红，除非判据直接读它——**只数行数是这类"单位写错"最常见的漏网方式**。
    ///
    /// Three paired points: the constraint count grows by exactly the number of links; the row names carry the pipeline prefix; and **the right-hand side is 1** (neither the cost 10
    /// nor an aircraft count). A wrong right-hand side stays green unless the criterion reads it — **counting rows is the usual way unit errors slip through**.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldAddOneRowPerLinkWithUnitBound() {
        (Aggregation aggregation, LinearMetaModel<Flt64> model) = Build(registerLinks: true);
        using LinearMetaModel<Flt64> unused = model;
        var limit = new FlightLinkLimit(aggregation.FlightLink);
        int constraintsBefore = model.Constraints.Count;

        limit.Invoke(model).IsOk.Should().BeTrue();

        model.Constraints.Count.Should().Be(constraintsBefore + aggregation.FlightLink.Items.Count);
        var bounds = model.Constraints
            .Where(constraint => constraint.Name.StartsWith("flight_link_limit_", StringComparison.Ordinal))
            .Select(constraint => ((LinearInequalityConstraint<Flt64>)constraint).Inequality.Rhs.Constant.ToDouble())
            .ToList();
        bounds.Should().HaveCount(1, "this fixture has exactly one link");
        bounds.Should().OnlyContain(bound => bound == 1.0, "a link holds as soon as it is stitched once");
    }

    /// <summary>
    /// **系数只影响目标，不进右端项** / The coefficient affects the objective only, never the right-hand side.
    /// </summary>
    /// <remarks>
    /// 两点成对：系数取分割成本（10）时约束数不变 / **右端项仍是 1**。系数进右端项就变成"这条链接要接十次"，与
    /// "接一次的代价是十倍"完全不同。
    ///
    /// Two paired points: with the split cost (10) as the coefficient the constraint count is unchanged; and **the right-hand side stays 1**. Putting the coefficient there would demand
    /// "this link be stitched ten times", wholly unlike "one stitch costs ten times as much".
    /// </remarks>
    [Fact]
    public void Invoke_ShouldKeepTheCoefficientOutOfTheBound() {
        (Aggregation aggregation, LinearMetaModel<Flt64> model) = Build(registerLinks: true);
        using LinearMetaModel<Flt64> unused = model;
        var limit = new FlightLinkLimit(aggregation.FlightLink, link => link.SplitCost);
        int constraintsBefore = model.Constraints.Count;

        limit.Invoke(model).IsOk.Should().BeTrue();

        model.Constraints.Count.Should().Be(constraintsBefore + aggregation.FlightLink.Items.Count);
        model.Constraints
            .Where(constraint => constraint.Name.StartsWith("flight_link_limit_", StringComparison.Ordinal))
            .Select(constraint => ((LinearInequalityConstraint<Flt64>)constraint).Inequality.Rhs.Constant.ToDouble())
            .Should().OnlyContain(bound => bound == 1.0);
    }

    /// <summary>
    /// **没有链接时什么都不加** / No links add nothing.
    /// </summary>
    /// <remarks>
    /// 两点成对：没有链接 → 返回成功 / 约束数不变。**"没有链接"不该变成"加一行恒真约束"**。
    ///
    /// Two paired points: no links returns success; and the constraint count is unchanged. **"No links" must not become "one always-true row"**.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldAddNothingWithoutLinks() {
        (Aggregation aggregation, LinearMetaModel<Flt64> model) = Build(registerLinks: false);
        using LinearMetaModel<Flt64> unused = model;
        var limit = new FlightLinkLimit(aggregation.FlightLink);
        int constraintsBefore = model.Constraints.Count;

        limit.Invoke(model).IsOk.Should().BeTrue();

        model.Constraints.Count.Should().Be(constraintsBefore);
    }
}
