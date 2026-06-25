#nullable enable

using System;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Core.Plugin.Gurobi;

/// <summary>
/// Gurobi 插件异步求解辅助类，替代 Kotlin pluginSolverAsyncScope。
/// Gurobi plugin async solver helper, replaces Kotlin pluginSolverAsyncScope.
/// </summary>
internal static class PluginSolverAsync
{
    /// <summary>
    /// 在后台线程执行操作 / Execute action on a background thread.
    /// </summary>
    /// <param name="action">要执行的操作 / Action to execute.</param>
    /// <returns>异步任务 / Async task.</returns>
    public static Task RunAsync(Action action) => Task.Run(action);

    /// <summary>
    /// 在后台线程执行异步操作 / Execute async function on a background thread.
    /// </summary>
    /// <param name="func">要执行的异步函数 / Async function to execute.</param>
    /// <returns>异步任务 / Async task.</returns>
    public static Task RunAsync(Func<Task> func) => Task.Run(func);
}
