#nullable enable

using System;

namespace Fuookami.Ospf.Core.Solver;
/// <summary>
/// 求解器内存清理支持，提供基于内存压力阈值的 GC 回收策略。
/// Solver memory cleanup support, providing GC collection strategies based on memory pressure thresholds.
/// </summary>
public static class SolverMemoryCleanup {
    /// <summary>
    /// 默认内存压力阈值（字节），超过此值时触发 GC 回收。
    /// Default memory pressure threshold (bytes); triggers GC collection when exceeded.
    /// </summary>
    private const long DefaultPressureThreshold = 512L * 1024L * 1024L;

    /// <summary>
    /// 当内存压力超过阈值时执行 GC 回收。
    /// Execute GC collection when memory pressure exceeds the threshold.
    /// </summary>
    public static void CleanupOnMemoryPressure() {
        CleanupOnMemoryPressure(DefaultPressureThreshold);
    }

    /// <summary>
    /// 当内存压力超过指定阈值时执行 GC 回收。
    /// Execute GC collection when memory pressure exceeds the specified threshold.
    /// </summary>
    /// <param name="threshold">内存压力阈值（字节）/ Memory pressure threshold (bytes)</param>
    public static void CleanupOnMemoryPressure(long threshold) {
        long currentMemory = GC.GetTotalMemory(false);
        if (currentMemory > threshold) {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    /// <summary>
    /// 求解完成后执行 GC 回收以释放临时资源。
    /// Execute GC collection after a solving run to release temporary resources.
    /// </summary>
    public static void CleanupAfterRun() {
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }
}
