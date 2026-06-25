#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Service;
/// <summary>
/// 物料等价切片模板缓存（顺序版）/ Material-equivalent slice template cache (sequential).
/// </summary>
/// <typeparam name="TMaterial">物料类型 / Material type.</typeparam>
/// <typeparam name="TSlice">切片类型 / Slice type.</typeparam>
internal sealed class GenerationMaterialSliceTemplateCache<TMaterial, TSlice>
    where TMaterial : notnull {
    private readonly Dictionary<GenerationMaterialWidthRangeKey, IReadOnlyList<IReadOnlyList<TSlice>>> _cache = new();

    /// <summary>
    /// 获取缓存的切片模板 / Get cached slice templates.
    /// </summary>
    /// <param name="key">宽度范围键 / Width range key.</param>
    /// <param name="cacheHitAction">缓存命中回调 / Cache hit callback.</param>
    /// <param name="cacheMissAction">缓存未命中回调 / Cache miss callback.</param>
    /// <returns>缓存的切片模板，未命中返回 null / Cached templates, null on miss.</returns>
    public IReadOnlyList<IReadOnlyList<TSlice>>? Get(
        GenerationMaterialWidthRangeKey key,
        System.Action? cacheHitAction = null,
        System.Action? cacheMissAction = null) {
        if (_cache.TryGetValue(key, out IReadOnlyList<IReadOnlyList<TSlice>>? templates)) {
            cacheHitAction?.Invoke();
            return templates;
        }
        cacheMissAction?.Invoke();
        return null;
    }

    /// <summary>
    /// 存入切片模板 / Store slice templates.
    /// </summary>
    /// <param name="key">宽度范围键 / Width range key.</param>
    /// <param name="templates">切片模板列表 / Slice template list.</param>
    public void Put(GenerationMaterialWidthRangeKey key, IReadOnlyList<IReadOnlyList<TSlice>> templates) => _cache.TryAdd(key, templates);
}
