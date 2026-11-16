// 网络建设问题数据层 / The data layer of a network design problem.
// 与固定 Kotlin （5875 字节）的数据部分、 的
// `NetworkConstructionSolution` 对应。
// The data portion of the: and `NetworkConstructionSolution` from
//.
//
// 这一层是**纯领域数据**，不含变量与注册态：建设决策的生命周期由 `NetworkDesignAggregation` 持有，同一份建设问题因此可以被场景复用。
// This layer is **pure domain data** with no variables or registration state: the design decision's lifecycle is owned by
// `NetworkDesignAggregation`, so one design problem can be reused across scenarios.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Flow;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Quantity;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Domain.Design;

/// <summary>
/// 离散建设等级 / Discrete construction level.
/// </summary>
/// <remarks>
/// **`Capacity` 是建设后的总容量，不是增量**。若按增量理解，两个等级同时被选会得到"两次叠加"的容量，而模型里一旦出现并列的等级
/// 互斥约束失效，容量就会凭空放大；把它定义成总量则让"选一个等级"这件事天然自洽。
/// **`Capacity` is the resulting total capacity, not an increment.** Read as an increment, two selected levels would stack and
/// double the capacity — and any lapse in the mutual-exclusion rows would silently inflate capacity. Defined as a total, "pick one
/// level" is self-consistent by construction.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="Id">等级标识 / Level identity.</param>
/// <param name="Capacity">建设后总容量 / Resulting total capacity.</param>
/// <param name="Cost">建设费用 / Construction cost.</param>
public sealed record CapacityLevel<V>(string Id, Quantity<V> Capacity, Quantity<V> Cost)
    where V : struct, IRealNumber<V>;

/// <summary>
/// 可选服务通道；未选择等级表示关闭 / Candidate service; no selected level means closed.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class NetworkService<V> where V : struct, IRealNumber<V> {
    /// <summary>
    /// 构造可选服务通道 / Construct a candidate service.
    /// </summary>
    /// <param name="arc">服务弧 / Service identity.</param>
    /// <param name="levels">可选容量等级 / Capacity levels.</param>
    /// <param name="required">是否必须选择一个等级 / Whether one level must be selected.</param>
    public NetworkService(NetworkArcId arc, IReadOnlyList<CapacityLevel<V>>? levels = null, bool required = false) {
        ArgumentNullException.ThrowIfNull(arc);
        Arc = arc;
        Levels = levels?.ToList() ?? new List<CapacityLevel<V>>();
        Required = required;
    }

    /// <summary>服务弧 / Service identity.</summary>
    public NetworkArcId Arc { get; }

    /// <summary>可选容量等级 / Capacity levels.</summary>
    public IReadOnlyList<CapacityLevel<V>> Levels { get; }

    /// <summary>是否必须选择一个等级 / Whether one level must be selected.</summary>
    public bool Required { get; }
}

/// <summary>
/// 候选设施 / Candidate facility.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class NetworkFacility<V> where V : struct, IRealNumber<V> {
    /// <summary>
    /// 构造候选设施 / Construct a candidate facility.
    /// </summary>
    /// <param name="node">设施节点 / Facility node.</param>
    /// <param name="cost">开设费用 / Opening cost.</param>
    /// <param name="required">是否必须开设 / Whether opening is required.</param>
    /// <param name="capacityRows">设施启用管理的共享处理容量行 / Shared processing rows gated by facility activation.</param>
    public NetworkFacility(
        NetworkNodeId node,
        Quantity<V> cost,
        bool required = false,
        IReadOnlySet<string>? capacityRows = null) {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(cost);
        Node = node;
        Cost = cost;
        Required = required;
        CapacityRows = capacityRows is null
            ? new HashSet<string>()
            : new HashSet<string>(capacityRows, StringComparer.Ordinal);
    }

    /// <summary>设施节点 / Facility node.</summary>
    public NetworkNodeId Node { get; }

    /// <summary>开设费用 / Opening cost.</summary>
    public Quantity<V> Cost { get; }

    /// <summary>是否必须开设 / Whether opening is required.</summary>
    public bool Required { get; }

    /// <summary>设施启用管理的共享处理容量行 / Shared processing rows gated by facility activation.</summary>
    public IReadOnlySet<string> CapacityRows { get; }
}

/// <summary>
/// 结构决策问题 / Structural decision problem.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class NetworkDesignProblem<V> where V : struct, IRealNumber<V> {
    /// <summary>
    /// 构造结构决策问题 / Construct a structural decision problem.
    /// </summary>
    /// <param name="flow">名义流问题 / Nominal flow problem.</param>
    /// <param name="services">候选通道 / Candidate services.</param>
    /// <param name="facilities">候选设施 / Candidate facilities.</param>
    /// <param name="budget">建设预算 / Construction budget.</param>
    public NetworkDesignProblem(
        FlowProblem<V> flow,
        IReadOnlyList<NetworkService<V>>? services = null,
        IReadOnlyList<NetworkFacility<V>>? facilities = null,
        Quantity<V>? budget = null) {
        ArgumentNullException.ThrowIfNull(flow);
        Flow = flow;
        Services = services?.ToList() ?? new List<NetworkService<V>>();
        Facilities = facilities?.ToList() ?? new List<NetworkFacility<V>>();
        Budget = budget;
    }

    /// <summary>名义流问题 / Nominal flow problem.</summary>
    public FlowProblem<V> Flow { get; }

    /// <summary>候选通道 / Candidate services.</summary>
    public IReadOnlyList<NetworkService<V>> Services { get; }

    /// <summary>候选设施 / Candidate facilities.</summary>
    public IReadOnlyList<NetworkFacility<V>> Facilities { get; }

    /// <summary>建设预算 / Construction budget.</summary>
    public Quantity<V>? Budget { get; }
}

/// <summary>
/// 结构决策结果 / Structural decision solution.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="OpenFacilities">已开设施 / Open facilities.</param>
/// <param name="ServiceLevels">已选通道等级 / Selected service levels.</param>
/// <param name="ConstructionCost">建设成本 / Construction cost.</param>
/// <param name="Flow">名义流结果 / Nominal flow solution.</param>
public sealed record NetworkDesignSolution<V>(
    IReadOnlySet<NetworkNodeId> OpenFacilities,
    IReadOnlyDictionary<NetworkArcId, string> ServiceLevels,
    Quantity<V> ConstructionCost,
    FlowSolution<V> Flow) where V : struct, IRealNumber<V>;

/// <summary>
/// 建设决策结果 / Construction decision result.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="OpenFacilities">已开设施 / Open facilities.</param>
/// <param name="ServiceLevels">已选通道等级 / Selected service levels.</param>
/// <param name="Cost">建设成本 / Construction cost.</param>
public sealed record NetworkConstructionSolution<V>(
    IReadOnlySet<NetworkNodeId> OpenFacilities,
    IReadOnlyDictionary<NetworkArcId, string> ServiceLevels,
    Quantity<V> Cost) where V : struct, IRealNumber<V>;
