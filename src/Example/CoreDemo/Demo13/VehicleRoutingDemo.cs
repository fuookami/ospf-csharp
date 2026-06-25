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

namespace Fuookami.Ospf.Example.CoreDemo.Demo13;
/// <summary>
/// 经销商：具有需求量。Dealer with demand quantity.
/// </summary>
public sealed class Dealer {
    public int Index { get; }
    public string Name { get; }
    public int Demand { get; }

    public Dealer(int index, string name, int demand) {
        Index = index;
        Name = name;
        Demand = demand;
    }
}

/// <summary>
/// 配送中心：具有供应量。Distribution center with supply quantity.
/// </summary>
public sealed class DistributionCenter {
    public int Index { get; }
    public string Name { get; }
    public int Supply { get; }
    /// <summary>到各经销商的距离。Distance to each dealer.</summary>
    public IReadOnlyDictionary<int, Flt64> DistanceToDealer { get; }

    public DistributionCenter(int index, string name, int supply,
        IReadOnlyDictionary<int, Flt64> distanceToDealer) {
        Index = index;
        Name = name;
        Supply = supply;
        DistanceToDealer = distanceToDealer;
    }
}

/// <summary>
/// 车辆路径规划演示：配送中心向经销商运输货物，最小化总运输距离。
/// Vehicle routing demo: ship from distribution centers to dealers, minimize total distance.
/// </summary>
public sealed class VehicleRoutingDemo {
    private static readonly List<Dealer> Dealers = new()
    {
        new Dealer(0, "D1", 100),
        new Dealer(1, "D2", 200),
        new Dealer(2, "D3", 150),
        new Dealer(3, "D4", 160),
        new Dealer(4, "D5", 140)
    };

    private static readonly List<DistributionCenter> Centers = new()
    {
        new DistributionCenter(0, "C1", 400, new Dictionary<int, Flt64>
        {
            { 0, new Flt64(10) }, { 1, new Flt64(15) }, { 2, new Flt64(20) },
            { 3, new Flt64(25) }, { 4, new Flt64(30) }
        }),
        new DistributionCenter(1, "C2", 200, new Dictionary<int, Flt64>
        {
            { 0, new Flt64(20) }, { 1, new Flt64(10) }, { 2, new Flt64(15) },
            { 3, new Flt64(30) }, { 4, new Flt64(25) }
        }),
        new DistributionCenter(2, "C3", 150, new Dictionary<int, Flt64>
        {
            { 0, new Flt64(30) }, { 1, new Flt64(25) }, { 2, new Flt64(10) },
            { 3, new Flt64(15) }, { 4, new Flt64(20) }
        })
    };

    private static readonly Flt64 CarCapacity = new(18);

    /// <summary>从中心 c 向经销商 d 的发货量。Shipment quantity from center c to dealer d.</summary>
    private readonly List<List<UIntVar>> _x = new();
    /// <summary>从中心 c 向经销商 d 派遣的车辆数。Truck count from center c to dealer d.</summary>
    private readonly List<List<UIntVar>> _y = new();
    private LinearExpressionSymbol? _totalDistance;
    private readonly LinearMetaModel<Flt64> _metaModel = new("coredemo13-vehicle-routing", ObjectCategory.Minimum);

    public LinearMetaModel<Flt64> MetaModel => _metaModel;

    /// <summary>
    /// 构建车辆路径优化模型（不求解，用于测试验证）。
    /// Build the vehicle routing optimization model (without solving, for test verification).
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
        foreach (DistributionCenter center in Centers) {
            var xRow = new List<UIntVar>();
            var yRow = new List<UIntVar>();
            foreach (Dealer dealer in Dealers) {
                var x = new UIntVar($"x_{center.Index}_{dealer.Index}");
                xRow.Add(x);
                Result<Success, ErrorCode, Error<ErrorCode>> r1 = _metaModel.Add(x);
                if (r1.IsFailed) {
                    return r1;
                }

                var y = new UIntVar($"y_{center.Index}_{dealer.Index}");
                yRow.Add(y);
                Result<Success, ErrorCode, Error<ErrorCode>> r2 = _metaModel.Add(y);
                if (r2.IsFailed) {
                    return r2;
                }
            }
            _x.Add(xRow);
            _y.Add(yRow);
        }
        return Results.Ok(Results.SuccessInstance);
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> InitSymbols() {
        // totalDistance = sum(distance[c,d] * x[c,d])
        var distPoly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
        foreach (DistributionCenter center in Centers) {
            foreach (Dealer dealer in Dealers) {
                distPoly.AddMonomial(new LinearMonomial<Flt64>(
                    center.DistanceToDealer[dealer.Index],
                    _x[center.Index][dealer.Index]));
            }
        }
        _totalDistance = new LinearExpressionSymbol(distPoly, name: "totalDistance");
        _metaModel.Add(_totalDistance);

        return Results.Ok(Results.SuccessInstance);
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> InitObjective() {
        // minimize total distance
        return _metaModel.AddObject(
            ObjectCategory.Minimum,
            _totalDistance!.Polynomial,
            "totalDistance",
            "Total Transportation Distance");
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> InitConstraints() {
        // Supply limit: sum(x[c, *]) <= supply[c]
        foreach (DistributionCenter center in Centers) {
            var poly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
            foreach (Dealer dealer in Dealers) {
                poly.AddMonomial(new LinearMonomial<Flt64>(
                    Flt64.One, _x[center.Index][dealer.Index]));
            }
            LinearInequality<Flt64> constraint = poly.ToLinearPolynomial().Le(new Flt64(center.Supply));
            Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.AddConstraint(constraint, group: null,
                name: $"supply_{center.Index}");
            if (result.IsFailed) {
                return result;
            }
        }

        // Demand satisfaction: sum(x[*, d]) >= demand[d]
        foreach (Dealer dealer in Dealers) {
            var poly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
            foreach (DistributionCenter center in Centers) {
                poly.AddMonomial(new LinearMonomial<Flt64>(
                    Flt64.One, _x[center.Index][dealer.Index]));
            }
            LinearInequality<Flt64> constraint = poly.ToLinearPolynomial().Ge(new Flt64(dealer.Demand));
            Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.AddConstraint(constraint, group: null,
                name: $"demand_{dealer.Index}");
            if (result.IsFailed) {
                return result;
            }
        }

        // Shipment-capacity linking: x[c,d] <= capacity * y[c,d]
        // Rewrite as: x[c,d] - capacity * y[c,d] <= 0
        foreach (DistributionCenter center in Centers) {
            foreach (Dealer dealer in Dealers) {
                var poly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
                poly.AddMonomial(new LinearMonomial<Flt64>(
                    Flt64.One, _x[center.Index][dealer.Index]));
                poly.AddMonomial(new LinearMonomial<Flt64>(
                    CarCapacity.Negate(), _y[center.Index][dealer.Index]));
                LinearInequality<Flt64> constraint = poly.ToLinearPolynomial().Le(Flt64.Zero);
                Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.AddConstraint(constraint, group: null,
                    name: $"capLink_{center.Index}_{dealer.Index}");
                if (result.IsFailed) {
                    return result;
                }
            }
        }

        return Results.Ok(Results.SuccessInstance);
    }
}
