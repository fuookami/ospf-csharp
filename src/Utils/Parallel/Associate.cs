#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Utils.Parallel;
/// <summary>并行 Associate 扩展方法 / Parallel Associate extension methods.</summary>
public static class ParallelAssociateExtensions {
    /// <summary>并行按键关联 / Parallel associate by key.</summary>
    public static async Task<Dictionary<TKey, TSource>> AssociateByParallelly<TSource, TKey>(
        this IEnumerable<TSource> source,
        Func<TSource, TKey> keySelector,
        int? concurrency = null)
        where TKey : notnull {
        var items = source.ToList();
        List<TKey> keys = await items.MapParallelly(keySelector, concurrency).ConfigureAwait(false);
        return keys.Zip(items, (k, v) => (k, v)).ToDictionary(x => x.k, x => x.v);
    }

    /// <summary>并行关联值 / Parallel associate with value.</summary>
    public static async Task<Dictionary<TSource, TValue>> AssociateWithParallelly<TSource, TValue>(
        this IEnumerable<TSource> source,
        Func<TSource, TValue> valueSelector,
        int? concurrency = null)
        where TSource : notnull {
        var items = source.ToList();
        List<TValue> values = await items.MapParallelly(valueSelector, concurrency).ConfigureAwait(false);
        return items.Zip(values, (k, v) => (k, v)).ToDictionary(x => x.k, x => x.v);
    }
}
