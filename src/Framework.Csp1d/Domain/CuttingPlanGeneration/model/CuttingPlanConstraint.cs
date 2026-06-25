#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;
/// <summary>
/// 切割方案约束评估上下文 / Cutting plan constraint evaluation context.
///
/// 在生成器搜索过程中传递给约束 predicate 的上下文，
/// 包含当前已生成的 slices、累计宽度、物料幅宽上界和物料信息。
/// Context passed to constraint predicates during generator search,
/// containing current slices, cumulative width, material width upper bound, and material info.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
/// <param name="Slices">当前切片列表 / Current slice list.</param>
/// <param name="TotalWidth">累计总宽度 / Cumulative total width.</param>
/// <param name="UpperBound">物料幅宽上界 / Material width upper bound.</param>
/// <param name="MaterialId">物料标识 / Material identifier.</param>
public sealed record CuttingPlanConstraintContext<V>(
    IReadOnlyList<CuttingPlanSliceStub<V>> Slices,
    Quantity<V> TotalWidth,
    Quantity<V> UpperBound,
    string MaterialId
) where V : struct;

/// <summary>
/// 切片存根，用于约束评估 / Slice stub for constraint evaluation.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
/// <param name="ProductionType">产出类型 / Production type.</param>
/// <param name="ProductionLength">产出长度（可空）/ Production length (nullable).</param>
/// <param name="Width">宽度 / Width.</param>
/// <param name="Amount">数量 / Amount.</param>
public sealed record CuttingPlanSliceStub<V>(
    string ProductionType,
    Quantity<V>? ProductionLength,
    Quantity<V> Width,
    UInt64 Amount
) where V : struct;

/// <summary>
/// 切割方案约束 predicate / Cutting plan constraint predicate.
///
/// 可组合的约束接口，在生成器搜索过程中用于剪枝和方案可行性判断。
/// Composable constraint interface used for pruning and plan feasibility during generator search.
///
/// 约束分两类 / Two types of constraints:
/// - 剪枝约束（IsPruning = true）：在搜索中间节点检查，用于提前剪枝无效分支
///   Pruning constraints (IsPruning = true): checked at intermediate search nodes for early pruning
/// - 叶节点约束（IsPruning = false）：仅在产出方案时检查，中间节点可能尚未满足
///   Leaf constraints (IsPruning = false): only checked when emitting plans, may not be satisfied at intermediate nodes
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public interface ICuttingPlanConstraint<V> where V : struct {
    /// <summary>
    /// 判断当前搜索上下文是否满足约束 / Check whether the current search context satisfies the constraint.
    /// </summary>
    /// <param name="context">约束评估上下文 / Constraint evaluation context.</param>
    /// <returns>true 如果约束满足；false 如果违反约束 / true if satisfied; false if violated.</returns>
    bool IsSatisfied(CuttingPlanConstraintContext<V> context);

    /// <summary>
    /// 是否为剪枝约束 / Whether this is a pruning constraint.
    /// </summary>
    bool IsPruning => true;
}
