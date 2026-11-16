// 车辆类型分配与弧分支遮罩 / Vehicle-type assignment and arc branch mask.
// 与固定 Kotlin 对应。
//
// 分支定价里的"遮罩"编码了三类分支决策：某车辆类型禁访某些客户、某客户必须由某车辆类型服务、某条弧被禁或被要求。
// 遮罩本身不可变，所有校验都在构造时一次做完，因此下游每次查询只是纯函数判断，不需要重复校验。
// The mask in branch-and-price encodes three kinds of branching decision: a vehicle type forbidden from some customers, a
// customer that must be served by one vehicle type, and an arc that is forbidden or required. The mask is immutable and all
// validation happens once at construction, so downstream queries are pure predicates with no re-validation.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Infrastructure;

/// <summary>
/// 不可变车辆类型分配与弧分支遮罩 / Immutable vehicle-type assignment and arc branch mask.
/// </summary>
/// <typeparam name="K">车辆资源稳定键类型 / Vehicle-resource stable-key type.</typeparam>
public sealed class BranchMask<K> where K : notnull {
    private readonly Dictionary<K, HashSet<NetworkNodeId>> _forbiddenNodes;
    private readonly Dictionary<NetworkNodeId, K> _requiredResources;
    private readonly HashSet<ResourceArc<K>> _forbiddenArcs;
    private readonly HashSet<ResourceArc<K>> _requiredArcs;

    private BranchMask(
        NetworkNodeId startDepot,
        NetworkNodeId endDepot,
        Dictionary<K, HashSet<NetworkNodeId>> forbiddenNodes,
        Dictionary<NetworkNodeId, K> requiredResources,
        HashSet<ResourceArc<K>> forbiddenArcs,
        HashSet<ResourceArc<K>> requiredArcs) {
        StartDepot = startDepot;
        EndDepot = endDepot;
        _forbiddenNodes = forbiddenNodes;
        _requiredResources = requiredResources;
        _forbiddenArcs = forbiddenArcs;
        _requiredArcs = requiredArcs;
    }

    /// <summary>起始仓库 / Start depot.</summary>
    public NetworkNodeId StartDepot { get; }

    /// <summary>结束仓库 / End depot.</summary>
    public NetworkNodeId EndDepot { get; }

    /// <summary>各资源禁止访问的客户 / Customers forbidden for each resource.</summary>
    public IReadOnlyDictionary<K, IReadOnlySet<NetworkNodeId>> ForbiddenNodes =>
        _forbiddenNodes.ToDictionary(
            pair => pair.Key, pair => (IReadOnlySet<NetworkNodeId>)pair.Value);

    /// <summary>客户必须使用的资源 / Required resource for each customer.</summary>
    public IReadOnlyDictionary<NetworkNodeId, K> RequiredResources => _requiredResources;

    /// <summary>禁止弧 / Forbidden arcs.</summary>
    public IReadOnlySet<ResourceArc<K>> ForbiddenArcs => _forbiddenArcs;

    /// <summary>必选弧 / Required arcs.</summary>
    public IReadOnlySet<ResourceArc<K>> RequiredArcs => _requiredArcs;

    /// <summary>
    /// 判断车辆资源是否可访问节点 / Check whether a vehicle resource may visit a node.
    /// </summary>
    /// <remarks>
    /// **两个仓库永远可访问**，且这条判断在所有其它规则之前返回：若某次分支刚好禁止了仓库所在的节点，车辆就无路可走，
    /// 整棵子树会因不可行而被剪掉——而真正的原因是一条自相矛盾的分支，不是"这个区域没有解"。
    /// **Both depots are always visitable**, and that check precedes every other rule: if a branch happens to forbid a depot's
    /// node, no route exists and the whole subtree is pruned as infeasible — while the real cause is a self-contradictory
    /// branch, not "this region has no solution".
    /// </remarks>
    /// <param name="resourceKey">资源稳定键 / Stable resource key.</param>
    /// <param name="nodeId">节点标识 / Node identity.</param>
    /// <returns>是否允许访问 / Whether the node may be visited.</returns>
    public bool AllowsNode(K resourceKey, NetworkNodeId nodeId) {
        ArgumentNullException.ThrowIfNull(nodeId);
        if (nodeId.Equals(StartDepot) || nodeId.Equals(EndDepot)) {
            return true;
        }
        if (_forbiddenNodes.TryGetValue(resourceKey, out HashSet<NetworkNodeId>? forbidden)
            && forbidden.Contains(nodeId)) {
            return false;
        }
        return !_requiredResources.TryGetValue(nodeId, out K? required)
            || EqualityComparer<K>.Default.Equals(required, resourceKey);
    }

    /// <summary>
    /// 判断车辆资源是否可使用端点弧 / Check whether a vehicle resource may use the default-identity arc.
    /// </summary>
    /// <param name="resourceKey">资源稳定键 / Stable resource key.</param>
    /// <param name="from">起点 / Origin.</param>
    /// <param name="to">终点 / Destination.</param>
    /// <returns>是否允许 / Whether the arc is allowed.</returns>
    public bool AllowsArc(K resourceKey, NetworkNodeId from, NetworkNodeId to) =>
        AllowsArc(resourceKey, from, to, ResourceArcs.DefaultId(from, to));

    /// <summary>
    /// 判断带稳定弧标识的资源弧是否可用 / Check whether an identified resource arc is allowed.
    /// </summary>
    /// <param name="resourceKey">资源稳定键 / Stable resource key.</param>
    /// <param name="from">起点 / Origin.</param>
    /// <param name="to">终点 / Destination.</param>
    /// <param name="arcId">独立稳定弧标识 / Independent stable arc identity.</param>
    /// <returns>是否允许 / Whether the arc is allowed.</returns>
    public bool AllowsArc(K resourceKey, NetworkNodeId from, NetworkNodeId to, NetworkArcId arcId) =>
        AllowsArc(new ResourceArc<K>(resourceKey, from, to, arcId));

    /// <summary>
    /// 判断资源弧是否可用 / Check whether a resource arc is allowed.
    /// </summary>
    /// <remarks>
    /// 必选弧还带来一条**排他约束**：某客户若有一条必选出弧（且其起点不是起始仓库），则从该客户出发的其它弧一律禁用；入弧
    /// 同理。少了这条，必选弧只是"鼓励"而不是"要求"，分支定价会反复生成不含它的列。
    /// A required arc additionally implies an **exclusion constraint**: if a customer has a required outgoing arc (whose
    /// origin is not the start depot), every other arc leaving that customer is banned; likewise for incoming arcs. Without
    /// this, a required arc is merely preferred rather than required, and branch-and-price keeps generating columns without it.
    /// </remarks>
    /// <param name="arc">候选弧 / Candidate arc.</param>
    /// <returns>是否允许 / Whether the arc is allowed.</returns>
    public bool AllowsArc(ResourceArc<K> arc) {
        ArgumentNullException.ThrowIfNull(arc);
        K resourceKey = arc.ResourceKey;
        if (!AllowsNode(resourceKey, arc.From) || !AllowsNode(resourceKey, arc.To)
            || _forbiddenArcs.Contains(arc)) {
            return false;
        }
        foreach (ResourceArc<K> required in _requiredArcs) {
            if (!EqualityComparer<K>.Default.Equals(required.ResourceKey, resourceKey)) {
                continue;
            }
            if (!required.From.Equals(StartDepot) && arc.From.Equals(required.From) && !arc.Equals(required)) {
                return false;
            }
            if (!required.To.Equals(EndDepot) && arc.To.Equals(required.To) && !arc.Equals(required)) {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// 判断完整路径是否与遮罩兼容 / Check whether a complete path is compatible with the mask.
    /// </summary>
    /// <param name="resourceKey">资源稳定键 / Stable resource key.</param>
    /// <param name="path">有序节点 / Ordered nodes.</param>
    /// <returns>是否兼容 / Whether it is compatible.</returns>
    public bool IsRouteCompatible(K resourceKey, IReadOnlyList<NetworkNodeId> path) {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Count < 2 || !path[0].Equals(StartDepot) || !path[^1].Equals(EndDepot)) {
            return false;
        }
        if (path.Any(node => !AllowsNode(resourceKey, node))) {
            return false;
        }
        var arcIds = new List<NetworkArcId>(path.Count - 1);
        for (int position = 0; position + 1 < path.Count; position++) {
            arcIds.Add(ResourceArcs.DefaultId(path[position], path[position + 1]));
        }
        return IsRouteCompatible(resourceKey, path, arcIds);
    }

    /// <summary>
    /// 判断包含明确平行弧标识的完整路线是否兼容 / Check a complete route with explicit parallel-arc identities.
    /// </summary>
    /// <remarks>
    /// **弧标识数量必须等于路径段数**：数量对不上说明调用方给的路径与弧序列不是同一条路线，此时返回 `false` 而不是试图
    /// 补齐或截断——补齐会用默认标识悄悄替换显式标识，让平行弧分支失效。
    /// **The arc-identity count must equal the segment count**: a mismatch means the path and the arc sequence are not the same
    /// route, and this returns `false` rather than padding or truncating — padding would silently replace explicit identities
    /// with defaults and void the parallel-arc branching.
    /// </remarks>
    /// <param name="resourceKey">资源稳定键 / Stable resource key.</param>
    /// <param name="path">有序节点 / Ordered nodes.</param>
    /// <param name="arcIds">有序弧标识 / Ordered arc identities.</param>
    /// <returns>是否兼容 / Whether it is compatible.</returns>
    public bool IsRouteCompatible(
        K resourceKey,
        IReadOnlyList<NetworkNodeId> path,
        IReadOnlyList<NetworkArcId> arcIds) {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(arcIds);
        if (path.Count < 2 || !path[0].Equals(StartDepot) || !path[^1].Equals(EndDepot)
            || arcIds.Count != path.Count - 1
            || path.Any(node => !AllowsNode(resourceKey, node))) {
            return false;
        }
        for (int position = 0; position + 1 < path.Count; position++) {
            if (!AllowsArc(resourceKey, path[position], path[position + 1], arcIds[position])) {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// 判断显式资源弧序列组成的路线是否兼容 / Check a route expressed as explicit resource arcs.
    /// </summary>
    /// <param name="resourceKey">资源稳定键 / Stable resource key.</param>
    /// <param name="arcs">有序资源弧 / Ordered resource arcs.</param>
    /// <returns>是否兼容 / Whether it is compatible.</returns>
    public bool IsResourceArcSequenceCompatible(K resourceKey, IReadOnlyList<ResourceArc<K>> arcs) {
        ArgumentNullException.ThrowIfNull(arcs);
        if (arcs.Count == 0
            || arcs.Any(arc => !EqualityComparer<K>.Default.Equals(arc.ResourceKey, resourceKey))
            || !arcs[0].From.Equals(StartDepot)
            || !arcs[^1].To.Equals(EndDepot)) {
            return false;
        }
        for (int position = 0; position + 1 < arcs.Count; position++) {
            if (!arcs[position].To.Equals(arcs[position + 1].From)) {
                return false;
            }
        }
        var path = new List<NetworkNodeId> { arcs[0].From };
        path.AddRange(arcs.Select(arc => arc.To));
        return IsRouteCompatible(resourceKey, path, arcs.Select(arc => arc.Id).ToList());
    }

    /// <summary>
    /// 创建并校验分支遮罩 / Create and validate a branch mask.
    /// </summary>
    /// <remarks>
    /// 校验顺序：起止仓库不同 → 每条必选弧自身不矛盾（未被禁、同客户的必选出/入弧唯一、同客户的车辆类型唯一）→ 必选车辆
    /// 类型不与禁访冲突。
    /// **必选弧会被"翻译"成必选车辆类型**：若一条必选弧从客户 A 出发，则 A 必须由该弧的资源服务。少了这一步，
    /// 弧级分支与车辆类型级分支会各自独立生效，产生"某客户必须走这辆车，但那辆车又不许访问它"这种自相矛盾的遮罩。
    /// Check order: distinct depots → each required arc is self-consistent (not forbidden, unique required outgoing/incoming at
    /// a customer, unique vehicle type per customer) → required vehicle types do not collide with forbidden nodes.
    /// **A required arc is translated into a required vehicle type**: if a required arc leaves customer A, then A must be
    /// served by that arc's resource. Without this, arc-level and vehicle-type-level branches act independently and can
    /// produce the contradictory "this customer must use that vehicle, but that vehicle may not visit it".
    /// </remarks>
    /// <param name="startDepot">起始仓库 / Start depot.</param>
    /// <param name="endDepot">结束仓库 / End depot.</param>
    /// <param name="forbiddenNodes">各资源禁止访问的客户 / Customers forbidden for each resource.</param>
    /// <param name="requiredResources">客户必须使用的资源 / Required resource for each customer.</param>
    /// <param name="forbiddenArcs">禁止弧 / Forbidden arcs.</param>
    /// <param name="requiredArcs">必选弧 / Required arcs.</param>
    /// <returns>分支遮罩或冲突失败 / The branch mask, or a conflict failure.</returns>
    public static Result<BranchMask<K>, ErrorCode, Error<ErrorCode>> Invoke(
        NetworkNodeId startDepot,
        NetworkNodeId endDepot,
        IReadOnlyDictionary<K, IReadOnlySet<NetworkNodeId>>? forbiddenNodes = null,
        IReadOnlyDictionary<NetworkNodeId, K>? requiredResources = null,
        IReadOnlySet<ResourceArc<K>>? forbiddenArcs = null,
        IReadOnlySet<ResourceArc<K>>? requiredArcs = null) {
        ArgumentNullException.ThrowIfNull(startDepot);
        ArgumentNullException.ThrowIfNull(endDepot);

        if (startDepot.Equals(endDepot)) {
            return NetworkSchedulingErrors.Failure<BranchMask<K>>(
                "创建分支遮罩失败：起止仓库必须使用不同节点 ID / Failed to create branch mask: start and end depots must "
                + "use different node IDs");
        }

        var forbiddenNodeIndex = new Dictionary<K, HashSet<NetworkNodeId>>();
        if (forbiddenNodes is not null) {
            foreach (KeyValuePair<K, IReadOnlySet<NetworkNodeId>> pair in forbiddenNodes) {
                forbiddenNodeIndex[pair.Key] = new HashSet<NetworkNodeId>(pair.Value);
            }
        }
        Dictionary<NetworkNodeId, K> normalizedResources = requiredResources is null
            ? new Dictionary<NetworkNodeId, K>()
            : new Dictionary<NetworkNodeId, K>(requiredResources);
        HashSet<ResourceArc<K>> normalizedForbiddenArcs = forbiddenArcs is null
            ? new HashSet<ResourceArc<K>>()
            : new HashSet<ResourceArc<K>>(forbiddenArcs);
        HashSet<ResourceArc<K>> normalizedRequiredArcs = requiredArcs is null
            ? new HashSet<ResourceArc<K>>()
            : new HashSet<ResourceArc<K>>(requiredArcs);

        var requiredOutgoing = new Dictionary<NetworkNodeId, ResourceArc<K>>();
        var requiredIncoming = new Dictionary<NetworkNodeId, ResourceArc<K>>();
        foreach (ResourceArc<K> arc in normalizedRequiredArcs) {
            if (normalizedForbiddenArcs.Contains(arc)) {
                return NetworkSchedulingErrors.Failure<BranchMask<K>>(
                    $"创建分支遮罩失败：弧 {arc.From.Value}->{arc.To.Value} 同时被禁止和要求 / Failed to create branch mask: "
                    + $"arc {arc.From.Value}->{arc.To.Value} is both forbidden and required");
            }
            if (!arc.From.Equals(startDepot)) {
                if (requiredOutgoing.TryGetValue(arc.From, out ResourceArc<K>? previousOutgoing)
                    && !previousOutgoing.Equals(arc)) {
                    return NetworkSchedulingErrors.Failure<BranchMask<K>>(
                        $"创建分支遮罩失败：客户 {arc.From.Value} 存在冲突的必选出弧 / Failed to create branch mask: "
                        + $"customer {arc.From.Value} has conflicting required outgoing arcs");
                }
                requiredOutgoing[arc.From] = arc;
                if (normalizedResources.TryGetValue(arc.From, out K? previousResource)
                    && !EqualityComparer<K>.Default.Equals(previousResource, arc.ResourceKey)) {
                    return NetworkSchedulingErrors.Failure<BranchMask<K>>(
                        $"创建分支遮罩失败：客户 {arc.From.Value} 存在冲突的车辆类型 / Failed to create branch mask: "
                        + $"customer {arc.From.Value} has conflicting vehicle types");
                }
                normalizedResources[arc.From] = arc.ResourceKey;
            }
            if (!arc.To.Equals(endDepot)) {
                if (requiredIncoming.TryGetValue(arc.To, out ResourceArc<K>? previousIncoming)
                    && !previousIncoming.Equals(arc)) {
                    return NetworkSchedulingErrors.Failure<BranchMask<K>>(
                        $"创建分支遮罩失败：客户 {arc.To.Value} 存在冲突的必选入弧 / Failed to create branch mask: customer "
                        + $"{arc.To.Value} has conflicting required incoming arcs");
                }
                requiredIncoming[arc.To] = arc;
                if (normalizedResources.TryGetValue(arc.To, out K? previousResource)
                    && !EqualityComparer<K>.Default.Equals(previousResource, arc.ResourceKey)) {
                    return NetworkSchedulingErrors.Failure<BranchMask<K>>(
                        $"创建分支遮罩失败：客户 {arc.To.Value} 存在冲突的车辆类型 / Failed to create branch mask: "
                        + $"customer {arc.To.Value} has conflicting vehicle types");
                }
                normalizedResources[arc.To] = arc.ResourceKey;
            }
        }

        foreach (KeyValuePair<NetworkNodeId, K> pair in normalizedResources) {
            if (forbiddenNodeIndex.TryGetValue(pair.Value, out HashSet<NetworkNodeId>? forbidden)
                && forbidden.Contains(pair.Key)) {
                return NetworkSchedulingErrors.Failure<BranchMask<K>>(
                    $"创建分支遮罩失败：客户 {pair.Key.Value} 的必选车辆类型同时被禁止 / Failed to create branch mask: "
                    + $"required vehicle type for customer {pair.Key.Value} is forbidden");
            }
        }

        return Results.Ok(new BranchMask<K>(
            startDepot, endDepot, forbiddenNodeIndex, normalizedResources,
            normalizedForbiddenArcs, normalizedRequiredArcs));
    }
}
