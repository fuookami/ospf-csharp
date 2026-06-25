#nullable enable

using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Symbol.Polynomial;

namespace Fuookami.Ospf.Math.Symbol.Monomial
{
    /// <summary>
    /// 规范单项式 / Canonical Monomial (c * x1^n1 * x2^n2 * ...).
    /// </summary>
    public sealed record CanonicalMonomial<T>(
        T Coefficient,
        IReadOnlyDictionary<ISymbol, int> Powers
    ) : IToCanonicalPolynomial<T>
        where T : struct, IRing<T>
    {
        /// <summary>从符号列表创建 / Creates from a list of factors (power = count).</summary>
        public CanonicalMonomial(T coefficient, IEnumerable<ISymbol> factors)
            : this(coefficient,
                   (IReadOnlyDictionary<ISymbol, int>)factors.GroupBy(s => s)
                       .ToDictionary(g => g.Key, g => g.Count())) { }

        /// <summary>展开的符号因子列表 / Expanded factor list (each symbol repeated by its power).</summary>
        public IReadOnlyList<ISymbol> Factors =>
            Powers.SelectMany(kv => Enumerable.Repeat(kv.Key, kv.Value)).ToList();

        /// <summary>总次数 / Total degree (sum of powers).</summary>
        public int Degree => Powers.Values.Sum();

        /// <summary>表达式类型 / Expression category.</summary>
        public Category Category => Degree switch
        {
            0 or 1 => LinearCategory.Instance,
            2 => QuadraticCategory.Instance,
            _ => NonlinearCategory.Instance,
        };

        /// <inheritdoc/>
        public CanonicalPolynomial<T> ToCanonicalPolynomial()
            => new(new[] { this }, LinearMonomial<T>.ZeroOf(Coefficient));

        /// <summary>一元取反 / Unary negation.</summary>
        public static CanonicalMonomial<T> operator -(CanonicalMonomial<T> x)
            => x with { Coefficient = x.Coefficient.Negate() };

        /// <summary>标量乘法（右）/ Scalar multiplication (right).</summary>
        public static CanonicalMonomial<T> operator *(CanonicalMonomial<T> x, T s)
            => x with { Coefficient = x.Coefficient.Times(s) };

        /// <summary>标量乘法（左）/ Scalar multiplication (left).</summary>
        public static CanonicalMonomial<T> operator *(T s, CanonicalMonomial<T> x) => x * s;
    }

    /// <summary>
    /// 规范单项式扩展运算 / Canonical-monomial operators needing tighter bounds.
    /// </summary>
    public static class CanonicalMonomialOps
    {
        /// <summary>除以标量 / Division by scalar (requires IField).</summary>
        public static CanonicalMonomial<T> Div<T>(this CanonicalMonomial<T> x, T s) where T : struct, IRing<T>, IField<T>
            => x with { Coefficient = x.Coefficient.Div(s) };
    }
}
