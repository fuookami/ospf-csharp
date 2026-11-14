// VRPTW 模型：客户、起止仓库与路线停靠点 / VRPTW model: customer, depot and route stop.
// 与固定 Kotlin 基线 `Customer.kt`（76 行）、`Depot.kt`（16 行）、`RouteStop.kt`（26 行）对应。
// Mirrors the pinned baseline's `Customer.kt`, `Depot.kt` and `RouteStop.kt`.

using System;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Infrastructure;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Model;

/// <summary>
/// 客户稳定标识 / Stable customer identifier.
/// </summary>
/// <param name="Value">标识文本 / Identifier text.</param>
public sealed record CustomerId(string Value) : IComparable<CustomerId> {
    /// <inheritdoc/>
    public int CompareTo(CustomerId? other) =>
        other is null ? 1 : string.CompareOrdinal(Value, other.Value);

    /// <inheritdoc/>
    public override string ToString() => Value;
}

/// <summary>
/// VRPTW 客户 / VRPTW customer.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="Id">客户稳定标识 / Stable customer identifier.</param>
/// <param name="Node">网络节点 / Network node.</param>
/// <param name="Demand">需求 / Demand.</param>
/// <param name="TimeWindow">服务时间窗 / Service time window.</param>
/// <param name="ServiceTime">服务时长 / Service duration.</param>
public sealed class Customer<V>(
    CustomerId Id,
    NetworkNode<V> Node,
    Quantity<V> Demand,
    ServiceTimeWindow TimeWindow,
    TimeSpan ServiceTime) where V : struct, IRealNumber<V> {
    /// <summary>客户稳定标识 / Stable customer identifier.</summary>
    public CustomerId Id { get; } = Id;

    /// <summary>网络节点 / Network node.</summary>
    public NetworkNode<V> Node { get; } = Node;

    /// <summary>需求 / Demand.</summary>
    public Quantity<V> Demand { get; } = Demand;

    /// <summary>服务时间窗 / Service time window.</summary>
    public ServiceTimeWindow TimeWindow { get; } = TimeWindow;

    /// <summary>服务时长 / Service duration.</summary>
    public TimeSpan ServiceTime { get; } = ServiceTime;

    /// <summary>
    /// 创建客户 / Create a customer.
    /// </summary>
    /// <remarks>
    /// 三条校验按此顺序：标识非空 → 需求非负 → 服务时长非负。**标识检查最先做**，因为后续两条的失败消息都要点名
    /// 是哪个客户，标识为空时那句话没有意义。
    /// Three checks in this order: non-blank identity → non-negative demand → non-negative service time. **The identity
    /// check comes first** because the other two messages name the offending customer, which is meaningless without one.
    /// </remarks>
    /// <param name="id">客户稳定标识 / Stable customer identifier.</param>
    /// <param name="node">网络节点 / Network node.</param>
    /// <param name="demand">非负需求 / Non-negative demand.</param>
    /// <param name="timeWindow">服务时间窗 / Service time window.</param>
    /// <param name="serviceTime">非负服务时长 / Non-negative service duration.</param>
    /// <returns>客户或校验失败 / The customer, or a validation failure.</returns>
    public static Result<Customer<V>, ErrorCode, Error<ErrorCode>> Invoke(
        CustomerId id,
        NetworkNode<V> node,
        Quantity<V> demand,
        ServiceTimeWindow timeWindow,
        TimeSpan serviceTime) {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(demand);
        ArgumentNullException.ThrowIfNull(timeWindow);

        if (string.IsNullOrWhiteSpace(id.Value)) {
            return NetworkSchedulingErrors.Failure<Customer<V>>(
                "创建客户失败：客户 ID 不能为空 / Failed to create customer: customer ID cannot be blank");
        }
        if (demand.Value.CompareTo(demand.Value.Constants.Zero) < 0) {
            return NetworkSchedulingErrors.Failure<Customer<V>>(
                $"创建客户失败：客户 {id.Value} 的需求不能为负 / Failed to create customer: demand of customer {id.Value} "
                + "cannot be negative");
        }
        if (serviceTime < TimeSpan.Zero) {
            return NetworkSchedulingErrors.Failure<Customer<V>>(
                $"创建客户失败：客户 {id.Value} 的服务时间不能为负 / Failed to create customer: service time of customer "
                + $"{id.Value} cannot be negative");
        }
        return Results.Ok(new Customer<V>(id, node, demand, timeWindow, serviceTime));
    }
}

/// <summary>
/// VRPTW 起止仓库 / VRPTW start or end depot.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="Node">网络节点 / Network node.</param>
/// <param name="TimeWindow">仓库开放时间窗 / Depot opening time window.</param>
public sealed record Depot<V>(NetworkNode<V> Node, ServiceTimeWindow TimeWindow)
    where V : struct, IRealNumber<V>;

/// <summary>
/// 路线停靠点的完整资源状态 / Complete resource state at a route stop.
/// </summary>
/// <remarks>
/// 三个时刻（到达、服务开始、离开）**分别保存而不是只存一个**：它们之间的差值就是等待与服务时长，下游定价与可行性校验都要
/// 读这些差值；只存离开时刻会迫使每个使用点重新推算等待时间，而推算规则一旦不一致就会出现"校验通过但排程超时"。
/// The three instants (arrival, service start, departure) are **stored separately rather than collapsed into one**: their
/// differences are exactly the waiting time and service duration, and downstream pricing and feasibility checks read those
/// differences. Storing only departure would force every use site to re-derive waiting time, and inconsistent derivations
/// produce "validated yet overtime" schedules.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="NodeId">网络节点标识 / Network node identity.</param>
/// <param name="CustomerId">客户标识，仓库处为 null / Customer identity, null at depots.</param>
/// <param name="Arrival">到达绝对时刻 / Absolute arrival instant.</param>
/// <param name="ServiceStart">服务开始绝对时刻 / Absolute service-start instant.</param>
/// <param name="Departure">离开绝对时刻 / Absolute departure instant.</param>
/// <param name="AccumulatedLoad">累计负载 / Accumulated load.</param>
public sealed record RouteStop<V>(
    NetworkNodeId NodeId,
    CustomerId? CustomerId,
    DateTimeOffset Arrival,
    DateTimeOffset ServiceStart,
    DateTimeOffset Departure,
    Quantity<V> AccumulatedLoad) where V : struct, IRealNumber<V>;
