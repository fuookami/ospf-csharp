// 网络调度基础设施：节点、代价与错误 / Network-scheduling infrastructure: nodes, costs and errors.
// 与`network-scheduling-infrastructure` 的（9 行）、（19 行）、
// , and.
//
// **这是 P7 的骨架批次**：`network-scheduling` 在是 12 个模块、118 个源文件的独立框架，C# 侧此前**完全没有对应项目**。
// 这里建立项目、接入解决方案，并实现依赖已闭合的最小一层；其余模块（flow／design／allocation／vrp／resilience／route…）
// 逐批推进。不宣称任何跨语言互通或性能结论——这里只有单元测试。
// **This is P7's skeleton batch**: in the implementation `network-scheduling` is a standalone framework of 12 modules and 118
// source files, and the C# side had **no corresponding project at all**. creates the project, wires it into
// the solution and ports the smallest dependency-closed layer; the remaining modules (flow / design / allocation / vrp /
// resilience / route ...) follow batch by batch. No cross-language interoperability or performance claim is made — this
// batch ships unit tests only.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;

/// <summary>
/// 网络调度非法参数失败 / Network-scheduling illegal-argument failure.
/// </summary>
public static class NetworkSchedulingErrors {
    /// <summary>
    /// 构造非法参数失败 / Build an illegal-argument failure.
    /// </summary>
    /// <typeparam name="T">结果载荷类型 / Result payload type.</typeparam>
    /// <param name="message">失败说明 / Failure message.</param>
    /// <returns>失败结果 / A failed result.</returns>
    public static Result<T, ErrorCode, Error<ErrorCode>> Failure<T>(string message) =>
        Results.Failed<T>(new Err<ErrorCode>(ErrorCode.IllegalArgument, message));

    /// <summary>
    /// 构造一个不带返回值的网络调度失败 / Build a value-less network-scheduling failure.
    /// </summary>
    /// <remarks>
    /// 校验器只关心"通过或失败"，不需要返回载荷；用 `Failure&lt;Success&gt;` 观感上像是要返回什么，单独给一个无泛型的入口
    /// 更贴近`Try` 语义。
    /// Validators only care about "pass or fail" and carry no payload; spelling it `Failure&lt;Success&gt;` reads as if
    /// something is returned, so a non-generic entry mirrors its `Try` more closely.
    /// </remarks>
    /// <param name="message">失败说明 / Failure message.</param>
    /// <returns>失败结果 / A failed result.</returns>
    public static Try TryFailure(string message) =>
        Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.IllegalArgument, message));
}

/// <summary>
/// 网络节点稳定标识 / Stable network node identifier.
/// </summary>
/// <param name="Value">标识文本 / Identifier text.</param>
public sealed record NetworkNodeId(string Value) : IComparable<NetworkNodeId> {
    /// <inheritdoc/>
    public int CompareTo(NetworkNodeId? other) =>
        other is null ? 1 : string.CompareOrdinal(Value, other.Value);

    /// <inheritdoc/>
    public override string ToString() => Value;
}

/// <summary>
/// 可归一化的网络成本 / Normalizable network cost.
/// </summary>
/// <remarks>
/// **成本允许为负**：负成本在网络问题里是合法语义（补贴、回收、逆向流量），把负数当错误会让这类建模无法表达。因此构造器
/// 只做包装，不做符号校验。
/// **A negative cost is allowed**: negative costs are legitimate in network problems (subsidies, recycling, reverse flows),
/// and rejecting them would make such models inexpressible. The constructor therefore only wraps and never checks the sign.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class NetworkCost<V>(Quantity<V> quantity) where V : struct, IRealNumber<V> {
    /// <summary>网络成本物理量 / Network cost quantity.</summary>
    public Quantity<V> Quantity { get; } = quantity;

    /// <summary>
    /// 创建网络成本 / Create a network cost.
    /// </summary>
    /// <param name="quantity">网络成本物理量 / Network cost quantity.</param>
    /// <returns>网络成本 / The network cost.</returns>
    public static Result<NetworkCost<V>, ErrorCode, Error<ErrorCode>> Invoke(Quantity<V> quantity) {
        ArgumentNullException.ThrowIfNull(quantity);
        return Results.Ok(new NetworkCost<V>(quantity));
    }
}

/// <summary>
/// 不依赖全局索引的网络节点 / Network node independent of global indices.
/// </summary>
/// <remarks>
/// **节点不持有全局索引**：索引会随图结构变化而失效，而节点标识必须是稳定的。下游按标识而非按位置寻址，图重排不会让
/// 已有引用错位。
/// **A node holds no global index**: indices become stale as the graph changes, while a node's identity must be stable.
/// Downstream addresses nodes by identifier rather than position, so a graph reordering never misplaces existing references.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="Id">稳定标识 / Stable identifier.</param>
/// <param name="Attributes">通用节点属性 / Generic node attributes.</param>
public sealed class NetworkNode<V>(NetworkNodeId Id, IReadOnlyDictionary<string, Quantity<V>> Attributes)
    where V : struct, IRealNumber<V> {
    /// <summary>稳定标识 / Stable identifier.</summary>
    public NetworkNodeId Id { get; } = Id;

    /// <summary>
    /// 通用节点属性 / Generic node attributes.
    /// </summary>
    /// <remarks>
    /// 构造时**复制一份**：调用方后续修改传入的字典不会悄悄改掉节点属性，节点因此是值稳定的。
 /// **Copied at construction**
    /// the node's attributes, so the node is value-stable.
    /// </remarks>
    public IReadOnlyDictionary<string, Quantity<V>> Attributes { get; } =
        Attributes.ToDictionary(pair => pair.Key, pair => pair.Value);

    /// <summary>
    /// 创建网络节点 / Create a network node.
    /// </summary>
    /// <remarks>
    /// **空标识被拒绝**：节点标识是图的寻址键，空标识会让多个节点互相顶替，且这种错误在后续求最短路径时表现为"路径凭空
    /// 缩短"而不是报错。
    /// **A blank identifier is rejected**: the identifier is the graph's addressing key, and a blank one lets several nodes
    /// overwrite each other — an error that later shows up as "paths that are mysteriously shorter" rather than as a failure.
    /// </remarks>
    /// <param name="id">稳定标识 / Stable identifier.</param>
    /// <param name="attributes">通用节点属性 / Generic node attributes.</param>
    /// <returns>节点或校验失败 / The node, or a validation failure.</returns>
    public static Result<NetworkNode<V>, ErrorCode, Error<ErrorCode>> Invoke(
        NetworkNodeId id,
        IReadOnlyDictionary<string, Quantity<V>>? attributes = null) {
        ArgumentNullException.ThrowIfNull(id);
        if (string.IsNullOrWhiteSpace(id.Value)) {
            return NetworkSchedulingErrors.Failure<NetworkNode<V>>(
                "创建网络节点失败：节点 ID 不能为空 / Failed to create network node: node ID cannot be blank");
        }
        return Results.Ok(new NetworkNode<V>(id, attributes ?? new Dictionary<string, Quantity<V>>()));
    }
}
