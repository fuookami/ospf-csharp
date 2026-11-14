#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
namespace Fuookami.Ospf.Core.Model.Intermediate;

/// <summary>通用单元格接口 / Generic cell interface</summary>
public interface ICell<V> where V : struct, IRealNumber<V> {
    /// <summary>使用当前缓存结果求值 / Evaluate using current cached results</summary>
    V? Evaluate();
    /// <summary>使用解向量求值 / Evaluate using solution vector</summary>
    V? Evaluate(IReadOnlyList<V> solution);
    /// <summary>使用变量-值映射求值 / Evaluate using variable-value map</summary>
    V? Evaluate(IReadOnlyDictionary<VariableItemKey, V> solution);
}

/// <summary>线性单元格接口 / Linear cell interface</summary>
public interface ILinearCell<V> : ICell<V> where V : struct, IRealNumber<V> {
    /// <summary>系数 / Coefficient</summary>
    V Coefficient { get; }
    /// <summary>关联的 Token / Associated token</summary>
    Token<V> Token { get; }
}

/// <summary>二次单元格接口 / Quadratic cell interface</summary>
public interface IQuadraticCell<V> : ICell<V> where V : struct, IRealNumber<V> {
    /// <summary>系数 / Coefficient</summary>
    V Coefficient { get; }
    /// <summary>第一个 Token / First token</summary>
    Token<V> Token1 { get; }
    /// <summary>第二个 Token（可选，null 表示线性项）/ Second token (optional; null means a linear term)</summary>
    Token<V>? Token2 { get; }
}

/// <summary>线性单元格实现 / Linear cell implementation</summary>
public sealed class LinearCellImpl<V> : ILinearCell<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly IAbstractTokenTable<V> _tokenTable;

    /// <inheritdoc/>
    public V Coefficient { get; }
    /// <inheritdoc/>
    public Token<V> Token { get; }

    /// <summary>创建保留原生系数的线性单元格 / Create a linear cell retaining its native coefficient.</summary>
    /// <param name="tokenTable">令牌表 / Token table.</param>
    /// <param name="coefficient">原生数值系数 / Native numeric coefficient.</param>
    /// <param name="token">变量令牌 / Variable token.</param>
    public LinearCellImpl(
        IAbstractTokenTable<V> tokenTable,
        V coefficient,
        Token<V> token) {
        _tokenTable = tokenTable;
        Coefficient = coefficient;
        Token = token;
    }

    /// <inheritdoc/>
    public V? Evaluate() => Token.Result is { } r ? Coefficient.Times(r) : (V?)null;

    /// <inheritdoc/>
    public V? Evaluate(IReadOnlyList<V> solution) {
        int? idx = _tokenTable.IndexOf(Token);
        return idx is { } i && i >= 0 && i < solution.Count ? Coefficient.Times(solution[i]) : (V?)null;
    }

    /// <inheritdoc/>
    public V? Evaluate(IReadOnlyDictionary<VariableItemKey, V> solution) =>
        solution.TryGetValue(Token.Key, out V v) ? Coefficient.Times(v) : (V?)null;

    /// <inheritdoc/>
    public override string ToString() =>
        Coefficient.Eq(NumericConstantsRegistry.For<V>().One) ? Token.Name : $"{Coefficient} * {Token.Name}";
}

/// <summary>二次单元格实现 / Quadratic cell implementation</summary>
public sealed class QuadraticCellImpl<V> : IQuadraticCell<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly IAbstractTokenTable<V> _tokenTable;

    /// <inheritdoc/>
    public V Coefficient { get; }
    /// <inheritdoc/>
    public Token<V> Token1 { get; }
    /// <inheritdoc/>
    public Token<V>? Token2 { get; }

    /// <summary>创建保留原生系数的二次单元格 / Create a quadratic cell retaining its native coefficient.</summary>
    /// <param name="tokenTable">令牌表 / Token table.</param>
    /// <param name="coefficient">原生数值系数 / Native numeric coefficient.</param>
    /// <param name="token1">首个变量令牌 / First variable token.</param>
    /// <param name="token2">可选第二变量令牌 / Optional second variable token.</param>
    public QuadraticCellImpl(
        IAbstractTokenTable<V> tokenTable,
        V coefficient,
        Token<V> token1,
        Token<V>? token2 = null) {
        _tokenTable = tokenTable;
        Coefficient = coefficient;
        Token1 = token1;
        Token2 = token2;
    }

    /// <inheritdoc/>
    public V? Evaluate() {
        V? r1 = Token1.Result;
        if (r1 is null) {
            return default;
        }

        if (Token2 is null) {
            return Coefficient.Times(r1.Value);
        }

        V? r2 = Token2.Result;
        return r2 is not null ? Coefficient.Times(r1.Value).Times(r2.Value) : (V?)null;
    }

    /// <inheritdoc/>
    public V? Evaluate(IReadOnlyList<V> solution) {
        int? idx1 = _tokenTable.IndexOf(Token1);
        if (idx1 is null || idx1.Value < 0 || idx1.Value >= solution.Count) {
            return default;
        }

        if (Token2 is null) {
            return Coefficient.Times(solution[idx1.Value]);
        }

        int? idx2 = _tokenTable.IndexOf(Token2);
        return idx2 is { } i2 && i2 >= 0 && i2 < solution.Count
            ? Coefficient.Times(solution[idx1.Value]).Times(solution[i2])
            : (V?)null;
    }

    /// <inheritdoc/>
    public V? Evaluate(IReadOnlyDictionary<VariableItemKey, V> solution) {
        if (!solution.TryGetValue(Token1.Key, out V v1)) {
            return default;
        }

        if (Token2 is null) {
            return Coefficient.Times(v1);
        }

        return solution.TryGetValue(Token2.Key, out V v2) ? Coefficient.Times(v1).Times(v2) : (V?)null;
    }

    /// <inheritdoc/>
    public override string ToString() {
        if (Token2 is null) {
            return Coefficient.Eq(NumericConstantsRegistry.For<V>().One) ? Token1.Name : $"{Coefficient} * {Token1.Name}";
        }

        return Coefficient.Eq(NumericConstantsRegistry.For<V>().One)
            ? $"{Token1.Name} * {Token2.Name}"
            : $"{Coefficient} * {Token1.Name} * {Token2.Name}";
    }
}
