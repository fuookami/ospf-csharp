#nullable enable

using System;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Framework.Solver;
/// <summary>
/// Framework 异步辅助方法（替代 Kotlin frameworkAsyncScope）/
/// Framework async helpers (replaces Kotlin frameworkAsyncScope).
/// <para>
/// Kotlin 的 <c>frameworkAsyncScope</c>（<c>CoroutineScope(SupervisorJob + Dispatchers.Default)</c>）
/// 在 C# 中不再需要——调用方直接 <c>await</c> 返回的 <c>Task</c> 即可。
/// 此类提供 <c>Task.Run</c> 包装，用于需要显式卸载到线程池的场景。
/// </para>
/// </summary>
internal static class FrameworkAsyncHelpers {
    /// <summary>在线程池上运行异步操作 / Run async operation on thread pool.</summary>
    public static Task<T> RunAsync<T>(Func<Task<T>> action) => Task.Run(action);

    /// <summary>在线程池上运行异步操作 / Run async operation on thread pool.</summary>
    public static Task RunAsync(Func<Task> action) => Task.Run(action);
}
