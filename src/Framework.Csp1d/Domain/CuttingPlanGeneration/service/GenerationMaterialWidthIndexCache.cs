#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Service
{
    /// <summary>
    /// 物料等价宽度索引缓存 / Material-equivalent width index cache.
    ///
    /// 缓存具有相同宽度范围的物料的过滤后宽度索引。
    /// Caches filtered width indices for materials with the same width range.
    /// </summary>
    /// <typeparam name="TMaterial">物料类型 / Material type.</typeparam>
    /// <typeparam name="TEntry">条目类型 / Entry type.</typeparam>
    internal sealed class GenerationMaterialWidthIndexCache<TMaterial, TEntry>
        where TMaterial : notnull
    {
        private readonly Func<TMaterial, GenerationMaterialWidthRangeKey> _getKey;
        private readonly Func<TMaterial, TEntry, bool> _filter;
        private readonly Dictionary<GenerationMaterialWidthRangeKey, IReadOnlyList<TEntry>> _cache = new();
        private readonly object _lock = new();

        /// <summary>
        /// 创建物料宽度索引缓存 / Create material width index cache.
        /// </summary>
        /// <param name="getKey">从物料获取缓存键 / Get cache key from material.</param>
        /// <param name="filter">过滤谓词 / Filter predicate.</param>
        public GenerationMaterialWidthIndexCache(
            Func<TMaterial, GenerationMaterialWidthRangeKey> getKey,
            Func<TMaterial, TEntry, bool> filter)
        {
            _getKey = getKey;
            _filter = filter;
        }

        /// <summary>
        /// 获取过滤后的条目列表 / Get filtered entry list.
        /// </summary>
        /// <param name="material">物料 / Material.</param>
        /// <param name="allEntries">所有条目 / All entries.</param>
        /// <param name="cacheHitAction">缓存命中回调 / Cache hit callback.</param>
        /// <returns>过滤后的条目 / Filtered entries.</returns>
        public IReadOnlyList<TEntry> Get(
            TMaterial material,
            IReadOnlyList<TEntry> allEntries,
            System.Action? cacheHitAction = null)
        {
            var key = _getKey(material);
            lock (_lock)
            {
                if (_cache.TryGetValue(key, out var cached))
                {
                    cacheHitAction?.Invoke();
                    return cached;
                }
            }

            var filtered = new List<TEntry>();
            foreach (var entry in allEntries)
            {
                if (_filter(material, entry))
                {
                    filtered.Add(entry);
                }
            }

            lock (_lock)
            {
                _cache.TryAdd(key, filtered);
            }
            return filtered;
        }
    }
}
