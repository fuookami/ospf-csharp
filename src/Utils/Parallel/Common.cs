#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Utils.Parallel
{
    /// <summary>并行公共辅助方法 / Parallel common helpers.</summary>
    internal static class Common
    {
        /// <summary>默认并发数 / Default concurrent amount.</summary>
        public static int DefaultConcurrentAmount<TSource>(this IEnumerable<TSource> source) =>
            Math.Max(1, Environment.ProcessorCount - 1);

        /// <summary>使用工作池执行 / Execute with worker pool.</summary>
        public static async Task<List<TResult>> ExecuteWithWorkerPool<TSource, TResult>(
            this IEnumerable<TSource> source,
            int concurrent,
            Func<int, TSource, Task<TResult>> func)
        {
            var items = source.ToList();
            if (items.Count == 0) return new List<TResult>();

            if (concurrent <= 1)
            {
                // Sequential
                var results = new List<TResult>(items.Count);
                for (int i = 0; i < items.Count; i++)
                    results.Add(await func(i, items[i]).ConfigureAwait(false));
                return results;
            }

            if (concurrent >= items.Count)
            {
                // Parallel WhenAll
                var tasks = items.Select((item, i) => func(i, item));
                return (await Task.WhenAll(tasks).ConfigureAwait(false)).ToList();
            }

            // Bounded parallelism via SemaphoreSlim
            var semaphore = new SemaphoreSlim(concurrent);
            var resultArray = new TResult[items.Count];
            var taskList = new Task[items.Count];

            for (int i = 0; i < items.Count; i++)
            {
                var index = i;
                taskList[i] = Task.Run(async () =>
                {
                    await semaphore.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        resultArray[index] = await func(index, items[index]).ConfigureAwait(false);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });
            }

            await Task.WhenAll(taskList).ConfigureAwait(false);
            return resultArray.ToList();
        }

        /// <summary>使用工作池执行（Try 模式）/ Execute with worker pool (Try mode).</summary>
        public static async Task<Result<List<TResult>, ErrorCode, Error<ErrorCode>>> ExecuteTryWithWorkerPool<TSource, TResult>(
            this IEnumerable<TSource> source,
            int concurrent,
            Func<int, TSource, Task<Result<TResult, ErrorCode, Error<ErrorCode>>>> func)
        {
            var items = source.ToList();
            if (items.Count == 0) return Results.Ok(new List<TResult>());

            var results = await source.ExecuteWithWorkerPool(concurrent, func).ConfigureAwait(false);

            var okResults = new List<TResult>();
            foreach (var r in results)
            {
                if (r.IsFailed)
                    return new Failed<List<TResult>, ErrorCode, Error<ErrorCode>>(((dynamic)r).Error);
                okResults.Add(r.Value!);
            }
            return Results.Ok(okResults);
        }
    }
}
