#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Utils.Parallel;
/// <summary>并行 Max/Min 扩展方法 / Parallel Max/Min extension methods.</summary>
public static class ParallelMaxMinExtensions {
    /// <summary>并行按key取最大 / Parallel max by key.</summary>
    public static async Task<TSource?> MaxByOrNullParallelly<TSource, TKey>(
        this IEnumerable<TSource> source,
        Func<TSource, TKey> keySelector,
        int? concurrency = null)
        where TKey : IComparable<TKey> => await Task.FromResult(source.MaxBy(keySelector)).ConfigureAwait(false);

    /// <summary>并行按key取最小 / Parallel min by key.</summary>
    public static async Task<TSource?> MinByOrNullParallelly<TSource, TKey>(
        this IEnumerable<TSource> source,
        Func<TSource, TKey> keySelector,
        int? concurrency = null)
        where TKey : IComparable<TKey> => await Task.FromResult(source.MinBy(keySelector)).ConfigureAwait(false);
}
