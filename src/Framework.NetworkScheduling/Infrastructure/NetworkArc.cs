// 网络调度基础设施：弧、容量、流量、供需与容量上下界 / Network-scheduling infrastructure: arcs, capacity, flow, balance
// and capacity bounds.
// 与固定 Kotlin 基线 `network-scheduling-infrastructure` 的 `NetworkArc.kt`（51 行）、`Capacity.kt`（24 行）、
// `Flow.kt`（24 行）、`NodeBalance.kt`（23 行）、`CapacityBounds.kt`（49 行）对应。
// Mirrors the pinned baseline's `NetworkArc.kt`, `Capacity.kt`, `Flow.kt`, `NodeBalance.kt` and `CapacityBounds.kt`.
//
// 这一层的特点：**每个类型都是"带校验的构造器"**，校验失败一律返回 `IllegalArgument`，而不是抛出。这是基线的统一约定，
// 也是下游能把多个构造失败汇总成一份报告的前提。
// The pattern here: **every type is a "validating constructor"** whose failures all return `IllegalArgument` rather than
// throwing. That is the baseline's convention and the reason downstream can aggregate several construction failures into
// one report.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;

/// <summary>
/// 网络弧稳定标识 / Stable network arc identifier.
/// </summary>
/// <param name="Value">标识文本 / Identifier text.</param>
public sealed record NetworkArcId(string Value) : IComparable<NetworkArcId> {
    /// <inheritdoc/>
    public int CompareTo(NetworkArcId? other) =>
        other is null ? 1 : string.CompareOrdinal(Value, other.Value);

    /// <inheritdoc/>
    public override string ToString() => Value;
}

/// <summary>
/// 非负容量 / Non-negative capacity.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="quantity">非负容量物理量 / Non-negative capacity quantity.</param>
public sealed class Capacity<V>(Quantity<V> quantity) where V : struct, IRealNumber<V> {
    /// <summary>非负容量物理量 / Non-negative capacity quantity.</summary>
    public Quantity<V> Quantity { get; } = quantity;

    /// <summary>
    /// 创建非负容量 / Create a non-negative capacity.
    /// </summary>
    /// <remarks>
    /// **零容量是合法的**：它表示"这条边不可用"，是一个有意义的建模手段（封路、禁用通道）。若把零也拒掉，调用方只能删边，
    /// 而删边会改变图的拓扑，让节点编号与下游引用错位。
    /// **A zero capacity is legal**: it means "this edge is unusable", a meaningful modelling device (blocking a road,
    /// disabling a channel). Rejecting zero would force callers to delete the edge, which changes the graph's topology and
    /// misaligns node numbering against downstream references.
    /// </remarks>
    /// <param name="quantity">容量物理量 / Capacity quantity.</param>
    /// <returns>容量或校验失败 / The capacity, or a validation failure.</returns>
    public static Result<Capacity<V>, ErrorCode, Error<ErrorCode>> Invoke(Quantity<V> quantity) {
        ArgumentNullException.ThrowIfNull(quantity);
        if (quantity.Value.CompareTo(quantity.Value.Constants.Zero) < 0) {
            return NetworkSchedulingErrors.Failure<Capacity<V>>(
                "创建容量失败：容量不能为负 / Failed to create capacity: capacity cannot be negative");
        }
        return Results.Ok(new Capacity<V>(quantity));
    }
}

/// <summary>
/// 非负流量 / Non-negative flow.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="quantity">非负流量物理量 / Non-negative flow quantity.</param>
public sealed class Flow<V>(Quantity<V> quantity) where V : struct, IRealNumber<V> {
    /// <summary>非负流量物理量 / Non-negative flow quantity.</summary>
    public Quantity<V> Quantity { get; } = quantity;

    /// <summary>
    /// 创建非负流量 / Create a non-negative flow.
    /// </summary>
    /// <remarks>
    /// **零流量合法**（边未被使用），负流量非法（那属于反向边的语义，应显式建模为一条反向弧）。
    /// **A zero flow is legal** (the edge is unused); a negative flow is not — that belongs to a reverse edge and should be
    /// modelled explicitly as one.
    /// </remarks>
    /// <param name="quantity">流量物理量 / Flow quantity.</param>
    /// <returns>流量或校验失败 / The flow, or a validation failure.</returns>
    public static Result<Flow<V>, ErrorCode, Error<ErrorCode>> Invoke(Quantity<V> quantity) {
        ArgumentNullException.ThrowIfNull(quantity);
        if (quantity.Value.CompareTo(quantity.Value.Constants.Zero) < 0) {
            return NetworkSchedulingErrors.Failure<Flow<V>>(
                "创建流量失败：流量不能为负 / Failed to create flow: flow cannot be negative");
        }
        return Results.Ok(new Flow<V>(quantity));
    }
}

/// <summary>
/// 节点净供需量 / Node net balance.
/// </summary>
/// <remarks>
/// **允许任意符号**：正值表示供给，负值表示需求。这与 <see cref="Capacity{V}"/>／<see cref="Flow{V}"/> 的非负约束形成对比，
/// 因为"供需"本身就是一个带方向的量，强行拆成供给量与需求量两个字段会让下游每次都要判断哪一侧为零。
/// **Either sign is allowed**: a positive value is supply and a negative one is demand. This contrasts with the non-negative
/// constraints on <see cref="Capacity{V}"/> / <see cref="Flow{V}"/> because a balance is inherently directional; splitting
/// it into separate supply and demand fields would force downstream to test which side is zero every time.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="net">净供需量 / Net balance.</param>
public sealed class NodeBalance<V>(Quantity<V> net) where V : struct, IRealNumber<V> {
    /// <summary>净供需量 / Net balance.</summary>
    public Quantity<V> Net { get; } = net;

    /// <summary>
    /// 创建节点净供需量，允许任意符号 / Create a balance with either sign.
    /// </summary>
    /// <param name="net">净供需量 / Net balance.</param>
    /// <returns>净供需量 / The balance.</returns>
    public static Result<NodeBalance<V>, ErrorCode, Error<ErrorCode>> Invoke(Quantity<V> net) {
        ArgumentNullException.ThrowIfNull(net);
        return Results.Ok(new NodeBalance<V>(net));
    }
}

/// <summary>
/// 容量上下界 / Capacity bounds.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="lower">下界 / Lower bound.</param>
/// <param name="upper">上界 / Upper bound.</param>
public sealed class CapacityBounds<V>(Quantity<V> lower, Quantity<V> upper) where V : struct, IRealNumber<V> {
    /// <summary>下界 / Lower bound.</summary>
    public Quantity<V> Lower { get; } = lower;

    /// <summary>上界 / Upper bound.</summary>
    public Quantity<V> Upper { get; } = upper;

    /// <summary>
    /// 创建并校验容量上下界 / Create and validate capacity bounds.
    /// </summary>
    /// <remarks>
    /// 四条校验按此顺序：量纲一致 → 非负 → 上界可换算 → 下界不大于上界。
    /// **量纲检查必须最先做**：若上下界量纲不同，后续所有数值比较都失去意义，先比数值会得到一个"看起来合理"的错误结论。
    /// **上界按原单位保存**：基线校验时把上界换算到下界单位比较，但**存的是原始上界**。若存换算后的值，调用方拿到的
    /// `Upper` 单位会与他传入的不同，下游再按自己期望的单位读就会错。
    /// Four checks in this order: same dimension → non-negative → upper convertible → lower not above upper.
    /// **The dimension check must come first**: with mismatched dimensions every later numeric comparison is meaningless,
    /// and comparing values first yields a "plausible-looking" wrong conclusion. **The upper bound is stored in its original
    /// unit**: the baseline converts only for the comparison, so storing the converted value would hand callers an `Upper`
    /// in a different unit than they passed and mislead downstream reading it in their expected unit.
    /// </remarks>
    /// <param name="lower">下界（非负）/ Lower bound (non-negative).</param>
    /// <param name="upper">上界（不小于下界）/ Upper bound (not below the lower bound).</param>
    /// <returns>容量上下界或校验失败 / The bounds, or a validation failure.</returns>
    public static Result<CapacityBounds<V>, ErrorCode, Error<ErrorCode>> Invoke(
        Quantity<V> lower,
        Quantity<V> upper) {
        ArgumentNullException.ThrowIfNull(lower);
        ArgumentNullException.ThrowIfNull(upper);

        if (!lower.Unit.SameDimension(upper.Unit)) {
            return NetworkSchedulingErrors.Failure<CapacityBounds<V>>(
                "创建容量上下界失败：上下界量纲必须一致 / Failed to create capacity bounds: lower and upper bounds "
                + "must have the same dimension");
        }
        if (lower.Value.CompareTo(lower.Value.Constants.Zero) < 0
            || upper.Value.CompareTo(upper.Value.Constants.Zero) < 0) {
            return NetworkSchedulingErrors.Failure<CapacityBounds<V>>(
                "创建容量上下界失败：上下界不能为负 / Failed to create capacity bounds: bounds cannot be negative");
        }
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> normalizedUpper = upper.ConvertTo(lower.Unit);
        if (!normalizedUpper.IsOk) {
            return NetworkSchedulingErrors.Failure<CapacityBounds<V>>(
                "创建容量上下界失败：上界无法转换到下界单位 / Failed to create capacity bounds: upper bound cannot be "
                + "converted to the lower-bound unit");
        }
        if (lower.Value.CompareTo(normalizedUpper.Value.Value) > 0) {
            return NetworkSchedulingErrors.Failure<CapacityBounds<V>>(
                "创建容量上下界失败：下界不能大于上界 / Failed to create capacity bounds: lower bound cannot exceed "
                + "the upper bound");
        }
        return Results.Ok(new CapacityBounds<V>(lower, upper));
    }
}

/// <summary>
/// 网络弧 / Network arc.
/// </summary>
/// <remarks>
/// **默认标识用长度前缀拼接**（`"${from.Length}:${from}${to.Length}:${to}"`）。这不是随意格式：直接拼 `from + to` 会让
/// `("ab","c")` 与 `("a","bc")` 得到同一个 `"abc"`，两条不同的弧在字典里互相顶替。长度前缀让拼接可逆，也保证平行弧在
/// 未显式给 id 时仍各自独立。
/// **The default identifier uses length prefixes** (`"${from.Length}:${from}${to.Length}:${to}"`). That is not arbitrary: a
/// plain `from + to` makes `("ab","c")` and `("a","bc")` collide on `"abc"`, letting two different arcs overwrite each other
/// in a dictionary. Length prefixes make the concatenation reversible and keep parallel arcs distinct even without an
/// explicit id.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="Id">稳定弧标识 / Stable arc identifier.</param>
/// <param name="From">起点 / Origin.</param>
/// <param name="To">终点 / Destination.</param>
/// <param name="Cost">弧成本 / Arc cost.</param>
/// <param name="Attributes">通用弧属性 / Generic arc attributes.</param>
public sealed class NetworkArc<V>(
    NetworkArcId Id,
    NetworkNodeId From,
    NetworkNodeId To,
    Quantity<V> Cost,
    IReadOnlyDictionary<string, Quantity<V>> Attributes) where V : struct, IRealNumber<V> {
    /// <summary>稳定弧标识 / Stable arc identifier.</summary>
    public NetworkArcId Id { get; } = Id;

    /// <summary>起点 / Origin.</summary>
    public NetworkNodeId From { get; } = From;

    /// <summary>终点 / Destination.</summary>
    public NetworkNodeId To { get; } = To;

    /// <summary>弧成本 / Arc cost.</summary>
    public Quantity<V> Cost { get; } = Cost;

    /// <summary>通用弧属性 / Generic arc attributes.</summary>
    public IReadOnlyDictionary<string, Quantity<V>> Attributes { get; } =
        Attributes.ToDictionary(pair => pair.Key, pair => pair.Value);

    /// <summary>
    /// 创建并校验网络弧 / Create and validate a network arc.
    /// </summary>
    /// <param name="from">起点 / Origin.</param>
    /// <param name="to">终点 / Destination.</param>
    /// <param name="cost">弧成本 / Arc cost.</param>
    /// <param name="attributes">通用弧属性 / Generic arc attributes.</param>
    /// <param name="id">稳定弧标识，平行弧必须显式指定 / Stable identity, required explicitly for parallel arcs.</param>
    /// <returns>网络弧或校验失败 / The arc, or a validation failure.</returns>
    public static Result<NetworkArc<V>, ErrorCode, Error<ErrorCode>> Invoke(
        NetworkNodeId from,
        NetworkNodeId to,
        Quantity<V> cost,
        IReadOnlyDictionary<string, Quantity<V>>? attributes = null,
        NetworkArcId? id = null) {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        ArgumentNullException.ThrowIfNull(cost);

        NetworkArcId identity = id ?? new NetworkArcId(
            $"{from.Value.Length}:{from.Value}{to.Value.Length}:{to.Value}");
        if (string.IsNullOrWhiteSpace(identity.Value)) {
            return NetworkSchedulingErrors.Failure<NetworkArc<V>>(
                "创建网络弧失败：弧标识不能为空 / Failed to create network arc: blank identifier");
        }
        return Results.Ok(new NetworkArc<V>(
            identity, from, to, cost, attributes ?? new Dictionary<string, Quantity<V>>()));
    }
}
