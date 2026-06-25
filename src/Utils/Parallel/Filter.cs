#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Utils.Parallel;
/// <summary>并行 Filter 扩展方法 / Parallel Filter extension methods.</summary>
public static class ParallelFilterExtensions {
    /// <summary>并行过滤 / Parallel filter.</summary>
    public static async Task<List<TSource>> FilterParallelly<TSource>(
        this IEnumerable<TSource> source,
        Func<TSource, bool> predicate,
        int? concurrency = null) {
        var items = source.ToList();
        int c = concurrency ?? items.DefaultConcurrentAmount();
        List<bool> results = await items.ExecuteWithWorkerPool(c, (_, item) => Task.FromResult(predicate(item))).ConfigureAwait(false);
        return items.Zip(results, (item, keep) => (item, keep)).Where(x => x.keep).Select(x => x.item).ToList();
    }

    /// <summary>并行取非过滤 / Parallel filter not.</summary>
    public static async Task<List<TSource>> FilterNotParallelly<TSource>(
        this IEnumerable<TSource> source,
        Func<TSource, bool> predicate,
        int? concurrency = null)
        => await source.FilterParallelly(x => !predicate(x), concurrency).ConfigureAwait(false);

    /// <summary>并行非空过滤 / Parallel filter not null.</summary>
    public static async Task<List<TSource>> FilterNotNullParallelly<TSource>(
        this IEnumerable<TSource> source,
        int? concurrency = null)
        where TSource : class
        => await source.FilterParallelly(x => x is not null, concurrency).ConfigureAwait(false);
}
