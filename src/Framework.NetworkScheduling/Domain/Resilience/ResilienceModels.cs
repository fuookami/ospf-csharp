// 韧性域：连通要求与故障场景 / Resilience domain: connectivity requirements and failure scenarios.
// 与固定 Kotlin 基线 `resilience/NetworkResilienceContext.kt`（6557 字节）中的要求与场景类型对应。
// Mirrors the requirement and scenario types of the pinned baseline's `resilience/NetworkResilienceContext.kt`.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Design;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Design.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Flow;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Flow.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Domain.Resilience;

/// <summary>
/// 连通要求类型 / The connectivity requirement kind.
/// </summary>
public enum ConnectivityKind {
    /// <summary>仅要求可达 / Reachability only.</summary>
    Reachable,

    /// <summary>要求边不相交 / Edge-disjoint.</summary>
    EdgeDisjoint,

    /// <summary>要求点不相交 / Node-disjoint.</summary>
    NodeDisjoint,
}

/// <summary>
/// 业务连通要求，不等同于故障后的容量保障 / A structural connectivity requirement, distinct from post-failure capacity assurance.
/// </summary>
/// <remarks>
/// **`Reachable` 与不相交是两件事**。`Reachable` 只要求"存在一条路径"，`EdgeDisjoint` / `NodeDisjoint` 要求路径之间不共享边或点。
/// 把 `Reachable` 当成"paths 条独立路径"会让 `paths > 1` 的要求得到满足于单条路径的答案，而调用方以为拿到了冗余。
/// **`Reachable` and disjointness are different requirements.** `Reachable` only asks "some path exists", while `EdgeDisjoint` /
/// `NodeDisjoint` demand that the paths share no edge or no node. Reading `Reachable` as "paths independent paths" would satisfy a
/// `paths > 1` requirement with a single path while the caller believes they got redundancy.
/// **连通性不保证容量**。故障后是否还能承载需求由场景流决定；这条要求只说"结构上还有路"。
/// **Connectivity guarantees no capacity.** Whether demand still flows after a failure is decided by the scenario flows; this
/// requirement only says "a path still exists structurally".
/// </remarks>
/// <param name="Source">起点 / The source.</param>
/// <param name="Target">终点 / The target.</param>
/// <param name="Paths">路径数量 / The required path count.</param>
/// <param name="Kind">不相交类型 / The disjointness kind.</param>
public sealed record ConnectivityRequirement(
    NetworkNodeId Source,
    NetworkNodeId Target,
    int Paths = 1,
    ConnectivityKind Kind = ConnectivityKind.Reachable);

/// <summary>
/// 显式故障场景，不自动免除失效节点供需 / An explicit failure scenario; failed-node demand is not automatically waived.
/// </summary>
/// <remarks>
/// **失效节点上的供需默认仍然存在**。若自动免除，任何"某个节点挂了"的场景都会自动变得可行——因为需求随之消失，于是"韧性"变成了
/// 同义反复。要免除必须显式写进 `Balances`。
/// **Demand at a failed node still exists by default.** Waiving it automatically would make every "some node is down" scenario
/// trivially feasible — the demand disappears with the node, and "resilience" becomes a tautology. Waiving it must be stated
/// explicitly in `Balances`.
/// **`Slacks` 为 null 表示继承名义配置**，而不是"无松弛"。两者的区别在于：无松弛让场景不可行，继承则让它与名义模型同样宽松。
/// **`Slacks` being null means the nominal configuration is inherited**, not "no relaxation". The difference matters: no relaxation
/// makes the scenario infeasible, whereas inheriting keeps it as relaxed as the nominal model.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class FailureScenario<V> where V : struct, IRealNumber<V> {
    /// <summary>
    /// 构造故障场景 / Construct a failure scenario.
    /// </summary>
    /// <param name="id">场景标识 / The scenario identity.</param>
    /// <param name="closedArcs">关闭服务 / Closed services.</param>
    /// <param name="failedNodes">失效节点 / Failed nodes.</param>
    /// <param name="upperCapacities">降级后的容量上界 / Degraded upper capacities.</param>
    /// <param name="balances">显式覆盖商品供需 / Explicit commodity-balance overrides.</param>
    /// <param name="slacks">场景专用有界松弛；null 继承名义配置 / Scenario relaxation; null inherits the nominal configuration.</param>
    public FailureScenario(
        string id,
        IReadOnlySet<NetworkArcId>? closedArcs = null,
        IReadOnlySet<NetworkNodeId>? failedNodes = null,
        IReadOnlyDictionary<NetworkArcId, Quantity<V>>? upperCapacities = null,
        IReadOnlyDictionary<string, IReadOnlyList<SupplyDemand<V>>>? balances = null,
        IReadOnlyList<FlowSlack<V>>? slacks = null) {
        ArgumentNullException.ThrowIfNull(id);
        Id = id;
        ClosedArcs = closedArcs?.ToHashSet() ?? new HashSet<NetworkArcId>();
        FailedNodes = failedNodes?.ToHashSet() ?? new HashSet<NetworkNodeId>();
        UpperCapacities = upperCapacities is null
            ? new Dictionary<NetworkArcId, Quantity<V>>()
            : upperCapacities.ToDictionary(pair => pair.Key, pair => pair.Value);
        Balances = balances is null
            ? new Dictionary<string, IReadOnlyList<SupplyDemand<V>>>(StringComparer.Ordinal)
            : balances.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        Slacks = slacks?.ToList();
    }

    /// <summary>场景标识 / The scenario identity.</summary>
    public string Id { get; }

    /// <summary>关闭服务 / Closed services.</summary>
    public IReadOnlySet<NetworkArcId> ClosedArcs { get; }

    /// <summary>失效节点 / Failed nodes.</summary>
    public IReadOnlySet<NetworkNodeId> FailedNodes { get; }

    /// <summary>降级后的容量上界 / Degraded upper capacities.</summary>
    public IReadOnlyDictionary<NetworkArcId, Quantity<V>> UpperCapacities { get; }

    /// <summary>显式覆盖商品供需 / Explicit commodity-balance overrides.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<SupplyDemand<V>>> Balances { get; }

    /// <summary>场景专用有界松弛；null 继承名义配置 / Scenario relaxation; null inherits the nominal configuration.</summary>
    public IReadOnlyList<FlowSlack<V>>? Slacks { get; }

    /// <summary>
    /// 构造独立单弧故障场景，不枚举组合 / Build independent single-arc failures, not combinations.
    /// </summary>
    /// <remarks>
    /// **只生成单故障，不生成组合**。组合场景的数量随弧数指数增长；把它当成"全部两两组合"会让模型规模瞬间失控，而单故障已经能覆盖
    /// 最常见的"某条链路断了"。
    /// **Single failures only, never combinations.** Combination scenarios grow exponentially with the arc count; treating this as
    /// "all pairs" explodes the model instantly, while single failures already cover the common "one link is down" case.
    /// </remarks>
    /// <param name="arcs">服务集合 / The service identities.</param>
    /// <returns>单故障场景 / The single-failure scenarios.</returns>
    public static IReadOnlyList<FailureScenario<V>> SingleArcFailures(IReadOnlySet<NetworkArcId> arcs) {
        ArgumentNullException.ThrowIfNull(arcs);
        return arcs
            .Select(arc => new FailureScenario<V>(
                $"arc:{arc.Value}",
                closedArcs: new HashSet<NetworkArcId> { arc }))
            .ToList();
    }

    /// <summary>
    /// 构造独立单节点故障场景 / Build independent single-node failures.
    /// </summary>
    /// <param name="nodes">节点集合 / The node identities.</param>
    /// <returns>单故障场景 / The single-failure scenarios.</returns>
    public static IReadOnlyList<FailureScenario<V>> SingleNodeFailures(IReadOnlySet<NetworkNodeId> nodes) {
        ArgumentNullException.ThrowIfNull(nodes);
        return nodes
            .Select(node => new FailureScenario<V>(
                $"node:{node.Value}",
                failedNodes: new HashSet<NetworkNodeId> { node }))
            .ToList();
    }
}

/// <summary>
/// 韧性模型结果，结论仅适用于显式场景集合 / A resilience result, valid only for the explicit scenario set.
/// </summary>
/// <remarks>
/// **`WorstScenario` 是实际运行费用最高的场景**，不是"最坏意图"的场景。它由求解结果的费用比较得出，因此可以为零费用场景——那意味着所有
/// 场景都没有花费，而不是"没有最坏场景"。
/// **`WorstScenario` is the scenario with the highest realized operating cost**, not the intended-worst one. It comes from comparing
/// solved costs and may therefore be a zero-cost scenario — which means every scenario cost nothing, not "there is no worst case".
/// **`EffectiveCapacities` 已经把四类失效折算为零**：场景中该服务消失、两端设施未开、服务等级未选、建成容量超过声明容量。
/// **`EffectiveCapacities` has already collapsed four kinds of failure to zero**: the service vanished in the scenario, an endpoint
/// facility is closed, no service level was selected, or the built capacity exceeds the declared one.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="Design">共享建设选择 / Shared construction choices.</param>
/// <param name="Scenarios">场景流量与缺口 / Scenario flows and shortfalls.</param>
/// <param name="WorstOperatingCost">最坏运行成本 / Worst operating cost.</param>
/// <param name="ConnectivityFlows">连通要求的辅助流见证 / Auxiliary connectivity-flow witnesses.</param>
/// <param name="EffectiveCapacities">各场景建成且有效的通道容量 / Built and effective service capacities per scenario.</param>
/// <param name="WorstScenario">实际运行费用最高的场景 / The scenario with the highest realized operating cost.</param>
public sealed record NetworkResilienceSolution<V>(
    NetworkDesignSolution<V> Design,
    IReadOnlyDictionary<string, FlowSolution<V>> Scenarios,
    Quantity<V> WorstOperatingCost,
    IReadOnlyList<IReadOnlyDictionary<NetworkArcId, V>> ConnectivityFlows,
    IReadOnlyDictionary<string, IReadOnlyDictionary<NetworkArcId, Quantity<V>>> EffectiveCapacities,
    string WorstScenario) where V : struct, IRealNumber<V>;

/// <summary>
/// 共享建设与显式场景的上下文入口 / The context entry for shared construction and explicit scenarios.
/// </summary>
/// <remarks>
/// **这一层是薄包装，不新增状态**。场景流、连通见证与最坏费用变量都由
/// <see cref="NetworkResilienceAggregation{V}"/> 持有；上下文只提供默认命名空间与转发。
/// **This layer is a thin wrapper and adds no state.** The scenario flows, connectivity witnesses and the worst-cost variable all
/// live in <see cref="NetworkResilienceAggregation{V}"/>; the context only supplies the default namespace and forwards.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class NetworkResilienceContext<V> where V : struct, IRealNumber<V> {
    /// <summary>
    /// 构造韧性上下文 / Construct the resilience context.
    /// </summary>
    /// <param name="design">共享建设上下文 / The shared design context.</param>
    /// <param name="scenarios">显式故障场景 / The explicit failure scenarios.</param>
    /// <param name="connectivity">连通要求 / The connectivity requirements.</param>
    /// <param name="name">模型命名空间 / The model namespace.</param>
    public NetworkResilienceContext(
        NetworkDesignContext<V> design,
        IReadOnlyList<FailureScenario<V>>? scenarios = null,
        IReadOnlyList<ConnectivityRequirement>? connectivity = null,
        string name = "network_resilience") {
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(name);
        Design = design;
        Scenarios = scenarios?.ToList() ?? new List<FailureScenario<V>>();
        Connectivity = connectivity?.ToList() ?? new List<ConnectivityRequirement>();
        Name = name;
        Aggregation = new NetworkResilienceAggregation<V>(design, Scenarios, Connectivity, name);
    }

    /// <summary>共享建设上下文 / The shared design context.</summary>
    public NetworkDesignContext<V> Design { get; }

    /// <summary>显式故障场景 / The explicit failure scenarios.</summary>
    public IReadOnlyList<FailureScenario<V>> Scenarios { get; }

    /// <summary>连通要求 / The connectivity requirements.</summary>
    public IReadOnlyList<ConnectivityRequirement> Connectivity { get; }

    /// <summary>模型命名空间 / The model namespace.</summary>
    public string Name { get; }

    /// <summary>场景及连通聚合 / The scenario and connectivity aggregation.</summary>
    public NetworkResilienceAggregation<V> Aggregation { get; }

    /// <summary>
    /// 注册共享结构、场景流和辅助连通流 / Register the shared structure, recourse and connectivity flows.
    /// </summary>
    /// <param name="model">元模型 / The meta model.</param>
    /// <returns>注册结果 / The registration result.</returns>
    public Try Register(LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        return Aggregation.Register(model);
    }

    /// <summary>
    /// 获取已注册的独立场景流，供下游添加场景规则 / Access a registered recourse for downstream scenario rules.
    /// </summary>
    /// <param name="scenario">场景标识，名义场景为 nominal / The scenario identity; nominal for the base case.</param>
    /// <returns>场景上下文或错误 / The scenario context, or an error.</returns>
    public Result<FlowContext<V>, ErrorCode, Error<ErrorCode>> Flow(string scenario) {
        ArgumentNullException.ThrowIfNull(scenario);
        return Aggregation.Flow(scenario);
    }

    /// <summary>
    /// 提取所有显式场景结果 / Extract every explicit scenario result.
    /// </summary>
    /// <param name="model">已求解模型 / The solved model.</param>
    /// <returns>业务结果 / The business solution.</returns>
    public Result<NetworkResilienceSolution<V>, ErrorCode, Error<ErrorCode>> ExtractSolution(
        LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        return Aggregation.ExtractSolution(model);
    }
}
