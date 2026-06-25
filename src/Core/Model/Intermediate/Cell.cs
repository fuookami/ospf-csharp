#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Core.Model.Intermediate
{
    /// <summary>通用单元格接口 / Generic cell interface</summary>
    public interface ICell<V> where V : struct, IRealNumber<V>
    {
        /// <summary>使用当前缓存结果求值 / Evaluate using current cached results</summary>
        V? Evaluate();
        /// <summary>使用解向量求值 / Evaluate using solution vector</summary>
        V? Evaluate(IReadOnlyList<V> solution);
        /// <summary>使用变量-值映射求值 / Evaluate using variable-value map</summary>
        V? Evaluate(IReadOnlyDictionary<VariableItemKey, V> solution);
    }

    /// <summary>线性单元格接口 / Linear cell interface</summary>
    public interface ILinearCell<V> : ICell<V> where V : struct, IRealNumber<V>
    {
        /// <summary>系数 / Coefficient</summary>
        V Coefficient { get; }
        /// <summary>关联的 Token / Associated token</summary>
        Token<V> Token { get; }
    }

    /// <summary>二次单元格接口 / Quadratic cell interface</summary>
    public interface IQuadraticCell<V> : ICell<V> where V : struct, IRealNumber<V>
    {
        /// <summary>系数 / Coefficient</summary>
        V Coefficient { get; }
        /// <summary>第一个 Token / First token</summary>
        Token<V> Token1 { get; }
        /// <summary>第二个 Token（可选，null 表示平方项）/ Second token (optional; null means squared term)</summary>
        Token<V>? Token2 { get; }
    }

    /// <summary>线性单元格实现 / Linear cell implementation</summary>
    public sealed class LinearCellImpl<V> : ILinearCell<V>
        where V : struct, IRealNumber<V>, INumberField<V>
    {
        private readonly IFlt64ValueConverter<V>? _converter;
        private readonly Flt64 _coefficientFlt64;
        private readonly IAbstractTokenTable<V> _tokenTable;

        /// <inheritdoc/>
        public V Coefficient => _converter is not null ? _converter.IntoValue(_coefficientFlt64) : (V)(object)_coefficientFlt64;
        /// <inheritdoc/>
        public Token<V> Token { get; }

        public LinearCellImpl(IAbstractTokenTable<V> tokenTable, Flt64 coefficient, Token<V> token, IFlt64ValueConverter<V>? converter = null)
        {
            _tokenTable = tokenTable;
            _coefficientFlt64 = coefficient;
            Token = token;
            _converter = converter;
        }

        /// <inheritdoc/>
        public V? Evaluate() => Token.Result is { } r ? Coefficient.Times(r) : default;

        /// <inheritdoc/>
        public V? Evaluate(IReadOnlyList<V> solution)
        {
            var idx = _tokenTable.IndexOf(Token);
            return idx is { } i && i < solution.Count ? Coefficient.Times(solution[i]) : default;
        }

        /// <inheritdoc/>
        public V? Evaluate(IReadOnlyDictionary<VariableItemKey, V> solution) =>
            solution.TryGetValue(Token.Key, out var v) ? Coefficient.Times(v) : default;

        /// <inheritdoc/>
        public override string ToString() =>
            _coefficientFlt64 == Flt64.One ? Token.Name : $"{_coefficientFlt64} * {Token.Name}";
    }

    /// <summary>二次单元格实现 / Quadratic cell implementation</summary>
    public sealed class QuadraticCellImpl<V> : IQuadraticCell<V>
        where V : struct, IRealNumber<V>, INumberField<V>
    {
        private readonly IFlt64ValueConverter<V>? _converter;
        private readonly Flt64 _coefficientFlt64;
        private readonly IAbstractTokenTable<V> _tokenTable;

        /// <inheritdoc/>
        public V Coefficient => _converter is not null ? _converter.IntoValue(_coefficientFlt64) : (V)(object)_coefficientFlt64;
        /// <inheritdoc/>
        public Token<V> Token1 { get; }
        /// <inheritdoc/>
        public Token<V>? Token2 { get; }

        public QuadraticCellImpl(
            IAbstractTokenTable<V> tokenTable,
            Flt64 coefficient,
            Token<V> token1,
            Token<V>? token2 = null,
            IFlt64ValueConverter<V>? converter = null)
        {
            _tokenTable = tokenTable;
            _coefficientFlt64 = coefficient;
            Token1 = token1;
            Token2 = token2;
            _converter = converter;
        }

        /// <inheritdoc/>
        public V? Evaluate()
        {
            var r1 = Token1.Result;
            if (r1 is null) return default;
            if (Token2 is null) return Coefficient.Times(r1.Value);
            var r2 = Token2.Result;
            return r2 is not null ? Coefficient.Times(r1.Value).Times(r2.Value) : default;
        }

        /// <inheritdoc/>
        public V? Evaluate(IReadOnlyList<V> solution)
        {
            var idx1 = _tokenTable.IndexOf(Token1);
            if (idx1 is null || idx1.Value >= solution.Count) return default;
            if (Token2 is null) return Coefficient.Times(solution[idx1.Value]);
            var idx2 = _tokenTable.IndexOf(Token2);
            return idx2 is { } i2 && i2 < solution.Count
                ? Coefficient.Times(solution[idx1.Value]).Times(solution[i2])
                : default;
        }

        /// <inheritdoc/>
        public V? Evaluate(IReadOnlyDictionary<VariableItemKey, V> solution)
        {
            if (!solution.TryGetValue(Token1.Key, out var v1)) return default;
            if (Token2 is null) return Coefficient.Times(v1);
            return solution.TryGetValue(Token2.Key, out var v2) ? Coefficient.Times(v1).Times(v2) : default;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            if (Token2 is null)
                return _coefficientFlt64 == Flt64.One ? Token1.Name : $"{_coefficientFlt64} * {Token1.Name}";
            return _coefficientFlt64 == Flt64.One
                ? $"{Token1.Name} * {Token2.Name}"
                : $"{_coefficientFlt64} * {Token1.Name} * {Token2.Name}";
        }
    }
}
