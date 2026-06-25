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

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo3;
/// <summary>
/// 切割需求：具有宽度和需求数量。Cutting demand with width and demand quantity.
/// </summary>
public sealed class CuttingDemand {
    public int Index { get; }
    public double Width { get; }
    public double Demand { get; }

    public CuttingDemand(int index, double width, double demand) {
        Index = index;
        Width = width;
        Demand = demand;
    }
}

/// <summary>
/// 切割模式：描述如何从原料切割出各产品。Cutting pattern describing how to cut products from raw material.
/// </summary>
public sealed class CuttingPattern {
    public int Index { get; }
    public IReadOnlyList<int> Quantities { get; }

    public CuttingPattern(int index, IReadOnlyList<int> quantities) {
        Index = index;
        Quantities = quantities;
    }
}

/// <summary>
/// 列生成演示：一维下料问题（CSP1D）。
/// Column generation demo: One-dimensional Cutting Stock Problem (CSP1D).
///
/// CSP1D 框架尚未在 C# 端实现，此演示展示核心建模模式。
/// The CSP1D framework is not yet implemented on the C# side; this demo shows the core modeling pattern.
///
/// Column Generation Loop Pattern / 列生成循环模式:
///
///   1. Master Problem (LP Relaxation):
///      - Solve the LP relaxation of the restricted master with current patterns.
///      - Extract dual prices (shadow prices) from demand constraints.
///        主问题（LP 松弛）：用当前模式求解 LP 松弛，提取需求约束的对偶价格。
///
///   2. Pricing Subproblem (Knapsack):
///      - Use dual prices as item profits.
///      - Solve a bounded knapsack: max sum(dual[d] * q[d]) s.t. sum(width[d] * q[d]) &lt;= rawLength.
///      - If reduced cost > 0 (for minimization), the new pattern improves the master.
///        定价子问题（背包）：用对偶价格作为物品利润，求解有界背包问题。
///
///   3. Iterate:
///      - Add the new pattern column to the master.
///      - Repeat until no improving pattern exists (reduced cost &lt;= 0).
///        迭代：将新模式列加入主问题，重复直到无改进模式（缩减成本 &lt;= 0）。
///
///   4. Branch-and-Price (optional):
///      - If LP relaxation is not integer, branch on fractional pattern variables.
///        分支定价（可选）：如果 LP 松弛非整数，对分数模式变量分支。
/// </summary>
public sealed class ColumnGenerationDemo {
    private const double RawLength = 1000.0;

    private static readonly List<CuttingDemand> Demands = new()
    {
        new CuttingDemand(0, 450.0, 97.0),
        new CuttingDemand(1, 360.0, 610.0),
        new CuttingDemand(2, 310.0, 395.0),
        new CuttingDemand(3, 140.0, 211.0)
    };

    private readonly List<CuttingPattern> _patterns = new();
    private readonly List<UIntVar> _y = new();
    private readonly LinearMetaModel<Flt64> _metaModel = new("demo3-csp1d", ObjectCategory.Minimum);

    public LinearMetaModel<Flt64> MetaModel => _metaModel;
    public IReadOnlyList<CuttingPattern> Patterns => _patterns;

    /// <summary>
    /// 构建 CSP1D 主问题模型（使用初始切割模式）。
    /// Build the CSP1D master problem model with initial cutting patterns.
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

        Result<Success, ErrorCode, Error<ErrorCode>> r3 = InitConstraints();
        if (r3.IsFailed) {
            return r3;
        }

        return Results.Ok(Results.SuccessInstance);
    }

    /// <summary>
    /// 执行一次列生成迭代：定价子问题生成新模式。
    /// Perform one column generation iteration: pricing subproblem generates a new pattern.
    ///
    /// In a full implementation this would:
    ///   1. Solve the current master LP relaxation.
    ///   2. Extract dual prices from demand constraints.
    ///   3. Solve the pricing knapsack subproblem using dual prices as profits.
    ///   4. If reduced cost > 0, add the new pattern to the master.
    ///   5. Return whether an improving pattern was found.
    /// </summary>
    /// <param name="dualPrices">对偶价格向量（每个需求一个）/ Dual price vector (one per demand).</param>
    /// <returns>新增模式的切割数量列表，若无改进模式则返回空列表。
    /// Quantities of the new pattern added, or empty if no improving pattern found.</returns>
    public IReadOnlyList<int> ColumnGenerationIteration(IReadOnlyList<double> dualPrices) {
        // Pricing subproblem: bounded knapsack
        // max sum(dualPrice[d] * q[d]) subject to sum(width[d] * q[d]) <= rawLength
        // where q[d] is a non-negative integer for each demand d.

        // Greedy heuristic for the pricing knapsack:
        // Sort demands by profit/width ratio (dual price / width), fill greedily.
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
        // (1 is the cost of using one additional raw roll)
        double reducedCost = ReducedCost(dualPrices, quantities);

        if (reducedCost > 1e-6) {
            // Improving pattern found - add to pattern pool
            var newPattern = new CuttingPattern(_patterns.Count, quantities);
            _patterns.Add(newPattern);
            return quantities;
        }

        // No improving pattern found
        return Array.Empty<int>();
    }

    /// <summary>
    /// 计算给定模式的缩减成本。
    /// Compute the reduced cost of a given pattern.
    ///
    /// Reduced Cost = sum(dualPrice[d] * q[d]) - patternCost
    /// For CSP1D, patternCost = 1 (one raw roll per pattern).
    /// If reduced cost > 0, the pattern improves the current master solution.
    ///
    /// 缩减成本 = sum(对偶价格[d] * 切割数量[d]) - 模式成本。
    /// 对于 CSP1D，模式成本 = 1（每个模式使用一根原料）。
    /// 如果缩减成本 > 0，该模式改进当前主问题解。
    /// </summary>
    /// <param name="dualPrices">对偶价格向量 / Dual price vector.</param>
    /// <param name="quantities">模式中各需求的切割数量 / Quantities of each demand in the pattern.</param>
    /// <returns>缩减成本值 / Reduced cost value.</returns>
    public static double ReducedCost(IReadOnlyList<double> dualPrices, IReadOnlyList<int> quantities) {
        // Reduced cost = sum(dual[d] * q[d]) - 1
        // The "- 1" represents the cost of consuming one additional raw roll.
        double profit = 0.0;
        for (int d = 0; d < dualPrices.Count && d < quantities.Count; d++) {
            profit += dualPrices[d] * quantities[d];
        }
        return profit - 1.0;
    }

    private void GenerateInitialPatterns() {
        // Generate initial single-item patterns: each demand gets its own pattern
        for (int d = 0; d < Demands.Count; d++) {
            int[] quantities = new int[Demands.Count];
            quantities[d] = (int)(RawLength / Demands[d].Width);
            _patterns.Add(new CuttingPattern(d, quantities));
        }
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> InitVariables() {
        // y[pattern] = number of times this pattern is used
        foreach (CuttingPattern pattern in _patterns) {
            var y = new UIntVar($"y_{pattern.Index}");
            _y.Add(y);
            Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.Add(y);
            if (result.IsFailed) {
                return result;
            }
        }
        return Results.Ok(Results.SuccessInstance);
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> InitObjective() {
        // minimize total number of raw material cuts: sum(y)
        var objPoly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
        foreach (UIntVar y in _y) {
            objPoly.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, y));
        }
        return _metaModel.AddObject(
            ObjectCategory.Minimum,
            objPoly.ToLinearPolynomial(),
            "totalCuts",
            "Total Raw Material Cuts");
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> InitConstraints() {
        // For each demand d: sum(pattern.quantities[d] * y[pattern]) >= demand
        for (int d = 0; d < Demands.Count; d++) {
            var demandPoly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
            for (int p = 0; p < _patterns.Count; p++) {
                if (_patterns[p].Quantities[d] > 0) {
                    demandPoly.AddMonomial(new LinearMonomial<Flt64>(
                        new Flt64(_patterns[p].Quantities[d]),
                        _y[p]));
                }
            }
            LinearInequality<Flt64> constraint = demandPoly.ToLinearPolynomial().Ge(new Flt64(Demands[d].Demand));
            Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.AddConstraint(constraint, group: null, name: $"demand_{d}");
            if (result.IsFailed) {
                return result;
            }
        }
        return Results.Ok(Results.SuccessInstance);
    }
}
