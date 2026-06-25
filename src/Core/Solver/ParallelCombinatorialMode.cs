#nullable enable

namespace Fuookami.Ospf.Core.Solver
{
    /// <summary>
    /// 并行组合求解模式 / Parallel combinatorial solve mode.
    /// </summary>
    public enum ParallelCombinatorialMode
    {
        /// <summary>取第一个成功结果 / Take first successful result</summary>
        First,
        /// <summary>取最优结果 / Take best result</summary>
        Best,
    }
}
