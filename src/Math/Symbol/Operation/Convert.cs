#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System.Collections.Generic;

namespace Fuookami.Ospf.Math.Symbol.Operation;
// ===== Conversion Error Enums =====

/// <summary>线性转换错误 / Linear demotion error reason.</summary>
public enum TryToLinearError {
    /// <summary>规范单项式非线性 / Canonical monomial is not linear.</summary>
    CanonicalMonomialIsNotLinear,
    /// <summary>二次单项式非线性 / Quadratic monomial is not linear.</summary>
    QuadraticMonomialIsNotLinear,
    /// <summary>规范多项式非线性 / Canonical polynomial is not linear.</summary>
    CanonicalPolynomialIsNotLinear,
    /// <summary>二次多项式非线性 / Quadratic polynomial is not linear.</summary>
    QuadraticPolynomialIsNotLinear,
    /// <summary>二次不等式非线性 / Quadratic inequality is not linear.</summary>
    QuadraticInequalityIsNotLinear,
    /// <summary>规范不等式非线性 / Canonical inequality is not linear.</summary>
    CanonicalInequalityIsNotLinear,
}

/// <summary>二次转换错误 / Quadratic demotion error reason.</summary>
public enum TryToQuadraticError {
    /// <summary>规范单项式非二次 / Canonical monomial is not quadratic.</summary>
    CanonicalMonomialIsNotQuadratic,
    /// <summary>规范多项式非二次 / Canonical polynomial is not quadratic.</summary>
    CanonicalPolynomialIsNotQuadratic,
    /// <summary>规范不等式非二次 / Canonical inequality is not quadratic.</summary>
    CanonicalInequalityIsNotQuadratic,
}

/// <summary>规范转换错误 / Canonical conversion error reason.</summary>
public enum TryToCanonicalError {
    /// <summary>不支持的转换 / Unsupported conversion.</summary>
    Unsupported,
}

// ===== Convert Extension Methods =====

/// <summary>
/// 转换运算扩展 / Conversion operation extensions.
/// 提供单项式和多项式类型转换的核心实现。
/// Provides core implementation for monomial and polynomial type conversions.
/// </summary>
public static class ConvertOps {
    // ===== LinearMonomial promotion =====

    /// <summary>线性单项式转二次单项式 / Convert linear monomial to quadratic monomial.</summary>
    public static QuadraticMonomial<T> ToQuadraticMonomial<T>(this LinearMonomial<T> m)
        where T : struct, IRing<T>
        => QuadraticMonomial<T>.Linear(m.Coefficient, m.Symbol);

    /// <summary>线性单项式转规范单项式 / Convert linear monomial to canonical monomial.</summary>
    public static CanonicalMonomial<T> ToCanonicalMonomial<T>(this LinearMonomial<T> m)
        where T : struct, IRing<T>
        => new(m.Coefficient, new System.Collections.Generic.Dictionary<ISymbol, int> { [m.Symbol] = 1 });

    // ===== QuadraticMonomial demotion =====

    /// <summary>尝试将二次单项式降阶为线性单项式 / Try to demote quadratic monomial to linear.</summary>
    public static Result<LinearMonomial<T>, ErrorCode, Error<ErrorCode>> TryToLinearMonomial<T>(
        this QuadraticMonomial<T> m)
        where T : struct, IRing<T> {
        if (m.IsQuadratic) {
            return new Failed<LinearMonomial<T>, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument, "Quadratic monomial is not linear.");
        }

        return new Ok<LinearMonomial<T>, ErrorCode, Error<ErrorCode>>(
            new LinearMonomial<T>(m.Coefficient, m.Symbol1));
    }

    /// <summary>二次单项式转规范单项式 / Convert quadratic monomial to canonical monomial.</summary>
    public static CanonicalMonomial<T> ToCanonicalMonomial<T>(this QuadraticMonomial<T> m)
        where T : struct, IRing<T> {
        var powers = new System.Collections.Generic.Dictionary<ISymbol, int>();
        powers[m.Symbol1] = powers.TryGetValue(m.Symbol1, out int p1) ? p1 + 1 : 1;
        if (m.Symbol2 is not null) {
            powers[m.Symbol2] = powers.TryGetValue(m.Symbol2, out int p2) ? p2 + 1 : 1;
        }

        return new CanonicalMonomial<T>(m.Coefficient, powers);
    }

    // ===== CanonicalMonomial demotion =====

    /// <summary>尝试将规范单项式降阶为线性单项式 / Try to demote canonical monomial to linear.</summary>
    public static Result<LinearMonomial<T>, ErrorCode, Error<ErrorCode>> TryToLinearMonomial<T>(
        this CanonicalMonomial<T> m)
        where T : struct, IRing<T> {
        if (m.Degree != 1) {
            return new Failed<LinearMonomial<T>, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument, "Canonical monomial is not linear.");
        }

        KeyValuePair<ISymbol, int> entry = System.Linq.Enumerable.FirstOrDefault(
            m.Powers, kv => kv.Value == 1);
        if (entry.Key is null) {
            return new Failed<LinearMonomial<T>, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument, "Canonical monomial has no single-symbol power.");
        }

        return new Ok<LinearMonomial<T>, ErrorCode, Error<ErrorCode>>(
            new LinearMonomial<T>(m.Coefficient, entry.Key));
    }

    /// <summary>尝试将规范单项式降阶为二次单项式 / Try to demote canonical monomial to quadratic.</summary>
    public static Result<QuadraticMonomial<T>, ErrorCode, Error<ErrorCode>> TryToQuadraticMonomial<T>(
        this CanonicalMonomial<T> m)
        where T : struct, IRing<T> {
        if (m.Degree > 2) {
            return new Failed<QuadraticMonomial<T>, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument, "Canonical monomial is not quadratic.");
        }

        if (m.Degree == 0) {
            return new Ok<QuadraticMonomial<T>, ErrorCode, Error<ErrorCode>>(
                QuadraticMonomial<T>.Linear(m.Coefficient, System.Linq.Enumerable.First(m.Powers).Key));
        }

        if (m.Degree == 1) {
            KeyValuePair<ISymbol, int> entry = System.Linq.Enumerable.FirstOrDefault(m.Powers, kv => kv.Value == 1);
            if (entry.Key is null) {
                return new Failed<QuadraticMonomial<T>, ErrorCode, Error<ErrorCode>>(
                    ErrorCode.IllegalArgument, "Canonical monomial has no single-symbol power.");
            }

            return new Ok<QuadraticMonomial<T>, ErrorCode, Error<ErrorCode>>(
                QuadraticMonomial<T>.Linear(m.Coefficient, entry.Key));
        }
        // Degree == 2
        if (m.Powers.Count == 1 && System.Linq.Enumerable.First(m.Powers).Value == 2) {
            ISymbol symbol = System.Linq.Enumerable.First(m.Powers).Key;
            return new Ok<QuadraticMonomial<T>, ErrorCode, Error<ErrorCode>>(
                QuadraticMonomial<T>.Quadratic(m.Coefficient, symbol, symbol));
        }
        if (m.Powers.Count == 2 && System.Linq.Enumerable.All(m.Powers, kv => kv.Value == 1)) {
            var symbols = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Select(m.Powers, kv => kv.Key));
            return new Ok<QuadraticMonomial<T>, ErrorCode, Error<ErrorCode>>(
                QuadraticMonomial<T>.Quadratic(m.Coefficient, symbols[0], symbols[1]));
        }
        return new Failed<QuadraticMonomial<T>, ErrorCode, Error<ErrorCode>>(
            ErrorCode.IllegalArgument, "Canonical monomial degree-2 structure is not quadratic.");
    }

    // ===== LinearPolynomial promotion =====

    /// <summary>线性多项式转二次多项式 / Convert linear polynomial to quadratic polynomial.</summary>
    public static QuadraticPolynomial<T> ToQuadraticPolynomial<T>(this LinearPolynomial<T> p)
        where T : struct, IRing<T>
        => p.ToQuadraticPolynomial();

    /// <summary>线性多项式转规范多项式 / Convert linear polynomial to canonical polynomial.</summary>
    public static CanonicalPolynomial<T> ToCanonicalPolynomial<T>(this LinearPolynomial<T> p)
        where T : struct, IRing<T>
        => p.ToCanonicalPolynomial();

    // ===== QuadraticPolynomial demotion =====

    /// <summary>尝试将二次多项式降阶为线性多项式 / Try to demote quadratic polynomial to linear.</summary>
    public static Result<LinearPolynomial<T>, ErrorCode, Error<ErrorCode>> TryToLinearPolynomial<T>(
        this QuadraticPolynomial<T> p)
        where T : struct, IRing<T> {
        LinearPolynomial<T>? result = p.ToLinearPolynomialOrNull();
        if (result is null) {
            return new Failed<LinearPolynomial<T>, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument, "Quadratic polynomial contains quadratic terms.");
        }

        return new Ok<LinearPolynomial<T>, ErrorCode, Error<ErrorCode>>(result);
    }

    /// <summary>二次多项式转规范多项式 / Convert quadratic polynomial to canonical polynomial.</summary>
    public static CanonicalPolynomial<T> ToCanonicalPolynomial<T>(this QuadraticPolynomial<T> p)
        where T : struct, IRing<T>
        => p.ToCanonicalPolynomial();

    // ===== CanonicalPolynomial demotion =====

    /// <summary>尝试将规范多项式降阶为线性多项式 / Try to demote canonical polynomial to linear.</summary>
    public static Result<LinearPolynomial<T>, ErrorCode, Error<ErrorCode>> TryToLinearPolynomial<T>(
        this CanonicalPolynomial<T> p)
        where T : struct, IRing<T> {
        LinearPolynomial<T>? result = p.ToLinearPolynomialOrNull();
        if (result is null) {
            return new Failed<LinearPolynomial<T>, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument, "Canonical polynomial is not linear.");
        }

        return new Ok<LinearPolynomial<T>, ErrorCode, Error<ErrorCode>>(result);
    }

    /// <summary>尝试将规范多项式降阶为二次多项式 / Try to demote canonical polynomial to quadratic.</summary>
    public static Result<QuadraticPolynomial<T>, ErrorCode, Error<ErrorCode>> TryToQuadraticPolynomial<T>(
        this CanonicalPolynomial<T> p)
        where T : struct, IRing<T> {
        QuadraticPolynomial<T>? result = p.ToQuadraticPolynomialOrNull();
        if (result is null) {
            return new Failed<QuadraticPolynomial<T>, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument, "Canonical polynomial is not quadratic.");
        }

        return new Ok<QuadraticPolynomial<T>, ErrorCode, Error<ErrorCode>>(result);
    }

    // ===== Inequality conversions =====

    /// <summary>尝试将二次不等式降阶为线性不等式 / Try to demote quadratic inequality to linear.</summary>
    public static Result<LinearInequality<T>, ErrorCode, Error<ErrorCode>> TryToLinearInequality<T>(
        this QuadraticInequalityOf<T> ineq)
        where T : struct, IRing<T> {
        Result<LinearPolynomial<T>, ErrorCode, Error<ErrorCode>> lhs = ineq.Lhs.TryToLinearPolynomial();
        Result<LinearPolynomial<T>, ErrorCode, Error<ErrorCode>> rhs = ineq.Rhs.TryToLinearPolynomial();
        if (lhs.IsFailed || rhs.IsFailed) {
            return new Failed<LinearInequality<T>, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument, "Quadratic inequality is not linear.");
        }

        return new Ok<LinearInequality<T>, ErrorCode, Error<ErrorCode>>(
            new LinearInequality<T>(lhs.Value, rhs.Value, ineq.Comparison));
    }

    /// <summary>尝试将规范不等式降阶为线性不等式 / Try to demote canonical inequality to linear.</summary>
    public static Result<LinearInequality<T>, ErrorCode, Error<ErrorCode>> TryToLinearInequality<T>(
        this CanonicalInequality<T> ineq)
        where T : struct, IRing<T> {
        Result<LinearPolynomial<T>, ErrorCode, Error<ErrorCode>> lhs = ineq.Lhs.TryToLinearPolynomial();
        Result<LinearPolynomial<T>, ErrorCode, Error<ErrorCode>> rhs = ineq.Rhs.TryToLinearPolynomial();
        if (lhs.IsFailed || rhs.IsFailed) {
            return new Failed<LinearInequality<T>, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument, "Canonical inequality is not linear.");
        }

        return new Ok<LinearInequality<T>, ErrorCode, Error<ErrorCode>>(
            new LinearInequality<T>(lhs.Value, rhs.Value, ineq.Comparison));
    }

    /// <summary>尝试将规范不等式降阶为二次不等式 / Try to demote canonical inequality to quadratic.</summary>
    public static Result<QuadraticInequalityOf<T>, ErrorCode, Error<ErrorCode>> TryToQuadraticInequality<T>(
        this CanonicalInequality<T> ineq)
        where T : struct, IRing<T> {
        Result<QuadraticPolynomial<T>, ErrorCode, Error<ErrorCode>> lhs = ineq.Lhs.TryToQuadraticPolynomial();
        Result<QuadraticPolynomial<T>, ErrorCode, Error<ErrorCode>> rhs = ineq.Rhs.TryToQuadraticPolynomial();
        if (lhs.IsFailed || rhs.IsFailed) {
            return new Failed<QuadraticInequalityOf<T>, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument, "Canonical inequality is not quadratic.");
        }

        return new Ok<QuadraticInequalityOf<T>, ErrorCode, Error<ErrorCode>>(
            new QuadraticInequalityOf<T>(lhs.Value, rhs.Value, ineq.Comparison));
    }
}
