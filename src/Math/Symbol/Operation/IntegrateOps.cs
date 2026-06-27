#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using System.Collections.Generic;

namespace Fuookami.Ospf.Math.Symbol.Operation;

/// <summary>
/// 积分运算（完整）/ Integration Operations (Full).
/// 支持线性、二次和规范多项式的积分计算。
/// Kotlin IntegrateOps.kt 的对应实现。
/// Supports integration for linear, quadratic, and canonical polynomials.
/// </summary>
public static class FullIntegrateOps {
    // ===== Linear -> Quadratic =====

    /// <summary>
    /// 线性单项式积分 / Integrate a linear monomial.
    /// integral(a*x) dx = a*x^2/2; integral(a*y) dx = a*y*x.
    /// </summary>
    /// <param name="m">线性单项式 / Linear monomial.</param>
    /// <param name="variable">积分变量 / Variable of integration.</param>
    /// <param name="integrationConstant">积分常数 / Integration constant.</param>
    /// <returns>积分结果（二次多项式）/ Integral result (quadratic polynomial).</returns>
    public static QuadraticPolynomial<T> IntegrateLinearMonomial<T>(
        this LinearMonomial<T> m,
        ISymbol variable,
        T integrationConstant)
        where T : struct, IRing<T>, IField<T>, IRealNumber<T> {
        INumericConstants<T> constants = NumericConstantsRegistry.For<T>();
        T two = constants.Two;

        if (SymbolIdentity.StableId(m.Symbol) == SymbolIdentity.StableId(variable)) {
            // integral(a*x) dx = (a/2) * x^2
            return new QuadraticPolynomial<T>(
                new[] { QuadraticMonomial<T>.Quadratic(m.Coefficient.Div(two), variable, variable) },
                integrationConstant);
        }
        else {
            // integral(a*y) dx = a*y*x
            return new QuadraticPolynomial<T>(
                new[] { QuadraticMonomial<T>.Quadratic(m.Coefficient, m.Symbol, variable) },
                integrationConstant);
        }
    }

    /// <summary>
    /// 线性多项式积分 / Integrate a linear polynomial.
    /// </summary>
    /// <param name="p">线性多项式 / Linear polynomial.</param>
    /// <param name="variable">积分变量 / Variable of integration.</param>
    /// <param name="integrationConstant">积分常数 / Integration constant.</param>
    /// <returns>积分结果（二次多项式）/ Integral result (quadratic polynomial).</returns>
    public static QuadraticPolynomial<T> IntegrateLinear<T>(
        this LinearPolynomial<T> p,
        ISymbol variable,
        T integrationConstant)
        where T : struct, IRing<T>, IField<T>, IRealNumber<T> {
        INumericConstants<T> constants = NumericConstantsRegistry.For<T>();
        T zero = constants.Zero;
        T two = constants.Two;
        var result = new List<QuadraticMonomial<T>>();

        foreach (LinearMonomial<T> m in p.Monomials) {
            if (SymbolIdentity.StableId(m.Symbol) == SymbolIdentity.StableId(variable)) {
                result.Add(QuadraticMonomial<T>.Quadratic(m.Coefficient.Div(two), variable, variable));
            }
            else {
                result.Add(QuadraticMonomial<T>.Quadratic(m.Coefficient, m.Symbol, variable));
            }
        }

        // integral(constant) dx = constant * x
        if (!p.Constant.Eq(zero)) {
            result.Add(QuadraticMonomial<T>.Linear(p.Constant, variable));
        }

        return new QuadraticPolynomial<T>(result, integrationConstant);
    }

    // ===== Quadratic -> Canonical =====

    /// <summary>
    /// 二次单项式积分 / Integrate a quadratic monomial.
    /// Handles all cases: x^2, x*y, x, etc.
    /// </summary>
    /// <param name="m">二次单项式 / Quadratic monomial.</param>
    /// <param name="variable">积分变量 / Variable of integration.</param>
    /// <param name="integrationConstant">积分常数 / Integration constant.</param>
    /// <returns>积分结果（规范多项式）/ Integral result (canonical polynomial).</returns>
    public static CanonicalPolynomial<T> IntegrateQuadraticMonomial<T>(
        this QuadraticMonomial<T> m,
        ISymbol variable,
        T integrationConstant)
        where T : struct, IRing<T>, IField<T>, IRealNumber<T> {
        INumericConstants<T> constants = NumericConstantsRegistry.For<T>();
        T two = constants.Two;
        T three = constants.Three;

        ISymbol s1 = m.Symbol1;
        ISymbol? s2 = m.Symbol2;
        bool s1Match = SymbolIdentity.StableId(s1) == SymbolIdentity.StableId(variable);
        bool s2Match = s2 is not null && SymbolIdentity.StableId(s2) == SymbolIdentity.StableId(variable);

        // Case 1: s1 == s2 == variable (a * x^2)
        if (s1Match && s2Match) {
            var powers = new Dictionary<ISymbol, int> { [variable] = 3 };
            return new CanonicalPolynomial<T>(
                new[] { new CanonicalMonomial<T>(m.Coefficient.Div(three), powers) },
                integrationConstant);
        }

        // Case 2: s1 == variable, s2 == null (a * x)
        if (s1Match && s2 is null) {
            var powers = new Dictionary<ISymbol, int> { [variable] = 2 };
            return new CanonicalPolynomial<T>(
                new[] { new CanonicalMonomial<T>(m.Coefficient.Div(two), powers) },
                integrationConstant);
        }

        // Case 3: s1 == variable, s2 != variable (a * x * y)
        if (s1Match && s2 is not null && !s2Match) {
            var powers = new Dictionary<ISymbol, int> { [variable] = 2, [s2] = 1 };
            return new CanonicalPolynomial<T>(
                new[] { new CanonicalMonomial<T>(m.Coefficient.Div(two), powers) },
                integrationConstant);
        }

        // Case 4: s2 == variable, s1 != variable (a * y * x)
        if (s2Match && !s1Match) {
            var powers = new Dictionary<ISymbol, int> { [s1] = 1, [variable] = 2 };
            return new CanonicalPolynomial<T>(
                new[] { new CanonicalMonomial<T>(m.Coefficient.Div(two), powers) },
                integrationConstant);
        }

        // Case 5: neither matches (a * y * z, treat as constant wrt x)
        {
            var powers = new Dictionary<ISymbol, int> { [s1] = 1, [variable] = 1 };
            if (s2 is not null) {
                powers[s2] = powers.TryGetValue(s2, out int p2) ? p2 + 1 : 1;
            }

            return new CanonicalPolynomial<T>(
                new[] { new CanonicalMonomial<T>(m.Coefficient, powers) },
                integrationConstant);
        }
    }

    /// <summary>
    /// 二次多项式积分 / Integrate a quadratic polynomial.
    /// </summary>
    /// <param name="p">二次多项式 / Quadratic polynomial.</param>
    /// <param name="variable">积分变量 / Variable of integration.</param>
    /// <param name="integrationConstant">积分常数 / Integration constant.</param>
    /// <returns>积分结果（规范多项式）/ Integral result (canonical polynomial).</returns>
    public static CanonicalPolynomial<T> IntegrateQuadratic<T>(
        this QuadraticPolynomial<T> p,
        ISymbol variable,
        T integrationConstant)
        where T : struct, IRing<T>, IField<T>, IRealNumber<T> {
        INumericConstants<T> constants = NumericConstantsRegistry.For<T>();
        T zero = constants.Zero;
        var result = new List<CanonicalMonomial<T>>();

        foreach (QuadraticMonomial<T> m in p.Monomials) {
            CanonicalPolynomial<T> monomialIntegral = m.IntegrateQuadraticMonomial(variable, zero);
            result.AddRange(monomialIntegral.Monomials);
        }

        // integral(constant) dx = constant * x
        if (!p.Constant.Eq(zero)) {
            result.Add(new CanonicalMonomial<T>(p.Constant, new Dictionary<ISymbol, int> { [variable] = 1 }));
        }

        return new CanonicalPolynomial<T>(result, integrationConstant);
    }

    // ===== Canonical -> Canonical =====

    /// <summary>
    /// 规范单项式积分 / Integrate a canonical monomial.
    /// integral(a * x^n) dx = a * x^(n+1) / (n+1).
    /// </summary>
    /// <param name="m">规范单项式 / Canonical monomial.</param>
    /// <param name="variable">积分变量 / Variable of integration.</param>
    /// <param name="integrationConstant">积分常数 / Integration constant.</param>
    /// <returns>积分结果（规范多项式）/ Integral result (canonical polynomial).</returns>
    public static CanonicalPolynomial<T> IntegrateCanonicalMonomial<T>(
        this CanonicalMonomial<T> m,
        ISymbol variable,
        T integrationConstant)
        where T : struct, IRing<T>, IField<T>, IRealNumber<T> {
        INumericConstants<T> constants = NumericConstantsRegistry.For<T>();
        T one = constants.One;
        int currentExponent = m.Powers.TryGetValue(variable, out int exp) ? exp : 0;
        int newExponent = currentExponent + 1;

        // Build divisor (n+1) by repeated addition
        T divisor = one;
        for (int i = 1; i < newExponent; i++) {
            divisor = divisor.Plus(one);
        }

        T scaledCoefficient = m.Coefficient.Div(divisor);

        var newPowers = new Dictionary<ISymbol, int>(m.Powers) {
            [variable] = newExponent
        };

        return new CanonicalPolynomial<T>(
            new[] { new CanonicalMonomial<T>(scaledCoefficient, newPowers) },
            integrationConstant);
    }

    /// <summary>
    /// 规范多项式积分 / Integrate a canonical polynomial.
    /// </summary>
    /// <param name="p">规范多项式 / Canonical polynomial.</param>
    /// <param name="variable">积分变量 / Variable of integration.</param>
    /// <param name="integrationConstant">积分常数 / Integration constant.</param>
    /// <returns>积分结果（规范多项式）/ Integral result (canonical polynomial).</returns>
    public static CanonicalPolynomial<T> IntegrateCanonical<T>(
        this CanonicalPolynomial<T> p,
        ISymbol variable,
        T integrationConstant)
        where T : struct, IRing<T>, IField<T>, IRealNumber<T> {
        INumericConstants<T> constants = NumericConstantsRegistry.For<T>();
        T zero = constants.Zero;
        var result = new List<CanonicalMonomial<T>>();

        foreach (CanonicalMonomial<T> m in p.Monomials) {
            CanonicalPolynomial<T> monomialIntegral = m.IntegrateCanonicalMonomial(variable, zero);
            result.AddRange(monomialIntegral.Monomials);
        }

        // integral(constant) dx = constant * x
        if (!p.Constant.Eq(zero)) {
            result.Add(new CanonicalMonomial<T>(p.Constant, new Dictionary<ISymbol, int> { [variable] = 1 }));
        }

        return new CanonicalPolynomial<T>(result, integrationConstant);
    }
}
