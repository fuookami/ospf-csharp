#nullable enable

using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Symbol.Monomial;

namespace Fuookami.Ospf.Math.Symbol.Polynomial
{
    /// <summary>
    /// 规范多项式 / Canonical Polynomial (linear combination of canonical monomials + constant).
    /// </summary>
    public sealed record CanonicalPolynomial<T>(
        IReadOnlyList<CanonicalMonomial<T>> Monomials,
        T Constant
    ) : IToCanonicalPolynomial<T>, ITryToLinearPolynomial<T>, ITryToQuadraticPolynomial<T>
        where T : struct, IRing<T>
    {
        /// <summary>表达式类型 / Expression category.</summary>
        public Category Category => (Monomials.Max(m => (int?)m.Degree) ?? 0) switch
        {
            0 or 1 => LinearCategory.Instance,
            2 => QuadraticCategory.Instance,
            _ => NonlinearCategory.Instance,
        };

        /// <inheritdoc/>
        public CanonicalPolynomial<T> ToCanonicalPolynomial() => this;

        /// <inheritdoc/>
        public LinearPolynomial<T>? ToLinearPolynomialOrNull()
        {
            var linearMonomials = new List<LinearMonomial<T>>();
            var canonicalConstant = Constant;
            foreach (var m in Monomials)
            {
                switch (m.Degree)
                {
                    case 0:
                        canonicalConstant = canonicalConstant.Plus(m.Coefficient);
                        break;
                    case 1:
                        var entry = m.Powers.FirstOrDefault(kv => kv.Value == 1);
                        if (entry.Key is null) return null;
                        linearMonomials.Add(new LinearMonomial<T>(m.Coefficient, entry.Key));
                        break;
                    default:
                        return null;
                }
            }
            return new LinearPolynomial<T>(linearMonomials, canonicalConstant);
        }

        /// <inheritdoc/>
        public QuadraticPolynomial<T>? ToQuadraticPolynomialOrNull()
        {
            var quadraticMonomials = new List<QuadraticMonomial<T>>();
            var canonicalConstant = Constant;
            foreach (var m in Monomials)
            {
                if (m.Degree is 0)
                {
                    canonicalConstant = canonicalConstant.Plus(m.Coefficient);
                    continue;
                }
                if (m.Degree is 1)
                {
                    var entry = m.Powers.FirstOrDefault(kv => kv.Value == 1);
                    if (entry.Key is null) return null;
                    quadraticMonomials.Add(QuadraticMonomial<T>.Linear(m.Coefficient, entry.Key));
                }
                else if (m.Degree is 2)
                {
                    if (m.Powers.Count == 1 && m.Powers.Values.First() == 2)
                    {
                        var symbol = m.Powers.Keys.First();
                        quadraticMonomials.Add(QuadraticMonomial<T>.Quadratic(m.Coefficient, symbol, symbol));
                    }
                    else if (m.Powers.Count == 2 && m.Powers.Values.All(v => v == 1))
                    {
                        var symbols = m.Powers.Keys.ToList();
                        quadraticMonomials.Add(QuadraticMonomial<T>.Quadratic(m.Coefficient, symbols[0], symbols[1]));
                    }
                    else return null;
                }
                else return null;
            }
            return new QuadraticPolynomial<T>(quadraticMonomials, canonicalConstant);
        }

        // ---- operators ----

        /// <summary>一元取反 / Unary negation.</summary>
        public static CanonicalPolynomial<T> operator -(CanonicalPolynomial<T> x)
            => new(x.Monomials.Select(m => -m).ToList(), x.Constant.Negate());

        /// <summary>多项式加单项式 / Polynomial + monomial.</summary>
        public static CanonicalPolynomial<T> operator +(CanonicalPolynomial<T> a, CanonicalMonomial<T> b)
            => new(a.Monomials.Append(b).ToList(), a.Constant);

        /// <summary>单项式加多项式 / Monomial + polynomial.</summary>
        public static CanonicalPolynomial<T> operator +(CanonicalMonomial<T> a, CanonicalPolynomial<T> b)
            => new(new[] { a }.Concat(b.Monomials).ToList(), b.Constant);

        /// <summary>多项式减单项式 / Polynomial - monomial.</summary>
        public static CanonicalPolynomial<T> operator -(CanonicalPolynomial<T> a, CanonicalMonomial<T> b)
            => new(a.Monomials.Append(-b).ToList(), a.Constant);

        /// <summary>单项式减多项式 / Monomial - polynomial.</summary>
        public static CanonicalPolynomial<T> operator -(CanonicalMonomial<T> a, CanonicalPolynomial<T> b)
            => new(new[] { a }.Concat(b.Monomials.Select(m => -m)).ToList(), b.Constant.Negate());

        /// <summary>多项式加法 / Polynomial addition.</summary>
        public static CanonicalPolynomial<T> operator +(CanonicalPolynomial<T> a, CanonicalPolynomial<T> b)
            => new(a.Monomials.Concat(b.Monomials).ToList(), a.Constant.Plus(b.Constant));

        /// <summary>多项式减法 / Polynomial subtraction.</summary>
        public static CanonicalPolynomial<T> operator -(CanonicalPolynomial<T> a, CanonicalPolynomial<T> b)
            => new(a.Monomials.Concat(b.Monomials.Select(m => -m)).ToList(), a.Constant.Minus(b.Constant));

        /// <summary>标量乘法（右）/ Scalar multiplication (right).</summary>
        public static CanonicalPolynomial<T> operator *(CanonicalPolynomial<T> x, T s)
            => new(x.Monomials.Select(m => m * s).ToList(), x.Constant.Times(s));

        /// <summary>标量乘法（左）/ Scalar multiplication (left).</summary>
        public static CanonicalPolynomial<T> operator *(T s, CanonicalPolynomial<T> x) => x * s;

        /// <summary>多项式加标量 / Polynomial + scalar.</summary>
        public static CanonicalPolynomial<T> operator +(CanonicalPolynomial<T> x, T s) => x with { Constant = x.Constant.Plus(s) };

        /// <summary>标量加多项式 / Scalar + polynomial.</summary>
        public static CanonicalPolynomial<T> operator +(T s, CanonicalPolynomial<T> x) => x with { Constant = s.Plus(x.Constant) };

        /// <summary>多项式减标量 / Polynomial - scalar.</summary>
        public static CanonicalPolynomial<T> operator -(CanonicalPolynomial<T> x, T s) => x with { Constant = x.Constant.Minus(s) };

        /// <summary>标量减多项式 / Scalar - polynomial.</summary>
        public static CanonicalPolynomial<T> operator -(T s, CanonicalPolynomial<T> x)
            => new(x.Monomials.Select(m => -m).ToList(), s.Minus(x.Constant));
    }

    /// <summary>
    /// 规范多项式扩展运算 / Canonical-polynomial operators needing tighter bounds.
    /// </summary>
    public static class CanonicalPolynomialOps
    {
        /// <summary>除以标量 / Division by scalar (requires IField).</summary>
        public static CanonicalPolynomial<T> Div<T>(this CanonicalPolynomial<T> x, T s) where T : struct, IRing<T>, IField<T>
            => new(x.Monomials.Select(m => m.Div(s)).ToList(), x.Constant.Div(s));
    }
}
