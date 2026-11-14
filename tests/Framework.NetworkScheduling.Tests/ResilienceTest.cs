// 韧性域测试 / Tests for the resilience domain.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Design;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Design.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Flow;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Flow.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Resilience;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Resilience.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Tests;

/// <summary>
/// 韧性域 / The resilience domain.
/// </summary>
public sealed class ResilienceTest {
    private static readonly NetworkNodeId Plant = new("plant");
    private static readonly NetworkNodeId Store = new("store");
    private static readonly NetworkArcId Service = new("arc:plant-store");

    /// <summary>**单弧故障只生成单故障，不生成组合** / Single-arc failures stay single, never combinations.</summary>
    [Fact]
    public void SingleArcFailures_ShouldNotEnumerateCombinations() {
        var arcs = new HashSet<NetworkArcId> { new("arc:a"), new("arc:b") };

        IReadOnlyList<FailureScenario<Flt64>> scenarios = FailureScenario<Flt64>.SingleArcFailures(arcs);

        scenarios.Should().HaveCount(2, "两条弧各一个单故障 / one failure per arc");
        scenarios.Should().OnlyContain(scenario => scenario.ClosedArcs.Count == 1);
    }

    /// <summary>单弧故障场景用 `arc:` 前缀标识 / Single-arc scenarios use the `arc:` prefix.</summary>
    [Fact]
    public void SingleArcFailures_ShouldUseArcPrefix() {
        IReadOnlyList<FailureScenario<Flt64>> scenarios =
            FailureScenario<Flt64>.SingleArcFailures(new HashSet<NetworkArcId> { Service });

        scenarios.Should().ContainSingle();
        scenarios[0].Id.Should().Be("arc:arc:plant-store");
        scenarios[0].ClosedArcs.Should().Contain(Service);
    }

    /// <summary>单节点故障场景用 `node:` 前缀标识 / Single-node scenarios use the `node:` prefix.</summary>
    [Fact]
    public void SingleNodeFailures_ShouldUseNodePrefix() {
        IReadOnlyList<FailureScenario<Flt64>> scenarios =
            FailureScenario<Flt64>.SingleNodeFailures(new HashSet<NetworkNodeId> { Plant });

        scenarios.Should().ContainSingle();
        scenarios[0].Id.Should().Be("node:plant");
        scenarios[0].FailedNodes.Should().Contain(Plant);
    }

    /// <summary>**未被覆盖的松弛为 null，表示继承名义配置** / An unspecified slack is null, meaning inherit the nominal one.</summary>
    [Fact]
    public void FailureScenario_ShouldLeaveSlacksNullByDefault() =>
        new FailureScenario<Flt64>("arc:a").Slacks.Should().BeNull();

    /// <summary>显式给出的松弛被保留 / Explicit slacks are kept.</summary>
    [Fact]
    public void FailureScenario_ShouldKeepExplicitSlacks() {
        var scenario = new FailureScenario<Flt64>("arc:a", slacks: new List<FlowSlack<Flt64>>());

        scenario.Slacks.Should().NotBeNull().And.BeEmpty();
    }

    /// <summary>**重复场景标识被拒** / Duplicate scenario identities are rejected.</summary>
    [Fact]
    public void Register_ShouldRejectDuplicateScenarioIds() {
        NetworkResilienceContext<Flt64> context = Context(
            new[] { new FailureScenario<Flt64>("f1"), new FailureScenario<Flt64>("f1") });

        context.Register(Model()).IsOk.Should().BeFalse();
    }

    /// <summary>**占用 nominal 标识被拒** / Reserving the nominal identity is rejected.</summary>
    [Fact]
    public void Register_ShouldRejectReservedNominalId() {
        NetworkResilienceContext<Flt64> context = Context(new[] { new FailureScenario<Flt64>("nominal") });

        context.Register(Model()).IsOk.Should().BeFalse();
    }

    /// <summary>**空白场景标识被拒** / A blank scenario identity is rejected.</summary>
    [Fact]
    public void Register_ShouldRejectBlankScenarioId() {
        NetworkResilienceContext<Flt64> context = Context(new[] { new FailureScenario<Flt64>("  ") });

        context.Register(Model()).IsOk.Should().BeFalse();
    }

    /// <summary>无场景时注册成功并落下名义场景 / Registration succeeds and lands the nominal scenario.</summary>
    [Fact]
    public void Register_ShouldSucceedWithoutScenarios() {
        NetworkResilienceContext<Flt64> context = Context(Array.Empty<FailureScenario<Flt64>>());
        LinearMetaModel<Flt64> model = Model();

        context.Register(model).IsOk.Should().BeTrue();
        context.Aggregation.ScenarioFlows.Keys.Should().Contain("nominal");
    }

    /// <summary>**同一模型重复注册幂等** / Repeated registration on the same model is idempotent.</summary>
    [Fact]
    public void Register_ShouldBeIdempotentOnSameModel() {
        NetworkResilienceContext<Flt64> context = Context(Array.Empty<FailureScenario<Flt64>>());
        LinearMetaModel<Flt64> model = Model();

        context.Register(model).IsOk.Should().BeTrue();
        context.Register(model).IsOk.Should().BeTrue();
        context.Aggregation.ScenarioFlows.Should().ContainKey("nominal");
    }

    /// <summary>**跨模型复用被拒** / Reuse across models is rejected.</summary>
    [Fact]
    public void Register_ShouldRejectSecondModel() {
        NetworkResilienceContext<Flt64> context = Context(Array.Empty<FailureScenario<Flt64>>());
        context.Register(Model()).IsOk.Should().BeTrue();

        context.Register(Model()).IsOk.Should().BeFalse();
    }

    /// <summary>**未注册时取场景流失败** / Reading a scenario flow before registration fails.</summary>
    [Fact]
    public void Flow_ShouldFailBeforeRegistration() =>
        Context(Array.Empty<FailureScenario<Flt64>>()).Flow("nominal").IsOk.Should().BeFalse();

    /// <summary>注册后可取名义场景流 / The nominal scenario flow is available after registration.</summary>
    [Fact]
    public void Flow_ShouldReturnNominalAfterRegistration() {
        NetworkResilienceContext<Flt64> context = Context(Array.Empty<FailureScenario<Flt64>>());
        context.Register(Model());

        context.Flow("nominal").IsOk.Should().BeTrue();
    }

    /// <summary>**未知场景被拒** / An unknown scenario is rejected.</summary>
    [Fact]
    public void Flow_ShouldRejectUnknownScenario() {
        NetworkResilienceContext<Flt64> context = Context(Array.Empty<FailureScenario<Flt64>>());
        context.Register(Model());

        context.Flow("ghost").IsOk.Should().BeFalse();
    }

    /// <summary>**未注册时提取失败** / Extraction before registration fails.</summary>
    [Fact]
    public void ExtractSolution_ShouldFailBeforeRegistration() =>
        Context(Array.Empty<FailureScenario<Flt64>>()).ExtractSolution(Model()).IsOk.Should().BeFalse();

    /// <summary>**未求解时提取失败** / Extraction on an unsolved model fails.</summary>
    [Fact]
    public void ExtractSolution_ShouldFailWhenUnsolved() {
        NetworkResilienceContext<Flt64> context = Context(Array.Empty<FailureScenario<Flt64>>());
        LinearMetaModel<Flt64> model = Model();
        context.Register(model);

        context.ExtractSolution(model).IsOk.Should().BeFalse();
    }

    /// <summary>**连通要求起止相同时被拒** / A connectivity requirement with equal endpoints is rejected.</summary>
    [Fact]
    public void ConnectivityFlow_ShouldRejectSameEndpoint() {
        NetworkResilienceContext<Flt64> context = Context(
            Array.Empty<FailureScenario<Flt64>>(),
            new[] { new ConnectivityRequirement(Plant, Plant) });

        context.Register(Model()).IsOk.Should().BeFalse();
    }

    /// <summary>**可达性要求不能要多条路径** / A reachable requirement cannot ask for several paths.</summary>
    [Fact]
    public void ConnectivityFlow_ShouldRejectReachableWithMultiplePaths() {
        NetworkResilienceContext<Flt64> context = Context(
            Array.Empty<FailureScenario<Flt64>>(),
            new[] { new ConnectivityRequirement(Plant, Store, Paths: 2) });

        context.Register(Model()).IsOk.Should().BeFalse();
    }

    /// <summary>**未知端点的连通要求被拒** / A connectivity requirement with an unknown endpoint is rejected.</summary>
    [Fact]
    public void ConnectivityFlow_ShouldRejectUnknownEndpoint() {
        NetworkResilienceContext<Flt64> context = Context(
            Array.Empty<FailureScenario<Flt64>>(),
            new[] { new ConnectivityRequirement(Plant, new NetworkNodeId("ghost")) });

        context.Register(Model()).IsOk.Should().BeFalse();
    }

    /// <summary>**连通组件不可复用**：同一组件注册两次被拒 / A connectivity component cannot be reused.</summary>
    [Fact]
    public void ConnectivityFlow_ShouldRejectReuse() {
        NetworkResilienceContext<Flt64> design = Context(Array.Empty<FailureScenario<Flt64>>());
        LinearMetaModel<Flt64> model = Model();
        design.Register(model);
        var witness = new ConnectivityFlow<Flt64>(
            design.Design,
            new ConnectivityRequirement(Plant, Store),
            0,
            "network_resilience");
        witness.Register(model).IsOk.Should().BeTrue();

        witness.Register(model).IsOk.Should().BeFalse();
    }

    /// <summary>单弧故障场景与名义场景一同注册 / A single-arc scenario registers alongside the nominal one.</summary>
    [Fact]
    public void Register_ShouldLandScenarioFlow() {
        NetworkResilienceContext<Flt64> context = Context(
            new[] { new FailureScenario<Flt64>("f1", closedArcs: new HashSet<NetworkArcId> { Service }) });
        LinearMetaModel<Flt64> model = Model();

        context.Register(model).IsOk.Should().BeTrue();
        context.Aggregation.ScenarioFlows.Keys.Should().Contain("f1");
    }

    /// <summary>**故障场景引用未知服务被拒** / A failed scenario referencing an unknown service is rejected.</summary>
    [Fact]
    public void Register_ShouldRejectUnknownClosedArc() {
        NetworkResilienceContext<Flt64> context = Context(new[] {
            new FailureScenario<Flt64>("f1", closedArcs: new HashSet<NetworkArcId> { new("arc:ghost") }),
        });

        context.Register(Model()).IsOk.Should().BeFalse();
    }

    /// <summary>**降级容量高于名义容量被拒** / A degraded capacity above the nominal one is rejected.</summary>
    [Fact]
    public void Register_ShouldRejectInflatedCapacity() {
        NetworkResilienceContext<Flt64> context = Context(new[] {
            new FailureScenario<Flt64>("f1", upperCapacities: new Dictionary<NetworkArcId, Quantity<Flt64>> {
                [Service] = Meters(99.0),
            }),
        });

        context.Register(Model()).IsOk.Should().BeFalse();
    }

    /// <summary>构造上下文 / Build the context.</summary>
    private static NetworkResilienceContext<Flt64> Context(
        IReadOnlyList<FailureScenario<Flt64>> scenarios,
        IReadOnlyList<ConnectivityRequirement>? connectivity = null) =>
        new(Design(), scenarios, connectivity);

    /// <summary>构造共享建设上下文 / Build the shared design context.</summary>
    private static NetworkDesignContext<Flt64> Design() =>
        new(
            new NetworkDesignProblem<Flt64>(FlowProblem()),
            Flt64NetworkSchedulingSolverValueAdapter.Instance);

    /// <summary>构造名义流问题 / Build the nominal flow problem.</summary>
    private static FlowProblem<Flt64> FlowProblem() {
        var attributes = new Dictionary<string, Quantity<Flt64>>();
        var nodes = new List<NetworkNode<Flt64>> {
            new(Plant, attributes),
            new(Store, attributes),
        };
        NetworkArc<Flt64> arc = NetworkArc<Flt64>.Invoke(Plant, Store, Meters(1.0), null, Service).Value;
        return new FlowProblem<Flt64>(
            NetworkGraph<NetworkNode<Flt64>, NetworkArc<Flt64>>.Invoke<Flt64>(nodes, new[] { arc }).Value,
            new[] {
                FlowArc<Flt64>.Invoke(arc, new CapacityBounds<Flt64>(Meters(0.0), Meters(10.0))).Value,
            },
            new[] {
                new CommodityFlow<Flt64>(
                    FlowCommodity<Flt64>.Invoke(
                        "req",
                        new[] {
                            SupplyDemand<Flt64>.Invoke(Plant, NodeBalance<Flt64>.Invoke(Meters(5.0)).Value)
                                .Value,
                            SupplyDemand<Flt64>.Invoke(Store, NodeBalance<Flt64>.Invoke(Meters(-5.0)).Value)
                                .Value,
                        }).Value,
                    SIBaseUnits.Meter),
            },
            new Flt64(0.0),
            SIBaseUnits.Meter);
    }

    /// <summary>构造模型 / Build a model.</summary>
    private static LinearMetaModel<Flt64> Model() => new("network_resilience");

    /// <summary>以米为单位的量 / A quantity in metres.</summary>
    private static Quantity<Flt64> Meters(double value) => new(new Flt64(value), SIBaseUnits.Meter);
}
