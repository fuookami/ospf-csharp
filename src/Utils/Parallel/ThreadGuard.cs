#nullable enable

using Fuookami.Ospf.Utils.Functional;
using System;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Utils.Parallel;
/// <summary>
/// 异步包装器 / Async wrapper (mirrors ospf-kotlin Async&lt;T&gt;).
/// Wraps a Task and provides Join/Dispose.
/// </summary>
public sealed class Async<T> : IDisposable {
    private readonly Task<T> _task;

    /// <summary>构造函数 / Constructor.</summary>
    public Async(Func<T> task) {
        _task = Task.Run(task);
    }

    /// <summary>等待结果 / Wait for result.</summary>
    public T Join() => _task.GetAwaiter().GetResult();

    /// <inheritdoc/>
    public void Dispose() {
        try { _task.Wait(); } catch { /* swallow */ }
    }
}

// ThreadGuard is a type alias — use `Async<Unit>` directly at call sites.
// Example: using ThreadGuard = Fuookami.Ospf.Utils.Parallel.Async&lt;Fuookami.Ospf.Utils.Functional.Unit&gt;;
