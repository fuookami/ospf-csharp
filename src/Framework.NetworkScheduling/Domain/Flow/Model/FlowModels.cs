// 网络流弧、节点与商品 / Flow arc, node and commodity.
// 与固定 Kotlin 基线 `model/FlowArc.kt`（2985 字节）、`model/FlowNode.kt`（2312 字节）、`model/FlowCommodity.kt`（1688 字节）、
// `model/SupplyDemand.kt`（2369 字节）对应。
// Mirrors the pinned baseline's `FlowArc.kt`, `FlowNode.kt`, `FlowCommodity.kt`, `SupplyDemand.kt`.
//
// 这一层是**纯领域数据**，不持有任何模型注册态：流弧不知道自己在哪个模型里、流节点不知道有哪些变量引用它。注册态在上下文层，
// 因此同一份流网络可以被多个模型复用、被并行求解、被重放。
// This layer is **pure domain data** and holds no model registration state: a flow arc does not know which model it lives in, a flow
// node does not know which variables reference it. Registration state lives in the context layer, so one flow network can be reused
// by several models, solved in parallel, and replayed.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Domain.Flow.Model;

/// <summary>
/// 网络流弧，不持有任何模型注册状态 / Flow arc without model registration state.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class FlowArc<V> where V : struct, IRealNumber<V> {
    private FlowArc(NetworkArc<V> networkArc, CapacityBounds<V> capacity) {
        NetworkArc = networkArc;
        Capacity = capacity;
    }

    /// <summary>业务服务弧 / Business service arc.</summary>
    public NetworkArc<V> NetworkArc { get; }

    /// <summary>运行容量；设计问题中上界是有限建设包络 / Operating bounds; the upper bound is a finite construction envelope.</summary>
    public CapacityBounds<V> Capacity { get; }

    /// <summary>弧起点 / Arc origin.</summary>
    public NetworkNodeId From => NetworkArc.From;

    /// <summary>弧终点 / Arc destination.</summary>
    public NetworkNodeId To => NetworkArc.To;

    /// <summary>弧成本 / Arc cost.</summary>
    public Quantity<V> Cost => NetworkArc.Cost;

    /// <summary>通用弧属性 / Generic arc attributes.</summary>
    public IReadOnlyDictionary<string, Quantity<V>> Attributes => NetworkArc.Attributes;

    /// <summary>
    /// 包装网络弧与容量上下界 / Wrap a network arc and its capacity bounds.
    /// </summary>
    /// <param name="networkArc">业务服务弧 / Business service arc.</param>
    /// <param name="capacity">服务容量 / Service capacity bounds.</param>
    /// <returns>网络流弧或校验错误 / The flow arc, or a validation error.</returns>
    public static Result<FlowArc<V>, ErrorCode, Error<ErrorCode>> Invoke(
        NetworkArc<V> networkArc,
        CapacityBounds<V> capacity) {
        ArgumentNullException.ThrowIfNull(networkArc);
        ArgumentNullException.ThrowIfNull(capacity);
        return Results.Ok(new FlowArc<V>(networkArc, capacity));
    }

    /// <summary>
    /// 创建网络流弧 / Create a network-flow arc.
    /// </summary>
    /// <param name="from">起点 / Origin.</param>
    /// <param name="to">终点 / Destination.</param>
    /// <param name="cost">单位流量费用 / Unit-flow cost.</param>
    /// <param name="capacity">服务容量 / Service capacity bounds.</param>
    /// <param name="attributes">通用弧属性 / Generic arc attributes.</param>
    /// <param name="id">稳定弧标识，平行弧必须显式指定 / Stable identity, required explicitly for parallel arcs.</param>
    /// <returns>网络流弧或校验错误 / The flow arc, or a validation error.</returns>
    public static Result<FlowArc<V>, ErrorCode, Error<ErrorCode>> Invoke(
        NetworkNodeId from,
        NetworkNodeId to,
        Quantity<V> cost,
        CapacityBounds<V> capacity,
        IReadOnlyDictionary<string, Quantity<V>>? attributes = null,
        NetworkArcId? id = null) {
        Result<NetworkArc<V>, ErrorCode, Error<ErrorCode>> arc =
            NetworkArc<V>.Invoke(from, to, cost, attributes, id);
        if (arc is Failed<NetworkArc<V>, ErrorCode, Error<ErrorCode>> arcFailure) {
            return Results.Failed<FlowArc<V>>(arcFailure.Error);
        }
        return Invoke(arc.Value, capacity);
    }
}

/// <summary>
/// 节点供需绑定；正值为供给，负值为需求 / Node balance binding; positive values are supply, negative values are demand.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class SupplyDemand<V> where V : struct, IRealNumber<V> {
    private SupplyDemand(NetworkNodeId nodeId, NodeBalance<V> balance) {
        NodeId = nodeId;
        Balance = balance;
    }

    /// <summary>节点标识 / Node identifier.</summary>
    public NetworkNodeId NodeId { get; }

    /// <summary>节点净供需 / Node net balance.</summary>
    public NodeBalance<V> Balance { get; }

    /// <summary>净供需量 / Net balance quantity.</summary>
    public Quantity<V> Net => Balance.Net;

    /// <summary>
    /// 绑定节点供需量 / Bind a node to a supply-demand balance.
    /// </summary>
    /// <remarks>
    /// **空白节点标识必须被拒绝**。空白标识会通过后续所有的字典查找（作为一个键它完全合法），于是这份供需被静静地挂在一个
    /// 不存在的节点上，守恒约束少一条，求解器只会返回一个"看起来可行"的解。
    /// **A blank node identity must be rejected.** A blank identity passes every later dictionary lookup (it is a perfectly legal
    /// key), so the balance is silently attached to a node that does not exist, one conservation constraint goes missing, and the
    /// solver returns a plausible-looking feasible answer.
    /// </remarks>
    /// <param name="nodeId">节点标识 / Node identifier.</param>
    /// <param name="balance">节点净供需 / Node net balance.</param>
    /// <returns>供需绑定或校验错误 / The binding, or a validation error.</returns>
    public static Result<SupplyDemand<V>, ErrorCode, Error<ErrorCode>> Invoke(
        NetworkNodeId nodeId,
        NodeBalance<V> balance) {
        ArgumentNullException.ThrowIfNull(nodeId);
        ArgumentNullException.ThrowIfNull(balance);
        return string.IsNullOrWhiteSpace(nodeId.Value)
            ? NetworkSchedulingErrors.Failure<SupplyDemand<V>>(
                "绑定供需失败：节点 ID 不能为空 / Failed to bind supply-demand: node ID cannot be blank")
            : Results.Ok(new SupplyDemand<V>(nodeId, balance));
    }

    /// <summary>
    /// 直接使用净供需量绑定节点 / Bind a node directly to a net balance quantity.
    /// </summary>
    /// <param name="nodeId">节点标识 / Node identifier.</param>
    /// <param name="net">节点净供需量 / Node net balance quantity.</param>
    /// <returns>供需绑定或校验错误 / The binding, or a validation error.</returns>
    public static Result<SupplyDemand<V>, ErrorCode, Error<ErrorCode>> Invoke(
        NetworkNodeId nodeId,
        Quantity<V> net) {
        Result<NodeBalance<V>, ErrorCode, Error<ErrorCode>> balance = NodeBalance<V>.Invoke(net);
        return balance is Failed<NodeBalance<V>, ErrorCode, Error<ErrorCode>> balanceFailure
            ? Results.Failed<SupplyDemand<V>>(balanceFailure.Error)
            : Invoke(nodeId, balance.Value);
    }
}

/// <summary>
/// 网络流节点 / Network-flow node.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class FlowNode<V> where V : struct, IRealNumber<V> {
    private FlowNode(NetworkNode<V> networkNode, IReadOnlyDictionary<string, NodeBalance<V>> balances) {
        NetworkNode = networkNode;
        Balances = balances.ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    /// <summary>通用网络节点 / Generic network node.</summary>
    public NetworkNode<V> NetworkNode { get; }

    /// <summary>按商品标识索引的节点供需快照 / Node-balance snapshot indexed by commodity identity.</summary>
    public IReadOnlyDictionary<string, NodeBalance<V>> Balances { get; }

    /// <summary>稳定节点标识 / Stable node identity.</summary>
    public NetworkNodeId Id => NetworkNode.Id;

    /// <summary>节点属性 / Node attributes.</summary>
    public IReadOnlyDictionary<string, Quantity<V>> Attributes => NetworkNode.Attributes;

    /// <summary>
    /// 获取指定商品的节点供需 / Get the node balance for a commodity.
    /// </summary>
    /// <param name="commodityId">商品标识 / Commodity identity.</param>
    /// <returns>节点供需，未声明时为 null / The node balance, or null when absent.</returns>
    public NodeBalance<V>? BalanceOf(string commodityId) {
        ArgumentNullException.ThrowIfNull(commodityId);
        return Balances.TryGetValue(commodityId, out NodeBalance<V>? balance) ? balance : null;
    }

    /// <summary>
    /// 包装通用网络节点 / Wrap a generic network node.
    /// </summary>
    /// <param name="networkNode">通用网络节点 / Generic network node.</param>
    /// <param name="balances">按商品标识索引的供需 / Balances indexed by commodity identity.</param>
    /// <returns>流节点或校验错误 / The flow node, or a validation error.</returns>
    public static Result<FlowNode<V>, ErrorCode, Error<ErrorCode>> Invoke(
        NetworkNode<V> networkNode,
        IReadOnlyDictionary<string, NodeBalance<V>>? balances = null) {
        ArgumentNullException.ThrowIfNull(networkNode);
        return Results.Ok(new FlowNode<V>(
            networkNode, balances ?? new Dictionary<string, NodeBalance<V>>()));
    }
}

/// <summary>
/// 网络流商品及其节点供需 / Flow commodity and its node balances.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class FlowCommodity<V> where V : struct, IRealNumber<V> {
    private FlowCommodity(string id, IReadOnlyList<SupplyDemand<V>> supplyDemands) {
        Id = id;
        SupplyDemands = supplyDemands.ToList();
    }

    /// <summary>商品标识 / Commodity identifier.</summary>
    public string Id { get; }

    /// <summary>商品节点供需 / Commodity node balances.</summary>
    public IReadOnlyList<SupplyDemand<V>> SupplyDemands { get; }

    /// <summary>
    /// 创建网络流商品 / Create a flow commodity.
    /// </summary>
    /// <remarks>
    /// **同一节点的供需不得重复**。重复条目会让守恒约束被加两次（或两次相抵），净供需因此悄悄翻倍或归零，而约束数量、模型规模、
    /// 求解状态全都正常。这类错误只能靠构造期去重检查拦住。
    /// **One node must not carry two balances.** A duplicate entry adds the conservation constraint twice (or nets it to zero), so the
    /// net supply silently doubles or vanishes while the constraint count, model size and solve status all look normal. Only a
    /// construction-time duplicate check catches this.
    /// </remarks>
    /// <param name="id">商品标识 / Commodity identifier.</param>
    /// <param name="supplyDemands">节点供需 / Node balances.</param>
    /// <returns>商品或校验错误 / The commodity, or a validation error.</returns>
    public static Result<FlowCommodity<V>, ErrorCode, Error<ErrorCode>> Invoke(
        string id,
        IReadOnlyList<SupplyDemand<V>>? supplyDemands = null) {
        ArgumentNullException.ThrowIfNull(id);
        IReadOnlyList<SupplyDemand<V>> demands = supplyDemands ?? Array.Empty<SupplyDemand<V>>();
        if (string.IsNullOrWhiteSpace(id)) {
            return NetworkSchedulingErrors.Failure<FlowCommodity<V>>(
                "创建流商品失败：商品 ID 不能为空 / Failed to create flow commodity: commodity ID cannot be blank");
        }
        if (demands.GroupBy(demand => demand.NodeId).Any(group => group.Count() > 1)) {
            return NetworkSchedulingErrors.Failure<FlowCommodity<V>>(
                "创建流商品失败：节点供需重复 / Failed to create flow commodity: duplicate node balance");
        }
        return Results.Ok(new FlowCommodity<V>(id, demands));
    }
}
