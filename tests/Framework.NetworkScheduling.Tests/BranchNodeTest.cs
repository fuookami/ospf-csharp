// 分支决策与分支节点测试 / Tests for branch decisions and branch nodes.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Framework.NetworkScheduling.Application.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Infrastructure;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Tests;

/// <summary>
/// 分支决策与分支节点 / Branch decisions and branch nodes.
/// </summary>
public sealed class BranchNodeTest {
    private static readonly NetworkNodeId Start = new("depot");
    private static readonly NetworkNodeId End = new("depot-end");

    /// <summary>**根节点无决策、无父、深度为 0** / The root has no decisions, no parent and depth 0.</summary>
    [Fact]
    public void Root_ShouldBeEmptyAndPending() {
        BranchNode root = BranchNode.Root(Start, End).Value;

        root.Id.Should().Be(0);
        root.ParentId.Should().BeNull();
        root.Depth.Should().Be(0);
        root.Decisions.Should().BeEmpty();
        root.Status.Should().Be(NodeStatus.Pending);
    }

    /// <summary>**根节点下界默认是 −∞** / The root's default lower bound is −∞.</summary>
    [Fact]
    public void Root_ShouldDefaultToNegativeInfinity() =>
        BranchNode.Root(Start, End).Value.EffectiveLowerBound.ToDouble().Should().Be(double.NegativeInfinity);

    /// <summary>根节点下界可显式指定 / The root's lower bound can be given explicitly.</summary>
    [Fact]
    public void Root_ShouldAcceptExplicitLowerBound() =>
        BranchNode.Root(Start, End, new Flt64(-5.0)).Value.EffectiveLowerBound.ToDouble()
            .Should().BeApproximately(-5.0, 1e-9);

    /// <summary>**子节点继承父下界** / A child inherits its parent's lower bound.</summary>
    [Fact]
    public void Child_ShouldInheritParentLowerBound() {
        BranchNode root = BranchNode.Root(Start, End, new Flt64(-5.0)).Value;
        BranchNode child = BranchNode.Child(
            1,
            root,
            new ForbidVehicleType(new VehicleTypeId("truck"), new CustomerId("c1"), new NetworkNodeId("n1")),
            Start,
            End).Value;

        child.ParentId.Should().Be(0);
        child.Depth.Should().Be(1);
        child.InheritedLowerBound.ToDouble().Should().BeApproximately(-5.0, 1e-9);
        child.EffectiveLowerBound.ToDouble().Should().BeApproximately(-5.0, 1e-9);
    }

    /// <summary>**求解后有效下界换成本节点的 LP 值** / After solving, the effective bound becomes this node's LP value.</summary>
    [Fact]
    public void Solve_ShouldReplaceEffectiveLowerBound() {
        BranchNode root = BranchNode.Root(Start, End, new Flt64(-5.0)).Value;

        root.Solve(new Flt64(3.5));

        root.EffectiveLowerBound.ToDouble().Should().BeApproximately(3.5, 1e-9);
        root.Status.Should().Be(NodeStatus.Solved);
        root.InheritedLowerBound.ToDouble().Should().BeApproximately(-5.0, 1e-9, "继承值不被改写 / the inherited value stays");
    }

    /// <summary>不可行与剪枝是两种不同状态 / Infeasible and pruned are distinct states.</summary>
    [Fact]
    public void MarkInfeasibleAndPrune_ShouldBeDistinct() {
        BranchNode node = BranchNode.Root(Start, End).Value;

        node.MarkInfeasible();
        node.Status.Should().Be(NodeStatus.Infeasible);

        BranchNode other = BranchNode.Root(Start, End).Value;
        other.Prune();
        other.Status.Should().Be(NodeStatus.Pruned);
    }

    /// <summary>**无 incumbent 时任何节点都可改进** / Without an incumbent any node can improve.</summary>
    [Fact]
    public void CanImprove_ShouldBeTrueWithoutIncumbent() =>
        BranchNode.Root(Start, End).Value.CanImprove(null, new Flt64(1e-6)).Should().BeTrue();

    /// <summary>下界低于 incumbent 时可改进 / A bound below the incumbent can improve.</summary>
    [Fact]
    public void CanImprove_ShouldBeTrueWhenBoundBelow() {
        BranchNode node = BranchNode.Root(Start, End).Value;
        node.Solve(new Flt64(9.0));

        node.CanImprove(new Flt64(10.0), new Flt64(1e-6)).Should().BeTrue();
    }

    /// <summary>**下界不低于 incumbent 时不可改进** / A bound not below the incumbent cannot improve.</summary>
    [Fact]
    public void CanImprove_ShouldBeFalseWhenBoundAbove() {
        BranchNode node = BranchNode.Root(Start, End).Value;
        node.Solve(new Flt64(10.5));

        node.CanImprove(new Flt64(10.0), new Flt64(1e-6)).Should().BeFalse();
    }

    /// <summary>**容差内的相等视为不可改进** / Equality within tolerance counts as no improvement.</summary>
    [Fact]
    public void CanImprove_ShouldRespectTolerance() {
        BranchNode node = BranchNode.Root(Start, End).Value;
        node.Solve(new Flt64(10.0));

        node.CanImprove(new Flt64(10.0), new Flt64(1e-6)).Should().BeFalse();
    }

    /// <summary>**禁止车型决策进入禁止节点集** / A forbid-vehicle-type decision lands in the forbidden node set.</summary>
    [Fact]
    public void Accumulate_ShouldCollectForbiddenNodes() {
        var decisions = new List<IBranchDecision> {
            new ForbidVehicleType(new VehicleTypeId("truck"), new CustomerId("c1"), new NetworkNodeId("n1")),
        };

        BranchMask<VehicleTypeId> mask = BranchDecisionAccumulator.AccumulateBranchMask(Start, End, decisions).Value;
        mask.AllowsNode(new VehicleTypeId("truck"), new NetworkNodeId("n1")).Should().BeFalse();
        mask.AllowsNode(new VehicleTypeId("van"), new NetworkNodeId("n1")).Should().BeTrue();
    }

    /// <summary>**要求车型决策进入必选资源集** / A require-vehicle-type decision lands in the required resources.</summary>
    [Fact]
    public void Accumulate_ShouldCollectRequiredResources() {
        var decisions = new List<IBranchDecision> {
            new RequireVehicleType(new VehicleTypeId("truck"), new CustomerId("c1"), new NetworkNodeId("n1")),
        };

        BranchMask<VehicleTypeId> mask = BranchDecisionAccumulator.AccumulateBranchMask(Start, End, decisions).Value;
        mask.RequiredResources[new NetworkNodeId("n1")].Should().Be(new VehicleTypeId("truck"));
    }

    /// <summary>**禁止弧决策进入禁止弧集** / A forbid-arc decision lands in the forbidden arc set.</summary>
    [Fact]
    public void Accumulate_ShouldCollectForbiddenArcs() {
        var from = new NetworkNodeId("n1");
        var to = new NetworkNodeId("n2");
        var decisions = new List<IBranchDecision> {
            new ForbidArc(new VehicleTypeId("truck"), from, to),
        };

        BranchMask<VehicleTypeId> mask = BranchDecisionAccumulator.AccumulateBranchMask(Start, End, decisions).Value;
        mask.ForbiddenArcs.Should().ContainSingle();
        mask.AllowsArc(new VehicleTypeId("truck"), from, to).Should().BeFalse();
    }

    /// <summary>**必选弧决策进入必选弧集** / A require-arc decision lands in the required arc set.</summary>
    [Fact]
    public void Accumulate_ShouldCollectRequiredArcs() {
        var from = new NetworkNodeId("n1");
        var to = new NetworkNodeId("n2");
        var decisions = new List<IBranchDecision> {
            new RequireArc(new VehicleTypeId("truck"), from, to),
        };

        BranchMask<VehicleTypeId> mask = BranchDecisionAccumulator.AccumulateBranchMask(Start, End, decisions).Value;
        mask.RequiredArcs.Should().ContainSingle();
    }

    /// <summary>
    /// **同一客户的两次要求车型决策是后写覆盖，而不是两条都留下**——那是分支冲突的解法。
    /// **Two require-vehicle-type decisions for the same customer are last-write-wins, not both kept** — that is how a branch
    /// conflict is resolved.
    /// </summary>
    [Fact]
    public void Accumulate_ShouldLetLaterRequireOverride() {
        var node = new NetworkNodeId("n1");
        var decisions = new List<IBranchDecision> {
            new RequireVehicleType(new VehicleTypeId("truck"), new CustomerId("c1"), node),
            new RequireVehicleType(new VehicleTypeId("van"), new CustomerId("c1"), node),
        };

        BranchMask<VehicleTypeId> mask = BranchDecisionAccumulator.AccumulateBranchMask(Start, End, decisions).Value;
        mask.RequiredResources[node].Should().Be(new VehicleTypeId("van"));
    }

    /// <summary>**重复的禁止决策按集合收敛** / Repeated forbid decisions collapse as a set.</summary>
    [Fact]
    public void Accumulate_ShouldCollapseRepeatedForbids() {
        var node = new NetworkNodeId("n1");
        var truck = new VehicleTypeId("truck");
        var decisions = new List<IBranchDecision> {
            new ForbidVehicleType(truck, new CustomerId("c1"), node),
            new ForbidVehicleType(truck, new CustomerId("c1"), node),
        };

        BranchMask<VehicleTypeId> mask = BranchDecisionAccumulator.AccumulateBranchMask(Start, End, decisions).Value;
        mask.ForbiddenNodes[truck].Should().ContainSingle();
    }

    /// <summary>空决策集给出空遮罩 / An empty decision set gives an empty mask.</summary>
    [Fact]
    public void Accumulate_ShouldHandleEmptyDecisions() {
        BranchMask<VehicleTypeId> mask =
            BranchDecisionAccumulator.AccumulateBranchMask(Start, End, Array.Empty<IBranchDecision>()).Value;

        mask.ForbiddenNodes.Should().BeEmpty();
        mask.RequiredResources.Should().BeEmpty();
        mask.ForbiddenArcs.Should().BeEmpty();
        mask.RequiredArcs.Should().BeEmpty();
    }

    /// <summary>**仓库节点永远可访问**，即使被禁止决策点名 / Depot nodes stay visitable even when a forbid decision names them.</summary>
    [Fact]
    public void Accumulate_ShouldKeepDepotsVisitable() {
        var decisions = new List<IBranchDecision> {
            new ForbidVehicleType(new VehicleTypeId("truck"), new CustomerId("depot"), Start),
        };

        BranchMask<VehicleTypeId> mask = BranchDecisionAccumulator.AccumulateBranchMask(Start, End, decisions).Value;
        mask.AllowsNode(new VehicleTypeId("truck"), Start).Should().BeTrue();
    }

    /// <summary>**子节点的决策是父决策加上新增的一条** / A child's decisions are the parent's plus the new one.</summary>
    [Fact]
    public void Child_ShouldExtendParentDecisions() {
        BranchNode root = BranchNode.Root(Start, End).Value;
        IBranchDecision first = new ForbidVehicleType(
            new VehicleTypeId("truck"), new CustomerId("c1"), new NetworkNodeId("n1"));
        BranchNode child = BranchNode.Child(1, root, first, Start, End).Value;
        IBranchDecision second = new ForbidArc(
            new VehicleTypeId("truck"), new NetworkNodeId("n1"), new NetworkNodeId("n2"));
        BranchNode grandchild = BranchNode.Child(2, child, second, Start, End).Value;

        grandchild.Decisions.Should().HaveCount(2);
        grandchild.Depth.Should().Be(2);
        grandchild.ParentId.Should().Be(1);
        root.Decisions.Should().BeEmpty("父节点不被子节点改动 / the parent is untouched by its child");
    }

    /// <summary>**兄弟节点的遮罩互不影响** / Sibling masks do not affect each other.</summary>
    [Fact]
    public void Child_ShouldProduceIndependentSiblings() {
        BranchNode root = BranchNode.Root(Start, End).Value;
        BranchNode left = BranchNode.Child(
            1, root,
            new ForbidVehicleType(new VehicleTypeId("truck"), new CustomerId("c1"), new NetworkNodeId("n1")),
            Start, End).Value;
        BranchNode right = BranchNode.Child(
            2, root,
            new RequireVehicleType(new VehicleTypeId("van"), new CustomerId("c1"), new NetworkNodeId("n1")),
            Start, End).Value;

        left.BranchMask.AllowsNode(new VehicleTypeId("truck"), new NetworkNodeId("n1")).Should().BeFalse();
        right.BranchMask.RequiredResources[new NetworkNodeId("n1")].Should().Be(new VehicleTypeId("van"));
        left.BranchMask.RequiredResources.Should().BeEmpty();
    }
}
