#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using System.Collections.Generic;

namespace Fuookami.Ospf.Math.Symbol.Operation;

/// <summary>
/// Flt64 同类项合并快捷函数 / Flt64 Combine Terms Convenience Functions.
/// 提供 Flt64 多项式的同类项合并快捷函数，封装通用合并运算。
/// Kotlin CombineTerms.kt 的对应实现。
/// Provides Flt64 polynomial like-term combination convenience functions.
/// </summary>
public static class Flt64CombineTerms {
    // ===== LinearPolynomial<Flt64> =====

    /// <summary>
    /// 合并 Flt64 线性多项式中的同类项 / Combine like terms in Flt64 linear polynomial.
    /// </summary>
    /// <param name="p">Flt64 线性多项式 / Flt64 linear polynomial.</param>
    /// <returns>合并后的多项式 / Combined polynomial.</returns>
    public static LinearPolynomial<Flt64> CombineTermsLinear(this LinearPolynomial<Flt64> p)
        => LinearQuadraticOps.CombineLinearTerms(p);

    // ===== QuadraticPolynomial<Flt64> =====

    /// <summary>
    /// 合并 Flt64 二次多项式中的同类项 / Combine like terms in Flt64 quadratic polynomial.
    /// </summary>
    /// <param name="p">Flt64 二次多项式 / Flt64 quadratic polynomial.</param>
    /// <returns>合并后的多项式 / Combined polynomial.</returns>
    public static QuadraticPolynomial<Flt64> CombineTermsQuadratic(this QuadraticPolynomial<Flt64> p)
        => LinearQuadraticOps.CombineQuadraticTerms(p);

    // ===== CanonicalPolynomial<Flt64> =====

    /// <summary>
    /// 合并 Flt64 规范多项式中的同类项 / Combine like terms in Flt64 canonical polynomial.
    /// </summary>
    /// <param name="p">Flt64 规范多项式 / Flt64 canonical polynomial.</param>
    /// <returns>合并后的多项式 / Combined polynomial.</returns>
    public static CanonicalPolynomial<Flt64> CombineTermsCanonical(this CanonicalPolynomial<Flt64> p)
        => CanonicalOps.CombineCanonicalTerms(p);

    // ===== Iterable extensions =====

    /// <summary>
    /// 合并 Flt64 线性单项式集合中的同类项 / Combine like terms in Flt64 linear monomials.
    /// </summary>
    /// <param name="monomials">Flt64 线性单项式集合 / Flt64 linear monomial collection.</param>
    /// <returns>合并后的单项式列表 / Combined monomial list.</returns>
    public static List<LinearMonomial<Flt64>> CombineTerms(this IEnumerable<LinearMonomial<Flt64>> monomials) {
        var grouped = new Dictionary<ISymbol, Flt64>();
        foreach (LinearMonomial<Flt64> m in monomials) {
            if (grouped.TryGetValue(m.Symbol, out Flt64 existing)) {
                grouped[m.Symbol] = existing.Plus(m.Coefficient);
            }
            else {
                grouped[m.Symbol] = m.Coefficient;
            }
        }

        var result = new List<LinearMonomial<Flt64>>();
        foreach ((ISymbol? symbol, Flt64 coeff) in grouped) {
            if (!coeff.Eq(Flt64.Zero)) {
                result.Add(new LinearMonomial<Flt64>(coeff, symbol));
            }
        }
        return result;
    }

    /// <summary>
    /// 合并 Flt64 二次单项式集合中的同类项 / Combine like terms in Flt64 quadratic monomials.
    /// </summary>
    /// <param name="monomials">Flt64 二次单项式集合 / Flt64 quadratic monomial collection.</param>
    /// <returns>合并后的单项式列表 / Combined monomial list.</returns>
    public static List<QuadraticMonomial<Flt64>> CombineTerms(this IEnumerable<QuadraticMonomial<Flt64>> monomials) {
        var grouped = new Dictionary<(ISymbol, ISymbol?), Flt64>();
        foreach (QuadraticMonomial<Flt64> m in monomials) {
            (ISymbol, ISymbol?) key = (m.Symbol1, m.Symbol2);
            if (grouped.TryGetValue(key, out Flt64 existing)) {
                grouped[key] = existing.Plus(m.Coefficient);
            }
            else {
                grouped[key] = m.Coefficient;
            }
        }

        var result = new List<QuadraticMonomial<Flt64>>();
        foreach (((ISymbol s1, ISymbol? s2) key, Flt64 coeff) in grouped) {
            if (!coeff.Eq(Flt64.Zero)) {
                result.Add(new QuadraticMonomial<Flt64>(coeff, key.s1, key.s2));
            }
        }
        return result;
    }

    /// <summary>
    /// 合并 Flt64 规范单项式集合中的同类项 / Combine like terms in Flt64 canonical monomials.
    /// </summary>
    /// <param name="monomials">Flt64 规范单项式集合 / Flt64 canonical monomial collection.</param>
    /// <returns>合并后的单项式列表 / Combined monomial list.</returns>
    public static List<CanonicalMonomial<Flt64>> CombineCanonicalTerms(this IEnumerable<CanonicalMonomial<Flt64>> monomials)
        => CanonicalOps.CombineCanonicalMonomials(monomials);
}
