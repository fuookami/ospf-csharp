// 网络结构与设施分区问题 / Network-structure and facility-partition problem.
// 与固定 Kotlin 基线 `structure/NetworkStructureProblem.kt`（3489 字节）对应。
// Mirrors the pinned baseline's `NetworkStructureProblem.kt`.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Design;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Concept;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Domain.Structure;

/// <summary>
/// 可由哪些设施负责的分区覆盖请求 / Partition-coverage request and its eligible owners.
/// </summary>
/// <param name="Id">稳定分区标识 / Stable partition identity.</param>
/// <param name="EligibleFacilities">可负责该分区的设施 / Facilities eligible to own this partition.</param>
/// <param name="Required">是否必须归属一个设施 / Whether this partition must be assigned.</param>
public sealed class FacilityPartitionRequest(
    string Id,
    IReadOnlySet<NetworkNodeId> EligibleFacilities,
    bool Required = true) {
    /// <summary>稳定分区标识 / Stable partition identity.</summary>
    public string Id { get; } = Id;

    /// <summary>可负责该分区的设施 / Facilities eligible to own this partition.</summary>
    public IReadOnlySet<NetworkNodeId> EligibleFacilities { get; } =
        new HashSet<NetworkNodeId>(EligibleFacilities ?? throw new ArgumentNullException(nameof(EligibleFacilities)));

    /// <summary>是否必须归属一个设施 / Whether this partition must be assigned.</summary>
    public bool Required { get; } = Required;
}

/// <summary>
/// 设施被分配的分区数量配额 / Assignment-count quota for a facility.
/// </summary>
/// <param name="Facility">设施节点 / Facility node.</param>
/// <param name="Minimum">最少归属分区数 / Minimum owned partitions.</param>
/// <param name="Maximum">最多归属分区数，null 表示不额外限制 / Maximum owned partitions, null for no extra limit.</param>
public sealed record FacilityAssignmentQuota(
    NetworkNodeId Facility,
    int Minimum = 0,
    int? Maximum = null);

/// <summary>
/// 根有向树与设施分区归属问题 / Rooted directed-tree and facility-partition assignment problem.
/// </summary>
/// <remarks>
/// **分区覆盖独立于商品流**：分区请求不表达"多少货从哪走到哪"，只表达"这块区域归哪个设施管"。把两者混在一个模型里会让分区决策被
/// 流量约束牵着走，而它们在业务上并不耦合。
/// **Partition coverage is independent of commodity flows**: a partition request says nothing about how much cargo moves where, only
/// which facility governs that area. Folding both into one model would let flow constraints drag the partition decision along even
/// though the two are not coupled in the business.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class NetworkStructureProblem<V> where V : struct, IRealNumber<V> {
    /// <summary>
    /// 构造网络结构问题 / Construct a network-structure problem.
    /// </summary>
    /// <param name="design">建设问题 / Network construction problem.</param>
    /// <param name="root">必须作为根的节点 / Required root node.</param>
    /// <param name="terminals">必须连接到根的节点 / Nodes that must connect to the root.</param>
    /// <param name="steinerNodes">可选中转节点 / Optional relay nodes.</param>
    /// <param name="partitions">独立于商品流的分区覆盖请求 / Partition requests independent of commodity flows.</param>
    /// <param name="quotas">设施分区归属数量配额 / Facility assignment-count quotas.</param>
    public NetworkStructureProblem(
        NetworkDesignProblem<V> design,
        NetworkNodeId root,
        IReadOnlySet<NetworkNodeId> terminals,
        IReadOnlySet<NetworkNodeId>? steinerNodes = null,
        IReadOnlyList<FacilityPartitionRequest>? partitions = null,
        IReadOnlyList<FacilityAssignmentQuota>? quotas = null) {
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(terminals);
        Design = design;
        Root = root;
        Terminals = new HashSet<NetworkNodeId>(terminals);
        SteinerNodes = steinerNodes is null
            ? new HashSet<NetworkNodeId>()
            : new HashSet<NetworkNodeId>(steinerNodes);
        Partitions = partitions?.ToList() ?? new List<FacilityPartitionRequest>();
        Quotas = quotas?.ToList() ?? new List<FacilityAssignmentQuota>();
    }

    /// <summary>建设问题 / The network construction problem.</summary>
    public NetworkDesignProblem<V> Design { get; }

    /// <summary>必须作为根的节点 / The required root node.</summary>
    public NetworkNodeId Root { get; }

    /// <summary>必须连接到根的节点 / Nodes that must connect to the root.</summary>
    public IReadOnlySet<NetworkNodeId> Terminals { get; }

    /// <summary>可选中转节点 / Optional relay nodes.</summary>
    public IReadOnlySet<NetworkNodeId> SteinerNodes { get; }

    /// <summary>独立于商品流的分区覆盖请求 / Partition requests independent of commodity flows.</summary>
    public IReadOnlyList<FacilityPartitionRequest> Partitions { get; }

    /// <summary>设施分区归属数量配额 / Facility assignment-count quotas.</summary>
    public IReadOnlyList<FacilityAssignmentQuota> Quotas { get; }

    /// <summary>根树可能涉及的节点 / Nodes the rooted tree may involve.</summary>
    public IReadOnlySet<NetworkNodeId> TreeNodes {
        get {
            var nodes = new HashSet<NetworkNodeId>(Terminals) { Root };
            nodes.UnionWith(SteinerNodes);
            return nodes;
        }
    }
}

/// <summary>
/// 已注册结构的建设、树和分区归属结果 / Construction, tree and partition assignments.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="Construction">设施与服务建设选择 / Facility and service construction decisions.</param>
/// <param name="SelectedTreeArcs">选中的有向树弧 / Selected directed tree arcs.</param>
/// <param name="ConnectedNodes">根树中的节点 / Nodes in the rooted tree.</param>
/// <param name="ParentArcs">每个非根连接节点的父弧 / Parent arc for each connected non-root node.</param>
/// <param name="PartitionOwners">已归属分区的设施 / Facility owning each assigned partition.</param>
public sealed record NetworkStructureSolution<V>(
    NetworkConstructionSolution<V> Construction,
    IReadOnlySet<NetworkArcId> SelectedTreeArcs,
    IReadOnlySet<NetworkNodeId> ConnectedNodes,
    IReadOnlyDictionary<NetworkNodeId, NetworkArcId> ParentArcs,
    IReadOnlyDictionary<string, NetworkNodeId> PartitionOwners) where V : struct, IRealNumber<V>;
