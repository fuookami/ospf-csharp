#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Symbol.Function;

/// <summary>
/// 正向指示行：指示为 1 时 <c>poly ≥ tolerance</c>，为 0 时 <c>poly ≤ 0</c> /
/// Positive-indicator rows.
/// </summary>
/// <remarks>
/// 对齐基线 `BigM.kt` 的 `positiveIndicatorConstraints`、`safePositiveIndicatorConstraints`、
/// `relaxBigM` 补。
///
/// **两行，方向相反，而两侧用的 Big-M <b>不是同一个数</b>**：
///
/// | 行 | 系数 | 指示 = 0 | 指示 = 1 |
/// | --- | --- | --- | --- |
/// | `_positive_ub` | `−bigM` | `poly ≤ 0` | `poly ≤ bigM`（放开） |
/// | `_positive_lb` | `−(bigM + \|tol\|)` | `poly ≥ −bigM` | `poly ≥ tolerance` |
///
/// **`_positive_lb` 用的是 `bigM + |tolerance|`，不是 `bigM`——这个差别<b>只在指示 = 0 时</b>显现。**
///
/// **推导**：**下界行要的是 `poly ≥ tolerance − M·(1 − ind)`，即 `poly ≥ (tolerance − M) + M·ind`。**
/// **如果照抄 `M`，指示 = 0 时得到 `poly ≥ tolerance − M`——那是 `−M` <b>加上一个 tolerance</b>，
/// 比 `−M` 更松。**
/// **用 `M + |tol|` 则让指示 = 0 时恰好是 `poly ≥ −M`，也就是"刚好不约束"。**
///
/// **所以这条不是风格差异，是<b>松弛得刚好</b>与<b>多松一个 tolerance</b> 的差异。**
/// **它不会让模型错——两种写法都允许所有合法解——但会让松弛变松，而且看不出原因。**
///
/// Following the baseline's `positiveIndicatorConstraints`, `safePositiveIndicatorConstraints` and
/// `relaxBigM`.
///
/// **Two rows, opposite directions, and the two sides do <b>not</b> use the same Big-M**:
///
/// | Row | Coefficient | indicator = 0 | indicator = 1 |
/// | --- | --- | --- | --- |
/// | `_positive_ub` | `−bigM` | `poly ≤ 0` | `poly ≤ bigM` (relaxed) |
/// | `_positive_lb` | `−(bigM + \|tol\|)` | `poly ≥ −bigM` | `poly ≥ tolerance` |
///
/// **`_positive_lb` uses `bigM + |tolerance|`, not `bigM`, and that difference shows <b>only at indicator 0</b>.**
///
/// **Derivation**: **the lower row wants `poly ≥ tolerance − M·(1 − ind)`, i.e. `poly ≥ (tolerance − M) + M·ind`.**
/// **Copying `M` gives `poly ≥ tolerance − M` at indicator 0 — which is `−M` <b>plus a tolerance</b>, looser than
/// `−M`.** **Using `M + |tol|` makes it exactly `poly ≥ −M`, i.e. "just barely not constraining".**
///
/// **So this is not a style difference but one between <b>relaxed exactly enough</b> and <b>relaxed by an extra
/// tolerance</b>.** **Neither makes the model wrong — both admit every legal solution — but the looser one weakens the
/// relaxation, and nothing shows why.**
/// </remarks>
public static class PositiveIndicatorRows
{
    /// <summary>
    /// 放开用的 Big-M：`bigM + |margin|` / The relaxation Big-M: `bigM + |margin|`.
    /// </summary>
    /// <remarks>
    /// **`|margin|` 而不是 `margin`**：**tolerance 是"正分支的下界"，它可以是负数
    /// （那时"正分支"其实是"至少 −tol"）。取绝对值保证放开量恒为正，否则一个负 tolerance 会让
    /// `bigM + tol &lt; bigM`，于是下界行在指示 = 0 时反而<b>更紧</b>，可能切掉合法解。**
    ///
    /// **`|margin|` rather than `margin`**: **tolerance is "the positive branch's lower edge" and may be negative (then
    /// the "positive" branch is really "at least −tol").** **Taking the absolute value keeps the relaxation positive;
    /// otherwise a negative tolerance makes `bigM + tol &lt; bigM`, so the lower row gets <b>tighter</b> at indicator 0
    /// and can cut off legal solutions.**
    /// </remarks>
    public static Flt64 RelaxBigM(Flt64 bigM, Flt64 margin) => bigM.Plus(Abs(margin));

    /// <summary>构建两条正向指示行 / Build the two positive-indicator rows.</summary>
    /// <param name="poly">被指示的多项式 / The governed polynomial.</param>
    /// <param name="indicator">指示列 / The indicator column.</param>
    /// <param name="bigM">Big-M / The big-M.</param>
    /// <param name="tolerance">正分支下界 / The positive branch's lower edge.</param>
    /// <param name="namePrefix">名称前缀 / The name prefix.</param>
    public static Result<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>
        Build(
            LinearPolynomial<Flt64> poly,
            IVariableItem indicator,
            Flt64 bigM,
            Flt64 tolerance,
            string namePrefix) {
        ArgumentNullException.ThrowIfNull(poly);
        ArgumentNullException.ThrowIfNull(indicator);

        if (!ConditionPolynomials.IsUsable(poly)) {
            return Fail("the indicator polynomial contains a non-finite or unrepresentable value");
        }

        if (!BigMValues.IsUsable(bigM) || bigM.CompareTo(Flt64.Zero) <= 0) {
            return Fail("the indicator Big-M must be finite, encodable and greater than zero");
        }

        if (!BigMValues.IsUsable(tolerance) || tolerance.CompareTo(Flt64.Zero) < 0) {
            return Fail("the indicator tolerance must be finite, encodable and non-negative");
        }

        Flt64 zero = bigM.Minus(bigM);
        Flt64 lowerRelaxM = RelaxBigM(bigM, tolerance);
        var monomials = new List<LinearMonomial<Flt64>>(poly.Monomials.Count + 1);
        foreach (LinearMonomial<Flt64> monomial in poly.Monomials) {
            monomials.Add(new LinearMonomial<Flt64>(monomial.Coefficient, monomial.Symbol));
        }

        // **上界：`poly − bigM·ind ≤ 0`。** **指示 = 0 时 `poly ≤ 0`；指示 = 1 时放开到 `poly ≤ bigM`。**
        //
        // **Upper row: `poly − bigM·ind ≤ 0`.** **`poly ≤ 0` at indicator 0, relaxed to `poly ≤ bigM` at one.**
        var upperMonomials = new List<LinearMonomial<Flt64>>(monomials) {
            new LinearMonomial<Flt64>(bigM.Negate(), indicator),
        };

        // **下界：`poly − (bigM+|tol|)·ind ≥ tolerance − (bigM+|tol|)`。**
        // **指示 = 1 时右侧是 `tolerance`；指示 = 0 时是 `−bigM`。**
        //
        // **Lower row: `poly − (bigM+|tol|)·ind ≥ tolerance − (bigM+|tol|)`.**
        // **The right-hand side is `tolerance` at indicator 1 and `−bigM` at zero.**
        var lowerMonomials = new List<LinearMonomial<Flt64>>(monomials) {
            new LinearMonomial<Flt64>(lowerRelaxM.Negate(), indicator),
        };

        var rows = new List<LinearInequality<Flt64>>(2) {
            new(
                Lhs: new LinearPolynomial<Flt64>(upperMonomials, poly.Constant),
                Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), zero),
                Comparison: Comparison.LE,
                Name: $"{namePrefix}_positive_ub"),
            new(
                Lhs: new LinearPolynomial<Flt64>(lowerMonomials, poly.Constant),
                Rhs: new LinearPolynomial<Flt64>(
                    Array.Empty<LinearMonomial<Flt64>>(), tolerance.Minus(lowerRelaxM)),
                Comparison: Comparison.GE,
                Name: $"{namePrefix}_positive_lb"),
        };

        foreach (LinearInequality<Flt64> row in rows) {
            if (!ConditionPolynomials.IsUsable(row.Lhs) || !ConditionPolynomials.IsUsable(row.Rhs)) {
                return Fail($"the indicator row `{row.Name}` has unusable numeric values");
            }
        }

        return new Ok<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>(rows);
    }

    private static Flt64 Abs(Flt64 value) =>
        value.CompareTo(Flt64.Zero) < 0 ? value.Negate() : value;

    private static Result<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>
        Fail(string message) =>
            new Failed<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument, message);
}
