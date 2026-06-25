#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Symbol.Polynomial;

namespace Fuookami.Ospf.Math.Symbol.Inequality
{
    /// <summary>
    /// 规范不等式 / Canonical Inequality.
    /// </summary>
    public sealed record CanonicalInequality<T>(
        CanonicalPolynomial<T> Lhs,
        CanonicalPolynomial<T> Rhs,
        Comparison Comparison
    ) where T : struct, IRing<T>
    {
        /// <summary>反转不等式 / Reverses the inequality (swap sides + reverse comparison).</summary>
        public CanonicalInequality<T> Reverse()
            => new(Rhs, Lhs, Comparison.Reverse());
    }
}
