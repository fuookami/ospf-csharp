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

namespace Fuookami.Ospf.Example.CoreDemo.Demo15;
/// <summary>
/// 车型：具有名称。Car model with name.
/// </summary>
public sealed class CarModel {
    public int Index { get; }
    public string Name { get; }

    public CarModel(int index, string name) {
        Index = index;
        Name = name;
    }
}

/// <summary>
/// 配送中心：具有各车型需求量。Distribution center with demand per car model.
/// </summary>
public sealed class DistributionCenter {
    public int Index { get; }
    public string Name { get; }
    public IReadOnlyDictionary<int, int> Demand { get; }

    public DistributionCenter(int index, string name, IReadOnlyDictionary<int, int> demand) {
        Index = index;
        Name = name;
        Demand = demand;
    }
}

/// <summary>
/// 制造商：具有产能和到各中心的物流成本。Manufacturer with capacity and logistics cost to each center.
/// </summary>
public sealed class Manufacturer {
    public int Index { get; }
    public string Name { get; }
    public int Capacity { get; }
    /// <summary>到各配送中心的物流成本。Logistics cost to each center (center_index -> cost).</summary>
    public IReadOnlyDictionary<int, Flt64> LogisticsCost { get; }

    public Manufacturer(int index, string name, int capacity,
        IReadOnlyDictionary<int, Flt64> logisticsCost) {
        Index = index;
        Name = name;
        Capacity = capacity;
        LogisticsCost = logisticsCost;
    }
}

/// <summary>
/// 多工厂配送演示（简化版）：最小化物流成本。
/// Multi-plant distribution demo (simplified): minimize logistics cost.
///
/// 原始模型包含替代率（PctVar），此处简化为基础运输模型。
/// The original model includes substitution rates (PctVar); here we simplify to a basic shipment model.
/// </summary>
public sealed class MultiPlantDistributionDemo {
    private static readonly List<CarModel> Models = new()
    {
        new CarModel(0, "M1"),
        new CarModel(1, "M2"),
        new CarModel(2, "M3"),
        new CarModel(3, "M4")
    };

    private static readonly List<DistributionCenter> Centers = new()
    {
        new DistributionCenter(0, "Center-A", new Dictionary<int, int>
        {
            { 0, 50 }, { 1, 30 }, { 2, 40 }, { 3, 20 }
        }),
        new DistributionCenter(1, "Center-B", new Dictionary<int, int>
        {
            { 0, 40 }, { 1, 35 }, { 2, 45 }, { 3, 25 }
        })
    };

    private static readonly List<Manufacturer> Manufacturers = new()
    {
        new Manufacturer(0, "Plant-1", 200, new Dictionary<int, Flt64>
        {
            { 0, new Flt64(3) }, { 1, new Flt64(5) }
        }),
        new Manufacturer(1, "Plant-2", 150, new Dictionary<int, Flt64>
        {
            { 0, new Flt64(4) }, { 1, new Flt64(2) }
        }),
        new Manufacturer(2, "Plant-3", 180, new Dictionary<int, Flt64>
        {
            { 0, new Flt64(6) }, { 1, new Flt64(3) }
        })
    };

    /// <summary>从制造商 m 向中心 c 运输车型 k 的数量。Shipment from manufacturer m to center c for model k.</summary>
    private readonly List<List<List<UIntVar>>> _x = new();
    private LinearExpressionSymbol? _totalCost;
    private readonly LinearMetaModel<Flt64> _metaModel = new("coredemo15-multi-plant-distribution", ObjectCategory.Minimum);

    public LinearMetaModel<Flt64> MetaModel => _metaModel;

    /// <summary>
    /// 构建多工厂配送模型（不求解，用于测试验证）。
    /// Build the multi-plant distribution model (without solving, for test verification).
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
        foreach (Manufacturer mfg in Manufacturers) {
            var centerSlices = new List<List<UIntVar>>();
            foreach (DistributionCenter center in Centers) {
                var modelVars = new List<UIntVar>();
                foreach (CarModel model in Models) {
                    var x = new UIntVar($"x_{mfg.Index}_{center.Index}_{model.Index}");
                    modelVars.Add(x);
                    Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.Add(x);
                    if (result.IsFailed) {
                        return result;
                    }
                }
                centerSlices.Add(modelVars);
            }
            _x.Add(centerSlices);
        }
        return Results.Ok(Results.SuccessInstance);
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> InitSymbols() {
        // totalCost = sum(logisticsCost[m,c] * x[m,c,k])
        var costPoly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
        foreach (Manufacturer mfg in Manufacturers) {
            foreach (DistributionCenter center in Centers) {
                Flt64 cost = mfg.LogisticsCost[center.Index];
                foreach (CarModel model in Models) {
                    costPoly.AddMonomial(new LinearMonomial<Flt64>(
                        cost, _x[mfg.Index][center.Index][model.Index]));
                }
            }
        }
        _totalCost = new LinearExpressionSymbol(costPoly, name: "totalCost");
        _metaModel.Add(_totalCost);

        return Results.Ok(Results.SuccessInstance);
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> InitObjective() {
        // minimize total logistics cost
        return _metaModel.AddObject(
            ObjectCategory.Minimum,
            _totalCost!.Polynomial,
            "totalCost",
            "Total Logistics Cost");
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> InitConstraints() {
        // Demand satisfaction: sum(x[*, c, k]) >= demand[c][k]
        foreach (DistributionCenter center in Centers) {
            foreach (CarModel model in Models) {
                var poly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
                foreach (Manufacturer mfg in Manufacturers) {
                    poly.AddMonomial(new LinearMonomial<Flt64>(
                        Flt64.One, _x[mfg.Index][center.Index][model.Index]));
                }
                LinearInequality<Flt64> constraint = poly.ToLinearPolynomial().Ge(
                    new Flt64(center.Demand[model.Index]));
                Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.AddConstraint(constraint, group: null,
                    name: $"demand_{center.Index}_{model.Index}");
                if (result.IsFailed) {
                    return result;
                }
            }
        }

        // Production capacity: sum(x[m, *, *]) <= capacity[m]
        foreach (Manufacturer mfg in Manufacturers) {
            var poly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
            foreach (DistributionCenter center in Centers) {
                foreach (CarModel model in Models) {
                    poly.AddMonomial(new LinearMonomial<Flt64>(
                        Flt64.One, _x[mfg.Index][center.Index][model.Index]));
                }
            }
            LinearInequality<Flt64> constraint = poly.ToLinearPolynomial().Le(new Flt64(mfg.Capacity));
            Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.AddConstraint(constraint, group: null,
                name: $"capacity_{mfg.Index}");
            if (result.IsFailed) {
                return result;
            }
        }

        return Results.Ok(Results.SuccessInstance);
    }
}
