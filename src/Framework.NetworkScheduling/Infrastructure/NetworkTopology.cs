// 拓扑算法 / Topology algorithms.
// 与固定 Kotlin 基线 `NetworkTopology.kt`（302 行）对应。
// Mirrors the pinned baseline's `NetworkTopology.kt`.
//
// 本类**不拥有业务或求解状态**：只读图、只算，过滤产生新视图而不改动原图。所有算法都按"标识"而非"索引"工作，因此下游
// 可以在过滤后的视图里继续用同一套标识寻址。
// This class **owns neither business nor solver state**: it only reads the graph and computes, and filtering produces a new
// view rather than mutating the original. Every algorithm works on **identities** rather than indices, so downstream keeps
// addressing with the same identifiers inside filtered views.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;

/// <summary>
/// 最短路终态 / Shortest-path terminal states.
/// </summary>
public enum ShortestPathStatus {
    /// <summary>已找到 / Found.</summary>
    Found,

    /// <summary>不可达 / Unreachable.</summary>
    Unreachable,

    /// <summary>负环 / Negative cycle.</summary>
    NegativeCycle,
}

/// <summary>
/// 最短路结果 / Shortest-path result.
/// </summary>
/// <typeparam name="A">弧载荷类型 / Arc payload type.</typeparam>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="Status">终态 / Terminal state.</param>
/// <param name="Arcs">有序业务弧 / Ordered payload arcs.</param>
/// <param name="Cost">总成本，无最短路时为 null / Total cost, null without a shortest path.</param>
public sealed record ShortestPathResult<A, V>(
    ShortestPathStatus Status,
    IReadOnlyList<A> Arcs,
    Quantity<V>? Cost) where V : struct, IRealNumber<V>;

/// <summary>
/// 拓扑算法，不拥有业务或求解状态 / Topology algorithms without business or solver state.
/// </summary>
/// <typeparam name="N">节点载荷类型 / Node payload type.</typeparam>
/// <typeparam name="A">弧载荷类型 / Arc payload type.</typeparam>
public sealed class NetworkTopology<N, A> {
    private readonly NetworkGraph<N, A> _graph;

    /// <summary>
    /// 由图构造拓扑视图 / Construct a topology view over a graph.
    /// </summary>
    /// <param name="graph">有效网络视图 / Effective network view.</param>
    public NetworkTopology(NetworkGraph<N, A> graph) {
        ArgumentNullException.ThrowIfNull(graph);
        _graph = graph;
    }

    /// <summary>有效网络视图 / Effective network view.</summary>
    public NetworkGraph<N, A> Graph => _graph;

    /// <summary>
    /// 创建仅含允许节点和弧的拓扑视图 / Create a topology view containing only allowed nodes and arcs.
    /// </summary>
    /// <param name="allowedNode">节点过滤器 / Node predicate.</param>
    /// <param name="allowedArc">弧过滤器 / Arc predicate.</param>
    /// <returns>过滤后的拓扑 / The filtered topology.</returns>
    public NetworkTopology<N, A> Filtered(
        Func<NetworkNodeId, bool>? allowedNode = null,
        Func<A, bool>? allowedArc = null) {
        Func<NetworkNodeId, bool> nodeFilter = allowedNode ?? (_ => true);
        Func<A, bool> arcFilter = allowedArc ?? (_ => true);
        NetworkGraph<N, A> filteredNodes =
            _graph.FilteredNodes(node => nodeFilter(_graph.NodeId(node)));
        return new NetworkTopology<N, A>(filteredNodes.Filtered(arcFilter));
    }

    /// <summary>
    /// 多源可达节点 / Multi-source reachability.
    /// </summary>
    /// <remarks>
    /// 起点集合中**任一标识不存在即报错**，而不是静默忽略：忽略会让调用方以为"这个起点没有可达节点"，而真实原因是拼写
    /// 错误或图被过滤掉了该节点。
    /// **Any missing source identity is an error** rather than being silently ignored: ignoring it would make the caller read
    /// "this source reaches nothing", when the real cause is a typo or the node being filtered out.
    /// </remarks>
    /// <param name="sources">起点集合 / Sources.</param>
    /// <param name="allowed">弧过滤器 / Arc predicate.</param>
    /// <param name="allowedNode">节点过滤器 / Node predicate.</param>
    /// <returns>可达标识或输入错误 / Reachable identities, or an invalid-input failure.</returns>
    public Result<IReadOnlySet<NetworkNodeId>, ErrorCode, Error<ErrorCode>> Reachable(
        IReadOnlyCollection<NetworkNodeId> sources,
        Func<A, bool>? allowed = null,
        Func<NetworkNodeId, bool>? allowedNode = null) {
        ArgumentNullException.ThrowIfNull(sources);
        Func<A, bool> arcFilter = allowed ?? (_ => true);
        Func<NetworkNodeId, bool> nodeFilter = allowedNode ?? (_ => true);

        if (sources.Any(source => _graph.IndexOf(source) is null)) {
            return NetworkSchedulingErrors.Failure<IReadOnlySet<NetworkNodeId>>(
                "可达性起点不存在 / Reachability source does not exist");
        }
        var visited = new HashSet<NetworkNodeId>(sources.Where(nodeFilter));
        var queue = new Queue<NetworkNodeId>(visited);
        IReadOnlyDictionary<NetworkNodeId, List<A>> outgoing =
            queue.Count == 0 ? new Dictionary<NetworkNodeId, List<A>>() : _graph.OutgoingIndex();
        while (queue.Count > 0) {
            NetworkNodeId node = queue.Dequeue();
            if (!outgoing.TryGetValue(node, out List<A>? arcs)) {
                continue;
            }
            foreach (A arc in arcs.Where(arcFilter)) {
                NetworkNodeId next = _graph.To(arc);
                if (nodeFilter(next) && visited.Add(next)) {
                    queue.Enqueue(next);
                }
            }
        }
        return Results.Ok<IReadOnlySet<NetworkNodeId>>(visited);
    }

    /// <summary>
    /// 无权路径见证 / Unweighted path witness.
    /// </summary>
    /// <param name="source">起点 / Source.</param>
    /// <param name="target">终点 / Target.</param>
    /// <param name="allowedArc">弧过滤器 / Arc predicate.</param>
    /// <param name="allowedNode">节点过滤器 / Node predicate.</param>
    /// <returns>有序弧，不可达时为 null / Ordered arcs, null when unreachable.</returns>
    public Result<IReadOnlyList<A>?, ErrorCode, Error<ErrorCode>> Witness(
        NetworkNodeId source,
        NetworkNodeId target,
        Func<A, bool>? allowedArc = null,
        Func<NetworkNodeId, bool>? allowedNode = null) {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        Func<A, bool> arcFilter = allowedArc ?? (_ => true);
        Func<NetworkNodeId, bool> nodeFilter = allowedNode ?? (_ => true);

        if (_graph.IndexOf(source) is null || _graph.IndexOf(target) is null) {
            return NetworkSchedulingErrors.Failure<IReadOnlyList<A>?>(
                "路径端点不存在 / Path endpoint does not exist");
        }
        if (!nodeFilter(source) || !nodeFilter(target)) {
            return Results.Ok<IReadOnlyList<A>?>(null);
        }
        if (source.Equals(target)) {
            return Results.Ok<IReadOnlyList<A>?>(Array.Empty<A>());
        }

        var previous = new Dictionary<NetworkNodeId, A>();
        var visited = new HashSet<NetworkNodeId> { source };
        var queue = new Queue<NetworkNodeId>();
        queue.Enqueue(source);
        IReadOnlyDictionary<NetworkNodeId, List<A>> outgoing = _graph.OutgoingIndex();
        while (queue.Count > 0) {
            NetworkNodeId node = queue.Dequeue();
            if (node.Equals(target)) {
                return Results.Ok<IReadOnlyList<A>?>(Backtrack(source, target, previous));
            }
            if (!outgoing.TryGetValue(node, out List<A>? arcs)) {
                continue;
            }
            foreach (A arc in arcs.Where(arcFilter)) {
                NetworkNodeId next = _graph.To(arc);
                if (nodeFilter(next) && visited.Add(next)) {
                    previous[next] = arc;
                    queue.Enqueue(next);
                }
            }
        }
        return Results.Ok<IReadOnlyList<A>?>(null);
    }

    /// <summary>
    /// 拓扑排序，有环时为 null / Topological order, null for cyclic graphs.
    /// </summary>
    /// <remarks>
    /// 用 Kahn 算法（按入度逐个摘除）。**返回 `null` 表示有环**，而不是抛异常或返回部分顺序：部分顺序对下游毫无用处，
    /// 因为它看起来像一个合法答案。
    /// Uses Kahn's algorithm (peel by in-degree). **`null` means a cycle**, rather than throwing or returning a partial order:
    /// a partial order is useless downstream because it looks like a legitimate answer.
    /// </remarks>
    /// <returns>节点顺序，有环时为 null / Node order, null when cyclic.</returns>
    public IReadOnlyList<NetworkNodeId>? TopologicalOrder() {
        IReadOnlyDictionary<NetworkNodeId, List<A>> incoming =
            _graph.Nodes.Count == 0 ? new Dictionary<NetworkNodeId, List<A>>() : _graph.IncomingIndex();
        var degree = new Dictionary<NetworkNodeId, int>();
        foreach (NetworkNodeId id in _graph.NodeIds) {
            degree[id] = incoming.TryGetValue(id, out List<A>? arcs) ? arcs.Count : 0;
        }
        var queue = new Queue<NetworkNodeId>(degree.Where(pair => pair.Value == 0).Select(pair => pair.Key));
        var result = new List<NetworkNodeId>();
        IReadOnlyDictionary<NetworkNodeId, List<A>> outgoing =
            queue.Count == 0 ? new Dictionary<NetworkNodeId, List<A>>() : _graph.OutgoingIndex();
        while (queue.Count > 0) {
            NetworkNodeId node = queue.Dequeue();
            result.Add(node);
            if (!outgoing.TryGetValue(node, out List<A>? arcs)) {
                continue;
            }
            foreach (A arc in arcs) {
                NetworkNodeId next = _graph.To(arc);
                int count = degree[next] - 1;
                degree[next] = count;
                if (count == 0) {
                    queue.Enqueue(next);
                }
            }
        }
        return result.Count == _graph.Nodes.Count ? result : null;
    }

    /// <summary>
    /// 检查有向环 / Detect a directed cycle.
    /// </summary>
    /// <returns>是否有环 / Whether a cycle exists.</returns>
    public bool HasCycle() => TopologicalOrder() is null;

    /// <summary>
    /// 迭代式 Kosaraju 强连通分量 / Iterative Kosaraju strongly connected components.
    /// </summary>
    /// <remarks>
    /// **必须迭代而非递归**：递归实现遇到长链（数万节点）会栈溢出，而网络图的节点规模完全可能达到这个量级。第一遍用显式
    /// 栈记录完成序，第二遍在反向图上按逆完成序扩展。
    /// **Iterative rather than recursive**: a recursive version blows the stack on long chains (tens of thousands of nodes),
    /// which network graphs can easily reach. The first pass records finishing order with an explicit stack; the second
    /// expands on the reversed graph in reverse finishing order.
    /// </remarks>
    /// <returns>分量列表 / Components.</returns>
    public IReadOnlyList<IReadOnlySet<NetworkNodeId>> StronglyConnectedComponents() {
        var seen = new HashSet<NetworkNodeId>();
        var order = new List<NetworkNodeId>();
        IReadOnlyDictionary<NetworkNodeId, List<A>> outgoing =
            _graph.Nodes.Count == 0 ? new Dictionary<NetworkNodeId, List<A>>() : _graph.OutgoingIndex();
        foreach (NetworkNodeId root in _graph.NodeIds) {
            var stack = new Stack<(NetworkNodeId Node, bool Closing)>();
            stack.Push((root, false));
            while (stack.Count > 0) {
                (NetworkNodeId node, bool closing) = stack.Pop();
                if (closing) {
                    order.Add(node);
                } else if (seen.Add(node)) {
                    stack.Push((node, true));
                    if (outgoing.TryGetValue(node, out List<A>? arcs)) {
                        for (int index = arcs.Count - 1; index >= 0; index--) {
                            stack.Push((_graph.To(arcs[index]), false));
                        }
                    }
                }
            }
        }

        seen.Clear();
        var components = new List<IReadOnlySet<NetworkNodeId>>();
        IReadOnlyDictionary<NetworkNodeId, List<A>> incoming =
            order.Count == 0 ? new Dictionary<NetworkNodeId, List<A>>() : _graph.IncomingIndex();
        for (int position = order.Count - 1; position >= 0; position--) {
            NetworkNodeId root = order[position];
            if (seen.Contains(root)) {
                continue;
            }
            var component = new HashSet<NetworkNodeId>();
            var stack = new Stack<NetworkNodeId>();
            stack.Push(root);
            while (stack.Count > 0) {
                NetworkNodeId node = stack.Pop();
                if (!seen.Add(node)) {
                    continue;
                }
                component.Add(node);
                if (incoming.TryGetValue(node, out List<A>? arcs)) {
                    foreach (A arc in arcs) {
                        stack.Push(_graph.From(arc));
                    }
                }
            }
            components.Add(component);
        }
        return components;
    }

    /// <summary>
    /// 最短路径 / Shortest path.
    /// </summary>
    /// <remarks>
    /// **非负权重用 Dijkstra，出现负权重改用 Bellman-Ford**。负环只在**能影响目标**时才报 `NegativeCycle`：一个与目标无关的
    /// 负环不该让整趟求解失败，否则图上任意一处负环都会毒化所有查询。
    /// **Dijkstra for non-negative weights, Bellman-Ford once any weight is negative.** A negative cycle is reported only when
    /// it **can affect the target**: an unrelated negative cycle must not fail the whole solve, or any single negative cycle
    /// anywhere in the graph would poison every query.
    /// </remarks>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="source">起点 / Source.</param>
    /// <param name="target">终点 / Target.</param>
    /// <param name="zero">数值零 / Numeric zero.</param>
    /// <param name="unit">成本单位 / Cost unit.</param>
    /// <param name="weight">弧成本 / Arc cost.</param>
    /// <param name="allowedArc">弧过滤器 / Arc predicate.</param>
    /// <param name="allowedNode">节点过滤器 / Node predicate.</param>
    /// <returns>最短路结果 / The shortest-path result.</returns>
    public Result<ShortestPathResult<A, V>, ErrorCode, Error<ErrorCode>> ShortestPath<V>(
        NetworkNodeId source,
        NetworkNodeId target,
        V zero,
        PhysicalUnit unit,
        Func<A, Result<Quantity<V>, ErrorCode, Error<ErrorCode>>> weight,
        Func<A, bool>? allowedArc = null,
        Func<NetworkNodeId, bool>? allowedNode = null) where V : struct, IRealNumber<V> {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(weight);

        if (_graph.IndexOf(source) is null || _graph.IndexOf(target) is null) {
            return NetworkSchedulingErrors.Failure<ShortestPathResult<A, V>>(
                "最短路端点不存在 / Shortest-path endpoint does not exist");
        }
        Func<NetworkNodeId, bool> nodeFilter = allowedNode ?? (_ => true);
        if (!nodeFilter(source) || !nodeFilter(target)) {
            return Results.Ok(new ShortestPathResult<A, V>(
                ShortestPathStatus.Unreachable, Array.Empty<A>(), null));
        }

        NetworkGraph<N, A> filteredNodes = _graph.FilteredNodes(node => nodeFilter(_graph.NodeId(node)));
        NetworkGraph<N, A> effectiveGraph = filteredNodes.Filtered(allowedArc ?? (_ => true));
        return new NetworkTopology<N, A>(effectiveGraph)
            .ShortestPathOnView(source, target, zero, unit, weight);
    }

    /// <summary>
    /// 在已过滤视图上计算最短路 / Compute the shortest path on the already-filtered view.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="source">起点 / Source.</param>
    /// <param name="target">终点 / Target.</param>
    /// <param name="zero">数值零 / Numeric zero.</param>
    /// <param name="unit">成本单位 / Cost unit.</param>
    /// <param name="weight">弧成本 / Arc cost.</param>
    /// <returns>最短路结果 / The shortest-path result.</returns>
    private Result<ShortestPathResult<A, V>, ErrorCode, Error<ErrorCode>> ShortestPathOnView<V>(
        NetworkNodeId source,
        NetworkNodeId target,
        V zero,
        PhysicalUnit unit,
        Func<A, Result<Quantity<V>, ErrorCode, Error<ErrorCode>>> weight)
        where V : struct, IRealNumber<V> {
        var weights = new Dictionary<NetworkArcId, V>();
        foreach (A arc in _graph.Arcs) {
            Result<Quantity<V>, ErrorCode, Error<ErrorCode>> quantity = weight(arc);
            if (quantity is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> failed) {
                return Results.Failed<ShortestPathResult<A, V>>(failed.Error);
            }
            Result<Quantity<V>, ErrorCode, Error<ErrorCode>> converted = quantity.Value.ConvertTo(unit);
            if (converted is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> unconvertible) {
                return NetworkSchedulingErrors.Failure<ShortestPathResult<A, V>>(
                    "最短路成本单位不兼容 / Incompatible shortest-path cost unit");
            }
            weights[_graph.ArcId(arc)] = converted.Value.Value;
        }

        var distance = new Dictionary<NetworkNodeId, V> { [source] = zero };
        var previous = new Dictionary<NetworkNodeId, A>();
        bool Relax(A arc) {
            if (!distance.TryGetValue(_graph.From(arc), out V origin)) {
                return false;
            }
            V candidate = origin.Plus(weights[_graph.ArcId(arc)]);
            NetworkNodeId destination = _graph.To(arc);
            if (distance.TryGetValue(destination, out V existing)
                && candidate.CompareTo(existing) >= 0) {
                return false;
            }
            distance[destination] = candidate;
            previous[destination] = arc;
            return true;
        }

        bool anyNegative = weights.Values.Any(value => value.CompareTo(zero) < 0);
        if (!anyNegative) {
            IReadOnlyDictionary<NetworkNodeId, List<A>> outgoing = _graph.OutgoingIndex();
            var queue = new PriorityQueue<NetworkNodeId, V>(Comparer<V>.Create(
                (lhs, rhs) => lhs.CompareTo(rhs)));
            queue.Enqueue(source, zero);
            while (queue.Count > 0) {
                NetworkNodeId node = queue.Dequeue();
                if (!outgoing.TryGetValue(node, out List<A>? arcs)) {
                    continue;
                }
                foreach (A arc in arcs) {
                    if (Relax(arc)) {
                        queue.Enqueue(_graph.To(arc), distance[_graph.To(arc)]);
                    }
                }
            }
        } else {
            for (int iteration = 1; iteration < _graph.Nodes.Count; iteration++) {
                bool changed = false;
                foreach (A arc in _graph.Arcs) {
                    if (Relax(arc)) {
                        changed = true;
                    }
                }
                if (!changed) {
                    break;
                }
            }
            var affected = new HashSet<NetworkNodeId>();
            foreach (A arc in _graph.Arcs) {
                if (!distance.TryGetValue(_graph.From(arc), out V origin)) {
                    continue;
                }
                NetworkNodeId destination = _graph.To(arc);
                bool improves = !distance.TryGetValue(destination, out V existing)
                    || origin.Plus(weights[_graph.ArcId(arc)]).CompareTo(existing) < 0;
                if (improves) {
                    affected.Add(destination);
                }
            }
            Result<IReadOnlySet<NetworkNodeId>, ErrorCode, Error<ErrorCode>> reversedReachable =
                new NetworkTopology<N, A>(_graph.Reverse()).Reachable(new[] { target });
            IReadOnlySet<NetworkNodeId> relevant = reversedReachable.IsOk
                ? reversedReachable.Value
                : new HashSet<NetworkNodeId>();
            if (affected.Any(relevant.Contains)) {
                return Results.Ok(new ShortestPathResult<A, V>(
                    ShortestPathStatus.NegativeCycle, Array.Empty<A>(), null));
            }
        }

        if (!distance.TryGetValue(target, out V cost)) {
            return Results.Ok(new ShortestPathResult<A, V>(
                ShortestPathStatus.Unreachable, Array.Empty<A>(), null));
        }
        return Results.Ok(new ShortestPathResult<A, V>(
            ShortestPathStatus.Found,
            Backtrack(source, target, previous),
            new Quantity<V>(cost, unit)));
    }

    /// <summary>
    /// 回溯路径 / Backtrack the path.
    /// </summary>
    /// <remarks>
    /// 步数上限取节点数：负环路径回溯会无限循环，而上限保证它在最坏情况下也能停下来并返回空路径。
    /// The step cap is the node count: backtracking a path through a negative cycle would loop forever, and the cap guarantees
    /// it terminates with an empty path in the worst case.
    /// </remarks>
    /// <param name="source">起点 / Source.</param>
    /// <param name="target">终点 / Target.</param>
    /// <param name="previous">前驱弧 / Predecessor arcs.</param>
    /// <returns>有序弧 / Ordered arcs.</returns>
    private IReadOnlyList<A> Backtrack(
        NetworkNodeId source,
        NetworkNodeId target,
        IReadOnlyDictionary<NetworkNodeId, A> previous) {
        var result = new List<A>();
        NetworkNodeId current = target;
        while (!current.Equals(source) && result.Count < _graph.Nodes.Count) {
            if (!previous.TryGetValue(current, out A? arc) || arc is null) {
                return Array.Empty<A>();
            }
            result.Add(arc);
            current = _graph.From(arc);
        }
        if (!current.Equals(source)) {
            return Array.Empty<A>();
        }
        result.Reverse();
        return result;
    }
}
