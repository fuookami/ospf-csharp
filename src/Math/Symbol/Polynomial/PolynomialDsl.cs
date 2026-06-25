#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Math.Symbol.Polynomial;
/// <summary>
/// 多项式快捷 DSL / Polynomial Quick DSL (Kotlin QuickDsl.kt top-level functions).
/// </summary>
public static class PolynomialDsl {
    /// <summary>从可变多项式转不可变 / Converts mutable polynomial to immutable.</summary>
    public static LinearPolynomial<T> FromMutable<T>(MutableLinearPolynomial<T> poly) where T : struct, INumberField<T>, IRealNumber<T>
        => poly.ToLinearPolynomial();

    // ---- Linear aggregation: Sum / SumSafe / SumOrNull ----

    /// <summary>聚合线性单项式为多项式 / Aggregates linear monomials into a polynomial (throws on empty).</summary>
    public static LinearPolynomial<T> Sum<T>(IEnumerable<LinearMonomial<T>> monomials) where T : struct, IRing<T> {
        var list = monomials.ToList();
        if (list.Count == 0) {
            throw new InvalidOperationException("empty monomials");
        }

        return new LinearPolynomial<T>(list, LinearMonomial<T>.ZeroOf(list[0].Coefficient));
    }

    /// <summary>聚合线性多项式 / Aggregates linear polynomials (throws on empty).</summary>
    public static LinearPolynomial<T> Sum<T>(IEnumerable<LinearPolynomial<T>> polynomials) where T : struct, IRing<T> {
        var monomials = new List<LinearMonomial<T>>();
        T constant = default;
        bool hasConst = false;
        foreach (LinearPolynomial<T> p in polynomials) {
            monomials.AddRange(p.Monomials);
            constant = hasConst ? constant.Plus(p.Constant) : p.Constant;
            hasConst = true;
        }
        if (!hasConst) {
            throw new InvalidOperationException("empty polynomials");
        }

        return new LinearPolynomial<T>(monomials, constant);
    }

    /// <summary>带选择器的聚合 / Aggregation with selector (throws on empty).</summary>
    public static LinearPolynomial<T> Sum<T, E>(IEnumerable<E> elements, Func<E, LinearMonomial<T>> selector) where T : struct, IRing<T> {
        var monomials = new List<LinearMonomial<T>>();
        T constant = default;
        bool hasConst = false;
        foreach (E? e in elements) {
            LinearMonomial<T> m = selector(e);
            monomials.Add(m);
            if (!hasConst) { constant = LinearMonomial<T>.ZeroOf(m.Coefficient); hasConst = true; }
        }
        if (!hasConst) {
            throw new InvalidOperationException("empty elements");
        }

        return new LinearPolynomial<T>(monomials, constant);
    }

    /// <summary>聚合多项式（带选择器）/ Aggregates polynomials with selector.</summary>
    public static LinearPolynomial<T> SumPolynomials<T, E>(IEnumerable<E> elements, Func<E, LinearPolynomial<T>> selector) where T : struct, IRing<T>
        => Sum(elements.Select(selector));

    /// <summary>展平聚合 / Flat aggregation with selector.</summary>
    public static LinearPolynomial<T> FlatSum<T, E>(IEnumerable<E> elements, Func<E, IEnumerable<LinearMonomial<T>>> selector) where T : struct, IRing<T>
        => Sum(elements.SelectMany(selector));

    /// <summary>安全聚合（返回 Result）/ Safe aggregation (returns Result).</summary>
    public static Result<LinearPolynomial<T>, ErrorCode, Error<ErrorCode>> SumSafe<T, E>(
        IEnumerable<E> items, Func<E, LinearMonomial<T>?> transform) where T : struct, IRing<T> {
        var list = items.Select(transform).Where(m => m is not null).Cast<LinearMonomial<T>>().ToList();
        return list.Count == 0
            ? new Failed<LinearPolynomial<T>, ErrorCode, Error<ErrorCode>>(ErrorCode.DataEmpty, "Empty linear monomial list; cannot infer zero value.")
            : new Ok<LinearPolynomial<T>, ErrorCode, Error<ErrorCode>>(Sum(list));
    }

    /// <summary>聚合返回 nullable / Aggregation returning nullable.</summary>
    public static LinearPolynomial<T>? SumOrNull<T, E>(IEnumerable<E> items, Func<E, LinearMonomial<T>?> transform) where T : struct, IRing<T> {
        var list = items.Select(transform).Where(m => m is not null).Cast<LinearMonomial<T>>().ToList();
        return list.Count == 0 ? null : Sum(list);
    }

    /// <summary>展平安全聚合 / Flat safe aggregation.</summary>
    public static Result<LinearPolynomial<T>, ErrorCode, Error<ErrorCode>> FlatSumSafe<T, E>(
        IEnumerable<E> list, Func<E, IEnumerable<LinearMonomial<T>?>> transform) where T : struct, IRing<T> {
        var monomials = list.SelectMany(transform).Where(m => m is not null).Cast<LinearMonomial<T>>().ToList();
        return monomials.Count == 0
            ? new Failed<LinearPolynomial<T>, ErrorCode, Error<ErrorCode>>(ErrorCode.DataEmpty, "Empty linear monomial list; cannot infer zero value.")
            : new Ok<LinearPolynomial<T>, ErrorCode, Error<ErrorCode>>(Sum(monomials));
    }

    /// <summary>展平聚合返回 nullable / Flat aggregation returning nullable.</summary>
    public static LinearPolynomial<T>? FlatSumOrNull<T, E>(
        IEnumerable<E> list, Func<E, IEnumerable<LinearMonomial<T>?>> transform) where T : struct, IRing<T> {
        var monomials = list.SelectMany(transform).Where(m => m is not null).Cast<LinearMonomial<T>>().ToList();
        return monomials.Count == 0 ? null : Sum(monomials);
    }

    // ---- Quadratic aggregation ----

    /// <summary>聚合二次单项式为多项式 / Aggregates quadratic monomials (throws on empty).</summary>
    public static QuadraticPolynomial<T> QSum<T>(IEnumerable<QuadraticMonomial<T>> monomials) where T : struct, IRing<T> {
        var list = monomials.ToList();
        if (list.Count == 0) {
            throw new InvalidOperationException("empty monomials");
        }

        return new QuadraticPolynomial<T>(list, LinearMonomial<T>.ZeroOf(list[0].Coefficient));
    }

    /// <summary>安全二次聚合 / Safe quadratic aggregation.</summary>
    public static Result<QuadraticPolynomial<T>, ErrorCode, Error<ErrorCode>> QSumSafe<T, E>(
        IEnumerable<E> items, Func<E, QuadraticMonomial<T>?> transform) where T : struct, IRing<T> {
        var list = items.Select(transform).Where(m => m is not null).Cast<QuadraticMonomial<T>>().ToList();
        return list.Count == 0
            ? new Failed<QuadraticPolynomial<T>, ErrorCode, Error<ErrorCode>>(ErrorCode.DataEmpty, "Empty quadratic monomial list; cannot infer zero value.")
            : new Ok<QuadraticPolynomial<T>, ErrorCode, Error<ErrorCode>>(QSum(list));
    }

    /// <summary>二次聚合返回 nullable / Quadratic aggregation returning nullable.</summary>
    public static QuadraticPolynomial<T>? QSumOrNull<T, E>(
        IEnumerable<E> items, Func<E, QuadraticMonomial<T>?> transform) where T : struct, IRing<T> {
        var list = items.Select(transform).Where(m => m is not null).Cast<QuadraticMonomial<T>>().ToList();
        return list.Count == 0 ? null : QSum(list);
    }
}
