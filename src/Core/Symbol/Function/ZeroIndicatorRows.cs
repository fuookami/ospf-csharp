#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Symbol.Function;

/// <summary>
/// 零值指示行：指示为 1 时落在容差带内，为 0 时至少偏离 <c>strictBoundary</c> /
/// Zero-detection indicator rows.
/// </summary>
/// <remarks>
/// 对齐基线 `BigM.kt` 的 `zeroIndicatorConstraints`、`safeZeroIndicatorConstraints`。
///
/// **四条行，而<b>第二条变量（`sideVar`）只在"偏离"那一侧出现</b>**：
///
/// | 行 | 内容 | 何时生效 |
/// | --- | --- | --- |
/// | `_zero_band_ub` | `poly + bandM·ind ≤ tolerance + bandM` | 指示 = 1 时 `poly ≤ tolerance` |
/// | `_zero_band_lb` | `poly − bandM·ind ≥ −tolerance − bandM` | 指示 = 1 时 `poly ≥ −tolerance` |
/// | `_zero_out_lb` | `poly + outM·ind − outM·side ≥ strictBoundary − outM` | 指示 = 0 且 `side = 1` 时 `poly ≥ strictBoundary` |
/// | `_zero_out_ub` | `poly − outM·ind − outM·side ≤ −strictBoundary` | 指示 = 0 且 `side = 0` 时 `poly ≤ −strictBoundary` |
///
/// **为什么需要 `sideVar`**：**"偏离零"有两个方向。**
/// **一个二值指示只能表达"是或不是"，说不清"往哪边偏"——所以偏离侧需要<b>第二个二值</b>来选方向。**
/// **没有它，模型只能表达"偏离"而不能表达"偏向正还是负"，
/// 于是 `poly = +5` 与 `poly = −5` 会被这套行混成一个状态。**
///
/// **`bandM` 与 `outM` 是两个<b>不同</b>的松弛量**：
/// **`bandM = bigM + |tolerance|`（带内侧），`outM = bigM + |strictBoundary|`（带外侧）。**
/// **用同一个会让其中一侧多松或过紧——而两侧的间隔本来就不同。**
///
/// **`strictBoundary` 必须<b>严格大于</b> `tolerance`**：**否则"带内"与"带外"在边界上重叠，
/// 一个点可以同时声称"在带内"与"偏离了"。** **这种重叠不会让模型不可行，只会让它有第二个解。**
///
/// Following the baseline's `zeroIndicatorConstraints` and `safeZeroIndicatorConstraints`.
///
/// **Four rows, and the <b>second binary (`sideVar`) appears only on the deviation side</b>**:
///
/// | Row | Content | When it binds |
/// | --- | --- | --- |
/// | `_zero_band_ub` | `poly + bandM·ind ≤ tolerance + bandM` | `poly ≤ tolerance` at indicator 1 |
/// | `_zero_band_lb` | `poly − bandM·ind ≥ −tolerance − bandM` | `poly ≥ −tolerance` at indicator 1 |
/// | `_zero_out_lb` | `poly + outM·ind − outM·side ≥ strictBoundary − outM` | `poly ≥ strictBoundary` at indicator 0, side 1 |
/// | `_zero_out_ub` | `poly − outM·ind − outM·side ≤ −strictBoundary` | `poly ≤ −strictBoundary` at indicator 0, side 0 |
///
/// **Why `sideVar` is needed**: **"deviates from zero" has two directions.**
/// **One binary indicator can say "is or is not" but not "which way" — so the deviation side needs a <b>second
/// binary</b> to pick the direction.** **Without it the model expresses "deviates" without expressing "deviates
/// positive or negative", and `poly = +5` and `poly = −5` collapse into one state.**
///
/// **`bandM` and `outM` are two <b>different</b> relaxations**: **`bandM = bigM + |tolerance|` (inside the band),
/// `outM = bigM + |strictBoundary|` (outside).** **Using one for both over- or under-relaxes one side, and the two
/// gaps are different to begin with.**
///
/// **`strictBoundary` must be <b>strictly greater</b> than `tolerance`**: **otherwise "inside the band" and "outside"
/// overlap at the edge, and a point can claim both.** **That overlap does not make the model infeasible — it gives it a
/// second solution.**
/// </remarks>
public static class ZeroIndicatorRows
{
    /// <summary>构建四条零值指示行 / Build the four zero-detection rows.</summary>
    /// <param name="poly">被检测的多项式 / The detected polynomial.</param>
    /// <param name="indicator">"落在带内"的指示列 / The in-band indicator.</param>
    /// <param name="sideVar">偏离方向列 / The deviation-direction column.</param>
    /// <param name="bigM">Big-M / The big-M.</param>
    /// <param name="tolerance">带内半宽 / The in-band half-width.</param>
    /// <param name="strictBoundary">带外间隔 / The outside gap.</param>
    /// <param name="namePrefix">名称前缀 / The name prefix.</param>
    public static Result<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>
        Build(
            LinearPolynomial<Flt64> poly,
            IVariableItem indicator,
            IVariableItem sideVar,
            Flt64 bigM,
            Flt64 tolerance,
            Flt64 strictBoundary,
            string namePrefix) {
        ArgumentNullException.ThrowIfNull(poly);
        ArgumentNullException.ThrowIfNull(indicator);
        ArgumentNullException.ThrowIfNull(sideVar);

        if (!ConditionPolynomials.IsUsable(poly)) {
            return Fail("the indicator polynomial contains a non-finite or unrepresentable value");
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

        // **带内与带外必须在边界上分开。**
        // **The band and the outside must be separated at the edge.**
        if (strictBoundary.CompareTo(tolerance) <= 0) {
            return Fail(
                $"the strict boundary ({strictBoundary}) must exceed the tolerance ({tolerance}), " +
                "or the two states overlap at the edge");
        }

        // **同一个变量当指示与方向列，会让"偏离方向"与"是否偏离"绑定——两个二值退化成一个。**
        // **One variable serving both roles ties "which way" to "whether", collapsing two binaries into one.**
        if (ReferenceEquals(sideVar, indicator)) {
            return Fail(
                "the deviation-direction column must differ from the in-band indicator, or the two states collapse");
        }

        Flt64 bandM = PositiveIndicatorRows.RelaxBigM(bigM, tolerance);
        Flt64 outM = PositiveIndicatorRows.RelaxBigM(bigM, strictBoundary);

        var polyMonomials = new List<LinearMonomial<Flt64>>(poly.Monomials.Count);
        foreach (LinearMonomial<Flt64> monomial in poly.Monomials) {
            polyMonomials.Add(new LinearMonomial<Flt64>(monomial.Coefficient, monomial.Symbol));
        }

        var rows = new List<LinearInequality<Flt64>>(4) {
            // **带内上界：指示 = 1 时 `poly ≤ tolerance`。**
            // **In-band upper: `poly ≤ tolerance` at indicator 1.**
            new(
                Lhs: new LinearPolynomial<Flt64>(
                    Append(polyMonomials, new LinearMonomial<Flt64>(bandM, indicator)), poly.Constant),
                Rhs: new LinearPolynomial<Flt64>(
                    Array.Empty<LinearMonomial<Flt64>>(), tolerance.Plus(bandM)),
                Comparison: Comparison.LE,
                Name: $"{namePrefix}_zero_band_ub"),

            // **带内下界：指示 = 1 时 `poly ≥ −tolerance`。**
            // **In-band lower: `poly ≥ −tolerance` at indicator 1.**
            new(
                Lhs: new LinearPolynomial<Flt64>(
                    Append(polyMonomials, new LinearMonomial<Flt64>(bandM.Negate(), indicator)), poly.Constant),
                Rhs: new LinearPolynomial<Flt64>(
                    Array.Empty<LinearMonomial<Flt64>>(), tolerance.Negate().Minus(bandM)),
                Comparison: Comparison.GE,
                Name: $"{namePrefix}_zero_band_lb"),

            // **正向偏离：指示 = 0 且 `side = 1` 时 `poly ≥ strictBoundary`。**
            // **Positive deviation: `poly ≥ strictBoundary` at indicator 0, side 1.**
            new(
                Lhs: new LinearPolynomial<Flt64>(
                    Append2(
                        polyMonomials,
                        new LinearMonomial<Flt64>(outM, indicator),
                        new LinearMonomial<Flt64>(outM.Negate(), sideVar)),
                    poly.Constant),
                Rhs: new LinearPolynomial<Flt64>(
                    Array.Empty<LinearMonomial<Flt64>>(), strictBoundary.Minus(outM)),
                Comparison: Comparison.GE,
                Name: $"{namePrefix}_zero_out_lb"),

            // **负向偏离：指示 = 0 且 `side = 0` 时 `poly ≤ −strictBoundary`。**
            // **Negative deviation: `poly ≤ −strictBoundary` at indicator 0, side 0.**
            new(
                Lhs: new LinearPolynomial<Flt64>(
                    Append2(
                        polyMonomials,
                        new LinearMonomial<Flt64>(outM.Negate(), indicator),
                        new LinearMonomial<Flt64>(outM.Negate(), sideVar)),
                    poly.Constant),
                Rhs: new LinearPolynomial<Flt64>(
                    Array.Empty<LinearMonomial<Flt64>>(), strictBoundary.Negate()),
                Comparison: Comparison.LE,
                Name: $"{namePrefix}_zero_out_ub"),
        };

        foreach (LinearInequality<Flt64> row in rows) {
            if (!ConditionPolynomials.IsUsable(row.Lhs) || !ConditionPolynomials.IsUsable(row.Rhs)) {
                return Fail($"the indicator row `{row.Name}` has unusable numeric values");
            }
        }

        return new Ok<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>(rows);
    }

    /// <summary>
    /// 非零检测的四条行，**极性与 <see cref="Build"/> 相反** /
    /// The four nonzero-detection rows, with the <b>opposite polarity</b> to <see cref="Build"/>.
    /// </summary>
    /// <remarks>
    /// **同一个看起来一样的 `indicator` 参数，在两个函数里含义相反：**
    ///
    /// | 函数 | 指示 = 1 表示 |
    /// | --- | --- |
    /// | <see cref="Build"/>（零值检测） | **落在带内** |
    /// | 本函数（非零检测） | **落在带外** |
    ///
    /// **两套行因此把"紧"的那一侧对调：**
    ///
    /// | 行 | 内容 | 何时紧 |
    /// | --- | --- | --- |
    /// | `_band_ub` | `poly − bandM·ind ≤ tolerance` | **指示 = 0** 时 `poly ≤ tolerance` |
    /// | `_band_lb` | `poly + bandM·ind ≥ −tolerance` | **指示 = 0** 时 `poly ≥ −tolerance` |
    /// | `_out_lb` | `poly − outM·ind − outM·side ≥ strict − 2·outM` | **指示 = 1 且 `side = 1`** 时 `poly ≥ strict` |
    /// | `_out_ub` | `poly + outM·ind − outM·side ≤ −strict + outM` | **指示 = 1 且 `side = 0`** 时 `poly ≤ −strict` |
    ///
    /// **注意 `_out_lb` 的右侧是 `strict − 2·outM`，不是 `strict − outM`**：
    /// **因为那一行同时带了 `ind` 与 `side` 两个 −`outM` 项，
    /// 指示 = 1、`side = 1` 时两者相加正好抵消，于是落到 `strict`。**
    /// **少一个 `−outM` 会让紧点偏移整个 `outM`——而模型仍然可行。**
    ///
    /// **The same-looking `indicator` parameter means the opposite in the two functions:**
    ///
    /// | Function | indicator = 1 means |
    /// | --- | --- |
    /// | <see cref="Build"/> (zero detection) | **inside the band** |
    /// | this one (nonzero detection) | **outside the band** |
    ///
    /// **So the two row sets swap which side binds.**
    ///
    /// **Note `_out_lb`'s right-hand side is `strict − 2·outM`, not `strict − outM`**: **that row carries two `−outM`
    /// terms (`ind` and `side`), and at indicator 1 with `side = 1` they cancel exactly onto `strict`.** **Dropping one
    /// `−outM` shifts the tight point by a whole `outM` — while the model stays feasible.**
    /// </remarks>
    public static Result<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>
        BuildNonzero(
            LinearPolynomial<Flt64> poly,
            IVariableItem indicator,
            IVariableItem sideVar,
            Flt64 bigM,
            Flt64 tolerance,
            Flt64 strictBoundary,
            string namePrefix) {
        Result<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>> checkedValues =
            Build(poly, indicator, sideVar, bigM, tolerance, strictBoundary, namePrefix);
        if (checkedValues is Failed<
                IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>> failure) {
            return new Failed<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>(
                failure.Error);
        }

        Flt64 bandM = PositiveIndicatorRows.RelaxBigM(bigM, tolerance);
        Flt64 outM = PositiveIndicatorRows.RelaxBigM(bigM, strictBoundary);

        var polyMonomials = new List<LinearMonomial<Flt64>>(poly.Monomials.Count);
        foreach (LinearMonomial<Flt64> monomial in poly.Monomials) {
            polyMonomials.Add(new LinearMonomial<Flt64>(monomial.Coefficient, monomial.Symbol));
        }

        var rows = new List<LinearInequality<Flt64>>(4) {
            new(
                Lhs: new LinearPolynomial<Flt64>(
                    Append(polyMonomials, new LinearMonomial<Flt64>(bandM.Negate(), indicator)),
                    poly.Constant),
                Rhs: new LinearPolynomial<Flt64>(
                    Array.Empty<LinearMonomial<Flt64>>(), tolerance),
                Comparison: Comparison.LE,
                Name: $"{namePrefix}_band_ub"),
            new(
                Lhs: new LinearPolynomial<Flt64>(
                    Append(polyMonomials, new LinearMonomial<Flt64>(bandM, indicator)),
                    poly.Constant),
                Rhs: new LinearPolynomial<Flt64>(
                    Array.Empty<LinearMonomial<Flt64>>(), tolerance.Negate()),
                Comparison: Comparison.GE,
                Name: $"{namePrefix}_band_lb"),
            new(
                Lhs: new LinearPolynomial<Flt64>(
                    Append2(
                        polyMonomials,
                        new LinearMonomial<Flt64>(outM.Negate(), indicator),
                        new LinearMonomial<Flt64>(outM.Negate(), sideVar)),
                    poly.Constant),
                Rhs: new LinearPolynomial<Flt64>(
                    Array.Empty<LinearMonomial<Flt64>>(),
                    strictBoundary.Minus(outM).Minus(outM)),
                Comparison: Comparison.GE,
                Name: $"{namePrefix}_out_lb"),
            new(
                Lhs: new LinearPolynomial<Flt64>(
                    Append2(
                        polyMonomials,
                        new LinearMonomial<Flt64>(outM, indicator),
                        new LinearMonomial<Flt64>(outM.Negate(), sideVar)),
                    poly.Constant),
                Rhs: new LinearPolynomial<Flt64>(
                    Array.Empty<LinearMonomial<Flt64>>(), strictBoundary.Negate().Plus(outM)),
                Comparison: Comparison.LE,
                Name: $"{namePrefix}_out_ub"),
        };

        foreach (LinearInequality<Flt64> row in rows) {
            if (!ConditionPolynomials.IsUsable(row.Lhs) || !ConditionPolynomials.IsUsable(row.Rhs)) {
                return Fail($"the indicator row `{row.Name}` has unusable numeric values");
            }
        }

        return new Ok<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>(rows);
    }

    private static List<LinearMonomial<Flt64>> Append(
        List<LinearMonomial<Flt64>> monomials, LinearMonomial<Flt64> extra) {
        var copy = new List<LinearMonomial<Flt64>>(monomials.Count + 1);
        copy.AddRange(monomials);
        copy.Add(extra);
        return copy;
    }

    private static List<LinearMonomial<Flt64>> Append2(
        List<LinearMonomial<Flt64>> monomials,
        LinearMonomial<Flt64> first,
        LinearMonomial<Flt64> second) {
        var copy = new List<LinearMonomial<Flt64>>(monomials.Count + 2);
        copy.AddRange(monomials);
        copy.Add(first);
        copy.Add(second);
        return copy;
    }

    private static Result<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>
        Fail(string message) =>
            new Failed<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument, message);
}
