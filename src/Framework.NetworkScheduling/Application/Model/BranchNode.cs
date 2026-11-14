// 分支决策与分支节点 / Branch decisions and branch nodes.
// 与固定 Kotlin 基线 `application/model/BranchDecision.kt`（5206 字节）、`BranchNode.kt`（5822 字节）对应。
// Mirrors the pinned baseline's `BranchDecision.kt` and `BranchNode.kt`.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Infrastructure;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Application.Model;

/// <summary>
/// 不可变分支决策标记接口 / Immutable branch-decision marker interface.
/// </summary>
/// <remarks>
/// 四种决策对应车辆类型分配分支与弧分支：<see cref="ForbidVehicleType"/> / <see cref="RequireVehicleType"/> 基于分配值，
/// <see cref="ForbidArc"/> / <see cref="RequireArc"/> 基于弧值。
/// The four decisions cover vehicle-type assignment branching and arc branching: <see cref="ForbidVehicleType"/> /
/// <see cref="RequireVehicleType"/> rest on the assignment value, <see cref="ForbidArc"/> / <see cref="RequireArc"/> on the arc value.
/// **决策必须不可变**。分支节点持有"从根到当前节点的全部决策"，任何就地修改都会同时改写祖先节点的历史，让回溯时重建的遮罩与
/// 求解时用过的遮罩不一致——而这种不一致只表现为"剪枝剪错了"，不会报错。
/// **Decisions must be immutable.** A branch node holds "all decisions from root to the current node"; mutating one in place would
/// rewrite ancestor history too, so the mask rebuilt during backtracking would disagree with the mask used during solving — and that
/// disagreement only ever shows up as "pruned the wrong subtree", never as an error.
/// </remarks>
public interface IBranchDecision {
}

/// <summary>
/// 车辆类型 k 禁止服务客户 i / Vehicle type k is forbidden to serve customer i.
/// </summary>
/// <param name="VehicleTypeId">车辆类型 / The vehicle type.</param>
/// <param name="CustomerId">客户 / The customer.</param>
/// <param name="CustomerNodeId">客户节点 / The customer's node.</param>
public sealed record ForbidVehicleType(
    VehicleTypeId VehicleTypeId,
    CustomerId CustomerId,
    NetworkNodeId CustomerNodeId) : IBranchDecision;

/// <summary>
/// 要求车辆类型 k 服务客户 i / Require vehicle type k to serve customer i.
/// </summary>
/// <param name="VehicleTypeId">车辆类型 / The vehicle type.</param>
/// <param name="CustomerId">客户 / The customer.</param>
/// <param name="CustomerNodeId">客户节点 / The customer's node.</param>
public sealed record RequireVehicleType(
    VehicleTypeId VehicleTypeId,
    CustomerId CustomerId,
    NetworkNodeId CustomerNodeId) : IBranchDecision;

/// <summary>
/// 车辆类型 k 禁止使用弧 (i, j) / Vehicle type k is forbidden to use arc (i, j).
/// </summary>
/// <param name="VehicleTypeId">车辆类型 / The vehicle type.</param>
/// <param name="From">起点 / Origin.</param>
/// <param name="To">终点 / Destination.</param>
/// <param name="ArcId">稳定弧标识 / Stable arc identity.</param>
public sealed record ForbidArc(
    VehicleTypeId VehicleTypeId,
    NetworkNodeId From,
    NetworkNodeId To,
    NetworkArcId? ArcId = null) : IBranchDecision;

/// <summary>
/// 强制弧 (i, j) 取值为 1 / Force arc (i, j) to take value 1.
/// </summary>
/// <remarks>
/// **必须识别仓库**：起点为起始仓库时只限制终点的其它入弧；终点为结束仓库时只限制起点的其它出弧。不得因要求一条仓库弧而禁止
/// 其它车辆路线离开或返回仓库——那会让整棵子树凭空不可行。
/// **Depots must be recognised**: when the origin is the start depot, only the destination's other incoming arcs are restricted;
/// when the destination is the end depot, only the origin's other outgoing arcs are restricted. Requiring one depot arc must not
/// ban other routes from leaving or returning to the depot — that would make the whole subtree spuriously infeasible.
/// </remarks>
/// <param name="VehicleTypeId">车辆类型 / The vehicle type.</param>
/// <param name="From">起点 / Origin.</param>
/// <param name="To">终点 / Destination.</param>
/// <param name="ArcId">稳定弧标识 / Stable arc identity.</param>
public sealed record RequireArc(
    VehicleTypeId VehicleTypeId,
    NetworkNodeId From,
    NetworkNodeId To,
    NetworkArcId? ArcId = null) : IBranchDecision;

/// <summary>
/// 把分支决策列表累积成 <see cref="BranchMask{K}"/> 的构造参数 / Accumulate branch decisions into
/// <see cref="BranchMask{K}"/> constructor parameters.
/// </summary>
/// <remarks>
/// **重复决策按集合语义收敛，而不是按顺序覆盖**：`RequireVehicleType` 对同一客户后写覆盖先写（那是冲突求解，后一次分支才有发言权），
/// 而禁止集与必选集都是集合，重复添加无副作用。若把四类都按"后写覆盖"处理，两条互斥的禁止决策会互相抹掉。
/// **Repeated decisions collapse by set semantics, not by last-write-wins**: `RequireVehicleType` for the same customer lets the
/// later write override the earlier one (that is how a conflict is resolved — the newer branch has the say), while the forbidden and
/// required arc sets are sets, so re-adding is a no-op. Treating all four as last-write-wins would let two mutually exclusive
/// forbiddances erase each other.
/// </remarks>
public static class BranchDecisionAccumulator {
/// <summary>
/// 累积分支决策 / Accumulate branch decisions.
/// </summary>
/// <param name="startDepot">起始仓库节点 / The start depot node.</param>
/// <param name="endDepot">结束仓库节点 / The end depot node.</param>
/// <param name="decisions">从根到当前节点的全部决策 / All decisions from root to the current node.</param>
/// <returns>不可变遮罩，或冲突失败 / The immutable mask, or a conflict failure.</returns>
    public static Result<BranchMask<VehicleTypeId>, ErrorCode, Error<ErrorCode>> AccumulateBranchMask(
        NetworkNodeId startDepot,
        NetworkNodeId endDepot,
        IReadOnlyList<IBranchDecision> decisions) {
    ArgumentNullException.ThrowIfNull(startDepot);
    ArgumentNullException.ThrowIfNull(endDepot);
    ArgumentNullException.ThrowIfNull(decisions);
    var forbiddenNodes = new Dictionary<VehicleTypeId, HashSet<NetworkNodeId>>();
    var requiredResources = new Dictionary<NetworkNodeId, VehicleTypeId>();
    var forbiddenArcs = new HashSet<ResourceArc<VehicleTypeId>>();
    var requiredArcs = new HashSet<ResourceArc<VehicleTypeId>>();

    foreach (IBranchDecision decision in decisions) {
        switch (decision) {
            case ForbidVehicleType forbid:
                if (!forbiddenNodes.TryGetValue(forbid.VehicleTypeId, out HashSet<NetworkNodeId>? set)) {
                    set = new HashSet<NetworkNodeId>();
                    forbiddenNodes[forbid.VehicleTypeId] = set;
                }
                set.Add(forbid.CustomerNodeId);
                break;
            case RequireVehicleType require:
                requiredResources[require.CustomerNodeId] = require.VehicleTypeId;
                break;
            case ForbidArc forbidArc:
                forbiddenArcs.Add(new ResourceArc<VehicleTypeId>(
                    forbidArc.VehicleTypeId,
                    forbidArc.From,
                    forbidArc.To,
                    forbidArc.ArcId ?? ResourceArcs.DefaultId(forbidArc.From, forbidArc.To)));
                break;
            case RequireArc requireArc:
                requiredArcs.Add(new ResourceArc<VehicleTypeId>(
                    requireArc.VehicleTypeId,
                    requireArc.From,
                    requireArc.To,
                    requireArc.ArcId ?? ResourceArcs.DefaultId(requireArc.From, requireArc.To)));
                break;
            default:
                break;
        }
    }

    return BranchMask<VehicleTypeId>.Invoke(
        startDepot,
        endDepot,
        forbiddenNodes.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlySet<NetworkNodeId>)pair.Value,
            EqualityComparer<VehicleTypeId>.Default),
        requiredResources,
        forbiddenArcs,
        requiredArcs);
    }
}

/// <summary>
/// 分支树节点状态 / Branch tree node status.
/// </summary>
public enum NodeStatus {
/// <summary>尚未求解 / Not yet solved.</summary>
    Pending,

/// <summary>已求解 / Solved.</summary>
    Solved,

/// <summary>已剪枝 / Pruned.</summary>
    Pruned,

/// <summary>不可行 / Infeasible.</summary>
    Infeasible,
}

/// <summary>
/// 分支树节点 / Branch tree node.
/// </summary>
/// <remarks>
/// 每个节点持有从根到当前节点的全部不可变分支决策、构造好的不可变 <see cref="BranchMask{K}"/>，以及继承与自身的 LP 下界。
/// **可变 model / context 不跨节点复用**：共享它们会让一个节点的定价改动污染兄弟节点的状态，而树搜索的前提正是"节点之间独立"。
/// Each node holds all immutable branch decisions from root to the current node, the constructed immutable
/// <see cref="BranchMask{K}"/>, and both inherited and own LP lower bounds. **Mutable model / context is not shared across
/// nodes**: sharing it would let one node's pricing corrupt a sibling's state, while tree search presupposes nodes are independent.
/// </remarks>
public sealed class BranchNode {
/// <summary>
/// 构造分支树节点 / Construct a branch-tree node.
/// </summary>
/// <param name="Id">节点唯一标识 / Unique node identity.</param>
/// <param name="ParentId">父节点标识（根节点为 null）/ Parent identity (null for the root).</param>
/// <param name="Depth">树深度 / Tree depth.</param>
/// <param name="Decisions">从根到本节点的全部决策 / All decisions from root to this node.</param>
/// <param name="BranchMask">由决策构造的不可变遮罩 / The immutable mask built from the decisions.</param>
/// <param name="InheritedLowerBound">继承的父节点 LP 下界 / The inherited parent LP lower bound.</param>
    public BranchNode(
        int Id,
        int? ParentId,
        int Depth,
        IReadOnlyList<IBranchDecision> Decisions,
        BranchMask<VehicleTypeId> BranchMask,
        Flt64 InheritedLowerBound) {
        ArgumentNullException.ThrowIfNull(Decisions);
        ArgumentNullException.ThrowIfNull(BranchMask);
        this.Id = Id;
        this.ParentId = ParentId;
        this.Depth = Depth;
        this.Decisions = Decisions;
        this.BranchMask = BranchMask;
        this.InheritedLowerBound = InheritedLowerBound;
        LowerBound = InheritedLowerBound;
        Status = NodeStatus.Pending;
    }

/// <summary>节点唯一标识 / Unique node identity.</summary>
    public int Id { get; }

/// <summary>父节点标识（根节点为 null）/ Parent identity (null for the root).</summary>
    public int? ParentId { get; }

/// <summary>树深度 / Tree depth.</summary>
    public int Depth { get; }

/// <summary>从根到本节点的全部决策 / All decisions from root to this node.</summary>
    public IReadOnlyList<IBranchDecision> Decisions { get; }

/// <summary>由决策构造的不可变遮罩 / The immutable mask built from the decisions.</summary>
    public BranchMask<VehicleTypeId> BranchMask { get; }

/// <summary>继承的父节点 LP 下界 / The inherited parent LP lower bound.</summary>
    public Flt64 InheritedLowerBound { get; }

/// <summary>本节点 LP 下界（求解后更新）/ The node's own LP lower bound, updated after solving.</summary>
    public Flt64 LowerBound { get; private set; }

/// <summary>节点状态 / Node status.</summary>
    public NodeStatus Status { get; private set; }

/// <summary>
/// 有效下界：求解后由本节点 LP 下界替换继承值，未求解节点仍使用继承值。
/// The effective lower bound: after solving, the node LP bound replaces the inherited value; pending nodes still use the
/// inherited value.
/// </summary>
    public Flt64 EffectiveLowerBound => LowerBound;

/// <summary>
/// 标记节点为已求解并更新下界 / Mark the node solved and update its lower bound.
/// </summary>
/// <param name="lpLowerBound">定价完成后的 LP 目标值 / The LP objective after pricing completes.</param>
    public void Solve(Flt64 lpLowerBound) {
        LowerBound = lpLowerBound;
        Status = NodeStatus.Solved;
    }

/// <summary>标记节点为不可行 / Mark the node infeasible.</summary>
    public void MarkInfeasible() => Status = NodeStatus.Infeasible;

/// <summary>标记节点为已剪枝 / Mark the node pruned.</summary>
    public void Prune() => Status = NodeStatus.Pruned;

/// <summary>
/// 判断节点是否仍有可能改进 incumbent / Whether the node can still improve the incumbent.
/// </summary>
/// <remarks>
/// **无 incumbent 时一律返回 true**，包括下界为 −∞ 的根节点：此时没有任何参照，任何节点都可能改进。
/// **With no incumbent it always returns true**, including for a root node whose bound is −∞: there is no reference at all,
/// so any node might improve it.
/// </remarks>
/// <param name="incumbent">当前 incumbent / The current incumbent.</param>
/// <param name="tolerance">比较容差 / Comparison tolerance.</param>
/// <returns>是否仍可改进 / Whether it can still improve.</returns>
    public bool CanImprove(Flt64? incumbent, Flt64 tolerance) {
        if (incumbent is not { } bound) {
            return true;
        }
        return EffectiveLowerBound.CompareTo(bound.Minus(tolerance)) < 0;
    }

/// <summary>
/// 创建根节点 / Create the root node.
/// </summary>
/// <param name="startDepot">起始仓库 / The start depot.</param>
/// <param name="endDepot">结束仓库 / The end depot.</param>
/// <param name="rootLowerBound">根节点初始下界 / The root's initial lower bound.</param>
/// <returns>根节点，或遮罩构造失败 / The root node, or a mask-construction failure.</returns>
    public static Result<BranchNode, ErrorCode, Error<ErrorCode>> Root(
        NetworkNodeId startDepot,
        NetworkNodeId endDepot,
        Flt64? rootLowerBound = null) {
        Result<BranchMask<VehicleTypeId>, ErrorCode, Error<ErrorCode>> mask =
            BranchMask<VehicleTypeId>.Invoke(startDepot, endDepot);
        if (mask is Failed<BranchMask<VehicleTypeId>, ErrorCode, Error<ErrorCode>> failure) {
            return Results.Failed<BranchNode>(failure.Error);
        }
        return Results.Ok(new BranchNode(
            0,
            null,
            0,
            Array.Empty<IBranchDecision>(),
            ((Ok<BranchMask<VehicleTypeId>, ErrorCode, Error<ErrorCode>>)mask).Value,
            rootLowerBound ?? new Flt64(double.NegativeInfinity)));
    }

/// <summary>
/// 创建子节点 / Create a child node.
/// </summary>
/// <param name="id">子节点标识 / The child identity.</param>
/// <param name="parent">父节点 / The parent node.</param>
/// <param name="decision">新增分支决策 / The new branch decision.</param>
/// <param name="startDepot">起始仓库 / The start depot.</param>
/// <param name="endDepot">结束仓库 / The end depot.</param>
/// <returns>子节点，或遮罩构造失败 / The child node, or a mask-construction failure.</returns>
    public static Result<BranchNode, ErrorCode, Error<ErrorCode>> Child(
        int id,
        BranchNode parent,
        IBranchDecision decision,
        NetworkNodeId startDepot,
        NetworkNodeId endDepot) {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(decision);
        var childDecisions = new List<IBranchDecision>(parent.Decisions) { decision };
        Result<BranchMask<VehicleTypeId>, ErrorCode, Error<ErrorCode>> mask =
            BranchDecisionAccumulator.AccumulateBranchMask(startDepot, endDepot, childDecisions);
        if (mask is Failed<BranchMask<VehicleTypeId>, ErrorCode, Error<ErrorCode>> failure) {
            return Results.Failed<BranchNode>(failure.Error);
        }
        return Results.Ok(new BranchNode(
            id,
            parent.Id,
            parent.Depth + 1,
            childDecisions,
            ((Ok<BranchMask<VehicleTypeId>, ErrorCode, Error<ErrorCode>>)mask).Value,
            parent.LowerBound));
    }
}
