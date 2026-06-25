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

namespace Fuookami.Ospf.Example.CoreDemo.Demo8;
/// <summary>
/// 生产计划演示：在设备工时约束下最大化利润。
/// Production planning demo: maximize profit subject to equipment man-hour constraints.
/// </summary>
public sealed class ProductionSchedulingDemo {
    private static readonly int NumProducts = 5;
    private static readonly int NumEquipments = 4;

    /// <summary>各产品利润 / Profit per product.</summary>
    private static readonly List<Flt64> Profit = new()
    {
        new Flt64(123), new Flt64(94), new Flt64(105), new Flt64(132), new Flt64(118)
    };

    /// <summary>各设备数量 / Amount per equipment type.</summary>
    private static readonly List<Flt64> EquipmentAmount = new()
    {
        new Flt64(12), new Flt64(14), new Flt64(8), new Flt64(6)
    };

    /// <summary>最大工时 / Maximum man-hours per equipment.</summary>
    private static readonly Flt64 MaxManHours = new(2000);

    /// <summary>
    /// 各设备对各产品的工时需求 / Man-hours required per product per equipment type.
    /// ManHours[equipment][product].
    /// </summary>
    private static readonly List<List<Flt64>> ManHours = new()
    {
        new() { new Flt64(3), new Flt64(2), new Flt64(4), new Flt64(5), new Flt64(1) },
        new() { new Flt64(4), new Flt64(1), new Flt64(3), new Flt64(2), new Flt64(5) },
        new() { new Flt64(2), new Flt64(5), new Flt64(1), new Flt64(4), new Flt64(3) },
        new() { new Flt64(5), new Flt64(3), new Flt64(2), new Flt64(1), new Flt64(4) }
    };

    private readonly List<UIntVar> _x = new();
    private readonly List<LinearExpressionSymbol> _equipmentUsage = new();
    private LinearExpressionSymbol? _totalProfit;
    private readonly LinearMetaModel<Flt64> _metaModel = new("demo8-production-scheduling", ObjectCategory.Maximum);

    public LinearMetaModel<Flt64> MetaModel => _metaModel;

    /// <summary>
    /// 构建生产计划模型（不求解，用于测试验证）。
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
        for (int p = 0; p < NumProducts; p++) {
            var x = new UIntVar($"x_{p}");
            _x.Add(x);
            Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.Add(x);
            if (result.IsFailed) {
                return result;
            }
        }
        return Results.Ok(Results.SuccessInstance);
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> InitSymbols() {
        // totalProfit = sum(profit[p] * x[p])
        var profitPoly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
        for (int p = 0; p < NumProducts; p++) {
            profitPoly.AddMonomial(new LinearMonomial<Flt64>(Profit[p], _x[p]));
        }
        _totalProfit = new LinearExpressionSymbol(profitPoly, name: "totalProfit");
        _metaModel.Add(_totalProfit);

        // equipmentUsage[e] = sum(manHours[e][p] * x[p])
        for (int e = 0; e < NumEquipments; e++) {
            var poly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
            for (int p = 0; p < NumProducts; p++) {
                poly.AddMonomial(new LinearMonomial<Flt64>(ManHours[e][p], _x[p]));
            }
            var sym = new LinearExpressionSymbol(poly, name: $"equipmentUsage_{e}");
            _equipmentUsage.Add(sym);
            _metaModel.Add(sym);
        }

        return Results.Ok(Results.SuccessInstance);
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> InitObjective() {
        // maximize total profit
        return _metaModel.AddObject(
            ObjectCategory.Maximum,
            _totalProfit!.Polynomial,
            "totalProfit",
            "Total Profit");
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> InitConstraints() {
        // equipmentUsage[e] <= equipmentAmount[e] * maxManHours
        for (int e = 0; e < NumEquipments; e++) {
            Flt64 maxCapacity = EquipmentAmount[e].Times(MaxManHours);
            LinearInequality<Flt64> constraint = _equipmentUsage[e].Polynomial.Le(maxCapacity);
            Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.AddConstraint(constraint, group: null, name: $"equipment_{e}_capacity");
            if (result.IsFailed) {
                return result;
            }
        }

        return Results.Ok(Results.SuccessInstance);
    }
}
