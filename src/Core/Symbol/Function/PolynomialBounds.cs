#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Variable;

namespace Fuookami.Ospf.Core.Symbol.Function;

/// <summary>
/// 从一个线性多项式推出区间 / Derive an interval from a linear polynomial.
/// </summary>
/// <remarks>
/// **这段逻辑原本在 `SelectFunction` 与 `ElementFunction` 里各有一份，
/// 而 `ComplementarityFunction` 需要第三份**——**第三份出现时才抽出来，而不是第二份。**
///
/// **推不出来就返回 `null`，不猜。** **猜一个界会让约束"构造成功"而夹在错误的区间上，
/// 于是结果悄悄偏移，而没有任何一步报错。**
///
/// **返回 `null` 的三种情况**：**某个 monomial 的符号不是 `RealVar`**（它不是变量，没有区间）、
/// **它的某个界缺失**、**某个界的值不可用**。
///
/// **This logic was duplicated in `SelectFunction` and `ElementFunction`, and
/// `ComplementarityFunction` needed a third copy** — **it is extracted when the third appears, not the second.**
///
/// **Returns `null` when it cannot be derived, never a guess.** **A guessed bound lets constraints "build fine" while
/// bracketing the wrong interval, so the result shifts with nothing reporting an error.**
///
/// **Three cases return `null`**: **a monomial's symbol is not a `RealVar`** (no interval),
/// **one of its bounds is missing**, **or a bound's value is unusable**.
/// </remarks>
internal static class PolynomialBounds
{
    /// <summary>推出一个多项式的有限区间 / Derive a finite interval.</summary>
    public static ConditionBounds<V>? FiniteBoundsOf<V>(
        LinearPolynomial<V> polynomial, IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> {
        V lower = polynomial.Constant;
        V upper = polynomial.Constant;

        foreach (LinearMonomial<V> monomial in polynomial.Monomials) {
            if (monomial.Symbol is not RealVar variable) {
                return null;
            }

            if (variable.Range.LowerBound is not { } lowerSlot ||
                variable.Range.UpperBound is not { } upperSlot) {
                return null;
            }

            V lowEnd = converter.IntoValue(lowerSlot.Value.Unwrap());
            V highEnd = converter.IntoValue(upperSlot.Value.Unwrap());
            if (!FunctionEvaluation.IsUsable(lowEnd) || !FunctionEvaluation.IsUsable(highEnd)) {
                return null;
            }

            // 系数为负时两端互换：monomial 的界是 min(c*lo, c*hi) .. max(c*lo, c*hi)。
            // A negative coefficient swaps the ends: a monomial ranges over min(c*lo, c*hi) .. max(...).
            V endA = monomial.Coefficient.Times(lowEnd);
            V endB = monomial.Coefficient.Times(highEnd);
            V monomialLower = endA.CompareTo(endB) <= 0 ? endA : endB;
            V monomialUpper = endA.CompareTo(endB) <= 0 ? endB : endA;

            lower = lower.Plus(monomialLower);
            upper = upper.Plus(monomialUpper);
        }

        return new ConditionBounds<V>(lower, upper);
    }
}
