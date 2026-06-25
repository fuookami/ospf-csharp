#nullable enable

using Fuookami.Ospf.Framework.Csp1d.Domain.LengthAssignment.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System.Collections.Generic;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.LengthAssignment;
/// <summary>
/// 长度推导函数：从需求量和产品属性推导卷长 / Length derivation function: derive coil length from demand quantity and product properties.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
/// <param name="demandQuantity">需求量 / Demand quantity.</param>
/// <param name="product">产品 / Product.</param>
/// <returns>推导的卷长，null 表示无法推导 / Derived coil length, null if derivation is not possible.</returns>
public delegate Quantity<V>? LengthDerivation<V>(Quantity<V> demandQuantity, Product<V> product) where V : struct;

/// <summary>
/// 长度分配约束 / Length assignment constraint.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public abstract record LengthAssignmentConstraint<V> where V : struct {
    private LengthAssignmentConstraint() { }

    /// <summary>
    /// 超长约束：产品卷长不得超过 maxOverProduceLength / Over-length constraint: product length must not exceed maxOverProduceLength.
    /// </summary>
    /// <param name="Product">产品 / Product.</param>
    public sealed record MaxOverLength<V>(Product<V> Product) : LengthAssignmentConstraint<V> where V : struct;

    /// <summary>
    /// 最小批次约束：产品分配批次数不得低于给定值 / Minimum batch constraint: assigned batch count must not be below given value.
    /// </summary>
    /// <param name="Product">产品 / Product.</param>
    /// <param name="MinBatches">最小批次数 / Minimum batch count.</param>
    public sealed record MinBatchCount<V>(Product<V> Product, UInt64 MinBatches) : LengthAssignmentConstraint<V> where V : struct;
}

/// <summary>
/// 长度分配输入 / Length assignment input.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
/// <param name="DynamicProducts">动态长度产品列表 / Dynamic-length products.</param>
/// <param name="Demands">产品需求 / Product demands.</param>
/// <param name="Constraints">长度分配约束 / Length assignment constraints.</param>
public sealed record LengthAssignmentInput<V>(
    IReadOnlyList<Product<V>> DynamicProducts,
    IReadOnlyList<ProductDemand<V>> Demands,
    IReadOnlyList<LengthAssignmentConstraint<V>>? Constraints = null
) where V : struct;

/// <summary>
/// 长度分配结果 / Length assignment result.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
/// <param name="Assignments">分配列表 / Assignment list.</param>
/// <param name="OverLengthRecords">超长记录 / Over-length records.</param>
public sealed record LengthAssignmentResult<V>(
    IReadOnlyList<LengthAssignmentRecord<V>> Assignments,
    IReadOnlyList<OverLengthRecord<V>> OverLengthRecords
) where V : struct;

/// <summary>
/// 长度分配上下文，负责动态卷长分配与超长检测 / Length assignment context: dynamic coil length assignment and over-length detection.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public sealed class LengthAssignmentContext<V> where V : struct {
    private readonly IQuantityArithmetic<V> _arithmetic;
    private readonly LengthDerivation<V> _lengthDerivation;

    /// <summary>
    /// 构造长度分配上下文 / Construct length assignment context.
    /// </summary>
    /// <param name="arithmetic">物理量算术策略 / Quantity arithmetic strategy.</param>
    /// <param name="lengthDerivation">长度推导函数，由下游项目按业务单位注入 / Length derivation function injected by downstream project per business unit.</param>
    public LengthAssignmentContext(IQuantityArithmetic<V> arithmetic, LengthDerivation<V> lengthDerivation) {
        _arithmetic = arithmetic;
        _lengthDerivation = lengthDerivation;
    }

    /// <summary>
    /// 执行长度分配 / Execute length assignment.
    /// </summary>
    /// <param name="input">长度分配输入 / Length assignment input.</param>
    /// <returns>长度分配结果 / Length assignment result.</returns>
    public Result<LengthAssignmentResult<V>, ErrorCode, Error<ErrorCode>> Assign(
        LengthAssignmentInput<V> input) {
        var assignments = new List<LengthAssignmentRecord<V>>();
        var overLengthRecords = new List<OverLengthRecord<V>>();

        foreach (Product<V> product in input.DynamicProducts) {
            ProductDemand<V>? demand = null;
            foreach (ProductDemand<V> d in input.Demands) {
                if (d.Product.Id == product.Id) {
                    demand = d;
                    break;
                }
            }
            if (demand is null) {
                continue;
            }

            Quantity<V>? assignedLength = _lengthDerivation(demand.Quantity, product);
            if (assignedLength is null) {
                continue;
            }

            UInt64 batchCount = UInt64.One;

            assignments.Add(new LengthAssignmentRecord<V>(
                product,
                assignedLength,
                batchCount
            ));

            // 检查超长约束 / Check over-length constraint
            Quantity<V>? maxOverLength = product.MaxOverProduceLength;
            if (maxOverLength is not null && maxOverLength.Unit == assignedLength.Unit) {
                dynamic comparison = ((dynamic)assignedLength.Value).PartialOrd((object)maxOverLength.Value);
                if (comparison is Order.Greater) {
                    Result<Quantity<V>, ErrorCode, Error<ErrorCode>> subResult = _arithmetic.Subtract(assignedLength, maxOverLength);
                    if (subResult.IsFailed) {
                        return new Failed<LengthAssignmentResult<V>, ErrorCode, Error<ErrorCode>>(
                            subResult is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> f ? f.Error : new Err<ErrorCode>(ErrorCode.Unknown));
                    }
                    if (subResult is Fatal<Quantity<V>, ErrorCode, Error<ErrorCode>> fat) {
                        return new Fatal<LengthAssignmentResult<V>, ErrorCode, Error<ErrorCode>>(fat.Errors);
                    }
                    overLengthRecords.Add(new OverLengthRecord<V>(
                        product,
                        subResult.Value
                    ));
                }
            }
        }

        return new Ok<LengthAssignmentResult<V>, ErrorCode, Error<ErrorCode>>(new LengthAssignmentResult<V>(
            assignments,
            overLengthRecords
        ));
    }
}
