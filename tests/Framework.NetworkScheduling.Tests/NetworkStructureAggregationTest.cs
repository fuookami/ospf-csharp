// 结构聚合与上下文测试 / Tests for the structure aggregation and context.

using System;
using System.Collections.Generic;
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
/// 结构聚合与上下文 / The structure aggregation and context.
/// </summary>
public sealed class NetworkStructureAggregationTest {
    /// <summary>主路径：根加终端可注册 / The main path registers: a root and one terminal.</summary>
    [Fact]
    public void Register_ShouldSucceedOnMainPath() =>
        Aggregation().Register(Model()).IsOk.Should().BeTrue();

    /// <summary>
    /// **注册同时建立建设变量、根树与覆盖三类行**：三者必须共处一个模型。
    /// **Registration adds construction, rooted-tree and coverage rows together**: all three must live in one model.
    /// </summary>
    [Fact]
    public void Register_ShouldAddRowsFromAllThreeComponents() {
        LinearMetaModel<Flt64> model = Model();
        Aggregation(partitions: true).Register(model);

        model.Constraints.Should().NotBeEmpty();
        model.Constraints.Should().Contain(c => c.Name.Contains("_tree_", StringComparison.Ordinal));
        model.Constraints.Should().Contain(c => c.Name.Contains("_partition_", StringComparison.Ordinal));
    }

    /// <summary>不注册目标时行数减少 / Skipping the objective removes the objective.</summary>
    [Fact]
    public void Register_ShouldAllowSkippingObjective() =>
        Aggregation().Register(Model(), minimizeCost: false).IsOk.Should().BeTrue();

    /// <summary>**同选项重复注册幂等** / Re-registering with the same options is idempotent.</summary>
    [Fact]
    public void Register_ShouldBeIdempotentForSameOptions() {
        LinearMetaModel<Flt64> model = Model();
        NetworkStructureAggregation<FltX> aggregation = Aggregation();

        aggregation.Register(model).IsOk.Should().BeTrue();
        aggregation.Register(model).IsOk.Should().BeTrue();
    }

    /// <summary>**不同目标选项重复注册被拒** / Re-registering with different objective options is rejected.</summary>
    [Fact]
    public void Register_ShouldRejectInconsistentOptions() {
        LinearMetaModel<Flt64> model = Model();
        NetworkStructureAggregation<FltX> aggregation = Aggregation();

        aggregation.Register(model).IsOk.Should().BeTrue();
        aggregation.Register(model, minimizeCost: false).IsOk.Should().BeFalse();
    }

    /// <summary>**不可跨模型复用** / No reuse across models.</summary>
    [Fact]
    public void Register_ShouldRejectSecondModel() {
        NetworkStructureAggregation<FltX> aggregation = Aggregation();

        aggregation.Register(Model()).IsOk.Should().BeTrue();
        aggregation.Register(new LinearMetaModel<Flt64>("other")).IsOk.Should().BeFalse();
    }

    /// <summary>
    /// **流弧与图快照不一致时被拒**：拓扑与声明不符会让树的父子关系建立在错误的邻接上。
    /// **A flow-arc/graph mismatch is rejected**: with topology disagreeing with the declaration the tree's parent relations rest on
    /// the wrong adjacency.
    /// </summary>
    [Fact]
    public void Register_ShouldRejectArcGraphMismatch() =>
        new NetworkStructureAggregation<FltX>(
                StructureProblem(graphArcId: "other"),
                new NetworkDesignContext<FltX>(DesignProblem(graphArcId: "other"), FltXNetworkSchedulingSolverValueAdapter.Instance))
            .Register(Model()).IsOk.Should().BeFalse();

    /// <summary>**根节点不存在时被拒** / A missing root node is rejected.</summary>
    [Fact]
    public void Register_ShouldRejectMissingRoot() =>
        new NetworkStructureAggregation<FltX>(
                StructureProblem(root: "ghost"),
                new NetworkDesignContext<FltX>(DesignProblem(), FltXNetworkSchedulingSolverValueAdapter.Instance))
            .Register(Model()).IsOk.Should().BeFalse();

    /// <summary>**重复分区标识被拒** / Duplicate partition identities are rejected.</summary>
    [Fact]
    public void Register_ShouldRejectDuplicatePartitionIds() {
        FacilityPartitionRequest[] partitions = new[] {
            new FacilityPartitionRequest("p1", new HashSet<NetworkNodeId> { new("n1") }),
            new FacilityPartitionRequest("p1", new HashSet<NetworkNodeId> { new("n1") }),
        };
        new NetworkStructureAggregation<FltX>(
                StructureProblem(partitions: partitions),
                new NetworkDesignContext<FltX>(DesignProblem(), FltXNetworkSchedulingSolverValueAdapter.Instance))
            .Register(Model()).IsOk.Should().BeFalse();
    }

    /// <summary>**配额上限小于下限时被拒** / A quota whose maximum is below its minimum is rejected.</summary>
    [Fact]
    public void Register_ShouldRejectInvalidQuota() {
        FacilityAssignmentQuota[] quotas = new[] { new FacilityAssignmentQuota(new NetworkNodeId("n1"), Minimum: 3, Maximum: 1) };
        new NetworkStructureAggregation<FltX>(
                StructureProblem(quotas: quotas),
                new NetworkDesignContext<FltX>(DesignProblem(), FltXNetworkSchedulingSolverValueAdapter.Instance))
            .Register(Model()).IsOk.Should().BeFalse();
    }

    /// <summary>**分区引用缺失设施时被拒** / A partition referencing a missing facility is rejected.</summary>
    [Fact]
    public void Register_ShouldRejectPartitionWithMissingFacility() {
        FacilityPartitionRequest[] partitions = new[] {
            new FacilityPartitionRequest("p1", new HashSet<NetworkNodeId> { new("ghost") }),
        };
        new NetworkStructureAggregation<FltX>(
                StructureProblem(partitions: partitions),
                new NetworkDesignContext<FltX>(DesignProblem(), FltXNetworkSchedulingSolverValueAdapter.Instance))
            .Register(Model()).IsOk.Should().BeFalse();
    }

    /// <summary>注册后树弧与分区表达式可查 / Tree-arc and partition expressions are queryable after registration.</summary>
    [Fact]
    public void Accessors_ShouldWorkAfterRegistration() {
        NetworkStructureAggregation<FltX> aggregation = Aggregation(partitions: true);
        aggregation.Register(Model());

        aggregation.SelectedTreeArc(new NetworkArcId("a1")).IsOk.Should().BeTrue();
        aggregation.ActiveTreeNode(new NetworkNodeId("n1")).IsOk.Should().BeTrue();
        aggregation.ParentArc(new NetworkNodeId("n1")).IsOk.Should().BeTrue();
        aggregation.PartitionAssignment("p1", new NetworkNodeId("n1")).IsOk.Should().BeTrue();
    }

    /// <summary>未注册时访问器失败 / The accessors fail before registration.</summary>
    [Fact]
    public void Accessors_ShouldFailBeforeRegistration() {
        NetworkStructureAggregation<FltX> aggregation = Aggregation();

        aggregation.SelectedTreeArc(new NetworkArcId("a1")).IsOk.Should().BeFalse();
        aggregation.ActiveTreeNode(new NetworkNodeId("n1")).IsOk.Should().BeFalse();
        aggregation.ParentArc(new NetworkNodeId("n1")).IsOk.Should().BeFalse();
    }

    /// <summary>未注册模型提取解失败 / Extracting from an unregistered model fails.</summary>
    [Fact]
    public void ExtractSolution_ShouldRejectUnregisteredModel() =>
        Aggregation().ExtractSolution(Model()).IsOk.Should().BeFalse();

    /// <summary>**注册后未求解时提取失败** / Extraction fails before the model is solved.</summary>
    [Fact]
    public void ExtractSolution_ShouldRejectUnsolvedModel() {
        LinearMetaModel<Flt64> model = Model();
        NetworkStructureAggregation<FltX> aggregation = Aggregation();
        aggregation.Register(model);

        aggregation.ExtractSolution(model).IsOk.Should().BeFalse();
    }

    /// <summary>上下文委托到聚合 / The context delegates to the aggregation.</summary>
    [Fact]
    public void Context_ShouldDelegate() {
        var context = new NetworkStructureContext<FltX>(
            StructureProblem(), FltXNetworkSchedulingSolverValueAdapter.Instance);
        LinearMetaModel<Flt64> model = Model();

        context.Register(model).IsOk.Should().BeTrue();
        context.Aggregation.ParentArc(new NetworkNodeId("n1")).IsOk.Should().BeTrue();
        context.ConstructionCost.Should().NotBeNull();
    }

    /// <summary>构造一个结构聚合 / Build a structure aggregation.</summary>
    private static NetworkStructureAggregation<FltX> Aggregation(bool partitions = false) =>
        new(
            StructureProblem(partitions: partitions ? DefaultPartitions() : null),
            new NetworkDesignContext<FltX>(DesignProblem(), FltXNetworkSchedulingSolverValueAdapter.Instance));

    /// <summary>一个引用 n1 的分区 / One partition eligible at n1.</summary>
    private static FacilityPartitionRequest[] DefaultPartitions() =>
        new[] { new FacilityPartitionRequest("p1", new HashSet<NetworkNodeId> { new("n1") }) };

    /// <summary>构造模型 / Build a model.</summary>
    private static LinearMetaModel<Flt64> Model() => new("structure");

    /// <summary>构造结构问题 / Build a structure problem.</summary>
    private static NetworkStructureProblem<FltX> StructureProblem(
        string root = "n0",
        IReadOnlyList<FacilityPartitionRequest>? partitions = null,
        IReadOnlyList<FacilityAssignmentQuota>? quotas = null,
        string graphArcId = "a1") =>
        new(
            DesignProblem(graphArcId),
            new NetworkNodeId(root),
            new HashSet<NetworkNodeId> { new("n1") },
            partitions: partitions,
            quotas: quotas);

    /// <summary>构造建设问题 / Build a design problem.</summary>
    private static NetworkDesignProblem<FltX> DesignProblem(string graphArcId = "a1") {
        // 流弧固定为 a1；图弧使用 graphArcId，二者不同即构成"流弧与图快照不一致"。
        // The flow arc is always a1; the graph arc uses graphArcId, so differing values form an arc/graph mismatch.
        NetworkArc<FltX> flowNetworkArc = NetworkArc<FltX>.Invoke(
            new NetworkNodeId("n0"), new NetworkNodeId("n1"),
            Cost(2.0), null, new NetworkArcId("a1")).Value;
        Domain.Flow.Model.FlowArc<FltX> arc = Domain.Flow.Model.FlowArc<FltX>.Invoke(
            flowNetworkArc, CapacityBounds<FltX>.Invoke(Meters(0.0), Meters(10.0)).Value).Value;
        NetworkArc<FltX> graphArc = NetworkArc<FltX>.Invoke(
            new NetworkNodeId("n0"), new NetworkNodeId("n1"),
            Cost(2.0), null, new NetworkArcId(graphArcId)).Value;

        NetworkNode<FltX>[] nodes = {
            NetworkNode<FltX>.Invoke(new NetworkNodeId("n0"), new Dictionary<string, Quantity<FltX>>()).Value,
            NetworkNode<FltX>.Invoke(new NetworkNodeId("n1"), new Dictionary<string, Quantity<FltX>>()).Value,
        };
        NetworkGraph<NetworkNode<FltX>, NetworkArc<FltX>> graph =
            NetworkGraph<NetworkNode<FltX>, NetworkArc<FltX>>.Invoke(
                nodes, new[] { graphArc },
                new NetworkGraphAccess<NetworkNode<FltX>, NetworkArc<FltX>>(
                    NodeId: node => node.Id,
                    ArcId: candidate => candidate.Id,
                    From: candidate => candidate.From,
                    To: candidate => candidate.To)).Value;

        return new NetworkDesignProblem<FltX>(
            new FlowProblem<FltX>(
                graph, new[] { arc }, Array.Empty<CommodityFlow<FltX>>(), new FltX(0.0),
                costUnit: NoneUnit.Instance),
            Array.Empty<NetworkService<FltX>>(),
            new[] { new NetworkFacility<FltX>(new NetworkNodeId("n1"), Cost(5.0)) });
    }

    /// <summary>以米为单位的量 / A quantity in metres.</summary>
    private static Quantity<FltX> Meters(double value) => new(new FltX(value), SIBaseUnits.Meter);

    /// <summary>无量纲费用 / A dimensionless cost.</summary>
    private static Quantity<FltX> Cost(double value) => new(new FltX(value), NoneUnit.Instance);
}
