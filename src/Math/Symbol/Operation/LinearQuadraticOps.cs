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
/// 线性二次核心运算 / Linear-Quadratic Core Operations.
/// 提供线性和二次多项式的同类项合并、求值、有序求值和部分求值。
/// Provides combining like terms, evaluation, ordered evaluation,
/// and partial evaluation for linear and quadratic polynomials.
/// </summary>
public static class LinearQuadraticOps {
    // ===== Linear: Combine Like Terms =====

    /// <summary>
    /// 合并线性多项式中的同类项 / Combine like terms in a linear polynomial.
    /// </summary>
    /// <param name="p">线性多项式 / Linear polynomial.</param>
    /// <returns>合并同类项后的线性多项式 / Linear polynomial with like terms combined.</returns>
    public static LinearPolynomial<T> CombineLinearTerms<T>(this LinearPolynomial<T> p)
        where T : struct, IRing<T>, IRealNumber<T> {
        INumericConstants<T> constants = NumericConstantsRegistry.For<T>();
        T zero = constants.Zero;
        var grouped = new Dictionary<ISymbol, T>(p.Monomials.Count);
        foreach (LinearMonomial<T> m in p.Monomials) {
            if (grouped.TryGetValue(m.Symbol, out T existing)) {
                grouped[m.Symbol] = existing.Plus(m.Coefficient);
            }
            else {
                grouped[m.Symbol] = m.Coefficient;
            }
        }

        var result = grouped
            .Where(kv => !kv.Value.Eq(zero))
            .Select(kv => new LinearMonomial<T>(kv.Value, kv.Key))
            .ToList();
        return new LinearPolynomial<T>(result, p.Constant);
    }

    // ===== Linear: Evaluate =====

    /// <summary>
    /// 使用给定值对线性多项式求值 / Evaluate a linear polynomial with given values.
    /// </summary>
    /// <param name="p">线性多项式 / Linear polynomial.</param>
    /// <param name="values">符号到值的映射 / Symbol-to-value mapping.</param>
    /// <param name="onMissing">缺失符号的回调（可选）/ Callback for missing symbols (optional).</param>
    /// <returns>求值结果，若存在未提供值的符号则返回 null / Evaluation result, or null if a symbol has no value.</returns>
    public static T? EvaluateLinear<T>(
        this LinearPolynomial<T> p,
        IReadOnlyDictionary<ISymbol, T> values,
        Func<ISymbol, T?>? onMissing = null)
        where T : struct, IRing<T> {
        T result = p.Constant;
        foreach (LinearMonomial<T> m in p.Monomials) {
            T? symbolValue = values.TryGetValue(m.Symbol, out T v) ? v : onMissing?.Invoke(m.Symbol);
            if (symbolValue is null) {
                return null;
            }

            result = result.Plus(m.Coefficient.Times(symbolValue.Value));
        }
        return result;
    }

    /// <summary>
    /// 使用有序符号和值对线性多项式求值 / Evaluate a linear polynomial with ordered symbols and values.
    /// </summary>
    /// <param name="p">线性多项式 / Linear polynomial.</param>
    /// <param name="order">符号顺序列表 / Ordered list of symbols.</param>
    /// <param name="values">与符号顺序对应的值列表 / Values corresponding to symbol order.</param>
    /// <returns>求值结果 / Evaluation result.</returns>
    public static Result<T, ErrorCode, Error<ErrorCode>> EvaluateLinearOrdered<T>(
        this LinearPolynomial<T> p,
        IReadOnlyList<ISymbol> order,
        IReadOnlyList<T> values)
        where T : struct, IRing<T> {
        Result<Dictionary<ISymbol, int>, ErrorCode, Error<ErrorCode>> indexResult = BuildOrderedSymbolIndex(order, values.Count);
        if (indexResult.IsFailed) {
            return new Failed<T, ErrorCode, Error<ErrorCode>>(ErrorCode.IllegalArgument, "Failed to build symbol index.");
        }

        Dictionary<ISymbol, int> indexOfSymbol = indexResult.Value;
        T result = p.Constant;
        foreach (LinearMonomial<T> m in p.Monomials) {
            if (!indexOfSymbol.TryGetValue(m.Symbol, out int index)) {
                return new Failed<T, ErrorCode, Error<ErrorCode>>(
                    ErrorCode.DataNotFound, $"Symbol {m.Symbol.Name} not found in order.");
            }

            result = result.Plus(m.Coefficient.Times(values[index]));
        }
        return new Ok<T, ErrorCode, Error<ErrorCode>>(result);
    }

    // ===== Linear: Partial Evaluate =====

    /// <summary>
    /// 对线性多项式进行部分求值 / Partially evaluate a linear polynomial.
    /// 将已知符号的值代入，返回仅包含未知符号的线性多项式。
    /// </summary>
    /// <param name="p">线性多项式 / Linear polynomial.</param>
    /// <param name="values">已知符号到值的映射 / Known symbol-to-value mapping.</param>
    /// <returns>部分求值后的线性多项式 / Partially evaluated linear polynomial.</returns>
    public static LinearPolynomial<T> PartialEvaluateLinear<T>(
        this LinearPolynomial<T> p,
        IReadOnlyDictionary<ISymbol, T> values)
        where T : struct, IRing<T>, IRealNumber<T> {
        INumericConstants<T> constants = NumericConstantsRegistry.For<T>();
        T zero = constants.Zero;
        var remaining = new List<LinearMonomial<T>>(p.Monomials.Count);
        T newConstant = p.Constant;
        foreach (LinearMonomial<T> m in p.Monomials) {
            if (values.TryGetValue(m.Symbol, out T v)) {
                newConstant = newConstant.Plus(m.Coefficient.Times(v));
            }
            else {
                remaining.Add(m);
            }
        }
        return new LinearPolynomial<T>(remaining, newConstant).CombineLinearTerms();
    }

    // ===== Quadratic: Combine Like Terms =====

    /// <summary>
    /// 合并二次多项式中的同类项 / Combine like terms in a quadratic polynomial.
    /// </summary>
    /// <param name="p">二次多项式 / Quadratic polynomial.</param>
    /// <returns>合并同类项后的二次多项式 / Quadratic polynomial with like terms combined.</returns>
    public static QuadraticPolynomial<T> CombineQuadraticTerms<T>(this QuadraticPolynomial<T> p)
        where T : struct, IRing<T>, IRealNumber<T> {
        INumericConstants<T> constants = NumericConstantsRegistry.For<T>();
        T zero = constants.Zero;
        var grouped = new Dictionary<(ISymbol, ISymbol?), T>();
        foreach (QuadraticMonomial<T> m in p.Monomials) {
            (ISymbol, ISymbol?) key = (m.Symbol1, m.Symbol2);
            if (grouped.TryGetValue(key, out T existing)) {
                grouped[key] = existing.Plus(m.Coefficient);
            }
            else {
                grouped[key] = m.Coefficient;
            }
        }

        var result = grouped
            .Where(kv => !kv.Value.Eq(zero))
            .Select(kv => new QuadraticMonomial<T>(kv.Value, kv.Key.Item1, kv.Key.Item2))
            .ToList();
        return new QuadraticPolynomial<T>(result, p.Constant);
    }

    // ===== Quadratic: Evaluate =====

    /// <summary>
    /// 使用给定值对二次多项式求值 / Evaluate a quadratic polynomial with given values.
    /// </summary>
    /// <param name="p">二次多项式 / Quadratic polynomial.</param>
    /// <param name="values">符号到值的映射 / Symbol-to-value mapping.</param>
    /// <param name="onMissing">缺失符号的回调（可选）/ Callback for missing symbols (optional).</param>
    /// <returns>求值结果，若存在未提供值的符号则返回 null / Evaluation result, or null if a symbol has no value.</returns>
    public static T? EvaluateQuadratic<T>(
        this QuadraticPolynomial<T> p,
        IReadOnlyDictionary<ISymbol, T> values,
        Func<ISymbol, T?>? onMissing = null)
        where T : struct, IRing<T> {
        T result = p.Constant;
        foreach (QuadraticMonomial<T> m in p.Monomials) {
            T? v1 = values.TryGetValue(m.Symbol1, out T sv1) ? sv1 : onMissing?.Invoke(m.Symbol1);
            if (v1 is null) {
                return null;
            }

            T term = m.Coefficient.Times(v1.Value);
            if (m.Symbol2 is not null) {
                T? v2 = values.TryGetValue(m.Symbol2, out T sv2) ? sv2 : onMissing?.Invoke(m.Symbol2);
                if (v2 is null) {
                    return null;
                }

                term = term.Times(v2.Value);
            }
            result = result.Plus(term);
        }
        return result;
    }

    /// <summary>
    /// 使用有序符号和值对二次多项式求值 / Evaluate a quadratic polynomial with ordered symbols and values.
    /// </summary>
    /// <param name="p">二次多项式 / Quadratic polynomial.</param>
    /// <param name="order">符号顺序列表 / Ordered list of symbols.</param>
    /// <param name="values">与符号顺序对应的值列表 / Values corresponding to symbol order.</param>
    /// <returns>求值结果 / Evaluation result.</returns>
    public static Result<T, ErrorCode, Error<ErrorCode>> EvaluateQuadraticOrdered<T>(
        this QuadraticPolynomial<T> p,
        IReadOnlyList<ISymbol> order,
        IReadOnlyList<T> values)
        where T : struct, IRing<T> {
        Result<Dictionary<ISymbol, int>, ErrorCode, Error<ErrorCode>> indexResult = BuildOrderedSymbolIndex(order, values.Count);
        if (indexResult.IsFailed) {
            return new Failed<T, ErrorCode, Error<ErrorCode>>(ErrorCode.IllegalArgument, "Failed to build symbol index.");
        }

        Dictionary<ISymbol, int> indexOfSymbol = indexResult.Value;
        T result = p.Constant;
        foreach (QuadraticMonomial<T> m in p.Monomials) {
            if (!indexOfSymbol.TryGetValue(m.Symbol1, out int i1)) {
                return new Failed<T, ErrorCode, Error<ErrorCode>>(
                    ErrorCode.DataNotFound, $"Symbol {m.Symbol1.Name} not found in order.");
            }

            T term = m.Coefficient.Times(values[i1]);
            if (m.Symbol2 is not null) {
                if (!indexOfSymbol.TryGetValue(m.Symbol2, out int i2)) {
                    return new Failed<T, ErrorCode, Error<ErrorCode>>(
                        ErrorCode.DataNotFound, $"Symbol {m.Symbol2.Name} not found in order.");
                }

                term = term.Times(values[i2]);
            }
            result = result.Plus(term);
        }
        return new Ok<T, ErrorCode, Error<ErrorCode>>(result);
    }

    // ===== Quadratic: Partial Evaluate =====

    /// <summary>
    /// 对二次多项式进行部分求值 / Partially evaluate a quadratic polynomial.
    /// 将已知符号的值代入，返回仅包含未知符号的二次多项式。
    /// </summary>
    /// <param name="p">二次多项式 / Quadratic polynomial.</param>
    /// <param name="values">已知符号到值的映射 / Known symbol-to-value mapping.</param>
    /// <returns>部分求值后的二次多项式 / Partially evaluated quadratic polynomial.</returns>
    public static QuadraticPolynomial<T> PartialEvaluateQuadratic<T>(
        this QuadraticPolynomial<T> p,
        IReadOnlyDictionary<ISymbol, T> values)
        where T : struct, IRing<T>, IRealNumber<T> {
        INumericConstants<T> constants = NumericConstantsRegistry.For<T>();
        T zero = constants.Zero;
        var remaining = new List<QuadraticMonomial<T>>(p.Monomials.Count);
        T newConstant = p.Constant;
        foreach (QuadraticMonomial<T> m in p.Monomials) {
            bool v1Found = values.TryGetValue(m.Symbol1, out T v1);
            if (m.Symbol2 is null) {
                if (v1Found) {
                    newConstant = newConstant.Plus(m.Coefficient.Times(v1));
                }
                else {
                    remaining.Add(m);
                }
            }
            else {
                bool v2Found = values.TryGetValue(m.Symbol2, out T v2);
                if (v1Found && v2Found) {
                    newConstant = newConstant.Plus(m.Coefficient.Times(v1).Times(v2));
                }
                else if (v1Found) {
                    remaining.Add(new QuadraticMonomial<T>(m.Coefficient.Times(v1), m.Symbol2, null));
                }
                else if (v2Found) {
                    remaining.Add(new QuadraticMonomial<T>(m.Coefficient.Times(v2), m.Symbol1, null));
                }
                else {
                    remaining.Add(m);
                }
            }
        }
        return new QuadraticPolynomial<T>(remaining, newConstant).CombineQuadraticTerms();
    }

    // ===== Internal Helpers =====

    /// <summary>
    /// 构建有序符号到索引的映射 / Build ordered symbol-to-index mapping.
    /// </summary>
    private static Result<Dictionary<ISymbol, int>, ErrorCode, Error<ErrorCode>> BuildOrderedSymbolIndex(
        IReadOnlyList<ISymbol> order, int valuesSize) {
        if (order.Count != valuesSize) {
            return new Failed<Dictionary<ISymbol, int>, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument,
                $"Order and values size mismatch: order.size={order.Count}, values.size={valuesSize}.");
        }

        var indexOfSymbol = new Dictionary<ISymbol, int>(order.Count);
        for (int i = 0; i < order.Count; i++) {
            if (indexOfSymbol.ContainsKey(order[i])) {
                return new Failed<Dictionary<ISymbol, int>, ErrorCode, Error<ErrorCode>>(
                    ErrorCode.IllegalArgument, "Symbol order contains duplicated symbols.");
            }

            indexOfSymbol[order[i]] = i;
        }
        return new Ok<Dictionary<ISymbol, int>, ErrorCode, Error<ErrorCode>>(indexOfSymbol);
    }
}
