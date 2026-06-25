#nullable enable

using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Service
{
    /// <summary>
    /// 物料等价切片模板缓存接口 / Interface for material-equivalent slice template cache.
    /// </summary>
    /// <typeparam name="TMaterial">物料类型 / Material type.</typeparam>
    /// <typeparam name="TSlice">切片类型 / Slice type.</typeparam>
    internal interface IGenerationSliceTemplateCache<TMaterial, TSlice>
    {
        /// <summary>
        /// 获取缓存的切片模板 / Get cached slice templates.
        /// </summary>
        /// <param name="material">物料 / Material.</param>
        /// <param name="cacheHitAction">缓存命中时的回调 / Callback on cache hit.</param>
        /// <param name="cacheMissAction">缓存未命中时的回调 / Callback on cache miss.</param>
        /// <returns>缓存的切片模板列表，未命中返回 null / Cached slice template list, null on miss.</returns>
        IReadOnlyList<IReadOnlyList<TSlice>>? Get(
            TMaterial material,
            System.Action? cacheHitAction = null,
            System.Action? cacheMissAction = null);

        /// <summary>
        /// 存入切片模板 / Store slice templates.
        /// </summary>
        /// <param name="material">物料 / Material.</param>
        /// <param name="templates">切片模板列表 / Slice template list.</param>
        void Put(TMaterial material, IReadOnlyList<IReadOnlyList<TSlice>> templates);
    }

    /// <summary>
    /// 顺序版切片模板缓存 / Sequential slice template cache.
    /// </summary>
    /// <typeparam name="TMaterial">物料类型 / Material type.</typeparam>
    /// <typeparam name="TSlice">切片类型 / Slice type.</typeparam>
    internal sealed class SequentialGenerationSliceTemplateCache<TMaterial, TSlice>
        : IGenerationSliceTemplateCache<TMaterial, TSlice>
        where TMaterial : notnull
    {
        private readonly Dictionary<TMaterial, IReadOnlyList<IReadOnlyList<TSlice>>> _cache = new();

        /// <inheritdoc/>
        public IReadOnlyList<IReadOnlyList<TSlice>>? Get(
            TMaterial material,
            System.Action? cacheHitAction = null,
            System.Action? cacheMissAction = null)
        {
            if (_cache.TryGetValue(material, out var templates))
            {
                cacheHitAction?.Invoke();
                return templates;
            }
            cacheMissAction?.Invoke();
            return null;
        }

        /// <inheritdoc/>
        public void Put(TMaterial material, IReadOnlyList<IReadOnlyList<TSlice>> templates)
        {
            _cache.TryAdd(material, templates);
        }
    }

    /// <summary>
    /// 并发版切片模板缓存 / Concurrent slice template cache.
    /// </summary>
    /// <typeparam name="TMaterial">物料类型 / Material type.</typeparam>
    /// <typeparam name="TSlice">切片类型 / Slice type.</typeparam>
    internal sealed class ConcurrentGenerationSliceTemplateCache<TMaterial, TSlice>
        : IGenerationSliceTemplateCache<TMaterial, TSlice>
        where TMaterial : notnull
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<TMaterial, IReadOnlyList<IReadOnlyList<TSlice>>> _cache = new();
        private long _hits;
        private long _misses;

        /// <summary>总命中数 / Total hits.</summary>
        public long TotalHits => System.Threading.Interlocked.Read(ref _hits);

        /// <summary>总未命中数 / Total misses.</summary>
        public long TotalMisses => System.Threading.Interlocked.Read(ref _misses);

        /// <inheritdoc/>
        public IReadOnlyList<IReadOnlyList<TSlice>>? Get(
            TMaterial material,
            System.Action? cacheHitAction = null,
            System.Action? cacheMissAction = null)
        {
            if (_cache.TryGetValue(material, out var templates))
            {
                System.Threading.Interlocked.Increment(ref _hits);
                cacheHitAction?.Invoke();
                return templates;
            }
            System.Threading.Interlocked.Increment(ref _misses);
            cacheMissAction?.Invoke();
            return null;
        }

        /// <inheritdoc/>
        public void Put(TMaterial material, IReadOnlyList<IReadOnlyList<TSlice>> templates)
        {
            _cache.TryAdd(material, templates);
        }
    }
}
