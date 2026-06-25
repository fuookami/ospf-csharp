#nullable enable

using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;

namespace Fuookami.Ospf.Math.Symbol.Operation
{
    /// <summary>
    /// 求值运算 / Evaluate operations.
    /// 将符号多项式在给定变量赋值下求值。
    /// Evaluates symbolic polynomials under given variable assignments.
    /// </summary>
    public static class EvaluateOps
    {
        // ===== LinearMonomial evaluate =====

        /// <summary>求值线性单项式 / Evaluate linear monomial.</summary>
        public static T Evaluate<T>(this LinearMonomial<T> m, IReadOnlyDictionary<ISymbol, T> values, T zero)
            where T : struct, IRing<T>
        {
            if (values.TryGetValue(m.Symbol, out var v))
                return m.Coefficient.Times(v);
            return zero;
        }

        // ===== LinearPolynomial evaluate =====

        /// <summary>求值线性多项式 / Evaluate linear polynomial.</summary>
        public static T Evaluate<T>(this LinearPolynomial<T> p, IReadOnlyDictionary<ISymbol, T> values)
            where T : struct, IRing<T>
        {
            var result = p.Constant;
            foreach (var m in p.Monomials)
            {
                if (values.TryGetValue(m.Symbol, out var v))
                    result = result.Plus(m.Coefficient.Times(v));
            }
            return result;
        }

        /// <summary>使用 IValueProvider 求值线性多项式 / Evaluate linear polynomial with value provider.</summary>
        public static T Evaluate<T>(this LinearPolynomial<T> p, IValueProvider<T> provider,
            MissingValuePolicy policy = MissingValuePolicy.UseZero)
            where T : struct, IRing<T>, IRealNumber<T>
        {
            var constants = NumericConstantsRegistry.For<T>();
            var result = p.Constant;
            foreach (var m in p.Monomials)
            {
                var val = provider.TryGetValue(m.Symbol);
                if (val is null)
                {
                    result = policy switch
                    {
                        MissingValuePolicy.UseZero => result,
                        MissingValuePolicy.Throw => throw new System.Collections.Generic.KeyNotFoundException(
                            $"Symbol {m.Symbol.Name} not found in value provider."),
                        _ => result
                    };
                    continue;
                }
                result = result.Plus(m.Coefficient.Times(val.Value));
            }
            return result;
        }

        // ===== QuadraticMonomial evaluate =====

        /// <summary>求值二次单项式 / Evaluate quadratic monomial.</summary>
        public static T Evaluate<T>(this QuadraticMonomial<T> m, IReadOnlyDictionary<ISymbol, T> values, T zero)
            where T : struct, IRing<T>
        {
            if (!values.TryGetValue(m.Symbol1, out var v1))
                return zero;
            if (m.Symbol2 is null)
                return m.Coefficient.Times(v1);
            if (!values.TryGetValue(m.Symbol2, out var v2))
                return zero;
            return m.Coefficient.Times(v1).Times(v2);
        }

        // ===== QuadraticPolynomial evaluate =====

        /// <summary>求值二次多项式 / Evaluate quadratic polynomial.</summary>
        public static T Evaluate<T>(this QuadraticPolynomial<T> p, IReadOnlyDictionary<ISymbol, T> values)
            where T : struct, IRing<T>
        {
            var result = p.Constant;
            foreach (var m in p.Monomials)
            {
                if (!values.TryGetValue(m.Symbol1, out var v1))
                    continue;
                if (m.Symbol2 is null)
                {
                    result = result.Plus(m.Coefficient.Times(v1));
                }
                else if (values.TryGetValue(m.Symbol2, out var v2))
                {
                    result = result.Plus(m.Coefficient.Times(v1).Times(v2));
                }
            }
            return result;
        }

        // ===== CanonicalMonomial evaluate =====

        /// <summary>求值规范单项式 / Evaluate canonical monomial.</summary>
        public static T Evaluate<T>(this CanonicalMonomial<T> m, IReadOnlyDictionary<ISymbol, T> values, T zero)
            where T : struct, IRing<T>
        {
            var product = m.Coefficient;
            foreach (var (symbol, power) in m.Powers)
            {
                if (!values.TryGetValue(symbol, out var v))
                    return zero;
                for (var i = 0; i < power; i++)
                    product = product.Times(v);
            }
            return product;
        }

        // ===== CanonicalPolynomial evaluate =====

        /// <summary>求值规范多项式 / Evaluate canonical polynomial.</summary>
        public static T Evaluate<T>(this CanonicalPolynomial<T> p, IReadOnlyDictionary<ISymbol, T> values)
            where T : struct, IRing<T>, IRealNumber<T>
        {
            var constants = NumericConstantsRegistry.For<T>();
            var result = p.Constant;
            foreach (var m in p.Monomials)
            {
                var term = m.Coefficient;
                var allFound = true;
                foreach (var (symbol, power) in m.Powers)
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
        }

        // ===== Partial evaluate =====

        /// <summary>部分求值线性多项式（固定某些变量）/ Partially evaluate linear polynomial (fix some variables).</summary>
        public static LinearPolynomial<T> PartialEvaluate<T>(
            this LinearPolynomial<T> p,
            IReadOnlyDictionary<ISymbol, T> values)
            where T : struct, IRing<T>
        {
            var remaining = new List<LinearMonomial<T>>();
            var constant = p.Constant;
            foreach (var m in p.Monomials)
            {
                if (values.TryGetValue(m.Symbol, out var v))
                    constant = constant.Plus(m.Coefficient.Times(v));
                else
                    remaining.Add(m);
            }
            return new LinearPolynomial<T>(remaining, constant);
        }

        /// <summary>部分求值二次多项式（固定某些变量）/ Partially evaluate quadratic polynomial (fix some variables).</summary>
        public static QuadraticPolynomial<T> PartialEvaluate<T>(
            this QuadraticPolynomial<T> p,
            IReadOnlyDictionary<ISymbol, T> values)
            where T : struct, IRing<T>
        {
            var remaining = new List<QuadraticMonomial<T>>();
            var constant = p.Constant;
            foreach (var m in p.Monomials)
            {
                var v1Found = values.TryGetValue(m.Symbol1, out var v1);
                if (m.Symbol2 is null)
                {
                    if (v1Found)
                        constant = constant.Plus(m.Coefficient.Times(v1!));
                    else
                        remaining.Add(m);
                }
                else
                {
                    var v2Found = values.TryGetValue(m.Symbol2, out var v2);
                    if (v1Found && v2Found)
                        constant = constant.Plus(m.Coefficient.Times(v1!).Times(v2!));
                    else
                        remaining.Add(m);
                }
            }
            return new QuadraticPolynomial<T>(remaining, constant);
        }
    }
}
