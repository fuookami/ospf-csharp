#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using System;

namespace Fuookami.Ospf.Core.Solver.Output;

/// <summary>
/// 不可行输出统一字段 / Infeasible output unified fields.
/// </summary>
/// <param name="Iterations">迭代次数（可选）/ Iteration count (optional)</param>
/// <param name="NodeCount">节点数（可选）/ Node count (optional)</param>
/// <param name="BestBound">最优界（可选）/ Best bound (optional)</param>
/// <param name="MipGap">MIP 间隙（可选）/ MIP gap (optional)</param>
/// <param name="SolveTime">求解时间 / Solve time</param>
public sealed record InfeasibleUnifiedFields(
    ulong? Iterations,
    ulong? NodeCount,
    Flt64? BestBound,
    Flt64? MipGap,
    TimeSpan SolveTime);

/// <summary>
/// 不可行输出字段解析器 / Infeasible output field resolver.
/// </summary>
public static class InfeasibleOutputResolver {
    /// <summary>
    /// 从求解状态解析不可行输出字段 / Resolve infeasible output fields from solving status.
    /// </summary>
    /// <param name="latestStatus">最新求解状态（可选）/ Latest solving status (optional)</param>
    /// <param name="fallbackSolveTime">回退求解时间 / Fallback solve time</param>
    /// <returns>不可行统一字段 / Infeasible unified fields</returns>
    public static InfeasibleUnifiedFields Resolve(
        SolvingStatus? latestStatus,
        TimeSpan fallbackSolveTime) {
        return new InfeasibleUnifiedFields(
            Iterations: latestStatus?.Iterations,
            NodeCount: latestStatus?.NodeCount,
            BestBound: latestStatus?.BestBound,
            MipGap: latestStatus?.MipGap,
            SolveTime: latestStatus?.SolveTime ?? fallbackSolveTime);
    }
}
