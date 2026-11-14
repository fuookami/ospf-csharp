#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Algebra.ValueRange;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Token;
/// <summary>
/// 重复符号错误 / Repeated symbol error
/// </summary>
public sealed class RepeatedSymbolError : Exception {
    /// <summary>已存在的符号 / The repeated symbol</summary>
    public IIntermediateSymbol RepeatedSymbol { get; }
    /// <summary>新符号 / The new symbol</summary>
    public IIntermediateSymbol Symbol { get; }

    public RepeatedSymbolError(IIntermediateSymbol repeatedSymbol, IIntermediateSymbol symbol)
        : base($"Repeated \"{symbol.Name}\", old: {repeatedSymbol}, new: {symbol}.") {
        RepeatedSymbol = repeatedSymbol;
        Symbol = symbol;
    }
}

/// <summary>
/// Token 表抽象接口 / Abstract interface for token tables
/// </summary>
/// <typeparam name="V">数值类型 / The number type</typeparam>
public interface IAbstractTokenTable<V> : IDisposable
    where V : struct, IRealNumber<V>, INumberField<V> {
    /// <summary>表达式类型分类 / Expression category</summary>
    Category Category { get; }
    /// <summary>Token 列表 / Token list</summary>
    AbstractTokenList<V> TokenList { get; }
    /// <summary>符号集合 / Symbol collection</summary>
    ICollection<IIntermediateSymbol> Symbols { get; }

    /// <summary>所有 Token / All tokens</summary>
    ICollection<Token<V>> Tokens => TokenList.Tokens;
    /// <summary>求解器中的 Token / Tokens in solver order</summary>
    IReadOnlyList<Token<V>> TokensInSolver => TokenList.TokensInSolver;
    /// <summary>是否缓存了解决方案 / Whether solution is cached</summary>
    bool CachedSolution => TokenList.CachedSolution;

    /// <summary>按变量项查找 Token / Find token by variable item</summary>
    Token<V>? Find(IVariableItem item) => TokenList.Find(item);
    /// <summary>按索引查找 Token / Find token by index</summary>
    Token<V>? Find(int index) => TokenList.Find(index);
    /// <summary>按索引访问 Token / Access token by index</summary>
    Token<V> this[int index] => TokenList[index];
    /// <summary>获取 Token 索引 / Get token index</summary>
    int? IndexOf(Token<V> token) => TokenList.IndexOf(token);
    /// <summary>获取变量项索引 / Get variable item index</summary>
    int? IndexOf(IVariableItem item) => Find(item) is { } t ? IndexOf(t) : null;

    /// <summary>获取排除指定项的求解器 Token / Get solver tokens excluding specified items</summary>
    IReadOnlyList<Token<V>> TokensInSolverWithout(IReadOnlySet<IVariableItem> items) =>
        TokensInSolver.Where(t => !items.Contains(t.Variable)).ToList();

    /// <summary>设置解决方案（V 列表）/ Set solution (V list)</summary>
    void SetSolution(IReadOnlyList<V> solution) { Flush(); TokenList.SetSolution(solution); }
    /// <summary>设置解决方案（变量→V 映射）/ Set solution (variable→V map)</summary>
    void SetSolution(IReadOnlyDictionary<IVariableItem, V> solution) { Flush(); TokenList.SetSolution(solution); }
    /// <summary>设置求解器解决方案（Flt64 列表）/ Set solver solution (Flt64 list)</summary>
    void SetSolverSolution(IReadOnlyList<Flt64> solution) { Flush(); TokenList.SetSolverSolution(solution); }
    /// <summary>设置求解器解决方案（变量→Flt64 映射）/ Set solver solution (variable→Flt64 map)</summary>
    void SetSolverSolution(IReadOnlyDictionary<IVariableItem, Flt64> solution) { Flush(); TokenList.SetSolverSolution(solution); }

    /// <summary>刷新缓存 / Flush caches</summary>
    void Flush();

    // ===== 缓存查询 / Cache queries =====

    /// <summary>按 solution 检查缓存 / Check cache by solution</summary>
    bool Cached(object cacheKey, IReadOnlyList<V>? solution);
    /// <summary>按 fixedValues 检查缓存 / Check cache by fixedValues</summary>
    bool Cached(object cacheKey, IReadOnlyDictionary<ISymbol, V> fixedValues);
    /// <summary>按 solution 获取缓存值 / Get cached value by solution</summary>
    V? CachedValue(object cacheKey, IReadOnlyList<V>? solution);
    /// <summary>按 fixedValues 获取缓存值 / Get cached value by fixedValues</summary>
    V? CachedValue(object cacheKey, IReadOnlyDictionary<ISymbol, V> fixedValues);
    /// <summary>按 solution 放入缓存 / Put into cache by solution</summary>
    V Cache(object cacheKey, IReadOnlyList<V>? solution, V value);
    /// <summary>按 fixedValues 放入缓存 / Put into cache by fixedValues</summary>
    V Cache(object cacheKey, IReadOnlyDictionary<ISymbol, V> fixedValues, V value);
    /// <summary>清除指定解对应的值缓存 / Clear the value cache for the specified solution.</summary>
    bool ClearCachedValue(object cacheKey, IReadOnlyList<V>? solution);

    /// <summary>检查线性展平缓存 / Check linear flatten cache</summary>
    bool CachedLinearFlatten(object cacheKey);
    /// <summary>获取线性展平缓存值 / Get linear flatten cached value</summary>
    LinearFlattenData<V>? CachedLinearFlattenValue(object cacheKey);
    /// <summary>放入线性展平缓存 / Put into linear flatten cache</summary>
    LinearFlattenData<V>? CacheLinearFlatten(object cacheKey, LinearFlattenData<V>? flatten);
    /// <summary>清除线性展平缓存 / Clear linear flatten cache</summary>
    LinearFlattenData<V>? ClearLinearFlatten(object cacheKey);

    /// <summary>检查二次展平缓存 / Check quadratic flatten cache</summary>
    bool CachedQuadraticFlatten(object cacheKey);
    /// <summary>获取二次展平缓存值 / Get quadratic flatten cached value</summary>
    QuadraticFlattenData<V>? CachedQuadraticFlattenValue(object cacheKey);
    /// <summary>放入二次展平缓存 / Put into quadratic flatten cache</summary>
    QuadraticFlattenData<V>? CacheQuadraticFlatten(object cacheKey, QuadraticFlattenData<V>? flatten);
    /// <summary>清除二次展平缓存 / Clear quadratic flatten cache</summary>
    QuadraticFlattenData<V>? ClearQuadraticFlatten(object cacheKey);

    /// <summary>检查范围缓存 / Check range cache</summary>
    bool CachedRange(object cacheKey, IReadOnlyList<V>? solution);
    /// <summary>获取范围缓存值 / Get range cached value</summary>
    ValueRange<V>? CachedRangeValue(object cacheKey, IReadOnlyList<V>? solution);
    /// <summary>放入范围缓存 / Put into range cache</summary>
    ValueRange<V>? CacheRange(object cacheKey, IReadOnlyList<V>? solution, ValueRange<V> range);
    /// <summary>清除范围缓存 / Clear range cache</summary>
    ValueRange<V>? ClearRange(object cacheKey);
}

/// <summary>
/// Token 列表抽象基类 / Abstract token list base
/// </summary>
public abstract class AbstractTokenList<T> : IDisposable
    where T : struct, IRealNumber<T> {
    /// <summary>所有 Token / All tokens</summary>
    public abstract ICollection<Token<T>> Tokens { get; }
    /// <summary>求解器中的 Token / Tokens in solver order</summary>
    public abstract IReadOnlyList<Token<T>> TokensInSolver { get; }
    /// <summary>是否缓存了解决方案 / Whether solution is cached</summary>
    public virtual bool CachedSolution => Tokens.Any(t => t.ResultFlt64 is not null);

    /// <summary>按索引访问 / Access by index</summary>
    public Token<T> this[int index] => Find(index)!;

    /// <summary>查找 Token 索引 / Find token index</summary>
    public virtual int? IndexOf(Token<T> token) {
        IReadOnlyList<Token<T>> tokensInSolver = TokensInSolver;
        if (tokensInSolver.Count == 0) {
            return null;
        }

        for (int i = 0; i < tokensInSolver.Count; i++) {
            if (tokensInSolver[i].Equals(token)) {
                return i;
            }
        }
        return null;
    }

    /// <summary>查找变量项索引 / Find variable item index</summary>
    public virtual int? IndexOf(IVariableItem item) => Find(item) is { } t ? IndexOf(t) : null;

    /// <summary>按变量项查找 / Find by variable item</summary>
    public abstract Token<T>? Find(IVariableItem item);

    /// <summary>按索引查找 / Find by index</summary>
    public Token<T>? Find(int index) {
        IReadOnlyList<Token<T>> tokensInSolver = TokensInSolver;
        if (tokensInSolver.Count > 0 && index >= 0 && index < tokensInSolver.Count) {
            return tokensInSolver[index];
        }

        return Tokens.FirstOrDefault(t => t.SolverIndex == index);
    }

    /// <summary>设置解决方案（T 列表）/ Set solution (T list)</summary>
    public virtual void SetSolution(IReadOnlyList<T> solution) {
        IReadOnlyList<Token<T>> tokensInSolver = TokensInSolver;
        for (int i = 0; i < System.Math.Min(solution.Count, tokensInSolver.Count); i++) {
            tokensInSolver[i].SetResult(solution[i]);
        }
    }

    /// <summary>设置解决方案（变量→T 映射）/ Set solution (variable→T map)</summary>
    public virtual void SetSolution(IReadOnlyDictionary<IVariableItem, T> solution) {
        foreach ((IVariableItem? variable, T value) in solution) {
            Find(variable)?.SetResult(value);
        }
    }

    /// <summary>设置求解器解决方案（Flt64 列表）/ Set solver solution (Flt64 list)</summary>
    public virtual void SetSolverSolution(IReadOnlyList<Flt64> solution) {
        IReadOnlyList<Token<T>> tokensInSolver = TokensInSolver;
        for (int i = 0; i < System.Math.Min(solution.Count, tokensInSolver.Count); i++) {
            tokensInSolver[i].ResultFlt64 = solution[i];
        }
    }

    /// <summary>设置求解器解决方案（变量→Flt64 映射）/ Set solver solution (variable→Flt64 map)</summary>
    public virtual void SetSolverSolution(IReadOnlyDictionary<IVariableItem, Flt64> solution) {
        foreach ((IVariableItem? variable, Flt64 value) in solution) {
            Token<T>? token = Find(variable);
            if (token is not null) {
                token.ResultFlt64 = value;
            }
        }
    }

    /// <summary>清除解决方案 / Clear solution</summary>
    public virtual void ClearSolution() {
        foreach (Token<T> token in Tokens) {
            token.ResultFlt64 = null;
        }
    }

    public virtual void Dispose() { }
}

/// <summary>
/// 可添加 Token 集合接口 / Addable token collection interface
/// </summary>
public interface IAddableTokenCollection<T> where T : struct, IRealNumber<T> {
    /// <summary>添加变量项 / Add variable item</summary>
    Try Add(IVariableItem item);
    /// <summary>原子批量添加变量项 / Add a batch of variable items atomically.</summary>
    /// <remarks>
    /// 失败时恢复原状态；回滚失败返回 Fatal，集合可能已损坏。实现须自行保证此契约，输入枚举器不得修改或重入目标集合。
    /// On failure restore the original state; a failed rollback returns Fatal and may leave the collection dirty.
    /// Implementations must enforce this contract; the input iterator must not mutate or reenter the collection.
    /// </remarks>
    Try Add(IEnumerable<IVariableItem> items);
}

/// <summary>
/// 可变 Token 列表抽象基类 / Abstract mutable token list base
/// </summary>
public abstract class AbstractMutableTokenList<T> : AbstractTokenList<T>, IAddableTokenCollection<T>
    where T : struct, IRealNumber<T> {
    /// <summary>移除变量项 / Remove variable item</summary>
    public abstract void Remove(IVariableItem item);
    /// <summary>刷新 / Flush</summary>
    public virtual void Flush() { }

    public abstract Try Add(IVariableItem item);
    public abstract Try Add(IEnumerable<IVariableItem> items);
}

/// <summary>
/// 不可变 Token 列表（快照）/ Immutable token list (snapshot)
/// </summary>
public sealed class TokenList<T> : AbstractTokenList<T> where T : struct, IRealNumber<T> {
    private readonly Dictionary<VariableItemKey, Token<T>> _list;
    private readonly object _lock = new();
    private bool? _cachedSolution;

    public TokenList(Dictionary<VariableItemKey, Token<T>> list) {
        _list = new Dictionary<VariableItemKey, Token<T>>(list);
        foreach ((VariableItemKey _, Token<T>? value) in _list) {
            lock (value.RefreshCallbacks) {
                value.RefreshCallbacks[this] = hasResult => {
                    lock (_lock) { _cachedSolution = null; }
                };
            }
        }
    }

    public override ICollection<Token<T>> Tokens => _list.Values;
    private IReadOnlyList<Token<T>>? _tokensInSolver;
    public override IReadOnlyList<Token<T>> TokensInSolver {
        get {
            lock (_lock) {
                return _tokensInSolver ??= Tokens.OrderBy(t => t.SolverIndex).ToList().AsReadOnly();
            }
        }
    }

    public override bool CachedSolution {
        get {
            lock (_lock) {
                return _cachedSolution ??= Tokens.Any(t => t.ResultFlt64 is not null);
            }
        }
    }

    public override Token<T>? Find(IVariableItem item) =>
        _list.TryGetValue(item.Key, out Token<T>? token) ? token : null;

    public override void Dispose() {
        foreach ((VariableItemKey _, Token<T>? v) in _list) {
            lock (v.RefreshCallbacks) {
                v.RefreshCallbacks.Remove(this);
            }
        }

        base.Dispose();
    }
}

internal sealed class MutableTableTokenList<T> : AbstractTokenList<T> where T : struct, IRealNumber<T> {
    private readonly object _lock = new();
    private readonly Dictionary<VariableItemKey, Token<T>> _list = new();
    private IReadOnlyList<Token<T>>? _tokensInSolver;
    private bool? _cachedSolution;
    private bool _disposed;

    public override ICollection<Token<T>> Tokens {
        get {
            lock (_lock) {
                return _list.Values.ToArray();
            }
        }
    }

    public override IReadOnlyList<Token<T>> TokensInSolver {
        get {
            lock (_lock) {
                return _tokensInSolver ??= _list.Values.OrderBy(t => t.SolverIndex).ToList().AsReadOnly();
            }
        }
    }

    public override bool CachedSolution {
        get {
            lock (_lock) {
                return _disposed
                    ? _list.Values.Any(token => token.ResultFlt64 is not null)
                    : _cachedSolution ??= _list.Values.Any(token => token.ResultFlt64 is not null);
            }
        }
    }

    public override Token<T>? Find(IVariableItem item) {
        lock (_lock) {
            return _list.TryGetValue(item.Key, out Token<T>? token) ? token : null;
        }
    }

    internal bool Contains(VariableItemKey key) {
        lock (_lock) {
            return _list.ContainsKey(key);
        }
    }

    internal Try AddBatch(
        IEnumerable<IVariableItem> items,
        int firstIndex,
        IFlt64ValueConverter<T>? converter,
        bool checkTokenExists,
        HashSet<VariableItemKey>? skipExisting,
        out int nextIndex,
        out bool replaced) {
        lock (_lock) {
            return TokenBatchSupport.Add(
                items: items,
                contains: Contains,
                add: Replace,
                captureRollback: CaptureRollback,
                checkTokenExists: checkTokenExists,
                skipExisting: skipExisting,
                firstIndex: firstIndex,
                converter: converter,
                nextIndex: out nextIndex,
                replaced: out replaced
            );
        }
    }

    private Action CaptureRollback(VariableItemKey key) {
        _list.TryGetValue(key, out Token<T>? original);
        Action<bool>? callback = null;
        if (original is not null) {
            lock (original.RefreshCallbacks) {
                original.RefreshCallbacks.TryGetValue(this, out callback);
            }
        }
        return () => {
            lock (_lock) {
                Remove(key);
                if (original is not null) {
                    _list[key] = original;
                    lock (original.RefreshCallbacks) {
                        if (callback is not null) {
                            original.RefreshCallbacks[this] = callback;
                        }
                    }
                    _tokensInSolver = null;
                    _cachedSolution = null;
                }
            }
        };
    }

    private void Replace(VariableItemKey key, Token<T> token) {
        _list.TryGetValue(key, out Token<T>? original);
        _list[key] = token;
        if (original is not null) {
            lock (original.RefreshCallbacks) {
                original.RefreshCallbacks.Remove(this);
            }
        }
        if (!_disposed) {
            lock (token.RefreshCallbacks) {
                token.RefreshCallbacks[this] = hasResult => {
                    lock (_lock) { _cachedSolution = null; }
                };
            }
        }
        _tokensInSolver = null;
        _cachedSolution = null;
    }

    internal void Add(Token<T> token) => Add(token.Key, token);

    internal void Add(VariableItemKey key, Token<T> token) {
        lock (_lock) {
            _list.Add(key, token);
            bool completed = false;
            try {
                if (!_disposed) {
                    lock (token.RefreshCallbacks) {
                        token.RefreshCallbacks[this] = hasResult => {
                            lock (_lock) { _cachedSolution = null; }
                        };
                    }
                }
                _tokensInSolver = null;
                _cachedSolution = null;
                completed = true;
            }
            finally {
                if (!completed) {
                    _list.Remove(key);
                    lock (token.RefreshCallbacks) {
                        token.RefreshCallbacks.Remove(this);
                    }
                }
            }
        }
    }

    internal void Remove(IVariableItem item) => Remove(item.Key);

    internal void Remove(VariableItemKey key) {
        lock (_lock) {
            if (_list.Remove(key, out Token<T>? removed)) {
                lock (removed.RefreshCallbacks) {
                    removed.RefreshCallbacks.Remove(this);
                }
                _tokensInSolver = null;
                _cachedSolution = null;
            }
        }
    }

    internal TokenList<T> Snapshot() {
        lock (_lock) {
            return new TokenList<T>(_list);
        }
    }

    internal MutableTableTokenListState<T> SnapshotState() {
        lock (_lock) {
            var callbacks = new List<(Token<T> Token, Action<bool>? Callback)>();
            foreach (Token<T> token in _list.Values) {
                lock (token.RefreshCallbacks) {
                    token.RefreshCallbacks.TryGetValue(this, out Action<bool>? callback);
                    callbacks.Add((token, callback));
                }
            }
            return new MutableTableTokenListState<T>(new Dictionary<VariableItemKey, Token<T>>(_list), callbacks);
        }
    }

    internal void RestoreState(MutableTableTokenListState<T> state) {
        lock (_lock) {
            foreach (Token<T> token in _list.Values) {
                lock (token.RefreshCallbacks) {
                    token.RefreshCallbacks.Remove(this);
                }
            }
            _list.Clear();
            foreach ((VariableItemKey key, Token<T> token) in state.Tokens) {
                _list.Add(key, token);
            }
            foreach ((Token<T> token, Action<bool>? callback) in state.Callbacks) {
                lock (token.RefreshCallbacks) {
                    if (callback is not null) {
                        token.RefreshCallbacks[this] = callback;
                    }
                    else {
                        token.RefreshCallbacks.Remove(this);
                    }
                }
            }
            _tokensInSolver = null;
            _cachedSolution = null;
        }
    }

    public override void Dispose() {
        lock (_lock) {
            if (_disposed) {
                return;
            }
            _disposed = true;
            foreach (Token<T> token in _list.Values) {
                lock (token.RefreshCallbacks) {
                    token.RefreshCallbacks.Remove(this);
                }
            }
            _cachedSolution = null;
        }
    }
}

internal sealed record MutableTableTokenListState<T>(
    IReadOnlyDictionary<VariableItemKey, Token<T>> Tokens,
    IReadOnlyList<(Token<T> Token, Action<bool>? Callback)> Callbacks)
    where T : struct, IRealNumber<T>;

/// <summary>符号表注册事务 / Token-table registration transaction.</summary>
public interface ITokenRegistrationTransaction {
    /// <summary>提交注册 / Commit registration.</summary>
    /// <returns>提交结果 / Commit result.</returns>
    Try Commit();

    /// <summary>恢复注册前状态 / Restore the state before registration.</summary>
    /// <returns>回滚结果 / Rollback result.</returns>
    Try Rollback();
}

/// <summary>
/// 可变 Token 表抽象接口 / Abstract mutable token table interface
/// </summary>
public interface IAbstractMutableTokenTable<V> : IAbstractTokenTable<V>, IAddableTokenCollection<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    /// <summary>开始符号表注册事务 / Begin a token-table registration transaction.</summary>
    /// <remarks>
    /// 实现须恢复注册修改的自身结构与缓存；提交失败后仍须允许回滚。
    /// Implementations must restore their registration-mutated structure and caches and allow rollback after failed commit.
    /// 调用期间不得关闭、并发或重入修改；外部副作用和共享 token 值不属于内置事务。
    /// Closing, concurrent or reentrant mutation is unsupported; external side effects and shared token values are outside the built-in transaction.
    /// </remarks>
    /// <returns>事务或不支持错误 / A transaction or an unsupported error.</returns>
    Result<ITokenRegistrationTransaction, ErrorCode, Error<ErrorCode>> BeginTokenRegistration() =>
        Results.Failed<ITokenRegistrationTransaction>(new Err<ErrorCode>(
            ErrorCode.ApplicationError,
            "符号表不支持注册事务。 / The token table does not support registration transactions."
        ));

    /// <summary>移除变量项 / Remove variable item</summary>
    void Remove(IVariableItem item);
    /// <summary>添加符号 / Add symbol</summary>
    Try Add(IIntermediateSymbol symbol);
    /// <summary>批量添加符号 / Batch add symbols</summary>
    Try Add(IEnumerable<IIntermediateSymbol> symbols);
    /// <summary>移除符号 / Remove symbol</summary>
    void Remove(IIntermediateSymbol symbol);
}

/// <summary>
/// 并发 Token table 读写实现（统一锁 + TokenCacheContexts）
/// Concurrent token-table read/write (unified lock + cache contexts)
/// </summary>
/// <typeparam name="V">数值类型 / The number type</typeparam>
public sealed class ConcurrentTokenTable<V> : IAbstractTokenTable<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly object _lock = new();
    private readonly TokenCacheContexts<V> _cacheContexts = new();

    /// <summary>表达式类型分类 / Expression category</summary>
    public Category Category { get; }
    /// <summary>Token 列表 / Token list</summary>
    public AbstractTokenList<V> TokenList { get; }
    /// <summary>符号集合 / Symbol collection</summary>
    public ICollection<IIntermediateSymbol> Symbols { get; }

    /// <summary>创建表并取得列表的释放所有权 / Create a table that owns the supplied token list.</summary>
    /// <param name="category">表达式分类 / Expression category.</param>
    /// <param name="tokenList">由此表释放的 token 列表 / Token list disposed by this table.</param>
    /// <param name="symbols">符号集合 / Symbol collection.</param>
    public ConcurrentTokenTable(Category category, AbstractTokenList<V> tokenList, IReadOnlyList<IIntermediateSymbol> symbols) {
        Category = category;
        TokenList = tokenList;
        Symbols = symbols.ToList();
    }

    /// <summary>从可变 Token 表构造不可变副本 / Construct an immutable copy from a mutable token table</summary>
    public ConcurrentTokenTable(IAbstractMutableTokenTable<V> tokenTable)
        : this(
            category: tokenTable.Category,
            tokenList: tokenTable is ConcurrentMutableTokenTable<V> concurrent
                ? concurrent.MutableTokenList.Snapshot()
                : new TokenList<V>(tokenTable.Tokens.ToDictionary(token => token.Key)),
            symbols: tokenTable.Symbols.ToList()
        ) { }

    public void Flush() {
        lock (_lock) {
            _cacheContexts.ClearAll();
        }
    }

    public bool Cached(object cacheKey, IReadOnlyList<V>? solution) {
        lock (_lock) {
            return _cacheContexts.Value.Cached(cacheKey, solution);
        }
    }
    public bool Cached(object cacheKey, IReadOnlyDictionary<ISymbol, V> fixedValues) {
        lock (_lock) {
            return _cacheContexts.Value.Cached(cacheKey, fixedValues);
        }
    }
    public V? CachedValue(object cacheKey, IReadOnlyList<V>? solution) {
        lock (_lock) {
            return _cacheContexts.Value.Value(cacheKey, solution);
        }
    }
    public V? CachedValue(object cacheKey, IReadOnlyDictionary<ISymbol, V> fixedValues) {
        lock (_lock) {
            return _cacheContexts.Value.Value(cacheKey, fixedValues);
        }
    }
    public V Cache(object cacheKey, IReadOnlyList<V>? solution, V value) {
        lock (_lock) {
            return _cacheContexts.Value.Put(cacheKey, solution, value);
        }
    }
    public V Cache(object cacheKey, IReadOnlyDictionary<ISymbol, V> fixedValues, V value) {
        lock (_lock) {
            return _cacheContexts.Value.Put(cacheKey, fixedValues, value);
        }
    }
    public bool ClearCachedValue(object cacheKey, IReadOnlyList<V>? solution) {
        lock (_lock) {
            return _cacheContexts.Value.Remove(cacheKey, solution);
        }
    }

    public bool CachedLinearFlatten(object cacheKey) {
        lock (_lock) {
            return _cacheContexts.LinearFlatten.Contains(cacheKey);
        }
    }
    public LinearFlattenData<V>? CachedLinearFlattenValue(object cacheKey) {
        lock (_lock) {
            return _cacheContexts.LinearFlatten.Get(cacheKey);
        }
    }
    public LinearFlattenData<V>? CacheLinearFlatten(object cacheKey, LinearFlattenData<V>? flatten) { lock (_lock) { _cacheContexts.LinearFlatten.Put(cacheKey, flatten); return flatten; } }
    public LinearFlattenData<V>? ClearLinearFlatten(object cacheKey) {
        lock (_lock) {
            return _cacheContexts.LinearFlatten.Remove(cacheKey);
        }
    }

    public bool CachedQuadraticFlatten(object cacheKey) {
        lock (_lock) {
            return _cacheContexts.QuadraticFlatten.Contains(cacheKey);
        }
    }
    public QuadraticFlattenData<V>? CachedQuadraticFlattenValue(object cacheKey) {
        lock (_lock) {
            return _cacheContexts.QuadraticFlatten.Get(cacheKey);
        }
    }
    public QuadraticFlattenData<V>? CacheQuadraticFlatten(object cacheKey, QuadraticFlattenData<V>? flatten) { lock (_lock) { _cacheContexts.QuadraticFlatten.Put(cacheKey, flatten); return flatten; } }
    public QuadraticFlattenData<V>? ClearQuadraticFlatten(object cacheKey) {
        lock (_lock) {
            return _cacheContexts.QuadraticFlatten.Remove(cacheKey);
        }
    }

    public bool CachedRange(object cacheKey, IReadOnlyList<V>? solution) {
        lock (_lock) {
            return _cacheContexts.Range.Contains(cacheKey);
        }
    }
    public ValueRange<V>? CachedRangeValue(object cacheKey, IReadOnlyList<V>? solution) {
        lock (_lock) {
            return _cacheContexts.Range.Get(cacheKey)?.Range;
        }
    }
    public ValueRange<V>? CacheRange(object cacheKey, IReadOnlyList<V>? solution, ValueRange<V> range) { lock (_lock) { _cacheContexts.Range.Put(cacheKey, ExpressionRange<V>.Create(range)); return range; } }
    public ValueRange<V>? ClearRange(object cacheKey) {
        lock (_lock) {
            return _cacheContexts.Range.Remove(cacheKey)?.Range;
        }
    }

    public void Dispose() {
        lock (_lock) {
            _cacheContexts.ClearAll();
        }
        TokenList.Dispose();
    }
}

internal sealed record MutableTokenRegistrationSnapshot<V>(
    ConcurrentMutableTokenTable<V> Working,
    IReadOnlyList<Token<V>> Tokens,
    IReadOnlyList<Token<V>> WorkingTokens,
    IReadOnlyList<IIntermediateSymbol> Symbols,
    int NextIndex)
    where V : struct, IRealNumber<V>, INumberField<V>;

/// <summary>
/// 并发可变 Token 表基类 / Concurrent mutable token table base
/// </summary>
public abstract class ConcurrentMutableTokenTable<V> : IAbstractMutableTokenTable<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private protected readonly object _lock = new();
    internal TokenCacheContexts<V> CacheContexts = new();
    internal readonly MutableTableTokenList<V> MutableTokenList = new();
    protected IFlt64ValueConverter<V>? Converter { get; }
    private readonly bool _checkTokenExists;
    private HashSet<VariableItemKey>? _auxiliaryExistingKeys;
    private bool _disposed;
    private int _currentIndex;

    internal int NextIndex => _currentIndex;
    internal void RestoreNextIndex(int index) => _currentIndex = index;

    public Category Category { get; }
    public AbstractTokenList<V> TokenList { get; }
    public ICollection<IIntermediateSymbol> Symbols { get; }

    protected ConcurrentMutableTokenTable(
        Category category,
        ICollection<IIntermediateSymbol> symbols,
        IFlt64ValueConverter<V>? converter = null,
        bool checkTokenExists = false) {
        Category = category;
        TokenList = MutableTokenList;
        Symbols = symbols;
        Converter = converter;
        _checkTokenExists = checkTokenExists;
    }

    /// <inheritdoc/>
    public virtual Result<ITokenRegistrationTransaction, ErrorCode, Error<ErrorCode>> BeginTokenRegistration() {
        lock (_lock) {
            if (_disposed || this is not (ConcurrentAutoTokenTable<V> or ConcurrentManualTokenTable<V>)) {
                return Results.Failed<ITokenRegistrationTransaction>(new Err<ErrorCode>(
                    _disposed ? ErrorCode.ApplicationStopped : ErrorCode.ApplicationError,
                    "符号表已关闭或不支持注册事务。 / The token table is closed or does not support registration transactions."
                ));
            }
            try {
                return Results.Ok<ITokenRegistrationTransaction>(new RegistrationTransaction(
                    table: this,
                    tokens: MutableTokenList.SnapshotState(),
                    symbols: Symbols.ToArray(),
                    caches: CacheContexts.Snapshot(),
                    nextIndex: _currentIndex
                ));
            }
            catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) {
                return Results.Failed<ITokenRegistrationTransaction>(new Err<ErrorCode>(
                    ErrorCode.ApplicationError,
                    $"读取符号表事务失败：{error.Message} / Failed to snapshot token-table transaction: {error.Message}"
                ));
            }
        }
    }

    private sealed class RegistrationTransaction(
        ConcurrentMutableTokenTable<V> table,
        MutableTableTokenListState<V> tokens,
        IIntermediateSymbol[] symbols,
        TokenCacheContexts<V> caches,
        int nextIndex) : ITokenRegistrationTransaction {
        private bool _finished;

        public Try Commit() {
            lock (table._lock) {
                if (_finished || table._disposed) {
                    return Invalid();
                }
                _finished = true;
                return Results.OkInstance;
            }
        }

        public Try Rollback() {
            lock (table._lock) {
                if (_finished || table._disposed) {
                    return Invalid();
                }
                try {
                    table.MutableTokenList.RestoreState(tokens);
                    table._currentIndex = nextIndex;
                    table.Symbols.Clear();
                    foreach (IIntermediateSymbol symbol in symbols) {
                        table.Symbols.Add(symbol);
                    }
                    table.CacheContexts = caches;
                    _finished = true;
                    return Results.OkInstance;
                }
                catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) {
                    return Results.Failed<Success>(new Err<ErrorCode>(
                        ErrorCode.ApplicationError,
                        $"恢复符号表事务失败：{error.Message} / Failed to restore token-table transaction: {error.Message}"
                    ));
                }
            }
        }

        private static Try Invalid() => Results.Failed<Success>(new Err<ErrorCode>(
            ErrorCode.ApplicationError,
            "符号表注册事务状态无效。 / Invalid token-table registration transaction state."
        ));
    }

    internal Result<MutableTokenRegistrationSnapshot<V>, ErrorCode, Error<ErrorCode>> BeginRegistration() {
        lock (_lock) {
            if (_disposed) {
                return Results.Failed<MutableTokenRegistrationSnapshot<V>>(new Err<ErrorCode>(
                    ErrorCode.ApplicationStopped,
                    "符号表已关闭 / Token table is closed"));
            }
            ConcurrentMutableTokenTable<V>? working = this switch {
                ConcurrentAutoTokenTable<V> => new ConcurrentAutoTokenTable<V>(
                    category: Category,
                    symbols: new List<IIntermediateSymbol>(),
                    converter: Converter,
                    checkTokenExists: _checkTokenExists
                ),
                ConcurrentManualTokenTable<V> => new ConcurrentManualTokenTable<V>(
                    category: Category,
                    symbols: new List<IIntermediateSymbol>(),
                    converter: Converter,
                    checkTokenExists: _checkTokenExists
                ),
                _ => null,
            };
            if (working is null) {
                return Results.Failed<MutableTokenRegistrationSnapshot<V>>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "符号表不支持构建事务 / Token table does not support build transactions"));
            }
            try {
                working._currentIndex = _currentIndex;
                Token<V>[] tokens = MutableTokenList.Tokens.ToArray();
                Token<V>[] workingTokens = tokens.Select(token => token.CopyForRegistration()).ToArray();
                foreach (Token<V> token in workingTokens) {
                    working.MutableTokenList.Add(token);
                }
                return Results.Ok(new MutableTokenRegistrationSnapshot<V>(
                    Working: working,
                    Tokens: tokens,
                    WorkingTokens: workingTokens,
                    Symbols: Symbols.ToArray(),
                    NextIndex: _currentIndex
                ));
            }
            catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
                working.Dispose();
                return Results.Failed<MutableTokenRegistrationSnapshot<V>>(new Err<ErrorCode>(
                    ErrorCode.ApplicationError,
                    $"读取符号表快照失败：{error.Message} / Failed to snapshot token table: {error.Message}"));
            }
        }
    }

    internal Try CommitRegistration(MutableTokenRegistrationSnapshot<V> snapshot, ConcurrentTokenTable<V> modelTokens) {
        lock (_lock) {
            if (_disposed) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ApplicationStopped,
                    "符号表已关闭 / Token table is closed"));
            }
            Token<V>[] current = MutableTokenList.Tokens.ToArray();
            Token<V>[] prepared = snapshot.Working.MutableTokenList.Tokens.ToArray();
            var existing = snapshot.Tokens.ToDictionary(token => token.Key);
            if (current.Length != snapshot.Tokens.Count ||
                prepared.Length != modelTokens.TokenList.Tokens.Count ||
                !SameSymbols(Symbols, snapshot.Symbols) ||
                !SameSymbols(snapshot.Working.Symbols, snapshot.Symbols) ||
                !SameSymbols(modelTokens.Symbols, snapshot.Symbols) ||
                snapshot.Tokens.Any(token =>
                    !ReferenceEquals(MutableTokenList.Find(token.Variable), token) ||
                    !ReferenceEquals(modelTokens.TokenList.Find(token.Variable), token)) ||
                snapshot.WorkingTokens.Any(token =>
                    !ReferenceEquals(snapshot.Working.MutableTokenList.Find(token.Variable), token)) ||
                prepared.Any(token => !ReferenceEquals(modelTokens.TokenList.Find(token.Variable),
                    existing.TryGetValue(token.Key, out Token<V>? original) ? original : token)) ||
                _currentIndex != snapshot.NextIndex) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ApplicationError,
                    "模型符号表在构建期间已改变 / Model token table changed during construction"));
            }
            var added = new List<Token<V>>();
            try {
                foreach (Token<V> token in prepared.Where(token => !existing.ContainsKey(token.Key))) {
                    added.Add(token);
                    MutableTokenList.Add(token);
                }
            }
            catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
                foreach (Token<V> token in added) {
                    MutableTokenList.Remove(token.Variable);
                }
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ApplicationError,
                    $"提交符号表失败：{error.Message} / Failed to commit token table: {error.Message}"));
            }
            _currentIndex = snapshot.Working._currentIndex;
            return Results.OkInstance;
        }
    }

    private static bool SameSymbols(
        ICollection<IIntermediateSymbol> current,
        IReadOnlyList<IIntermediateSymbol> expected) =>
        current.Count == expected.Count && current.Zip(expected).All(pair => ReferenceEquals(pair.First, pair.Second));

    public void Flush() {
        lock (_lock) {
            CacheContexts.ClearAll();
        }
    }

    public bool Cached(object cacheKey, IReadOnlyList<V>? solution) {
        lock (_lock) {
            return CacheContexts.Value.Cached(cacheKey, solution);
        }
    }
    public bool Cached(object cacheKey, IReadOnlyDictionary<ISymbol, V> fixedValues) {
        lock (_lock) {
            return CacheContexts.Value.Cached(cacheKey, fixedValues);
        }
    }
    public V? CachedValue(object cacheKey, IReadOnlyList<V>? solution) {
        lock (_lock) {
            return CacheContexts.Value.Value(cacheKey, solution);
        }
    }
    public V? CachedValue(object cacheKey, IReadOnlyDictionary<ISymbol, V> fixedValues) {
        lock (_lock) {
            return CacheContexts.Value.Value(cacheKey, fixedValues);
        }
    }
    public V Cache(object cacheKey, IReadOnlyList<V>? solution, V value) {
        lock (_lock) {
            return CacheContexts.Value.Put(cacheKey, solution, value);
        }
    }
    public V Cache(object cacheKey, IReadOnlyDictionary<ISymbol, V> fixedValues, V value) {
        lock (_lock) {
            return CacheContexts.Value.Put(cacheKey, fixedValues, value);
        }
    }
    public bool ClearCachedValue(object cacheKey, IReadOnlyList<V>? solution) {
        lock (_lock) {
            return CacheContexts.Value.Remove(cacheKey, solution);
        }
    }

    public bool CachedLinearFlatten(object cacheKey) {
        lock (_lock) {
            return CacheContexts.LinearFlatten.Contains(cacheKey);
        }
    }
    public LinearFlattenData<V>? CachedLinearFlattenValue(object cacheKey) {
        lock (_lock) {
            return CacheContexts.LinearFlatten.Get(cacheKey);
        }
    }
    public LinearFlattenData<V>? CacheLinearFlatten(object cacheKey, LinearFlattenData<V>? flatten) { lock (_lock) { CacheContexts.LinearFlatten.Put(cacheKey, flatten); return flatten; } }
    public LinearFlattenData<V>? ClearLinearFlatten(object cacheKey) {
        lock (_lock) {
            return CacheContexts.LinearFlatten.Remove(cacheKey);
        }
    }

    public bool CachedQuadraticFlatten(object cacheKey) {
        lock (_lock) {
            return CacheContexts.QuadraticFlatten.Contains(cacheKey);
        }
    }
    public QuadraticFlattenData<V>? CachedQuadraticFlattenValue(object cacheKey) {
        lock (_lock) {
            return CacheContexts.QuadraticFlatten.Get(cacheKey);
        }
    }
    public QuadraticFlattenData<V>? CacheQuadraticFlatten(object cacheKey, QuadraticFlattenData<V>? flatten) { lock (_lock) { CacheContexts.QuadraticFlatten.Put(cacheKey, flatten); return flatten; } }
    public QuadraticFlattenData<V>? ClearQuadraticFlatten(object cacheKey) {
        lock (_lock) {
            return CacheContexts.QuadraticFlatten.Remove(cacheKey);
        }
    }

    public bool CachedRange(object cacheKey, IReadOnlyList<V>? solution) {
        lock (_lock) {
            return CacheContexts.Range.Contains(cacheKey);
        }
    }
    public ValueRange<V>? CachedRangeValue(object cacheKey, IReadOnlyList<V>? solution) {
        lock (_lock) {
            return CacheContexts.Range.Get(cacheKey)?.Range;
        }
    }
    public ValueRange<V>? CacheRange(object cacheKey, IReadOnlyList<V>? solution, ValueRange<V> range) { lock (_lock) { CacheContexts.Range.Put(cacheKey, ExpressionRange<V>.Create(range)); return range; } }
    public ValueRange<V>? ClearRange(object cacheKey) {
        lock (_lock) {
            return CacheContexts.Range.Remove(cacheKey)?.Range;
        }
    }

    public abstract Try Add(IVariableItem item);
    public abstract Try Add(IEnumerable<IVariableItem> items);
    public abstract void Remove(IVariableItem item);
    public abstract Try Add(IIntermediateSymbol symbol);
    public abstract Try Add(IEnumerable<IIntermediateSymbol> symbols);
    public abstract void Remove(IIntermediateSymbol symbol);

    internal Try RegisterAuxiliaryTokensSkippingExisting(
        IIntermediateSymbol<V> symbol,
        HashSet<VariableItemKey> knownKeys) {
        lock (_lock) {
            HashSet<VariableItemKey>? previous = _auxiliaryExistingKeys;
            _auxiliaryExistingKeys = knownKeys;
            try {
                return symbol.RegisterAuxiliaryTokens(this);
            }
            finally {
                _auxiliaryExistingKeys = previous;
            }
        }
    }

    private protected Try AddItems(IEnumerable<IVariableItem> items) {
        if (_disposed) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationStopped,
                "符号表已关闭 / Token table is closed"));
        }
        Try result = MutableTokenList.AddBatch(
            items: items,
            firstIndex: _currentIndex,
            converter: Converter,
            checkTokenExists: _checkTokenExists,
            skipExisting: _auxiliaryExistingKeys,
            nextIndex: out int nextIndex,
            replaced: out bool replaced
        );
        if (result.IsOk) {
            _currentIndex = nextIndex;
            if (replaced) {
                CacheContexts.ClearValue();
            }
        }
        return result;
    }

    protected static Try AddSymbol(
        ICollection<IIntermediateSymbol> symbols,
        IIntermediateSymbol symbol) {
        if (symbol is null) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "符号不能为空。 / The symbol must not be null."
            ));
        }
        if (symbols.Any(existing =>
                string.Equals(existing.Name, symbol.Name, StringComparison.Ordinal))) {
            var error = new RepeatedSymbolError(
                symbols.First(existing =>
                    string.Equals(existing.Name, symbol.Name, StringComparison.Ordinal)),
                symbol);
            return Results.Failed<Success>(
                new Err<ErrorCode>(ErrorCode.SymbolRepetitive, error.Message));
        }
        symbols.Add(symbol);
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    internal Try AddSymbolInTransaction(IIntermediateSymbol symbol) {
        lock (_lock) {
            if (_disposed) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ApplicationStopped,
                    "符号表已关闭 / Token table is closed"));
            }
            return AddSymbol(Symbols, symbol);
        }
    }

    protected static Try AddSymbols(
        ICollection<IIntermediateSymbol> symbols,
        IEnumerable<IIntermediateSymbol> additions) {
        foreach (IIntermediateSymbol symbol in additions) {
            Try result = AddSymbol(symbols, symbol);
            if (result.IsFailed) {
                return result;
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    public void Dispose() {
        lock (_lock) {
            if (_disposed) {
                return;
            }
            _disposed = true;
            CacheContexts.ClearAll();
            TokenList.Dispose();
        }
    }
}

/// <summary>
/// 并发自动索引 Token 表 / Concurrent auto-indexed token table
/// </summary>
public sealed class ConcurrentAutoTokenTable<V> : ConcurrentMutableTokenTable<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    /// <summary>创建自动索引表并传递缓存值转换器 / Create an auto-indexed table with a cached-value converter.</summary>
    /// <param name="category">表达式分类 / Expression category.</param>
    /// <param name="symbols">符号集合 / Symbol collection.</param>
    /// <param name="converter">求解器值转换器 / Solver-value converter.</param>
    /// <param name="checkTokenExists">是否拒绝重复变量 / Whether to reject duplicate variables.</param>
    public ConcurrentAutoTokenTable(
        Category category,
        ICollection<IIntermediateSymbol> symbols,
        IFlt64ValueConverter<V>? converter = null,
        bool checkTokenExists = false)
        : base(
            category: category,
            symbols: symbols,
            converter: converter,
            checkTokenExists: checkTokenExists
        ) { }

    public override Try Add(IVariableItem item) => Add(new[] { item });

    public override Try Add(IEnumerable<IVariableItem> items) {
        lock (_lock) {
            return AddItems(items);
        }
    }

    public override void Remove(IVariableItem item) {
        lock (_lock) {
            MutableTokenList.Remove(item);
        }
    }

    public override Try Add(IIntermediateSymbol symbol) {
        lock (_lock) {
            return TokenRegistrationTransactionSupport.Run(this, () => AddSymbolInTransaction(symbol));
        }
    }

    public override Try Add(IEnumerable<IIntermediateSymbol> symbols) {
        lock (_lock) {
            return TokenRegistrationTransactionSupport.Run(this, () => AddSymbols(Symbols, symbols));
        }
    }

    public override void Remove(IIntermediateSymbol symbol) {
        lock (_lock) {
            Symbols.Remove(symbol);
        }
    }
}

/// <summary>
/// 并发手动注册 Token 表 / Concurrent manually registered token table
/// </summary>
public sealed class ConcurrentManualTokenTable<V> : ConcurrentMutableTokenTable<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    /// <summary>创建手动注册表并传递缓存值转换器 / Create a manually registered table with a cached-value converter.</summary>
    /// <param name="category">表达式分类 / Expression category.</param>
    /// <param name="symbols">符号集合 / Symbol collection.</param>
    /// <param name="converter">求解器值转换器 / Solver-value converter.</param>
    /// <param name="checkTokenExists">是否拒绝重复变量 / Whether to reject duplicate variables.</param>
    public ConcurrentManualTokenTable(
        Category category,
        ICollection<IIntermediateSymbol> symbols,
        IFlt64ValueConverter<V>? converter = null,
        bool checkTokenExists = false)
        : base(
            category: category,
            symbols: symbols,
            converter: converter,
            checkTokenExists: checkTokenExists
        ) { }

    public override Try Add(IVariableItem item) => Add(new[] { item });

    public override Try Add(IEnumerable<IVariableItem> items) {
        lock (_lock) {
            return AddItems(items);
        }
    }

    public override void Remove(IVariableItem item) {
        lock (_lock) {
            MutableTokenList.Remove(item);
        }
    }

    public override Try Add(IIntermediateSymbol symbol) {
        lock (_lock) {
            return TokenRegistrationTransactionSupport.Run(this, () => AddSymbolInTransaction(symbol));
        }
    }

    public override Try Add(IEnumerable<IIntermediateSymbol> symbols) {
        lock (_lock) {
            return TokenRegistrationTransactionSupport.Run(this, () => AddSymbols(Symbols, symbols));
        }
    }

    public override void Remove(IIntermediateSymbol symbol) {
        lock (_lock) {
            Symbols.Remove(symbol);
        }
    }

}
