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

namespace Fuookami.Ospf.Example.CoreDemo.Demo5;
/// <summary>
/// 货物：具有重量和价值属性。Cargo with weight and value attributes.
/// </summary>
public sealed class Cargo {
    public int Index { get; }
    public Flt64 Weight { get; }
    public Flt64 Value { get; }

    public Cargo(int index, Flt64 weight, Flt64 value) {
        Index = index;
        Weight = weight;
        Value = value;
    }
}

/// <summary>
/// 0-1 背包问题演示：在重量约束下最大化价值。
/// 0-1 Knapsack problem demo: maximize value subject to weight constraint.
/// </summary>
public sealed class KnapsackDemo {
    private static readonly List<Cargo> Cargos = new()
    {
        new Cargo(0, new Flt64(2), new Flt64(6)),
        new Cargo(1, new Flt64(2), new Flt64(3)),
        new Cargo(2, new Flt64(6), new Flt64(5)),
        new Cargo(3, new Flt64(5), new Flt64(4)),
        new Cargo(4, new Flt64(4), new Flt64(6))
    };

    private static readonly Flt64 MaxWeight = new(10);

    private readonly List<BinVar> _x = new();
    private LinearExpressionSymbol? _cargoValue;
    private LinearExpressionSymbol? _cargoWeight;
    private readonly LinearMetaModel<Flt64> _metaModel = new("coredemo5-knapsack", ObjectCategory.Maximum);

    public LinearMetaModel<Flt64> MetaModel => _metaModel;

    /// <summary>
    /// 构建背包模型（不求解，用于测试验证）。
    /// Build the knapsack model (without solving, for test verification).
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
        foreach (Cargo cargo in Cargos) {
            var x = new BinVar($"x_{cargo.Index}");
            _x.Add(x);
            Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.Add(x);
            if (result.IsFailed) {
                return result;
            }
        }
        return Results.Ok(Results.SuccessInstance);
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> InitSymbols() {
        // cargoValue = sum(cargo.value * x[cargo])
        var valuePoly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
        foreach (Cargo cargo in Cargos) {
            valuePoly.AddMonomial(new LinearMonomial<Flt64>(cargo.Value, _x[cargo.Index]));
        }
        _cargoValue = new LinearExpressionSymbol(valuePoly, name: "cargoValue");
        _metaModel.Add(_cargoValue);

        // cargoWeight = sum(cargo.weight * x[cargo])
        var weightPoly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
        foreach (Cargo cargo in Cargos) {
            weightPoly.AddMonomial(new LinearMonomial<Flt64>(cargo.Weight, _x[cargo.Index]));
        }
        _cargoWeight = new LinearExpressionSymbol(weightPoly, name: "cargoWeight");
        _metaModel.Add(_cargoWeight);

        return Results.Ok(Results.SuccessInstance);
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> InitObjective() {
        // maximize cargoValue
        return _metaModel.AddObject(
            ObjectCategory.Maximum,
            _cargoValue!.Polynomial,
            "cargoValue",
            "Total Cargo Value");
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> InitConstraints() {
        // cargoWeight <= maxWeight
        LinearInequality<Flt64> weightConstraint = _cargoWeight!.Polynomial.Le(MaxWeight);
        return _metaModel.AddConstraint(weightConstraint, group: null, name: "maxWeight");
    }
}
