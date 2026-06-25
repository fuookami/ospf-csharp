#nullable enable

namespace Fuookami.Ospf.Framework.Solver;
/// <summary>
/// 并行组合求解模式 / Parallel combinatorial solve mode.
/// </summary>
public enum ParallelCombinatorialMode {
    /// <summary>取第一个成功结果 / Take the first successful result</summary>
    First,
    /// <summary>取最优结果 / Take the best result</summary>
    Best
}
