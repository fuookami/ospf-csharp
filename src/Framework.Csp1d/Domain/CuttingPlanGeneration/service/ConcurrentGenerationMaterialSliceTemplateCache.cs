#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Service;
/// <summary>
/// 并发安全的物料等价切片模板缓存 / Thread-safe material-equivalent slice template cache.
///
/// 使用 ConcurrentDictionary 保证线程安全。
/// Uses ConcurrentDictionary for thread safety.
/// </summary>
/// <typeparam name="TSlice">切片类型 / Slice type.</typeparam>
internal sealed class ConcurrentGenerationMaterialSliceTemplateCache<TSlice> {
    private readonly ConcurrentDictionary<GenerationMaterialWidthRangeKey, System.Collections.Generic.IReadOnlyList<System.Collections.Generic.IReadOnlyList<TSlice>>> _cache = new();
    private long _hits;
    private long _misses;

    /// <summary>总命中数 / Total hits.</summary>
    public long TotalHits => Interlocked.Read(ref _hits);

    /// <summary>总未命中数 / Total misses.</summary>
    public long TotalMisses => Interlocked.Read(ref _misses);

    /// <summary>
    /// 获取缓存的切片模板 / Get cached slice templates.
    /// </summary>
    /// <param name="key">宽度范围键 / Width range key.</param>
    /// <returns>缓存的切片模板，未命中返回 null / Cached templates, null on miss.</returns>
    public System.Collections.Generic.IReadOnlyList<System.Collections.Generic.IReadOnlyList<TSlice>>? Get(
        GenerationMaterialWidthRangeKey key) {
        if (_cache.TryGetValue(key, out IReadOnlyList<IReadOnlyList<TSlice>>? templates)) {
            Interlocked.Increment(ref _hits);
            return templates;
        }
        Interlocked.Increment(ref _misses);
        return null;
    }

    /// <summary>
    /// 存入切片模板 / Store slice templates.
    /// </summary>
    /// <param name="key">宽度范围键 / Width range key.</param>
    /// <param name="templates">切片模板列表 / Slice template list.</param>
    public void Put(
        GenerationMaterialWidthRangeKey key,
        System.Collections.Generic.IReadOnlyList<System.Collections.Generic.IReadOnlyList<TSlice>> templates) => _cache.TryAdd(key, templates);
}
