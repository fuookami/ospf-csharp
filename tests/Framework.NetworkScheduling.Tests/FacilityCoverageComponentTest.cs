// 设施覆盖组件测试 / Tests for the facility coverage component.

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
/// 设施覆盖组件 / The facility coverage component.
/// </summary>
public sealed class FacilityCoverageComponentTest {
    /// <summary>主路径：一个分区、一个合格设施可注册 / The main path registers: one partition, one eligible facility.</summary>
    [Fact]
    public void Register_ShouldSucceedOnMainPath() =>
        Component().Register(Model()).IsOk.Should().BeTrue();

    /// <summary>
    /// **每个归属注册一条"归属 ≤ 设施启用"联动行**：未开设的设施把归属钉死在零。
    /// **Each assignment registers a "assignment ≤ activation" row**: an unopened facility pins its assignments to zero.
    /// </summary>
    [Fact]
    public void Register_ShouldLinkAssignmentToActivation() {
        LinearMetaModel<Flt64> model = Model();
        Component().Register(model);

        model.Constraints.Count(constraint => constraint.Name.Contains("_open_", StringComparison.Ordinal))
            .Should().Be(1);
    }

    /// <summary>必选分区额外注册一条覆盖等式 / A required partition adds one coverage row.</summary>
    [Fact]
    public void Register_ShouldAddCoverageRowForRequiredPartition() {
        LinearMetaModel<Flt64> model = Model();
        Component().Register(model);

        model.Constraints.Should().HaveCount(2, "1 联动 + 1 覆盖 / 1 linking + 1 coverage");
    }

    /// <summary>可选分区同样注册覆盖行（不等式）/ An optional partition still registers a coverage row.</summary>
    [Fact]
    public void Register_ShouldAddCoverageRowForOptionalPartition() {
        LinearMetaModel<Flt64> model = Model();
        Component().Register(model);

        model.Constraints.Should().HaveCount(2);
    }

    /// <summary>两个合格设施时联动与覆盖行成对增长 / Two eligible facilities double the linking rows.</summary>
    [Fact]
    public void Register_ShouldScaleWithEligibleFacilities() {
        LinearMetaModel<Flt64> model = Model();
        Component(facilities: 2).Register(model);

        model.Constraints.Count(constraint => constraint.Name.Contains("_open_", StringComparison.Ordinal))
            .Should().Be(2);
    }

    /// <summary>配额为每个设施注册一条下界行 / A quota registers one lower row per facility.</summary>
    [Fact]
    public void Register_ShouldRegisterQuota() {
        LinearMetaModel<Flt64> model = Model();
        Component(quota: true).Register(model);

        model.Constraints.Count(constraint => constraint.Name.Contains("_quota", StringComparison.Ordinal))
            .Should().Be(1);
    }

    /// <summary>注册后归属表达式可查 / The assignment expression is queryable after registration.</summary>
    [Fact]
    public void Assignment_ShouldBeQueryableAfterRegistration() {
        FacilityCoverageComponent<FltX> component = Component();
        component.Register(Model());

        component.Assignment("p1", new NetworkNodeId("n1")).IsOk.Should().BeTrue();
    }

    /// <summary>**不合格组合被拒**：未在合格列表里的设施不能查询归属 / An ineligible pair is rejected.</summary>
    [Fact]
    public void Assignment_ShouldRejectIneligiblePair() {
        FacilityCoverageComponent<FltX> component = Component();
        component.Register(Model());

        component.Assignment("p1", new NetworkNodeId("ghost")).IsOk.Should().BeFalse();
    }

    /// <summary>未注册时查询归属失败 / Querying assignments before registration fails.</summary>
    [Fact]
    public void Assignment_ShouldRejectBeforeRegistration() =>
        Component().Assignment("p1", new NetworkNodeId("n1")).IsOk.Should().BeFalse();

    /// <summary>**不可复用** / The component cannot be reused.</summary>
    [Fact]
    public void Register_ShouldRejectReuse() {
        FacilityCoverageComponent<FltX> component = Component();
        component.Register(Model());

        component.Register(Model()).IsOk.Should().BeFalse();
    }

    /// <summary>未注册模型提取解失败 / Extracting from an unregistered model fails.</summary>
    [Fact]
    public void ExtractSolution_ShouldRejectUnregisteredModel() =>
        Component().ExtractSolution(Model()).IsOk.Should().BeFalse();

    /// <summary>注册后未求解时提取解失败 / Extraction fails before the model is solved.</summary>
    [Fact]
    public void ExtractSolution_ShouldRejectUnsolvedModel() {
        LinearMetaModel<Flt64> model = Model();
        FacilityCoverageComponent<FltX> component = Component();
        component.Register(model);

        component.ExtractSolution(model).IsOk.Should().BeFalse();
    }

    /// <summary>构造一个设施覆盖组件 / Build a facility coverage component.</summary>
    private static FacilityCoverageComponent<FltX> Component(
        int facilities = 1,
        bool required = true,
        bool quota = false) =>
        new(
            StructureProblem(facilities, required, quota),
            new NetworkDesignContext<FltX>(DesignProblem(), FltXNetworkSchedulingSolverValueAdapter.Instance),
            "structure");

    /// <summary>构造模型 / Build a model.</summary>
    private static LinearMetaModel<Flt64> Model() => new("structure");

    /// <summary>构造结构问题 / Build a structure problem.</summary>
    private static NetworkStructureProblem<FltX> StructureProblem(
        int facilities,
        bool required,
        bool quota) {
        var eligible = new HashSet<NetworkNodeId>();
        for (int index = 0; index < facilities; index++) {
            eligible.Add(new NetworkNodeId($"n{index + 1}"));
        }
        FacilityPartitionRequest[] partitions = new[] { new FacilityPartitionRequest("p1", eligible, required) };
        FacilityAssignmentQuota[]? quotas = quota
            ? new[] { new FacilityAssignmentQuota(new NetworkNodeId("n1"), Minimum: 1) }
            : null;

        return new NetworkStructureProblem<FltX>(
            DesignProblem(),
            new NetworkNodeId("n0"),
            new HashSet<NetworkNodeId> { new("n1") },
            partitions: partitions,
            quotas: quotas);
    }

    /// <summary>构造建设问题 / Build a design problem.</summary>
    private static NetworkDesignProblem<FltX> DesignProblem() {
        NetworkFacility<FltX>[] facilities = new[] {
            new NetworkFacility<FltX>(new NetworkNodeId("n1"), Cost(5.0)),
            new NetworkFacility<FltX>(new NetworkNodeId("n2"), Cost(6.0)),
        };

        NetworkArc<FltX> networkArc = NetworkArc<FltX>.Invoke(
            new NetworkNodeId("n0"), new NetworkNodeId("n1"),
            Cost(2.0), null, new NetworkArcId("a1")).Value;
        Domain.Flow.Model.FlowArc<FltX> arc = Domain.Flow.Model.FlowArc<FltX>.Invoke(
            networkArc, CapacityBounds<FltX>.Invoke(Meters(0.0), Meters(10.0)).Value).Value;

        NetworkNode<FltX>[] nodes = {
            NetworkNode<FltX>.Invoke(new NetworkNodeId("n0"), new Dictionary<string, Quantity<FltX>>()).Value,
            NetworkNode<FltX>.Invoke(new NetworkNodeId("n1"), new Dictionary<string, Quantity<FltX>>()).Value,
            NetworkNode<FltX>.Invoke(new NetworkNodeId("n2"), new Dictionary<string, Quantity<FltX>>()).Value,
        };
        NetworkGraph<NetworkNode<FltX>, NetworkArc<FltX>> graph =
            NetworkGraph<NetworkNode<FltX>, NetworkArc<FltX>>.Invoke(
                nodes, new[] { networkArc },
                new NetworkGraphAccess<NetworkNode<FltX>, NetworkArc<FltX>>(
                    NodeId: node => node.Id,
                    ArcId: candidate => candidate.Id,
                    From: candidate => candidate.From,
                    To: candidate => candidate.To)).Value;

        var commodity = new CommodityFlow<FltX>(
            Domain.Flow.Model.FlowCommodity<FltX>.Invoke("c1").Value, SIBaseUnits.Meter);

        return new NetworkDesignProblem<FltX>(
            new FlowProblem<FltX>(
                graph, new[] { arc }, new[] { commodity }, new FltX(0.0), costUnit: NoneUnit.Instance),
            Array.Empty<NetworkService<FltX>>(),
            facilities);
    }

    /// <summary>以米为单位的量 / A quantity in metres.</summary>
    private static Quantity<FltX> Meters(double value) => new(new FltX(value), SIBaseUnits.Meter);

    /// <summary>无量纲费用 / A dimensionless cost.</summary>
    private static Quantity<FltX> Cost(double value) => new(new FltX(value), NoneUnit.Instance);
}
