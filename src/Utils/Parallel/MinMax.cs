#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Utils.Parallel;
/// <summary>并行 MinMax 扩展方法 / Parallel MinMax extension methods.</summary>
public static class ParallelMinMaxExtensions {
    /// <summary>并行最小最大 / Parallel min-max by key.</summary>
    public static async Task<(TSource min, TSource max)?> MinMaxByOrNullParallelly<TSource, TKey>(
        this IEnumerable<TSource> source,
        Func<TSource, TKey> keySelector,
        int? concurrency = null)
        where TKey : IComparable<TKey> {
        TSource? min = source.MinBy(keySelector);
        TSource? max = source.MaxBy(keySelector);
        if (min is null || max is null) {
            return null;
        }

        return (min, max);
    }
}
