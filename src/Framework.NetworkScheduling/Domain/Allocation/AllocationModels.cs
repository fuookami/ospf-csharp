// 运输分配域 / Transport-allocation domain.
// 与固定 Kotlin 基线 `allocation/AllocationContext.kt`（21086 字节）对应。
// Mirrors the pinned baseline's `allocation/AllocationContext.kt`.

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
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Domain.Allocation;

/// <summary>
/// 运输地点，可选层级按非递减方向约束服务弧 / A transport location; optional layers constrain services to non-decreasing direction.
/// </summary>
/// <remarks>
/// **层级只约束方向，不约束可达性**。它表达"越下游越大"这一业务事实，让服务弧不能逆向；把它当成"只能同层或相邻层"会让合法的
/// 跨层直达服务被静默拒绝。
/// **A layer constrains direction only, not reachability.** It encodes the business fact "farther downstream is larger" so service
/// arcs cannot go backwards; treating it as "same or adjacent layer only" would silently reject legitimate cross-layer direct
/// services.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class AllocationLocation<V> where V : struct, IRealNumber<V> {
    /// <summary>
    /// 构造运输地点 / Construct a transport location.
    /// </summary>
    /// <param name="id">稳定地点标识 / Stable location identity.</param>
    /// <param name="layer">可选层级 / The optional layer.</param>
    /// <param name="attributes">地点物理属性 / Physical location attributes.</param>
    public AllocationLocation(
        NetworkNodeId id,
        int? layer = null,
        IReadOnlyDictionary<string, Quantity<V>>? attributes = null) {
        ArgumentNullException.ThrowIfNull(id);
        Id = id;
        Layer = layer;
        Attributes = attributes is null
            ? new Dictionary<string, Quantity<V>>(StringComparer.Ordinal)
            : attributes.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    /// <summary>稳定地点标识 / Stable location identity.</summary>
    public NetworkNodeId Id { get; }

    /// <summary>可选层级，数值越大越下游 / The optional layer; larger is farther downstream.</summary>
    public int? Layer { get; }

    /// <summary>地点物理属性 / Physical location attributes.</summary>
    public IReadOnlyDictionary<string, Quantity<V>> Attributes { get; }
}

/// <summary>
/// 服务的建设容量等级；容量代表建成后的总容量 / A construction capacity level; capacity is the resulting total, not an increment.
/// </summary>
/// <remarks>
/// **`Capacity` 是建成后的总容量，不是增量**。按增量理解会让选高等级的服务反而比低等级容量小，而这种错误只表现为"容量约束莫名其妙
/// 变紧"，不会报错。
/// **`Capacity` is the resulting total, not an increment.** Reading it as an increment makes a higher level yield less capacity
/// than a lower one, and that mistake only shows up as "the capacity row is inexplicably tight", never as an error.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="Id">等级标识 / Level identity.</param>
/// <param name="Capacity">建成后的服务容量 / The resulting service capacity.</param>
/// <param name="Cost">建设费用 / The construction cost.</param>
public sealed record AllocationCapacityLevel<V>(
    string Id,
    Quantity<V> Capacity,
    Quantity<V> Cost) where V : struct, IRealNumber<V>;

/// <summary>
/// 可被请求使用的运输服务 / A transport service that requests may use.
/// </summary>
/// <remarks>
/// **`CapacityLevels` 为空表示服务固定启用**，非空表示必须选一个等级（`Required` 为真时强制）。把空列表理解成"不可用"会让整个
/// 分配问题凭空不可行。
/// **An empty `CapacityLevels` means the service is always available**; a non-empty list means one level must be chosen (forced when
/// `Required` is true). Reading the empty list as "unavailable" would make the whole allocation problem spuriously infeasible.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class AllocationService<V> where V : struct, IRealNumber<V> {
    /// <summary>
    /// 构造运输服务 / Construct a transport service.
    /// </summary>
    /// <param name="id">稳定服务标识 / Stable service identity.</param>
    /// <param name="from">起点 / Origin.</param>
    /// <param name="to">终点 / Destination.</param>
    /// <param name="cost">每请求单位运行费用 / Operating cost per request unit.</param>
    /// <param name="capacity">固定运行容量或建设容量包络 / Fixed operating capacity or the construction envelope.</param>
    /// <param name="capacityLevels">可选建设等级 / The optional construction levels.</param>
    /// <param name="required">是否必须选择建设等级 / Whether a construction level must be selected.</param>
    /// <param name="attributes">服务属性 / Service attributes.</param>
    public AllocationService(
        NetworkArcId id,
        NetworkNodeId from,
        NetworkNodeId to,
        Quantity<V> cost,
        CapacityBounds<V> capacity,
        IReadOnlyList<AllocationCapacityLevel<V>>? capacityLevels = null,
        bool required = false,
        IReadOnlyDictionary<string, Quantity<V>>? attributes = null) {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        ArgumentNullException.ThrowIfNull(cost);
        ArgumentNullException.ThrowIfNull(capacity);
        Id = id;
        From = from;
        To = to;
        Cost = cost;
        Capacity = capacity;
        CapacityLevels = capacityLevels?.ToList() ?? new List<AllocationCapacityLevel<V>>();
        Required = required;
        Attributes = attributes is null
            ? new Dictionary<string, Quantity<V>>(StringComparer.Ordinal)
            : attributes.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    /// <summary>稳定服务标识 / Stable service identity.</summary>
    public NetworkArcId Id { get; }

    /// <summary>起点 / Origin.</summary>
    public NetworkNodeId From { get; }

    /// <summary>终点 / Destination.</summary>
    public NetworkNodeId To { get; }

    /// <summary>每请求单位运行费用 / Operating cost per request unit.</summary>
    public Quantity<V> Cost { get; }

    /// <summary>固定运行容量或建设容量包络 / Fixed operating capacity or the construction envelope.</summary>
    public CapacityBounds<V> Capacity { get; }

    /// <summary>可选建设等级 / The optional construction levels.</summary>
    public IReadOnlyList<AllocationCapacityLevel<V>> CapacityLevels { get; }

    /// <summary>是否必须选择建设等级 / Whether a construction level must be selected.</summary>
    public bool Required { get; }

    /// <summary>服务属性 / Service attributes.</summary>
    public IReadOnlyDictionary<string, Quantity<V>> Attributes { get; }
}

/// <summary>
/// 单个来源至去向的运输请求 / A shipment request from one origin to one destination.
/// </summary>
/// <remarks>
/// **`IntegerStep` 表示流的整数步长，不表示整单不可拆分**。把它当成不可拆分会强制整单走同一条路径，容量与配额约束因此失真。
/// **`IntegerStep` is the integer step of the flow, not an indivisibility constraint on the whole request.** Treating it as
/// indivisible forces the entire shipment onto one path, distorting capacity and quota rows.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class AllocationRequest<V> where V : struct, IRealNumber<V> {
    /// <summary>
    /// 构造运输请求 / Construct a shipment request.
    /// </summary>
    /// <param name="id">请求标识，同时用作流商品标识 / The request identity, also the flow commodity identity.</param>
    /// <param name="origin">起运地点 / The origin location.</param>
    /// <param name="destination">目的地点 / The destination location.</param>
    /// <param name="quantity">请求数量 / The requested amount.</param>
    /// <param name="unit">流量归一化单位 / The normalized flow unit.</param>
    /// <param name="integerStep">可选整数流步长 / The optional integer-flow step.</param>
    /// <param name="allowedServices">可用服务，null 表示全部 / Allowed services; null means all.</param>
    /// <param name="costs">按服务覆盖的每单位运行费用 / Per-unit operating cost overrides by service.</param>
    public AllocationRequest(
        string id,
        NetworkNodeId origin,
        NetworkNodeId destination,
        Quantity<V> quantity,
        PhysicalUnit? unit = null,
        Quantity<V>? integerStep = null,
        IReadOnlySet<NetworkArcId>? allowedServices = null,
        IReadOnlyDictionary<NetworkArcId, Quantity<V>>? costs = null) {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(quantity);
        Id = id;
        Origin = origin;
        Destination = destination;
        Quantity = quantity;
        Unit = unit ?? quantity.Unit;
        IntegerStep = integerStep;
        AllowedServices = allowedServices?.ToHashSet();
        Costs = costs is null
            ? new Dictionary<NetworkArcId, Quantity<V>>()
            : costs.ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    /// <summary>请求标识 / The request identity.</summary>
    public string Id { get; }

    /// <summary>起运地点 / The origin location.</summary>
    public NetworkNodeId Origin { get; }

    /// <summary>目的地点 / The destination location.</summary>
    public NetworkNodeId Destination { get; }

    /// <summary>请求数量 / The requested amount.</summary>
    public Quantity<V> Quantity { get; }

    /// <summary>流量归一化单位 / The normalized flow unit.</summary>
    public PhysicalUnit Unit { get; }

    /// <summary>可选整数流步长 / The optional integer-flow step.</summary>
    public Quantity<V>? IntegerStep { get; }

    /// <summary>可用服务，null 表示全部 / Allowed services; null means all.</summary>
    public IReadOnlySet<NetworkArcId>? AllowedServices { get; }

    /// <summary>按服务覆盖的每单位运行费用 / Per-unit operating cost overrides by service.</summary>
    public IReadOnlyDictionary<NetworkArcId, Quantity<V>> Costs { get; }
}

/// <summary>
/// 请求在服务上的资源消耗键 / The resource-consumption key for a request on a service.
/// </summary>
/// <param name="Request">请求标识 / The request identity.</param>
/// <param name="Service">服务标识 / The service identity.</param>
public sealed record AllocationFlowKey(string Request, NetworkArcId Service);

/// <summary>
/// 明确的共享配额行 / An explicit shared quota row.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class AllocationQuota<V> where V : struct, IRealNumber<V> {
    /// <summary>
    /// 构造共享配额 / Construct a shared quota.
    /// </summary>
    /// <param name="id">配额标识 / The quota identity.</param>
    /// <param name="bounds">配额容量 / The quota bounds.</param>
    /// <param name="usage">每单位请求对配额的消耗 / Quota consumption per request unit.</param>
    public AllocationQuota(
        string id,
        CapacityBounds<V> bounds,
        IReadOnlyDictionary<AllocationFlowKey, Quantity<V>> usage) {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(bounds);
        ArgumentNullException.ThrowIfNull(usage);
        Id = id;
        Bounds = bounds;
        Usage = usage.ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    /// <summary>配额标识 / The quota identity.</summary>
    public string Id { get; }

    /// <summary>配额容量 / The quota bounds.</summary>
    public CapacityBounds<V> Bounds { get; }

    /// <summary>每单位请求对配额的消耗 / Quota consumption per request unit.</summary>
    public IReadOnlyDictionary<AllocationFlowKey, Quantity<V>> Usage { get; }
}

/// <summary>
/// 可选枢纽及其管理的共享配额 / An optional hub and the shared quotas it controls.
/// </summary>
/// <remarks>
/// **`QuotaIds` 是"由该枢纽启停控制"的配额**。枢纽不开时这些配额整行为零；把控制关系当成普通引用会让枢纽开与不开对配额毫无影响，
/// 枢纽费用因此白付。
/// **`QuotaIds` are the quotas gated by this hub's activation.** While the hub is closed those rows are forced to zero; treating the
/// relation as a plain reference would make the hub's activation irrelevant to the quotas, so its opening cost buys nothing.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class AllocationHub<V> where V : struct, IRealNumber<V> {
    /// <summary>
    /// 构造可选枢纽 / Construct an optional hub.
    /// </summary>
    /// <param name="location">枢纽地点 / The hub location.</param>
    /// <param name="openingCost">开设费用 / The opening cost.</param>
    /// <param name="required">是否必须开设 / Whether the hub must be opened.</param>
    /// <param name="quotaIds">由该枢纽启停控制的配额 / The quotas gated by this hub's activation.</param>
    public AllocationHub(
        NetworkNodeId location,
        Quantity<V> openingCost,
        bool required = false,
        IReadOnlySet<string>? quotaIds = null) {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(openingCost);
        Location = location;
        OpeningCost = openingCost;
        Required = required;
        QuotaIds = quotaIds?.ToHashSet(StringComparer.Ordinal) ?? new HashSet<string>(StringComparer.Ordinal);
    }

    /// <summary>枢纽地点 / The hub location.</summary>
    public NetworkNodeId Location { get; }

    /// <summary>开设费用 / The opening cost.</summary>
    public Quantity<V> OpeningCost { get; }

    /// <summary>是否必须开设 / Whether the hub must be opened.</summary>
    public bool Required { get; }

    /// <summary>由该枢纽启停控制的配额 / The quotas gated by this hub's activation.</summary>
    public IReadOnlySet<string> QuotaIds { get; }
}
