#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Math.Symbol.Operation;

/// <summary>
/// 可变多项式合并运算 / Mutable Polynomial Combine Operations.
/// 提供可变多项式的原地同类项合并操作。
/// 支持快速累积模式：先使用 += 累积，最后一次性合并。
/// Kotlin MutableCombineOps.kt 的对应实现。
/// Provides in-place like-term combination for mutable polynomials.
/// Supports FastSum pattern: accumulate with Add(), then combine once at the end.
/// </summary>
public static class MutableCombineOps {
    // ===== MutableLinearPolynomial =====

    /// <summary>
    /// 原地合并可变线性多项式中的同类项 / Combine like terms in mutable linear polynomial in-place.
    /// FastSum 模式：先用 Add 累积，最后一次性合并同类项。
    /// </summary>
    /// <param name="p">可变线性多项式 / Mutable linear polynomial.</param>
    public static void CombineTerms<T>(this MutableLinearPolynomial<T> p)
        where T : struct, INumberField<T>, IRealNumber<T> {
        INumericConstants<T> constants = NumericConstantsRegistry.For<T>();
        T zero = constants.Zero;
        var grouped = new Dictionary<ISymbol, T>(p._monomials.Count);
        foreach (LinearMonomial<T> m in p._monomials) {
            if (grouped.TryGetValue(m.Symbol, out T existing)) {
                grouped[m.Symbol] = existing.Plus(m.Coefficient);
            }
            else {
                grouped[m.Symbol] = m.Coefficient;
            }
        }

        p._monomials.Clear();
        foreach ((ISymbol? symbol, T coeff) in grouped) {
            if (!coeff.Eq(zero)) {
                p._monomials.Add(new LinearMonomial<T>(coeff, symbol));
            }
        }
    }

    /// <summary>
    /// 累加多项式并合并同类项（一步操作）/ Add polynomial and combine terms in one operation.
    /// </summary>
    /// <param name="p">可变线性多项式 / Mutable linear polynomial.</param>
    /// <param name="rhs">要累加的多项式 / Polynomial to add.</param>
    public static void AddAndCombine<T>(this MutableLinearPolynomial<T> p, LinearPolynomial<T> rhs)
        where T : struct, INumberField<T>, IRealNumber<T> {
        p._monomials.AddRange(rhs.Monomials);
        p._constant = p._constant.Plus(rhs.Constant);
        p.CombineTerms();
    }

    /// <summary>
    /// 减去多项式并合并同类项（一步操作）/ Subtract polynomial and combine terms in one operation.
    /// </summary>
    /// <param name="p">可变线性多项式 / Mutable linear polynomial.</param>
    /// <param name="rhs">要减去的多项式 / Polynomial to subtract.</param>
    public static void SubtractAndCombine<T>(this MutableLinearPolynomial<T> p, LinearPolynomial<T> rhs)
        where T : struct, INumberField<T>, IRealNumber<T> {
        p._monomials.AddRange(rhs.Monomials.Select(m => -m));
        p._constant = p._constant.Minus(rhs.Constant);
        p.CombineTerms();
    }

    // ===== MutableQuadraticPolynomial =====

    /// <summary>
    /// 原地合并可变二次多项式中的同类项 / Combine like terms in mutable quadratic polynomial in-place.
    /// </summary>
    /// <param name="p">可变二次多项式 / Mutable quadratic polynomial.</param>
    public static void CombineTerms<T>(this MutableQuadraticPolynomial<T> p)
        where T : struct, INumberField<T>, IRealNumber<T> {
        INumericConstants<T> constants = NumericConstantsRegistry.For<T>();
        T zero = constants.Zero;
        var grouped = new Dictionary<(ISymbol, ISymbol?), T>();
        foreach (QuadraticMonomial<T> m in p._monomials) {
            (ISymbol, ISymbol?) key = (m.Symbol1, m.Symbol2);
            if (grouped.TryGetValue(key, out T existing)) {
                grouped[key] = existing.Plus(m.Coefficient);
            }
            else {
                grouped[key] = m.Coefficient;
            }
        }

        p._monomials.Clear();
        foreach (((ISymbol s1, ISymbol? s2) key, T coeff) in grouped) {
            if (!coeff.Eq(zero)) {
                p._monomials.Add(new QuadraticMonomial<T>(coeff, key.s1, key.s2));
            }
        }
    }

    /// <summary>
    /// 累加二次多项式并合并同类项 / Add quadratic polynomial and combine terms.
    /// </summary>
    /// <param name="p">可变二次多项式 / Mutable quadratic polynomial.</param>
    /// <param name="rhs">要累加的二次多项式 / Quadratic polynomial to add.</param>
    public static void AddAndCombine<T>(this MutableQuadraticPolynomial<T> p, QuadraticPolynomial<T> rhs)
        where T : struct, INumberField<T>, IRealNumber<T> {
        p._monomials.AddRange(rhs.Monomials);
        p._constant = p._constant.Plus(rhs.Constant);
        p.CombineTerms();
    }

    /// <summary>
    /// 减去二次多项式并合并同类项 / Subtract quadratic polynomial and combine terms.
    /// </summary>
    /// <param name="p">可变二次多项式 / Mutable quadratic polynomial.</param>
    /// <param name="rhs">要减去的二次多项式 / Quadratic polynomial to subtract.</param>
    public static void SubtractAndCombine<T>(this MutableQuadraticPolynomial<T> p, QuadraticPolynomial<T> rhs)
        where T : struct, INumberField<T>, IRealNumber<T> {
        p._monomials.AddRange(rhs.Monomials.Select(m => -m));
        p._constant = p._constant.Minus(rhs.Constant);
        p.CombineTerms();
    }

    // ===== MutableCanonicalPolynomial =====

    /// <summary>
    /// 原地合并可变规范多项式中的同类项 / Combine like terms in mutable canonical polynomial in-place.
    /// 使用 PowerVectorKey 进行高效合并。
    /// </summary>
    /// <param name="p">可变规范多项式 / Mutable canonical polynomial.</param>
    public static void CombineTerms<T>(this MutableCanonicalPolynomial<T> p)
        where T : struct, INumberField<T>, IRealNumber<T> {
        List<CanonicalMonomial<T>> combined = p._monomials.CombineCanonicalMonomials();
        p._monomials.Clear();
        p._monomials.AddRange(combined);
    }

    /// <summary>
    /// 累加规范多项式并合并同类项 / Add canonical polynomial and combine terms.
    /// </summary>
    /// <param name="p">可变规范多项式 / Mutable canonical polynomial.</param>
    /// <param name="rhs">要累加的规范多项式 / Canonical polynomial to add.</param>
    public static void AddAndCombine<T>(this MutableCanonicalPolynomial<T> p, CanonicalPolynomial<T> rhs)
        where T : struct, INumberField<T>, IRealNumber<T> {
        p._monomials.AddRange(rhs.Monomials);
        p._constant = p._constant.Plus(rhs.Constant);
        p.CombineTerms();
    }

    /// <summary>
    /// 减去规范多项式并合并同类项 / Subtract canonical polynomial and combine terms.
    /// </summary>
    /// <param name="p">可变规范多项式 / Mutable canonical polynomial.</param>
    /// <param name="rhs">要减去的规范多项式 / Canonical polynomial to subtract.</param>
    public static void SubtractAndCombine<T>(this MutableCanonicalPolynomial<T> p, CanonicalPolynomial<T> rhs)
        where T : struct, INumberField<T>, IRealNumber<T> {
        p._monomials.AddRange(rhs.Monomials.Select(m => -m));
        p._constant = p._constant.Minus(rhs.Constant);
        p.CombineTerms();
    }
}
