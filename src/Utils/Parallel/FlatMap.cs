#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Utils.Parallel;
/// <summary>并行 FlatMap 扩展方法 / Parallel FlatMap extension methods.</summary>
public static class ParallelFlatMapExtensions {
    /// <summary>并行 FlatMap / Parallel flatMap.</summary>
    public static async Task<List<TResult>> FlatMapParallelly<TSource, TResult>(
        this IEnumerable<TSource> source,
        Func<TSource, IEnumerable<TResult>> selector,
        int? concurrency = null) {
        List<IEnumerable<TResult>> mapped = await source.MapParallelly(selector, concurrency).ConfigureAwait(false);
        return mapped.SelectMany(x => x).ToList();
    }

    /// <summary>并行 FlatMap（异步）/ Parallel flatMap (async).</summary>
    public static async Task<List<TResult>> FlatMapParallelly<TSource, TResult>(
        this IEnumerable<TSource> source,
        Func<TSource, Task<IEnumerable<TResult>>> selector,
        int? concurrency = null) {
        List<IEnumerable<TResult>> mapped = await source.MapParallelly(selector, concurrency).ConfigureAwait(false);
        return mapped.SelectMany(x => x).ToList();
    }
}
