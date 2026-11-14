// 网络流问题的数据层 / The data layer of a network-flow problem.
// 与固定 Kotlin 基线 `FlowContext.kt`（8792 字节）的数据部分对应：`FlowKey`、`CommodityFlow`、`FlowCapacity`、`FlowSlack`、
// `FlowProblem`、`FlowSolution`。
// Mirrors the data portion of the pinned baseline's `FlowContext.kt`: `FlowKey`, `CommodityFlow`, `FlowCapacity`, `FlowSlack`,
// `FlowProblem`, `FlowSolution`.
//
// 注册态（变量、表达式、松弛）不在这一层，由 `FlowAggregation` 持有：同一份问题可以被多个模型复用、并行求解、重放。
// Registration state (variables, expressions, slacks) is not here but held by `FlowAggregation`: one problem can be reused by several
// models, solved in parallel, and replayed.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Domain.Flow;

/// <summary>
/// 商品和服务弧的稳定键 / Stable commodity-service key.
/// </summary>
/// <param name="Commodity">商品标识 / Commodity identifier.</param>
/// <param name="Arc">服务弧标识 / Service arc identifier.</param>
public sealed record FlowKey(string Commodity, NetworkArcId Arc);

/// <summary>
/// 商品流配置，整数步长不是整单不可拆分约束 / Commodity flow options; integer steps do not imply unsplittable orders.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class CommodityFlow<V> where V : struct, IRealNumber<V> {
    /// <summary>
    /// 构造商品流配置 / Construct the commodity flow options.
    /// </summary>
    /// <param name="commodity">商品供需 / Commodity balances.</param>
    /// <param name="unit">商品归一化单位 / Commodity normalisation unit.</param>
    /// <param name="integerStep">整数流每步数量，null 表示连续 / Amount per integer step, null for continuous flow.</param>
    /// <param name="costs">每单位商品的弧费用 / Arc costs per commodity unit.</param>
    /// <param name="allowedArcs">可用服务弧，null 表示全部 / Allowed services, null for all.</param>
    public CommodityFlow(
        Model.FlowCommodity<V> commodity,
        PhysicalUnit unit,
        Quantity<V>? integerStep = null,
        IReadOnlyDictionary<NetworkArcId, Quantity<V>>? costs = null,
        IReadOnlySet<NetworkArcId>? allowedArcs = null) {
        ArgumentNullException.ThrowIfNull(commodity);
        ArgumentNullException.ThrowIfNull(unit);
        Commodity = commodity;
        Unit = unit;
        IntegerStep = integerStep;
        Costs = costs is null
            ? new Dictionary<NetworkArcId, Quantity<V>>()
            : costs.ToDictionary(pair => pair.Key, pair => pair.Value);
        AllowedArcs = allowedArcs is null ? null : new HashSet<NetworkArcId>(allowedArcs);
    }

    /// <summary>商品供需 / Commodity balances.</summary>
    public Model.FlowCommodity<V> Commodity { get; }

    /// <summary>商品归一化单位 / Commodity normalisation unit.</summary>
    public PhysicalUnit Unit { get; }

    /// <summary>整数流每步数量，null 表示连续 / Amount per integer step, null for continuous flow.</summary>
    public Quantity<V>? IntegerStep { get; }

    /// <summary>每单位商品的弧费用 / Arc costs per commodity unit.</summary>
    public IReadOnlyDictionary<NetworkArcId, Quantity<V>> Costs { get; }

    /// <summary>可用服务弧，null 表示全部 / Allowed services, null for all.</summary>
    public IReadOnlySet<NetworkArcId>? AllowedArcs { get; }
}

/// <summary>
/// 显式共享资源行；用量按一个商品归一化单位声明 / Explicit shared-resource row; usage is per normalised commodity unit.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class FlowCapacity<V> where V : struct, IRealNumber<V> {
    /// <summary>
    /// 构造显式资源行 / Construct an explicit resource row.
    /// </summary>
    /// <param name="id">资源标识 / Resource identifier.</param>
    /// <param name="bounds">资源容量 / Resource bounds.</param>
    /// <param name="usage">单位商品的资源消耗 / Resource consumption per commodity unit.</param>
    public FlowCapacity(string id, CapacityBounds<V> bounds, IReadOnlyDictionary<FlowKey, Quantity<V>>? usage = null) {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(bounds);
        Id = id;
        Bounds = bounds;
        Usage = usage is null
            ? new Dictionary<FlowKey, Quantity<V>>()
            : usage.ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    /// <summary>资源标识 / Resource identifier.</summary>
    public string Id { get; }

    /// <summary>资源容量 / Resource bounds.</summary>
    public CapacityBounds<V> Bounds { get; }

    /// <summary>单位商品的资源消耗 / Resource consumption per commodity unit.</summary>
    public IReadOnlyDictionary<FlowKey, Quantity<V>> Usage { get; }
}

/// <summary>
/// 有界供需松弛，只能显式启用 / Bounded balance relaxation, enabled explicitly.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="Commodity">商品 / Commodity.</param>
/// <param name="Node">节点 / Node.</param>
/// <param name="UnusedSupply">最大弃供 / Maximum unused supply.</param>
/// <param name="UnmetDemand">最大缺货 / Maximum unmet demand.</param>
/// <param name="Penalty">每商品单位罚费用 / Penalty per commodity unit.</param>
public sealed record FlowSlack<V>(
    string Commodity,
    NetworkNodeId Node,
    Quantity<V> UnusedSupply,
    Quantity<V> UnmetDemand,
    Quantity<V> Penalty) where V : struct, IRealNumber<V>;

/// <summary>
/// 不持有 solver 状态的流问题快照 / Flow problem snapshot without solver state.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class FlowProblem<V> where V : struct, IRealNumber<V> {
    /// <summary>
    /// 构造流问题 / Construct a flow problem.
    /// </summary>
    /// <param name="graph">网络拓扑 / Network topology.</param>
    /// <param name="arcs">服务弧及容量 / Services and capacities.</param>
    /// <param name="commodities">商品配置 / Commodity options.</param>
    /// <param name="zero">数值零 / Numeric zero.</param>
    /// <param name="costUnit">费用单位 / Cost unit.</param>
    /// <param name="capacityUsage">默认弧容量的异量纲消耗覆盖 / Cross-dimensional consumption overrides.</param>
    /// <param name="resources">额外共享资源或节点吞吐行 / Additional shared-resource or node-throughput rows.</param>
    /// <param name="slacks">显式供需松弛 / Explicit balance relaxation.</param>
    public FlowProblem(
        NetworkGraph<NetworkNode<V>, NetworkArc<V>> graph,
        IReadOnlyList<Model.FlowArc<V>> arcs,
        IReadOnlyList<CommodityFlow<V>> commodities,
        V zero,
        PhysicalUnit? costUnit = null,
        IReadOnlyDictionary<FlowKey, Quantity<V>>? capacityUsage = null,
        IReadOnlyList<FlowCapacity<V>>? resources = null,
        IReadOnlyList<FlowSlack<V>>? slacks = null) {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(arcs);
        ArgumentNullException.ThrowIfNull(commodities);
        Graph = graph;
        Arcs = arcs.ToList();
        Commodities = commodities.ToList();
        Zero = zero;
        CostUnit = costUnit ?? NoneUnit.Instance;
        CapacityUsage = capacityUsage is null
            ? new Dictionary<FlowKey, Quantity<V>>()
            : capacityUsage.ToDictionary(pair => pair.Key, pair => pair.Value);
        Resources = resources?.ToList() ?? new List<FlowCapacity<V>>();
        Slacks = slacks?.ToList() ?? new List<FlowSlack<V>>();
    }

    /// <summary>网络拓扑 / Network topology.</summary>
    public NetworkGraph<NetworkNode<V>, NetworkArc<V>> Graph { get; }

    /// <summary>服务弧及容量 / Services and capacities.</summary>
    public IReadOnlyList<Model.FlowArc<V>> Arcs { get; }

    /// <summary>商品配置 / Commodity options.</summary>
    public IReadOnlyList<CommodityFlow<V>> Commodities { get; }

    /// <summary>数值零 / Numeric zero.</summary>
    public V Zero { get; }

    /// <summary>费用单位 / Cost unit.</summary>
    public PhysicalUnit CostUnit { get; }

    /// <summary>默认弧容量的异量纲消耗覆盖 / Cross-dimensional consumption overrides.</summary>
    public IReadOnlyDictionary<FlowKey, Quantity<V>> CapacityUsage { get; }

    /// <summary>额外共享资源或节点吞吐行 / Additional shared-resource or node-throughput rows.</summary>
    public IReadOnlyList<FlowCapacity<V>> Resources { get; }

    /// <summary>显式供需松弛 / Explicit balance relaxation.</summary>
    public IReadOnlyList<FlowSlack<V>> Slacks { get; }

    /// <summary>
    /// 构建默认弧资源行，不将不同量纲裸值相加 / Build arc resource rows without mixing raw dimensions.
    /// </summary>
    /// <remarks>
    /// **每条商品在一根弧上的默认消耗是"1 个商品单位"，且必须先换算到弧容量的量纲**。若两者量纲不同又没给显式覆盖，这里直接失败
    /// 而不是把两个数硬加：一个以"吨"计的货物占用一条以"立方米"计的管道，数值上可以相加，物理上毫无意义——而模型照样会解出
    /// 一个"可行"答案。显式 `capacityUsage` 就是让调用方声明真实换算关系的地方。
    /// **Each commodity's default consumption on an arc is "one commodity unit", and it must first convert into the arc capacity's
    /// dimension.** If the two dimensions differ and no explicit override is given, this fails rather than adding the two numbers: a
    /// cargo measured in tonnes occupying a pipe measured in cubic metres adds up numerically and is physically meaningless — yet the
    /// model still returns a "feasible" answer. The explicit `capacityUsage` map is where the caller declares the real conversion.
    /// </remarks>
    /// <returns>资源行或单位错误 / The resource rows, or a unit error.</returns>
    public Result<IReadOnlyList<FlowCapacity<V>>, ErrorCode, Error<ErrorCode>> Capacities() {
        var rows = new List<FlowCapacity<V>>();
        foreach (Model.FlowArc<V> arc in Arcs) {
            var usage = new Dictionary<FlowKey, Quantity<V>>();
            foreach (CommodityFlow<V> commodity in Commodities) {
                if (commodity.AllowedArcs is { } allowed && !allowed.Contains(arc.NetworkArc.Id)) {
                    continue;
                }
                var key = new FlowKey(commodity.Commodity.Id, arc.NetworkArc.Id);
                Quantity<V> amount = CapacityUsage.TryGetValue(key, out Quantity<V>? overridden)
                    ? overridden
                    : new Quantity<V>(Zero.Constants.One, commodity.Unit);
                if (!amount.ConvertTo(arc.Capacity.Upper.Unit).IsOk) {
                    return NetworkSchedulingErrors.Failure<IReadOnlyList<FlowCapacity<V>>>(
                        $"共享容量需要显式同量纲消耗：{key.Commodity}/{key.Arc.Value} / Explicit compatible capacity usage "
                        + $"required: {key.Commodity}/{key.Arc.Value}");
                }
                usage[key] = amount;
            }
            rows.Add(new FlowCapacity<V>($"arc:{arc.NetworkArc.Id.Value}", arc.Capacity, usage));
        }
        return Results.Ok<IReadOnlyList<FlowCapacity<V>>>(rows.Concat(Resources).ToList());
    }
}

/// <summary>
/// 业务流量提取结果 / Extracted business flows.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="Flows">商品服务流量 / Commodity-service quantities.</param>
/// <param name="Cost">总费用，包含显式罚成本 / Total cost including explicit penalties.</param>
/// <param name="CapacityUsage">资源利用量 / Resource utilisation.</param>
/// <param name="UnusedSupply">弃供 / Unused supply.</param>
/// <param name="UnmetDemand">缺货 / Unmet demand.</param>
/// <param name="CommodityCosts">各商品运输费用 / Transportation cost by commodity.</param>
/// <param name="RelaxationCost">松弛罚费用 / Relaxation penalties.</param>
/// <param name="SatisfiedBalances">实际供需净量 / Realised net balances.</param>
public sealed record FlowSolution<V>(
    IReadOnlyDictionary<FlowKey, Quantity<V>> Flows,
    Quantity<V> Cost,
    IReadOnlyDictionary<string, Quantity<V>> CapacityUsage,
    IReadOnlyDictionary<(string Commodity, NetworkNodeId Node), Quantity<V>> UnusedSupply,
    IReadOnlyDictionary<(string Commodity, NetworkNodeId Node), Quantity<V>> UnmetDemand,
    IReadOnlyDictionary<string, Quantity<V>> CommodityCosts,
    Quantity<V> RelaxationCost,
    IReadOnlyDictionary<(string Commodity, NetworkNodeId Node), Quantity<V>> SatisfiedBalances)
    where V : struct, IRealNumber<V>;
