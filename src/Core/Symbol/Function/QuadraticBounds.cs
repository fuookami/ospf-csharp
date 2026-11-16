#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Variable;

namespace Fuookami.Ospf.Core.Symbol.Function;

/// <summary>
/// 二次多项式的区间与求值 / A quadratic polynomial's interval and evaluation.
/// </summary>
/// <remarks>
/// **`QuadraticMinFunction` 与 `QuadraticMaskingRangeFunction` 都需要这两件事，
/// 而 `FunctionEvaluation` 只处理线性多项式。**
///
/// **区间推导对双线性项是精确的（极值在角的四个顶点上），
/// 对一般二次项只是保守估计。** **保守对大 M 是安全的（偏大只让约束变松，不会误约束），
/// 但偏大也会让松弛变差——这是已知取舍。**
///
/// **Both `QuadraticMinFunction` and `QuadraticMaskingRangeFunction` need these two things, and
/// `FunctionEvaluation` only handles linear polynomials.**
///
/// **The interval derivation is exact for bilinear terms (the extremum is at one of the four corners) and only a
/// conservative estimate for a general quadratic term.** **Conservative is safe for a big-M (too large only relaxes a
/// constraint), but too large also weakens the relaxation — a known trade-off.**
/// </remarks>
internal static class QuadraticBounds
{
    /// <summary>推出一个二次多项式的区间 / Derive a quadratic polynomial's interval.</summary>
    /// <remarks>
    /// **任何一项的符号不是 `RealVar`、或某个变量缺界，就整体返回 `null`**——
    /// **用"能推出来的那几项"会让大 M 偏小，于是某些约束失效，
    /// 而那表现为"求解成功但结果不对"。**
    ///
    /// **One symbol that is not a `RealVar`, or one variable without bounds, makes the whole thing `null`** —
    /// **using only the terms that do work makes the big-M too small, so some rows stop binding, which shows up as
    /// "solved fine, wrong answer".**
    /// </remarks>
    public static ConditionBounds<Flt64>? Of(
        QuadraticPolynomial<Flt64> polynomial, IIntoValue<Flt64> converter) {
        Flt64 lower = polynomial.Constant;
        Flt64 upper = polynomial.Constant;

        foreach (QuadraticMonomial<Flt64> monomial in polynomial.Monomials) {
            if (monomial.Symbol1 is not RealVar first || RangeOf(first, converter) is not { } firstRange) {
                return null;
            }

            if (monomial.Symbol2 is null) {
                Flt64 endA = monomial.Coefficient.Times(firstRange.Lower);
                Flt64 endB = monomial.Coefficient.Times(firstRange.Upper);
                lower = lower.Plus(endA.CompareTo(endB) <= 0 ? endA : endB);
                upper = upper.Plus(endA.CompareTo(endB) <= 0 ? endB : endA);
                continue;
            }

            if (monomial.Symbol2 is not RealVar second || RangeOf(second, converter) is not { } secondRange) {
                return null;
            }

            // 四角取最值 / The four corners.
            Flt64[] corners = {
                monomial.Coefficient.Times(firstRange.Lower).Times(secondRange.Lower),
                monomial.Coefficient.Times(firstRange.Lower).Times(secondRange.Upper),
                monomial.Coefficient.Times(firstRange.Upper).Times(secondRange.Lower),
                monomial.Coefficient.Times(firstRange.Upper).Times(secondRange.Upper),
            };

            Flt64 cornerMin = corners[0];
            Flt64 cornerMax = corners[0];
            for (int index = 1; index < corners.Length; ++index) {
                if (corners[index].CompareTo(cornerMin) < 0) {
                    cornerMin = corners[index];
                }

                if (corners[index].CompareTo(cornerMax) > 0) {
                    cornerMax = corners[index];
                }
            }

            lower = lower.Plus(cornerMin);
            upper = upper.Plus(cornerMax);
        }

        return new ConditionBounds<Flt64>(lower, upper);
    }

    /// <summary>求一个二次多项式在给定点上的值 / Evaluate a quadratic polynomial at a point.</summary>
    public static Flt64? Evaluate(
        QuadraticPolynomial<Flt64> polynomial, IReadOnlyDictionary<ISymbol, Flt64> values) {
        Flt64 total = polynomial.Constant;
        foreach (QuadraticMonomial<Flt64> monomial in polynomial.Monomials) {
            if (!values.TryGetValue(monomial.Symbol1, out Flt64 first)) {
                return null;
            }

            if (monomial.Symbol2 is { } secondSymbol) {
                if (!values.TryGetValue(secondSymbol, out Flt64 second)) {
                    return null;
                }

                total = total.Plus(monomial.Coefficient.Times(first).Times(second));
            } else {
                total = total.Plus(monomial.Coefficient.Times(first));
            }
        }

        return total;
    }

    private static (Flt64 Lower, Flt64 Upper)? RangeOf(RealVar variable, IIntoValue<Flt64> converter) {
        if (variable.Range.LowerBound is not { } lowerSlot ||
            variable.Range.UpperBound is not { } upperSlot) {
            return null;
        }

        Flt64 lower = converter.IntoValue(lowerSlot.Value.Unwrap());
        Flt64 upper = converter.IntoValue(upperSlot.Value.Unwrap());
        return FunctionEvaluation.IsUsable(lower) && FunctionEvaluation.IsUsable(upper)
            ? (lower, upper)
            : null;
    }
}
