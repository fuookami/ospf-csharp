#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
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

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo8
{
    /// <summary>
    /// 投资产品：具有收益率和风险。Investment product with yield and risk.
    /// </summary>
    public sealed class InvestmentProduct
    {
        public int Index { get; }
        public string Name { get; }
        public Flt64 ExpectedYield { get; }
        public Flt64 Risk { get; }

        public InvestmentProduct(int index, string name, Flt64 expectedYield, Flt64 risk)
        {
            Index = index;
            Name = name;
            ExpectedYield = expectedYield;
            Risk = risk;
        }
    }

    /// <summary>
    /// 多目标优化演示：投资组合优化（收益最大化 vs 风险最小化）。
    /// Multi-objective optimization demo: portfolio optimization (maximize yield vs minimize risk).
    ///
    /// 使用加权和方法将多目标转换为单目标。
    /// Uses weighted-sum method to convert multi-objective to single-objective.
    /// </summary>
    public sealed class MultiObjectiveDemo
    {
        private static readonly List<InvestmentProduct> Products = new()
        {
            new InvestmentProduct(0, "Stock-A", new Flt64(0.12), new Flt64(0.15)),
            new InvestmentProduct(1, "Stock-B", new Flt64(0.08), new Flt64(0.10)),
            new InvestmentProduct(2, "Bond-C", new Flt64(0.05), new Flt64(0.03)),
            new InvestmentProduct(3, "Fund-D", new Flt64(0.10), new Flt64(0.12)),
            new InvestmentProduct(4, "Cash-E", new Flt64(0.02), new Flt64(0.01))
        };

        private static readonly Flt64 TotalFunds = new(100.0);
        private static readonly Flt64 MaxRisk = new(0.10);
        private static readonly Flt64 YieldWeight = new(0.6);
        private static readonly Flt64 RiskWeight = new(0.4);

        private readonly List<UIntVar> _x = new();
        private LinearExpressionSymbol? _totalYield;
        private LinearExpressionSymbol? _totalRisk;
        private readonly LinearMetaModel<Flt64> _metaModel = new("demo8-multi-objective", ObjectCategory.Maximum);

        public LinearMetaModel<Flt64> MetaModel => _metaModel;
        public IReadOnlyList<InvestmentProduct> InvestmentProducts => Products;

        /// <summary>
        /// 构建多目标投资模型（加权和法）。
        /// Build the multi-objective investment model (weighted sum method).
        /// </summary>
        public Result<Success, ErrorCode, Error<ErrorCode>> BuildModel()
        {
            var r1 = InitVariables();
            if (r1.IsFailed) return r1;

            var r2 = InitSymbols();
            if (r2.IsFailed) return r2;

            var r3 = InitObjective();
            if (r3.IsFailed) return r3;

            var r4 = InitConstraints();
            if (r4.IsFailed) return r4;

            return Results.Ok(Results.SuccessInstance);
        }

        private Result<Success, ErrorCode, Error<ErrorCode>> InitVariables()
        {
            // x[i] = amount invested in product i
            foreach (var product in Products)
            {
                var x = new UIntVar($"x_{product.Index}");
                _x.Add(x);
                var result = _metaModel.Add(x);
                if (result.IsFailed) return result;
            }
            return Results.Ok(Results.SuccessInstance);
        }

        private Result<Success, ErrorCode, Error<ErrorCode>> InitSymbols()
        {
            // totalYield = sum(product.expectedYield * x[product])
            var yieldPoly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
            foreach (var product in Products)
            {
                yieldPoly.AddMonomial(new LinearMonomial<Flt64>(product.ExpectedYield, _x[product.Index]));
            }
            _totalYield = new LinearExpressionSymbol(yieldPoly, name: "totalYield");
            _metaModel.Add(_totalYield);

            // totalRisk = sum(product.risk * x[product])
            var riskPoly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
            foreach (var product in Products)
            {
                riskPoly.AddMonomial(new LinearMonomial<Flt64>(product.Risk, _x[product.Index]));
            }
            _totalRisk = new LinearExpressionSymbol(riskPoly, name: "totalRisk");
            _metaModel.Add(_totalRisk);

            return Results.Ok(Results.SuccessInstance);
        }

        private Result<Success, ErrorCode, Error<ErrorCode>> InitObjective()
        {
            // maximize (yieldWeight * yield - riskWeight * risk)
            var objPoly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
            foreach (var mono in _totalYield!.Polynomial.Monomials)
            {
                objPoly.AddMonomial(new LinearMonomial<Flt64>(
                    mono.Coefficient.Times(YieldWeight), mono.Symbol));
            }
            foreach (var mono in _totalRisk!.Polynomial.Monomials)
            {
                objPoly.AddMonomial(new LinearMonomial<Flt64>(
                    mono.Coefficient.Times(RiskWeight).Negate(), mono.Symbol));
            }
            return _metaModel.AddObject(
                ObjectCategory.Maximum,
                objPoly.ToLinearPolynomial(),
                "weightedObjective",
                "Weighted Yield - Risk");
        }

        private Result<Success, ErrorCode, Error<ErrorCode>> InitConstraints()
        {
            // sum(x) = totalFunds
            var fundsPoly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
            foreach (var x in _x)
            {
                fundsPoly.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, x));
            }
            var fundsConstraint = fundsPoly.ToLinearPolynomial().Eq(TotalFunds);
            var r1 = _metaModel.AddConstraint(fundsConstraint, group: null, name: "totalFunds");
            if (r1.IsFailed) return r1;

            // risk <= maxRisk * totalFunds
            var riskConstraint = _totalRisk!.Polynomial.Le(MaxRisk.Times(TotalFunds));
            var r2 = _metaModel.AddConstraint(riskConstraint, group: null, name: "maxRisk");
            if (r2.IsFailed) return r2;

            return Results.Ok(Results.SuccessInstance);
        }
    }
}
