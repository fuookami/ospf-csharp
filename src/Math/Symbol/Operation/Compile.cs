#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Math.Symbol.Operation;
/// <summary>
/// 编译运算 / Compile operations.
/// 将符号多项式编译为可高效重复求值的委托。
/// Compiles symbolic polynomials into delegates for efficient repeated evaluation.
/// </summary>
public static class CompileOps {
    /// <summary>
    /// 编译线性多项式为委托 / Compile linear polynomial to delegate.
    /// </summary>
    /// <returns>接受值字典返回结果的委托 / Delegate accepting value dictionary and returning result.</returns>
    public static Func<IReadOnlyDictionary<ISymbol, T>, T> Compile<T>(this LinearPolynomial<T> p)
        where T : struct, IRing<T> {
        // Capture monomials and constant in closure
        var monomials = new List<(T Coefficient, ISymbol Symbol)>(p.Monomials.Count);
        foreach (LinearMonomial<T> m in p.Monomials) {
            monomials.Add((m.Coefficient, m.Symbol));
        }

        T constant = p.Constant;

        return values => {
            T result = constant;
            foreach ((T coeff, ISymbol? sym) in monomials) {
                if (values.TryGetValue(sym, out T v)) {
                    result = result.Plus(coeff.Times(v));
                }
            }
            return result;
        };
    }

    /// <summary>
    /// 编译二次多项式为委托 / Compile quadratic polynomial to delegate.
    /// </summary>
    public static Func<IReadOnlyDictionary<ISymbol, T>, T> Compile<T>(this QuadraticPolynomial<T> p)
        where T : struct, IRing<T> {
        var monomials = new List<(T Coefficient, ISymbol Symbol1, ISymbol? Symbol2)>(p.Monomials.Count);
        foreach (QuadraticMonomial<T> m in p.Monomials) {
            monomials.Add((m.Coefficient, m.Symbol1, m.Symbol2));
        }

        T constant = p.Constant;

        return values => {
            T result = constant;
            foreach ((T coeff, ISymbol? s1, ISymbol? s2) in monomials) {
                if (!values.TryGetValue(s1, out T v1)) {
                    continue;
                }

                if (s2 is null) {
                    result = result.Plus(coeff.Times(v1));
                }
                else if (values.TryGetValue(s2, out T v2)) {
                    result = result.Plus(coeff.Times(v1).Times(v2));
                }
            }
            return result;
        };
    }

    /// <summary>
    /// 编译规范多项式为委托 / Compile canonical polynomial to delegate.
    /// </summary>
    public static Func<IReadOnlyDictionary<ISymbol, T>, T> Compile<T>(this CanonicalPolynomial<T> p)
        where T : struct, IRing<T> {
        var monomials = new List<(T Coefficient, IReadOnlyDictionary<ISymbol, int> Powers)>(p.Monomials.Count);
        foreach (CanonicalMonomial<T> m in p.Monomials) {
            monomials.Add((m.Coefficient, m.Powers));
        }

        T constant = p.Constant;

        return values => {
            T result = constant;
            foreach ((T coeff, IReadOnlyDictionary<ISymbol, int>? powers) in monomials) {
                T term = coeff;
                bool allFound = true;
                foreach ((ISymbol? symbol, int power) in powers) {
                    if (!values.TryGetValue(symbol, out T v)) {
                        allFound = false;
                        break;
                    }
                    for (int i = 0; i < power; i++) {
                        term = term.Times(v);
                    }
                }
                if (allFound) {
                    result = result.Plus(term);
                }
            }
            return result;
        };
    }
}
