#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Core.Solver;
/// <summary>
/// 求解器异步辅助类，提供 Task 包装工具。
/// Solver async helper, providing Task wrapping utilities.
/// </summary>
public static class SolverAsync {
    /// <summary>
    /// 在后台线程运行异步操作。
    /// Run async operation on a background thread.
    /// </summary>
    public static Task<T> RunAsync<T>(Func<Task<T>> func, CancellationToken cancellationToken = default)
        => Task.Run(func, cancellationToken);

    /// <summary>
    /// 在后台线程运行异步操作。
    /// Run async operation on a background thread.
    /// </summary>
    public static Task RunAsync(Func<Task> func, CancellationToken cancellationToken = default)
        => Task.Run(func, cancellationToken);
}
