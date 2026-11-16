#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using BenchmarkDotNet.Attributes;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Benchmark;

/// <summary>
/// **共享同一张不可变图上的并发首读与稳态读取** / Concurrent first consumption and steady reads over one shared immutable graph.
/// </summary>
/// <remarks>
/// **与基线 `NetworkSharedGraphBenchmark.kt`（146 行，`19c72455` 新增）对应，与 `NetworkIndexBuildBenchmark` 互补：**
/// **后者是每线程独立建图，而这里所有 reader 共享同一张图**。** **冷批次作为一个完整操作计时**，
/// 含建图、线程调度、栅栏对齐与全部 reader 完成；**`DispatchOnly` 是同一调度协议下的空调度对照。**
///
/// **`ColdReverseBatch` 单独存在是有原因的：** **反向视图不是"把弧倒过来重排"那么简单，
/// 它是同一份弧快照配一张方向表——冷批次里重复建出来的反向视图才是这里要量的东西。**
///
/// **Corresponds to the 146-line baseline, complementing `NetworkIndexBuildBenchmark`: that one builds a graph per thread, while here every reader
/// shares the same graph.** **A cold batch is timed as one operation including construction, dispatch, barrier alignment and completion, with
/// `DispatchOnly` as the empty-dispatch control under the same protocol.** **`ColdReverseBatch` exists on its own because a reverse view is not
/// "re-arranging the arcs" — it is the same arc snapshot plus a direction table, and it is the freshly built reverse view inside a cold batch that
/// this measures.**
/// </remarks>
[MemoryDiagnoser]
public class NetworkSharedGraphBenchmark
{
    /// <summary>图形态 / The graph shape.</summary>
    [Params("ring4", "sparse2", "highOut", "empty")]
    public string Shape { get; set; } = "ring4";

    /// <summary>节点数 / The node count.</summary>
    [Params(256, 1_024, 4_096)]
    public int Size { get; set; } = 256;

    /// <summary>冷批次内同时首次消费的 reader 数 / Readers arriving together for the cold batch.</summary>
    [Params(1, 2, 4, 8)]
    public int Readers { get; set; } = 1;

    /// <summary>共享图的首个节点出弧数（测试可见）/ Outgoing arcs of the first node on the shared graph (visible to tests).</summary>
    internal int WarmArcCount { get; private set; }

    private List<NetworkNodeId> _nodes = null!;
    private NetworkGraph<NetworkNodeId, Arc> _warmGraph = null!;
    private NetworkTopology<NetworkNodeId, Arc> _warm = null!;
    private NetworkGraphAccess<NetworkNodeId, Arc> _access = null!;

    /// <summary>共享已消费图与固定输入都在计时外建立 / The consumed shared graph and the fixed inputs are built outside timing.</summary>
    [GlobalSetup]
    public void Setup() {
        _access = new NetworkGraphAccess<NetworkNodeId, Arc>(
            NodeId: node => node,
            ArcId: arc => arc.Id,
            From: arc => arc.From,
            To: arc => arc.To);

        _nodes = new List<NetworkNodeId>(Size);
        for (int index = 0; index < Size; ++index) {
            _nodes.Add(new NetworkNodeId("n" + index));
        }

        _warmGraph = Create(_nodes, BuildArcs());
        WarmArcCount = _warmGraph.Outgoing(_nodes[0]).Count;
        _warm = new NetworkTopology<NetworkNodeId, Arc>(_warmGraph);
        // **先做一次可达，把拓扑内部视图消费掉：** **否则第一次测量里混着冷建表，而这一批要测的是稳态读。**
        // **Prime the topology once, or the first measurement would include the cold build.**
        _ = Reachable(_warm, _nodes[0]);
    }

    /// <summary>共享稳态出弧与入弧读取规模 / Steady shared outgoing and incoming read sizes.</summary>
    /// <returns>读到的弧数之和 / The sum of arcs read</returns>
    [Benchmark]
    public int WarmSharedRead() {
        int sum = WarmArcCount;
        for (int slot = 0; slot < Readers; ++slot) {
            sum += _warmGraph.Outgoing(_nodes[slot % Size]).Count;
            sum += _warmGraph.Incoming(_nodes[slot % Size]).Count;
        }
        return sum;
    }

    /// <summary>共享图上的多源稳态遍历规模 / Multi-source steady traversal size on the shared graph.</summary>
    /// <returns>可达节点数 / The number of reachable nodes</returns>
    [Benchmark]
    public int WarmSharedReachable() {
        var sources = new HashSet<NetworkNodeId>();
        for (int slot = 0; slot < Readers; ++slot) {
            sources.Add(_nodes[slot % Size]);
        }
        return Reachable(_warm, sources).Count;
    }

    /// <summary>一次完整冷并发首次消费批次的可达规模之和 / Reachability totals of one complete cold concurrent batch.</summary>
    /// <returns>可达规模之和 / The sum of reachable sizes</returns>
    [Benchmark]
    public int ColdFirstReadBatch() {
        var topology = new NetworkTopology<NetworkNodeId, Arc>(Create(_nodes, BuildArcs()));
        return RunBatch(slot => Reachable(topology, _nodes[slot % Size]).Count);
    }

    /// <summary>一次完整冷并发首次反向视图消费批次 / Reverse-view totals of one complete cold concurrent batch.</summary>
    /// <returns>反向视图的出弧与入弧数之和 / The outgoing plus incoming count on the reverse view</returns>
    [Benchmark]
    public int ColdReverseBatch() {
        NetworkGraph<NetworkNodeId, Arc> reverse = Create(_nodes, BuildArcs()).Reverse();
        return RunBatch(slot => reverse.Outgoing(_nodes[slot % Size]).Count + reverse.Incoming(_nodes[slot % Size]).Count);
    }

    /// <summary>相同调度协议下的空调度对照 / The empty-dispatch control under the same protocol.</summary>
    /// <returns>到达顺序号之和 / The sum of arrival ordinals</returns>
    [Benchmark]
    public int DispatchOnly() => RunBatch(slot => slot);

    /// <summary>在栅栏上对齐后并发执行一批 / Run one batch after aligning every reader on a barrier.</summary>
    /// <param name="body">每个 reader 的工作 / The work of one reader.</param>
    /// <returns>各 reader 结果之和 / The sum of the reader results</returns>
    private int RunBatch(Func<int, int> body) {
        using var barrier = new Barrier(Readers);
        var workers = new Thread[Readers];
        int[] results = new int[Readers];
        // **到达顺序号用原子递增分配，和基线一致：** **若改成"线程下标"，屏障对齐后的顺序就不再真实，
        // 而"谁先到"正是并发首读要观察的东西。**
        //
        // **Arrival ordinals come from an atomic counter, as in the baseline: using the thread index instead would fabricate an order after the
        // barrier, and "who arrived first" is exactly what a concurrent first read is meant to observe.**
        int arrivals = 0;
        for (int slot = 0; slot < Readers; ++slot) {
            workers[slot] = new Thread(() => {
                barrier.SignalAndWait();
                int ordinal = Interlocked.Increment(ref arrivals) - 1;
                results[ordinal] = body(ordinal);
            });
            workers[slot].Start();
        }

        foreach (Thread worker in workers) {
            worker.Join();
        }

        int total = 0;
        foreach (int result in results) {
            total += result;
        }
        return total;
    }

    /// <summary>按形态建弧 / Build the arcs for the shape.</summary>
    /// <returns>弧列表 / The arc list</returns>
    private List<Arc> BuildArcs() {
        int degree = Shape switch {
            "empty" => 0,
            "sparse2" => 2,
            "highOut" => 8,
            _ => 4,
        };
        var arcs = new List<Arc>(Size * degree);
        for (int from = 0; from < Size; ++from) {
            for (int step = 1; step <= degree; ++step) {
                arcs.Add(new Arc(
                    new NetworkArcId("a" + from + "_" + step),
                    _nodes[from],
                    _nodes[(from + step) % Size]));
            }
        }
        return arcs;
    }

    /// <summary>建图，失败即抛（基准夹具里没有"部分成功"这种状态）/ Build the graph, throwing on failure.</summary>
    /// <param name="nodes">节点 / Nodes.</param>
    /// <param name="arcs">弧 / Arcs.</param>
    /// <returns>图 / The graph</returns>
    private NetworkGraph<NetworkNodeId, Arc> Create(IReadOnlyList<NetworkNodeId> nodes, IReadOnlyList<Arc> arcs) {
        Result<NetworkGraph<NetworkNodeId, Arc>, ErrorCode, Error<ErrorCode>> result =
            NetworkGraph<NetworkNodeId, Arc>.Invoke(nodes, arcs, _access);
        return result is Ok<NetworkGraph<NetworkNodeId, Arc>, ErrorCode, Error<ErrorCode>> ok
            ? ok.Value
            : throw new InvalidOperationException("基准图必须可构造 / the benchmark graph must build: " + result);
    }

    /// <summary>求可达集，失败即抛 / Compute reachability, throwing on failure.</summary>
    /// <param name="topology">拓扑 / The topology.</param>
    /// <param name="source">起点 / The source.</param>
    /// <returns>可达节点 / Reachable nodes</returns>
    private static IReadOnlySet<NetworkNodeId> Reachable(NetworkTopology<NetworkNodeId, Arc> topology, NetworkNodeId source) =>
        Reachable(topology, new HashSet<NetworkNodeId> { source });

    private static IReadOnlySet<NetworkNodeId> Reachable(NetworkTopology<NetworkNodeId, Arc> topology, IReadOnlyCollection<NetworkNodeId> sources) {
        Result<IReadOnlySet<NetworkNodeId>, ErrorCode, Error<ErrorCode>> result = topology.Reachable(sources);
        return result is Ok<IReadOnlySet<NetworkNodeId>, ErrorCode, Error<ErrorCode>> ok
            ? ok.Value
            : throw new InvalidOperationException("基准可达集必须可算 / the benchmark reachability must compute: " + result);
    }

    /// <summary>弧 / An arc.</summary>
    /// <param name="Id">弧标识 / Arc identity.</param>
    /// <param name="From">起点 / Tail.</param>
    /// <param name="To">终点 / Head.</param>
    internal sealed record Arc(NetworkArcId Id, NetworkNodeId From, NetworkNodeId To);
}