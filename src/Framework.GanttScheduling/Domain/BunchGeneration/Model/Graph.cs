#nullable enable

using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchGeneration.Model;
/// <summary>
/// 节点基类 / Node base class
/// </summary>
public abstract class Node {
    /// <summary>
    /// 节点构造 / Node constructor
    /// </summary>
    /// <param name="index">节点索引 / Node index</param>
    protected Node(ulong index) {
        Index = index;
    }

    /// <summary>根节点索引 / Root node index</summary>
    internal static readonly ulong RootIndex = 0UL;

    /// <summary>终止节点索引 / End node index</summary>
    internal static readonly ulong EndIndex = ulong.MaxValue;

    /// <summary>节点索引 / Node index</summary>
    public ulong Index { get; }

    /// <summary>时间 / Time</summary>
    public abstract DateTimeOffset Time { get; }
}

/// <summary>
/// 根节点 / Root node
/// </summary>
public sealed class RootNode : Node {
    /// <summary>共享实例 / Shared instance</summary>
    public static readonly RootNode Instance = new();

    private RootNode() : base(RootIndex) { }

    /// <inheritdoc/>
    public override DateTimeOffset Time => DateTimeOffset.MinValue;

    /// <inheritdoc/>
    public override string ToString() => "Root";
}

/// <summary>
/// 终止节点 / End node
/// </summary>
public sealed class EndNode : Node {
    /// <summary>共享实例 / Shared instance</summary>
    public static readonly EndNode Instance = new();

    private EndNode() : base(EndIndex) { }

    /// <inheritdoc/>
    public override DateTimeOffset Time => DateTimeOffset.MaxValue;

    /// <inheritdoc/>
    public override string ToString() => "End";
}

/// <summary>
/// 任务节点 / Task node
/// </summary>
/// <typeparam name="T">任务类型 / Task type</typeparam>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public sealed class TaskNode<T, E, A> : Node
    where T : IAbstractTask<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    /// <summary>
    /// 任务节点构造 / Task node constructor
    /// </summary>
    /// <param name="task">任务 / The task</param>
    /// <param name="time">时间 / Time</param>
    /// <param name="index">节点索引 / Node index</param>
    public TaskNode(T task, DateTimeOffset time, ulong index) : base(index) {
        Task = task;
        Time = time;
    }

    /// <summary>任务 / The task</summary>
    public T Task { get; }

    /// <inheritdoc/>
    public override DateTimeOffset Time { get; }

    /// <inheritdoc/>
    public override string ToString() => Task?.ToString() ?? $"TaskNode({Index})";
}

/// <summary>
/// 边 / Edge
/// </summary>
/// <param name="From">起始节点 / Source node</param>
/// <param name="To">目标节点 / Target node</param>
public sealed record Edge(Node From, Node To) {
    /// <inheritdoc/>
    public override string ToString() => $"{From} -> {To}";
}

/// <summary>
/// 有向图 / Directed graph
/// </summary>
/// <remarks>
/// 任务束生成图模型。支持节点和边的增删查改以及反转操作。
/// Bunch generation graph model. Supports node/edge CRUD and reverse operations.
/// </remarks>
public class Graph {
    private readonly Dictionary<ulong, Node> _nodes = new();
    private readonly Dictionary<Node, HashSet<Edge>> _edges = new();

    /// <summary>所有节点 / All nodes</summary>
    public IReadOnlyDictionary<ulong, Node> Nodes => _nodes;

    /// <summary>所有边映射 / All edge mappings</summary>
    public IReadOnlyDictionary<Node, IReadOnlySet<Edge>> Edges =>
        _edges.ToDictionary(kv => kv.Key, kv => (IReadOnlySet<Edge>)kv.Value);

    /// <summary>
    /// 添加节点 / Add node
    /// </summary>
    /// <param name="node">节点 / The node</param>
    public void Put(Node node) => _nodes[node.Index] = node;

    /// <summary>
    /// 添加边 / Add edge
    /// </summary>
    /// <param name="from">起始节点 / Source node</param>
    /// <param name="to">目标节点 / Target node</param>
    public void Put(Node from, Node to) {
        if (!_edges.TryGetValue(from, out HashSet<Edge>? edgeSet)) {
            edgeSet = new HashSet<Edge>();
            _edges[from] = edgeSet;
        }
        edgeSet.Add(new Edge(from, to));
    }

    /// <summary>
    /// 按索引获取节点 / Get node by index
    /// </summary>
    /// <param name="index">节点索引 / Node index</param>
    /// <returns>节点，若不存在则为 null / The node, or null if not found</returns>
    public Node? GetNode(ulong index) => _nodes.TryGetValue(index, out Node? node) ? node : null;

    /// <summary>
    /// 获取节点的出边 / Get outgoing edges for node
    /// </summary>
    /// <param name="node">节点 / The node</param>
    /// <returns>出边集合 / Set of outgoing edges</returns>
    public IReadOnlySet<Edge> GetEdges(Node node) => _edges.TryGetValue(node, out HashSet<Edge>? edges) ? edges : new HashSet<Edge>();

    /// <summary>
    /// 检查两节点是否相连 / Check if two nodes are connected
    /// </summary>
    /// <param name="from">起始节点 / Source node</param>
    /// <param name="to">目标节点 / Target node</param>
    /// <returns>是否相连 / Whether connected</returns>
    public bool Connected(Node from, Node to) => _edges.TryGetValue(from, out HashSet<Edge>? edges) && edges.Contains(new Edge(from, to));

    /// <summary>
    /// 反转图 / Reverse the graph
    /// </summary>
    /// <returns>反转后的新图 / A new reversed graph</returns>
    public Graph Reverse() {
        var reversed = new Graph();
        foreach (KeyValuePair<ulong, Node> kv in _nodes) {
            reversed._nodes[kv.Key] = kv.Value;
        }
        foreach ((Node? from, HashSet<Edge>? edges) in _edges) {
            foreach (Edge edge in edges) {
                Node rfrom = edge.To is EndNode ? RootNode.Instance : edge.To;
                Node rto = from is RootNode ? EndNode.Instance : from;
                if (!reversed._edges.TryGetValue(rfrom, out HashSet<Edge>? rset)) {
                    rset = new HashSet<Edge>();
                    reversed._edges[rfrom] = rset;
                }
                rset.Add(new Edge(rfrom, rto));
            }
        }
        return reversed;
    }
}
