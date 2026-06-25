#nullable enable

using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;

namespace Fuookami.Ospf.Math.Symbol.Operation
{
    /// <summary>
    /// 积分运算 / Integration operations.
    /// 对符号多项式关于指定变量求不定积分。
    /// Computes indefinite integrals of symbolic polynomials with respect to a given variable.
    /// </summary>
    public static class IntegrateOps
    {
        /// <summary>
        /// 线性多项式对指定变量求不定积分 / Integrate linear polynomial with respect to a variable.
        /// </summary>
        /// <param name="p">被积多项式 / Polynomial to integrate.</param>
        /// <param name="variable">积分变量 / Variable of integration.</param>
        /// <param name="integrationConstant">积分常数 / Integration constant.</param>
        /// <returns>积分后的二次多项式 / Integrated quadratic polynomial.</returns>
        public static QuadraticPolynomial<T> Integrate<T>(
            this LinearPolynomial<T> p, ISymbol variable, T integrationConstant)
            where T : struct, IRing<T>, IField<T>, IRealNumber<T>
        {
            var constants = NumericConstantsRegistry.For<T>();
            var two = constants.Two;
            var result = new List<QuadraticMonomial<T>>();

            foreach (var m in p.Monomials)
            {
                if (SymbolIdentity.StableId(m.Symbol) == SymbolIdentity.StableId(variable))
                {
                    // ∫ c*x dx = (c/2) * x^2
                    result.Add(QuadraticMonomial<T>.Quadratic(m.Coefficient.Div(two), variable, variable));
                }
                else
                {
                    // ∫ c*y dx = c*y*x (treat y as constant)
                    result.Add(QuadraticMonomial<T>.Quadratic(m.Coefficient, m.Symbol, variable));
                }
            }

            return new QuadraticPolynomial<T>(result, integrationConstant);
        }

        /// <summary>
        /// 二次多项式对指定变量求不定积分 / Integrate quadratic polynomial with respect to a variable.
        /// </summary>
        public static CanonicalPolynomial<T> Integrate<T>(
            this QuadraticPolynomial<T> p, ISymbol variable, T integrationConstant)
            where T : struct, IRing<T>, IField<T>, IRealNumber<T>
        {
            var constants = NumericConstantsRegistry.For<T>();
            var result = new List<CanonicalMonomial<T>>();

            foreach (var m in p.Monomials)
            {
                if (m.IsQuadratic)
                {
                    var s1Match = SymbolIdentity.StableId(m.Symbol1) == SymbolIdentity.StableId(variable);
                    var s2Match = SymbolIdentity.StableId(m.Symbol2!) == SymbolIdentity.StableId(variable);

                    if (s1Match && s2Match)
                    {
                        // ∫ c*x^2 dx = (c/3) * x^3
                        var three = constants.Three;
                        var powers = new Dictionary<ISymbol, int> { [variable] = 3 };
                        result.Add(new CanonicalMonomial<T>(m.Coefficient.Div(three), powers));
                    }
                    else
                    {
                        // Build canonical with integrated variable
                        var powers = new Dictionary<ISymbol, int>();
                        var otherSymbol = s1Match ? m.Symbol2! : m.Symbol1;
                        powers[otherSymbol] = powers.TryGetValue(otherSymbol, out var p1) ? p1 + 1 : 1;
                        powers[variable] = powers.TryGetValue(variable, out var pv) ? pv + 1 : 1;
                        var two = constants.Two;
                        result.Add(new CanonicalMonomial<T>(m.Coefficient.Div(two), powers));
                    }
                }
                else
                {
                    // Linear term
                    if (SymbolIdentity.StableId(m.Symbol1) == SymbolIdentity.StableId(variable))
                    {
                        // ∫ c*x dx = (c/2)*x^2
                        var two = constants.Two;
                        var powers = new Dictionary<ISymbol, int> { [variable] = 2 };
                        result.Add(new CanonicalMonomial<T>(m.Coefficient.Div(two), powers));
                    }
                    else
                    {
                        // ∫ c*y dx = c*y*x
                        var powers = new Dictionary<ISymbol, int>
                        {
                            [m.Symbol1] = 1,
                            [variable] = 1,
                        };
                        result.Add(new CanonicalMonomial<T>(m.Coefficient, powers));
                    }
                }
            }

            return new CanonicalPolynomial<T>(result, integrationConstant);
        }
    }
}
