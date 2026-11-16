#nullable enable

using BenchmarkDotNet.Attributes;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Benchmark;

/// <summary>
/// 真实图入口的冷构建、反向复用及稳态读取 / Production cold graph construction, reverse reuse, and warm reads.
/// </summary>
/// <remarks>
///
/// **`Setup` 里已经把图预热一次**（`StronglyConnectedComponents`），所以 `WarmReachable` 测的是"索引已建好"的读取，
/// 而 `ColdReachable` 每次都用新图。**两者如果共用同一张图，两个基准测的就是同一件事**，
/// 而稳态读取与冷构建的差距恰恰是这个基准要回答的问题。
///
/// **`Setup` warms the graph once** (`StronglyConnectedComponents`), so `WarmReachable` measures reads against an already-built index while
/// `ColdReachable` builds a fresh graph every call. **Sharing one graph between them would make both benchmarks measure the same thing**, and the gap
/// between warm reads and cold construction is exactly what this benchmark exists to answer.
/// </remarks>
[MemoryDiagnoser]
public class NetworkIndexBuildBenchmark {
    /// <summary>节点数，弧数为其四倍 / Node count, with four times as many arcs.</summary>
    [Params(256, 1024, 4096)]
    public int Size { get; set; } = 256;

    private List<NetworkNodeId> _nodes = null!;
    private List<BenchmarkArc> _arcs = null!;
    private NetworkGraph<NetworkNodeId, BenchmarkArc> _graph = null!;
    private NetworkGraphAccess<NetworkNodeId, BenchmarkArc> _access = null!;

    /// <summary>固定输入和已使用的图在计时外创建 / Prepare fixed inputs and an already-used graph outside timing.</summary>
    [GlobalSetup]
    public void Setup() {
        _nodes = Enumerable.Range(0, Size)
            .Select(index => new NetworkNodeId($"n{index}"))
            .ToList();
        _arcs = new List<BenchmarkArc>(Size * 4);
        for (int from = 0; from < Size; ++from) {
            for (int step = 1; step <= 4; ++step) {
                _arcs.Add(new BenchmarkArc(
                    new NetworkArcId($"a{from}_{step}"),
                    _nodes[from],
                    _nodes[(from + step) % Size]));
            }
        }
        _access = new NetworkGraphAccess<NetworkNodeId, BenchmarkArc>(
            NodeId: node => node,
            ArcId: arc => arc.Id,
            From: arc => arc.From,
            To: arc => arc.To);
        _graph = Create();
        var topology = new NetworkTopology<NetworkNodeId, BenchmarkArc>(_graph);
        _ = topology.StronglyConnectedComponents();
    }

    /// <summary>未消费索引的新图 / A new graph without any index consumed.</summary>
    /// <returns>新图 / The new graph.</returns>
    [Benchmark]
    public object ConstructOnly() => Create();

    /// <summary>首次完整出向遍历的结果 / The result of a first full outgoing traversal.</summary>
    /// <returns>可达标识集 / The reachable identity set.</returns>
    [Benchmark]
    public object ColdReachable() =>
        new NetworkTopology<NetworkNodeId, BenchmarkArc>(Create())
            .Reachable(new[] { _nodes[0] });

    /// <summary>首次双向遍历的结果 / The result of a first bidirectional traversal.</summary>
    /// <returns>强连通分量 / Strongly connected components.</returns>
    [Benchmark]
    public object ColdComponents() =>
        new NetworkTopology<NetworkNodeId, BenchmarkArc>(Create()).StronglyConnectedComponents();

    /// <summary>反向图的出弧，与原图入弧共享 / Reverse outgoing arcs, shared with the original incoming arcs.</summary>
    /// <returns>出弧快照 / The outgoing arc snapshot.</returns>
    [Benchmark]
    public object ReverseAndRead() => _graph.Reverse().Outgoing(_nodes[0]);

    /// <summary>不读取索引的反向视图 / A reverse view without any index read.</summary>
    /// <returns>反向图 / The reversed graph.</returns>
    [Benchmark]
    public object ReverseOnly() => _graph.Reverse();

    /// <summary>稳态真实遍历的结果 / The result of a warm production traversal.</summary>
    /// <returns>可达标识集 / The reachable identity set.</returns>
    [Benchmark]
    public object WarmReachable() =>
        new NetworkTopology<NetworkNodeId, BenchmarkArc>(_graph).Reachable(new[] { _nodes[0] });

    /// <summary>过滤视图上的真实遍历 / A production traversal over a filtered view.</summary>
    /// <returns>可达标识集 / The reachable identity set.</returns>
    [Benchmark]
    public object FilteredReachable() {
        NetworkNodeId last = _nodes[^1];
        return new NetworkTopology<NetworkNodeId, BenchmarkArc>(_graph)
            .Filtered(allowedNode: node => node != last, allowedArc: arc => arc.To != last)
            .Reachable(new[] { _nodes[0] });
    }

    internal NetworkGraph<NetworkNodeId, BenchmarkArc> Create() {
        Result<NetworkGraph<NetworkNodeId, BenchmarkArc>, ErrorCode, Error<ErrorCode>> created =
            NetworkGraph<NetworkNodeId, BenchmarkArc>.Invoke(_nodes, _arcs, _access);
        if (created is not Ok<
                NetworkGraph<NetworkNodeId, BenchmarkArc>,
                ErrorCode,
                Error<ErrorCode>> ok) {
            throw new InvalidOperationException(
                "基准图必须合法 / the benchmark graph must be valid: " + created);
        }
        return ok.Value;
    }

    internal IReadOnlyList<NetworkNodeId> Nodes => _nodes;

    internal IReadOnlyList<BenchmarkArc> Arcs => _arcs;

    /// <summary>基准用弧载荷 / The benchmark arc payload.</summary>
    /// <param name="Id">弧标识 / Arc identity.</param>
    /// <param name="From">起点 / Source.</param>
    /// <param name="To">终点 / Target.</param>
    internal sealed record BenchmarkArc(NetworkArcId Id, NetworkNodeId From, NetworkNodeId To);
}
