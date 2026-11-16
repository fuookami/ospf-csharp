// 不可变有向多重图 / Immutable directed multigraph.
//
// 这一层的设计要点是**拓扑标识在创建时快照化**：节点与弧的标识、起点、终点在构造时全部提取并冻结，之后图再被过滤、反转，
// 也只是对这些快如此索引选择，而不是重新读业务对象。因此**过滤与反转不会改变任何标识**，下游可以放心按标识寻址。
// The design point here is that **topology identities are snapshotted at construction**: node and arc identifiers, origins
// and destinations are all extracted and frozen up front, so later filtering or reversing only re-indexes those snapshots
// rather than re-reading business objects. **Filtering and reversing therefore never change any identity**, and downstream
// can safely address by identifier.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;

/// <summary>
/// 与数值和业务无关的拓扑访问 / Numeric- and business-independent topology access.
/// </summary>
/// <typeparam name="N">节点载荷类型 / Node payload type.</typeparam>
/// <typeparam name="A">弧载荷类型 / Arc payload type.</typeparam>
/// <param name="NodeId">节点标识提取器 / Node identity selector.</param>
/// <param name="ArcId">弧标识提取器 / Arc identity selector.</param>
/// <param name="From">起点提取器 / Origin selector.</param>
/// <param name="To">终点提取器 / Destination selector.</param>
public sealed record NetworkGraphAccess<N, A>(
    Func<N, NetworkNodeId> NodeId,
    Func<A, NetworkArcId> ArcId,
    Func<A, NetworkNodeId> From,
    Func<A, NetworkNodeId> To);

/// <summary>
/// 不可变有向多重图 / Immutable directed multigraph.
/// </summary>
/// <typeparam name="N">节点载荷类型 / Node payload type.</typeparam>
/// <typeparam name="A">弧载荷类型 / Arc payload type.</typeparam>
public sealed class NetworkGraph<N, A> {
    private readonly List<N> _nodes;
    private readonly List<A> _arcs;
    private readonly List<NetworkNodeId> _nodeIds;
    private readonly List<NetworkArcId> _arcIds;
    private readonly List<NetworkNodeId> _arcFrom;
    private readonly List<NetworkNodeId> _arcTo;
    private readonly bool _reversed;

    /// <summary>
    /// 由快照构造图 / Construct a graph from snapshots.
    /// </summary>
    /// <param name="nodes">节点快照 / Node snapshot.</param>
    /// <param name="arcs">弧快照 / Arc snapshot.</param>
    /// <param name="access">拓扑约定 / Topology access.</param>
    /// <param name="nodeIds">节点标识快照 / Node identity snapshot.</param>
    /// <param name="arcIds">弧标识快照 / Arc identity snapshot.</param>
    /// <param name="arcFrom">弧起点快照 / Arc origin snapshot.</param>
    /// <param name="arcTo">弧终点快照 / Arc destination snapshot.</param>
    /// <param name="reversed">是否反向视图 / Whether this is a reversed view.</param>
    private NetworkGraph(
        List<N> nodes,
        List<A> arcs,
        NetworkGraphAccess<N, A> access,
        List<NetworkNodeId> nodeIds,
        List<NetworkArcId> arcIds,
        List<NetworkNodeId> arcFrom,
        List<NetworkNodeId> arcTo,
        bool reversed) {
        _nodes = nodes;
        _arcs = arcs;
        Access = access;
        _nodeIds = nodeIds;
        _arcIds = arcIds;
        _arcFrom = arcFrom;
        _arcTo = arcTo;
        _reversed = reversed;
    }

    /// <summary>拓扑约定 / Topology access.</summary>
    public NetworkGraphAccess<N, A> Access { get; }

    /// <summary>节点快照 / Node snapshot.</summary>
    public IReadOnlyList<N> Nodes => _nodes;

    /// <summary>弧快照 / Arc snapshot.</summary>
    public IReadOnlyList<A> Arcs => _arcs;

    /// <summary>稳定节点标识 / Stable node identities.</summary>
    public IReadOnlyList<NetworkNodeId> NodeIds => _nodeIds;

    /// <summary>稳定弧标识 / Stable arc identities.</summary>
    public IReadOnlyList<NetworkArcId> ArcIds => _arcIds;

    /// <summary>查询节点索引 / Look up the node index.</summary>
    /// <param name="id">节点标识 / Node identity.</param>
    /// <returns>索引或 null / The index, or null.</returns>
    public int? IndexOf(NetworkNodeId id) {
        ArgumentNullException.ThrowIfNull(id);
        int index = _nodeIds.IndexOf(id);
        return index >= 0 ? index : null;
    }

    /// <summary>获取节点的快照标识 / Get a node's snapshotted identity.</summary>
    /// <param name="node">节点载荷 / Node payload.</param>
    /// <returns>稳定标识 / Stable identity.</returns>
    public NetworkNodeId NodeId(N node) {
        ArgumentNullException.ThrowIfNull(node);
        int index = _nodes.IndexOf(node);
        return index >= 0 ? _nodeIds[index] : Access.NodeId(node);
    }

    /// <summary>获取弧的快照标识 / Get an arc's snapshotted identity.</summary>
    /// <param name="arc">弧载荷 / Arc payload.</param>
    /// <returns>稳定标识 / Stable identity.</returns>
    public NetworkArcId ArcId(A arc) {
        ArgumentNullException.ThrowIfNull(arc);
        int index = _arcs.IndexOf(arc);
        return index >= 0 ? _arcIds[index] : Access.ArcId(arc);
    }

    /// <summary>查询节点 / Look up a node.</summary>
    /// <param name="id">节点标识 / Node identity.</param>
    /// <returns>节点或 null / The node, or null.</returns>
    public N? Node(NetworkNodeId id) {
        int? index = IndexOf(id);
        if (index is not { } position) {
            return default;
        }
        return _nodes[position];
    }

    /// <summary>查询弧 / Look up an arc.</summary>
    /// <param name="id">弧标识 / Arc identity.</param>
    /// <returns>弧或 null / The arc, or null.</returns>
    public A? Arc(NetworkArcId id) {
        ArgumentNullException.ThrowIfNull(id);
        int index = _arcIds.IndexOf(id);
        if (index < 0) {
            return default;
        }
        return _arcs[index];
    }

    /// <summary>
    /// 获取视图中的起点 / Get the origin in this view.
    /// </summary>
    /// <remarks>
    /// 反向视图中起点与终点**互换**，但载荷与标识不变——这正是 `Reverse` 的全部效果。
    /// In a reversed view origin and destination **swap** while payloads and identities stay unchanged — that is the entire
    /// effect of `Reverse`.
    /// </remarks>
    /// <param name="arc">弧 / Arc.</param>
    /// <returns>起点标识 / Origin identity.</returns>
    public NetworkNodeId From(A arc) {
        int index = ArcPositionOf(arc);
        if (index < 0) {
            return Access.From(arc);
        }
        return _reversed ? _arcTo[index] : _arcFrom[index];
    }

    /// <summary>获取视图中的终点 / Get the destination in this view.</summary>
    /// <param name="arc">弧 / Arc.</param>
    /// <returns>终点标识 / Destination identity.</returns>
    public NetworkNodeId To(A arc) {
        int index = ArcPositionOf(arc);
        if (index < 0) {
            return Access.To(arc);
        }
        return _reversed ? _arcFrom[index] : _arcTo[index];
    }

    /// <summary>获取出弧 / Get outgoing arcs.</summary>
    /// <param name="id">节点标识 / Node identity.</param>
    /// <returns>弧快照 / Arc snapshot.</returns>
    public IReadOnlyList<A> Outgoing(NetworkNodeId id) {
        ArgumentNullException.ThrowIfNull(id);
        return OutgoingIndex().TryGetValue(id, out List<A>? arcs) ? arcs : Array.Empty<A>();
    }

    /// <summary>获取入弧 / Get incoming arcs.</summary>
    /// <param name="id">节点标识 / Node identity.</param>
    /// <returns>弧快照 / Arc snapshot.</returns>
    public IReadOnlyList<A> Incoming(NetworkNodeId id) {
        ArgumentNullException.ThrowIfNull(id);
        return IncomingIndex().TryGetValue(id, out List<A>? arcs) ? arcs : Array.Empty<A>();
    }

    /// <summary>
    /// 过滤弧，保留原始标识 / Filter arcs, preserving their original identities.
    /// </summary>
    /// <param name="predicate">弧过滤器 / Arc predicate.</param>
    /// <returns>过滤图 / The filtered graph.</returns>
    public NetworkGraph<N, A> Filtered(Func<A, bool> predicate) {
        ArgumentNullException.ThrowIfNull(predicate);
        var retained = Enumerable.Range(0, _arcs.Count).Where(index => predicate(_arcs[index])).ToList();
        return new NetworkGraph<N, A>(
            _nodes,
            retained.Select(index => _arcs[index]).ToList(),
            Access,
            _nodeIds,
            retained.Select(index => _arcIds[index]).ToList(),
            retained.Select(index => _arcFrom[index]).ToList(),
            retained.Select(index => _arcTo[index]).ToList(),
            _reversed);
    }

    /// <summary>
    /// 过滤节点并移除所有关联弧 / Filter nodes and remove every incident arc.
    /// </summary>
    /// <remarks>
    /// **保留节点的标识列表与节点列表一一对应**，且被删节点的关联弧全部移除——只删节点不删弧会让图里出现指向不存在节点的弧，
    /// 后续遍历会在这些弧上反复尝试解析节点。
    /// **The retained identity list stays aligned with the retained node list**, and every arc incident to a removed node is
    /// dropped — deleting nodes without their arcs leaves arcs pointing at absent nodes, and later traversals keep trying to
    /// resolve them.
    /// </remarks>
    /// <param name="predicate">节点过滤器 / Node predicate.</param>
    /// <returns>过滤图 / The filtered graph.</returns>
    public NetworkGraph<N, A> FilteredNodes(Func<N, bool> predicate) {
        ArgumentNullException.ThrowIfNull(predicate);

        var retainedNodes = Enumerable.Range(0, _nodes.Count).Where(index => predicate(_nodes[index])).ToList();
        var retainedIds = retainedNodes.Select(index => _nodeIds[index]).ToHashSet();
        var retainedArcs = Enumerable.Range(0, _arcs.Count)
            .Where(index => retainedIds.Contains(_arcFrom[index]) && retainedIds.Contains(_arcTo[index]))
            .ToList();
        return new NetworkGraph<N, A>(
            retainedNodes.Select(index => _nodes[index]).ToList(),
            retainedArcs.Select(index => _arcs[index]).ToList(),
            Access,
            retainedNodes.Select(index => _nodeIds[index]).ToList(),
            retainedArcs.Select(index => _arcIds[index]).ToList(),
            retainedArcs.Select(index => _arcFrom[index]).ToList(),
            retainedArcs.Select(index => _arcTo[index]).ToList(),
            _reversed);
    }

    /// <summary>
    /// 反向视图，不改写业务载荷或稳定标识 / A reverse view that rewrites neither payloads nor stable identities.
    /// </summary>
    /// <returns>反向图 / The reversed graph.</returns>
    public NetworkGraph<N, A> Reverse() =>
        new(_nodes, _arcs, Access, _nodeIds, _arcIds, _arcFrom, _arcTo, !_reversed);

    /// <summary>
    /// 算法在循环外固定方向表 / Algorithms capture a directional table outside their loops.
    /// </summary>
    /// <returns>出弧索引 / The outgoing index.</returns>
    internal IReadOnlyDictionary<NetworkNodeId, List<A>> OutgoingIndex() =>
        _reversed ? BuildAdjacency(_arcTo) : BuildAdjacency(_arcFrom);

    /// <summary>入弧索引 / The incoming index.</summary>
    /// <returns>入弧索引 / The incoming index.</returns>
    internal IReadOnlyDictionary<NetworkNodeId, List<A>> IncomingIndex() =>
        _reversed ? BuildAdjacency(_arcFrom) : BuildAdjacency(_arcTo);

    /// <summary>
    /// 创建并校验通用图 / Create and validate a generic graph.
    /// </summary>
    /// <remarks>
    /// 四条校验按此顺序：节点标识非空且唯一 → 弧标识非空且唯一 → 弧引用已存在的节点。**标识唯一性必须在引用检查之前**：
    /// 重复标识会让"弧引用了哪个节点"产生歧义，先查引用只会得到"引用了某节点"这种无用的通过。
    /// Four checks in this order: node identities non-blank and unique → arc identities non-blank and unique → arcs reference
    /// existing nodes. **Uniqueness must precede the reference check**: duplicate identities make "which node does this arc
    /// reference" ambiguous, and checking references first only yields a useless pass.
    /// </remarks>
    /// <param name="nodes">节点 / Nodes.</param>
    /// <param name="arcs">弧 / Arcs.</param>
    /// <param name="access">拓扑访问 / Topology access.</param>
    /// <returns>图或错误 / The graph, or an error.</returns>
    public static Result<NetworkGraph<N, A>, ErrorCode, Error<ErrorCode>> Invoke(
        IReadOnlyList<N> nodes,
        IReadOnlyList<A> arcs,
        NetworkGraphAccess<N, A> access) {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(arcs);
        ArgumentNullException.ThrowIfNull(access);

        var nodeIds = nodes.Select(access.NodeId).ToList();
        if (nodeIds.Any(id => string.IsNullOrWhiteSpace(id.Value)) || nodeIds.Distinct().Count() != nodeIds.Count) {
            return NetworkSchedulingErrors.Failure<NetworkGraph<N, A>>(
                "节点标识为空或重复 / Blank or duplicate node identity");
        }
        var arcIds = arcs.Select(access.ArcId).ToList();
        if (arcIds.Any(id => string.IsNullOrWhiteSpace(id.Value)) || arcIds.Distinct().Count() != arcIds.Count) {
            return NetworkSchedulingErrors.Failure<NetworkGraph<N, A>>(
                "弧标识为空或重复 / Blank or duplicate arc identity");
        }
        var arcFrom = arcs.Select(access.From).ToList();
        var arcTo = arcs.Select(access.To).ToList();
        var nodeIdSet = nodeIds.ToHashSet();
        if (Enumerable.Range(0, arcs.Count)
            .Any(index => !nodeIdSet.Contains(arcFrom[index]) || !nodeIdSet.Contains(arcTo[index]))) {
            return NetworkSchedulingErrors.Failure<NetworkGraph<N, A>>(
                "弧引用缺失节点 / Arc references a missing node");
        }
        return Results.Ok(new NetworkGraph<N, A>(
            nodes.ToList(), arcs.ToList(), access, nodeIds, arcIds, arcFrom, arcTo, false));
    }

    /// <summary>
    /// 创建内置物理量图 / Create a physical-quantity graph.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="nodes">节点 / Nodes.</param>
    /// <param name="arcs">弧 / Arcs.</param>
    /// <returns>图或错误 / The graph, or an error.</returns>
    public static Result<NetworkGraph<NetworkNode<V>, NetworkArc<V>>, ErrorCode, Error<ErrorCode>>
        Invoke<V>(IReadOnlyList<NetworkNode<V>> nodes, IReadOnlyList<NetworkArc<V>> arcs)
        where V : struct, IRealNumber<V> {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(arcs);
        return NetworkGraph<NetworkNode<V>, NetworkArc<V>>.Invoke(
            nodes,
            arcs,
            new NetworkGraphAccess<NetworkNode<V>, NetworkArc<V>>(
                NodeId: node => node.Id,
                ArcId: arc => arc.Id,
                From: arc => arc.From,
                To: arc => arc.To));
    }

    /// <summary>查询弧在快照中的位置 / The arc's position in the snapshot.</summary>
    /// <param name="arc">弧载荷 / Arc payload.</param>
    /// <returns>位置，未找到为 -1 / The position, or -1.</returns>
    private int ArcPositionOf(A arc) => _arcs.IndexOf(arc);

    /// <summary>按方向键构造邻接索引 / Build an adjacency index keyed by one endpoint.</summary>
    /// <param name="keys">端点标识快照 / Endpoint identity snapshot.</param>
    /// <returns>邻接索引 / The adjacency index.</returns>
    private Dictionary<NetworkNodeId, List<A>> BuildAdjacency(List<NetworkNodeId> keys) {
        var index = new Dictionary<NetworkNodeId, List<A>>();
        for (int position = 0; position < keys.Count; position++) {
            NetworkNodeId key = keys[position];
            if (!index.TryGetValue(key, out List<A>? bucket)) {
                bucket = new List<A>();
                index[key] = bucket;
            }
            bucket.Add(_arcs[position]);
        }
        return index;
    }
}
