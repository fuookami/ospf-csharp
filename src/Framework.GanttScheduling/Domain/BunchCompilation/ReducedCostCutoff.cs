// 约简成本截断 / Reduced cost cutoff.
// 两处调用共用一个实现：原先各写一份私有拷贝，现在只有这一份。
// One implementation for two call sites, where each used to carry its own private copy.

using System;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation;

/// <summary>
/// 约简成本截断 / Reduced cost cutoff.
/// </summary>
/// <remarks>
/// **顺序是先向下取整、再整数乘 2、再整数除 3**，不是浮点 `x * 2 / 3` 后截断。
/// 两者在非整数输入上给出不同的答案：`10.9` 走整数路径得 `floor(10.9)=10 → 20/3=6`，
/// 走浮点路径得 `(long)7.266 = 7`。**一次差 1 的截断会让整条列生成轨迹不同**——
/// 截断值决定了哪些列被判为"值得保留"，差 1 就换掉一批列。
///
/// 整数除法本身也向下取整，因此不需要再补一次取整。
///
/// **The order is floor first, then integer multiply by 2, then integer divide by 3** — not floating `x * 2 / 3` followed by a truncation. The two disagree on non-integral
/// input: `10.9` follows the integer path to `floor(10.9)=10 → 20/3=6`, while the floating path gives `(long)7.266 = 7`. **A truncation off by one changes the whole
/// column-generation trajectory** — the cutoff decides which columns count as worth keeping, and being off by one swaps a batch of them.
///
/// Integer division floors as well, so no second rounding is needed.
/// </remarks>
public static class ReducedCostCutoff {
    /// <summary>
    /// 计算下一个约简成本截断值 / Compute the next reduced cost cutoff.
    /// </summary>
    /// <param name="maximumReducedCost">当前最大约简成本 / The current maximum reduced cost.</param>
    /// <returns>下一个截断值，至少 5 / The next cutoff, at least 5.</returns>
    public static Flt64 Next(Flt64 maximumReducedCost) {
        long floored = (long)maximumReducedCost.Floor().ToDouble();
        long reducedCostCutoff = floored * 2L / 3L;
        return new Flt64(System.Math.Max(reducedCostCutoff, 5L));
    }
}
