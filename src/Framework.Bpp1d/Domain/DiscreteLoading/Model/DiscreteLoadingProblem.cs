// 无维度绑定的稀疏装载问题 / Dimension-independent sparse loading problem.
// 与固定 Kotlin 对应。
// The pinned Kotlin: .

using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;

namespace Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model;

/// <summary>
/// 离散货物，身份不可因尺寸相同而合并 / Discrete cargo whose identity is never merged.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="Id">货物标识 / Cargo identity.</param>
/// <param name="Weight">单件重量 / Item weight.</param>
/// <param name="Required">是否必须装载 / Whether loading is mandatory.</param>
/// <param name="FixedPositionId">固定装载位置 / Fixed position.</param>
public sealed record LoadingItem<V>(
    string Id,
    Quantity<V> Weight,
    bool Required = false,
    string? FixedPositionId = null)
    where V : struct, IFloatingNumber<V>;

/// <summary>
/// 固定位置的容量与占用 / Fixed-position capacity and occupancy.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="Id">舱位标识 / Position identity.</param>
/// <param name="MaxCount">最大件数 / Maximum item count.</param>
/// <param name="MaxWeight">最大总重量 / Maximum total weight.</param>
/// <param name="FixedOccupied">是否有外部固定占用 / External fixed occupancy.</param>
/// <param name="Enabled">是否允许决策装载 / Whether decision loading is allowed.</param>
public sealed record LoadingPosition<V>(
    string Id,
    ulong MaxCount,
    Quantity<V> MaxWeight,
    bool FixedOccupied = false,
    bool Enabled = true)
    where V : struct, IFloatingNumber<V>;

/// <summary>
/// 合法分配的稳定键 / Stable key for a legal assignment.
/// </summary>
/// <param name="ItemId">货物 ID / Cargo ID.</param>
/// <param name="PositionId">舱位 ID / Position ID.</param>
public sealed record AssignmentKey(string ItemId, string PositionId);

/// <summary>
/// 独立于几何的互斥边；不具有传递性 / Geometry-independent, non-transitive conflict edge.
/// </summary>
/// <param name="First">首舱位 / First position.</param>
/// <param name="Second">次舱位 / Second position.</param>
public sealed record PositionConflict(string First, string Second);

/// <summary>
/// 一种布局允许使用的舱位 / Positions permitted by one layout.
/// </summary>
/// <param name="Id">布局标识 / Layout identity.</param>
/// <param name="PositionIds">允许使用的舱位 / Permitted positions.</param>
public sealed record LoadingLayout(string Id, IReadOnlySet<string> PositionIds);

/// <summary>
/// 在显式作用域内恰选一种布局 / Select exactly one layout within an explicit scope.
/// </summary>
/// <param name="Id">选择组标识 / Group identity.</param>
/// <param name="PositionIds">受本选择组约束的舱位 / Positions governed by this group.</param>
/// <param name="Layouts">可选布局，共享舱位可出现在多个布局 / Layouts, with shared positions allowed.</param>
public sealed record LayoutSelectionGroup(
    string Id,
    IReadOnlySet<string> PositionIds,
    IReadOnlyList<LoadingLayout> Layouts);

/// <summary>
/// 无维度绑定的稀疏装载问题 / Dimension-independent sparse loading problem.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="Items">货物 / Items.</param>
/// <param name="Positions">舱位 / Positions.</param>
/// <param name="AllowedAssignments">合法组合，不自动补全笛卡尔积 / Legal combinations, never implicitly completed.</param>
/// <param name="Conflicts">必须互斥的舱位 / Conflicting positions.</param>
/// <param name="LayoutGroups">可选的整机或区域布局选择 / Optional global or regional layout choices.</param>
/// <param name="WeightUnit">求解器重量口径 / Solver weight unit.</param>
/// <remarks>
/// **`AllowedAssignments` 不会被自动补全成笛卡尔积。** 基线明确如此：
/// 只给"合法的组合"，未列出的组合就是**不允许**。自动补全会让"这条航线不能装这件货"
/// 变成"能装"，而模型不会报错——**一个会静默放大可行域的默认值**。
///
/// **`AllowedAssignments` is never completed into a Cartesian product.** The code is explicit: only the listed combinations are legal, and anything unlisted is
/// **forbidden**. Auto-completion would turn "this cargo may not ride this route" into "it may", with no model error — **a default that silently widens the feasible set**.
/// </remarks>
public sealed record DiscreteLoadingProblem<V>(
    IReadOnlyList<LoadingItem<V>> Items,
    IReadOnlyList<LoadingPosition<V>> Positions,
    IReadOnlySet<AssignmentKey> AllowedAssignments,
    IReadOnlySet<PositionConflict> Conflicts,
    IReadOnlyList<LayoutSelectionGroup> LayoutGroups,
    PhysicalUnit WeightUnit)
    where V : struct, IFloatingNumber<V>;
