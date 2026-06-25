#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Utils.Parallel
{
    /// <summary>并行 Find 扩展方法 / Parallel Find extension methods.</summary>
    public static class ParallelFindExtensions
    {
        /// <summary>并行查找 / Parallel find.</summary>
        public static async Task<TSource?> FindParallelly<TSource>(
            this IEnumerable<TSource> source,
            Func<TSource, bool> predicate,
            int? concurrency = null)
        {
            var items = source.ToList();
            var c = concurrency ?? items.DefaultConcurrentAmount();
            var results = await items.ExecuteWithWorkerPool(c, (_, item) => Task.FromResult(predicate(item))).ConfigureAwait(false);
            for (int i = 0; i < items.Count; i++)
            {
                if (results[i]) return items[i];
            }
            return default;
        }

        /// <summary>并行全满足 / Parallel all.</summary>
        public static async Task<bool> AllParallelly<TSource>(
            this IEnumerable<TSource> source,
            Func<TSource, bool> predicate,
            int? concurrency = null)
        {
            var items = source.ToList();
            var c = concurrency ?? items.DefaultConcurrentAmount();
            var results = await items.ExecuteWithWorkerPool(c, (_, item) => Task.FromResult(predicate(item))).ConfigureAwait(false);
            return results.All(x => x);
        }

        /// <summary>并行存在满足 / Parallel any.</summary>
        public static async Task<bool> AnyParallelly<TSource>(
            this IEnumerable<TSource> source,
            Func<TSource, bool> predicate,
            int? concurrency = null)
        {
            var items = source.ToList();
            var c = concurrency ?? items.DefaultConcurrentAmount();
            var results = await items.ExecuteWithWorkerPool(c, (_, item) => Task.FromResult(predicate(item))).ConfigureAwait(false);
            return results.Any(x => x);
        }

        /// <summary>并行无满足 / Parallel none.</summary>
        public static async Task<bool> NoneParallelly<TSource>(
            this IEnumerable<TSource> source,
            Func<TSource, bool> predicate,
            int? concurrency = null)
            => !await source.AnyParallelly(predicate, concurrency).ConfigureAwait(false);

        /// <summary>并行计数 / Parallel count.</summary>
        public static async Task<int> CountParallelly<TSource>(
            this IEnumerable<TSource> source,
            Func<TSource, bool> predicate,
            int? concurrency = null)
        {
            var items = source.ToList();
            var c = concurrency ?? items.DefaultConcurrentAmount();
            var results = await items.ExecuteWithWorkerPool(c, (_, item) => Task.FromResult(predicate(item))).ConfigureAwait(false);
            return results.Count(x => x);
        }
    }
}
