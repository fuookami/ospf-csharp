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

namespace Fuookami.Ospf.Example.CoreDemo.Demo3;
/// <summary>
/// 产品：具有最低产量要求。Product with minimum yield requirement.
/// </summary>
public sealed class YieldProduct {
    public int Index { get; }
    public string Name { get; }
    public Flt64 MinYield { get; }

    public YieldProduct(int index, string name, Flt64 minYield) {
        Index = index;
        Name = name;
        MinYield = minYield;
    }
}

/// <summary>
/// 材料：具有成本和各产品产量系数。Material with cost and yield coefficient per product.
/// </summary>
public sealed class Material {
    public int Index { get; }
    public string Name { get; }
    public Flt64 Cost { get; }
    public IReadOnlyDictionary<int, Flt64> YieldPerProduct { get; }

    public Material(int index, string name, Flt64 cost, IReadOnlyDictionary<int, Flt64> yieldPerProduct) {
        Index = index;
        Name = name;
        Cost = cost;
        YieldPerProduct = yieldPerProduct;
    }
}

/// <summary>
/// 生产计划优化：在最低产量约束下最小化材料成本。
/// Production planning optimization: minimize material cost subject to minimum yield constraints.
/// </summary>
public sealed class ProductionPlanningDemo {
    private static readonly List<YieldProduct> Products = new()
    {
        new YieldProduct(0, "Product_A", new Flt64(15000.0)),
        new YieldProduct(1, "Product_B", new Flt64(15000.0)),
        new YieldProduct(2, "Product_C", new Flt64(10000.0))
    };

    private static readonly List<Material> Materials = new()
    {
        new Material(0, "Material_1", new Flt64(12.0), new Dictionary<int, Flt64>
        {
            { 0, new Flt64(2.0) }, { 1, new Flt64(1.0) }, { 2, new Flt64(3.0) }
        }),
        new Material(1, "Material_2", new Flt64(8.0), new Dictionary<int, Flt64>
        {
            { 0, new Flt64(1.5) }, { 1, new Flt64(2.0) }, { 2, new Flt64(1.0) }
        }),
        new Material(2, "Material_3", new Flt64(15.0), new Dictionary<int, Flt64>
        {
            { 0, new Flt64(3.0) }, { 1, new Flt64(2.5) }, { 2, new Flt64(2.0) }
        }),
        new Material(3, "Material_4", new Flt64(10.0), new Dictionary<int, Flt64>
        {
            { 0, new Flt64(1.0) }, { 1, new Flt64(1.0) }, { 2, new Flt64(1.5) }
        })
    };

    private readonly List<UIntVar> _x = new();
    private LinearExpressionSymbol? _cost;
    private readonly List<LinearExpressionSymbol> _yields = new();
    private readonly LinearMetaModel<Flt64> _metaModel = new("coredemo3-production-planning", ObjectCategory.Minimum);

    public LinearMetaModel<Flt64> MetaModel => _metaModel;

    /// <summary>
    /// 构建生产计划模型（不求解，用于测试验证）。
    /// Build the production planning model (without solving, for test verification).
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
        foreach (Material material in Materials) {
            var x = new UIntVar($"x_{material.Index}");
            _x.Add(x);
            Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.Add(x);
            if (result.IsFailed) {
                return result;
            }
        }
        return Results.Ok(Results.SuccessInstance);
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> InitSymbols() {
        // cost = sum(material.cost * x[material])
        var costPoly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
        foreach (Material material in Materials) {
            costPoly.AddMonomial(new LinearMonomial<Flt64>(material.Cost, _x[material.Index]));
        }
        _cost = new LinearExpressionSymbol(costPoly, name: "cost");
        _metaModel.Add(_cost);

        // yield[product] = sum(material.yieldPerProduct[product] * x[material])
        foreach (YieldProduct product in Products) {
            var yieldPoly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
            foreach (Material material in Materials) {
                yieldPoly.AddMonomial(new LinearMonomial<Flt64>(
                    material.YieldPerProduct[product.Index],
                    _x[material.Index]));
            }
            var yieldSymbol = new LinearExpressionSymbol(yieldPoly, name: $"yield_{product.Index}");
            _yields.Add(yieldSymbol);
            _metaModel.Add(yieldSymbol);
        }

        return Results.Ok(Results.SuccessInstance);
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> InitObjective() {
        // minimize cost
        return _metaModel.AddObject(
            ObjectCategory.Minimum,
            _cost!.Polynomial,
            "cost",
            "Total Material Cost");
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> InitConstraints() {
        // yield[product] >= minYield for each product
        foreach (YieldProduct product in Products) {
            LinearInequality<Flt64> constraint = _yields[product.Index].Polynomial.Ge(product.MinYield);
            Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.AddConstraint(constraint, group: null, name: $"minYield_{product.Index}");
            if (result.IsFailed) {
                return result;
            }
        }

        return Results.Ok(Results.SuccessInstance);
    }
}
