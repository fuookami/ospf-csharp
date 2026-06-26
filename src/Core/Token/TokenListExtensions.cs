#nullable enable

using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Token;

/// <summary>
/// 自动令牌列表，自动为新变量分配索引。
/// Auto token list, automatically assigning indices to new variables.
/// </summary>
/// <typeparam name="T">数值类型 / The number type</typeparam>
public sealed class AutoTokenList<T> : AbstractMutableTokenList<T>
    where T : struct, IRealNumber<T>, INumberField<T> {
    private readonly Dictionary<VariableItemKey, Token<T>> _list = new();
    private int _currentIndex;
    private IReadOnlyList<Token<T>>? _tokensInSolver;

    /// <inheritdoc/>
    public override ICollection<Token<T>> Tokens => _list.Values;

    /// <inheritdoc/>
    public override IReadOnlyList<Token<T>> TokensInSolver =>
        _tokensInSolver ??= Tokens.OrderBy(t => t.SolverIndex).ToList();

    /// <summary>
    /// 按变量项查找 Token，不存在时自动创建。
    /// Find token by variable item, auto-creating if not present.
    /// </summary>
    /// <param name="item">变量项 / Variable item</param>
    /// <returns>Token 实例 / Token instance</returns>
    public override Token<T>? Find(IVariableItem item) {
        VariableItemKey key = item.Key;
        if (!_list.ContainsKey(key)) {
            var token = new Token<T>(item, _currentIndex++, new Dictionary<object, Action<bool>>());
            _list[key] = token;
            _tokensInSolver = null;
        }
        return _list[key];
    }

    /// <inheritdoc/>
    public override void Remove(IVariableItem item) {
        _list.Remove(item.Key);
        _tokensInSolver = null;
    }

    /// <inheritdoc/>
    public override Try Add(IVariableItem item) {
        Find(item);
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <inheritdoc/>
    public override Try Add(IEnumerable<IVariableItem> items) {
        foreach (IVariableItem item in items) {
            Try result = Add(item);
            if (result is Failed<Success, ErrorCode, Error<ErrorCode>> f) {
                return f;
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }
}

/// <summary>
/// 手动令牌列表，使用变量自身的索引。
/// Manual token list, using the variable's own index.
/// </summary>
/// <typeparam name="T">数值类型 / The number type</typeparam>
public sealed class ManualTokenList<T> : AbstractMutableTokenList<T>
    where T : struct, IRealNumber<T>, INumberField<T> {
    private readonly Dictionary<VariableItemKey, Token<T>> _list = new();
    private IReadOnlyList<Token<T>>? _tokensInSolver;

    /// <inheritdoc/>
    public override ICollection<Token<T>> Tokens => _list.Values;

    /// <inheritdoc/>
    public override IReadOnlyList<Token<T>> TokensInSolver =>
        _tokensInSolver ??= Tokens.OrderBy(t => t.SolverIndex).ToList();

    /// <summary>
    /// 按变量项查找 Token，不存在时返回 null。
    /// Find token by variable item, returning null if not present.
    /// </summary>
    /// <param name="item">变量项 / Variable item</param>
    /// <returns>Token 实例或 null / Token instance or null</returns>
    public override Token<T>? Find(IVariableItem item) =>
        _list.TryGetValue(item.Key, out Token<T>? token) ? token : null;

    /// <inheritdoc/>
    public override void Remove(IVariableItem item) {
        _list.Remove(item.Key);
        _tokensInSolver = null;
    }

    /// <inheritdoc/>
    public override Try Add(IVariableItem item) {
        VariableItemKey key = item.Key;
        if (!_list.ContainsKey(key)) {
            var token = new Token<T>(item, item.Index, new Dictionary<object, Action<bool>>());
            _list[key] = token;
            _tokensInSolver = null;
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <inheritdoc/>
    public override Try Add(IEnumerable<IVariableItem> items) {
        foreach (IVariableItem item in items) {
            Try result = Add(item);
            if (result is Failed<Success, ErrorCode, Error<ErrorCode>> f) {
                return f;
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
