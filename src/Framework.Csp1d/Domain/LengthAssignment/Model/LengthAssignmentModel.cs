#nullable enable

using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Quantity;
using System.Collections.Generic;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.LengthAssignment.Model;
/// <summary>
/// 卷长分配结果，描述单个产品被分配的卷长 / Length assignment result for a single product.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
/// <param name="Product">产品 / Product.</param>
/// <param name="AssignedLength">分配的卷长 / Assigned coil length.</param>
/// <param name="BatchCount">分配的批次数 / Assigned batch count.</param>
public sealed record LengthAssignmentRecord<V>(
    Product<V> Product,
    Quantity<V> AssignedLength,
    UInt64 BatchCount
) where V : struct;

/// <summary>
/// 超长记录，产品实际卷长超过最大超产长度的部分 / Over-length record: length exceeding maxOverProduceLength.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
/// <param name="Product">产品 / Product.</param>
/// <param name="OverLength">超长量 / Over-length quantity.</param>
public sealed record OverLengthRecord<V>(
    Product<V> Product,
    Quantity<V> OverLength
) where V : struct;

/// <summary>
/// 长度分配目标 / Length assignment objective.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public abstract record LengthAssignmentObjective<V> where V : struct {
    private LengthAssignmentObjective() { }

    /// <summary>
    /// 最小化总卷长 / Minimize total assigned length.
    /// </summary>
    /// <param name="Weight">目标权重 / Objective weight.</param>
    public sealed record MinimizeTotalLength<V>(V Weight) : LengthAssignmentObjective<V> where V : struct;

    /// <summary>
    /// 最小化批次数 / Minimize batch count.
    /// </summary>
    /// <param name="Weight">目标权重 / Objective weight.</param>
    public sealed record MinimizeBatchCount<V>(V Weight) : LengthAssignmentObjective<V> where V : struct;

    /// <summary>
    /// 最小化超长 / Minimize over-length.
    /// </summary>
    /// <param name="Weight">目标权重 / Objective weight.</param>
    public sealed record MinimizeOverLength<V>(V Weight) : LengthAssignmentObjective<V> where V : struct;
}
