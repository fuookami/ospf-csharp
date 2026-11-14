// 资源弧、资源图与分支遮罩测试 / Tests for resource arcs, resource graphs and branch masks.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Infrastructure;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Xunit;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Tests;

/// <summary>
/// 资源弧、资源图与分支遮罩 / Resource arcs, resource graphs and branch masks.
/// </summary>
public sealed class ResourceGraphTest {
    private static readonly NetworkNodeId Start = new("depot-start");
    private static readonly NetworkNodeId End = new("depot-end");
    private static readonly NetworkNodeId A = new("customer-a");
    private static readonly NetworkNodeId B = new("customer-b");

    /// <summary>
    /// **默认标识带长度前缀**：`("ab","c")` 与 `("a","bc")` 不该撞成同一个 `"abc"`。
    /// **The default identity carries length prefixes**: `("ab","c")` and `("a","bc")` must not collide on `"abc"`.
    /// </summary>
    [Fact]
    public void DefaultId_ShouldDisambiguate() {
        NetworkArcId first = ResourceArcs.DefaultId(new NetworkNodeId("ab"), new NetworkNodeId("c"));
        NetworkArcId second = ResourceArcs.DefaultId(new NetworkNodeId("a"), new NetworkNodeId("bc"));

        first.Should().NotBe(second);
        first.Value.Should().Be("2:ab1:c");
    }

    /// <summary>资源弧结构相等 / Resource arcs compare structurally.</summary>
    [Fact]
    public void ResourceArc_ShouldCompareStructurally() {
        var left = new ResourceArc<string>("truck", A, B);
        var right = new ResourceArc<string>("truck", A, B);

        left.Should().Be(right);
        new HashSet<ResourceArc<string>> { left, right }.Should().HaveCount(1);
    }

    /// <summary>资源键不同的弧不相等 / Arcs with different resource keys differ.</summary>
    [Fact]
    public void ResourceArc_ShouldDifferByResource() =>
        new ResourceArc<string>("truck", A, B)
            .Should().NotBe(new ResourceArc<string>("van", A, B));

    /// <summary>合法资源图构造成功 / A valid resource graph constructs.</summary>
    [Fact]
    public void Graph_ShouldBuild() =>
        GraphOf(
            new[] { Start, A },
            new[] { new ResourceArc<string>("truck", Start, A) }).IsOk.Should().BeTrue();

    /// <summary>
    /// **不同资源可用同一弧标识**：不同车辆类型走同一条物理路是完全正常的，强行全局唯一会让资源维度失去意义。
    /// **Different resources may share one arc identity**: different vehicle types using the same physical road is normal, and
    /// forcing global uniqueness would defeat the resource dimension.
    /// </summary>
    [Fact]
    public void Graph_ShouldAllowSameIdAcrossResources() =>
        GraphOf(
            new[] { Start, A },
            new[]
            {
                new ResourceArc<string>("truck", Start, A, new NetworkArcId("x1")),
                new ResourceArc<string>("van", Start, A, new NetworkArcId("x1")),
            }).IsOk.Should().BeTrue();

    /// <summary>同一资源内重复标识被拒绝 / A duplicate identity within one resource is rejected.</summary>
    [Fact]
    public void Graph_ShouldRejectDuplicateIdWithinResource() =>
        GraphOf(
            new[] { Start, A },
            new[]
            {
                new ResourceArc<string>("truck", Start, A, new NetworkArcId("x1")),
                new ResourceArc<string>("truck", Start, A, new NetworkArcId("x1")),
            }).IsOk.Should().BeFalse();

    /// <summary>重复节点标识被拒绝 / Duplicate node identities are rejected.</summary>
    [Fact]
    public void Graph_ShouldRejectDuplicateNode() =>
        GraphOf(new[] { Start, Start }, Array.Empty<ResourceArc<string>>()).IsOk.Should().BeFalse();

    /// <summary>弧引用缺失节点被拒绝 / An arc referencing a missing node is rejected.</summary>
    [Fact]
    public void Graph_ShouldRejectMissingNode() =>
        GraphOf(
            new[] { Start },
            new[] { new ResourceArc<string>("truck", Start, A) }).IsOk.Should().BeFalse();

    /// <summary>出弧按资源与起点分组 / Outgoing arcs group by resource and origin.</summary>
    [Fact]
    public void Graph_ShouldGroupOutgoing() {
        ResourceGraph<string> graph = GraphOf(
            new[] { Start, A, B },
            new[]
            {
                new ResourceArc<string>("truck", Start, A),
                new ResourceArc<string>("truck", Start, B),
                new ResourceArc<string>("van", Start, A),
            }).Value;

        graph.Outgoing("truck", Start).Should().HaveCount(2);
        graph.Outgoing("van", Start).Should().ContainSingle();
        graph.Outgoing("truck", A).Should().BeEmpty();
    }

    /// <summary>平行弧可全部取出 / Parallel arcs are all retrievable.</summary>
    [Fact]
    public void Graph_ShouldReturnParallelArcs() {
        ResourceGraph<string> graph = GraphOf(
            new[] { Start, A },
            new[]
            {
                new ResourceArc<string>("truck", Start, A, new NetworkArcId("x1")),
                new ResourceArc<string>("truck", Start, A, new NetworkArcId("x2")),
            }).Value;

        graph.Between("truck", Start, A).Select(arc => arc.Id.Value)
            .Should().BeEquivalentTo(new[] { "x1", "x2" });
    }

    /// <summary>合法遮罩构造成功 / A valid mask constructs.</summary>
    [Fact]
    public void Mask_ShouldBuild() =>
        BranchMask<string>.Invoke(Start, End).IsOk.Should().BeTrue();

    /// <summary>起始仓库标识在遮罩中保留 / Depot identities are kept on the mask.</summary>
    [Fact]
    public void Mask_ShouldKeepDepots() {
        BranchMask<string> mask = BranchMask<string>.Invoke(Start, End).Value;

        mask.StartDepot.Should().Be(Start);
        mask.EndDepot.Should().Be(End);
    }

    /// <summary>起止仓库相同被拒绝 / Identical depots are rejected.</summary>
    [Fact]
    public void Mask_ShouldRejectSameDepot() =>
        BranchMask<string>.Invoke(Start, Start).IsOk.Should().BeFalse();

    /// <summary>
    /// **两个仓库永远可访问**，即使被显式列为禁访：否则车辆无路可走，整棵子树会因"不可行"被剪，而真实原因是自相矛盾的
    /// 分支。
    /// **Both depots are always visitable** even when explicitly forbidden: otherwise no route exists and the subtree is pruned
    /// as "infeasible" when the real cause is a self-contradictory branch.
    /// </summary>
    [Fact]
    public void Mask_ShouldAlwaysAllowDepots() {
        BranchMask<string> mask = BranchMask<string>.Invoke(
            Start, End,
            forbiddenNodes: new Dictionary<string, IReadOnlySet<NetworkNodeId>>
            {
                ["truck"] = new HashSet<NetworkNodeId> { Start, End, A },
            }).Value;

        mask.AllowsNode("truck", Start).Should().BeTrue("起始仓库永远可达 / the start depot is always reachable");
        mask.AllowsNode("truck", End).Should().BeTrue("结束仓库永远可达 / the end depot is always reachable");
        mask.AllowsNode("truck", A).Should().BeFalse();
    }

    /// <summary>禁访规则生效 / The forbidden-node rule takes effect.</summary>
    [Fact]
    public void Mask_ShouldForbidNodes() {
        BranchMask<string> mask = BranchMask<string>.Invoke(
            Start, End,
            forbiddenNodes: new Dictionary<string, IReadOnlySet<NetworkNodeId>>
            {
                ["van"] = new HashSet<NetworkNodeId> { A },
            }).Value;

        mask.AllowsNode("van", A).Should().BeFalse();
        mask.AllowsNode("truck", A).Should().BeTrue("其他资源不受影响 / other resources are unaffected");
    }

    /// <summary>必选资源限制其他资源访问 / A required resource blocks other resources.</summary>
    [Fact]
    public void Mask_ShouldEnforceRequiredResource() {
        BranchMask<string> mask = BranchMask<string>.Invoke(
            Start, End,
            requiredResources: new Dictionary<NetworkNodeId, string> { [A] = "truck" }).Value;

        mask.AllowsNode("truck", A).Should().BeTrue();
        mask.AllowsNode("van", A).Should().BeFalse();
    }

    /// <summary>禁弧被拒绝 / A forbidden arc is rejected.</summary>
    [Fact]
    public void Mask_ShouldForbidArc() {
        var forbidden = new ResourceArc<string>("truck", Start, A);
        BranchMask<string> mask = BranchMask<string>.Invoke(
            Start, End,
            forbiddenArcs: new HashSet<ResourceArc<string>> { forbidden }).Value;

        mask.AllowsArc("truck", Start, A).Should().BeFalse();
        mask.AllowsArc("truck", A, B).Should().BeTrue();
    }

    /// <summary>
    /// **必选弧带来排他约束**：某客户有必选出弧时，从它出发的其它弧一律禁用。少了这条，必选弧只是"鼓励"而非"要求"。
    /// **A required arc implies exclusion**: with a required outgoing arc at a customer, every other arc leaving it is banned.
    /// Without this the arc is merely preferred rather than required.
    /// </summary>
    [Fact]
    public void Mask_ShouldExcludeAlternativesAtRequiredCustomer() {
        var required = new ResourceArc<string>("truck", A, B);
        BranchMask<string> mask = BranchMask<string>.Invoke(
            Start, End,
            requiredArcs: new HashSet<ResourceArc<string>> { required }).Value;

        mask.AllowsArc("truck", A, B).Should().BeTrue("必选弧本身可用 / the required arc itself is allowed");
        mask.AllowsArc("truck", A, End).Should().BeFalse("从 A 出发的其他弧被排除 / other arcs from A are excluded");
    }

    /// <summary>
    /// **必选弧被翻译成必选车辆类型**：否则弧级与车辆类型级分支各自独立，会产生"这客户必须走那辆车，那辆车又不许访问它"。
    /// **A required arc is translated into a required vehicle type**: otherwise arc-level and vehicle-type-level branches act
    /// independently and produce "this customer must use that vehicle, but that vehicle may not visit it".
    /// </summary>
    [Fact]
    public void Mask_ShouldTranslateRequiredArcToRequiredResource() {
        var required = new ResourceArc<string>("truck", A, B);
        BranchMask<string> mask = BranchMask<string>.Invoke(
            Start, End,
            requiredArcs: new HashSet<ResourceArc<string>> { required }).Value;

        mask.RequiredResources[A].Should().Be("truck");
        mask.RequiredResources[B].Should().Be("truck");
    }

    /// <summary>同客户的必选出弧冲突被拒绝 / Conflicting required outgoing arcs at one customer are rejected.</summary>
    [Fact]
    public void Mask_ShouldRejectConflictingOutgoing() =>
        BranchMask<string>.Invoke(
            Start, End,
            requiredArcs: new HashSet<ResourceArc<string>>
            {
                new("truck", A, B),
                new("truck", A, End),
            }).IsOk.Should().BeFalse();

    /// <summary>同客户的必选入弧冲突被拒绝 / Conflicting required incoming arcs at one customer are rejected.</summary>
    [Fact]
    public void Mask_ShouldRejectConflictingIncoming() =>
        BranchMask<string>.Invoke(
            Start, End,
            requiredArcs: new HashSet<ResourceArc<string>>
            {
                new("truck", Start, B),
                new("truck", A, B),
            }).IsOk.Should().BeFalse();

    /// <summary>同一弧既禁又选被拒绝 / An arc both forbidden and required is rejected.</summary>
    [Fact]
    public void Mask_ShouldRejectForbiddenAndRequired() =>
        BranchMask<string>.Invoke(
            Start, End,
            forbiddenArcs: new HashSet<ResourceArc<string>> { new("truck", A, B) },
            requiredArcs: new HashSet<ResourceArc<string>> { new("truck", A, B) }).IsOk.Should().BeFalse();

    /// <summary>必选车辆类型同时被禁访时报冲突 / A required vehicle type that is also forbidden is a conflict.</summary>
    [Fact]
    public void Mask_ShouldRejectForbiddenRequiredResource() =>
        BranchMask<string>.Invoke(
            Start, End,
            forbiddenNodes: new Dictionary<string, IReadOnlySet<NetworkNodeId>>
            {
                ["truck"] = new HashSet<NetworkNodeId> { A },
            },
            requiredResources: new Dictionary<NetworkNodeId, string> { [A] = "truck" }).IsOk.Should().BeFalse();

    /// <summary>起止仓库正确的完整路径兼容 / A complete path with correct depots is compatible.</summary>
    [Fact]
    public void Mask_ShouldAcceptCompatibleRoute() {
        BranchMask<string> mask = BranchMask<string>.Invoke(Start, End).Value;

        mask.IsRouteCompatible("truck", new[] { Start, A, B, End }).Should().BeTrue();
    }

    /// <summary>起点不是起始仓库的路径不兼容 / A path not starting at the start depot is incompatible.</summary>
    [Fact]
    public void Mask_ShouldRejectWrongStart() {
        BranchMask<string> mask = BranchMask<string>.Invoke(Start, End).Value;

        mask.IsRouteCompatible("truck", new[] { A, B, End }).Should().BeFalse();
    }

    /// <summary>少于两个节点的路径不兼容 / A path with fewer than two nodes is incompatible.</summary>
    [Fact]
    public void Mask_ShouldRejectShortPath() {
        BranchMask<string> mask = BranchMask<string>.Invoke(Start, End).Value;

        mask.IsRouteCompatible("truck", new[] { Start }).Should().BeFalse();
    }

    /// <summary>
    /// **弧标识数量必须等于路径段数**：数量不符说明路径与弧序列不是同一条路线，补默认标识会让平行弧分支失效。
    /// **The arc-identity count must equal the segment count**: a mismatch means they are not the same route, and padding with
    /// defaults would void parallel-arc branching.
    /// </summary>
    [Fact]
    public void Mask_ShouldRejectArcIdCountMismatch() {
        BranchMask<string> mask = BranchMask<string>.Invoke(Start, End).Value;

        mask.IsRouteCompatible(
            "truck",
            new[] { Start, A, B, End },
            new[] { ResourceArcs.DefaultId(Start, A) }).Should().BeFalse();
    }

    /// <summary>显式平行弧路线可按标识判定 / An explicit parallel-arc route is judged by identity.</summary>
    [Fact]
    public void Mask_ShouldJudgeByExplicitArcId() {
        var forbidden = new ResourceArc<string>("truck", A, B, new NetworkArcId("alt"));
        BranchMask<string> mask = BranchMask<string>.Invoke(
            Start, End,
            forbiddenArcs: new HashSet<ResourceArc<string>> { forbidden }).Value;

        NetworkNodeId[] path = new[] { Start, A, B, End };
        var allowedIds = new List<NetworkArcId>
        {
            ResourceArcs.DefaultId(Start, A),
            ResourceArcs.DefaultId(A, B),
            ResourceArcs.DefaultId(B, End),
        };
        var forbiddenIds = new List<NetworkArcId>
        {
            ResourceArcs.DefaultId(Start, A),
            new NetworkArcId("alt"),
            ResourceArcs.DefaultId(B, End),
        };

        mask.IsRouteCompatible("truck", path, allowedIds).Should().BeTrue();
        mask.IsRouteCompatible("truck", path, forbiddenIds).Should().BeFalse();
    }

    /// <summary>连续的资源弧序列兼容 / A contiguous resource-arc sequence is compatible.</summary>
    [Fact]
    public void Mask_ShouldAcceptArcSequence() {
        BranchMask<string> mask = BranchMask<string>.Invoke(Start, End).Value;
        ResourceArc<string>[] arcs = new[]
        {
            new ResourceArc<string>("truck", Start, A),
            new ResourceArc<string>("truck", A, B),
            new ResourceArc<string>("truck", B, End),
        };

        mask.IsResourceArcSequenceCompatible("truck", arcs).Should().BeTrue();
    }

    /// <summary>不连续的资源弧序列不兼容 / A non-contiguous arc sequence is incompatible.</summary>
    [Fact]
    public void Mask_ShouldRejectBrokenSequence() {
        BranchMask<string> mask = BranchMask<string>.Invoke(Start, End).Value;
        ResourceArc<string>[] arcs = new[]
        {
            new ResourceArc<string>("truck", Start, A),
            new ResourceArc<string>("truck", B, End),
        };

        mask.IsResourceArcSequenceCompatible("truck", arcs).Should().BeFalse();
    }

    /// <summary>资源键不一致的序列不兼容 / A sequence with a foreign resource key is incompatible.</summary>
    [Fact]
    public void Mask_ShouldRejectForeignResourceSequence() {
        BranchMask<string> mask = BranchMask<string>.Invoke(Start, End).Value;
        ResourceArc<string>[] arcs = new[]
        {
            new ResourceArc<string>("truck", Start, A),
            new ResourceArc<string>("van", A, End),
        };

        mask.IsResourceArcSequenceCompatible("truck", arcs).Should().BeFalse();
    }

    /// <summary>构造资源图 / Build a resource graph.</summary>
    private static Fuookami.Ospf.Utils.Functional.Result<
        ResourceGraph<string>, Fuookami.Ospf.Utils.Error.ErrorCode,
        Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>> GraphOf(
        IReadOnlyList<NetworkNodeId> nodes,
        IReadOnlyList<ResourceArc<string>> arcs) =>
        ResourceGraph<string>.Invoke(nodes, arcs);
}
