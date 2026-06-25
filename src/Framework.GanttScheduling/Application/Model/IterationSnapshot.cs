#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using System;

namespace Fuookami.Ospf.Framework.GanttScheduling.Application.Model;
/// <summary>
/// 迭代状态快照，将 application 算法内部 Flt64 状态转换为对外泛型结果。
/// Iteration state snapshot converting application-internal Flt64 state to generic outward-facing results.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type</typeparam>
/// <param name="Iteration">迭代次数 / Iteration count</param>
/// <param name="RunTime">运行时间 / Runtime</param>
/// <param name="BestObjective">最佳整数目标值 / Best integer objective</param>
/// <param name="BestLpObjective">最佳 LP 目标值 / Best LP objective</param>
/// <param name="BestDualObjective">最佳对偶目标值 / Best dual objective</param>
/// <param name="LowerBound">下界 / Lower bound</param>
/// <param name="UpperBound">上界 / Upper bound</param>
/// <param name="SlowLpImprovementStep">LP 改进缓慢步长 / Slow LP improvement step</param>
/// <param name="OptimalRate">最优率，无量纲 / Optimal rate, dimensionless</param>
/// <param name="IsImprovementSlow">是否改进缓慢 / Whether improvement is slow</param>
public sealed record IterationSnapshot<V>(
    ulong Iteration,
    TimeSpan RunTime,
    Quantity<V> BestObjective,
    Quantity<V> BestLpObjective,
    Quantity<V> BestDualObjective,
    Quantity<V> LowerBound,
    Quantity<V>? UpperBound,
    Quantity<V>? SlowLpImprovementStep,
    V OptimalRate,
    bool IsImprovementSlow
)
    where V : struct, IRealNumber<V>;
