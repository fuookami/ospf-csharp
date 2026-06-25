#nullable enable

using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Symbol.Monomial;

namespace Fuookami.Ospf.Math.Symbol.Polynomial
{
    /// <summary>
    /// 线性多项式 / Linear Polynomial (c1*x1 + ... + cn*xn + b).
    /// </summary>
    public sealed record LinearPolynomial<T>(
        IReadOnlyList<LinearMonomial<T>> Monomials,
        T Constant
    ) : IToLinearPolynomial<T>, IToQuadraticPolynomial<T>, IToCanonicalPolynomial<T>
        where T : struct, IRing<T>
    {
        /// <summary>表达式类型 / Expression category.</summary>
        public Category Category => LinearCategory.Instance;

        /// <inheritdoc/>
        public LinearPolynomial<T> ToLinearPolynomial() => this;

        /// <inheritdoc/>
        public QuadraticPolynomial<T> ToQuadraticPolynomial()
            => new(Monomials.Select(m => QuadraticMonomial<T>.Linear(m.Coefficient, m.Symbol)).ToList(), Constant);

        /// <inheritdoc/>
        public CanonicalPolynomial<T> ToCanonicalPolynomial()
            => new(Monomials.Select(m => new CanonicalMonomial<T>(m.Coefficient,
                new Dictionary<ISymbol, int> { [m.Symbol] = 1 })).ToList(), Constant);

        // ---- operators ----

        /// <summary>一元取反 / Unary negation.</summary>
        public static LinearPolynomial<T> operator -(LinearPolynomial<T> x)
            => new(x.Monomials.Select(m => new LinearMonomial<T>(m.Coefficient.Negate(), m.Symbol)).ToList(),
                   x.Constant.Negate());

        /// <summary>多项式加法 / Polynomial addition.</summary>
        public static LinearPolynomial<T> operator +(LinearPolynomial<T> a, LinearPolynomial<T> b)
            => new(a.Monomials.Concat(b.Monomials).ToList(), a.Constant.Plus(b.Constant));

        /// <summary>多项式减法 / Polynomial subtraction.</summary>
        public static LinearPolynomial<T> operator -(LinearPolynomial<T> a, LinearPolynomial<T> b)
            => new(a.Monomials.Concat(b.Monomials.Select(m => new LinearMonomial<T>(m.Coefficient.Negate(), m.Symbol))).ToList(),
                   a.Constant.Minus(b.Constant));

        /// <summary>标量乘法（右）/ Scalar multiplication (right).</summary>
        public static LinearPolynomial<T> operator *(LinearPolynomial<T> x, T s)
            => new(x.Monomials.Select(m => new LinearMonomial<T>(m.Coefficient.Times(s), m.Symbol)).ToList(),
                   x.Constant.Times(s));

        /// <summary>标量乘法（左）/ Scalar multiplication (left).</summary>
        public static LinearPolynomial<T> operator *(T s, LinearPolynomial<T> x) => x * s;

        /// <summary>多项式加标量 / Polynomial + scalar.</summary>
        public static LinearPolynomial<T> operator +(LinearPolynomial<T> x, T s) => x with { Constant = x.Constant.Plus(s) };

        /// <summary>标量加多项式 / Scalar + polynomial.</summary>
        public static LinearPolynomial<T> operator +(T s, LinearPolynomial<T> x) => x with { Constant = s.Plus(x.Constant) };

        /// <summary>多项式减标量 / Polynomial - scalar.</summary>
        public static LinearPolynomial<T> operator -(LinearPolynomial<T> x, T s) => x with { Constant = x.Constant.Minus(s) };

        /// <summary>标量减多项式 / Scalar - polynomial.</summary>
        public static LinearPolynomial<T> operator -(T s, LinearPolynomial<T> x)
            => new(x.Monomials.Select(m => new LinearMonomial<T>(m.Coefficient.Negate(), m.Symbol)).ToList(),
                   s.Minus(x.Constant));

        /// <summary>多项式加单项式 / Polynomial + monomial.</summary>
        public static LinearPolynomial<T> operator +(LinearPolynomial<T> a, LinearMonomial<T> b)
            => new(a.Monomials.Append(b).ToList(), a.Constant);

        /// <summary>单项式加多项式 / Monomial + polynomial.</summary>
        public static LinearPolynomial<T> operator +(LinearMonomial<T> a, LinearPolynomial<T> b)
            => new(new[] { a }.Concat(b.Monomials).ToList(), b.Constant);

        /// <summary>多项式减单项式 / Polynomial - monomial.</summary>
        public static LinearPolynomial<T> operator -(LinearPolynomial<T> a, LinearMonomial<T> b)
            => new(a.Monomials.Append(-b).ToList(), a.Constant);

        /// <summary>单项式减多项式 / Monomial - polynomial.</summary>
        public static LinearPolynomial<T> operator -(LinearMonomial<T> a, LinearPolynomial<T> b)
            => new(new[] { a }.Concat(b.Monomials.Select(m => -m)).ToList(), b.Constant.Negate());

        /// <summary>单项式乘多项式得到二次多项式 / Monomial * polynomial = quadratic polynomial.</summary>
        public static QuadraticPolynomial<T> operator *(LinearMonomial<T> a, LinearPolynomial<T> b)
        {
            var quadraticTerms = b.Monomials.Select(m => a * m).ToList();
            quadraticTerms.Add(QuadraticMonomial<T>.Linear(a.Coefficient.Times(b.Constant), a.Symbol));
            return new QuadraticPolynomial<T>(quadraticTerms, LinearMonomial<T>.ZeroOf(a.Coefficient));
        }

        /// <summary>多项式乘单项式得到二次多项式 / Polynomial * monomial = quadratic polynomial.</summary>
        public static QuadraticPolynomial<T> operator *(LinearPolynomial<T> a, LinearMonomial<T> b)
        {
            var quadraticTerms = a.Monomials.Select(m => m * b).ToList();
            quadraticTerms.Add(QuadraticMonomial<T>.Linear(a.Constant.Times(b.Coefficient), b.Symbol));
            return new QuadraticPolynomial<T>(quadraticTerms, LinearMonomial<T>.ZeroOf(a.Constant));
        }
    }

    /// <summary>
    /// 线性多项式扩展运算 / Linear-polynomial operators needing tighter bounds.
    /// </summary>
    public static class LinearPolynomialOps
    {
        /// <summary>除以标量 / Division by scalar (requires IField).</summary>
        public static LinearPolynomial<T> Div<T>(this LinearPolynomial<T> x, T s) where T : struct, IRing<T>, IField<T>
            => new(x.Monomials.Select(m => new LinearMonomial<T>(m.Coefficient.Div(s), m.Symbol)).ToList(),
                   x.Constant.Div(s));
    }
}
