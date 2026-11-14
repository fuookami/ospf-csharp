// 根有向树组件测试 / Tests for the rooted-tree component.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Design;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Design.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Flow;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Structure;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Structure.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Tests;

/// <summary>
/// 根有向树组件 / The rooted-tree component.
/// </summary>
public sealed class RootedTreeComponentTest {
    /// <summary>主路径：根加一个终端可注册 / The main path registers: a root and one terminal.</summary>
    [Fact]
    public void Register_ShouldSucceedOnMainPath() =>
        Component().Register(Model()).IsOk.Should().BeTrue();

    /// <summary>
    /// **每个非根节点注册一条父弧等式**：根为 `父弧和 = 0`，其余为 `父弧和 = 启用`。
    /// **Every non-root node registers a parent equality**: `sum(parents) = 0` at the root, `= activation` elsewhere.
    /// </summary>
    [Fact]
    public void Register_ShouldRegisterParentRowPerNode() {
        LinearMetaModel<Flt64> model = Model();
        Component(nodes: 3).Register(model);

        model.Constraints.Count(constraint =>
                constraint.Name.Contains("_tree_parent_", StringComparison.Ordinal)
                && !constraint.Name.Contains("_active_", StringComparison.Ordinal))
            .Should().Be(3);
    }

    /// <summary>**每个非根节点注册一条深度上界** / Every non-root node registers a depth bound.</summary>
    [Fact]
    public void Register_ShouldRegisterDepthBoundPerNonRootNode() {
        LinearMetaModel<Flt64> model = Model();
        Component(nodes: 3).Register(model);

        model.Constraints.Count(constraint => constraint.Name.Contains("_tree_depth_bound_", StringComparison.Ordinal))
            .Should().Be(2);
    }

    /// <summary>每条候选树弧注册三条联动行 / Every candidate tree arc registers three linking rows.</summary>
    [Fact]
    public void Register_ShouldRegisterThreeLinkagesPerArc() {
        LinearMetaModel<Flt64> model = Model();
        Component(nodes: 3, arcs: 2).Register(model);

        model.Constraints.Count(constraint => constraint.Name.Contains("_tree_service_", StringComparison.Ordinal))
            .Should().Be(2);
        model.Constraints.Count(constraint => constraint.Name.Contains("_tree_parent_active_", StringComparison.Ordinal))
            .Should().Be(2);
        model.Constraints.Count(constraint => constraint.Name.Contains("_tree_child_active_", StringComparison.Ordinal))
            .Should().Be(2);
    }

    /// <summary>
    /// **每条非指向根的弧注册一条深度递推**：这正是排除有向环的那条行。
    /// **Every non-root-pointing arc registers one depth recurrence**: this is the row that excludes directed cycles.
    /// </summary>
    [Fact]
    public void Register_ShouldRegisterOrderRowPerArc() {
        LinearMetaModel<Flt64> model = Model();
        Component(nodes: 3, arcs: 2).Register(model);

        model.Constraints.Count(constraint => constraint.Name.Contains("_tree_order_", StringComparison.Ordinal))
            .Should().BeGreaterThan(0);
    }

    /// <summary>中转节点注册启用与设施联动 / A relay node registers activation and facility linkage.</summary>
    [Fact]
    public void Register_ShouldRegisterRelayActivation() {
        LinearMetaModel<Flt64> model = Model();
        Component(nodes: 4, steiner: true).Register(model);

        model.Constraints.Count(constraint => constraint.Name.Contains("_tree_node_", StringComparison.Ordinal))
            .Should().Be(0, "启用变量不是约束 / activation variables are not constraints");
        model.Constraints.Count(constraint => constraint.Name.Contains("_tree_facility_", StringComparison.Ordinal))
            .Should().BeGreaterThan(0);
    }

    /// <summary>注册后父弧表达式可查 / The parent-arc expression is queryable after registration.</summary>
    [Fact]
    public void ParentArc_ShouldBeQueryableAfterRegistration() {
        RootedTreeComponent<FltX> component = Component(nodes: 3);
        component.Register(Model());

        component.ParentArc(new NetworkNodeId("n1")).IsOk.Should().BeTrue();
    }

    /// <summary>**非候选节点被拒** / A non-candidate node is rejected.</summary>
    [Fact]
    public void ParentArc_ShouldRejectNonCandidateNode() {
        RootedTreeComponent<FltX> component = Component();
        component.Register(Model());

        component.ParentArc(new NetworkNodeId("ghost")).IsOk.Should().BeFalse();
    }

    /// <summary>未注册时查询父弧失败 / Querying parent arcs before registration fails.</summary>
    [Fact]
    public void ParentArc_ShouldRejectBeforeRegistration() =>
        Component().ParentArc(new NetworkNodeId("n1")).IsOk.Should().BeFalse();

    /// <summary>**不可复用** / The component cannot be reused.</summary>
    [Fact]
    public void Register_ShouldRejectReuse() {
        RootedTreeComponent<FltX> component = Component();
        component.Register(Model());

        component.Register(Model()).IsOk.Should().BeFalse();
    }

    /// <summary>**未注册模型提取失败** / Extraction from an unregistered model fails.</summary>
    [Fact]
    public void ExtractSolution_ShouldRejectUnregisteredModel() =>
        Component().ExtractSolution(Model()).IsOk.Should().BeFalse();

    /// <summary>**注册后未求解时提取失败** / Extraction fails before the model is solved.</summary>
    [Fact]
    public void ExtractSolution_ShouldRejectUnsolvedModel() {
        LinearMetaModel<Flt64> model = Model();
        RootedTreeComponent<FltX> component = Component();
        component.Register(model);

        component.ExtractSolution(model).IsOk.Should().BeFalse();
    }

    /// <summary>构造一个根树组件 / Build a rooted-tree component.</summary>
    private static RootedTreeComponent<FltX> Component(
        int nodes = 2,
        int arcs = 1,
        bool steiner = false) =>
        new(
            StructureProblem(nodes, arcs, steiner),
            new NetworkDesignContext<FltX>(DesignProblem(nodes, arcs, steiner), FltXNetworkSchedulingSolverValueAdapter.Instance),
            "structure");

    /// <summary>构造模型 / Build a model.</summary>
    private static LinearMetaModel<Flt64> Model() => new("structure");

    /// <summary>构造结构问题 / Build a structure problem.</summary>
    private static NetworkStructureProblem<FltX> StructureProblem(int nodes, int arcs, bool steiner) {
        var terminals = new HashSet<NetworkNodeId>();
        var steinerNodes = new HashSet<NetworkNodeId>();
        for (int index = 1; index < nodes; index++) {
            if (steiner && index == nodes - 1) {
                steinerNodes.Add(new NetworkNodeId($"n{index}"));
            } else {
                terminals.Add(new NetworkNodeId($"n{index}"));
            }
        }
        return new NetworkStructureProblem<FltX>(
            DesignProblem(nodes, arcs, steiner),
            new NetworkNodeId("n0"),
            terminals,
            steinerNodes);
    }

    /// <summary>构造建设问题 / Build a design problem.</summary>
    private static NetworkDesignProblem<FltX> DesignProblem(int nodes, int arcs, bool facility = false) {
        NetworkNode<FltX>[] networkNodes = Enumerable.Range(0, nodes)
            .Select(index => NetworkNode<FltX>.Invoke(
                new NetworkNodeId($"n{index}"), new Dictionary<string, Quantity<FltX>>()).Value)
            .ToArray();
        NetworkArc<FltX>[] networkArcs = Enumerable.Range(0, arcs)
            .Select(index => NetworkArc<FltX>.Invoke(
                new NetworkNodeId("n0"), new NetworkNodeId($"n{index + 1}"),
                Cost(2.0), null, new NetworkArcId($"a{index + 1}")).Value)
            .ToArray();
        Domain.Flow.Model.FlowArc<FltX>[] flowArcs = networkArcs
            .Select(arc => Domain.Flow.Model.FlowArc<FltX>.Invoke(
                arc, CapacityBounds<FltX>.Invoke(Meters(0.0), Meters(10.0)).Value).Value)
            .ToArray();
        NetworkGraph<NetworkNode<FltX>, NetworkArc<FltX>> graph =
            NetworkGraph<NetworkNode<FltX>, NetworkArc<FltX>>.Invoke(
                networkNodes, networkArcs,
                new NetworkGraphAccess<NetworkNode<FltX>, NetworkArc<FltX>>(
                    NodeId: node => node.Id,
                    ArcId: candidate => candidate.Id,
                    From: candidate => candidate.From,
                    To: candidate => candidate.To)).Value;

        return new NetworkDesignProblem<FltX>(
            new FlowProblem<FltX>(
                graph, flowArcs, Array.Empty<CommodityFlow<FltX>>(), new FltX(0.0),
                costUnit: NoneUnit.Instance),
            Array.Empty<NetworkService<FltX>>(),
            facility && nodes > 1
                ? new[] { new NetworkFacility<FltX>(new NetworkNodeId($"n{nodes - 1}"), Cost(5.0)) }
                : Array.Empty<NetworkFacility<FltX>>());
    }

    /// <summary>以米为单位的量 / A quantity in metres.</summary>
    private static Quantity<FltX> Meters(double value) => new(new FltX(value), SIBaseUnits.Meter);

    /// <summary>无量纲费用 / A dimensionless cost.</summary>
    private static Quantity<FltX> Cost(double value) => new(new FltX(value), NoneUnit.Instance);
}
