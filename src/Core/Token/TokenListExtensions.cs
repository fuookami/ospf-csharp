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
    private readonly bool _checkTokenExists;
    private int _currentIndex;
    private IReadOnlyList<Token<T>>? _tokensInSolver;

    /// <summary>创建自动列表并选择重复键策略 / Create an auto list with a duplicate-key policy.</summary>
    /// <param name="checkTokenExists">是否拒绝重复变量 / Whether to reject duplicate variables.</param>
    public AutoTokenList(bool checkTokenExists = false) {
        _checkTokenExists = checkTokenExists;
    }

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
        if (!_list.TryGetValue(key, out Token<T>? token) && Add(item).IsOk) {
            _list.TryGetValue(key, out token);
        }
        return token;
    }

    /// <inheritdoc/>
    public override void Remove(IVariableItem item) {
        _list.Remove(item.Key);
        _tokensInSolver = null;
    }

    /// <inheritdoc/>
    public override Try Add(IVariableItem item) => Add(new[] { item });

    /// <inheritdoc/>
    public override Try Add(IEnumerable<IVariableItem> items) {
        Try result = TokenBatchSupport.Add<T>(
            items: items,
            contains: _list.ContainsKey,
            add: (key, token) => { _list[key] = token; _tokensInSolver = null; },
            captureRollback: key => {
                _list.TryGetValue(key, out Token<T>? original);
                return () => {
                    if (original is null) { _list.Remove(key); }
                    else { _list[key] = original; }
                    _tokensInSolver = null;
                };
            },
            checkTokenExists: _checkTokenExists,
            skipExisting: null,
            firstIndex: _currentIndex,
            converter: null,
            nextIndex: out int nextIndex,
            replaced: out _
        );
        if (result.IsOk) {
            _currentIndex = nextIndex;
        }
        return result;
    }
}

/// <summary>
/// 手动令牌列表，仅显式注册变量并按顺序分配索引。
/// Manual token list, registering variables explicitly and assigning indices in order.
/// </summary>
/// <typeparam name="T">数值类型 / The number type</typeparam>
public sealed class ManualTokenList<T> : AbstractMutableTokenList<T>
    where T : struct, IRealNumber<T>, INumberField<T> {
    private readonly Dictionary<VariableItemKey, Token<T>> _list = new();
    private readonly bool _checkTokenExists;
    private int _currentIndex;
    private IReadOnlyList<Token<T>>? _tokensInSolver;

    /// <summary>创建手动列表并选择重复键策略 / Create a manual list with a duplicate-key policy.</summary>
    /// <param name="checkTokenExists">是否拒绝重复变量 / Whether to reject duplicate variables.</param>
    public ManualTokenList(bool checkTokenExists = false) {
        _checkTokenExists = checkTokenExists;
    }

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
    public override Try Add(IVariableItem item) => Add(new[] { item });

    /// <inheritdoc/>
    public override Try Add(IEnumerable<IVariableItem> items) {
        Try result = TokenBatchSupport.Add<T>(
            items: items,
            contains: _list.ContainsKey,
            add: (key, token) => { _list[key] = token; _tokensInSolver = null; },
            captureRollback: key => {
                _list.TryGetValue(key, out Token<T>? original);
                return () => {
                    if (original is null) { _list.Remove(key); }
                    else { _list[key] = original; }
                    _tokensInSolver = null;
                };
            },
            checkTokenExists: _checkTokenExists,
            skipExisting: null,
            firstIndex: _currentIndex,
            converter: null,
            nextIndex: out int nextIndex,
            replaced: out _
        );
        if (result.IsOk) {
            _currentIndex = nextIndex;
        }
        return result;
    }
}
