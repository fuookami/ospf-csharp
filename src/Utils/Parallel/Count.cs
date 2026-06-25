#nullable enable

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Utils.Parallel;
/// <summary>并行 Count 扩展方法 / Parallel Count extension methods.</summary>
public static class ParallelCountExtensions {
    /// <summary>并行计数 / Parallel count.</summary>
    public static async Task<int> CountParallelly<TSource>(
        this IEnumerable<TSource> source,
        int? concurrency = null) => await Task.FromResult(source.Count()).ConfigureAwait(false);
}
