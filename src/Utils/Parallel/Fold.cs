#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Utils.Parallel;
/// <summary>并行 Fold 扩展方法 / Parallel Fold extension methods.</summary>
public static class ParallelFoldExtensions {
    /// <summary>并行折叠 / Parallel fold.</summary>
    public static async Task<TAccumulate> FoldParallelly<TSource, TAccumulate>(
        this IEnumerable<TSource> source,
        TAccumulate initial,
        Func<TAccumulate, TSource, TAccumulate> func,
        int? concurrency = null) {
        // For fold, we process sequentially (fold is inherently sequential)
        // but the func itself can be async
        TAccumulate? result = initial;
        foreach (TSource? item in source) {
            result = func(result, item);
        }
        return result;
    }

    /// <summary>并行折叠（异步）/ Parallel fold (async).</summary>
    public static async Task<TAccumulate> FoldParallelly<TSource, TAccumulate>(
        this IEnumerable<TSource> source,
        TAccumulate initial,
        Func<TAccumulate, TSource, Task<TAccumulate>> func) {
        TAccumulate? result = initial;
        foreach (TSource? item in source) {
            result = await func(result, item).ConfigureAwait(false);
        }
        return result;
    }
}
