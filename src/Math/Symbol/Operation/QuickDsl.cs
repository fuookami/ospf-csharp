#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Math.Symbol.Operation;
/// <summary>
/// 泛型快捷 DSL / Generic quick DSL.
/// 通过 Flt64ValueConverter 提供泛型多项式构造与聚合。
/// Provides generic polynomial construction and aggregation via Flt64ValueConverter.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class QuickDsl<V>
    where V : struct, INumberField<V>, IRealNumber<V> {
    private readonly IFlt64ValueConverter<V> _converter;

    /// <summary>构造函数 / Constructor.</summary>
    /// <param name="converter">Flt64 值转换器 / Flt64 value converter.</param>
    public QuickDsl(IFlt64ValueConverter<V> converter) {
        _converter = converter;
    }

    /// <summary>创建空线性多项式 / Create empty linear polynomial.</summary>
    public LinearPolynomial<V> LinearPolynomial()
        => new(Array.Empty<LinearMonomial<V>>(), _converter.Zero);

    /// <summary>从 Flt64 常数创建线性多项式 / Create linear polynomial from Flt64 constant.</summary>
    public LinearPolynomial<V> LinearPolynomial(Flt64 constant)
        => new(Array.Empty<LinearMonomial<V>>(), _converter.IntoValue(constant));

    /// <summary>从常数创建线性多项式 / Create linear polynomial from constant.</summary>
    public LinearPolynomial<V> LinearPolynomial(V constant)
        => new(Array.Empty<LinearMonomial<V>>(), constant);

    /// <summary>从单项式创建线性多项式 / Create linear polynomial from monomial.</summary>
    public LinearPolynomial<V> LinearPolynomial(LinearMonomial<V> monomial)
        => new(new[] { monomial }, _converter.Zero);

    /// <summary>从符号创建线性多项式 / Create linear polynomial from symbol.</summary>
    public LinearPolynomial<V> LinearPolynomial(ISymbol symbol)
        => new(new[] { new LinearMonomial<V>(_converter.One, symbol) }, _converter.Zero);

    /// <summary>创建可变线性多项式 / Create mutable linear polynomial.</summary>
    public MutableLinearPolynomial<V> MutableLinearPolynomial()
        => new(Array.Empty<LinearMonomial<V>>(), _converter.Zero);

    /// <summary>创建空二次多项式 / Create empty quadratic polynomial.</summary>
    public QuadraticPolynomial<V> QuadraticPolynomial()
        => new(Array.Empty<QuadraticMonomial<V>>(), _converter.Zero);

    /// <summary>从 Flt64 常数创建二次多项式 / Create quadratic polynomial from Flt64 constant.</summary>
    public QuadraticPolynomial<V> QuadraticPolynomial(Flt64 constant)
        => new(Array.Empty<QuadraticMonomial<V>>(), _converter.IntoValue(constant));

    /// <summary>从常数创建二次多项式 / Create quadratic polynomial from constant.</summary>
    public QuadraticPolynomial<V> QuadraticPolynomial(V constant)
        => new(Array.Empty<QuadraticMonomial<V>>(), constant);

    /// <summary>从单项式创建二次多项式 / Create quadratic polynomial from monomial.</summary>
    public QuadraticPolynomial<V> QuadraticPolynomial(QuadraticMonomial<V> monomial)
        => new(new[] { monomial }, _converter.Zero);

    /// <summary>从符号创建二次多项式 / Create quadratic polynomial from symbol.</summary>
    public QuadraticPolynomial<V> QuadraticPolynomial(ISymbol symbol)
        => new(new[] { QuadraticMonomial<V>.Linear(_converter.One, symbol) }, _converter.Zero);

    /// <summary>创建可变二次多项式 / Create mutable quadratic polynomial.</summary>
    public MutableQuadraticPolynomial<V> MutableQuadraticPolynomial()
        => new(Array.Empty<QuadraticMonomial<V>>(), null, _converter.Zero);

    /// <summary>对符号求和（线性）/ Sum symbols (linear).</summary>
    public LinearPolynomial<V> SumVars<E>(IEnumerable<E> items, Func<E, ISymbol?> selector) {
        var monomials = new List<LinearMonomial<V>>();
        foreach (E? item in items) {
            ISymbol? sym = selector(item);
            if (sym is not null) {
                monomials.Add(new LinearMonomial<V>(_converter.One, sym));
            }
        }
        return new LinearPolynomial<V>(monomials, _converter.Zero);
    }

    /// <summary>对符号求和（线性）/ Sum symbols (linear).</summary>
    public LinearPolynomial<V> Sum(IEnumerable<ISymbol> symbols) {
        var monomials = new List<LinearMonomial<V>>();
        foreach (ISymbol sym in symbols) {
            monomials.Add(new LinearMonomial<V>(_converter.One, sym));
        }

        return new LinearPolynomial<V>(monomials, _converter.Zero);
    }

    /// <summary>对符号求和（二次）/ Sum symbols (quadratic).</summary>
    public QuadraticPolynomial<V> QsumVars<E>(IEnumerable<E> items, Func<E, ISymbol?> selector) {
        var monomials = new List<QuadraticMonomial<V>>();
        foreach (E? item in items) {
            ISymbol? sym = selector(item);
            if (sym is not null) {
                monomials.Add(QuadraticMonomial<V>.Linear(_converter.One, sym));
            }
        }
        return new QuadraticPolynomial<V>(monomials, _converter.Zero);
    }

    /// <summary>对符号求和（二次）/ Sum symbols (quadratic).</summary>
    public QuadraticPolynomial<V> Qsum(IEnumerable<ISymbol> symbols) {
        var monomials = new List<QuadraticMonomial<V>>();
        foreach (ISymbol sym in symbols) {
            monomials.Add(QuadraticMonomial<V>.Linear(_converter.One, sym));
        }

        return new QuadraticPolynomial<V>(monomials, _converter.Zero);
    }
}

/// <summary>
/// 泛型快捷运算 / Generic quick ops.
/// 通过 Flt64ValueConverter 提供泛型算术运算。
/// Provides generic arithmetic via Flt64ValueConverter.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class QuickOps<V>
    where V : struct, INumberField<V>, IRealNumber<V> {
    private readonly IFlt64ValueConverter<V> _converter;

    /// <summary>构造函数 / Constructor.</summary>
    public QuickOps(IFlt64ValueConverter<V> converter) {
        _converter = converter;
    }

    /// <summary>Flt64 * Symbol / Flt64 times symbol.</summary>
    public LinearMonomial<V> Multiply(Flt64 lhs, ISymbol rhs)
        => new(_converter.IntoValue(lhs), rhs);

    /// <summary>Symbol * Flt64 / Symbol times Flt64.</summary>
    public LinearMonomial<V> Multiply(ISymbol lhs, Flt64 rhs)
        => new(_converter.IntoValue(rhs), lhs);

    /// <summary>int * Symbol / Int times symbol.</summary>
    public LinearMonomial<V> Multiply(int lhs, ISymbol rhs)
        => new(_converter.IntoValue(new Flt64(lhs)), rhs);

    /// <summary>int - Symbol / Int minus symbol.</summary>
    public LinearPolynomial<V> Subtract(int lhs, ISymbol rhs)
        => new(new[] { new LinearMonomial<V>(_converter.One.Negate(), rhs) },
               _converter.IntoValue(new Flt64(lhs)));

    /// <summary>int + Symbol / Int plus symbol.</summary>
    public LinearPolynomial<V> Add(int lhs, ISymbol rhs)
        => new(new[] { new LinearMonomial<V>(_converter.One, rhs) },
               _converter.IntoValue(new Flt64(lhs)));
}
