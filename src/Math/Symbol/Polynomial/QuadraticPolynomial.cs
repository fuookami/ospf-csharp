#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Symbol.Monomial;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Math.Symbol.Polynomial;
/// <summary>
/// 二次多项式 / Quadratic Polynomial (sum(ci*xi*xj) + sum(ci*xi) + b).
/// </summary>
public sealed record QuadraticPolynomial<T>(
    IReadOnlyList<QuadraticMonomial<T>> Monomials,
    T Constant
) : IToQuadraticPolynomial<T>, IToCanonicalPolynomial<T>, ITryToLinearPolynomial<T>
    where T : struct, IRing<T> {
    /// <summary>表达式类型 / Expression category.</summary>
    public Category Category
        => Monomials.Any(m => m.IsQuadratic) ? QuadraticCategory.Instance : LinearCategory.Instance;

    /// <inheritdoc/>
    public QuadraticPolynomial<T> ToQuadraticPolynomial() => this;

    /// <inheritdoc/>
    public CanonicalPolynomial<T> ToCanonicalPolynomial()
        => new(Monomials.Select(m => {
            var powers = new Dictionary<ISymbol, int>();
            powers[m.Symbol1] = powers.TryGetValue(m.Symbol1, out int p1) ? p1 + 1 : 1;
            if (m.Symbol2 is not null) {
                powers[m.Symbol2] = powers.TryGetValue(m.Symbol2, out int p2) ? p2 + 1 : 1;
            }

            return new CanonicalMonomial<T>(m.Coefficient, (IReadOnlyDictionary<ISymbol, int>)powers);
        }).ToList(), Constant);

    /// <inheritdoc/>
    public LinearPolynomial<T>? ToLinearPolynomialOrNull() {
        if (Monomials.Any(m => m.IsQuadratic)) {
            return null;
        }

        return new LinearPolynomial<T>(
            Monomials.Select(m => new LinearMonomial<T>(m.Coefficient, m.Symbol1)).ToList(),
            Constant);
    }

    // ---- operators ----

    /// <summary>一元取反 / Unary negation.</summary>
    public static QuadraticPolynomial<T> operator -(QuadraticPolynomial<T> x)
        => new(x.Monomials.Select(m => new QuadraticMonomial<T>(m.Coefficient.Negate(), m.Symbol1, m.Symbol2)).ToList(),
               x.Constant.Negate());

    /// <summary>多项式加法 / Polynomial addition.</summary>
    public static QuadraticPolynomial<T> operator +(QuadraticPolynomial<T> a, QuadraticPolynomial<T> b)
        => new(a.Monomials.Concat(b.Monomials).ToList(), a.Constant.Plus(b.Constant));

    /// <summary>多项式减法 / Polynomial subtraction.</summary>
    public static QuadraticPolynomial<T> operator -(QuadraticPolynomial<T> a, QuadraticPolynomial<T> b)
        => new(a.Monomials.Concat(b.Monomials.Select(m => new QuadraticMonomial<T>(m.Coefficient.Negate(), m.Symbol1, m.Symbol2))).ToList(),
               a.Constant.Minus(b.Constant));

    /// <summary>标量乘法（右）/ Scalar multiplication (right).</summary>
    public static QuadraticPolynomial<T> operator *(QuadraticPolynomial<T> x, T s)
        => new(x.Monomials.Select(m => new QuadraticMonomial<T>(m.Coefficient.Times(s), m.Symbol1, m.Symbol2)).ToList(),
               x.Constant.Times(s));

    /// <summary>标量乘法（左）/ Scalar multiplication (left).</summary>
    public static QuadraticPolynomial<T> operator *(T s, QuadraticPolynomial<T> x) => x * s;

    /// <summary>多项式加标量 / Polynomial + scalar.</summary>
    public static QuadraticPolynomial<T> operator +(QuadraticPolynomial<T> x, T s) => x with { Constant = x.Constant.Plus(s) };

    /// <summary>标量加多项式 / Scalar + polynomial.</summary>
    public static QuadraticPolynomial<T> operator +(T s, QuadraticPolynomial<T> x) => x with { Constant = s.Plus(x.Constant) };

    /// <summary>多项式减标量 / Polynomial - scalar.</summary>
    public static QuadraticPolynomial<T> operator -(QuadraticPolynomial<T> x, T s) => x with { Constant = x.Constant.Minus(s) };

    /// <summary>标量减多项式 / Scalar - polynomial.</summary>
    public static QuadraticPolynomial<T> operator -(T s, QuadraticPolynomial<T> x)
        => new(x.Monomials.Select(m => new QuadraticMonomial<T>(m.Coefficient.Negate(), m.Symbol1, m.Symbol2)).ToList(),
               s.Minus(x.Constant));

    /// <summary>多项式加二次单项式 / Polynomial + quadratic monomial.</summary>
    public static QuadraticPolynomial<T> operator +(QuadraticPolynomial<T> a, QuadraticMonomial<T> b)
        => new(a.Monomials.Append(b).ToList(), a.Constant);

    /// <summary>二次单项式加多项式 / Quadratic monomial + polynomial.</summary>
    public static QuadraticPolynomial<T> operator +(QuadraticMonomial<T> a, QuadraticPolynomial<T> b)
        => new(new[] { a }.Concat(b.Monomials).ToList(), b.Constant);

    /// <summary>多项式减二次单项式 / Polynomial - quadratic monomial.</summary>
    public static QuadraticPolynomial<T> operator -(QuadraticPolynomial<T> a, QuadraticMonomial<T> b)
        => new(a.Monomials.Append(-b).ToList(), a.Constant);

    /// <summary>二次单项式减多项式 / Quadratic monomial - polynomial.</summary>
    public static QuadraticPolynomial<T> operator -(QuadraticMonomial<T> a, QuadraticPolynomial<T> b)
        => new(new[] { a }.Concat(b.Monomials.Select(m => -m)).ToList(), b.Constant.Negate());

    /// <summary>多项式加线性单项式 / Polynomial + linear monomial (lift to quadratic).</summary>
    public static QuadraticPolynomial<T> operator +(QuadraticPolynomial<T> a, LinearMonomial<T> b)
        => new(a.Monomials.Append(QuadraticMonomial<T>.Linear(b.Coefficient, b.Symbol)).ToList(), a.Constant);

    /// <summary>线性单项式加多项式 / Linear monomial + polynomial (lift to quadratic).</summary>
    public static QuadraticPolynomial<T> operator +(LinearMonomial<T> a, QuadraticPolynomial<T> b)
        => new(new[] { QuadraticMonomial<T>.Linear(a.Coefficient, a.Symbol) }.Concat(b.Monomials).ToList(), b.Constant);

    /// <summary>多项式减线性单项式 / Polynomial - linear monomial.</summary>
    public static QuadraticPolynomial<T> operator -(QuadraticPolynomial<T> a, LinearMonomial<T> b)
        => new(a.Monomials.Append(-QuadraticMonomial<T>.Linear(b.Coefficient, b.Symbol)).ToList(), a.Constant);

    /// <summary>线性单项式减多项式 / Linear monomial - polynomial.</summary>
    public static QuadraticPolynomial<T> operator -(LinearMonomial<T> a, QuadraticPolynomial<T> b)
        => new(new[] { QuadraticMonomial<T>.Linear(a.Coefficient, a.Symbol) }.Concat(b.Monomials.Select(m => -m)).ToList(), b.Constant.Negate());

    /// <summary>多项式加线性多项式 / Polynomial + linear polynomial (lift to quadratic).</summary>
    public static QuadraticPolynomial<T> operator +(QuadraticPolynomial<T> a, LinearPolynomial<T> b)
        => new(a.Monomials.Concat(b.Monomials.Select(m => QuadraticMonomial<T>.Linear(m.Coefficient, m.Symbol))).ToList(),
               a.Constant.Plus(b.Constant));

    /// <summary>线性多项式加二次多项式 / Linear polynomial + quadratic polynomial.</summary>
    public static QuadraticPolynomial<T> operator +(LinearPolynomial<T> a, QuadraticPolynomial<T> b)
        => new(a.Monomials.Select(m => QuadraticMonomial<T>.Linear(m.Coefficient, m.Symbol)).Concat(b.Monomials).ToList(),
               a.Constant.Plus(b.Constant));

    /// <summary>多项式减线性多项式 / Polynomial - linear polynomial.</summary>
    public static QuadraticPolynomial<T> operator -(QuadraticPolynomial<T> a, LinearPolynomial<T> b)
        => new(a.Monomials.Concat(b.Monomials.Select(m => -QuadraticMonomial<T>.Linear(m.Coefficient, m.Symbol))).ToList(),
               a.Constant.Minus(b.Constant));

    /// <summary>线性多项式减二次多项式 / Linear polynomial - quadratic polynomial.</summary>
    public static QuadraticPolynomial<T> operator -(LinearPolynomial<T> a, QuadraticPolynomial<T> b)
        => new(a.Monomials.Select(m => QuadraticMonomial<T>.Linear(m.Coefficient, m.Symbol)).Concat(b.Monomials.Select(m => -m)).ToList(),
               a.Constant.Minus(b.Constant));
}

/// <summary>
/// 二次多项式扩展运算 / Quadratic-polynomial operators needing tighter bounds.
/// </summary>
public static class QuadraticPolynomialOps {
    /// <summary>除以标量 / Division by scalar (requires IField).</summary>
    public static QuadraticPolynomial<T> Div<T>(this QuadraticPolynomial<T> x, T s) where T : struct, IRing<T>, IField<T>
        => new(x.Monomials.Select(m => new QuadraticMonomial<T>(m.Coefficient.Div(s), m.Symbol1, m.Symbol2)).ToList(),
               x.Constant.Div(s));
}
