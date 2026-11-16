// 执行器占用与编译量 / Executor usage and compilation amount.
// `executorCompilation[e] = xor[e] + z[e]`（`withExecutorLeisure` 时）对应。
// Mirrors `xor[e] ≥ xi[task]` and `executorCompilation[e] = xor[e] + z[e]` (when `withExecutorLeisure`)
//'s.

using System;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;

/// <summary>
/// 执行器占用与编译量 / Executor usage and compilation amount.
/// </summary>
/// <remarks>
/// 两层量描述一个执行器：
///
/// | 符号 | 含义 | 约束 |
/// | --- | --- | --- |
/// | `xor[e]` | 该执行器**是否被用过** | `xor[e] ≥ xi[task]`，**只有下界** |
/// | `z[e]` | 该执行器的空闲量（仅 `withExecutorLeisure`） | 独立二值变量 |
/// | `executorCompilation[e]` | 编译量 | `xor[e] + z[e]`，或仅 `xor[e]` |
///
/// 两处容易想歪：
///
/// 1. **`xor` 只有下界**。`≥` 不是 `=`：求解器可以在"该执行器真的有活"时仍让 `xor = 1`，
///    也可以在没活时取 1——**只有最小化才把它压到等号**。依赖 `xor` 去推断"执行器未使用"的代码
///    在未最小化时是错的。
/// 2. **带空闲时编译量的上界是 2，不是 1**。成本系数乘上去时，同一套系数在有/无空闲两种配置下
///    能差出两倍——按"它就是个 0/1 指示量"去估算成本会低估一半。
///
/// The code describes an executor with layers:
///
/// | Symbol | Meaning | Constraint |
/// | --- | --- | --- |
/// | `xor[e]` | whether the executor **was used** | `xor[e] ≥ xi[task]`, **a lower bound only** |
/// | `z[e]` | the executor's leisure (only with `withExecutorLeisure`) | an independent binary variable |
/// | `executorCompilation[e]` | the compiled amount | `xor[e] + z[e]`, or `xor[e]` alone |
///
/// Two things are easy to get wrong:
///
/// 1. **`xor` has a lower bound only.** `≥` is not `=`: the solver may keep `xor = 1` even when the executor has work, and may set it to 1 with no work — **only minimisation
///    drives it to equality**. Code that reads `xor` to infer "the executor is unused" is wrong when nothing is minimised.
/// 2. **With leisure the compiled amount reaches 2, not 1.** Multiply by a cost coefficient and the same coefficients differ twofold between the two configurations —
///    estimating cost as if it were a 0/1 indicator underestimates by half.
/// </remarks>
public static class ExecutorUsage {
    /// <summary>
    /// 由被选中的列数推出"是否用过" / Derive "was used" from the number of selected columns.
    /// </summary>
    /// <param name="selectedColumns">被选中的列数 / The number of selected columns.</param>
    /// <returns>是否被用过 / Whether it was used.</returns>
    /// <remarks>
    /// **下界语义**：只要有一列被选中，`xor ≥ 1` 就必须成立；一列都没有时 `xor` 可 0 可 1（下界为 0）。
    /// 这里返回的是**下界**，不是取值。
    ///
    /// **Lower-bound semantics**: one selected column forces `xor ≥ 1`; with none the bound is 0 and `xor` may be either value. What this returns is the **bound**, not the value.
    /// </remarks>
    public static bool LowerBound(int selectedColumns) {
        ArgumentOutOfRangeException.ThrowIfNegative(selectedColumns);

        return selectedColumns > 0;
    }

    /// <summary>
    /// 计算编译量 / Compute the compiled amount.
    /// </summary>
    /// <param name="withLeisure">是否包含空闲量 / Whether leisure is included.</param>
    /// <param name="xor">占用指示量 / The usage indicator.</param>
    /// <param name="leisure">空闲量 / The leisure amount.</param>
    /// <returns>编译量 / The compiled amount.</returns>
    public static double Compilation(bool withLeisure, double xor, double leisure) =>
        withLeisure ? xor + leisure : xor;

    /// <summary>
    /// 编译量的上界 / The upper bound of the compiled amount.
    /// </summary>
    /// <param name="withLeisure">是否包含空闲量 / Whether leisure is included.</param>
    /// <returns>上界 / The upper bound.</returns>
    /// <remarks>
    /// 带空闲时是 `2`（占用 + 空闲），不带时是 `1`。**成本系数乘的是这个上界**，
    /// 所以两种配置下的成本能差一倍。
    ///
    /// `2` with leisure (usage + leisure) and `1` without. **The cost coefficient multiplies this bound**, so the two configurations differ twofold.
    /// </remarks>
    public static double CompilationUpperBound(bool withLeisure) => withLeisure ? 2.0 : 1.0;
}
