#nullable enable

using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Operator;
using Fuookami.Ospf.Math.Symbol.Polynomial;

namespace Fuookami.Ospf.Math.Symbol.Monomial
{
    /// <summary>
    /// 线性单项式 / Linear Monomial (c*x).
    /// Kotlin data class LinearMonomial&lt;T : Ring&lt;T&gt;&gt;.
    /// </summary>
    public sealed record LinearMonomial<T>(T Coefficient, ISymbol Symbol)
        : IToLinearPolynomial<T>, IToQuadraticPolynomial<T>
        where T : struct, IRing<T>
    {
        /// <summary>表达式类型 / Expression category.</summary>
        public Category Category => LinearCategory.Instance;

        /// <inheritdoc/>
        public LinearPolynomial<T> ToLinearPolynomial()
            => new(new[] { this }, ZeroOf(Coefficient));

        /// <inheritdoc/>
        public QuadraticPolynomial<T> ToQuadraticPolynomial()
            => new(new[] { QuadraticMonomial<T>.Linear(Coefficient, Symbol) }, ZeroOf(Coefficient));

        // ---- operators (Kotlin operator fun) ----

        /// <summary>一元取反 / Unary negation.</summary>
        public static LinearMonomial<T> operator -(LinearMonomial<T> x)
            => new(x.Coefficient.Negate(), x.Symbol);

        /// <summary>标量乘法（右）/ Scalar multiplication (right).</summary>
        public static LinearMonomial<T> operator *(LinearMonomial<T> x, T s)
            => new(x.Coefficient.Times(s), x.Symbol);

        /// <summary>标量乘法（左）/ Scalar multiplication (left).</summary>
        public static LinearMonomial<T> operator *(T s, LinearMonomial<T> x)
            => new(s.Times(x.Coefficient), x.Symbol);

        /// <summary>线性单项式乘法得到二次单项式 / Linear monomial * linear monomial = quadratic monomial.</summary>
        public static QuadraticMonomial<T> operator *(LinearMonomial<T> a, LinearMonomial<T> b)
            => new(a.Coefficient.Times(b.Coefficient), a.Symbol, b.Symbol);

        /// <summary>线性单项式乘以线性多项式得到二次多项式 / Linear monomial * linear polynomial = quadratic polynomial.</summary>
        public static QuadraticPolynomial<T> operator *(LinearMonomial<T> a, LinearPolynomial<T> b)
        {
            var quadraticTerms = b.Monomials.Select(m => a * m).ToList();
            var linearTerm = QuadraticMonomial<T>.Linear(a.Coefficient.Times(b.Constant), a.Symbol);
            quadraticTerms.Add(linearTerm);
            return new QuadraticPolynomial<T>(quadraticTerms, ZeroOf(a.Coefficient));
        }

        /// <summary>单项式加法 / Monomial addition.</summary>
        public static LinearPolynomial<T> operator +(LinearMonomial<T> a, LinearMonomial<T> b)
            => new(new[] { a, b }, ZeroOf(a.Coefficient));

        /// <summary>单项式加多项式 / Monomial + polynomial.</summary>
        public static LinearPolynomial<T> operator +(LinearMonomial<T> a, LinearPolynomial<T> b)
            => new(new[] { a }.Concat(b.Monomials).ToList(), b.Constant);

        /// <summary>单项式加标量 / Monomial + scalar.</summary>
        public static LinearPolynomial<T> operator +(LinearMonomial<T> a, T s)
            => new(new[] { a }, s);

        /// <summary>标量加单项式 / Scalar + monomial.</summary>
        public static LinearPolynomial<T> operator +(T s, LinearMonomial<T> b)
            => new(new[] { b }, s);

        /// <summary>单项式减法 / Monomial subtraction.</summary>
        public static LinearPolynomial<T> operator -(LinearMonomial<T> a, LinearMonomial<T> b)
            => new(new[] { a, -b }, ZeroOf(a.Coefficient));

        /// <summary>单项式减多项式 / Monomial - polynomial.</summary>
        public static LinearPolynomial<T> operator -(LinearMonomial<T> a, LinearPolynomial<T> b)
            => new(new[] { a }.Concat(b.Monomials.Select(m => -m)).ToList(), b.Constant.Negate());

        /// <summary>单项式减标量 / Monomial - scalar.</summary>
        public static LinearPolynomial<T> operator -(LinearMonomial<T> a, T s)
            => new(new[] { a }, s.Negate());

        /// <summary>标量减单项式 / Scalar - monomial.</summary>
        public static LinearPolynomial<T> operator -(T s, LinearMonomial<T> b)
            => new(new[] { -b }, s);

        /// <summary>推断零值 / Infer zero value (Kotlin private zeroOf).</summary>
        internal static T ZeroOf(T value) => value.Minus(value);
    }

    /// <summary>
    /// 线性单项式扩展运算 / Linear-monomial operators needing tighter bounds.
    /// </summary>
    public static class LinearMonomialOps
    {
        /// <summary>除以标量 / Division by scalar (requires IField).</summary>
        public static LinearMonomial<T> Div<T>(this LinearMonomial<T> x, T s) where T : struct, IRing<T>, IField<T>
            => new(x.Coefficient.Div(s), x.Symbol);

        /// <summary>取绝对值 / Absolute value (requires INumberField + IAbs).</summary>
        public static LinearMonomial<T> Abs<T>(this LinearMonomial<T> x) where T : struct, INumberField<T>, IRealNumber<T>, IAbs<T>
            => new(x.Coefficient.Abs(), x.Symbol);

        /// <summary>取倒数 / Reciprocal (requires IField + ITimesGroup).</summary>
        public static CanonicalMonomial<T> Reciprocal<T>(this LinearMonomial<T> x) where T : struct, IField<T>, ITimesGroup<T>
            => new(x.Coefficient.Reciprocal(), new Dictionary<ISymbol, int> { [x.Symbol] = -1 });
    }
}
