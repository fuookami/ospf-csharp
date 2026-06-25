#nullable enable

using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.WastingMinimization.Model;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.WastingMinimization;
/// <summary>
/// 浪费分析结果 / Waste analysis result.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
/// <param name="RestWidthWastes">余宽浪费列表 / Rest width waste list.</param>
/// <param name="RestMaterialWastes">余料浪费列表 / Rest material waste list.</param>
/// <param name="TotalRestWidth">总余宽 / Total rest width.</param>
/// <param name="TotalRestMaterial">总余料面积代理 / Total rest material area proxy.</param>
public sealed record WasteAnalysis<V>(
    IReadOnlyList<RestWidthWaste<V>> RestWidthWastes,
    IReadOnlyList<RestMaterialWaste<V>> RestMaterialWastes,
    Quantity<V>? TotalRestWidth,
    Quantity<V>? TotalRestMaterial
) where V : struct;

/// <summary>
/// 浪费最小化上下文，负责分析和量化切割方案集合中的各种浪费 / Waste minimization context: analyze and quantify various wastes across cutting plans.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public sealed class WastingMinimizationContext<V> where V : struct {
    private readonly IQuantityArithmetic<V> _arithmetic;

    /// <summary>
    /// 构造浪费最小化上下文 / Construct waste minimization context.
    /// </summary>
    /// <param name="arithmetic">物理量算术策略 / Quantity arithmetic strategy.</param>
    public WastingMinimizationContext(IQuantityArithmetic<V> arithmetic) {
        _arithmetic = arithmetic;
    }

    /// <summary>
    /// 分析切割方案集合的浪费 / Analyze waste across selected cutting plans.
    /// </summary>
    /// <param name="selectedPlans">选中的切割方案及其使用车次 / Selected cutting plans with usage amounts.</param>
    /// <returns>浪费分析结果 / Waste analysis result.</returns>
    public Result<WasteAnalysis<V>, ErrorCode, Error<ErrorCode>> Analyze(
        IReadOnlyList<CuttingPlanUsage<V>> selectedPlans) {
        var restWidthWastes = new List<RestWidthWaste<V>>();
        var restMaterialWastes = new List<RestMaterialWaste<V>>();
        Quantity<V>? totalRestWidth = null;
        Quantity<V>? totalRestMaterial = null;

        foreach (CuttingPlanUsage<V> usage in selectedPlans) {
            CuttingPlan<V> plan = usage.Plan;
            if (plan.RestWidth is null) {
                continue;
            }

            Quantity<V> restWidth = plan.RestWidth;

            // 计入批次数倍数 / Account for batch multiplier
            Result<Quantity<V>, ErrorCode, Error<ErrorCode>> batchResult = RepeatQuantity(restWidth, usage.Amount);
            if (batchResult is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> batchFailed) {
                return new Failed<WasteAnalysis<V>, ErrorCode, Error<ErrorCode>>(batchFailed.Error);
            }
            if (batchResult is Fatal<Quantity<V>, ErrorCode, Error<ErrorCode>> fat) {
                return new Fatal<WasteAnalysis<V>, ErrorCode, Error<ErrorCode>>(fat.Errors);
            }
            Quantity<V> batchRestWidth = batchResult.Value;

            restWidthWastes.Add(new RestWidthWaste<V>(plan, batchRestWidth));

            if (totalRestWidth is null) {
                totalRestWidth = batchRestWidth;
            }
            else {
                Result<Quantity<V>, ErrorCode, Error<ErrorCode>> addResult = _arithmetic.Add(totalRestWidth, batchRestWidth);
                if (addResult is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> addFailed) {
                    return new Failed<WasteAnalysis<V>, ErrorCode, Error<ErrorCode>>(addFailed.Error);
                }
                if (addResult is Fatal<Quantity<V>, ErrorCode, Error<ErrorCode>> fatAdd) {
                    return new Fatal<WasteAnalysis<V>, ErrorCode, Error<ErrorCode>>(fatAdd.Errors);
                }
                totalRestWidth = addResult.Value;
            }

            // 余料面积代理 = 余宽 * 物料长度 / Rest material area proxy = rest width * material length
            Quantity<V>? materialLength = plan.Material.Length;
            if (materialLength is not null) {
                Quantity<V> multiplied = MultiplyQuantities(restWidth, materialLength);
                Result<Quantity<V>, ErrorCode, Error<ErrorCode>> batchRestMaterialResult = RepeatQuantity(multiplied, usage.Amount);
                if (batchRestMaterialResult is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> batchMatFailed) {
                    return new Failed<WasteAnalysis<V>, ErrorCode, Error<ErrorCode>>(batchMatFailed.Error);
                }
                if (batchRestMaterialResult is Fatal<Quantity<V>, ErrorCode, Error<ErrorCode>> fatBatch) {
                    return new Fatal<WasteAnalysis<V>, ErrorCode, Error<ErrorCode>>(fatBatch.Errors);
                }
                Quantity<V> batchRestMaterial = batchRestMaterialResult.Value;

                restMaterialWastes.Add(new RestMaterialWaste<V>(plan, batchRestMaterial));

                if (totalRestMaterial is null) {
                    totalRestMaterial = batchRestMaterial;
                }
                else {
                    Result<Quantity<V>, ErrorCode, Error<ErrorCode>> addResult = _arithmetic.Add(totalRestMaterial, batchRestMaterial);
                    if (addResult is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> addMatFailed) {
                        return new Failed<WasteAnalysis<V>, ErrorCode, Error<ErrorCode>>(addMatFailed.Error);
                    }
                    if (addResult is Fatal<Quantity<V>, ErrorCode, Error<ErrorCode>> fatAdd) {
                        return new Fatal<WasteAnalysis<V>, ErrorCode, Error<ErrorCode>>(fatAdd.Errors);
                    }
                    totalRestMaterial = addResult.Value;
                }
            }
        }

        return new Ok<WasteAnalysis<V>, ErrorCode, Error<ErrorCode>>(new WasteAnalysis<V>(
            restWidthWastes,
            restMaterialWastes,
            totalRestWidth,
            totalRestMaterial
        ));
    }

    /// <summary>
    /// 重复数量计算，用于浪费最小化 / Repeat quantity calculation for waste minimization.
    /// </summary>
    /// <param name="q">待重复的数量 / Quantity to repeat.</param>
    /// <param name="times">重复次数 / Number of repetitions.</param>
    /// <returns>重复后的总量 / Total quantity after repetition.</returns>
    private Result<Quantity<V>, ErrorCode, Error<ErrorCode>> RepeatQuantity(Quantity<V> q, UInt64 times) {
        Quantity<V> total = _arithmetic.Zero(q.Unit);
        UInt64 count = UInt64.Zero;
        while (count < times) {
            Result<Quantity<V>, ErrorCode, Error<ErrorCode>> addResult = _arithmetic.Add(total, q);
            if (addResult is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> addFailed) {
                return new Failed<Quantity<V>, ErrorCode, Error<ErrorCode>>(addFailed.Error);
            }
            if (addResult is Fatal<Quantity<V>, ErrorCode, Error<ErrorCode>> fat) {
                return new Fatal<Quantity<V>, ErrorCode, Error<ErrorCode>>(fat.Errors);
            }
            total = addResult.Value;
            count += UInt64.One;
        }
        return new Ok<Quantity<V>, ErrorCode, Error<ErrorCode>>(total);
    }

    private Quantity<V> MultiplyQuantities(Quantity<V> a, Quantity<V> b) {
        if (a.Value is Flt64 fa && b.Value is Flt64 fb) {
            return new Quantity<V>((V)(object)(fa * fb), a.Unit.Multiply(b.Unit));
        }
        if (a.Value is FltX fxa && b.Value is FltX fxb) {
            return new Quantity<V>((V)(object)(fxa.Times(fxb)), a.Unit.Multiply(b.Unit));
        }
        throw new InvalidOperationException($"Unsupported value type for quantity multiplication: {a.Value.GetType()}");
    }
}
