#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Math.Symbol.Operation;

/// <summary>
/// 单项式和多项式转换运算（高级）/ Monomial and Polynomial Conversion Operations (Advanced).
/// 提供单项式级别的升阶/降阶转换和多项式减法运算。
/// Kotlin ConvertOps.kt 的对应实现。
/// Provides monomial-level promotion/demotion conversions and polynomial subtraction operations.
/// </summary>
public static class MonomialConvertOps {
    // ===== LinearMonomial promotion (distinct names to avoid ConvertOps ambiguity) =====

    /// <summary>线性单项式转二次单项式 / Convert linear monomial to quadratic monomial.</summary>
    public static QuadraticMonomial<T> ToQuadraticMonomialFromLinear<T>(this LinearMonomial<T> m)
        where T : struct, IRing<T>
        => QuadraticMonomial<T>.Linear(m.Coefficient, m.Symbol);

    /// <summary>线性单项式转规范单项式 / Convert linear monomial to canonical monomial.</summary>
    public static CanonicalMonomial<T> ToCanonicalMonomialFromLinear<T>(this LinearMonomial<T> m)
        where T : struct, IRing<T>
        => new(m.Coefficient, new Dictionary<ISymbol, int> { [m.Symbol] = 1 });

    // ===== QuadraticMonomial demotion =====

    /// <summary>
    /// 尝试将二次单项式降阶为线性单项式 / Try to demote quadratic monomial to linear.
    /// 返回 null 而非 Result（Kotlin toLinearMonomialOrNull 对应）。
    /// </summary>
    /// <returns>线性单项式，若为真正的二次项则返回 null / Linear monomial, or null if truly quadratic.</returns>
    public static LinearMonomial<T>? ToLinearMonomialOrNull<T>(this QuadraticMonomial<T> m)
        where T : struct, IRing<T> {
        if (m.IsQuadratic) {
            return null;
        }

        return new LinearMonomial<T>(m.Coefficient, m.Symbol1);
    }

    /// <summary>二次单项式转规范单项式 / Convert quadratic monomial to canonical monomial.</summary>
    public static CanonicalMonomial<T> ToCanonicalMonomialFromQuadratic<T>(this QuadraticMonomial<T> m)
        where T : struct, IRing<T> {
        var powers = new Dictionary<ISymbol, int>();
        powers[m.Symbol1] = powers.TryGetValue(m.Symbol1, out int p1) ? p1 + 1 : 1;
        if (m.Symbol2 is not null) {
            powers[m.Symbol2] = powers.TryGetValue(m.Symbol2, out int p2) ? p2 + 1 : 1;
        }

        return new CanonicalMonomial<T>(m.Coefficient, powers);
    }

    // ===== CanonicalMonomial demotion =====

    /// <summary>
    /// 尝试将规范单项式降阶为线性单项式 / Try to demote canonical monomial to linear.
    /// 返回 null 而非 Result（Kotlin toLinearMonomialOrNull 对应）。
    /// </summary>
    /// <returns>线性单项式，若次数不为 1 则返回 null / Linear monomial, or null if degree is not 1.</returns>
    public static LinearMonomial<T>? ToLinearMonomialOrNull<T>(this CanonicalMonomial<T> m)
        where T : struct, IRing<T> {
        if (m.Degree != 1) {
            return null;
        }

        KeyValuePair<ISymbol, int> entry = System.Linq.Enumerable.FirstOrDefault(
            m.Powers, kv => kv.Value == 1);
        if (entry.Key is null) {
            return null;
        }

        return new LinearMonomial<T>(m.Coefficient, entry.Key);
    }

    /// <summary>
    /// 尝试将规范单项式降阶为二次单项式 / Try to demote canonical monomial to quadratic.
    /// 返回 null 而非 Result（Kotlin toQuadraticMonomialOrNull 对应）。
    /// </summary>
    /// <returns>二次单项式，若次数超过 2 则返回 null / Quadratic monomial, or null if degree exceeds 2.</returns>
    public static QuadraticMonomial<T>? ToQuadraticMonomialOrNull<T>(this CanonicalMonomial<T> m)
        where T : struct, IRing<T> {
        if (m.Degree == 1) {
            KeyValuePair<ISymbol, int> entry = System.Linq.Enumerable.FirstOrDefault(m.Powers, kv => kv.Value == 1);
            if (entry.Key is null) {
                return null;
            }

            return QuadraticMonomial<T>.Linear(m.Coefficient, entry.Key);
        }

        if (m.Degree != 2) {
            return null;
        }

        if (m.Powers.Count == 1 && System.Linq.Enumerable.First(m.Powers).Value == 2) {
            ISymbol symbol = System.Linq.Enumerable.First(m.Powers).Key;
            return QuadraticMonomial<T>.Quadratic(m.Coefficient, symbol, symbol);
        }

        if (m.Powers.Count == 2 && System.Linq.Enumerable.All(m.Powers, kv => kv.Value == 1)) {
            var symbols = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Select(m.Powers, kv => kv.Key));
            return QuadraticMonomial<T>.Quadratic(m.Coefficient, symbols[0], symbols[1]);
        }

        return null;
    }

    // ===== Polynomial subtraction =====

    /// <summary>
    /// 两个线性多项式相减 / Subtract two linear polynomials.
    /// </summary>
    /// <param name="lhs">被减数 / Minuend.</param>
    /// <param name="rhs">减数 / Subtrahend.</param>
    /// <returns>相减后的线性多项式 / Resulting linear polynomial.</returns>
    public static LinearPolynomial<T> SubtractLinear<T>(
        this LinearPolynomial<T> lhs,
        LinearPolynomial<T> rhs)
        where T : struct, IRing<T>, IRealNumber<T> {
        INumericConstants<T> constants = NumericConstantsRegistry.For<T>();
        T zero = constants.Zero;
        var resultMonomials = new List<LinearMonomial<T>>(lhs.Monomials.Count + rhs.Monomials.Count);
        resultMonomials.AddRange(lhs.Monomials);
        resultMonomials.AddRange(rhs.Monomials.Select(m => new LinearMonomial<T>(m.Coefficient.Negate(), m.Symbol)));
        return new LinearPolynomial<T>(resultMonomials, lhs.Constant.Minus(rhs.Constant)).CombineLinearTerms();
    }

    /// <summary>
    /// 两个规范多项式相减 / Subtract two canonical polynomials.
    /// </summary>
    /// <param name="lhs">被减数 / Minuend.</param>
    /// <param name="rhs">减数 / Subtrahend.</param>
    /// <returns>相减后的规范多项式 / Resulting canonical polynomial.</returns>
    public static CanonicalPolynomial<T> SubtractCanonical<T>(
        this CanonicalPolynomial<T> lhs,
        CanonicalPolynomial<T> rhs)
        where T : struct, IRing<T>, IRealNumber<T> {
        var resultMonomials = new List<CanonicalMonomial<T>>(lhs.Monomials.Count + rhs.Monomials.Count);
        resultMonomials.AddRange(lhs.Monomials);
        resultMonomials.AddRange(rhs.Monomials.Select(m => new CanonicalMonomial<T>(m.Coefficient.Negate(), m.Powers)));
        return new CanonicalPolynomial<T>(resultMonomials, lhs.Constant.Minus(rhs.Constant)).CombineCanonicalTerms();
    }
}
