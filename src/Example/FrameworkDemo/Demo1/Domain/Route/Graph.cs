#nullable enable

using System.Collections.Generic;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo1.Domain.Route;

/// <summary>
/// 网络节点的基类（通过唯一 ID 标识并连接到边）。Base class for network nodes, identified by a unique ID and connected to edges.
/// </summary>
public abstract class Node {
    /// <summary>节点唯一标识符 / Unique node identifier</summary>
    public UInt64 Id { get; }
    /// <summary>在节点列表中的索引 / Index within the node list</summary>
    public int Index { get; }
    /// <summary>连接的边列表 / Connected edges</summary>
    public List<Edge> Edges { get; } = new();

    protected Node(UInt64 id, int index) {
        Id = id;
        Index = index;
    }

    /// <summary>添加连接边 / Add a connected edge</summary>
    public void Add(Edge edge) {
        Edges.Add(edge);
    }

    public abstract override string ToString();
}

/// <summary>
/// 网络中可以承载服务流量的传输节点。A transit node in the network that can carry service traffic.
/// </summary>
public sealed class NormalNode : Node {
    public NormalNode(UInt64 id, int index) : base(id, index) { }
    public override string ToString() => $"N{Id}";
}

/// <summary>
/// 从网络消耗带宽的具有特定需求的终端节点。A terminal node that consumes bandwidth from the network with a specific demand.
/// </summary>
/// <param name="Id">节点 ID / Node ID</param>
/// <param name="Demand">带宽需求 / Bandwidth demand</param>
public sealed class ClientNode : Node {
    /// <summary>带宽需求 / Bandwidth demand</summary>
    public UInt64 Demand { get; }

    public ClientNode(UInt64 id, int index, UInt64 demand) : base(id, index) {
        Demand = demand;
    }

    public override string ToString() => $"C{Id}";
}

/// <summary>
/// 两个节点之间的有向边（具有带宽容量和单位成本）。A directed edge between two nodes with bandwidth capacity and per-unit cost.
/// </summary>
public sealed class Edge {
    /// <summary>源节点 / Source node</summary>
    public Node From { get; }
    /// <summary>目标节点 / Destination node</summary>
    public Node To { get; }
    /// <summary>最大带宽 / Maximum bandwidth</summary>
    public UInt64 MaxBandwidth { get; }
    /// <summary>单位带宽成本 / Cost per unit bandwidth</summary>
    public UInt64 CostPerBandwidth { get; }
    /// <summary>在边列表中的索引 / Index within the edge list</summary>
    public int Index { get; }

    public Edge(Node from, Node to, UInt64 maxBandwidth, UInt64 costPerBandwidth, int index) {
        From = from;
        To = to;
        MaxBandwidth = maxBandwidth;
        CostPerBandwidth = costPerBandwidth;
        Index = index;
    }

    public override string ToString() => $"E({From},{To})";
}

/// <summary>
/// 持有所有节点和边的网络图结构容器。Container for the network graph structure holding all nodes and edges.
/// </summary>
/// <param name="Nodes">节点列表 / Node list</param>
/// <param name="Edges">边列表 / Edge list</param>
public sealed record SspGraph(
    IReadOnlyList<Node> Nodes,
    IReadOnlyList<Edge> Edges);
