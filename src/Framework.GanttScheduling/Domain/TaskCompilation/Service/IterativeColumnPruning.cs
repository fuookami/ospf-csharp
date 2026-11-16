// 迭代列剪枝 / Iterative column pruning.
// 与固定 Kotlin 基线 `gantt-scheduling-domain-task-compilation-context/.../IterativeAggregation.kt` 里
// `removeColumns` 的两个纯判定部分对应。
// Mirrors the two pure decisions inside `removeColumns` in the pinned baseline's `IterativeAggregation.kt`.

using System;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service;

/// <summary>
/// 迭代列剪枝 / Iterative column pruning.
/// </summary>
/// <remarks>
/// 每一轮从列池里剔除约简成本不再值得一提的列。判定有三条合取，而**第一条的边界方向最容易被写反**：
///
/// 基线写的是 `!(reducedCost(task) ls maximumReducedCost)`，即 `reducedCost >= maximumReducedCost` 时**剔除**。
/// 一个"自然"的实现会写成 `reducedCost > maximumReducedCost`——两者只在**恰好等于**时不同。
/// 恰好等于的列正是"刚刚够格"的列：写错时它被留在列池里，于是每轮都多扫一遍它，
/// 而**它永远不会变成有价值的列**。
///
/// Each round drops columns whose reduced cost is no longer worth keeping. The test is a conjunction of three, and **the boundary direction of the first is easiest to invert**:
///
/// the baseline writes `!(reducedCost(task) ls maximumReducedCost)`, i.e. drop when `reducedCost >= maximumReducedCost`. A "natural" implementation writes
/// `reducedCost > maximumReducedCost` — the two differ only at **exactly equal**. A column exactly at the boundary is the one that just barely qualifies: with the wrong
/// comparison it stays in the pool, gets rescanned every round, and **never becomes a column worth having**.
/// </remarks>
public static class IterativeColumnPruning {
    /// <summary>
    /// 判断一列是否应被剔除 / Whether a column should be dropped.
    /// </summary>
    /// <param name="reducedCost">该列的约简成本 / The column's reduced cost.</param>
    /// <param name="maximumReducedCost">当前最大约简成本 / The current maximum reduced cost.</param>
    /// <param name="isFixed">是否已固定 / Whether it is fixed.</param>
    /// <param name="isKept">是否被保留 / Whether it is kept.</param>
    /// <returns>是否剔除 / Whether to drop it.</returns>
    /// <remarks>
    /// **固定与保留的列优先**：它们可能因为模型结构原因暂时没有约简成本，但下一轮还要用。
    /// 剔除它们会让迭代**丢掉已经确定要保留的决定**，而下一轮只能重新发现同一个决定。
    ///
    /// **Fixed and kept columns take precedence**: they may temporarily show no reduced cost for structural reasons but are needed next round. Dropping them makes the iteration
    /// **discard a decision already made**, only to rediscover the same decision next round.
    /// </remarks>
    public static bool ShouldRemove(
        Flt64 reducedCost,
        Flt64 maximumReducedCost,
        bool isFixed,
        bool isKept) {
        if (isFixed || isKept) {
            return false;
        }
        return !(reducedCost < maximumReducedCost);
    }

    /// <summary>
    /// 计算下一轮的最大约简成本 / Compute the next round's maximum reduced cost.
    /// </summary>
    /// <param name="remainingAmount">剩余列数 / The remaining column count.</param>
    /// <param name="maximumColumnAmount">列数上限 / The column count ceiling.</param>
    /// <param name="maximumReducedCost">当前最大约简成本 / The current maximum reduced cost.</param>
    /// <returns>下一轮的最大约简成本 / the next round's maximum reduced cost.</returns>
    /// <remarks>
    /// 只在**列数仍超上限时**衰减，否则原样返回。衰减本身复用 `BunchCompilation` 里已落地的
    /// `NextReducedCostCutoff`——基线的同一段逻辑在 `IterativeAggregation` 里有一份私有拷贝，
    /// C# 共用一份实现即可。
    ///
    /// Decay only when the **column count still exceeds the ceiling**, otherwise return it unchanged. The decay itself reuses the already-landed `NextReducedCostCutoff` from
    /// `BunchCompilation` — the baseline keeps a private copy of the same code in `IterativeAggregation`, and C# shares one implementation.
    /// </remarks>
    public static Flt64 NextMaximumReducedCost(
        int remainingAmount,
        ulong maximumColumnAmount,
        Flt64 maximumReducedCost) {
        if (remainingAmount < 0) {
            throw new ArgumentOutOfRangeException(nameof(remainingAmount), remainingAmount, "剩余列数不能为负 / The remaining column count cannot be negative");
        }

        return (ulong)remainingAmount > maximumColumnAmount
            ? ReducedCostCutoff.Next(maximumReducedCost)
            : maximumReducedCost;
    }
}
