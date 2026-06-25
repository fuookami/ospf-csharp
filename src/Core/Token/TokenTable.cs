#nullable enable

using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Algebra.ValueRange;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Token
{
    /// <summary>
    /// 重复符号错误 / Repeated symbol error
    /// </summary>
    public sealed class RepeatedSymbolError : Exception
    {
        /// <summary>已存在的符号 / The repeated symbol</summary>
        public IIntermediateSymbol RepeatedSymbol { get; }
        /// <summary>新符号 / The new symbol</summary>
        public IIntermediateSymbol Symbol { get; }

        public RepeatedSymbolError(IIntermediateSymbol repeatedSymbol, IIntermediateSymbol symbol)
            : base($"Repeated \"{symbol.Name}\", old: {repeatedSymbol}, new: {symbol}.")
        {
            RepeatedSymbol = repeatedSymbol;
            Symbol = symbol;
        }
    }

    /// <summary>
    /// Token 表抽象接口 / Abstract interface for token tables
    /// </summary>
    /// <typeparam name="V">数值类型 / The number type</typeparam>
    public interface IAbstractTokenTable<V> : IDisposable
        where V : struct, IRealNumber<V>, INumberField<V>
    {
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
        where T : struct, IRealNumber<T>
    {
        /// <summary>所有 Token / All tokens</summary>
        public abstract ICollection<Token<T>> Tokens { get; }
        /// <summary>求解器中的 Token / Tokens in solver order</summary>
        public abstract IReadOnlyList<Token<T>> TokensInSolver { get; }
        /// <summary>是否缓存了解决方案 / Whether solution is cached</summary>
        public virtual bool CachedSolution => Tokens.Any(t => t.ResultFlt64 is not null);

        /// <summary>按索引访问 / Access by index</summary>
        public Token<T> this[int index] => Find(index)!;

        /// <summary>查找 Token 索引 / Find token index</summary>
        public virtual int? IndexOf(Token<T> token)
        {
            var tokensInSolver = TokensInSolver;
            if (tokensInSolver.Count == 0) return null;
            for (int i = 0; i < tokensInSolver.Count; i++)
            {
                if (tokensInSolver[i].Equals(token)) return i;
            }
            return null;
        }

        /// <summary>查找变量项索引 / Find variable item index</summary>
        public virtual int? IndexOf(IVariableItem item) => Find(item) is { } t ? IndexOf(t) : null;

        /// <summary>按变量项查找 / Find by variable item</summary>
        public abstract Token<T>? Find(IVariableItem item);

        /// <summary>按索引查找 / Find by index</summary>
        public Token<T>? Find(int index)
        {
            var tokensInSolver = TokensInSolver;
            if (tokensInSolver.Count > 0 && index >= 0 && index < tokensInSolver.Count)
                return tokensInSolver[index];
            return Tokens.FirstOrDefault(t => t.SolverIndex == index);
        }

        /// <summary>设置解决方案（T 列表）/ Set solution (T list)</summary>
        public virtual void SetSolution(IReadOnlyList<T> solution)
        {
            var tokensInSolver = TokensInSolver;
            for (int i = 0; i < System.Math.Min(solution.Count, tokensInSolver.Count); i++)
                tokensInSolver[i].SetResult(solution[i]);
        }

        /// <summary>设置解决方案（变量→T 映射）/ Set solution (variable→T map)</summary>
        public virtual void SetSolution(IReadOnlyDictionary<IVariableItem, T> solution)
        {
            foreach (var (variable, value) in solution)
                Find(variable)?.SetResult(value);
        }

        /// <summary>设置求解器解决方案（Flt64 列表）/ Set solver solution (Flt64 list)</summary>
        public virtual void SetSolverSolution(IReadOnlyList<Flt64> solution)
        {
            var tokensInSolver = TokensInSolver;
            for (int i = 0; i < System.Math.Min(solution.Count, tokensInSolver.Count); i++)
                tokensInSolver[i].ResultFlt64 = solution[i];
        }

        /// <summary>设置求解器解决方案（变量→Flt64 映射）/ Set solver solution (variable→Flt64 map)</summary>
        public virtual void SetSolverSolution(IReadOnlyDictionary<IVariableItem, Flt64> solution)
        {
            foreach (var (variable, value) in solution)
            {
                var token = Find(variable);
                if (token is not null) token.ResultFlt64 = value;
            }
        }

        /// <summary>清除解决方案 / Clear solution</summary>
        public virtual void ClearSolution()
        {
            foreach (var token in Tokens)
                token.ResultFlt64 = null;
        }

        public virtual void Dispose() { }
    }

    /// <summary>
    /// 可添加 Token 集合接口 / Addable token collection interface
    /// </summary>
    public interface IAddableTokenCollection<T> where T : struct, IRealNumber<T>
    {
        /// <summary>添加变量项 / Add variable item</summary>
        Try Add(IVariableItem item);
        /// <summary>批量添加变量项 / Batch add variable items</summary>
        Try Add(IEnumerable<IVariableItem> items);
    }

    /// <summary>
    /// 可变 Token 列表抽象基类 / Abstract mutable token list base
    /// </summary>
    public abstract class AbstractMutableTokenList<T> : AbstractTokenList<T>, IAddableTokenCollection<T>
        where T : struct, IRealNumber<T>
    {
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
    public sealed class TokenList<T> : AbstractTokenList<T> where T : struct, IRealNumber<T>
    {
        private readonly Dictionary<VariableItemKey, Token<T>> _list;
        private readonly object _lock = new();
        private bool? _cachedSolution;

        public TokenList(Dictionary<VariableItemKey, Token<T>> list)
        {
            _list = list;
            foreach (var (_, value) in _list)
            {
                value.RefreshCallbacks[this] = hasResult =>
                {
                    lock (_lock) { _cachedSolution = null; }
                };
            }
        }

        public override ICollection<Token<T>> Tokens => _list.Values;
        private IReadOnlyList<Token<T>>? _tokensInSolver;
        public override IReadOnlyList<Token<T>> TokensInSolver =>
            _tokensInSolver ??= Tokens.OrderBy(t => t.SolverIndex).ToList();

        public override bool CachedSolution
        {
            get
            {
                lock (_lock)
                {
                    return _cachedSolution ??= Tokens.Any(t => t.ResultFlt64 is not null);
                }
            }
        }

        public override Token<T>? Find(IVariableItem item) =>
            _list.TryGetValue(item.Key, out var token) ? token : null;

        public override void Dispose()
        {
            foreach (var (_, v) in _list)
                v.RefreshCallbacks.Remove(this);
            base.Dispose();
        }
    }

    /// <summary>
    /// 可变 Token 表抽象接口 / Abstract mutable token table interface
    /// </summary>
    public interface IAbstractMutableTokenTable<V> : IAbstractTokenTable<V>, IAddableTokenCollection<V>
        where V : struct, IRealNumber<V>, INumberField<V>
    {
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
        where V : struct, IRealNumber<V>, INumberField<V>
    {
        private readonly object _lock = new();
        private readonly TokenCacheContexts<V> _cacheContexts = new();

        /// <summary>表达式类型分类 / Expression category</summary>
        public Category Category { get; }
        /// <summary>Token 列表 / Token list</summary>
        public AbstractTokenList<V> TokenList { get; }
        /// <summary>符号集合 / Symbol collection</summary>
        public ICollection<IIntermediateSymbol> Symbols { get; }

        public ConcurrentTokenTable(Category category, AbstractTokenList<V> tokenList, IReadOnlyList<IIntermediateSymbol> symbols)
        {
            Category = category;
            TokenList = tokenList;
            Symbols = symbols.ToList();
        }

        /// <summary>从并发可变 Token 表构造不可变副本 / Construct immutable copy from concurrent mutable token table</summary>
        public ConcurrentTokenTable(ConcurrentMutableTokenTable<V> tokenTable)
            : this(tokenTable.Category, new TokenList<V>(tokenTable.MutableTokenList), tokenTable.Symbols.ToList()) { }

        public void Flush() { lock (_lock) _cacheContexts.ClearAll(); }

        public bool Cached(object cacheKey, IReadOnlyList<V>? solution) { lock (_lock) return _cacheContexts.Value.Cached(cacheKey, solution); }
        public bool Cached(object cacheKey, IReadOnlyDictionary<ISymbol, V> fixedValues) { lock (_lock) return _cacheContexts.Value.Cached(cacheKey, fixedValues); }
        public V? CachedValue(object cacheKey, IReadOnlyList<V>? solution) { lock (_lock) return _cacheContexts.Value.Value(cacheKey, solution); }
        public V? CachedValue(object cacheKey, IReadOnlyDictionary<ISymbol, V> fixedValues) { lock (_lock) return _cacheContexts.Value.Value(cacheKey, fixedValues); }
        public V Cache(object cacheKey, IReadOnlyList<V>? solution, V value) { lock (_lock) return _cacheContexts.Value.Put(cacheKey, solution, value); }
        public V Cache(object cacheKey, IReadOnlyDictionary<ISymbol, V> fixedValues, V value) { lock (_lock) return _cacheContexts.Value.Put(cacheKey, fixedValues, value); }

        public bool CachedLinearFlatten(object cacheKey) { lock (_lock) return _cacheContexts.LinearFlatten.Contains(cacheKey); }
        public LinearFlattenData<V>? CachedLinearFlattenValue(object cacheKey) { lock (_lock) return _cacheContexts.LinearFlatten.Get(cacheKey); }
        public LinearFlattenData<V>? CacheLinearFlatten(object cacheKey, LinearFlattenData<V>? flatten) { lock (_lock) { _cacheContexts.LinearFlatten.Put(cacheKey, flatten); return flatten; } }
        public LinearFlattenData<V>? ClearLinearFlatten(object cacheKey) { lock (_lock) return _cacheContexts.LinearFlatten.Remove(cacheKey); }

        public bool CachedQuadraticFlatten(object cacheKey) { lock (_lock) return _cacheContexts.QuadraticFlatten.Contains(cacheKey); }
        public QuadraticFlattenData<V>? CachedQuadraticFlattenValue(object cacheKey) { lock (_lock) return _cacheContexts.QuadraticFlatten.Get(cacheKey); }
        public QuadraticFlattenData<V>? CacheQuadraticFlatten(object cacheKey, QuadraticFlattenData<V>? flatten) { lock (_lock) { _cacheContexts.QuadraticFlatten.Put(cacheKey, flatten); return flatten; } }
        public QuadraticFlattenData<V>? ClearQuadraticFlatten(object cacheKey) { lock (_lock) return _cacheContexts.QuadraticFlatten.Remove(cacheKey); }

        public bool CachedRange(object cacheKey, IReadOnlyList<V>? solution) { lock (_lock) return _cacheContexts.Range.Contains(cacheKey); }
        public ValueRange<V>? CachedRangeValue(object cacheKey, IReadOnlyList<V>? solution) { lock (_lock) return _cacheContexts.Range.Get(cacheKey)?.Range; }
        public ValueRange<V>? CacheRange(object cacheKey, IReadOnlyList<V>? solution, ValueRange<V> range) { lock (_lock) { _cacheContexts.Range.Put(cacheKey, ExpressionRange<V>.Create(range)); return range; } }
        public ValueRange<V>? ClearRange(object cacheKey) { lock (_lock) return _cacheContexts.Range.Remove(cacheKey)?.Range; }

        public void Dispose() { _cacheContexts.ClearAll(); }
    }

    /// <summary>
    /// 并发可变 Token 表基类 / Concurrent mutable token table base
    /// </summary>
    public abstract class ConcurrentMutableTokenTable<V> : IAbstractMutableTokenTable<V>
        where V : struct, IRealNumber<V>, INumberField<V>
    {
        private readonly object _lock = new();
        internal readonly TokenCacheContexts<V> CacheContexts = new();
        internal readonly Dictionary<VariableItemKey, Token<V>> MutableTokenList = new();

        public Category Category { get; }
        public AbstractTokenList<V> TokenList { get; }
        public ICollection<IIntermediateSymbol> Symbols { get; }

        protected ConcurrentMutableTokenTable(Category category, ICollection<IIntermediateSymbol> symbols)
        {
            Category = category;
            TokenList = new TokenList<V>(MutableTokenList);
            Symbols = symbols;
        }

        public void Flush() { lock (_lock) CacheContexts.ClearAll(); }

        public bool Cached(object cacheKey, IReadOnlyList<V>? solution) { lock (_lock) return CacheContexts.Value.Cached(cacheKey, solution); }
        public bool Cached(object cacheKey, IReadOnlyDictionary<ISymbol, V> fixedValues) { lock (_lock) return CacheContexts.Value.Cached(cacheKey, fixedValues); }
        public V? CachedValue(object cacheKey, IReadOnlyList<V>? solution) { lock (_lock) return CacheContexts.Value.Value(cacheKey, solution); }
        public V? CachedValue(object cacheKey, IReadOnlyDictionary<ISymbol, V> fixedValues) { lock (_lock) return CacheContexts.Value.Value(cacheKey, fixedValues); }
        public V Cache(object cacheKey, IReadOnlyList<V>? solution, V value) { lock (_lock) return CacheContexts.Value.Put(cacheKey, solution, value); }
        public V Cache(object cacheKey, IReadOnlyDictionary<ISymbol, V> fixedValues, V value) { lock (_lock) return CacheContexts.Value.Put(cacheKey, fixedValues, value); }

        public bool CachedLinearFlatten(object cacheKey) { lock (_lock) return CacheContexts.LinearFlatten.Contains(cacheKey); }
        public LinearFlattenData<V>? CachedLinearFlattenValue(object cacheKey) { lock (_lock) return CacheContexts.LinearFlatten.Get(cacheKey); }
        public LinearFlattenData<V>? CacheLinearFlatten(object cacheKey, LinearFlattenData<V>? flatten) { lock (_lock) { CacheContexts.LinearFlatten.Put(cacheKey, flatten); return flatten; } }
        public LinearFlattenData<V>? ClearLinearFlatten(object cacheKey) { lock (_lock) return CacheContexts.LinearFlatten.Remove(cacheKey); }

        public bool CachedQuadraticFlatten(object cacheKey) { lock (_lock) return CacheContexts.QuadraticFlatten.Contains(cacheKey); }
        public QuadraticFlattenData<V>? CachedQuadraticFlattenValue(object cacheKey) { lock (_lock) return CacheContexts.QuadraticFlatten.Get(cacheKey); }
        public QuadraticFlattenData<V>? CacheQuadraticFlatten(object cacheKey, QuadraticFlattenData<V>? flatten) { lock (_lock) { CacheContexts.QuadraticFlatten.Put(cacheKey, flatten); return flatten; } }
        public QuadraticFlattenData<V>? ClearQuadraticFlatten(object cacheKey) { lock (_lock) return CacheContexts.QuadraticFlatten.Remove(cacheKey); }

        public bool CachedRange(object cacheKey, IReadOnlyList<V>? solution) { lock (_lock) return CacheContexts.Range.Contains(cacheKey); }
        public ValueRange<V>? CachedRangeValue(object cacheKey, IReadOnlyList<V>? solution) { lock (_lock) return CacheContexts.Range.Get(cacheKey)?.Range; }
        public ValueRange<V>? CacheRange(object cacheKey, IReadOnlyList<V>? solution, ValueRange<V> range) { lock (_lock) { CacheContexts.Range.Put(cacheKey, ExpressionRange<V>.Create(range)); return range; } }
        public ValueRange<V>? ClearRange(object cacheKey) { lock (_lock) return CacheContexts.Range.Remove(cacheKey)?.Range; }

        public abstract Try Add(IVariableItem item);
        public abstract Try Add(IEnumerable<IVariableItem> items);
        public abstract void Remove(IVariableItem item);
        public abstract Try Add(IIntermediateSymbol symbol);
        public abstract Try Add(IEnumerable<IIntermediateSymbol> symbols);
        public abstract void Remove(IIntermediateSymbol symbol);

        public void Dispose() { CacheContexts.ClearAll(); }
    }

    /// <summary>
    /// 并发自动索引 Token 表 / Concurrent auto-indexed token table
    /// </summary>
    public sealed class ConcurrentAutoTokenTable<V> : ConcurrentMutableTokenTable<V>
        where V : struct, IRealNumber<V>, INumberField<V>
    {
        private int _currentIndex;

        public ConcurrentAutoTokenTable(Category category, ICollection<IIntermediateSymbol> symbols)
            : base(category, symbols) { }

        public override Try Add(IVariableItem item)
        {
            var key = item.Key;
            if (!MutableTokenList.ContainsKey(key))
            {
                var token = new Token<V>(item, _currentIndex++, new Dictionary<object, Action<bool>>());
                MutableTokenList[key] = token;
            }
            return Results.Ok<Success>(Results.SuccessInstance);
        }

        public override Try Add(IEnumerable<IVariableItem> items)
        {
            foreach (var item in items)
            {
                var result = Add(item);
                if (result is Failed<Success, ErrorCode, Error<ErrorCode>> f) return f;
            }
            return Results.Ok<Success>(Results.SuccessInstance);
        }

        public override void Remove(IVariableItem item) => MutableTokenList.Remove(item.Key);

        public override Try Add(IIntermediateSymbol symbol) =>
            Results.Ok<Success>(Results.SuccessInstance);

        public override Try Add(IEnumerable<IIntermediateSymbol> symbols) =>
            Results.Ok<Success>(Results.SuccessInstance);

        public override void Remove(IIntermediateSymbol symbol) { }
    }

    /// <summary>
    /// 并发手动索引 Token 表 / Concurrent manual-indexed token table
    /// </summary>
    public sealed class ConcurrentManualTokenTable<V> : ConcurrentMutableTokenTable<V>
        where V : struct, IRealNumber<V>, INumberField<V>
    {
        public ConcurrentManualTokenTable(Category category, ICollection<IIntermediateSymbol> symbols)
            : base(category, symbols) { }

        public override Try Add(IVariableItem item)
        {
            var key = item.Key;
            if (!MutableTokenList.ContainsKey(key))
            {
                var token = new Token<V>(item, item.Index, new Dictionary<object, Action<bool>>());
                MutableTokenList[key] = token;
            }
            return Results.Ok<Success>(Results.SuccessInstance);
        }

        public override Try Add(IEnumerable<IVariableItem> items)
        {
            foreach (var item in items)
            {
                var result = Add(item);
                if (result is Failed<Success, ErrorCode, Error<ErrorCode>> f) return f;
            }
            return Results.Ok<Success>(Results.SuccessInstance);
        }

        public override void Remove(IVariableItem item) => MutableTokenList.Remove(item.Key);

        public override Try Add(IIntermediateSymbol symbol) =>
            Results.Ok<Success>(Results.SuccessInstance);

        public override Try Add(IEnumerable<IIntermediateSymbol> symbols) =>
            Results.Ok<Success>(Results.SuccessInstance);

        public override void Remove(IIntermediateSymbol symbol) { }
    }
}
