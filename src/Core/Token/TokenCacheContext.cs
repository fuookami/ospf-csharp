#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Monomial;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Core.Token;
/// <summary>
/// 线性展平缓存数据 / Linear flatten cache data
/// </summary>
public sealed record LinearFlattenData<T>(IReadOnlyList<LinearMonomial<T>> Monomials, T Constant)
    where T : struct, IRing<T>;

/// <summary>
/// 二次展平缓存数据 / Quadratic flatten cache data
/// </summary>
public sealed record QuadraticFlattenData<T>(IReadOnlyList<QuadraticMonomial<T>> Monomials, T Constant)
    where T : struct, IRing<T>;

/// <summary>
/// 线性展平缓存上下文 / Linear flatten cache context
/// </summary>
public sealed class LinearFlattenContext<V> where V : struct, IRing<V> {
    private readonly Dictionary<object, LinearFlattenData<V>?> _cache = new();

    /// <summary>是否包含指定键 / Whether contains the specified key</summary>
    public bool Contains(object cacheKey) => _cache.ContainsKey(cacheKey);
    /// <summary>获取缓存值 / Get cached value</summary>
    public LinearFlattenData<V>? Get(object cacheKey) => _cache.TryGetValue(cacheKey, out LinearFlattenData<V>? v) ? v : null;
    /// <summary>放入缓存 / Put into cache</summary>
    public void Put(object cacheKey, LinearFlattenData<V>? value) => _cache[cacheKey] = value;
    /// <summary>移除缓存 / Remove from cache</summary>
    public LinearFlattenData<V>? Remove(object cacheKey) =>
        _cache.Remove(cacheKey, out LinearFlattenData<V>? v) ? v : null;
    /// <summary>所有键 / All keys</summary>
    public IReadOnlySet<object> Keys => new HashSet<object>(_cache.Keys);
    /// <summary>清除 / Clear</summary>
    public void Clear() => _cache.Clear();
}

/// <summary>
/// 二次展平缓存上下文 / Quadratic flatten cache context
/// </summary>
public sealed class QuadraticFlattenContext<V> where V : struct, IRing<V> {
    private readonly Dictionary<object, QuadraticFlattenData<V>?> _cache = new();

    /// <summary>是否包含指定键 / Whether contains the specified key</summary>
    public bool Contains(object cacheKey) => _cache.ContainsKey(cacheKey);
    /// <summary>获取缓存值 / Get cached value</summary>
    public QuadraticFlattenData<V>? Get(object cacheKey) => _cache.TryGetValue(cacheKey, out QuadraticFlattenData<V>? v) ? v : null;
    /// <summary>放入缓存 / Put into cache</summary>
    public void Put(object cacheKey, QuadraticFlattenData<V>? value) => _cache[cacheKey] = value;
    /// <summary>移除缓存 / Remove from cache</summary>
    public QuadraticFlattenData<V>? Remove(object cacheKey) =>
        _cache.Remove(cacheKey, out QuadraticFlattenData<V>? v) ? v : null;
    /// <summary>所有键 / All keys</summary>
    public IReadOnlySet<object> Keys => new HashSet<object>(_cache.Keys);
    /// <summary>清除 / Clear</summary>
    public void Clear() => _cache.Clear();
}

/// <summary>
/// 值缓存上下文（按 solution 或 fixedValues 缓存）/ Value cache context (by solution or fixedValues)
/// </summary>
public sealed class ValueCacheContext<V> where V : struct, IRealNumber<V> {
    private readonly Dictionary<(object Key, IReadOnlyList<V>? Solution), V?> _solutionCache = new();
    private readonly Dictionary<(object Key, IReadOnlyDictionary<ISymbol, V> FixedValues), V?> _fixedValueCache = new();

    /// <summary>清除所有缓存 / Clear all caches</summary>
    public void Clear() { _solutionCache.Clear(); _fixedValueCache.Clear(); }

    /// <summary>按 solution 检查缓存 / Check cache by solution</summary>
    public bool Cached(object cacheKey, IReadOnlyList<V>? solution = null) =>
        _solutionCache.ContainsKey((cacheKey, solution));

    /// <summary>按 fixedValues 检查缓存 / Check cache by fixedValues</summary>
    public bool Cached(object cacheKey, IReadOnlyDictionary<ISymbol, V> fixedValues) =>
        _fixedValueCache.ContainsKey((cacheKey, fixedValues));

    /// <summary>按 solution 获取缓存值 / Get cached value by solution</summary>
    public V? Value(object cacheKey, IReadOnlyList<V>? solution = null) =>
        _solutionCache.TryGetValue((cacheKey, solution), out V? v) ? v : default;

    /// <summary>按 fixedValues 获取缓存值 / Get cached value by fixedValues</summary>
    public V? Value(object cacheKey, IReadOnlyDictionary<ISymbol, V> fixedValues) =>
        _fixedValueCache.TryGetValue((cacheKey, fixedValues), out V? v) ? v : default;

    /// <summary>按 solution 放入缓存 / Put into cache by solution</summary>
    public V Put(object cacheKey, IReadOnlyList<V>? solution, V value) {
        _solutionCache[(cacheKey, solution)] = value;
        return value;
    }

    /// <summary>按 fixedValues 放入缓存 / Put into cache by fixedValues</summary>
    public V Put(object cacheKey, IReadOnlyDictionary<ISymbol, V> fixedValues, V value) {
        _fixedValueCache[(cacheKey, fixedValues)] = value;
        return value;
    }

    /// <summary>按 cacheKey 移除缓存 / Remove cache by cacheKey</summary>
    public void Remove(object cacheKey) {
        var solutionKeysToRemove = new List<(object, IReadOnlyList<V>?)>();
        foreach ((object Key, IReadOnlyList<V>? Solution) key in _solutionCache.Keys) {
            if (key.Item1 == cacheKey) {
                solutionKeysToRemove.Add(key);
            }
        }
        foreach ((object, IReadOnlyList<V>?) key in solutionKeysToRemove) {
            _solutionCache.Remove(key);
        }

        var fixedValueKeysToRemove = new List<(object, IReadOnlyDictionary<ISymbol, V>)>();
        foreach ((object Key, IReadOnlyDictionary<ISymbol, V> FixedValues) key in _fixedValueCache.Keys) {
            if (key.Item1 == cacheKey) {
                fixedValueKeysToRemove.Add(key);
            }
        }
        foreach ((object, IReadOnlyDictionary<ISymbol, V>) key in fixedValueKeysToRemove) {
            _fixedValueCache.Remove(key);
        }
    }
}

/// <summary>
/// 范围缓存上下文 / Range cache context
/// </summary>
public sealed class RangeCacheContext<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly Dictionary<object, ExpressionRange<V>?> _cache = new();

    /// <summary>是否包含指定键 / Whether contains the specified key</summary>
    public bool Contains(object cacheKey) => _cache.ContainsKey(cacheKey);
    /// <summary>获取缓存值 / Get cached value</summary>
    public ExpressionRange<V>? Get(object cacheKey) => _cache.TryGetValue(cacheKey, out ExpressionRange<V>? v) ? v : null;
    /// <summary>放入缓存 / Put into cache</summary>
    public void Put(object cacheKey, ExpressionRange<V>? value) => _cache[cacheKey] = value;
    /// <summary>移除缓存 / Remove from cache</summary>
    public ExpressionRange<V>? Remove(object cacheKey) =>
        _cache.Remove(cacheKey, out ExpressionRange<V>? v) ? v : null;
    /// <summary>所有键 / All keys</summary>
    public IReadOnlySet<object> Keys => new HashSet<object>(_cache.Keys);
    /// <summary>清除 / Clear</summary>
    public void Clear() => _cache.Clear();
}

/// <summary>
/// 聚合缓存上下文（线性展平+二次展平+值+范围）
/// Aggregate cache context (linear flatten + quadratic flatten + value + range)
/// </summary>
public sealed record TokenCacheContexts<V>(
    LinearFlattenContext<V> LinearFlatten,
    QuadraticFlattenContext<V> QuadraticFlatten,
    ValueCacheContext<V> Value,
    RangeCacheContext<V> Range)
    where V : struct, IRealNumber<V>, INumberField<V> {
    /// <summary>创建默认实例 / Create default instance</summary>
    public TokenCacheContexts()
        : this(new LinearFlattenContext<V>(), new QuadraticFlattenContext<V>(),
               new ValueCacheContext<V>(), new RangeCacheContext<V>()) { }

    /// <summary>所有绑定的符号 / All bound symbols</summary>
    public IReadOnlySet<object> BoundSymbols() {
        var result = new HashSet<object>();
        result.UnionWith(LinearFlatten.Keys);
        result.UnionWith(QuadraticFlatten.Keys);
        result.UnionWith(Range.Keys);
        return result;
    }

    /// <summary>清除线性展平缓存 / Clear linear flatten cache</summary>
    public void ClearLinearFlatten() => LinearFlatten.Clear();
    /// <summary>清除二次展平缓存 / Clear quadratic flatten cache</summary>
    public void ClearQuadraticFlatten() => QuadraticFlatten.Clear();
    /// <summary>清除所有展平缓存 / Clear all flatten caches</summary>
    public void ClearFlatten() { ClearLinearFlatten(); ClearQuadraticFlatten(); }
    /// <summary>清除值缓存 / Clear value cache</summary>
    public void ClearValue() => Value.Clear();
    /// <summary>清除范围缓存 / Clear range cache</summary>
    public void ClearRange() => Range.Clear();
    /// <summary>清除所有缓存 / Clear all caches</summary>
    public void ClearAll() { ClearFlatten(); ClearValue(); ClearRange(); }
}
