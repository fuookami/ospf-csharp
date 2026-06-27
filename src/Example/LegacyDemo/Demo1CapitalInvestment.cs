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

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo1;
/// <summary>
/// 资本投资优化：在资本和负债约束下最大化利润。
/// Capital investment optimization: maximize profit subject to capital and liability constraints.
/// </summary>
public sealed class Company {
    public int Index { get; }
    public Flt64 Capital { get; }
    public Flt64 Liability { get; }
    public Flt64 Profit { get; }

    public Company(int index, Flt64 capital, Flt64 liability, Flt64 profit) {
        Index = index;
        Capital = capital;
        Liability = liability;
        Profit = profit;
    }
}

/// <summary>
/// 简单线性规划演示：资本投资优化。
/// Simple linear programming demo: capital investment optimization.
/// </summary>
public sealed class CapitalInvestmentDemo {
    private static readonly List<Company> Companies = new()
    {
        new Company(0, new Flt64(3.48), new Flt64(1.28), new Flt64(5400.0)),
        new Company(1, new Flt64(5.62), new Flt64(2.53), new Flt64(2300.0)),
        new Company(2, new Flt64(7.33), new Flt64(1.02), new Flt64(4600.0)),
        new Company(3, new Flt64(6.27), new Flt64(3.55), new Flt64(3300.0)),
        new Company(4, new Flt64(2.14), new Flt64(0.53), new Flt64(980.0))
    };

    private static readonly Flt64 MinCapital = new(10.0);
    private static readonly Flt64 MaxLiability = new(5.0);

    private readonly List<BinVar> _x = new();
    private LinearExpressionSymbol? _capital;
    private LinearExpressionSymbol? _liability;
    private LinearExpressionSymbol? _profit;
    private readonly LinearMetaModel<Flt64> _metaModel = new("demo1-capital-investment", ObjectCategory.Maximum);

    public LinearMetaModel<Flt64> MetaModel => _metaModel;

    /// <summary>
    /// 构建投资优化模型（不求解，用于测试验证）。
    /// Build the investment optimization model (without solving, for test verification).
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
        foreach (Company company in Companies) {
            var x = new BinVar($"x_{company.Index}");
            _x.Add(x);
            Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.Add(x);
            if (result.IsFailed) {
                return result;
            }
        }
        return Results.Ok(Results.SuccessInstance);
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> InitSymbols() {
        // Capital = sum(company.capital * x[company])
        var capitalPoly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
        foreach (Company company in Companies) {
            capitalPoly.AddMonomial(new LinearMonomial<Flt64>(company.Capital, _x[company.Index]));
        }
        _capital = new LinearExpressionSymbol(capitalPoly, name: "capital");
        _metaModel.Add(_capital);

        // Liability = sum(company.liability * x[company])
        var liabilityPoly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
        foreach (Company company in Companies) {
            liabilityPoly.AddMonomial(new LinearMonomial<Flt64>(company.Liability, _x[company.Index]));
        }
        _liability = new LinearExpressionSymbol(liabilityPoly, name: "liability");
        _metaModel.Add(_liability);

        // Profit = sum(company.profit * x[company])
        var profitPoly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
        foreach (Company company in Companies) {
            profitPoly.AddMonomial(new LinearMonomial<Flt64>(company.Profit, _x[company.Index]));
        }
        _profit = new LinearExpressionSymbol(profitPoly, name: "profit");
        _metaModel.Add(_profit);

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
        // capital >= minCapital
        LinearInequality<Flt64> capitalConstraint = _capital!.Polynomial.Ge(MinCapital);
        Result<Success, ErrorCode, Error<ErrorCode>> r1 = _metaModel.AddConstraint(capitalConstraint, group: null, name: "minCapital");
        if (r1.IsFailed) {
            return r1;
        }

        // liability <= maxLiability
        LinearInequality<Flt64> liabilityConstraint = _liability!.Polynomial.Le(MaxLiability);
        Result<Success, ErrorCode, Error<ErrorCode>> r2 = _metaModel.AddConstraint(liabilityConstraint, group: null, name: "maxLiability");
        if (r2.IsFailed) {
            return r2;
        }

        return Results.Ok(Results.SuccessInstance);
    }
}
