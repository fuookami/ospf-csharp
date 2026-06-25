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
    /// 微分运算 / Differentiation operations.
    /// 对符号多项式关于指定变量求偏导。
    /// Computes partial derivatives of symbolic polynomials with respect to a given variable.
    /// </summary>
    public static class DifferentiateOps
    {
        /// <summary>
        /// 线性多项式对指定变量求偏导 / Differentiate linear polynomial with respect to a variable.
        /// </summary>
        /// <returns>导数（常数）/ Derivative (constant).</returns>
        public static T Differentiate<T>(this LinearPolynomial<T> p, ISymbol variable)
            where T : struct, IRing<T>, IRealNumber<T>
        {
            return p.Monomials
                .Where(m => SymbolIdentity.StableId(m.Symbol) == SymbolIdentity.StableId(variable))
                .Aggregate(p.Constant.Minus(p.Constant), (acc, m) => acc.Plus(m.Coefficient));
        }

        /// <summary>
        /// 二次多项式对指定变量求偏导 / Differentiate quadratic polynomial with respect to a variable.
        /// </summary>
        /// <returns>导数多项式 / Derivative polynomial.</returns>
        public static LinearPolynomial<T> Differentiate<T>(this QuadraticPolynomial<T> p, ISymbol variable)
            where T : struct, IRing<T>, IRealNumber<T>
        {
            var constants = NumericConstantsRegistry.For<T>();
            var two = constants.Two;
            var zero = constants.Zero;
            var result = new List<LinearMonomial<T>>();
            var constTerm = zero;

            foreach (var m in p.Monomials)
            {
                if (m.IsQuadratic)
                {
                    var s1Match = SymbolIdentity.StableId(m.Symbol1) == SymbolIdentity.StableId(variable);
                    var s2Match = SymbolIdentity.StableId(m.Symbol2!) == SymbolIdentity.StableId(variable);

                    if (s1Match && s2Match)
                    {
                        // d/dx (c * x^2) = 2c * x
                        result.Add(new LinearMonomial<T>(m.Coefficient.Times(two), m.Symbol1));
                    }
                    else if (s1Match)
                    {
                        // d/dx (c * x * y) = c * y
                        result.Add(new LinearMonomial<T>(m.Coefficient, m.Symbol2!));
                    }
                    else if (s2Match)
                    {
                        // d/dy (c * x * y) = c * x
                        result.Add(new LinearMonomial<T>(m.Coefficient, m.Symbol1));
                    }
                }
                else
                {
                    // Linear term: d/dx (c * x) = c if x matches
                    if (SymbolIdentity.StableId(m.Symbol1) == SymbolIdentity.StableId(variable))
                        constTerm = constTerm.Plus(m.Coefficient);
                }
            }

            return new LinearPolynomial<T>(result, constTerm);
        }

        /// <summary>
        /// 规范多项式对指定变量求偏导 / Differentiate canonical polynomial with respect to a variable.
        /// </summary>
        /// <returns>导数规范多项式 / Derivative canonical polynomial.</returns>
        public static CanonicalPolynomial<T> Differentiate<T>(this CanonicalPolynomial<T> p, ISymbol variable)
            where T : struct, IRing<T>, IRealNumber<T>
        {
            var constants = NumericConstantsRegistry.For<T>();
            var result = new List<CanonicalMonomial<T>>();
            var constTerm = constants.Zero;

            foreach (var m in p.Monomials)
            {
                if (!m.Powers.TryGetValue(variable, out var power) || power == 0)
                    continue;

                // d/dx (c * x^n * ...) = c*n * x^(n-1) * ...
                var newCoeff = m.Coefficient;
                for (var i = 0; i < power; i++)
                    newCoeff = newCoeff.Plus(m.Coefficient);

                var newPowers = new Dictionary<ISymbol, int>(m.Powers);
                if (power == 1)
                    newPowers.Remove(variable);
                else
                    newPowers[variable] = power - 1;

                if (newPowers.Count == 0)
                    constTerm = constTerm.Plus(newCoeff);
                else
                    result.Add(new CanonicalMonomial<T>(newCoeff, newPowers));
            }

            return new CanonicalPolynomial<T>(result, constTerm);
        }
    }
}
