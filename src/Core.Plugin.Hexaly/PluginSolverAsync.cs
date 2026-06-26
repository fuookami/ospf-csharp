#nullable enable

using System;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Core.Plugin.Hexaly;

/// <summary>
/// Hexaly 插件异步求解辅助类。
/// Hexaly plugin async solver helper.
/// </summary>
internal static class PluginSolverAsync {
    /// <summary>
    /// 在后台线程执行操作 / Execute action on a background thread.
    /// </summary>
    public static Task RunAsync(Action action) => Task.Run(action);

    /// <summary>
    /// 在后台线程执行异步操作 / Execute async function on a background thread.
    /// </summary>
    public static Task RunAsync(Func<Task> func) => Task.Run(func);
}
