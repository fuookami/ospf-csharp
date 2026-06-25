#nullable enable

using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
/// <summary>
/// 切割方案对需求的统一贡献值 / Unified demand contribution produced by a cutting plan
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
/// <param name="Product">产品 / Product.</param>
/// <param name="Quantity">贡献值 / Contribution quantity.</param>
public sealed record CuttingPlanDemandContribution<V>(
    Product<V> Product,
    Quantity<V> Quantity
) : ICuttingPlanDemandContributionForCanonicalKey where V : struct {
    /// <inheritdoc/>
    string ICuttingPlanDemandContributionForCanonicalKey.ProductId => Product.Id;

    /// <inheritdoc/>
    string ICuttingPlanDemandContributionForCanonicalKey.UnitKey => Quantity.Unit.Symbol ?? Quantity.Unit.Name ?? "";

    /// <inheritdoc/>
    string ICuttingPlanDemandContributionForCanonicalKey.QuantityValue => Quantity.Value.ToString() ?? "";

    /// <summary>
    /// 按产品需求口径创建贡献 / Build contribution by product demand unit
    /// </summary>
    public static Result<CuttingPlanDemandContribution<V>, ErrorCode, Error<ErrorCode>> FromDemand(
        ProductDemand<V> demand,
        Quantity<V> width,
        UInt64 amount,
        IQuantityArithmetic<V> arithmetic,
        Quantity<V>? length = null) => Of(demand.Product, width, amount, demand.Quantity.Unit, arithmetic, length);

    /// <summary>
    /// 按指定需求单位创建贡献 / Build contribution by a demand unit
    /// </summary>
    public static Result<CuttingPlanDemandContribution<V>, ErrorCode, Error<ErrorCode>> Of(
        Product<V> product,
        Quantity<V> width,
        UInt64 amount,
        PhysicalUnit demandUnit,
        IQuantityArithmetic<V> arithmetic,
        Quantity<V>? length = null) {
        return QuantityOf(product, width, amount, demandUnit, arithmetic, length)
            .Map(quantity => new CuttingPlanDemandContribution<V>(product, quantity));
    }

    /// <summary>
    /// 计算切片对需求的贡献值 / Calculate slice contribution quantity
    /// </summary>
    public static Result<Quantity<V>, ErrorCode, Error<ErrorCode>> QuantityOf(
        Product<V> product,
        Quantity<V> width,
        UInt64 amount,
        PhysicalUnit demandUnit,
        IQuantityArithmetic<V> arithmetic,
        Quantity<V>? length = null) {
        Quantity<V>? contributionLength = length ?? product.Length;
        Quantity<V>? unitContribution = null;
        if (product.UnitWeight is { } unitWeight && contributionLength is { } currentLength) {
            if (unitWeight.Unit == demandUnit) {
                V areaValue = MultiplyValues(width.Value, currentLength.Value);
                V weightValue = MultiplyValues(areaValue, unitWeight.Value);
                unitContribution = new Quantity<V>(weightValue, demandUnit);
            }
        }

        if (unitContribution != null) {
            return Repeat(unitContribution, amount, arithmetic);
        }

        V one = GetConstantsOne(width.Value);
        var onePerPiece = new Quantity<V>(one, demandUnit);
        return Repeat(onePerPiece, amount, arithmetic);
    }

    private static V MultiplyValues(V a, V b) {
        if (a is Flt64 f64a && b is Flt64 f64b) {
            return (V)(object)f64a.Times(f64b);
        }

        if (a is FltX fxa && b is FltX fxb) {
            return (V)(object)fxa.Times(fxb);
        }

        throw new NotSupportedException($"Multiplication not supported for type {typeof(V).Name}");
    }

    private static V GetConstantsOne(V sample) {
        if (sample is Flt64) {
            return (V)(object)Flt64.One;
        }

        if (sample is FltX) {
            return (V)(object)FltX.One;
        }

        if (sample is Int64) {
            return (V)(object)Int64.One;
        }

        if (sample is UInt64) {
            return (V)(object)UInt64.One;
        }

        throw new NotSupportedException($"Constants.One not available for type {typeof(V).Name}");
    }

    /// <summary>
    /// 将物理量按离散次数累加 / Repeat quantity by discrete amount
    /// </summary>
    internal static Result<Quantity<V>, ErrorCode, Error<ErrorCode>> Repeat(Quantity<V> quantity, UInt64 amount, IQuantityArithmetic<V> arithmetic) {
        Quantity<V> result = arithmetic.Zero(quantity.Unit);
        UInt64 remaining = amount;
        while (remaining > UInt64.Zero) {
            Result<Quantity<V>, ErrorCode, Error<ErrorCode>> addResult = arithmetic.Add(result, quantity);
            if (addResult.IsFailed) {
                return new Failed<Quantity<V>, ErrorCode, Error<ErrorCode>>(
                    addResult is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> f ? f.Error : new Err<ErrorCode>(ErrorCode.Unknown));
            }

            result = addResult.Value;
            remaining = remaining - UInt64.One;
        }
        return new Ok<Quantity<V>, ErrorCode, Error<ErrorCode>>(result);
    }
}

/// <summary>
/// 按当前需求口径创建切片贡献的扩展方法 / Extension to build slice contribution by demand
/// </summary>
public static class ProductDemandContributionExtensions {
    /// <summary>
    /// 按当前需求口径创建切片贡献 / Build slice contribution by this demand
    /// </summary>
    public static Result<CuttingPlanDemandContribution<V>, ErrorCode, Error<ErrorCode>> Contribution<V>(
        this ProductDemand<V> demand,
        Quantity<V> width,
        UInt64 amount,
        IQuantityArithmetic<V> arithmetic,
        Quantity<V>? length = null) where V : struct => CuttingPlanDemandContribution<V>.FromDemand(demand, width, amount, arithmetic, length);
}
