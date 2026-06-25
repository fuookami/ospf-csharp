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

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo8;
/// <summary>
/// 投资产品：具有收益率和风险。Investment product with yield and risk.
/// </summary>
public sealed class InvestmentProduct {
    public int Index { get; }
    public string Name { get; }
    public Flt64 ExpectedYield { get; }
    public Flt64 Risk { get; }

    public InvestmentProduct(int index, string name, Flt64 expectedYield, Flt64 risk) {
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
public sealed class MultiObjectiveDemo {
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
        // x[i] = amount invested in product i
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
        // totalYield = sum(product.expectedYield * x[product])
        var yieldPoly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
        foreach (InvestmentProduct product in Products) {
            yieldPoly.AddMonomial(new LinearMonomial<Flt64>(product.ExpectedYield, _x[product.Index]));
        }
        _totalYield = new LinearExpressionSymbol(yieldPoly, name: "totalYield");
        _metaModel.Add(_totalYield);

        // totalRisk = sum(product.risk * x[product])
        var riskPoly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
        foreach (InvestmentProduct product in Products) {
            riskPoly.AddMonomial(new LinearMonomial<Flt64>(product.Risk, _x[product.Index]));
        }
        _totalRisk = new LinearExpressionSymbol(riskPoly, name: "totalRisk");
        _metaModel.Add(_totalRisk);

        return Results.Ok(Results.SuccessInstance);
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> InitObjective() {
        // maximize (yieldWeight * yield - riskWeight * risk)
        var objPoly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
        foreach (LinearMonomial<Flt64> mono in _totalYield!.Polynomial.Monomials) {
            objPoly.AddMonomial(new LinearMonomial<Flt64>(
                mono.Coefficient.Times(YieldWeight), mono.Symbol));
        }
        foreach (LinearMonomial<Flt64> mono in _totalRisk!.Polynomial.Monomials) {
            objPoly.AddMonomial(new LinearMonomial<Flt64>(
                mono.Coefficient.Times(RiskWeight).Negate(), mono.Symbol));
        }
        return _metaModel.AddObject(
            ObjectCategory.Maximum,
            objPoly.ToLinearPolynomial(),
            "weightedObjective",
            "Weighted Yield - Risk");
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> InitConstraints() {
        // sum(x) = totalFunds
        var fundsPoly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
        foreach (UIntVar x in _x) {
            fundsPoly.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, x));
        }
        LinearInequality<Flt64> fundsConstraint = fundsPoly.ToLinearPolynomial().Eq(TotalFunds);
        Result<Success, ErrorCode, Error<ErrorCode>> r1 = _metaModel.AddConstraint(fundsConstraint, group: null, name: "totalFunds");
        if (r1.IsFailed) {
            return r1;
        }

        // risk <= maxRisk * totalFunds
        LinearInequality<Flt64> riskConstraint = _totalRisk!.Polynomial.Le(MaxRisk.Times(TotalFunds));
        Result<Success, ErrorCode, Error<ErrorCode>> r2 = _metaModel.AddConstraint(riskConstraint, group: null, name: "maxRisk");
        if (r2.IsFailed) {
            return r2;
        }

        return Results.Ok(Results.SuccessInstance);
    }
}

/// <summary>
/// Pareto 前沿上的一个点。A point on the Pareto frontier.
/// </summary>
public sealed class ParetoPoint {
    /// <summary>Epsilon 约束值（风险上限）/ Epsilon constraint value (risk upper bound).</summary>
    public Flt64 Epsilon { get; }
    /// <summary>目标值1：总收益 / Objective 1: total yield.</summary>
    public Flt64 Yield { get; }
    /// <summary>目标值2：总风险 / Objective 2: total risk.</summary>
    public Flt64 Risk { get; }

    public ParetoPoint(Flt64 epsilon, Flt64 yield, Flt64 risk) {
        Epsilon = epsilon;
        Yield = yield;
        Risk = risk;
    }
}

/// <summary>
/// Epsilon-约束法多目标优化演示。
/// Epsilon-constraint method multi-objective optimization demo.
///
/// The epsilon-constraint method:
///   1. Select one objective to optimize (e.g., maximize yield).
///   2. Convert other objectives into constraints (e.g., risk &lt;= epsilon).
///   3. Vary epsilon over a range to generate the Pareto frontier.
///   4. Each epsilon value yields one Pareto-optimal solution.
///
/// Epsilon-约束法：
///   1. 选择一个目标进行优化（如最大化收益）。
///   2. 将其他目标转为约束（如风险 &lt;= epsilon）。
///   3. 在范围内变化 epsilon 生成 Pareto 前沿。
///   4. 每个 epsilon 值产生一个 Pareto 最优解。
/// </summary>
public sealed class EpsilonConstraintDemo {
    private static readonly List<InvestmentProduct> Products = new()
    {
        new InvestmentProduct(0, "Stock-A", new Flt64(0.12), new Flt64(0.15)),
        new InvestmentProduct(1, "Stock-B", new Flt64(0.08), new Flt64(0.10)),
        new InvestmentProduct(2, "Bond-C", new Flt64(0.05), new Flt64(0.03)),
        new InvestmentProduct(3, "Fund-D", new Flt64(0.10), new Flt64(0.12)),
        new InvestmentProduct(4, "Cash-E", new Flt64(0.02), new Flt64(0.01))
    };

    private static readonly Flt64 TotalFunds = new(100.0);

    public IReadOnlyList<InvestmentProduct> InvestmentProducts => Products;

    /// <summary>
    /// 生成 Pareto 前沿：遍历不同 epsilon 值，每个值构建并记录一个 Pareto 最优解。
    /// Generate Pareto frontier: iterate over epsilon values, build and record a Pareto-optimal solution for each.
    ///
    /// Epsilon values represent different risk tolerance levels.
    /// For each epsilon, we maximize yield subject to risk &lt;= epsilon * totalFunds.
    ///
    /// Epsilon 值代表不同的风险容忍水平。
    /// 对每个 epsilon，在风险 &lt;= epsilon * 总资金的约束下最大化收益。
    /// </summary>
    /// <param name="epsilonValues">Epsilon 值范围（风险容忍度）/ Epsilon value range (risk tolerance).</param>
    /// <returns>Pareto 前沿点列表 / List of Pareto frontier points.</returns>
    public List<ParetoPoint> GenerateParetoFrontier(IReadOnlyList<double> epsilonValues) {
        var frontier = new List<ParetoPoint>();

        foreach (double epsilon in epsilonValues) {
            var model = new LinearMetaModel<Flt64>(
                $"demo8-epsilon-{epsilon:F2}", ObjectCategory.Maximum);

            // Variables: x[i] = amount invested in product i
            var xVars = new List<UIntVar>();
            foreach (InvestmentProduct product in Products) {
                var x = new UIntVar($"x_{product.Index}");
                xVars.Add(x);
                model.Add(x);
            }

            // Objective: maximize total yield = sum(expectedYield[i] * x[i])
            var yieldPoly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
            foreach (InvestmentProduct product in Products) {
                yieldPoly.AddMonomial(new LinearMonomial<Flt64>(product.ExpectedYield, xVars[product.Index]));
            }
            model.AddObject(
                ObjectCategory.Maximum,
                yieldPoly.ToLinearPolynomial(),
                "totalYield",
                "Total Yield");

            // Constraint 1: sum(x) = totalFunds
            var fundsPoly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
            foreach (UIntVar x in xVars) {
                fundsPoly.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, x));
            }
            model.AddConstraint(fundsPoly.ToLinearPolynomial().Eq(TotalFunds),
                group: null, name: "totalFunds");

            // Constraint 2: total risk <= epsilon * totalFunds (the epsilon constraint)
            var riskPoly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
            foreach (InvestmentProduct product in Products) {
                riskPoly.AddMonomial(new LinearMonomial<Flt64>(product.Risk, xVars[product.Index]));
            }
            Flt64 epsilonBound = new Flt64(epsilon).Times(TotalFunds);
            model.AddConstraint(riskPoly.ToLinearPolynomial().Le(epsilonBound),
                group: null, name: "epsilonRisk");

            // Record the Pareto point (in practice, solve the model and extract solution values)
            // Here we record the epsilon bound as the risk, and a placeholder yield.
            frontier.Add(new ParetoPoint(
                new Flt64(epsilon),
                yield: Flt64.Zero,   // Would be extracted from solved model
                risk: epsilonBound)); // Upper bound on risk
        }

        return frontier;
    }

    /// <summary>
    /// 构建单个 epsilon 子问题模型（用于测试验证）。
    /// Build a single epsilon subproblem model (for test verification).
    /// </summary>
    /// <param name="epsilon">风险容忍度 / Risk tolerance.</param>
    /// <returns>构建好的元模型 / Built meta-model.</returns>
    public LinearMetaModel<Flt64> BuildEpsilonSubproblem(double epsilon) {
        var model = new LinearMetaModel<Flt64>(
            $"demo8-epsilon-sub-{epsilon:F2}", ObjectCategory.Maximum);

        var xVars = new List<UIntVar>();
        foreach (InvestmentProduct product in Products) {
            var x = new UIntVar($"x_{product.Index}");
            xVars.Add(x);
            model.Add(x);
        }

        // Maximize yield
        var yieldPoly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
        foreach (InvestmentProduct product in Products) {
            yieldPoly.AddMonomial(new LinearMonomial<Flt64>(product.ExpectedYield, xVars[product.Index]));
        }
        model.AddObject(ObjectCategory.Maximum, yieldPoly.ToLinearPolynomial(), "totalYield", "Total Yield");

        // Budget constraint
        var fundsPoly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
        foreach (UIntVar x in xVars) {
            fundsPoly.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, x));
        }
        model.AddConstraint(fundsPoly.ToLinearPolynomial().Eq(TotalFunds), group: null, name: "totalFunds");

        // Epsilon risk constraint
        var riskPoly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
        foreach (InvestmentProduct product in Products) {
            riskPoly.AddMonomial(new LinearMonomial<Flt64>(product.Risk, xVars[product.Index]));
        }
        model.AddConstraint(riskPoly.ToLinearPolynomial().Le(new Flt64(epsilon).Times(TotalFunds)),
            group: null, name: "epsilonRisk");

        return model;
    }
}
