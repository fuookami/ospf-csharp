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

namespace Fuookami.Ospf.Example.CoreDemo.Demo7;
/// <summary>
/// 运输问题演示：在仓库容量和门店需求约束下最小化运输成本。
/// Transportation problem demo: minimize shipping cost subject to warehouse stowage and store demand constraints.
/// </summary>
public sealed class TransportationDemo {
    private static readonly int NumStores = 4;
    private static readonly int NumWarehouses = 3;

    /// <summary>各门店需求量 / Demand per store.</summary>
    private static readonly List<Flt64> Demand = new()
    {
        new Flt64(200), new Flt64(400), new Flt64(600), new Flt64(300)
    };

    /// <summary>各仓库装载容量 / Stowage capacity per warehouse.</summary>
    private static readonly List<Flt64> Stowage = new()
    {
        new Flt64(510), new Flt64(470), new Flt64(520)
    };

    /// <summary>
    /// 各仓库到各门店的单位运输成本 / Shipping cost per unit from warehouse to store.
    /// Cost[warehouse][store].
    /// </summary>
    private static readonly List<List<Flt64>> Cost = new()
    {
        new() { new Flt64(2), new Flt64(3), new Flt64(4), new Flt64(5) },
        new() { new Flt64(3), new Flt64(2), new Flt64(5), new Flt64(3) },
        new() { new Flt64(4), new Flt64(1), new Flt64(3), new Flt64(2) }
    };

    private readonly List<List<UIntVar>> _x = new();
    private readonly List<LinearExpressionSymbol> _shipment = new();
    private readonly List<LinearExpressionSymbol> _purchase = new();
    private readonly LinearMetaModel<Flt64> _metaModel = new("demo7-transportation", ObjectCategory.Minimum);

    public LinearMetaModel<Flt64> MetaModel => _metaModel;

    /// <summary>
    /// 构建运输优化模型（不求解，用于测试验证）。
    /// Build the transportation optimization model (without solving, for test verification).
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
        for (int w = 0; w < NumWarehouses; w++) {
            var warehouseVars = new List<UIntVar>();
            for (int s = 0; s < NumStores; s++) {
                var x = new UIntVar($"x_{w}_{s}");
                warehouseVars.Add(x);
                Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.Add(x);
                if (result.IsFailed) {
                    return result;
                }
            }
            _x.Add(warehouseVars);
        }
        return Results.Ok(Results.SuccessInstance);
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> InitSymbols() {
        // shipment[w] = sum over stores: cost[w][s] * x[w][s]
        for (int w = 0; w < NumWarehouses; w++) {
            var poly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
            for (int s = 0; s < NumStores; s++) {
                poly.AddMonomial(new LinearMonomial<Flt64>(Cost[w][s], _x[w][s]));
            }
            var sym = new LinearExpressionSymbol(poly, name: $"shipment_{w}");
            _shipment.Add(sym);
            _metaModel.Add(sym);
        }

        // purchase[s] = sum over warehouses: x[w][s]
        for (int s = 0; s < NumStores; s++) {
            var poly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
            for (int w = 0; w < NumWarehouses; w++) {
                poly.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, _x[w][s]));
            }
            var sym = new LinearExpressionSymbol(poly, name: $"purchase_{s}");
            _purchase.Add(sym);
            _metaModel.Add(sym);
        }

        return Results.Ok(Results.SuccessInstance);
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> InitObjective() {
        // minimize total shipping cost = sum over w,s: cost[w][s] * x[w][s]
        var totalCostPoly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
        for (int w = 0; w < NumWarehouses; w++) {
            for (int s = 0; s < NumStores; s++) {
                totalCostPoly.AddMonomial(new LinearMonomial<Flt64>(Cost[w][s], _x[w][s]));
            }
        }
        return _metaModel.AddObject(
            ObjectCategory.Minimum,
            totalCostPoly.ToLinearPolynomial(),
            "totalCost",
            "Total Shipping Cost");
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> InitConstraints() {
        // shipment[w] <= stowage[w]
        for (int w = 0; w < NumWarehouses; w++) {
            LinearInequality<Flt64> constraint = _shipment[w].Polynomial.Le(Stowage[w]);
            Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.AddConstraint(constraint, group: null, name: $"stowage_{w}");
            if (result.IsFailed) {
                return result;
            }
        }

        // purchase[s] >= demand[s]
        for (int s = 0; s < NumStores; s++) {
            LinearInequality<Flt64> constraint = _purchase[s].Polynomial.Ge(Demand[s]);
            Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.AddConstraint(constraint, group: null, name: $"demand_{s}");
            if (result.IsFailed) {
                return result;
            }
        }

        return Results.Ok(Results.SuccessInstance);
    }
}
