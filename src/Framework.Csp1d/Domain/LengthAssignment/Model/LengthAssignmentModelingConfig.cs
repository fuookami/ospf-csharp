#nullable enable

using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.LengthAssignment.Model;
/// <summary>
/// 长度分配建模配置，供 application 层消费 / Length assignment modeling configuration for application layer.
///
/// 当提供此配置时，Csp1dMilpSolver 为动态长度产品注册卷长变量、超长松弛变量、边界约束和惩罚目标。
/// 所有权重为无量纲归一化系数，由调用方负责单位换算。
/// When provided, Csp1dMilpSolver registers length variables, over-length slack variables,
/// bound constraints, and penalty objectives for dynamic-length products.
/// All weights are dimensionless normalized coefficients; unit conversion is caller's responsibility.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
/// <param name="DynamicProductIds">需要动态分配卷长的产品 ID 集合 / Product IDs requiring dynamic length assignment.</param>
/// <param name="AssignedLengthLowerBound">已分配卷长下界，按产品 ID 口径，缺省表示无下界 / Assigned length lower bound per product id, missing for no lower bound.</param>
/// <param name="AssignedLengthUpperBound">已分配卷长上界，按产品 ID 口径，缺省表示无上界 / Assigned length upper bound per product id, missing for no upper bound.</param>
/// <param name="OverLengthPenalty">超长惩罚权重，按产品 ID 口径 / Over-length penalty weight per product id.</param>
/// <param name="OverLengthUpperBound">超长上限，按产品 ID 口径，缺省表示无上限 / Over-length upper bound per product id, missing for no limit.</param>
/// <param name="TotalLengthPenalty">总卷长惩罚权重 / Total assigned length penalty weight.</param>
/// <param name="BatchMinPenalty">批次最小化惩罚权重（动态长度产品）/ Batch minimization penalty weight for dynamic-length products.</param>
public sealed record LengthAssignmentModelingConfig<V>(
    IReadOnlySet<string>? DynamicProductIds = null,
    IReadOnlyDictionary<string, V>? AssignedLengthLowerBound = null,
    IReadOnlyDictionary<string, V>? AssignedLengthUpperBound = null,
    IReadOnlyDictionary<string, V>? OverLengthPenalty = null,
    IReadOnlyDictionary<string, V>? OverLengthUpperBound = null,
    V? TotalLengthPenalty = null,
    V? BatchMinPenalty = null
) where V : struct;

/// <summary>
/// 长度分配建模结果，从 solver solution 回填 / Length assignment modeling result back-filled from solver solution.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
/// <param name="AssignedLengths">动态长度产品的卷长分配值 / Assigned lengths for dynamic-length products.</param>
/// <param name="OverLengths">超长产品值 / Over-length values for products exceeding bounds.</param>
public sealed record LengthAssignmentModelingResult<V>(
    IReadOnlyList<ModeledAssignedLength<V>>? AssignedLengths = null,
    IReadOnlyList<ModeledOverLength<V>>? OverLengths = null
) where V : struct;

/// <summary>
/// 动态卷长分配结果（建模层扁平类型）/ Dynamic length assignment result (flat modeling type).
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
/// <param name="ProductId">产品 ID / Product id.</param>
/// <param name="AssignedLength">分配的卷长值 / Assigned length value.</param>
public sealed record ModeledAssignedLength<V>(
    string ProductId,
    V AssignedLength
) where V : struct;

/// <summary>
/// 超长建模结果（建模层扁平类型）/ Over-length modeling result (flat modeling type).
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
/// <param name="ProductId">产品 ID / Product id.</param>
/// <param name="OverLength">超长值 / Over-length value.</param>
public sealed record ModeledOverLength<V>(
    string ProductId,
    V OverLength
) where V : struct;
