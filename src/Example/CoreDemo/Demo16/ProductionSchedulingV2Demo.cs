#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Example.CoreDemo.Demo16;
/// <summary>
/// 生产周期：具有月份、生产力和需求量。Production period with month, productivity, and demand.
/// </summary>
public sealed class Period {
    public int Index { get; }
    public string Month { get; }
    public int Productivity { get; }
    public int Demand { get; }

    public Period(int index, string month, int productivity, int demand) {
        Index = index;
        Month = month;
        Productivity = productivity;
        Demand = demand;
    }
}

/// <summary>
/// 生产调度 V2 演示：多周期生产调度，最小化生产、存储和延迟交付成本。
/// Production scheduling V2 demo: multi-period production scheduling,
/// minimize production, storage, and delay delivery cost.
/// </summary>
public sealed class ProductionSchedulingV2Demo {
    private static readonly List<Period> Periods = new()
    {
        new Period(0, "Jan", 300, 200),
        new Period(1, "Feb", 400, 350),
        new Period(2, "Mar", 350, 300),
        new Period(3, "Apr", 250, 400)
    };

    private static readonly Flt64 ProductionPrice = new(40);
    private static readonly Flt64 DelayDeliveryPrice = new(2);
    private static readonly Flt64 StoragePrice = new(0.5);

    /// <summary>
    /// x[i,j] = 在周期 i 生产、用于满足周期 j 需求的产量。
    /// x[i,j] = quantity produced in period i to satisfy demand of period j.
    /// </summary>
    private readonly List<List<UIntVar>> _x = new();
    private LinearExpressionSymbol? _totalCost;
    private readonly LinearMetaModel<Flt64> _metaModel = new("coredemo16-production-scheduling-v2", ObjectCategory.Minimum);

    public LinearMetaModel<Flt64> MetaModel => _metaModel;

    /// <summary>
    /// 构建生产调度模型（不求解，用于测试验证）。
    /// Build the production scheduling model (without solving, for test verification).
    /// </summary>
    public Result<Success, ErrorCode, Error<ErrorCode>> BuildModel() {
        Result<Success, ErrorCode, Error<ErrorCode>> r1 = InitVariables();
        if (r1.IsFailed) {
            return r1;
        }

        Result<Success, ErrorCode, Error<ErrorCode>> r2 = InitSymbols();
        if (r2.IsFailed) {
            return r2;
        }

        Result<Success, ErrorCode, Error<ErrorCode>> r3 = InitObjective();
        if (r3.IsFailed) {
            return r3;
        }

        Result<Success, ErrorCode, Error<ErrorCode>> r4 = InitConstraints();
        if (r4.IsFailed) {
            return r4;
        }

        return Results.Ok(Results.SuccessInstance);
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> InitVariables() {
        foreach (Period producer in Periods) {
            var row = new List<UIntVar>();
            foreach (Period consumer in Periods) {
                var x = new UIntVar($"x_{producer.Index}_{consumer.Index}");
                row.Add(x);
                Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.Add(x);
                if (result.IsFailed) {
                    return result;
                }
            }
            _x.Add(row);
        }
        return Results.Ok(Results.SuccessInstance);
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> InitSymbols() {
        // totalCost = production cost + storage cost + delay delivery cost
        // Production cost: productionPrice * sum(x[i,j])
        // Storage cost (i < j): storagePrice * (j - i) * x[i,j]
        // Delay delivery cost (i > j): delayDeliveryPrice * (i - j) * x[i,j]
        var costPoly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
        foreach (Period producer in Periods) {
            foreach (Period consumer in Periods) {
                Flt64 coeff = Flt64.Zero;
                // Production cost always applies
                coeff = coeff.Plus(ProductionPrice);
                if (producer.Index < consumer.Index) {
                    // Storage: cost per period of storage
                    var gap = new Flt64(consumer.Index - producer.Index);
                    coeff = coeff.Plus(StoragePrice.Times(gap));
                }
                else if (producer.Index > consumer.Index) {
                    // Delay delivery: cost per period of delay
                    var gap = new Flt64(producer.Index - consumer.Index);
                    coeff = coeff.Plus(DelayDeliveryPrice.Times(gap));
                }
                costPoly.AddMonomial(new LinearMonomial<Flt64>(
                    coeff, _x[producer.Index][consumer.Index]));
            }
        }
        _totalCost = new LinearExpressionSymbol(costPoly, name: "totalCost");
        _metaModel.Add(_totalCost);

        return Results.Ok(Results.SuccessInstance);
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> InitObjective() {
        // minimize total cost
        return _metaModel.AddObject(
            ObjectCategory.Minimum,
            _totalCost!.Polynomial,
            "totalCost",
            "Total Production + Storage + Delay Cost");
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> InitConstraints() {
        // Demand satisfaction: sum(x[*, j]) >= demand[j]
        foreach (Period consumer in Periods) {
            var poly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
            foreach (Period producer in Periods) {
                poly.AddMonomial(new LinearMonomial<Flt64>(
                    Flt64.One, _x[producer.Index][consumer.Index]));
            }
            LinearInequality<Flt64> constraint = poly.ToLinearPolynomial().Ge(new Flt64(consumer.Demand));
            Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.AddConstraint(constraint, group: null,
                name: $"demand_{consumer.Index}");
            if (result.IsFailed) {
                return result;
            }
        }

        // Production capacity: sum(x[i, *]) <= productivity[i]
        foreach (Period producer in Periods) {
            var poly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
            foreach (Period consumer in Periods) {
                poly.AddMonomial(new LinearMonomial<Flt64>(
                    Flt64.One, _x[producer.Index][consumer.Index]));
            }
            LinearInequality<Flt64> constraint = poly.ToLinearPolynomial().Le(new Flt64(producer.Productivity));
            Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.AddConstraint(constraint, group: null,
                name: $"capacity_{producer.Index}");
            if (result.IsFailed) {
                return result;
            }
        }

        return Results.Ok(Results.SuccessInstance);
    }
}
