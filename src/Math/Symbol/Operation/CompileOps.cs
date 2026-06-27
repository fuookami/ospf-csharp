#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Math.Symbol.Operation;

/// <summary>
/// 编译运算（高级）/ Compile Operations (Advanced).
/// 将多项式编译为高效求值函数，使用预计算的索引映射避免运行时查找开销。
/// 支持编译求值函数和梯度函数。
/// Compiles polynomials into efficient evaluation functions using pre-computed index mapping.
/// Supports compiling evaluation and gradient functions.
/// </summary>
/// <summary>
/// 编译运算（高级，索引模式）/ Compile Operations (Advanced, Index-Based).
/// Kotlin CompileOps.kt 的对应实现，提供索引模式编译和梯度编译。
/// Separate from CompileOps (dictionary-based) in Compile.cs.
/// </summary>
public static class IndexCompileOps {
    // ===== Compile Evaluation =====

    /// <summary>
    /// 编译线性多项式为求值函数 / Compile a linear polynomial into an evaluation function.
    /// </summary>
    /// <param name="p">线性多项式 / Linear polynomial.</param>
    /// <param name="order">符号顺序列表 / Ordered list of symbols.</param>
    /// <returns>接受值列表并返回求值结果的函数 / Function accepting values and returning result.</returns>
    public static Result<Func<IReadOnlyList<T>, T>, ErrorCode, Error<ErrorCode>> CompileEvalLinear<T>(
        this LinearPolynomial<T> p,
        IReadOnlyList<ISymbol> order)
        where T : struct, IRing<T>, IRealNumber<T> {
        Result<Dictionary<ISymbol, int>, ErrorCode, Error<ErrorCode>> indexResult = BuildOrderIndex(order);
        if (indexResult.IsFailed) {
            return new Failed<Func<IReadOnlyList<T>, T>, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument, "Failed to build symbol index.");
        }

        Dictionary<ISymbol, int> indexOfSymbol = indexResult.Value;
        INumericConstants<T> constants = NumericConstantsRegistry.For<T>();
        T zero = constants.Zero;

        LinearPolynomial<T> source = p.CombineLinearTerms();
        int expectedSize = order.Count;
        var compiledMonomials = new List<(T Coefficient, int SymbolIndex)>(source.Monomials.Count);
        foreach (LinearMonomial<T> m in source.Monomials) {
            if (!indexOfSymbol.TryGetValue(m.Symbol, out int symbolIndex)) {
                return new Failed<Func<IReadOnlyList<T>, T>, ErrorCode, Error<ErrorCode>>(
                    ErrorCode.DataNotFound, $"Symbol {m.Symbol.Name} not found in order.");
            }

            compiledMonomials.Add((m.Coefficient, symbolIndex));
        }

        T constant = source.Constant;
        return new Ok<Func<IReadOnlyList<T>, T>, ErrorCode, Error<ErrorCode>>(values => {
            RequireValuesSize(values, expectedSize);
            T result = constant;
            foreach ((T coeff, int symIdx) in compiledMonomials) {
                result = result.Plus(coeff.Times(values[symIdx]));
            }
            return result;
        });
    }

    /// <summary>
    /// 编译二次多项式为求值函数 / Compile a quadratic polynomial into an evaluation function.
    /// </summary>
    /// <param name="p">二次多项式 / Quadratic polynomial.</param>
    /// <param name="order">符号顺序列表 / Ordered list of symbols.</param>
    /// <returns>接受值列表并返回求值结果的函数 / Function accepting values and returning result.</returns>
    public static Result<Func<IReadOnlyList<T>, T>, ErrorCode, Error<ErrorCode>> CompileEvalQuadratic<T>(
        this QuadraticPolynomial<T> p,
        IReadOnlyList<ISymbol> order)
        where T : struct, IRing<T>, IRealNumber<T> {
        Result<Dictionary<ISymbol, int>, ErrorCode, Error<ErrorCode>> indexResult = BuildOrderIndex(order);
        if (indexResult.IsFailed) {
            return new Failed<Func<IReadOnlyList<T>, T>, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument, "Failed to build symbol index.");
        }

        Dictionary<ISymbol, int> indexOfSymbol = indexResult.Value;
        INumericConstants<T> constants = NumericConstantsRegistry.For<T>();
        T zero = constants.Zero;

        QuadraticPolynomial<T> source = p.CombineQuadraticTerms();
        int expectedSize = order.Count;
        var compiledMonomials = new List<(T Coefficient, int Symbol1Index, int? Symbol2Index)>(source.Monomials.Count);
        foreach (QuadraticMonomial<T> m in source.Monomials) {
            if (!indexOfSymbol.TryGetValue(m.Symbol1, out int s1Index)) {
                return new Failed<Func<IReadOnlyList<T>, T>, ErrorCode, Error<ErrorCode>>(
                    ErrorCode.DataNotFound, $"Symbol {m.Symbol1.Name} not found in order.");
            }

            int? s2Index = null;
            if (m.Symbol2 is not null) {
                if (!indexOfSymbol.TryGetValue(m.Symbol2, out int s2i)) {
                    return new Failed<Func<IReadOnlyList<T>, T>, ErrorCode, Error<ErrorCode>>(
                        ErrorCode.DataNotFound, $"Symbol {m.Symbol2.Name} not found in order.");
                }

                s2Index = s2i;
            }

            compiledMonomials.Add((m.Coefficient, s1Index, s2Index));
        }

        T constant = source.Constant;
        return new Ok<Func<IReadOnlyList<T>, T>, ErrorCode, Error<ErrorCode>>(values => {
            RequireValuesSize(values, expectedSize);
            T result = constant;
            foreach ((T coeff, int s1Idx, int? s2Idx) in compiledMonomials) {
                if (s2Idx is null) {
                    result = result.Plus(coeff.Times(values[s1Idx]));
                }
                else {
                    result = result.Plus(coeff.Times(values[s1Idx]).Times(values[s2Idx.Value]));
                }
            }
            return result;
        });
    }

    /// <summary>
    /// 编译规范多项式为求值函数 / Compile a canonical polynomial into an evaluation function.
    /// </summary>
    /// <param name="p">规范多项式 / Canonical polynomial.</param>
    /// <param name="order">符号顺序列表 / Ordered list of symbols.</param>
    /// <returns>接受值列表并返回求值结果的函数 / Function accepting values and returning result.</returns>
    public static Result<Func<IReadOnlyList<T>, T>, ErrorCode, Error<ErrorCode>> CompileEvalCanonical<T>(
        this CanonicalPolynomial<T> p,
        IReadOnlyList<ISymbol> order)
        where T : struct, IRing<T>, IRealNumber<T> {
        Result<Dictionary<ISymbol, int>, ErrorCode, Error<ErrorCode>> indexResult = BuildOrderIndex(order);
        if (indexResult.IsFailed) {
            return new Failed<Func<IReadOnlyList<T>, T>, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument, "Failed to build symbol index.");
        }

        Dictionary<ISymbol, int> indexOfSymbol = indexResult.Value;
        INumericConstants<T> constants = NumericConstantsRegistry.For<T>();
        T zero = constants.Zero;
        T one = constants.One;

        CanonicalPolynomial<T> source = p.CombineCanonicalTerms();
        int expectedSize = order.Count;
        var compiledMonomials = new List<(T Coefficient, List<(int SymbolIndex, int Power)> Powers)>(source.Monomials.Count);
        foreach (CanonicalMonomial<T> m in source.Monomials) {
            var powers = new List<(int, int)>(m.Powers.Count);
            foreach ((ISymbol? symbol, int power) in m.Powers) {
                if (power < 0) {
                    return new Failed<Func<IReadOnlyList<T>, T>, ErrorCode, Error<ErrorCode>>(
                        ErrorCode.IllegalArgument, "Negative exponent is not supported by compiled canonical evaluation.");
                }

                if (!indexOfSymbol.TryGetValue(symbol, out int symIndex)) {
                    return new Failed<Func<IReadOnlyList<T>, T>, ErrorCode, Error<ErrorCode>>(
                        ErrorCode.DataNotFound, $"Symbol {symbol.Name} not found in order.");
                }

                powers.Add((symIndex, power));
            }

            compiledMonomials.Add((m.Coefficient, powers));
        }

        T constant = source.Constant;
        return new Ok<Func<IReadOnlyList<T>, T>, ErrorCode, Error<ErrorCode>>(values => {
            RequireValuesSize(values, expectedSize);
            T result = constant;
            foreach ((T coeff, List<(int SymIdx, int Pow)> powers) in compiledMonomials) {
                T term = coeff;
                foreach ((int symIdx, int pow) in powers) {
                    term = term.Times(CanonicalOps.ComputeNonNegativeRingPower(values[symIdx], pow, one));
                }
                result = result.Plus(term);
            }
            return result;
        });
    }

    // ===== Compile Gradient =====

    /// <summary>
    /// 编译线性多项式的梯度函数 / Compile a linear polynomial's gradient function.
    /// </summary>
    /// <param name="p">线性多项式 / Linear polynomial.</param>
    /// <param name="order">符号顺序列表 / Ordered list of symbols.</param>
    /// <returns>接受值列表并返回梯度向量的函数 / Function accepting values and returning gradient vector.</returns>
    public static Result<Func<IReadOnlyList<T>, IReadOnlyList<T>>, ErrorCode, Error<ErrorCode>> CompileGradientLinear<T>(
        this LinearPolynomial<T> p,
        IReadOnlyList<ISymbol> order)
        where T : struct, IRing<T>, IRealNumber<T> {
        Result<Dictionary<ISymbol, int>, ErrorCode, Error<ErrorCode>> indexResult = BuildOrderIndex(order);
        if (indexResult.IsFailed) {
            return new Failed<Func<IReadOnlyList<T>, IReadOnlyList<T>>, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument, "Failed to build symbol index.");
        }

        Dictionary<ISymbol, int> indexOfSymbol = indexResult.Value;
        INumericConstants<T> constants = NumericConstantsRegistry.For<T>();
        T zero = constants.Zero;

        LinearPolynomial<T> source = p.CombineLinearTerms();
        int expectedSize = order.Count;

        // For linear polynomials, gradient is constant (coefficients)
        var gradient = new T[expectedSize];
        for (int i = 0; i < expectedSize; i++) {
            gradient[i] = zero;
        }

        foreach (LinearMonomial<T> m in source.Monomials) {
            if (!indexOfSymbol.TryGetValue(m.Symbol, out int symIndex)) {
                return new Failed<Func<IReadOnlyList<T>, IReadOnlyList<T>>, ErrorCode, Error<ErrorCode>>(
                    ErrorCode.DataNotFound, $"Symbol {m.Symbol.Name} not found in order.");
            }

            gradient[symIndex] = gradient[symIndex].Plus(m.Coefficient);
        }

        IReadOnlyList<T> compiledGradient = Array.AsReadOnly(gradient);
        return new Ok<Func<IReadOnlyList<T>, IReadOnlyList<T>>, ErrorCode, Error<ErrorCode>>(values => {
            RequireValuesSize(values, expectedSize);
            return compiledGradient;
        });
    }

    /// <summary>
    /// 编译二次多项式的梯度函数 / Compile a quadratic polynomial's gradient function.
    /// </summary>
    /// <param name="p">二次多项式 / Quadratic polynomial.</param>
    /// <param name="order">符号顺序列表 / Ordered list of symbols.</param>
    /// <returns>接受值列表并返回梯度向量的函数 / Function accepting values and returning gradient vector.</returns>
    public static Result<Func<IReadOnlyList<T>, IReadOnlyList<T>>, ErrorCode, Error<ErrorCode>> CompileGradientQuadratic<T>(
        this QuadraticPolynomial<T> p,
        IReadOnlyList<ISymbol> order)
        where T : struct, IRing<T>, IRealNumber<T> {
        Result<Dictionary<ISymbol, int>, ErrorCode, Error<ErrorCode>> indexResult = BuildOrderIndex(order);
        if (indexResult.IsFailed) {
            return new Failed<Func<IReadOnlyList<T>, IReadOnlyList<T>>, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument, "Failed to build symbol index.");
        }

        Dictionary<ISymbol, int> indexOfSymbol = indexResult.Value;
        INumericConstants<T> constants = NumericConstantsRegistry.For<T>();
        T zero = constants.Zero;

        QuadraticPolynomial<T> source = p.CombineQuadraticTerms();
        int expectedSize = order.Count;

        // Separate linear terms (constant gradient) from quadratic terms (variable gradient)
        var baseGradient = new T[expectedSize];
        for (int i = 0; i < expectedSize; i++) {
            baseGradient[i] = zero;
        }

        var quadraticMonomials = new List<(T Coefficient, int S1Index, int S2Index)>();
        foreach (QuadraticMonomial<T> m in source.Monomials) {
            if (!indexOfSymbol.TryGetValue(m.Symbol1, out int s1Index)) {
                return new Failed<Func<IReadOnlyList<T>, IReadOnlyList<T>>, ErrorCode, Error<ErrorCode>>(
                    ErrorCode.DataNotFound, $"Symbol {m.Symbol1.Name} not found in order.");
            }

            if (m.Symbol2 is null) {
                // Linear term: constant gradient contribution
                baseGradient[s1Index] = baseGradient[s1Index].Plus(m.Coefficient);
            }
            else {
                if (!indexOfSymbol.TryGetValue(m.Symbol2, out int s2Index)) {
                    return new Failed<Func<IReadOnlyList<T>, IReadOnlyList<T>>, ErrorCode, Error<ErrorCode>>(
                        ErrorCode.DataNotFound, $"Symbol {m.Symbol2.Name} not found in order.");
                }

                quadraticMonomials.Add((m.Coefficient, s1Index, s2Index));
            }
        }

        return new Ok<Func<IReadOnlyList<T>, IReadOnlyList<T>>, ErrorCode, Error<ErrorCode>>(values => {
            RequireValuesSize(values, expectedSize);
            var gradient = new T[expectedSize];
            Array.Copy(baseGradient, gradient, expectedSize);
            foreach ((T coeff, int s1Idx, int s2Idx) in quadraticMonomials) {
                if (s1Idx == s2Idx) {
                    // d/dx(c*x^2) = 2*c*x
                    T twoCoeff = coeff.Plus(coeff);
                    gradient[s1Idx] = gradient[s1Idx].Plus(twoCoeff.Times(values[s1Idx]));
                }
                else {
                    // d/dx1(c*x1*x2) = c*x2; d/dx2(c*x1*x2) = c*x1
                    gradient[s1Idx] = gradient[s1Idx].Plus(coeff.Times(values[s2Idx]));
                    gradient[s2Idx] = gradient[s2Idx].Plus(coeff.Times(values[s1Idx]));
                }
            }
            return Array.AsReadOnly(gradient);
        });
    }

    // ===== Internal Helpers =====

    /// <summary>
    /// 编译符号顺序为索引映射 / Compile ordered symbol list into symbol-to-index map.
    /// </summary>
    private static Result<Dictionary<ISymbol, int>, ErrorCode, Error<ErrorCode>> BuildOrderIndex(
        IReadOnlyList<ISymbol> order) {
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

    /// <summary>
    /// 验证值列表大小与预期一致 / Assert value list size matches expected.
    /// </summary>
    private static void RequireValuesSize<T>(IReadOnlyList<T> values, int expectedSize) {
        if (values.Count != expectedSize) {
            throw new ArgumentException(
                $"Order and values size mismatch: order.size={expectedSize}, values.size={values.Count}.");
        }
    }
}
