#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;

namespace Fuookami.Ospf.Math.Symbol.Operation
{
    /// <summary>
    /// 编译运算 / Compile operations.
    /// 将符号多项式编译为可高效重复求值的委托。
    /// Compiles symbolic polynomials into delegates for efficient repeated evaluation.
    /// </summary>
    public static class CompileOps
    {
        /// <summary>
        /// 编译线性多项式为委托 / Compile linear polynomial to delegate.
        /// </summary>
        /// <returns>接受值字典返回结果的委托 / Delegate accepting value dictionary and returning result.</returns>
        public static Func<IReadOnlyDictionary<ISymbol, T>, T> Compile<T>(this LinearPolynomial<T> p)
            where T : struct, IRing<T>
        {
            // Capture monomials and constant in closure
            var monomials = new List<(T Coefficient, ISymbol Symbol)>(p.Monomials.Count);
            foreach (var m in p.Monomials)
                monomials.Add((m.Coefficient, m.Symbol));
            var constant = p.Constant;

            return values =>
            {
                var result = constant;
                foreach (var (coeff, sym) in monomials)
                {
                    if (values.TryGetValue(sym, out var v))
                        result = result.Plus(coeff.Times(v));
                }
                return result;
            };
        }

        /// <summary>
        /// 编译二次多项式为委托 / Compile quadratic polynomial to delegate.
        /// </summary>
        public static Func<IReadOnlyDictionary<ISymbol, T>, T> Compile<T>(this QuadraticPolynomial<T> p)
            where T : struct, IRing<T>
        {
            var monomials = new List<(T Coefficient, ISymbol Symbol1, ISymbol? Symbol2)>(p.Monomials.Count);
            foreach (var m in p.Monomials)
                monomials.Add((m.Coefficient, m.Symbol1, m.Symbol2));
            var constant = p.Constant;

            return values =>
            {
                var result = constant;
                foreach (var (coeff, s1, s2) in monomials)
                {
                    if (!values.TryGetValue(s1, out var v1))
                        continue;
                    if (s2 is null)
                    {
                        result = result.Plus(coeff.Times(v1));
                    }
                    else if (values.TryGetValue(s2, out var v2))
                    {
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
            where T : struct, IRing<T>
        {
            var monomials = new List<(T Coefficient, IReadOnlyDictionary<ISymbol, int> Powers)>(p.Monomials.Count);
            foreach (var m in p.Monomials)
                monomials.Add((m.Coefficient, m.Powers));
            var constant = p.Constant;

            return values =>
            {
                var result = constant;
                foreach (var (coeff, powers) in monomials)
                {
                    var term = coeff;
                    var allFound = true;
                    foreach (var (symbol, power) in powers)
                    {
                        if (!values.TryGetValue(symbol, out var v))
                        {
                            allFound = false;
                            break;
                        }
                        for (var i = 0; i < power; i++)
                            term = term.Times(v);
                    }
                    if (allFound)
                        result = result.Plus(term);
                }
                return result;
            };
        }
    }
}
