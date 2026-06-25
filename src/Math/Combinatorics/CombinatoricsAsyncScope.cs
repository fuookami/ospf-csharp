#nullable enable

using System.Threading;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Math.Combinatorics;
/// <summary>
/// 组合数学异步作用域 / Shared async scope for combinatorics.
/// </summary>
internal static class CombinatoricsAsyncScope {
    /// <summary>
    /// 替代 Kotlin CoroutineScope(SupervisorJob() + Dispatchers.Default)。
    /// Bounded-parallelism TaskFactory on the thread pool; channels own the producer task.
    /// </summary>
    public static TaskFactory Factory { get; } = new(
        CancellationToken.None,
        TaskCreationOptions.DenyChildAttach,
        TaskContinuationOptions.None,
        TaskScheduler.Default);
}
