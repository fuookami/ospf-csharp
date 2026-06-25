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
/// 可变线性多项式 / Mutable Linear Polynomial.
/// 增量构建用，构建完成后调用 ToLinearPolynomial() 转不可变。
/// Incremental builder; freeze via ToLinearPolynomial().
/// </summary>
public sealed class MutableLinearPolynomial<T>
    where T : struct, INumberField<T>, IRealNumber<T> {
    internal readonly List<LinearMonomial<T>> _monomials;
    internal T _constant;

    /// <summary>初始化可变线性多项式 / Initializes mutable linear polynomial.</summary>
    public MutableLinearPolynomial(IEnumerable<LinearMonomial<T>>? monomials = null, T constant = default) {
        _monomials = monomials?.ToList() ?? new();
        _constant = constant;
    }

    /// <summary>单项式列表（只读）/ Monomial list (read-only).</summary>
    public IReadOnlyList<LinearMonomial<T>> Monomials => _monomials;

    /// <summary>常量项 / Constant term.</summary>
    public T Constant => _constant;

    /// <summary>表达式类型 / Expression category.</summary>
    public Category Category => LinearCategory.Instance;

    /// <summary>创建零多项式 / Creates zero polynomial.</summary>
    public static Result<MutableLinearPolynomial<TNumber>, ErrorCode, Error<ErrorCode>> Zero<TNumber>()
        where TNumber : struct, INumberField<TNumber>, IRealNumber<TNumber> {
        return NumericConstantsRegistry.ForOrNull<TNumber>() is { } c
            ? new Ok<MutableLinearPolynomial<TNumber>, ErrorCode, Error<ErrorCode>>(
                new MutableLinearPolynomial<TNumber>(null, c.Zero))
            : new Failed<MutableLinearPolynomial<TNumber>, ErrorCode, Error<ErrorCode>>(
                ErrorCode.ApplicationError,
                $"No INumericConstants<{typeof(TNumber).Name}> registered for MutableLinearPolynomial.Zero");
    }

    /// <summary>创建单位多项式 / Creates one polynomial.</summary>
    public static Result<MutableLinearPolynomial<TNumber>, ErrorCode, Error<ErrorCode>> One<TNumber>()
        where TNumber : struct, INumberField<TNumber>, IRealNumber<TNumber> {
        return NumericConstantsRegistry.ForOrNull<TNumber>() is { } c
            ? new Ok<MutableLinearPolynomial<TNumber>, ErrorCode, Error<ErrorCode>>(
                new MutableLinearPolynomial<TNumber>(null, c.One))
            : new Failed<MutableLinearPolynomial<TNumber>, ErrorCode, Error<ErrorCode>>(
                ErrorCode.ApplicationError,
                $"No INumericConstants<{typeof(TNumber).Name}> registered for MutableLinearPolynomial.One");
    }

    /// <summary>从常量创建 / Creates from a constant value.</summary>
    public static MutableLinearPolynomial<T> FromConstant(T value) => new(null, value);

    /// <summary>添加单项式 / Adds a monomial.</summary>
    public void AddMonomial(LinearMonomial<T> monomial) => _monomials.Add(monomial);

    /// <summary>增加常量 / Adds to the constant term.</summary>
    public void AddConstant(T value) => _constant = _constant.Plus(value);

    /// <summary>设置常量 / Sets the constant term.</summary>
    public void SetConstant(T value) => _constant = value;

    /// <summary>清空 / Clears all monomials.</summary>
    public void Clear() => _monomials.Clear();

    /// <summary>转为不可变线性多项式 / Converts to immutable linear polynomial.</summary>
    public LinearPolynomial<T> ToLinearPolynomial() => new(_monomials.ToList(), _constant);

    /// <summary>转为不可变形式（别名）/ Converts to immutable form (alias).</summary>
    public LinearPolynomial<T> ToImmutable() => ToLinearPolynomial();

    // ---- mutation methods (Kotlin plusAssign/minusAssign/timesAssign/divAssign) ----
    // C# cannot overload compound-assignment operators on classes; use instance methods instead.

    /// <summary>加不可变多项式 / Add immutable polynomial.</summary>
    public void Add(LinearPolynomial<T> p) {
        _monomials.AddRange(p.Monomials);
        _constant = _constant.Plus(p.Constant);
    }

    /// <summary>加可变多项式 / Add mutable polynomial.</summary>
    public void Add(MutableLinearPolynomial<T> p) {
        _monomials.AddRange(p._monomials);
        _constant = _constant.Plus(p._constant);
    }

    /// <summary>减不可变多项式 / Subtract immutable polynomial.</summary>
    public void Subtract(LinearPolynomial<T> p) {
        _monomials.AddRange(p.Monomials.Select(m => -m));
        _constant = _constant.Minus(p.Constant);
    }

    /// <summary>减可变多项式 / Subtract mutable polynomial.</summary>
    public void Subtract(MutableLinearPolynomial<T> p) {
        _monomials.AddRange(p._monomials.Select(m => -m));
        _constant = _constant.Minus(p._constant);
    }

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
        => obj is MutableLinearPolynomial<T> o
           && _monomials.SequenceEqual(o._monomials)
           && _constant.Equals(o._constant);

    /// <inheritdoc/>
    public override int GetHashCode() {
        int h = _monomials.GetHashCode();
        return 31 * h + _constant.GetHashCode();
    }

    /// <inheritdoc/>
    public override string ToString()
        => $"MutableLinearPolynomial(monomials=[{string.Join(",", _monomials)}], constant={_constant})";
}

/// <summary>
/// 可变线性多项式扩展方法 / Mutable linear polynomial extension methods.
/// </summary>
public static class MutableLinearPolynomialOps {
    /// <summary>转为可变形式 / Converts to mutable form.</summary>
    public static MutableLinearPolynomial<T> ToMutable<T>(this LinearPolynomial<T> p) where T : struct, INumberField<T>, IRealNumber<T>
        => new(p.Monomials, p.Constant);

    /// <summary>转为不可变形式 / Converts to immutable form.</summary>
    public static LinearPolynomial<T> ToImmutable<T>(this MutableLinearPolynomial<T> p) where T : struct, INumberField<T>, IRealNumber<T>
        => p.ToLinearPolynomial();
}
