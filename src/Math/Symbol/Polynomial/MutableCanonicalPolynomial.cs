#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Math.Symbol.Polynomial;
/// <summary>
/// 可变规范多项式 / Mutable Canonical Polynomial.
/// 增量构建用，构建完成后调用 ToCanonicalPolynomial() 转不可变。
/// Incremental builder; freeze via ToCanonicalPolynomial().
/// </summary>
public sealed class MutableCanonicalPolynomial<T>
    where T : struct, INumberField<T>, IRealNumber<T> {
    internal readonly List<CanonicalMonomial<T>> _monomials;
    internal T _constant;

    /// <summary>初始化可变规范多项式 / Initializes mutable canonical polynomial.</summary>
    public MutableCanonicalPolynomial(IEnumerable<CanonicalMonomial<T>>? monomials = null, T constant = default) {
        _monomials = monomials?.ToList() ?? new();
        _constant = constant;
    }

    /// <summary>单项式列表（只读）/ Monomial list (read-only).</summary>
    public IReadOnlyList<CanonicalMonomial<T>> Monomials => _monomials;

    /// <summary>常量项 / Constant term.</summary>
    public T Constant => _constant;

    /// <summary>表达式类型 / Expression category.</summary>
    public Category Category => (_monomials.Max(m => (int?)m.Degree) ?? 0) switch {
        0 or 1 => LinearCategory.Instance,
        2 => QuadraticCategory.Instance,
        _ => NonlinearCategory.Instance,
    };

    /// <summary>创建零多项式 / Creates zero polynomial.</summary>
    public static Result<MutableCanonicalPolynomial<TNumber>, ErrorCode, Error<ErrorCode>> Zero<TNumber>()
        where TNumber : struct, INumberField<TNumber>, IRealNumber<TNumber> {
        return NumericConstantsRegistry.ForOrNull<TNumber>() is { } c
            ? new Ok<MutableCanonicalPolynomial<TNumber>, ErrorCode, Error<ErrorCode>>(
                new MutableCanonicalPolynomial<TNumber>(null, c.Zero))
            : new Failed<MutableCanonicalPolynomial<TNumber>, ErrorCode, Error<ErrorCode>>(
                ErrorCode.ApplicationError,
                $"No INumericConstants<{typeof(TNumber).Name}> registered for MutableCanonicalPolynomial.Zero");
    }

    /// <summary>创建单位多项式 / Creates one polynomial.</summary>
    public static Result<MutableCanonicalPolynomial<TNumber>, ErrorCode, Error<ErrorCode>> One<TNumber>()
        where TNumber : struct, INumberField<TNumber>, IRealNumber<TNumber> {
        return NumericConstantsRegistry.ForOrNull<TNumber>() is { } c
            ? new Ok<MutableCanonicalPolynomial<TNumber>, ErrorCode, Error<ErrorCode>>(
                new MutableCanonicalPolynomial<TNumber>(null, c.One))
            : new Failed<MutableCanonicalPolynomial<TNumber>, ErrorCode, Error<ErrorCode>>(
                ErrorCode.ApplicationError,
                $"No INumericConstants<{typeof(TNumber).Name}> registered for MutableCanonicalPolynomial.One");
    }

    /// <summary>从常量创建 / Creates from a constant value.</summary>
    public static MutableCanonicalPolynomial<T> FromConstant(T value) => new(null, value);

    /// <summary>添加单项式 / Adds a monomial.</summary>
    public void AddMonomial(CanonicalMonomial<T> monomial) => _monomials.Add(monomial);

    /// <summary>增加常量 / Adds to the constant term.</summary>
    public void AddConstant(T value) => _constant = _constant.Plus(value);

    /// <summary>设置常量 / Sets the constant term.</summary>
    public void SetConstant(T value) => _constant = value;

    /// <summary>清空 / Clears all monomials.</summary>
    public void Clear() => _monomials.Clear();

    /// <summary>转为不可变规范多项式 / Converts to immutable canonical polynomial.</summary>
    public CanonicalPolynomial<T> ToCanonicalPolynomial() => new(_monomials.ToList(), _constant);

    /// <summary>转为不可变形式（别名）/ Converts to immutable form (alias).</summary>
    public CanonicalPolynomial<T> ToImmutable() => ToCanonicalPolynomial();

    // ---- mutation methods (Kotlin plusAssign/minusAssign/timesAssign/divAssign) ----

    /// <summary>乘标量 / Multiply by scalar (mutating).</summary>
    public void MultiplyBy(T s) {
        for (int i = 0; i < _monomials.Count; i++) {
            _monomials[i] = _monomials[i] * s;
        }

        _constant = _constant.Times(s);
    }

    /// <summary>除标量 / Divide by scalar (mutating).</summary>
    public void DivideBy(T s) {
        for (int i = 0; i < _monomials.Count; i++) {
            _monomials[i] = _monomials[i].Div(s);
        }

        _constant = _constant.Div(s);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
        => obj is MutableCanonicalPolynomial<T> o
           && _monomials.SequenceEqual(o._monomials)
           && _constant.Equals(o._constant);

    /// <inheritdoc/>
    public override int GetHashCode() {
        int h = _monomials.GetHashCode();
        return 31 * h + _constant.GetHashCode();
    }

    /// <inheritdoc/>
    public override string ToString()
        => $"MutableCanonicalPolynomial(monomials=[{string.Join(",", _monomials)}], constant={_constant})";
}

/// <summary>
/// 可变规范多项式扩展方法 / Mutable canonical polynomial extension methods.
/// </summary>
public static class MutableCanonicalPolynomialOps {
    /// <summary>转为可变形式 / Converts to mutable form.</summary>
    public static MutableCanonicalPolynomial<T> ToMutable<T>(this CanonicalPolynomial<T> p) where T : struct, INumberField<T>, IRealNumber<T>
        => new(p.Monomials, p.Constant);

    /// <summary>转为不可变形式 / Converts to immutable form.</summary>
    public static CanonicalPolynomial<T> ToImmutable<T>(this MutableCanonicalPolynomial<T> p) where T : struct, INumberField<T>, IRealNumber<T>
        => p.ToCanonicalPolynomial();
}
