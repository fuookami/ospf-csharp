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

namespace Fuookami.Ospf.Example.CoreDemo.Demo12;
/// <summary>
/// 投资产品：具有收益率、风险、溢价和最低溢价要求。
/// Investment product with yield, risk, premium and minimum premium requirements.
/// </summary>
public sealed class InvestmentProduct {
    public int Index { get; }
    public Flt64 Yield { get; }
    public Flt64 Risk { get; }
    public Flt64 Premium { get; }
    public Flt64 MinPremium { get; }

    public InvestmentProduct(int index, Flt64 yield, Flt64 risk, Flt64 premium, Flt64 minPremium) {
        Index = index;
        Yield = yield;
        Risk = risk;
        Premium = premium;
        MinPremium = minPremium;
    }
}

/// <summary>
/// 投资组合优化演示：在风险和资金约束下最大化收益。
/// Investment portfolio optimization demo: maximize yield subject to risk and fund allocation constraints.
/// </summary>
public sealed class InvestmentPortfolioDemo {
    // NumProducts removed — unused field (CS0414)

    private static readonly Flt64 Funds = new(1000000);
    private static readonly Flt64 MaxRisk = new(0.02);

    private static readonly List<InvestmentProduct> Products = new()
    {
        new InvestmentProduct(0, new Flt64(0.08), new Flt64(0.05), new Flt64(0.02), new Flt64(0.01)),
        new InvestmentProduct(1, new Flt64(0.12), new Flt64(0.08), new Flt64(0.03), new Flt64(0.02)),
        new InvestmentProduct(2, new Flt64(0.06), new Flt64(0.03), new Flt64(0.01), new Flt64(0.005)),
        new InvestmentProduct(3, new Flt64(0.10), new Flt64(0.06), new Flt64(0.025), new Flt64(0.015)),
        new InvestmentProduct(4, new Flt64(0.15), new Flt64(0.10), new Flt64(0.04), new Flt64(0.025))
    };

    private readonly List<UIntVar> _x = new();
    private LinearExpressionSymbol? _totalYield;
    private LinearExpressionSymbol? _totalAllocation;
    private LinearExpressionSymbol? _totalRisk;
    private readonly LinearMetaModel<Flt64> _metaModel = new("demo12-investment-portfolio", ObjectCategory.Maximum);

    public LinearMetaModel<Flt64> MetaModel => _metaModel;

    /// <summary>
    /// 构建投资组合优化模型（不求解，用于测试验证）。
    /// Build the investment portfolio optimization model (without solving, for test verification).
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
        foreach (InvestmentProduct product in Products) {
            var x = new UIntVar($"x_{product.Index}");
            _x.Add(x);
            Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.Add(x);
            if (result.IsFailed) {
                return result;
            }
        }
        return Results.Ok(Results.SuccessInstance);
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> InitSymbols() {
        // totalYield = sum(yield[p] * x[p])
        var yieldPoly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
        foreach (InvestmentProduct product in Products) {
            yieldPoly.AddMonomial(new LinearMonomial<Flt64>(product.Yield, _x[product.Index]));
        }
        _totalYield = new LinearExpressionSymbol(yieldPoly, name: "totalYield");
        _metaModel.Add(_totalYield);

        // totalAllocation = sum(x[p])
        var allocPoly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
        foreach (InvestmentProduct product in Products) {
            allocPoly.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, _x[product.Index]));
        }
        _totalAllocation = new LinearExpressionSymbol(allocPoly, name: "totalAllocation");
        _metaModel.Add(_totalAllocation);

        // totalRisk = sum(risk[p] * x[p])
        var riskPoly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
        foreach (InvestmentProduct product in Products) {
            riskPoly.AddMonomial(new LinearMonomial<Flt64>(product.Risk, _x[product.Index]));
        }
        _totalRisk = new LinearExpressionSymbol(riskPoly, name: "totalRisk");
        _metaModel.Add(_totalRisk);

        return Results.Ok(Results.SuccessInstance);
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> InitObjective() {
        // maximize total yield
        return _metaModel.AddObject(
            ObjectCategory.Maximum,
            _totalYield!.Polynomial,
            "totalYield",
            "Total Investment Yield");
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> InitConstraints() {
        // Fund allocation constraint: sum(x[p]) = funds
        {
            LinearInequality<Flt64> constraint = _totalAllocation!.Polynomial.Eq(Funds);
            Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.AddConstraint(constraint, group: null, name: "fundAllocation");
            if (result.IsFailed) {
                return result;
            }
        }

        // Risk constraint: sum(risk[p] * x[p]) <= maxRisk * funds
        {
            Flt64 maxRiskAmount = MaxRisk.Times(Funds);
            LinearInequality<Flt64> constraint = _totalRisk!.Polynomial.Le(maxRiskAmount);
            Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.AddConstraint(constraint, group: null, name: "maxRisk");
            if (result.IsFailed) {
                return result;
            }
        }

        return Results.Ok(Results.SuccessInstance);
    }
}
