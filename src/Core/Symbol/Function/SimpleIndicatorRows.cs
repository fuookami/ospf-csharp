#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Symbol.Function;

/// <summary>
/// 简单不等式与非负检测的指示行 / Indicator rows for a simple inequality and for non-negativity.
/// </summary>
/// <remarks>
/// 对齐基线 `BigM.kt` 的 `simpleIndicatorConstraints`（LE/GE 分支）与 `nonnegativeIndicatorConstraints`。
///
/// **`simpleIndicatorConstraints` 先做<b>差分</b>：`diff = lhs − rhs`。**
/// **之后所有行都只针对 `diff`，与原来的两侧无关。**
/// **这一步容易被写成"直接把 `lhs` 当多项式"——那会让 `rhs` 的项整块消失，
/// 而模型仍然可行（少了一个约束项，不是矛盾），只是条件算错了。**
///
/// **`_violated` 行用的是 `relaxBigM(bigM, strictBoundary)`，不是 `bigM`。**
/// **这与 B1180 的下界行是同一个理由：`strictBoundary` 是"违反"侧的间隔，
/// 照抄 `bigM` 会让违反侧多松 `strictBoundary`。**
///
/// **`nonnegativeIndicatorConstraints` 是"非负检测"，方向与正向指示相反**：
/// **指示 = 1 时 `poly ≥ 0`（允许等于零），指示 = 0 时 `poly ≤ −tolerance`（严格负）。**
///
/// adds
///
/// **`simpleIndicatorConstraints` takes the <b>difference</b> first: `diff = lhs − rhs`.**
/// **Every row then works on `diff` alone.**
/// **That step is easy to write as "use `lhs` directly" — which drops the whole `rhs` side, and the model stays
/// feasible (a missing term, not a contradiction) while the condition is computed wrong.**
///
/// **The `_violated` row uses `relaxBigM(bigM, strictBoundary)`, not `bigM`.**
/// **Same reason as B1180's lower row: `strictBoundary` is the violation-side gap, and copying `bigM` relaxes the
/// violation side by an extra `strictBoundary`.**
///
/// **`nonnegativeIndicatorConstraints` is "non-negativity detection", the opposite polarity of a positive indicator**:
/// **indicator 1 means `poly ≥ 0` (zero allowed), indicator 0 means `poly ≤ −tolerance` (strictly negative).**
/// </remarks>
public static class SimpleIndicatorRows
{
    /// <summary>
    /// 为 LE/GE 不等式构建两条指示行 / Build the two indicator rows for a LE/GE inequality.
    /// </summary>
    /// <param name="inequality">原不等式 / The original inequality.</param>
    /// <param name="indicator">指示列 / The indicator column.</param>
    /// <param name="bigM">Big-M / The big-M.</param>
    /// <param name="tolerance">满足侧的容差 / The satisfaction-side tolerance.</param>
    /// <param name="strictBoundary">违反侧的间隔 / The violation-side gap.</param>
    /// <param name="namePrefix">名称前缀 / The name prefix.</param>
    /// <param name="sideVar">
    /// 偏离方向列；`EQ` 时使用，`null` 表示临建一个 / The deviation-direction column, used by `EQ`; `null` creates one.
    /// </param>
    public static Result<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>
        Build(
            LinearInequality<Flt64> inequality,
            IVariableItem indicator,
            Flt64 bigM,
            Flt64 tolerance,
            Flt64 strictBoundary,
            string namePrefix,
            IVariableItem? sideVar = null) {
        ArgumentNullException.ThrowIfNull(inequality);
        ArgumentNullException.ThrowIfNull(indicator);

        // **差分：`diff = lhs − rhs`。** **不差分会把 `rhs` 整块丢掉。**
        // **Take the difference; skipping it drops the whole right-hand side.**
        var diffMonomials = new List<LinearMonomial<Flt64>>(
            inequality.Lhs.Monomials.Count + inequality.Rhs.Monomials.Count);
        foreach (LinearMonomial<Flt64> monomial in inequality.Lhs.Monomials) {
            diffMonomials.Add(new LinearMonomial<Flt64>(monomial.Coefficient, monomial.Symbol));
        }

        foreach (LinearMonomial<Flt64> monomial in inequality.Rhs.Monomials) {
            diffMonomials.Add(new LinearMonomial<Flt64>(monomial.Coefficient.Negate(), monomial.Symbol));
        }

        Flt64 shiftedConstant = inequality.Lhs.Constant.Minus(inequality.Rhs.Constant);

        if (!ConditionPolynomials.IsUsable(new LinearPolynomial<Flt64>(diffMonomials, shiftedConstant))) {
            return Fail("the indicator difference contains a non-finite or unrepresentable value");
        }

        if (!BigMValues.IsUsable(bigM) || bigM.CompareTo(Flt64.Zero) <= 0) {
            return Fail("the indicator Big-M must be finite, encodable and greater than zero");
        }

        if (!BigMValues.IsUsable(tolerance) || tolerance.CompareTo(Flt64.Zero) < 0) {
            return Fail("the indicator tolerance must be finite, encodable and non-negative");
        }

        if (!BigMValues.IsUsable(strictBoundary) || strictBoundary.CompareTo(Flt64.Zero) <= 0) {
            return Fail("the strict boundary must be finite, encodable and greater than zero");
        }

        Flt64 strictRelaxM = PositiveIndicatorRows.RelaxBigM(bigM, strictBoundary);
        var rows = new List<LinearInequality<Flt64>>(2);

        switch (inequality.Comparison) {
            case Comparison.LE:
                // **满足：指示 = 1 时 `diff ≤ tolerance`**（**`diff + M·ind ≤ tolerance + M`**）。
                // **Satisfied: `diff ≤ tolerance` at indicator 1.**
                rows.Add(Row(
                    diffMonomials, shiftedConstant, indicator, bigM, tolerance.Plus(bigM),
                    Comparison.LE, $"{namePrefix}_sat"));

                // **违反：指示 = 0 时 `diff ≥ strictBoundary`**（**`diff + M'·ind ≥ strictBoundary`**）。
                // **Violated: `diff ≥ strictBoundary` at indicator 0.**
                rows.Add(Row(
                    diffMonomials, shiftedConstant, indicator, strictRelaxM, strictBoundary,
                    Comparison.GE, $"{namePrefix}_violated"));
                break;

            case Comparison.GE:
                // **满足：指示 = 1 时 `diff ≥ −tolerance`**（**`diff − M·ind ≥ −tolerance − M`**）。
                // **Satisfied: `diff ≥ −tolerance` at indicator 1.**
                rows.Add(Row(
                    diffMonomials, shiftedConstant, indicator, bigM.Negate(), tolerance.Negate().Minus(bigM),
                    Comparison.GE, $"{namePrefix}_sat"));

                // **违反：指示 = 0 时 `diff ≤ −strictBoundary`**（**`diff − M'·ind ≤ −strictBoundary`**）。
                // **Violated: `diff ≤ −strictBoundary` at indicator 0.**
                rows.Add(Row(
                    diffMonomials, shiftedConstant, indicator, strictRelaxM.Negate(), strictBoundary.Negate(),
                    Comparison.LE, $"{namePrefix}_violated"));
                break;

            // **`EQ` 走零容差带：`diff == 0` 的"偏离"有两个方向，所以需要方向列。**
            // **调用方不给 `sideVar` 时临建一个——基线也是这样（`BinVar("${namePrefix}_side")`）。**
            // **临建的列<b>必须被调用方登记</b>，否则它是自由的，方向就成了任意值。**
            //
            // **`EQ` goes through the zero band: "deviates from zero" has two directions, so it needs a direction
            // column.** **When the caller supplies none, one is created — as the baseline does.** **A created column
            // <b>must be registered by the caller</b>, or it is free and the direction is arbitrary.**
            case Comparison.EQ:
                IVariableItem side = sideVar ?? new BinVar($"{namePrefix}_side");
                return ZeroIndicatorRows.Build(
                    poly: new LinearPolynomial<Flt64>(diffMonomials, shiftedConstant),
                    indicator: indicator,
                    sideVar: side,
                    bigM: bigM,
                    tolerance: tolerance,
                    strictBoundary: strictBoundary,
                    namePrefix: namePrefix);

            default:
                return Fail($"indicator constraints are not supported for {inequality.Comparison}");
        }

        foreach (LinearInequality<Flt64> row in rows) {
            if (!ConditionPolynomials.IsUsable(row.Lhs) || !ConditionPolynomials.IsUsable(row.Rhs)) {
                return Fail($"the indicator row `{row.Name}` has unusable numeric values");
            }
        }

        return new Ok<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>(rows);
    }

    /// <summary>
    /// 为非负检测构建两条指示行 / Build the two non-negativity indicator rows.
    /// </summary>
    /// <remarks>
    /// **方向与正向指示相反**：**指示 = 1 时 `poly ≥ 0`（**允许等于零**），
    /// 指示 = 0 时 `poly ≤ −tolerance`（**严格负**）。**
    ///
    /// **`≥ 0` 而不是 `≥ tolerance`**：**非负检测问的是"是不是非负"，
    /// 而正向指示问的是"是不是至少 tolerance"——两个问题的分界线不同。**
    ///
    /// **The polarity is the opposite of a positive indicator**: **indicator 1 means `poly ≥ 0` (zero allowed), indicator
    /// 0 means `poly ≤ −tolerance` (strictly negative).**
    ///
    /// **`≥ 0` rather than `≥ tolerance`**: **non-negativity asks "is it at least zero" while a positive indicator asks
    /// "is it at least tolerance" — different dividers.**
    /// </remarks>
    public static Result<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>
        BuildNonnegative(
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

        Flt64 upperRelaxM = PositiveIndicatorRows.RelaxBigM(bigM, tolerance);
        var monomials = new List<LinearMonomial<Flt64>>(poly.Monomials.Count + 1);
        foreach (LinearMonomial<Flt64> monomial in poly.Monomials) {
            monomials.Add(new LinearMonomial<Flt64>(monomial.Coefficient, monomial.Symbol));
        }

        // **下界：`poly − M·ind ≥ −M`。** **指示 = 1 时 `poly ≥ 0`；指示 = 0 时放开到 `poly ≥ −M`。**
        // **Lower: `poly − M·ind ≥ −M`.** **`poly ≥ 0` at indicator 1, relaxed to `poly ≥ −M` at zero.**
        var lowerMonomials = new List<LinearMonomial<Flt64>>(monomials) {
            new LinearMonomial<Flt64>(bigM.Negate(), indicator),
        };

        // **上界：`poly − M'·ind ≤ −tolerance`。** **指示 = 0 时 `poly ≤ −tolerance`。**
        // **Upper: `poly − M'·ind ≤ −tolerance`.** **`poly ≤ −tolerance` at indicator 0.**
        var upperMonomials = new List<LinearMonomial<Flt64>>(monomials) {
            new LinearMonomial<Flt64>(upperRelaxM.Negate(), indicator),
        };

        var rows = new List<LinearInequality<Flt64>>(2) {
            new(
                Lhs: new LinearPolynomial<Flt64>(lowerMonomials, poly.Constant),
                Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), bigM.Negate()),
                Comparison: Comparison.GE,
                Name: $"{namePrefix}_nonnegative_lb"),
            new(
                Lhs: new LinearPolynomial<Flt64>(upperMonomials, poly.Constant),
                Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), tolerance.Negate()),
                Comparison: Comparison.LE,
                Name: $"{namePrefix}_nonnegative_ub"),
        };

        foreach (LinearInequality<Flt64> row in rows) {
            if (!ConditionPolynomials.IsUsable(row.Lhs) || !ConditionPolynomials.IsUsable(row.Rhs)) {
                return Fail($"the indicator row `{row.Name}` has unusable numeric values");
            }
        }

        return new Ok<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>(rows);
    }

    private static LinearInequality<Flt64> Row(
        List<LinearMonomial<Flt64>> diffMonomials,
        Flt64 shiftedConstant,
        IVariableItem indicator,
        Flt64 indicatorCoefficient,
        Flt64 rhsConstant,
        Comparison comparison,
        string name) {
        var monomials = new List<LinearMonomial<Flt64>>(diffMonomials.Count + 1);
        monomials.AddRange(diffMonomials);
        monomials.Add(new LinearMonomial<Flt64>(indicatorCoefficient, indicator));

        return new LinearInequality<Flt64>(
            Lhs: new LinearPolynomial<Flt64>(monomials, shiftedConstant),
            Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), rhsConstant),
            Comparison: comparison,
            Name: name);
    }

    private static Result<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>
        Fail(string message) =>
            new Failed<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument, message);
}
