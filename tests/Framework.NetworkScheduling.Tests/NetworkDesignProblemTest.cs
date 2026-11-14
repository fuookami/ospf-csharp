// 网络建设与结构问题测试 / Tests for the design and structure problem data layer.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Design;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Flow;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Structure;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Tests;

/// <summary>
/// 网络建设与结构问题数据层 / The design and structure problem data layer.
/// </summary>
public sealed class NetworkDesignProblemTest {
    /// <summary>建设等级保留标识、总容量与费用 / A capacity level keeps its identity, total capacity and cost.</summary>
    [Fact]
    public void CapacityLevel_ShouldRetainFields() {
        var level = new CapacityLevel<FltX>("high", Meters(100.0), Meters(50.0));

        level.Id.Should().Be("high");
        level.Capacity.Value.ToFlt64().ToDouble().Should().BeApproximately(100.0, 1e-9);
        level.Cost.Value.ToFlt64().ToDouble().Should().BeApproximately(50.0, 1e-9);
    }

    /// <summary>
    /// **容量是建设后的总容量，不是增量**：两个等级各自声明总量，不存在"叠加"语义。
    /// **Capacity is the resulting total, not an increment**: each level declares a total and there is no stacking semantics.
    /// </summary>
    [Fact]
    public void CapacityLevel_ShouldBeTotalsNotIncrements() {
        var low = new CapacityLevel<FltX>("low", Meters(10.0), Meters(1.0));
        var high = new CapacityLevel<FltX>("high", Meters(100.0), Meters(5.0));

        global::System.Math.Max(low.Capacity.Value.ToFlt64().ToDouble(), high.Capacity.Value.ToFlt64().ToDouble())
            .Should().BeApproximately(100.0, 1e-9, "选高等级得到 100，而非 110 / picking high yields 100, not 110");
    }

    /// <summary>服务保留等级列表 / A service retains its level list.</summary>
    [Fact]
    public void NetworkService_ShouldRetainLevels() {
        var service = new NetworkService<FltX>(
            new NetworkArcId("a1"),
            new[] {
                new CapacityLevel<FltX>("low", Meters(10.0), Meters(1.0)),
                new CapacityLevel<FltX>("high", Meters(100.0), Meters(5.0)),
            },
            required: true);

        service.Levels.Should().HaveCount(2);
        service.Required.Should().BeTrue();
    }

    /// <summary>服务等级列表是快照 / The service level list is a snapshot.</summary>
    [Fact]
    public void NetworkService_ShouldSnapshotLevels() {
        var levels = new List<CapacityLevel<FltX>> { new("low", Meters(10.0), Meters(1.0)) };
        var service = new NetworkService<FltX>(new NetworkArcId("a1"), levels);

        levels.Add(new CapacityLevel<FltX>("high", Meters(100.0), Meters(5.0)));

        service.Levels.Should().HaveCount(1);
    }

    /// <summary>设施保留容量行集合 / A facility retains its capacity-row set.</summary>
    [Fact]
    public void NetworkFacility_ShouldRetainCapacityRows() {
        var facility = new NetworkFacility<FltX>(
            new NetworkNodeId("n1"), Meters(7.0), required: true,
            capacityRows: new HashSet<string> { "throughput" });

        facility.CapacityRows.Should().Contain("throughput");
        facility.Required.Should().BeTrue();
        facility.Cost.Value.ToFlt64().ToDouble().Should().BeApproximately(7.0, 1e-9);
    }

    /// <summary>设施容量行集合是快照 / The facility capacity-row set is a snapshot.</summary>
    [Fact]
    public void NetworkFacility_ShouldSnapshotCapacityRows() {
        var rows = new HashSet<string> { "a" };
        var facility = new NetworkFacility<FltX>(new NetworkNodeId("n1"), Meters(7.0), capacityRows: rows);

        rows.Add("b");

        facility.CapacityRows.Should().HaveCount(1);
    }

    /// <summary>建设问题保留流问题与服务设施 / A design problem retains the flow problem, services and facilities.</summary>
    [Fact]
    public void NetworkDesignProblem_ShouldRetainParts() {
        var problem = new NetworkDesignProblem<FltX>(
            FlowProblem(),
            new[] { new NetworkService<FltX>(new NetworkArcId("a1")) },
            new[] { new NetworkFacility<FltX>(new NetworkNodeId("n1"), Meters(1.0)) },
            Meters(50.0));

        problem.Services.Should().HaveCount(1);
        problem.Facilities.Should().HaveCount(1);
        problem.Budget!.Value.ToFlt64().ToDouble().Should().BeApproximately(50.0, 1e-9);
    }

    /// <summary>预算可缺省 / The budget is optional.</summary>
    [Fact]
    public void NetworkDesignProblem_ShouldAllowNoBudget() =>
        new NetworkDesignProblem<FltX>(FlowProblem()).Budget.Should().BeNull();

    /// <summary>
    /// **根树节点 = 根 ∪ 终端 ∪ 中转**：三者的并集就是树可能涉及的节点集。
    /// **Tree nodes = root ∪ terminals ∪ Steiner nodes**: their union is the set the tree may involve.
    /// </summary>
    [Fact]
    public void NetworkStructureProblem_ShouldUnionTreeNodes() {
        var problem = new NetworkStructureProblem<FltX>(
            DesignProblem(),
            new NetworkNodeId("root"),
            new HashSet<NetworkNodeId> { new("t1"), new("t2") },
            new HashSet<NetworkNodeId> { new("s1") });

        problem.TreeNodes.Should().BeEquivalentTo(
            new[] { new NetworkNodeId("root"), new NetworkNodeId("t1"), new NetworkNodeId("t2"), new NetworkNodeId("s1") });
    }

    /// <summary>中转节点可缺省 / Steiner nodes are optional.</summary>
    [Fact]
    public void NetworkStructureProblem_ShouldAllowNoSteinerNodes() =>
        new NetworkStructureProblem<FltX>(
                DesignProblem(), new NetworkNodeId("root"), new HashSet<NetworkNodeId> { new("t1") })
            .TreeNodes.Should().HaveCount(2);

    /// <summary>分区请求保留可负责设施 / A partition request retains its eligible facilities.</summary>
    [Fact]
    public void FacilityPartitionRequest_ShouldRetainEligibleFacilities() {
        var request = new FacilityPartitionRequest(
            "p1", new HashSet<NetworkNodeId> { new("f1"), new("f2") }, Required: false);

        request.EligibleFacilities.Should().HaveCount(2);
        request.Required.Should().BeFalse();
    }

    /// <summary>分区可负责设施集合是快照 / The eligible-facility set is a snapshot.</summary>
    [Fact]
    public void FacilityPartitionRequest_ShouldSnapshotEligibleFacilities() {
        var eligible = new HashSet<NetworkNodeId> { new("f1") };
        var request = new FacilityPartitionRequest("p1", eligible);

        eligible.Add(new NetworkNodeId("f2"));

        request.EligibleFacilities.Should().HaveCount(1);
    }

    /// <summary>配额可省略上限 / A quota may omit its upper limit.</summary>
    [Fact]
    public void FacilityAssignmentQuota_ShouldAllowOpenMaximum() {
        var quota = new FacilityAssignmentQuota(new NetworkNodeId("f1"), Minimum: 1);

        quota.Maximum.Should().BeNull();
        quota.Minimum.Should().Be(1);
    }

    /// <summary>结构问题保留分区与配额 / The structure problem retains partitions and quotas.</summary>
    [Fact]
    public void NetworkStructureProblem_ShouldRetainPartitionsAndQuotas() {
        var problem = new NetworkStructureProblem<FltX>(
            DesignProblem(),
            new NetworkNodeId("root"),
            new HashSet<NetworkNodeId> { new("t1") },
            partitions: new[] { new FacilityPartitionRequest("p1", new HashSet<NetworkNodeId> { new("f1") }) },
            quotas: new[] { new FacilityAssignmentQuota(new NetworkNodeId("f1"), Minimum: 1, Maximum: 2) });

        problem.Partitions.Should().HaveCount(1);
        problem.Quotas.Should().HaveCount(1);
    }

    /// <summary>构造一个空流问题 / Build an empty flow problem.</summary>
    private static FlowProblem<FltX> FlowProblem() =>
        new(
            NetworkGraph<NetworkNode<FltX>, NetworkArc<FltX>>.Invoke(
                new List<NetworkNode<FltX>>(),
                new List<NetworkArc<FltX>>(),
                new NetworkGraphAccess<NetworkNode<FltX>, NetworkArc<FltX>>(
                    NodeId: node => node.Id,
                    ArcId: arc => arc.Id,
                    From: arc => arc.From,
                    To: arc => arc.To)).Value,
            Array.Empty<Domain.Flow.Model.FlowArc<FltX>>(),
            Array.Empty<CommodityFlow<FltX>>(),
            new FltX(0.0));

    /// <summary>构造一个建设问题 / Build a design problem.</summary>
    private static NetworkDesignProblem<FltX> DesignProblem() => new(FlowProblem());

    /// <summary>以米为单位的量 / A quantity in metres.</summary>
    private static Quantity<FltX> Meters(double value) => new(new FltX(value), SIBaseUnits.Meter);
}
