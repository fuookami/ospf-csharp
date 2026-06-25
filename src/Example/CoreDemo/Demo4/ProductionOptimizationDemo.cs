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

namespace Fuookami.Ospf.Example.CoreDemo.Demo4;
/// <summary>
/// 产品：具有利润、最大产量和材料消耗。Product with profit, max yield, and material usage.
/// </summary>
public sealed class OptProduct {
    public int Index { get; }
    public string Name { get; }
    public Flt64 Profit { get; }
    public Flt64 MaxYield { get; }
    public IReadOnlyDictionary<int, Flt64> MaterialUsage { get; }

    public OptProduct(int index, string name, Flt64 profit, Flt64 maxYield, IReadOnlyDictionary<int, Flt64> materialUsage) {
        Index = index;
        Name = name;
        Profit = profit;
        MaxYield = maxYield;
        MaterialUsage = materialUsage;
    }
}

/// <summary>
/// 材料：具有可用量。Material with available quantity.
/// </summary>
public sealed class RawMaterial {
    public int Index { get; }
    public string Name { get; }
    public Flt64 Available { get; }

    public RawMaterial(int index, string name, Flt64 available) {
        Index = index;
        Name = name;
        Available = available;
    }
}

/// <summary>
/// 生产优化：在材料约束下最大化利润，产量差异不超过1。
/// Production optimization: maximize profit subject to material constraints and production difference limit.
/// </summary>
public sealed class ProductionOptimizationDemo {
    private static readonly List<RawMaterial> RawMaterials = new()
    {
        new RawMaterial(0, "Raw_1", new Flt64(24.0)),
        new RawMaterial(1, "Raw_2", new Flt64(8.0))
    };

    private static readonly List<OptProduct> Products = new()
    {
        new OptProduct(0, "Prod_A", new Flt64(3.0), new Flt64(6.0), new Dictionary<int, Flt64>
        {
            { 0, new Flt64(6.0) }, { 1, new Flt64(1.0) }
        }),
        new OptProduct(1, "Prod_B", new Flt64(5.0), new Flt64(4.0), new Dictionary<int, Flt64>
        {
            { 0, new Flt64(4.0) }, { 1, new Flt64(2.0) }
        })
    };

    private readonly List<RealVar> _x = new();
    private LinearExpressionSymbol? _profit;
    private readonly List<LinearExpressionSymbol> _materialUsage = new();
    private LinearExpressionSymbol? _productionDiff;
    private readonly LinearMetaModel<Flt64> _metaModel = new("coredemo4-production-optimization", ObjectCategory.Maximum);

    public LinearMetaModel<Flt64> MetaModel => _metaModel;

    /// <summary>
    /// 构建生产优化模型（不求解，用于测试验证）。
    /// Build the production optimization model (without solving, for test verification).
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
        foreach (OptProduct product in Products) {
            var x = new RealVar($"x_{product.Index}");
            _x.Add(x);
            Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.Add(x);
            if (result.IsFailed) {
                return result;
            }
        }
        return Results.Ok(Results.SuccessInstance);
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> InitSymbols() {
        // profit = sum(product.profit * x[product])
        var profitPoly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
        foreach (OptProduct product in Products) {
            profitPoly.AddMonomial(new LinearMonomial<Flt64>(product.Profit, _x[product.Index]));
        }
        _profit = new LinearExpressionSymbol(profitPoly, name: "profit");
        _metaModel.Add(_profit);

        // materialUsage[material] = sum(product.materialUsage[material] * x[product])
        foreach (RawMaterial material in RawMaterials) {
            var usagePoly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
            foreach (OptProduct product in Products) {
                usagePoly.AddMonomial(new LinearMonomial<Flt64>(
                    product.MaterialUsage[material.Index],
                    _x[product.Index]));
            }
            var usageSymbol = new LinearExpressionSymbol(usagePoly, name: $"materialUsage_{material.Index}");
            _materialUsage.Add(usageSymbol);
            _metaModel.Add(usageSymbol);
        }

        // productionDiff = x[0] - x[1]
        var diffPoly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
        diffPoly.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, _x[0]));
        diffPoly.AddMonomial(new LinearMonomial<Flt64>(new Flt64(-1.0), _x[1]));
        _productionDiff = new LinearExpressionSymbol(diffPoly, name: "productionDiff");
        _metaModel.Add(_productionDiff);

        return Results.Ok(Results.SuccessInstance);
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> InitObjective() {
        // maximize profit
        return _metaModel.AddObject(
            ObjectCategory.Maximum,
            _profit!.Polynomial,
            "profit",
            "Total Profit");
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> InitConstraints() {
        // materialUsage[material] <= available for each material
        foreach (RawMaterial material in RawMaterials) {
            LinearInequality<Flt64> constraint = _materialUsage[material.Index].Polynomial.Le(material.Available);
            Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.AddConstraint(constraint, group: null, name: $"materialLimit_{material.Index}");
            if (result.IsFailed) {
                return result;
            }
        }

        // productionDiff <= 1 (x[0] - x[1] <= 1)
        {
            LinearInequality<Flt64> constraint = _productionDiff!.Polynomial.Le(Flt64.One);
            Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.AddConstraint(constraint, group: null, name: "productionDiffUp");
            if (result.IsFailed) {
                return result;
            }
        }

        // -productionDiff <= 1 (x[1] - x[0] <= 1)
        {
            var negDiffPoly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
            negDiffPoly.AddMonomial(new LinearMonomial<Flt64>(new Flt64(-1.0), _x[0]));
            negDiffPoly.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, _x[1]));
            LinearInequality<Flt64> constraint = negDiffPoly.ToLinearPolynomial().Le(Flt64.One);
            Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.AddConstraint(constraint, group: null, name: "productionDiffDown");
            if (result.IsFailed) {
                return result;
            }
        }

        // x[product] <= maxYield for each product
        foreach (OptProduct product in Products) {
            var boundPoly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
            boundPoly.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, _x[product.Index]));
            LinearInequality<Flt64> constraint = boundPoly.ToLinearPolynomial().Le(product.MaxYield);
            Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.AddConstraint(constraint, group: null, name: $"maxYield_{product.Index}");
            if (result.IsFailed) {
                return result;
            }
        }

        return Results.Ok(Results.SuccessInstance);
    }
}
