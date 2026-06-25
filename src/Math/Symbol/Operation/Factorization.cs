#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using System.Collections.Generic;

namespace Fuookami.Ospf.Math.Symbol.Operation;
/// <summary>
/// 二次多项式系数 / Quadratic polynomial coefficients.
/// 提取一元二次多项式 ax^2 + bx + c 的系数。
/// Extracts coefficients of univariate quadratic polynomial ax^2 + bx + c.
/// </summary>
/// <typeparam name="T">系数类型 / Coefficient type.</typeparam>
public sealed record QuadraticCoefficients<T>(
    T A,
    T B,
    T C,
    ISymbol Symbol) where T : struct, IRing<T>;

/// <summary>
/// 线性因子 / Linear factor (x - root).
/// </summary>
/// <typeparam name="T">系数类型 / Coefficient type.</typeparam>
public sealed record LinearFactor<T>(T Root) where T : struct, IRing<T>;

/// <summary>
/// 多项式根 / Polynomial roots.
/// 一元二次方程的求根结果。
/// Result of solving a univariate quadratic equation.
/// </summary>
/// <typeparam name="T">系数类型 / Coefficient type.</typeparam>
public sealed record PolynomialRoots<T>(
    bool IsReal,
    IReadOnlyList<T> Roots) where T : struct, IRing<T>;

/// <summary>
/// 二次因式分解结果 / Quadratic factorization result.
/// </summary>
/// <typeparam name="T">系数类型 / Coefficient type.</typeparam>
public sealed record QuadraticFactorization<T>(
    T LeadingCoefficient,
    IReadOnlyList<LinearFactor<T>> Factors,
    ISymbol Symbol) where T : struct, IRing<T>;

/// <summary>
/// 因式分解运算 / Factorization operations.
/// 对一元二次多项式进行因式分解和求根。
/// Performs factorization and root-finding for univariate quadratic polynomials.
/// </summary>
public static class FactorizationOps {
    /// <summary>
    /// 求解一元二次方程 ax^2 + bx + c = 0 / Solve univariate quadratic equation ax^2 + bx + c = 0.
    /// </summary>
    public static PolynomialRoots<T> SolveQuadratic<T>(QuadraticCoefficients<T> coefficients)
        where T : struct, IRing<T>, IField<T>, IFloatingNumber<T> {
        (T a, T b, T c, ISymbol _) = coefficients;
        INumericConstants<T> constants = NumericConstantsRegistry.For<T>();
        T zero = constants.Zero;
        T two = constants.Two;
        T four = two.Plus(two);

        // Discriminant: b^2 - 4ac
        T discriminant = b.Times(b).Minus(four.Times(a).Times(c));

        if (discriminant.Ls(zero)) {
            return new PolynomialRoots<T>(false, System.Array.Empty<T>());
        }

        if (discriminant.Eq(zero)) {
            // One repeated root: -b / (2a)
            T root = b.Negate().Div(two.Times(a));
            return new PolynomialRoots<T>(true, new[] { root });
        }

        // Two real roots: (-b +/- sqrt(d)) / (2a)
        T sqrtD = discriminant.Sqrt();
        T twoA = two.Times(a);
        T root1 = b.Negate().Plus(sqrtD).Div(twoA);
        T root2 = b.Negate().Minus(sqrtD).Div(twoA);
        return new PolynomialRoots<T>(true, new[] { root1, root2 });
    }

    /// <summary>
    /// 因式分解一元二次多项式 / Factorize univariate quadratic polynomial.
    /// </summary>
    public static QuadraticFactorization<T>? FactorizeQuadratic<T>(QuadraticCoefficients<T> coefficients)
        where T : struct, IRing<T>, IField<T>, IFloatingNumber<T> {
        (T a, T b, T c, ISymbol? symbol) = coefficients;
        INumericConstants<T> constants = NumericConstantsRegistry.For<T>();
        T zero = constants.Zero;

        if (a.Eq(zero)) {
            if (b.Eq(zero)) {
                return null; // Constant, cannot factorize
            }

            T root = c.Negate().Div(b);
            return new QuadraticFactorization<T>(b, new[] { new LinearFactor<T>(root) }, symbol);
        }

        PolynomialRoots<T> roots = SolveQuadratic(coefficients);
        if (!roots.IsReal || roots.Roots.Count == 0) {
            return null;
        }

        var factors = new List<LinearFactor<T>>(roots.Roots.Count);
        foreach (T r in roots.Roots) {
            factors.Add(new LinearFactor<T>(r));
        }

        return new QuadraticFactorization<T>(a, factors, symbol);
    }

    /// <summary>
    /// 从二次多项式提取一元系数 / Extract univariate coefficients from quadratic polynomial.
    /// </summary>
    public static QuadraticCoefficients<T>? ExtractUnivariateCoefficients<T>(this QuadraticPolynomial<T> p)
        where T : struct, IRing<T>, IRealNumber<T> {
        INumericConstants<T> constants = NumericConstantsRegistry.For<T>();
        T zero = constants.Zero;
        T a = zero;
        T b = zero;
        ISymbol? symbol = null;

        foreach (QuadraticMonomial<T> m in p.Monomials) {
            if (m.IsQuadratic) {
                if (symbol is null) {
                    symbol = m.Symbol1;
                }
                else if (SymbolIdentity.StableId(m.Symbol1) != SymbolIdentity.StableId(symbol)) {
                    return null; // Multi-variable
                }

                if (m.Symbol2 is not null &&
                    SymbolIdentity.StableId(m.Symbol2) != SymbolIdentity.StableId(m.Symbol1)) {
                    return null; // Cross-term
                }

                a = a.Plus(m.Coefficient);
            }
            else {
                if (symbol is null) {
                    symbol = m.Symbol1;
                }
                else if (SymbolIdentity.StableId(m.Symbol1) != SymbolIdentity.StableId(symbol)) {
                    return null;
                }

                b = b.Plus(m.Coefficient);
            }
        }

        if (symbol is null) {
            return null;
        }

        return new QuadraticCoefficients<T>(a, b, p.Constant, symbol);
    }

    /// <summary>
    /// 展开因式分解结果为二次多项式 / Expand factorization result to quadratic polynomial.
    /// </summary>
    public static QuadraticPolynomial<T> Expand<T>(this QuadraticFactorization<T> factorization)
        where T : struct, IRing<T>, IRealNumber<T> {
        T lc = factorization.LeadingCoefficient;
        ISymbol symbol = factorization.Symbol;
        INumericConstants<T> constants = NumericConstantsRegistry.For<T>();
        T zero = constants.Zero;

        if (factorization.Factors.Count == 0) {
            return new QuadraticPolynomial<T>(System.Array.Empty<QuadraticMonomial<T>>(), lc);
        }

        if (factorization.Factors.Count == 1) {
            // a(x - r) = ax - ar
            T root = factorization.Factors[0].Root;
            var linear = QuadraticMonomial<T>.Linear(lc, symbol);
            T constant = lc.Negate().Times(root);
            return new QuadraticPolynomial<T>(new[] { linear }, constant);
        }

        if (factorization.Factors.Count == 2) {
            // a(x - r1)(x - r2) = a(x^2 - (r1+r2)x + r1*r2)
            T r1 = factorization.Factors[0].Root;
            T r2 = factorization.Factors[1].Root;

            var quad = QuadraticMonomial<T>.Quadratic(lc, symbol, symbol);
            T linearCoeff = lc.Negate().Times(r1.Plus(r2));
            var linear = QuadraticMonomial<T>.Linear(linearCoeff, symbol);
            T constant = lc.Times(r1).Times(r2);

            return new QuadraticPolynomial<T>(new[] { quad, linear }, constant);
        }

        return new QuadraticPolynomial<T>(System.Array.Empty<QuadraticMonomial<T>>(), zero);
    }
}
