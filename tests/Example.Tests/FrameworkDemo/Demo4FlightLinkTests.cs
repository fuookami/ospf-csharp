// 链接是松弛，不是硬约束/ A link is a slack, not a hard constraint.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchCompilation.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Rule.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **链接是松弛，不是硬约束** / A link is a slack, not a hard constraint.
/// </summary>
/// <remarks>
/// 基线 `FlightLink.kt` 一直被当作"约束"来理解，实际是**松弛**：链接成不成立交给目标去压，而不是硬性禁止。
/// 每条链接建两个符号——`link_k` 与 `link_slack_k`——后者的源多项式是 `link_k + 0.5·y[前驱] + 0.5·y[后继]`，
/// 两条系数都是 0.5：**一条链接由两个任务共同承担，各算一半，才不会把同一对任务重复计数**。
///
/// The baseline `FlightLink.kt` is usually read as "a constraint" but is actually a **slack**: whether a link holds is left to the objective instead of being forbidden outright. Each
/// link builds two symbols — `link_k` and `link_slack_k` — the latter over the source polynomial `link_k + 0.5·y[prev] + 0.5·y[succ]`, both coefficients 0.5: **a link is shared by two
/// tasks and counting each as half is what stops the same pair from being counted twice**.
/// </remarks>
public class Demo4FlightLinkTests {
    private static readonly DateTimeOffset Origin = new(2024, 1, 1, 8, 0, 0, TimeSpan.Zero);

    private static readonly Airport Dep = new(new ICAO("ZBAA"), AirportType.International);

    private static readonly Airport Arr = new(new ICAO("ZSPD"), AirportType.Domestic);

    private static Aircraft AircraftFixture() {
        var type = AircraftType.GetOrAdd(new AircraftTypeCode("T746"));
        var minor = new AircraftMinorType(
            Type: type,
            Code: new AircraftMinorTypeCode("M746"),
            CostPerHour: new Flt64(1000),
            RouteFlyTime: new Dictionary<Route, TimeSpan>(),
            ConnectionTime: new Dictionary<Airport, TimeSpan>());
        var aircraft = new Aircraft(
            RegNo: new AircraftRegisterNumber("B746"),
            MinorType: minor,
            Capacity: new AircraftCapacity.PassengerCapacity(new Dictionary<PassengerClassId, ulong> {
                [new PassengerClassId("Y")] = 100UL,
            }));
        aircraft.SetUsability(new AircraftUsability(null, Dep, Origin));
        return aircraft;
    }

    private static FlightLeg Task(Aircraft aircraft, string no) => FlightLeg.Create(new FlightLegPlan(
        actualId: no,
        no: no,
        type: FlightType.Domestic,
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

    private static (BunchCompilation<FlightTaskBunch, FlightTask, Aircraft, FlightTaskAssignment> Compilation, List<FlightTask> Tasks, LinearMetaModel<Flt64> Model) Ready() {
        Aircraft aircraft = AircraftFixture();
        FlightLeg first = Task(aircraft, "CA301");
        FlightLeg second = Task(aircraft, "CA302");
        var tasks = new List<FlightTask> { first, second };
        var model = new LinearMetaModel<Flt64>();
        var compilation = new BunchCompilation<FlightTaskBunch, FlightTask, Aircraft, FlightTaskAssignment>(tasks, new List<Aircraft> { aircraft });
        compilation.Register(model).IsOk.Should().BeTrue();
        return (compilation, tasks, model);
    }

    /// <summary>
    /// **每条链接建两个符号，名字成对** / Each link builds two symbols with paired names.
    /// </summary>
    /// <remarks>
    /// 三点成对：一条链接 → 一张 `link` 表 + 一张 `slack` 表，各一个条目 / 名字分别是 `link_0` 与 `link_slack_0` /
    /// **两个符号都在模型里**。只测"返回 `Ok`"会让建了符号却没加进模型的实现通过。
    ///
    /// Three paired points: one link gives one `link` table and one `slack` table with one entry each; the names are `link_0` and `link_slack_0`; and **both symbols are in the model**.
    /// Checking only "returns Ok" would let an implementation that builds symbols without adding them pass.
    /// </remarks>
    [Fact]
    public void Register_ShouldBuildBothSymbolsPerLink() {
        (BunchCompilation<FlightTaskBunch, FlightTask, Aircraft, FlightTaskAssignment> compilation, List<FlightTask> tasks, LinearMetaModel<Flt64> model) = Ready();
        using LinearMetaModel<Flt64> unused = model;
        var flightLink = new FlightLink(
            new List<Link> { new ConnectingLink(tasks[0], tasks[1], new Flt64(10.0)) },
            compilation);

        flightLink.Register(model).IsOk.Should().BeTrue();

        flightLink.Links.Should().ContainSingle();
        flightLink.Slacks.Should().ContainSingle();
        flightLink.Links[0].Name.Should().Be("link_0");
        flightLink.Slacks[0].Name.Should().Be("link_slack_0");
        string joined = string.Join(" | ", model.Tokens.Symbols.Select(symbol => symbol.Name ?? string.Empty));
        joined.Should().Contain("link_0");
        joined.Should().Contain("link_slack_0");
    }

    /// <summary>
    /// **两个 0.5 系数就是这条规则的全部内容** / The two 0.5 coefficients are the whole rule.
    /// </summary>
    /// <remarks>
    /// 三点成对：源多项式里的项数 = 1（`link_k`）+ 取消变量数 / **系数是 1 和 0.5** / 两个任务各占一项。
    /// 系数写成 1（"整条链接由各自全算"）在解上未必立刻出错，**但会把同一对任务重复计数**——那正是 0.5 要防的。
    ///
    /// Three paired points: the source's term count is 1 (`link_k`) plus the number of cancellation variables; **the coefficients are 1 and 0.5**; and each of the two tasks holds one
    /// term. Writing 1 instead of 0.5 may not break the solution immediately, **but it double-counts the same pair** — exactly what the 0.5 prevents.
    /// </remarks>
    [Fact]
    public void SlackSource_ShouldWeightTheTasksByHalf() {
        (BunchCompilation<FlightTaskBunch, FlightTask, Aircraft, FlightTaskAssignment> compilation, List<FlightTask> tasks, LinearMetaModel<Flt64> model) = Ready();
        using LinearMetaModel<Flt64> unused = model;
        var flightLink = new FlightLink(
            new List<Link> { new ConnectingLink(tasks[0], tasks[1], new Flt64(10.0)) },
            compilation);

        flightLink.Register(model).IsOk.Should().BeTrue();

        LinearExpressionSymbol source = flightLink.Sources[0];
        source.Polynomial.Monomials.Should().HaveCount(3,
            "the link itself plus one half per task; a two-term source means the cancellation variables were silently dropped");
        source.Polynomial.Monomials[0].Coefficient.ToDouble().Should().Be(1.0, "the link itself weighs one");
        foreach (Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64> monomial in source.Polynomial.Monomials.Skip(1)) {
            monomial.Coefficient.ToDouble().Should().Be(0.5, "each task holds half of the link");
        }
    }

    /// <summary>
    /// **没有链接就什么都不建** / No links build nothing.
    /// </summary>
    /// <remarks>
    /// 两点成对：空链接表 → 注册成功 / 两张表都是空的（不是 `null`、也没有塞进去一个占位符号）。
    ///
    /// Two paired points: an empty link list registers successfully; both tables are empty (neither null nor holding a placeholder symbol).
    /// </remarks>
    [Fact]
    public void Register_ShouldBuildNothingWithoutLinks() {
        (BunchCompilation<FlightTaskBunch, FlightTask, Aircraft, FlightTaskAssignment> compilation, List<FlightTask> _, LinearMetaModel<Flt64> model) = Ready();
        using LinearMetaModel<Flt64> unused = model;
        var flightLink = new FlightLink(new List<Link>(), compilation);

        flightLink.Register(model).IsOk.Should().BeTrue();

        flightLink.Links.Should().BeEmpty();
        flightLink.Slacks.Should().BeEmpty();
    }

    /// <summary>
    /// **只有"同时含前后两个任务的束"才算接上** / Only a bunch holding both tasks stitches a link.
    /// </summary>
    /// <remarks>
    /// 四点成对：束里同时有两个任务 → `link_0` 恰好**一项** / 系数是 **1**（不是分割成本）/ 只含一个任务的束 → 一项都不加 /
    /// **第二轮换掉第一轮**（仍是一项）。把分割成本当系数会让"接了一次"变成"接了 10 次"——**名字还叫 link，语义已经换了**。
    ///
    /// Four paired points: a bunch holding both tasks gives `link_0` **exactly one** term; the coefficient is **1** (not the split cost); a bunch holding only one task adds nothing; and
    /// **the second round replaces the first** (still one term). Using the split cost as the coefficient would turn "stitched once" into "stitched ten times" — **still named link,
    /// semantically replaced**.
    /// </remarks>
    [Fact]
    public void AddColumns_ShouldCountOnlyBunchesHoldingBothTasks() {
        (BunchCompilation<FlightTaskBunch, FlightTask, Aircraft, FlightTaskAssignment> compilation, List<FlightTask> tasks, LinearMetaModel<Flt64> model) = Ready();
        using LinearMetaModel<Flt64> unused = model;
        var flightLink = new FlightLink(
            new List<Link> { new ConnectingLink(tasks[0], tasks[1], new Flt64(10.0)) },
            compilation);
        flightLink.Register(model).IsOk.Should().BeTrue();

        // 只含前驱的束：链接不成立 / a bunch holding only the predecessor: the link does not hold.
        var single = new List<FlightTaskBunch> { new(tasks[0].Aircraft!, new List<FlightTask> { tasks[0] }, 0L) };
        compilation.AddColumns(0UL, single, model).IsOk.Should().BeTrue();
        flightLink.AddColumns(0UL, single).IsOk.Should().BeTrue();
        flightLink.Links[0].Polynomial.Monomials.Should().BeEmpty();

        // 两个任务同束：链接成立，且只有一项 / both tasks in one bunch: the link holds, with exactly one term.
        var both = new List<FlightTaskBunch> { new(tasks[0].Aircraft!, new List<FlightTask> { tasks[0], tasks[1] }, 1L) };
        compilation.AddColumns(1UL, both, model).IsOk.Should().BeTrue();
        flightLink.AddColumns(1UL, both).IsOk.Should().BeTrue();
        flightLink.Links[0].Polynomial.Monomials.Should().HaveCount(1);
        flightLink.Links[0].Polynomial.Monomials[0].Coefficient.ToDouble().Should().Be(1.0,
            "one bunch counts once; the split cost belongs in the objective");

        // 再跑一轮：换掉而不是叠上去 / another round: replace, not stack.
        var again = new List<FlightTaskBunch> { new(tasks[1].Aircraft!, new List<FlightTask> { tasks[1], tasks[0] }, 2L) };
        compilation.AddColumns(2UL, again, model).IsOk.Should().BeTrue();
        flightLink.AddColumns(2UL, again).IsOk.Should().BeTrue();
        flightLink.Links[0].Polynomial.Monomials.Should().HaveCount(1, "the third round replaces the second");
    }

    /// <summary>
    /// **松弛的源指向链接符号本身** / The slack's source points at the link symbol itself.
    /// </summary>
    /// <remarks>
    /// 两点成对：加列之后源多项式的第一项**仍是同一个链接符号**（`BeSameAs`）/ `link_0` 里的项变成列变量。
    /// 如果建松弛时把 `link_k` 的多项式**拷了一份**，那么加列之后松弛看不到任何变化——**链接接上了，处罚还挂在零上**。
    ///
    /// Two paired points: after adding columns the source's first term **is still the same link symbol** (`BeSameAs`); and `link_0`'s own term becomes the column variable. If the slack
    /// had copied the link's polynomial, adding columns would leave the slack blind — **the link holds while the penalty still hangs on zero**.
    /// </remarks>
    [Fact]
    public void SlackSource_ShouldTrackTheLinkSymbol() {
        (BunchCompilation<FlightTaskBunch, FlightTask, Aircraft, FlightTaskAssignment> compilation, List<FlightTask> tasks, LinearMetaModel<Flt64> model) = Ready();
        using LinearMetaModel<Flt64> unused = model;
        var flightLink = new FlightLink(
            new List<Link> { new ConnectingLink(tasks[0], tasks[1], new Flt64(10.0)) },
            compilation);
        flightLink.Register(model).IsOk.Should().BeTrue();
        var both = new List<FlightTaskBunch> { new(tasks[0].Aircraft!, new List<FlightTask> { tasks[0], tasks[1] }, 0L) };
        compilation.AddColumns(0UL, both, model).IsOk.Should().BeTrue();

        flightLink.AddColumns(0UL, both).IsOk.Should().BeTrue();

        Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64> first = flightLink.Sources[0].Polynomial.Monomials[0];
        first.Symbol.Should().BeSameAs(flightLink.Links[0], "the slack must watch the live link symbol, not a copy of its polynomial");
        flightLink.Links[0].Polynomial.Monomials.Should().HaveCount(1);
    }

    /// <summary>
    /// **重注册不重建符号** / Re-registration does not rebuild the symbols.
    /// </summary>
    /// <remarks>
    /// 两点成对：注册两次之后表还是同一份 / 符号数不变。**重建会让同一个链接在模型里出现两个符号**，
    /// 求解时它们各自取值、彼此无关。
    ///
    /// Two paired points: registering twice leaves the same tables; the symbol count is unchanged. **Rebuilding would put two symbols for the same link into the model**, each taking its
    /// own value at solve time.
    /// </remarks>
    [Fact]
    public void Register_ShouldBeIdempotent() {
        (BunchCompilation<FlightTaskBunch, FlightTask, Aircraft, FlightTaskAssignment> compilation, List<FlightTask> tasks, LinearMetaModel<Flt64> model) = Ready();
        using LinearMetaModel<Flt64> unused = model;
        var flightLink = new FlightLink(
            new List<Link> { new ConnectingLink(tasks[0], tasks[1], new Flt64(10.0)) },
            compilation);

        flightLink.Register(model).IsOk.Should().BeTrue();
        int afterFirst = model.Tokens.Symbols.Count;
        LinearExpressionSymbol link = flightLink.Links[0];
        flightLink.Register(model).IsOk.Should().BeTrue();

        model.Tokens.Symbols.Count.Should().Be(afterFirst, "no symbol may be added twice");
        flightLink.Links[0].Should().BeSameAs(link, "the tables must not be rebuilt");
    }
}
