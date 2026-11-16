// 流量终于是一种资源/ A flow is finally a resource.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Rule.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Resource.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **流量终于是一种资源** / A flow is finally a resource.
/// </summary>
/// <remarks>
/// Demo4 的 `Flow` 此前是个**普通类**：有 `Id`/`Airport`/`Scene`/`Capacities`，却不继承 `ConnectionResource`，所以**当不了资源**，
/// 也就装不进 `BunchSchedulingConnectionResourceUsage`。它是 `ConnectionResource&lt;FlowControl, FltX&gt;`，而它的容量类型
/// `FlowControl` 本身也必须是 `AbstractResourceCapacity`——两者缺一，整条链路就断。
///
/// **一个"长得像资源、但不是资源"的类，比完全没有这个类更难发现。**
///
/// Demo4's `Flow` used to be a **plain class**: it had `Id`/`Airport`/`Scene`/`Capacities` but did not derive from `ConnectionResource`, so **it could not serve as a resource** and could not
/// be wired into `BunchSchedulingConnectionResourceUsage`. In the code it is a `ConnectionResource&lt;FlowControl, FltX&gt;`, and its capacity type `FlowControl` must itself be an
/// `AbstractResourceCapacity` — with either piece missing the chain breaks. **A class that looks like a resource without being one is harder to spot than a missing class.**
/// </remarks>
public class Demo4FlowResourceTests {
    private static readonly DateTimeOffset Origin = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static TimeWindow<Flt64> Window() => new(new TimeRange(Origin, Origin.AddDays(1)), false, TimeSpan.FromHours(8));

    private static Airport AnyAirport() => new(new ICAO("ZBAA"), AirportType.Domestic);

    private static FlowControl Rule(FlowControlScene scene, ulong amount) => new(
        "R-" + scene,
        AnyAirport(),
        new TimeRange(Origin, Origin.AddHours(8)),
        scene,
        new FlowControlCapacity(amount, TimeSpan.FromHours(8)));

    private static Flow Subject() => new("F1", AnyAirport(), FlowControlScene.Departure, new List<FlowControl> {
        Rule(FlowControlScene.Departure, 3UL),
        Rule(FlowControlScene.Arrival, 5UL),
    });

    /// <summary>
    /// **流量真的是连接资源，容量真的是资源容量** / The flow really is a connection resource and its rules really are capacities.
    /// </summary>
    /// <remarks>
    /// 四点成对：`Flow` 是 `ConnectionResource`（编译期就钉住，写成运行时断言是为了让人读到时明白意图）/
    /// 名称沿用`ICAO_flow` / `FlowControl` 是 `IAbstractResourceCapacity` / 容量的上下界就是规则的额度（下界 0、上界 3）。
    ///
    /// Four paired points: `Flow` is a `ConnectionResource` (the compiler pins it; the runtime assertion is there so readers see the intent); the name follows its
    /// `ICAO_flow`; `FlowControl` is an `IAbstractResourceCapacity`; and the capacity bounds are the rule's allowance (lower 0, upper 3).
    /// </remarks>
    [Fact]
    public void Flow_ShouldBeAConnectionResourceWithRuleCapacities() {
        Flow flow = Subject();

        flow.Should().BeAssignableTo<ConnectionResource<FlowControl, Flt64>>();
        flow.Name.Should().Be("ZBAA_flow");
        flow.InitialQuantityValue.ToDouble().Should().Be(0.0, "the baseline starts flows at zero");

        FlowControl rule = flow.Capacities[0];
        rule.Should().BeAssignableTo<IAbstractResourceCapacity<Flt64>>();
        rule.LowerBound.ToDouble().Should().Be(0.0);
        rule.UpperBound.ToDouble().Should().Be(3.0, "the rule's amount is its upper bound");
        rule.Closed.Should().BeFalse();
    }

    /// <summary>
    /// **没有任务对就没有占用** / No task pair means no occupancy.
    /// </summary>
    /// <remarks>
    /// 两点成对：两个任务都给 `null` → 占用 0（场景判定对 `null` 一律不命中）/ 容量列表为空时同样是 0。
    /// **这一条覆盖的正是"空指针进场景判定"那条路径**——它此前根本不存在，因为 `Flow` 不是资源。
    ///
    /// Two paired points: both tasks null yields 0 occupancy (the scene test never fires on null); an empty capacity list is 0 too. **This covers the "null pair reaches the scene
    /// test" path** — a path that could not exist before because `Flow` was not a resource.
    /// </remarks>
    [Fact]
    public void UsedBy_ShouldBeZeroWithoutATaskPair() {
        Flow flow = Subject();
        Flt64 used = flow.UsedBy<FlightTask,
            Aircraft,
            FlightTaskAssignment>(null!, null!, new TimeRange(Origin, Origin.AddHours(1)));

        used.ToDouble().Should().Be(0.0);

        var empty = new Flow("F2", AnyAirport(), FlowControlScene.Arrival, Array.Empty<FlowControl>());
        empty.UsedBy<FlightTask,
            Aircraft,
            FlightTaskAssignment>(null!, null!, new TimeRange(Origin, Origin.AddHours(1)))
            .ToDouble().Should().Be(0.0);
    }

    /// <summary>
    /// **装配之后任务时间行与流量行同时在模型里** / After assembly both task-time rows and flow rows live in the model.
    /// </summary>
    /// <remarks>
    /// 四点成对：聚合注册成功 / 模型里有任务时间行 / **有流量规则的聚合建出 `flow_quantity_` 行** /
    /// **没有流量规则时一条流量行都不建**——"没规则"不等于"建一堆恒零符号"。
    ///
    /// Four paired points: the aggregation registers; the model holds task-time rows; **an aggregation with flow rules builds `flow_quantity_` rows**; and **with no flow rules not a single
    /// flow row appears** — "no rules" is not "a pile of always-zero symbols".
    /// </remarks>
    [Fact]
    public void Aggregation_ShouldRegisterTaskTimeAndFlowTogether() {
        using var model = new LinearMetaModel<Flt64>();
        var aggregation = new Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchCompilation.Aggregation(
            Window(), new List<Aircraft>(), new List<FlightTask>(), new List<Flow> { Subject() });

        aggregation.Register(model).IsOk.Should().BeTrue();

        string joined = string.Join(" | ", model.Tokens.Symbols.Select(symbol => symbol.Name ?? string.Empty));
        joined.Should().Contain("flow_quantity_", "flow rules are wired into the aggregation");

        using var bare = new LinearMetaModel<Flt64>();
        var noFlow = new Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchCompilation.Aggregation(
            Window(), new List<Aircraft>(), new List<FlightTask>());
        noFlow.Register(bare).IsOk.Should().BeTrue();
        string bareNames = string.Join(" | ", bare.Tokens.Symbols.Select(symbol => symbol.Name ?? string.Empty));
        bareNames.Should().NotContain("flow_quantity_", "without flow rules there must be no flow rows");
    }

    /// <summary>
    /// **流量能装进连接资源用量并建出数量行** / A flow can be wired into connection usage and build its quantity rows.
    /// </summary>
    /// <remarks>
    /// 三点成对：注册成功 / 数量表非空 / 模型里出现 `flow_quantity_` 行。
    /// **这正是 `Flow` 此前做不到的事**——它不是 `ConnectionResource`，连作为类型参数都过不了。
    ///
    /// Three paired points: registration succeeds; the quantity table is non-empty; and `flow_quantity_` rows appear in the model. **That is exactly what `Flow` could not do
    /// before** — not being a `ConnectionResource`, it could not even be a type argument.
    /// </remarks>
    [Fact]
    public void Flow_ShouldRegisterAsConnectionUsage() {
        using var model = new LinearMetaModel<Flt64>();
        var usage = new BunchSchedulingConnectionResourceUsage<Flow, FlowControl, Flt64>(
            Window(), new List<Flow> { Subject() }, "flow");

        usage.Register(model).IsOk.Should().BeTrue();
        usage.Quantity.Should().NotBeNull();

        string joined = string.Join(" | ", model.Tokens.Symbols.Select(symbol => symbol.Name ?? string.Empty));
        joined.Should().Contain("flow_quantity_");
    }
}
