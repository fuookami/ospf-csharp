#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
namespace Fuookami.Ospf.Core.Model.Mechanism;

internal sealed class MechanismModelTokenPreparation<V> : IDisposable
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly AbstractMetaModel<V> _parent;
    private readonly ConcurrentMutableTokenTable<V> _source;
    private readonly MutableTokenRegistrationSnapshot<V> _snapshot;
    private bool _transferred;

    internal ConcurrentTokenTable<V> Tokens { get; }

    private MechanismModelTokenPreparation(
        AbstractMetaModel<V> parent,
        ConcurrentMutableTokenTable<V> source,
        MutableTokenRegistrationSnapshot<V> snapshot,
        ConcurrentTokenTable<V> tokens) {
        _parent = parent;
        _source = source;
        _snapshot = snapshot;
        Tokens = tokens;
    }

    internal static Result<MechanismModelTokenPreparation<V>, ErrorCode, Error<ErrorCode>> Create(
        AbstractMetaModel<V> parent,
        IReadOnlyDictionary<IVariableItem, V>? fixedVariables,
        RegistrationStatusCallBack? callBack) {
        if (parent.Tokens is not ConcurrentMutableTokenTable<V> source) {
            return Results.Failed<MechanismModelTokenPreparation<V>>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "符号表不支持构建事务 / Token table does not support build transactions"));
        }
        Result<MutableTokenRegistrationSnapshot<V>, ErrorCode, Error<ErrorCode>> started = source.BeginRegistration();
        if (started is Failed<MutableTokenRegistrationSnapshot<V>, ErrorCode, Error<ErrorCode>> failedStart) {
            return Results.Failed<MechanismModelTokenPreparation<V>>(failedStart.Error);
        }
        if (started is Fatal<MutableTokenRegistrationSnapshot<V>, ErrorCode, Error<ErrorCode>> fatalStart) {
            return new Fatal<MechanismModelTokenPreparation<V>, ErrorCode, Error<ErrorCode>>(fatalStart.Errors);
        }
        MutableTokenRegistrationSnapshot<V> snapshot = started.Value;
        ConcurrentTokenTable<V>? tokens = null;
        try {
            var symbols = snapshot.Symbols.ToList();
            var known = symbols.ToHashSet();
            var hidden = new HashSet<IIntermediateSymbol>();
            for (int i = 0; i < symbols.Count; ++i) {
                if (symbols[i] is not IIntermediateSymbol<V> symbol) {
                    continue;
                }
                foreach (IIntermediateSymbol dependency in symbol.Dependencies) {
                    if (!known.Contains(dependency) &&
                        (dependency is ILinearIntermediateSymbol<V> or IQuadraticIntermediateSymbol<V>) &&
                        dependency is not IMathFunctionSymbolBase<V> &&
                        dependency is not IQuadraticMathFunctionSymbolBase<V>) {
                        known.Add(dependency);
                        hidden.Add(dependency);
                        symbols.Add(dependency);
                    }
                }
            }
            Try registration = TokenTableRegistrationExtensions.RegisterWithHiddenDependencies(
                symbols: symbols,
                tokenTable: snapshot.Working,
                converter: parent.Converter,
                fixedValues: fixedVariables is null ? null : new FixedValues(fixedVariables),
                callBack: callBack,
                hidden: hidden
            );
            if (registration is Failed<Success, ErrorCode, Error<ErrorCode>> failed) {
                snapshot.Working.Dispose();
                return Results.Failed<MechanismModelTokenPreparation<V>>(failed.Error);
            }
            if (registration is Fatal<Success, ErrorCode, Error<ErrorCode>> fatal) {
                snapshot.Working.Dispose();
                return new Fatal<MechanismModelTokenPreparation<V>, ErrorCode, Error<ErrorCode>>(fatal.Errors);
            }
            // shortcut: 约束回调仍可修改父 token，完整隔离时重映射单元格 / Constraint callbacks can mutate parent tokens; remap cells for full isolation.
            var original = snapshot.Tokens.ToDictionary(token => token.Key);
            var prepared = snapshot.Working.TokenList.Tokens.ToDictionary(
                token => token.Key,
                token => original.TryGetValue(token.Key, out Token<V>? existing) ? existing : token
            );
            tokens = new ConcurrentTokenTable<V>(
                category: snapshot.Working.Category,
                tokenList: new TokenList<V>(prepared),
                symbols: snapshot.Working.Symbols.ToArray()
            );
            CopyCaches(snapshot.Working, tokens, symbols);
            return Results.Ok(new MechanismModelTokenPreparation<V>(
                parent: parent,
                source: source,
                snapshot: snapshot,
                tokens: tokens
            ));
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            tokens?.Dispose();
            snapshot.Working.Dispose();
            return BuildFailure<MechanismModelTokenPreparation<V>>(error);
        }
    }

    internal Try Commit(Func<Try>? beforeCommit, Func<Try> validateSource) {
        Try Finish() {
            try {
                Try? prepared = beforeCommit is null ? Results.OkInstance : beforeCommit();
                if (prepared is null) {
                    return Results.Failed<Success>(new Err<ErrorCode>(
                        ErrorCode.ApplicationError,
                        "提交前准备返回空结果 / Pre-commit preparation returned a null result"));
                }
                if (!prepared.IsOk) {
                    return prepared;
                }
                Try source = validateSource();
                return source.IsOk
                    ? _parent.CommitTokenPreparation(() => _source.CommitRegistration(_snapshot, Tokens))
                    : source;
            }
            catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
                return BuildFailure<Success>(error);
            }
        }

        // 回退身份须与辅助 token 同成败 / Fallback identities must commit or roll back with auxiliary tokens.
        return beforeCommit is not null && _parent.IdentityRegistry is { } registry
            ? registry.ResolveAtomically(Finish)
            : Finish();
    }

    internal void TransferOwnership() => _transferred = true;

    public void Dispose() {
        if (!_transferred) {
            Tokens.Dispose();
        }
        _snapshot.Working.Dispose();
    }

    internal static Result<T, ErrorCode, Error<ErrorCode>> BuildFailure<T>(Exception error) =>
        Results.Failed<T>(new Err<ErrorCode>(
            ErrorCode.ApplicationError,
            $"机制模型构建失败：{error.Message} / Mechanism model construction failed: {error.Message}"));

    private static void CopyCaches(
        ConcurrentMutableTokenTable<V> source,
        ConcurrentTokenTable<V> target,
        IEnumerable<IIntermediateSymbol> symbols) {
        foreach (IIntermediateSymbol symbol in symbols) {
            if (source.CachedValue(symbol, (IReadOnlyList<V>?)null) is { } value) {
                target.Cache(
                    cacheKey: symbol,
                    solution: (IReadOnlyList<V>?)null,
                    value: value
                );
            }
            if (source.CachedLinearFlatten(symbol)) {
                target.CacheLinearFlatten(symbol, source.CachedLinearFlattenValue(symbol));
            }
            if (source.CachedQuadraticFlatten(symbol)) {
                target.CacheQuadraticFlatten(symbol, source.CachedQuadraticFlattenValue(symbol));
            }
            if (source.CachedRangeValue(symbol, (IReadOnlyList<V>?)null) is { } range) {
                target.CacheRange(
                    cacheKey: symbol,
                    solution: (IReadOnlyList<V>?)null,
                    range: range
                );
            }
        }
    }

    private sealed class FixedValues : IReadOnlyDictionary<ISymbol, V> {
        private readonly IReadOnlyDictionary<IVariableItem, V> _values;

        internal FixedValues(IReadOnlyDictionary<IVariableItem, V> values) {
            _values = values;
        }

        public int Count => _values.Count;
        public IEnumerable<ISymbol> Keys => _values.Keys;
        public IEnumerable<V> Values => _values.Values;
        public V this[ISymbol key] => key is IVariableItem variable
            ? _values[variable]
            : throw new KeyNotFoundException();
        public bool ContainsKey(ISymbol key) => TryGetValue(key, out _);
        public bool TryGetValue(ISymbol key, out V value) {
            if (key is IVariableItem variable && _values.TryGetValue(variable, out value)) {
                return true;
            }
            value = default;
            return false;
        }
        public IEnumerator<KeyValuePair<ISymbol, V>> GetEnumerator() => _values
            .Select(pair => new KeyValuePair<ISymbol, V>(pair.Key, pair.Value)).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
