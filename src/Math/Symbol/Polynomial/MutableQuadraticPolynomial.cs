#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Math.Symbol.Polynomial
{
    /// <summary>
    /// 可变二次多项式 / Mutable Quadratic Polynomial.
    /// 增量构建用，构建完成后调用 ToQuadraticPolynomial() 转不可变。
    /// Incremental builder; freeze via ToQuadraticPolynomial().
    /// </summary>
    public sealed class MutableQuadraticPolynomial<T>
        where T : struct, INumberField<T>, IRealNumber<T>
    {
        internal readonly List<QuadraticMonomial<T>> _monomials;
        internal readonly List<LinearMonomial<T>> _linearMonomials;
        internal T _constant;

        /// <summary>初始化可变二次多项式 / Initializes mutable quadratic polynomial.</summary>
        public MutableQuadraticPolynomial(
            IEnumerable<QuadraticMonomial<T>>? monomials = null,
            IEnumerable<LinearMonomial<T>>? linearMonomials = null,
            T constant = default)
        {
            _monomials = monomials?.ToList() ?? new();
            _linearMonomials = linearMonomials?.ToList() ?? new();
            _constant = constant;
        }

        /// <summary>二次单项式列表（只读）/ Quadratic monomial list (read-only).</summary>
        public IReadOnlyList<QuadraticMonomial<T>> Monomials => _monomials;

        /// <summary>线性单项式列表（只读）/ Linear monomial list (read-only).</summary>
        public IReadOnlyList<LinearMonomial<T>> LinearMonomials => _linearMonomials;

        /// <summary>常量项 / Constant term.</summary>
        public T Constant => _constant;

        /// <summary>表达式类型 / Expression category.</summary>
        public Category Category => _monomials.Any(m => m.IsQuadratic) ? QuadraticCategory.Instance : LinearCategory.Instance;

        /// <summary>创建零多项式 / Creates zero polynomial.</summary>
        public static Result<MutableQuadraticPolynomial<TNumber>, ErrorCode, Error<ErrorCode>> Zero<TNumber>()
            where TNumber : struct, INumberField<TNumber>, IRealNumber<TNumber>
        {
            return NumericConstantsRegistry.ForOrNull<TNumber>() is { } c
                ? new Ok<MutableQuadraticPolynomial<TNumber>, ErrorCode, Error<ErrorCode>>(
                    new MutableQuadraticPolynomial<TNumber>(null, null, c.Zero))
                : new Failed<MutableQuadraticPolynomial<TNumber>, ErrorCode, Error<ErrorCode>>(
                    ErrorCode.ApplicationError,
                    $"No INumericConstants<{typeof(TNumber).Name}> registered for MutableQuadraticPolynomial.Zero");
        }

        /// <summary>创建单位多项式 / Creates one polynomial.</summary>
        public static Result<MutableQuadraticPolynomial<TNumber>, ErrorCode, Error<ErrorCode>> One<TNumber>()
            where TNumber : struct, INumberField<TNumber>, IRealNumber<TNumber>
        {
            return NumericConstantsRegistry.ForOrNull<TNumber>() is { } c
                ? new Ok<MutableQuadraticPolynomial<TNumber>, ErrorCode, Error<ErrorCode>>(
                    new MutableQuadraticPolynomial<TNumber>(null, null, c.One))
                : new Failed<MutableQuadraticPolynomial<TNumber>, ErrorCode, Error<ErrorCode>>(
                    ErrorCode.ApplicationError,
                    $"No INumericConstants<{typeof(TNumber).Name}> registered for MutableQuadraticPolynomial.One");
        }

        /// <summary>从常量创建 / Creates from a constant value.</summary>
        public static MutableQuadraticPolynomial<T> FromConstant(T value) => new(null, null, value);

        /// <summary>添加二次单项式 / Adds a quadratic monomial.</summary>
        public void AddMonomial(QuadraticMonomial<T> monomial) => _monomials.Add(monomial);

        /// <summary>添加线性单项式（提升为二次）/ Adds a linear monomial (lifted to quadratic).</summary>
        public void AddLinearMonomial(LinearMonomial<T> monomial) => _linearMonomials.Add(monomial);

        /// <summary>增加常量 / Adds to the constant term.</summary>
        public void AddConstant(T value) => _constant = _constant.Plus(value);

        /// <summary>设置常量 / Sets the constant term.</summary>
        public void SetConstant(T value) => _constant = value;

        /// <summary>清空 / Clears all monomials.</summary>
        public void Clear() { _monomials.Clear(); _linearMonomials.Clear(); }

        /// <summary>转为不可变二次多项式 / Converts to immutable quadratic polynomial.</summary>
        public QuadraticPolynomial<T> ToQuadraticPolynomial()
        {
            var all = _monomials.Concat(_linearMonomials.Select(m => QuadraticMonomial<T>.Linear(m.Coefficient, m.Symbol))).ToList();
            return new QuadraticPolynomial<T>(all, _constant);
        }

        /// <summary>转为不可变形式（别名）/ Converts to immutable form (alias).</summary>
        public QuadraticPolynomial<T> ToImmutable() => ToQuadraticPolynomial();

        // ---- mutation methods (Kotlin plusAssign/minusAssign/timesAssign/divAssign) ----

        /// <summary>乘标量 / Multiply by scalar (mutating).</summary>
        public void MultiplyBy(T s)
        {
            for (var i = 0; i < _monomials.Count; i++)
                _monomials[i] = _monomials[i] * s;
            for (var i = 0; i < _linearMonomials.Count; i++)
                _linearMonomials[i] = _linearMonomials[i] * s;
            _constant = _constant.Times(s);
        }

        /// <summary>除标量 / Divide by scalar (mutating).</summary>
        public void DivideBy(T s)
        {
            for (var i = 0; i < _monomials.Count; i++)
                _monomials[i] = _monomials[i].Div(s);
            for (var i = 0; i < _linearMonomials.Count; i++)
                _linearMonomials[i] = _linearMonomials[i].Div(s);
            _constant = _constant.Div(s);
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
            => obj is MutableQuadraticPolynomial<T> o
               && _monomials.SequenceEqual(o._monomials)
               && _linearMonomials.SequenceEqual(o._linearMonomials)
               && _constant.Equals(o._constant);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            var h = _monomials.GetHashCode();
            h = 31 * h + _linearMonomials.GetHashCode();
            return 31 * h + _constant.GetHashCode();
        }

        /// <inheritdoc/>
        public override string ToString()
            => $"MutableQuadraticPolynomial(monomials=[{string.Join(",", _monomials)}], linear=[{string.Join(",", _linearMonomials)}], constant={_constant})";
    }

    /// <summary>
    /// 可变二次多项式扩展方法 / Mutable quadratic polynomial extension methods.
    /// </summary>
    public static class MutableQuadraticPolynomialOps
    {
        /// <summary>转为可变形式 / Converts to mutable form.</summary>
        public static MutableQuadraticPolynomial<T> ToMutable<T>(this QuadraticPolynomial<T> p) where T : struct, INumberField<T>, IRealNumber<T>
            => new(p.Monomials, null, p.Constant);

        /// <summary>转为不可变形式 / Converts to immutable form.</summary>
        public static QuadraticPolynomial<T> ToImmutable<T>(this MutableQuadraticPolynomial<T> p) where T : struct, INumberField<T>, IRealNumber<T>
            => p.ToQuadraticPolynomial();
    }
}
