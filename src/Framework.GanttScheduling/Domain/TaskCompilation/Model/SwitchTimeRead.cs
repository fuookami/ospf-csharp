// 换型时间的读取 / Reading a switch time back.
// 与固定 Kotlin 基线 `task_compilation/model/Switch.kt` 的 `switchTimeQuantity` 对应：
// `evaluate(...) ?: switchTime[from, to].toLinearPolynomial.constant`。
// Mirrors `switchTimeQuantity` in the pinned baseline's `task_compilation/model/Switch.kt`:
// `evaluate(...) ?: switchTime[from, to].toLinearPolynomial.constant`.

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;

/// <summary>
/// 换型时间的读取 / Reading a switch time back.
/// </summary>
/// <remarks>
/// 求解之后取一对任务的换型时间，要有两步：
///
/// 1. 用解出来的 token 表**求值**；
/// 2. 求不出值时，退回该表达式的**常数项**。
///
/// 第二步不是"兜底给零"。 已经说过：无 `taskTime` 的分支里 `switchTime` 是一个**常量表达式**
/// （`distance(t1.end → t2.end)`），它根本没有 token 可求值。若第二步给 0，
/// **所有固定时段排布的换型时间都会读成 0**——换型代价在报告里凭空消失，而求解本身是对的。
///
/// Reading a task pair's switch time after solving takes two steps:
///
/// 1. **evaluate** it against the solved token table;
/// 2. when evaluation yields nothing, fall back to the expression's **constant term**.
///
/// The second step is not a "default to zero". As noted, in the branch without `taskTime` the `switchTime` symbol is a **constant expression**
/// (`distance(t1.end → t2.end)`) with no token to evaluate at all. Defaulting to 0 there means **every fixed-time arrangement reports a switch time of zero** — the switch cost
/// silently vanishes from the report while the solve itself was correct.
/// </remarks>
public static class SwitchTimeRead {
    /// <summary>
    /// 取得换型时间 / Read the switch time.
    /// </summary>
    /// <param name="evaluated">求值结果，求不出时为 `null` / The evaluated value, `null` when it cannot be evaluated.</param>
    /// <param name="constant">表达式的常数项 / The expression's constant term.</param>
    /// <returns>换型时间 / The switch time.</returns>
    public static double Value(double? evaluated, double constant) => evaluated ?? constant;

    /// <summary>
    /// 判断是否走了常数兜底 / Whether the constant fallback was used.
    /// </summary>
    /// <param name="evaluated">求值结果 / The evaluated value.</param>
    /// <returns>是否走兜底 / Whether the fallback applies.</returns>
    public static bool UsesConstant(double? evaluated) => evaluated is null;
}
