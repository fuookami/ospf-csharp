#nullable enable

using System;
using System.Diagnostics;

namespace Fuookami.Ospf.Utils
{
    /// <summary>系统辅助方法 / System helper methods (mirrors ospf-kotlin System).</summary>
    public static class SystemHelper
    {
        /// <summary>检查内存使用是否超过阈值 / Check if memory usage exceeds threshold.</summary>
        public static bool MemoryUseOver(double threshold = 0.8)
        {
            var process = Process.GetCurrentProcess();
            var totalMemory = GC.GetTotalMemory(false);
            var workingSet = process.WorkingSet64;
            return (double)totalMemory / workingSet > threshold;
        }
    }
}
