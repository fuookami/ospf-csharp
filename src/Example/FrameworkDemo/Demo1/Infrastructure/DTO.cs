#nullable enable

using System.Collections.Generic;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo1.Infrastructure;

/// <summary>
/// 数据传输对象，表示具有源/目标节点、带宽和成本的网络边。Data transfer object representing a network edge with source/destination nodes, bandwidth, and cost.
/// </summary>
/// <param name="FromNodeId">源节点 ID / Source node ID</param>
/// <param name="ToNodeId">目标节点 ID / Destination node ID</param>
/// <param name="MaxBandwidth">最大带宽 / Maximum bandwidth</param>
/// <param name="CostPerBandwidth">单位带宽成本 / Cost per unit bandwidth</param>
public sealed record EdgeDTO(
    UInt64 FromNodeId,
    UInt64 ToNodeId,
    UInt64 MaxBandwidth,
    UInt64 CostPerBandwidth);

/// <summary>
/// 数据传输对象，表示连接到普通节点的具有带宽需求的客户端节点。Data transfer object representing a client node attached to a normal node with a bandwidth demand.
/// </summary>
/// <param name="Id">客户端节点 ID / Client node ID</param>
/// <param name="NormalNodeId">关联的普通节点 ID / Associated normal node ID</param>
/// <param name="Demand">带宽需求 / Bandwidth demand</param>
public sealed record ClientNodeDTO(
    UInt64 Id,
    UInt64 NormalNodeId,
    UInt64 Demand);

/// <summary>
/// SSP 问题的聚合输入数据（包括服务成本、节点数、边和客户端节点）。Aggregated input data for the SSP problem including service cost, node count, edges, and client nodes.
/// </summary>
/// <param name="ServiceCost">服务成本 / Service cost</param>
/// <param name="NormalNodeAmount">普通节点数量 / Number of normal nodes</param>
/// <param name="Edges">边列表 / Edge list</param>
/// <param name="ClientNodes">客户端节点列表 / Client node list</param>
public sealed record SspInput(
    UInt64 ServiceCost,
    UInt64 NormalNodeAmount,
    IReadOnlyList<EdgeDTO> Edges,
    IReadOnlyList<ClientNodeDTO> ClientNodes);

/// <summary>
/// 包含计算的服务路径（节点 ID 列表）的输出。Output containing the computed service paths as lists of node IDs.
/// </summary>
/// <param name="Links">服务路径列表 / Service path list</param>
public sealed record SspOutput(
    IReadOnlyList<IReadOnlyList<UInt64>> Links);
