#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Math.Symbol.Operation;

/// <summary>
/// 规范多项式核心运算 / Canonical Polynomial Core Operations.
/// 提供规范多项式的同类项合并、求值、有序求值和部分求值。
/// 使用 PowerVectorKey 进行高效的幂向量比较。
/// Provides combining like terms, evaluation, ordered evaluation,
/// and partial evaluation for canonical polynomials using PowerVectorKey.
/// </summary>
public static class CanonicalOps {
    // ===== Combine Like Terms =====

    /// <summary>
    /// 合并规范单项式集合中的同类项 / Combine like terms in canonical monomials.
    /// 使用 PowerVectorKey 进行高效分组。
    /// </summary>
    /// <param name="monomials">规范单项式集合 / Canonical monomial collection.</param>
    /// <returns>合并后的单项式列表 / Combined monomial list.</returns>
    public static List<CanonicalMonomial<T>> CombineCanonicalMonomials<T>(
        this IEnumerable<CanonicalMonomial<T>> monomials)
        where T : struct, IRing<T>, IRealNumber<T> {
        INumericConstants<T> constants = NumericConstantsRegistry.For<T>();
        T zero = constants.Zero;

        // Step 1: Collect all unique symbols and build index mapping
        var symbolIndex = new Dictionary<ISymbol, int>();
        var symbolList = new List<ISymbol>();
        foreach (CanonicalMonomial<T> m in monomials) {
            foreach (ISymbol s in m.Powers.Keys) {
                if (!symbolIndex.ContainsKey(s)) {
                    symbolIndex[s] = symbolList.Count;
                    symbolList.Add(s);
                }
            }
        }

        // Step 2: Combine using PowerVectorKey
        var grouped = new Dictionary<PowerVectorKey, (T Coefficient, IReadOnlyDictionary<ISymbol, int> Powers)>();
        foreach (CanonicalMonomial<T> m in monomials) {
            PowerVectorKey key = PowerVectorKey.Create(
                (IReadOnlyDictionary<ISymbol, int>)m.Powers,
                (IReadOnlyDictionary<ISymbol, int>)symbolIndex,
                symbolList.Count);

            if (grouped.TryGetValue(key, out (T Coefficient, IReadOnlyDictionary<ISymbol, int> Powers) existing)) {
                grouped[key] = (existing.Coefficient.Plus(m.Coefficient), existing.Powers);
            }
            else {
                grouped[key] = (m.Coefficient, m.Powers);
            }
        }

        // Step 3: Convert back to list
        return grouped.Values
            .Where(kv => !kv.Coefficient.Eq(zero))
            .Select(kv => new CanonicalMonomial<T>(kv.Coefficient, kv.Powers))
            .ToList();
    }

    /// <summary>
    /// 合并规范多项式中的同类项 / Combine like terms in a canonical polynomial.
    /// </summary>
    /// <param name="p">规范多项式 / Canonical polynomial.</param>
    /// <returns>合并同类项后的规范多项式 / Canonical polynomial with like terms combined.</returns>
    public static CanonicalPolynomial<T> CombineCanonicalTerms<T>(this CanonicalPolynomial<T> p)
        where T : struct, IRing<T>, IRealNumber<T> {
        List<CanonicalMonomial<T>> combined = p.Monomials.CombineCanonicalMonomials();
        return new CanonicalPolynomial<T>(combined, p.Constant);
    }

    // ===== Evaluate =====

    /// <summary>
    /// 使用给定值对规范多项式求值 / Evaluate a canonical polynomial with given values.
    /// </summary>
    /// <param name="p">规范多项式 / Canonical polynomial.</param>
    /// <param name="values">符号到值的映射 / Symbol-to-value mapping.</param>
    /// <param name="onMissing">缺失符号的回调（可选）/ Callback for missing symbols (optional).</param>
    /// <returns>求值结果，若存在未提供值的符号则返回 null / Evaluation result, or null if a symbol has no value.</returns>
    public static T? EvaluateCanonical<T>(
        this CanonicalPolynomial<T> p,
        IReadOnlyDictionary<ISymbol, T> values,
        Func<ISymbol, T?>? onMissing = null)
        where T : struct, IRing<T>, IRealNumber<T> {
        INumericConstants<T> constants = NumericConstantsRegistry.For<T>();
        T one = constants.One;
        T result = p.Constant;
        foreach (CanonicalMonomial<T> m in p.Monomials) {
            T term = m.Coefficient;
            bool allFound = true;
            foreach ((ISymbol? symbol, int power) in m.Powers) {
                T? factor = values.TryGetValue(symbol, out T v) ? v : onMissing?.Invoke(symbol);
                if (factor is null) {
                    allFound = false;
                    break;
                }

                T powerValue = ComputeNonNegativeRingPower(factor.Value, power, one);
                term = term.Times(powerValue);
            }
            if (allFound) {
                result = result.Plus(term);
            }
        }
        return result;
    }

    /// <summary>
    /// 使用有序符号和值对规范多项式求值 / Evaluate a canonical polynomial with ordered symbols and values.
    /// </summary>
    /// <param name="p">规范多项式 / Canonical polynomial.</param>
    /// <param name="order">符号顺序列表 / Ordered list of symbols.</param>
    /// <param name="values">与符号顺序对应的值列表 / Values corresponding to symbol order.</param>
    /// <returns>求值结果 / Evaluation result.</returns>
    public static Result<T, ErrorCode, Error<ErrorCode>> EvaluateCanonicalOrdered<T>(
        this CanonicalPolynomial<T> p,
        IReadOnlyList<ISymbol> order,
        IReadOnlyList<T> values)
        where T : struct, IRing<T>, IRealNumber<T> {
        if (order.Count != values.Count) {
            return new Failed<T, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument,
                $"Order and values size mismatch: order.size={order.Count}, values.size={values.Count}.");
        }

        var indexOfSymbol = new Dictionary<ISymbol, int>(order.Count);
        for (int i = 0; i < order.Count; i++) {
            if (indexOfSymbol.ContainsKey(order[i])) {
                return new Failed<T, ErrorCode, Error<ErrorCode>>(
                    ErrorCode.IllegalArgument, "Symbol order contains duplicated symbols.");
            }

            indexOfSymbol[order[i]] = i;
        }

        INumericConstants<T> constants = NumericConstantsRegistry.For<T>();
        T one = constants.One;
        T result = p.Constant;
        foreach (CanonicalMonomial<T> m in p.Monomials) {
            T term = m.Coefficient;
            foreach ((ISymbol? symbol, int power) in m.Powers) {
                if (!indexOfSymbol.TryGetValue(symbol, out int index)) {
                    return new Failed<T, ErrorCode, Error<ErrorCode>>(
                        ErrorCode.DataNotFound, $"Symbol {symbol.Name} not found in order.");
                }

                T powerValue = ComputeNonNegativeRingPower(values[index], power, one);
                term = term.Times(powerValue);
            }
            result = result.Plus(term);
        }
        return new Ok<T, ErrorCode, Error<ErrorCode>>(result);
    }

    // ===== Partial Evaluate =====

    /// <summary>
    /// 对规范多项式进行部分求值 / Partially evaluate a canonical polynomial.
    /// 将已知符号的值代入，返回仅包含未知符号的规范多项式。
    /// </summary>
    /// <param name="p">规范多项式 / Canonical polynomial.</param>
    /// <param name="values">已知符号到值的映射 / Known symbol-to-value mapping.</param>
    /// <returns>部分求值后的规范多项式 / Partially evaluated canonical polynomial.</returns>
    public static CanonicalPolynomial<T> PartialEvaluateCanonical<T>(
        this CanonicalPolynomial<T> p,
        IReadOnlyDictionary<ISymbol, T> values)
        where T : struct, IRing<T>, IRealNumber<T> {
        INumericConstants<T> constants = NumericConstantsRegistry.For<T>();
        T zero = constants.Zero;
        T one = constants.One;
        var remaining = new List<CanonicalMonomial<T>>(p.Monomials.Count);
        T newConstant = p.Constant;
        foreach (CanonicalMonomial<T> m in p.Monomials) {
            T newCoefficient = m.Coefficient;
            var remainedPowers = new Dictionary<ISymbol, int>();
            foreach ((ISymbol? symbol, int power) in m.Powers) {
                if (values.TryGetValue(symbol, out T factor)) {
                    T powerValue = ComputeNonNegativeRingPower(factor, power, one);
                    newCoefficient = newCoefficient.Times(powerValue);
                }
                else {
                    remainedPowers[symbol] = power;
                }
            }
            if (remainedPowers.Count == 0) {
                newConstant = newConstant.Plus(newCoefficient);
            }
            else {
                remaining.Add(new CanonicalMonomial<T>(newCoefficient, remainedPowers));
            }
        }
        return new CanonicalPolynomial<T>(remaining, newConstant).CombineCanonicalTerms();
    }

    // ===== Internal Helpers =====

    /// <summary>
    /// 计算非负整数幂 / Compute non-negative integer power via repeated multiplication.
    /// </summary>
    /// <param name="value">底数 / Base value.</param>
    /// <param name="power">指数（非负）/ Exponent (non-negative).</param>
    /// <param name="one">乘法单位元 / Multiplicative identity.</param>
    /// <returns>幂值 / Power value.</returns>
    public static T ComputeNonNegativeRingPower<T>(T value, int power, T one) where T : struct, IRing<T> {
        if (power == 0) {
            return one;
        }

        if (power == 1) {
            return value;
        }

        T result = one;
        T @base = value;
        int exp = power;
        while (exp > 0) {
            if (exp % 2 == 1) {
                result = result.Times(@base);
            }

            if (exp > 1) {
                @base = @base.Times(@base);
            }

            exp /= 2;
        }
        return result;
    }
}
