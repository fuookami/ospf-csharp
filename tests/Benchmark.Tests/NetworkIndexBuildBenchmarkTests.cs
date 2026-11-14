// 网络索引基准夹具测试 / Tests for the network-index benchmark fixtures.

using System.Linq;
using FluentAssertions;
using System.Collections.Generic;
using Fuookami.Ospf.Benchmark;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Xunit;

namespace Fuookami.Ospf.Benchmark.Tests;

/// <summary>
/// 网络索引基准夹具 / The network-index benchmark fixtures.
/// </summary>
public sealed class NetworkIndexBuildBenchmarkTests {
    /// <summary>**弧数是节点数的四倍** / The arc count is four times the node count.</summary>
    /// <remarks>
    /// 四倍是因为每个节点出四条弧。**少一条都会让冷构建基准测的是另一张图**，
    /// 而"图能不能建出来"在弧数不对时依然成立。
    ///
    /// Four times because every node has four outgoing arcs. **One short would make the cold-construction benchmark measure a different graph**, and
    /// "the graph builds" stays true even with the wrong arc count.
    /// </remarks>
    [Fact]
    public void Setup_ShouldProduceFourArcsPerNode() {
        var benchmark = new NetworkIndexBuildBenchmark { Size = 64 };

        benchmark.Setup();

        benchmark.Nodes.Should().HaveCount(64);
        benchmark.Arcs.Should().HaveCount(256);
        benchmark.Arcs.Select(arc => arc.Id.Value).Should().OnlyHaveUniqueItems();
    }

    /// <summary>**每个节点恰好四条出弧，且目标各不相同** / Every node has exactly four outgoing arcs, to four distinct targets.</summary>
    /// <remarks>
    /// **只数条数是不够的**：把目标全写成同一个节点后条数依然是 4，于是"四条出弧"的断言照样通过，
    /// **而那张图的出度分布已经不成立了**。必须断言目标集合。
    ///
    /// **Counting is not enough**: collapsing every target onto one node keeps the count at 4, so the "four outgoing arcs" assertion still passes while
    /// **the out-degree distribution has already stopped holding**. The target set is what must be asserted.
    /// </remarks>
    [Fact]
    public void Setup_ShouldGiveEveryNodeFourOutgoingArcs() {
        var benchmark = new NetworkIndexBuildBenchmark { Size = 16 };

        benchmark.Setup();
        NetworkGraph<NetworkNodeId, NetworkIndexBuildBenchmark.BenchmarkArc> graph = benchmark.Create();

        foreach (NetworkNodeId node in benchmark.Nodes) {
            var targets = graph.Outgoing(node).Select(arc => arc.To.Value).ToList();
            targets.Should().HaveCount(4);
            targets.Should().OnlyHaveUniqueItems();
        }
        graph.Outgoing(benchmark.Nodes[0]).Select(arc => arc.To.Value)
            .Should().Equal("n1", "n2", "n3", "n4");
    }

    /// <summary>**图包含自环与回绕弧** / The graph contains self-loops and wraparound arcs.</summary>
    /// <remarks>
    /// `(from + step) % size` 在 `size <= 4` 时会产生自环。**自环是冷构建最容易漏掉的一类弧**——
    /// 建索引时按 `from` 分桶、按 `to` 归组，自环会同时落进两侧，去重写得含糊就会丢。
    ///
    /// `(from + step) % size` yields self-loops once `size <= 4`. **Self-loops are the arc kind most easily dropped by cold construction** — indexing
    /// buckets by `from` and groups by `to` puts a self-loop on both sides, and a vague deduplication loses it.
    /// </remarks>
    [Fact]
    public void Setup_ShouldKeepSelfLoopsAndWraparoundArcs() {
        var benchmark = new NetworkIndexBuildBenchmark { Size = 4 };

        benchmark.Setup();
        NetworkGraph<NetworkNodeId, NetworkIndexBuildBenchmark.BenchmarkArc> graph = benchmark.Create();

        benchmark.Arcs.Should().Contain(arc => arc.From == arc.To);
        benchmark.Arcs.Should().Contain(arc => arc.From.Value == "n3" && arc.To.Value == "n0");
        graph.Outgoing(benchmark.Nodes[0]).Should().HaveCount(4);
    }

    /// <summary>**反向视图只翻转方向，不改写载荷与标识** / The reverse view flips direction only, rewriting neither payloads nor identities.</summary>
    /// <remarks>
    /// 反向图若重写弧标识，"反向"就变成了"另一张图"，而**下游按标识做去重与缓存时会悄悄失去共享**。
    ///
    /// If the reverse rewrote arc identities, "reverse" would become "a different graph", and **downstream deduplication and caching keyed by identity
    /// would silently lose its sharing**.
    /// </remarks>
    [Fact]
    public void Reverse_ShouldKeepArcIdentities() {
        var benchmark = new NetworkIndexBuildBenchmark { Size = 8 };

        benchmark.Setup();
        NetworkGraph<NetworkNodeId, NetworkIndexBuildBenchmark.BenchmarkArc> graph = benchmark.Create();
        NetworkGraph<NetworkNodeId, NetworkIndexBuildBenchmark.BenchmarkArc> reversed = graph.Reverse();

        // **断言的是"反向仍用原来的标识"，不是"两侧互相相等"**：两侧共用同一个 `ArcId` 访问器，
        // 而**访问器一改，两侧就一起改、相等依然成立**。真正的参照是夹具里那份原始弧表。
        // **The assertion is "the reverse still uses the original identities", not "both sides agree"**: both sides share one `ArcId` accessor, and
        // **changing the accessor changes both sides together, so equality still holds**. The real reference is the fixture's own arc table.
        var expectedIncoming = benchmark.Arcs
            .Where(arc => arc.To == benchmark.Nodes[1])
            .Select(arc => arc.Id.Value)
            .ToList();
        reversed.Outgoing(benchmark.Nodes[1]).Select(arc => arc.Id.Value)
            .Should().BeEquivalentTo(expectedIncoming);
        // **断言图自己的标识表，而不是载荷里的字段**：`Outgoing` 返回的是弧载荷，载荷自带的 `Id` 与图的
        // 标识表是两处独立的数据，**访问器写错时只有标识表会变，载荷不会**。
        // **Assert the graph's own identity table, not a field inside the payload**: `Outgoing` returns arc payloads, whose `Id` is independent of the
        // graph's identity table, and **a wrong accessor moves only the table, never the payload**.
        reversed.ArcIds.Select(id => id.Value).Should().BeEquivalentTo(
            benchmark.Arcs.Select(arc => arc.Id.Value).ToList());
        graph.ArcIds.Select(id => id.Value).Should().BeEquivalentTo(
            benchmark.Arcs.Select(arc => arc.Id.Value).ToList());
    }
}
