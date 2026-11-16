// 车辆资源维度的有向弧 / Directed arc in a vehicle-resource dimension.
// And.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Infrastructure;

/// <summary>
/// 资源弧标识工具 / Resource-arc identity helpers.
/// </summary>
public static class ResourceArcs {
    /// <summary>
    /// 为端点唯一的旧式弧创建确定性标识 / Derive a deterministic identity for legacy endpoint-unique arcs.
    /// </summary>
    /// <remarks>
    /// 与 <see cref="NetworkArc{V}"/> 的默认标识同一套规则：**长度前缀拼接**，让 `("ab","c")` 与 `("a","bc")` 不会撞成同一个
    /// `"abc"`。平行弧必须显式给标识，否则会互相顶替。
    /// Same rule as <see cref="NetworkArc{V}"/>'s default identity: **length-prefixed concatenation**, so `("ab","c")` and
    /// `("a","bc")` do not collide on `"abc"`. Parallel arcs must carry explicit identities or they overwrite each other.
    /// </remarks>
    /// <param name="from">起点 / Origin.</param>
    /// <param name="to">终点 / Destination.</param>
    /// <returns>确定性弧标识 / A deterministic arc identity.</returns>
    public static NetworkArcId DefaultId(NetworkNodeId from, NetworkNodeId to) {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        return new NetworkArcId($"{from.Value.Length}:{from.Value}{to.Value.Length}:{to.Value}");
    }
}

/// <summary>
/// 车辆资源维度的有向弧 / Directed arc in a vehicle-resource dimension.
/// </summary>
/// <remarks>
/// 用 `record` 而非类：`data class`，两侧的 `Set` 成员判断（`arc in forbiddenArcs`）与冲突比较（`arc != required`）
/// 都依赖**结构相等**。四个字段全是浅值对象，结构相等在这里既正确又便宜。
/// A `record` rather than a class: the code is a `data class`, and both the `Set` membership test
/// (`arc in forbiddenArcs`) and the conflict comparison (`arc != required`) rely on **structural equality**. All four fields
/// are shallow value objects, so structural equality is both correct and cheap here.
/// </remarks>
/// <typeparam name="K">车辆资源稳定键类型 / Vehicle-resource stable-key type.</typeparam>
/// <param name="ResourceKey">资源稳定键 / Stable resource key.</param>
/// <param name="From">起点 / Origin.</param>
/// <param name="To">终点 / Destination.</param>
/// <param name="Id">独立稳定弧标识 / Independent stable arc identity.</param>
public sealed record ResourceArc<K>(
    K ResourceKey,
    NetworkNodeId From,
    NetworkNodeId To,
    NetworkArcId Id) where K : notnull {
    /// <summary>
    /// 以默认标识构造资源弧 / Construct a resource arc with the default identity.
    /// </summary>
    /// <param name="resourceKey">资源稳定键 / Stable resource key.</param>
    /// <param name="from">起点 / Origin.</param>
    /// <param name="to">终点 / Destination.</param>
    public ResourceArc(K resourceKey, NetworkNodeId from, NetworkNodeId to)
        : this(resourceKey, from, to, ResourceArcs.DefaultId(from, to)) { }
}

/// <summary>
/// 车辆资源维度的不可变有向多重图 / Immutable directed multigraph partitioned by vehicle resource.
/// </summary>
/// <typeparam name="K">车辆资源稳定键类型 / Vehicle-resource stable-key type.</typeparam>
public sealed class ResourceGraph<K> where K : notnull {
    private readonly List<NetworkNodeId> _nodes;
    private readonly List<ResourceArc<K>> _arcs;
    private readonly Dictionary<(K Key, NetworkNodeId From), List<ResourceArc<K>>> _outgoing;
    private readonly Dictionary<(K Key, NetworkNodeId From, NetworkNodeId To), List<ResourceArc<K>>> _between;

    private ResourceGraph(List<NetworkNodeId> nodes, List<ResourceArc<K>> arcs) {
        _nodes = nodes;
        _arcs = arcs;
        _outgoing = new Dictionary<(K, NetworkNodeId), List<ResourceArc<K>>>();
        _between = new Dictionary<(K, NetworkNodeId, NetworkNodeId), List<ResourceArc<K>>>();
        foreach (ResourceArc<K> arc in arcs) {
            Append(_outgoing, (arc.ResourceKey, arc.From), arc);
            Append(_between, (arc.ResourceKey, arc.From, arc.To), arc);
        }
    }

    /// <summary>图节点 / Graph nodes.</summary>
    public IReadOnlyList<NetworkNodeId> Nodes => _nodes;

    /// <summary>资源弧 / Resource arcs.</summary>
    public IReadOnlyList<ResourceArc<K>> Arcs => _arcs;

    /// <summary>
    /// 获取资源从节点出发的弧 / Get arcs leaving a node for a resource.
    /// </summary>
    /// <param name="resourceKey">资源稳定键 / Stable resource key.</param>
    /// <param name="from">起点 / Origin.</param>
    /// <returns>弧快照 / Arc snapshot.</returns>
    public IReadOnlyList<ResourceArc<K>> Outgoing(K resourceKey, NetworkNodeId from) {
        ArgumentNullException.ThrowIfNull(from);
        return _outgoing.TryGetValue((resourceKey, from), out List<ResourceArc<K>>? arcs)
            ? arcs
            : Array.Empty<ResourceArc<K>>();
    }

    /// <summary>
    /// 获取指定端点间的全部平行弧 / Get all parallel arcs between two endpoints.
    /// </summary>
    /// <param name="resourceKey">资源稳定键 / Stable resource key.</param>
    /// <param name="from">起点 / Origin.</param>
    /// <param name="to">终点 / Destination.</param>
    /// <returns>弧快照 / Arc snapshot.</returns>
    public IReadOnlyList<ResourceArc<K>> Between(K resourceKey, NetworkNodeId from, NetworkNodeId to) {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        return _between.TryGetValue((resourceKey, from, to), out List<ResourceArc<K>>? arcs)
            ? arcs
            : Array.Empty<ResourceArc<K>>();
    }

    /// <summary>
    /// 创建并校验资源多重图 / Create and validate a resource multigraph.
    /// </summary>
    /// <remarks>
    /// 三条校验：节点标识非空且唯一 → 弧标识在同一资源内唯一 → 弧引用已存在节点。
    /// **弧标识唯一性按"资源 + 标识"判**，而不是全局唯一：不同车辆类型走同一条物理路是完全正常的，强行全局唯一会让
    /// 资源维度失去意义。平行弧（同资源、同端点、多个标识）则必须用不同标识区分，否则会互相顶替。
    /// Three checks: node identities non-blank and unique → arc identities unique within a resource → arcs reference existing
    /// nodes. **Arc identity uniqueness is scoped to "resource + identity"** rather than global: different vehicle types using
    /// the same physical road is entirely normal, and forcing global uniqueness would defeat the resource dimension. Parallel
    /// arcs (same resource, same endpoints, several identities) must differ in identity or they overwrite each other.
    /// </remarks>
    /// <param name="nodes">图节点 / Graph nodes.</param>
    /// <param name="arcs">资源弧，平行弧须使用不同标识 / Resource arcs; parallel arcs need distinct identities.</param>
    /// <returns>图或输入校验失败 / The graph, or an input-validation failure.</returns>
    public static Result<ResourceGraph<K>, ErrorCode, Error<ErrorCode>> Invoke(
        IReadOnlyList<NetworkNodeId> nodes,
        IReadOnlyList<ResourceArc<K>> arcs) {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(arcs);

        var nodeSet = nodes.ToHashSet();
        if (nodes.Any(node => string.IsNullOrWhiteSpace(node.Value)) || nodeSet.Count != nodes.Count) {
            return NetworkSchedulingErrors.Failure<ResourceGraph<K>>(
                "资源图节点标识为空或重复 / Blank or duplicate resource-graph node identity");
        }
        if (arcs.Any(arc => string.IsNullOrWhiteSpace(arc.Id.Value))
            || arcs.Select(arc => (arc.ResourceKey, arc.Id)).Distinct().Count() != arcs.Count) {
            return NetworkSchedulingErrors.Failure<ResourceGraph<K>>(
                "资源图弧标识为空或同一资源内重复 / Blank or duplicate arc identity within a resource");
        }
        if (arcs.Any(arc => !nodeSet.Contains(arc.From) || !nodeSet.Contains(arc.To))) {
            return NetworkSchedulingErrors.Failure<ResourceGraph<K>>(
                "资源图弧引用缺失节点 / Resource arc references a missing node");
        }
        return Results.Ok(new ResourceGraph<K>(nodes.ToList(), arcs.ToList()));
    }

    /// <summary>向分组索引追加一条弧 / Append one arc to a grouping index.</summary>
    /// <typeparam name="TKey">分组键类型 / Grouping key type.</typeparam>
    /// <param name="index">分组索引 / The grouping index.</param>
    /// <param name="key">分组键 / The grouping key.</param>
    /// <param name="arc">资源弧 / The resource arc.</param>
    private static void Append<TKey>(
        Dictionary<TKey, List<ResourceArc<K>>> index,
        TKey key,
        ResourceArc<K> arc) where TKey : notnull {
        if (!index.TryGetValue(key, out List<ResourceArc<K>>? bucket)) {
            bucket = new List<ResourceArc<K>>();
            index[key] = bucket;
        }
        bucket.Add(arc);
    }
}
