#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Service
{
    /// <summary>
    /// 生成并行辅助方法 / Generation parallelism helpers.
    /// </summary>
    internal static class GenerationParallelism
    {
        /// <summary>
        /// 并行执行生成任务 / Run generation tasks in parallel.
        ///
        /// 使用 SemaphoreSlim 控制并发度。
        /// Uses SemaphoreSlim to control concurrency.
        /// </summary>
        /// <typeparam name="T">结果类型 / Result type.</typeparam>
        /// <param name="parallelism">并发度 / Parallelism level.</param>
        /// <param name="tasks">任务列表 / Task list.</param>
        /// <returns>任务结果列表 / Task result list.</returns>
        public static async Task<IReadOnlyList<T>> RunGenerationTasksAsync<T>(
            int parallelism,
            IReadOnlyList<Func<T>> tasks)
        {
            var normalizedParallelism = global::System.Math.Max(1, parallelism);
            if (normalizedParallelism <= 1 || tasks.Count <= 1)
            {
                return tasks.Select(t => t()).ToList();
            }

            var semaphore = new SemaphoreSlim(normalizedParallelism, normalizedParallelism);
            var runningTasks = new Task<T>[tasks.Count];

            for (int i = 0; i < tasks.Count; i++)
            {
                var task = tasks[i];
                await semaphore.WaitAsync().ConfigureAwait(false);
                runningTasks[i] = Task.Run(() =>
                {
                    try
                    {
                        return task();
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });
            }

            return await Task.WhenAll(runningTasks).ConfigureAwait(false);
        }

        /// <summary>
        /// 同步执行生成任务（当 parallelism 为 1 时）/ Run generation tasks synchronously (when parallelism is 1).
        /// </summary>
        /// <typeparam name="T">结果类型 / Result type.</typeparam>
        /// <param name="tasks">任务列表 / Task list.</param>
        /// <returns>任务结果列表 / Task result list.</returns>
        public static IReadOnlyList<T> RunGenerationTasksSequential<T>(
            IReadOnlyList<Func<T>> tasks)
        {
            return tasks.Select(t => t()).ToList();
        }
    }
}
