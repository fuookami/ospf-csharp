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

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo5;
/// <summary>
/// 下料需求：具有宽度和数量。Cutting demand with width and quantity.
/// </summary>
public sealed class StockDemand {
    public int Index { get; }
    public double Width { get; }
    public int Quantity { get; }

    public StockDemand(int index, double width, int quantity) {
        Index = index;
        Width = width;
        Quantity = quantity;
    }
}

/// <summary>
/// 一维下料问题演示（CSP1D）。
/// One-dimensional Cutting Stock Problem demo (CSP1D).
///
/// CSP1D 框架尚未在 C# 端实现，此演示使用核心建模 API 展示 CSP1D 主问题。
/// The CSP1D framework is not yet implemented on the C# side;
/// this demo uses the core modeling API to show the CSP1D master problem.
/// </summary>
public sealed class CuttingStockDemo {
    private const double RawLength = 100.0;

    private static readonly List<StockDemand> Demands = new()
    {
        new StockDemand(0, 45.0, 97),
        new StockDemand(1, 36.0, 610),
        new StockDemand(2, 31.0, 395),
        new StockDemand(3, 14.0, 211)
    };

    private readonly List<List<int>> _patterns = new();
    private readonly List<UIntVar> _patternVars = new();
    private readonly LinearMetaModel<Flt64> _metaModel = new("demo5-cutting-stock", ObjectCategory.Minimum);

    public LinearMetaModel<Flt64> MetaModel => _metaModel;
    public IReadOnlyList<StockDemand> StockDemands => Demands;

    /// <summary>
    /// 构建下料主问题模型。
    /// Build the cutting stock master problem model.
    /// </summary>
    public Result<Success, ErrorCode, Error<ErrorCode>> BuildModel() {
        GenerateInitialPatterns();

        Result<Success, ErrorCode, Error<ErrorCode>> r1 = InitVariables();
        if (r1.IsFailed) {
            return r1;
        }

        Result<Success, ErrorCode, Error<ErrorCode>> r2 = InitObjective();
        if (r2.IsFailed) {
            return r2;
        }

        Result<Success, ErrorCode, Error<ErrorCode>> r3 = InitDemandConstraints();
        if (r3.IsFailed) {
            return r3;
        }

        return Results.Ok(Results.SuccessInstance);
    }

    /// <summary>
    /// 列生成主问题：展示 LP 松弛主问题结构和对偶价格提取模式。
    /// Column generation master problem: show LP relaxation master structure and dual price extraction pattern.
    ///
    /// In a full column generation implementation:
    ///   1. Solve the LP relaxation of this master problem.
    ///   2. Extract dual prices (shadow prices) for each demand constraint.
    ///      Dual price[d] = marginal value of one additional unit of demand d.
    ///   3. Pass dual prices to the pricing subproblem to generate new patterns.
    ///
    /// The master problem structure:
    ///   min  sum_p  y[p]                          (minimize total rolls)
    ///   s.t. sum_p  pattern[p][d] * y[p] >= D[d]  (satisfy demand d)
    ///        y[p] >= 0                             (non-negativity)
    ///
    /// 在完整的列生成实现中：
    ///   1. 求解此主问题的 LP 松弛。
    ///   2. 提取每个需求约束的对偶价格（影子价格）。
    ///      对偶价格[d] = 需求 d 增加一单位的边际价值。
    ///   3. 将对偶价格传递给定价子问题以生成新模式。
    /// </summary>
    /// <param name="dualPrices">
    /// 输出：对偶价格向量（每个需求一个）。调用者在求解 LP 后填入。
    /// Output: dual price vector (one per demand). Caller fills after solving LP.
    /// </param>
    /// <returns>主问题构建结果 / Master problem build result.</returns>
    public Result<Success, ErrorCode, Error<ErrorCode>> ColumnGenerationMaster(out List<double> dualPrices) {
        // Initialize dual prices to zero (would be extracted from LP solution in practice)
        dualPrices = new List<double>();
        for (int d = 0; d < Demands.Count; d++) {
            dualPrices.Add(0.0);
        }

        // The master problem model is already built by BuildModel().
        // In practice, after solving the LP relaxation:
        //   dualPrices[d] = LP solver.GetDualPrice(constraint_d);
        //
        // For CSP1D, dual prices represent how valuable it would be
        // to have one more unit of each demand width available.
        // Higher dual price => more valuable to produce that width.

        // Verify the model is well-formed
        if (_metaModel.MetaSubObjects.Count == 0) {
            return Results.Failed<Success>(
                new Err<ErrorCode>(ErrorCode.ApplicationError, "Master problem has no objective"));
        }

        if (_metaModel.RelationConstraints.Count < Demands.Count) {
            return Results.Failed<Success>(
                new Err<ErrorCode>(ErrorCode.ApplicationError, "Master problem missing demand constraints"));
        }

        return Results.Ok(Results.SuccessInstance);
    }

    /// <summary>
    /// 定价子问题：使用对偶价格生成新切割模式。
    /// Pricing subproblem: generate new cutting pattern using dual prices.
    ///
    /// The pricing subproblem is a bounded knapsack:
    ///   max  sum_d  dualPrice[d] * q[d]
    ///   s.t. sum_d  width[d] * q[d] &lt;= rawLength
    ///        q[d] &gt;= 0, integer
    ///
    /// Reduced cost = optimal value - 1.
    /// If reduced cost > 0, the new pattern improves the master problem.
    ///
    /// 定价子问题是一个有界背包：
    ///   max  sum(对偶价格[d] * 切割数量[d])
    ///   s.t. sum(宽度[d] * 切割数量[d]) &lt;= 原料长度
    ///   缩减成本 = 最优值 - 1。
    ///   如果缩减成本 > 0，新模式改进主问题。
    /// </summary>
    /// <param name="dualPrices">对偶价格向量 / Dual price vector.</param>
    /// <returns>新模式的各需求切割数量，若无改进模式则返回 null。
    /// Quantities for each demand in the new pattern, or null if no improving pattern.</returns>
    public IReadOnlyList<int>? PricingSubproblem(IReadOnlyList<double> dualPrices) {
        // Greedy heuristic for the bounded knapsack pricing problem:
        // Sort items by profit/width ratio, fill greedily.
        var sortedIndices = Demands
            .Select((d, i) => new { Index = i, Ratio = dualPrices[i] / d.Width })
            .OrderByDescending(x => x.Ratio)
            .Select(x => x.Index)
            .ToList();

        double remainingLength = RawLength;
        int[] quantities = new int[Demands.Count];

        foreach (int d in sortedIndices) {
            int maxFit = (int)(remainingLength / Demands[d].Width);
            if (maxFit > 0) {
                quantities[d] = maxFit;
                remainingLength -= maxFit * Demands[d].Width;
            }
        }

        // Compute reduced cost = sum(dual[d] * q[d]) - 1
        double reducedCostValue = 0.0;
        for (int d = 0; d < Demands.Count; d++) {
            reducedCostValue += dualPrices[d] * quantities[d];
        }
        reducedCostValue -= 1.0;

        // Only return the pattern if it improves the master (reduced cost > 0)
        if (reducedCostValue > 1e-6) {
            return quantities;
        }

        return null;
    }

    private void GenerateInitialPatterns() {
        // Initial patterns: one per demand, maximizing that single item
        for (int d = 0; d < Demands.Count; d++) {
            int[] pattern = new int[Demands.Count];
            pattern[d] = (int)(RawLength / Demands[d].Width);
            _patterns.Add(pattern.ToList());
        }
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> InitVariables() {
        for (int p = 0; p < _patterns.Count; p++) {
            var y = new UIntVar($"pattern_{p}");
            _patternVars.Add(y);
            Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.Add(y);
            if (result.IsFailed) {
                return result;
            }
        }
        return Results.Ok(Results.SuccessInstance);
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> InitObjective() {
        // minimize sum of all pattern usages
        var objPoly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
        foreach (UIntVar y in _patternVars) {
            objPoly.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, y));
        }
        return _metaModel.AddObject(
            ObjectCategory.Minimum,
            objPoly.ToLinearPolynomial(),
            "totalRolls",
            "Total Raw Rolls Used");
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> InitDemandConstraints() {
        // For each demand d: sum(pattern[d] * y[pattern]) >= demand.quantity
        for (int d = 0; d < Demands.Count; d++) {
            var poly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
            for (int p = 0; p < _patterns.Count; p++) {
                if (_patterns[p][d] > 0) {
                    poly.AddMonomial(new LinearMonomial<Flt64>(
                        new Flt64(_patterns[p][d]),
                        _patternVars[p]));
                }
            }
            LinearInequality<Flt64> constraint = poly.ToLinearPolynomial().Ge(new Flt64(Demands[d].Quantity));
            Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.AddConstraint(constraint, group: null, name: $"demand_{d}");
            if (result.IsFailed) {
                return result;
            }
        }
        return Results.Ok(Results.SuccessInstance);
    }
}
