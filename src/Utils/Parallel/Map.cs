#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Utils.Parallel;
/// <summary>并行 Map 扩展方法 / Parallel Map extension methods.</summary>
public static class ParallelMapExtensions {
    /// <summary>并行映射 / Parallel map.</summary>
    public static async Task<List<TResult>> MapParallelly<TSource, TResult>(
        this IEnumerable<TSource> source,
        Func<TSource, TResult> selector,
        int? concurrency = null) {
        var items = source.ToList();
        int c = concurrency ?? items.DefaultConcurrentAmount();
        return await items.ExecuteWithWorkerPool(c, (_, item) => Task.FromResult(selector(item))).ConfigureAwait(false);
    }

    /// <summary>并行映射（异步）/ Parallel map (async).</summary>
    public static async Task<List<TResult>> MapParallelly<TSource, TResult>(
        this IEnumerable<TSource> source,
        Func<TSource, Task<TResult>> selector,
        int? concurrency = null) {
        var items = source.ToList();
        int c = concurrency ?? items.DefaultConcurrentAmount();
        return await items.ExecuteWithWorkerPool(c, (_, item) => selector(item)).ConfigureAwait(false);
    }

    /// <summary>并行带索引映射 / Parallel indexed map.</summary>
    public static async Task<List<TResult>> MapIndexedParallelly<TSource, TResult>(
        this IEnumerable<TSource> source,
        Func<int, TSource, TResult> selector,
        int? concurrency = null) {
        var items = source.ToList();
        int c = concurrency ?? items.DefaultConcurrentAmount();
        return await items.ExecuteWithWorkerPool(c, (i, item) => Task.FromResult(selector(i, item))).ConfigureAwait(false);
    }

    /// <summary>并行过滤映射 / Parallel map not null.</summary>
    public static async Task<List<TResult>> MapNotNullParallelly<TSource, TResult>(
        this IEnumerable<TSource> source,
        Func<TSource, TResult?> selector,
        int? concurrency = null)
        where TResult : notnull {
        List<TResult?> mapped = await source.MapParallelly(selector, concurrency).ConfigureAwait(false);
        return mapped.Where(x => x is not null).Cast<TResult>().ToList();
    }
}
