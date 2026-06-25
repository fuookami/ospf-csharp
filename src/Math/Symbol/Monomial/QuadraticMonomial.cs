#nullable enable

using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Symbol.Polynomial;

namespace Fuookami.Ospf.Math.Symbol.Monomial
{
    /// <summary>
    /// 二次单项式 / Quadratic Monomial (c*x*y or c*x^2).
    /// symbol2 == null => linear term c*symbol1; symbol1 == symbol2 => pure quadratic c*x^2.
    /// </summary>
    public sealed record QuadraticMonomial<T>(
        T Coefficient,
        ISymbol Symbol1,
        ISymbol? Symbol2 = null
    ) : IToQuadraticPolynomial<T>, IToCanonicalPolynomial<T>
        where T : struct, IRing<T>
    {
        /// <summary>是否为二次项 / Whether this is a quadratic term.</summary>
        public bool IsQuadratic => Symbol2 is not null;

        /// <summary>表达式类型 / Expression category.</summary>
        public Category Category => IsQuadratic ? QuadraticCategory.Instance : LinearCategory.Instance;

        /// <summary>创建线性项 / Creates a linear term (symbol2 == null).</summary>
        public static QuadraticMonomial<T> Linear(T coefficient, ISymbol symbol) => new(coefficient, symbol);

        /// <summary>创建二次项 / Creates a quadratic term.</summary>
        public static QuadraticMonomial<T> Quadratic(T coefficient, ISymbol s1, ISymbol s2) => new(coefficient, s1, s2);

        /// <inheritdoc/>
        public QuadraticPolynomial<T> ToQuadraticPolynomial()
            => new(new[] { this }, LinearMonomial<T>.ZeroOf(Coefficient));

        /// <inheritdoc/>
        public CanonicalPolynomial<T> ToCanonicalPolynomial()
        {
            var powers = new Dictionary<ISymbol, int>();
            powers[Symbol1] = powers.TryGetValue(Symbol1, out var p1) ? p1 + 1 : 1;
            if (Symbol2 is not null)
                powers[Symbol2] = powers.TryGetValue(Symbol2, out var p2) ? p2 + 1 : 1;
            return new CanonicalPolynomial<T>(
                new[] { new CanonicalMonomial<T>(Coefficient, (IReadOnlyDictionary<ISymbol, int>)powers) },
                LinearMonomial<T>.ZeroOf(Coefficient));
        }

        // ---- operators ----

        /// <summary>一元取反 / Unary negation.</summary>
        public static QuadraticMonomial<T> operator -(QuadraticMonomial<T> x)
            => new(x.Coefficient.Negate(), x.Symbol1, x.Symbol2);

        /// <summary>标量乘法（右）/ Scalar multiplication (right).</summary>
        public static QuadraticMonomial<T> operator *(QuadraticMonomial<T> x, T s)
            => new(x.Coefficient.Times(s), x.Symbol1, x.Symbol2);

        /// <summary>标量乘法（左）/ Scalar multiplication (left).</summary>
        public static QuadraticMonomial<T> operator *(T s, QuadraticMonomial<T> x) => x * s;

        /// <summary>二次单项式加法 / Quadratic monomial addition.</summary>
        public static QuadraticPolynomial<T> operator +(QuadraticMonomial<T> a, QuadraticMonomial<T> b)
            => new(new[] { a, b }, LinearMonomial<T>.ZeroOf(a.Coefficient));

        /// <summary>二次单项式减法 / Quadratic monomial subtraction.</summary>
        public static QuadraticPolynomial<T> operator -(QuadraticMonomial<T> a, QuadraticMonomial<T> b)
            => new(new[] { a, -b }, LinearMonomial<T>.ZeroOf(a.Coefficient));

        /// <summary>二次单项式加线性单项式 / Quadratic monomial + linear monomial.</summary>
        public static QuadraticPolynomial<T> operator +(QuadraticMonomial<T> a, LinearMonomial<T> b)
            => new(new[] { a, QuadraticMonomial<T>.Linear(b.Coefficient, b.Symbol) }, LinearMonomial<T>.ZeroOf(a.Coefficient));

        /// <summary>线性单项式加二次单项式 / Linear monomial + quadratic monomial.</summary>
        public static QuadraticPolynomial<T> operator +(LinearMonomial<T> a, QuadraticMonomial<T> b)
            => new(new[] { QuadraticMonomial<T>.Linear(a.Coefficient, a.Symbol), b }, LinearMonomial<T>.ZeroOf(a.Coefficient));

        /// <summary>二次单项式加标量 / Quadratic monomial + scalar.</summary>
        public static QuadraticPolynomial<T> operator +(QuadraticMonomial<T> a, T s)
            => new(new[] { a }, s);

        /// <summary>标量加二次单项式 / Scalar + quadratic monomial.</summary>
        public static QuadraticPolynomial<T> operator +(T s, QuadraticMonomial<T> b)
            => new(new[] { b }, s);

        /// <summary>二次单项式减线性单项式 / Quadratic monomial - linear monomial.</summary>
        public static QuadraticPolynomial<T> operator -(QuadraticMonomial<T> a, LinearMonomial<T> b)
            => new(new[] { a, -QuadraticMonomial<T>.Linear(b.Coefficient, b.Symbol) }, LinearMonomial<T>.ZeroOf(a.Coefficient));

        /// <summary>线性单项式减二次单项式 / Linear monomial - quadratic monomial.</summary>
        public static QuadraticPolynomial<T> operator -(LinearMonomial<T> a, QuadraticMonomial<T> b)
            => new(new[] { QuadraticMonomial<T>.Linear(a.Coefficient, a.Symbol), -b }, LinearMonomial<T>.ZeroOf(a.Coefficient));

        /// <summary>二次单项式减标量 / Quadratic monomial - scalar.</summary>
        public static QuadraticPolynomial<T> operator -(QuadraticMonomial<T> a, T s)
            => new(new[] { a }, s.Negate());

        /// <summary>标量减二次单项式 / Scalar - quadratic monomial.</summary>
        public static QuadraticPolynomial<T> operator -(T s, QuadraticMonomial<T> b)
            => new(new[] { -b }, s);
    }

    /// <summary>
    /// 二次单项式扩展运算 / Quadratic-monomial operators needing tighter bounds.
    /// </summary>
    public static class QuadraticMonomialOps
    {
        /// <summary>除以标量 / Division by scalar (requires IField).</summary>
        public static QuadraticMonomial<T> Div<T>(this QuadraticMonomial<T> x, T s) where T : struct, IRing<T>, IField<T>
            => new(x.Coefficient.Div(s), x.Symbol1, x.Symbol2);
    }
}
