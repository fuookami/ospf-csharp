// 不准时代价 / The not-on-time cost.
// 与固定 Kotlin 基线 `task_compilation/Service/.../TaskNotOnTimeMinimization.kt` 对应，
// 是 `TaskOnTimeReward` 的对偶。
// Mirrors the pinned baseline's `TaskNotOnTimeMinimization.kt`; the dual of the earlier `TaskOnTimeReward`.

using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service.Limits;

/// <summary>
/// 不准时代价 / The not-on-time cost.
/// </summary>
/// <remarks>
/// 与 `TaskOnTimeReward` 成对，两者共用"阈值零 = 不封顶"这条约定，但**数学形态相反**：
///
/// | | 准时（最大化） | 不准时（最小化） |
/// | --- | --- | --- |
/// | 阈值零 | 全额线性奖励 | 全额线性惩罚 |
/// | 阈值非零 | `min(x, cap)`，奖到顶为止 | `max(0, x − bar)`，**只罚超出** |
///
/// 阈值都是 5、系数都是 1、`x` 都是 9 时，准时得 5、不准时得 4。
/// **这个差就是判据的分辨力**：若有人把一条写成另一条的形态，两者的答案会互换。
///
/// Paired with `TaskOnTimeReward`. Both share the "zero threshold = uncapped" convention while their **mathematical forms are opposite**:
///
/// | | On time (maximise) | Not on time (minimise) |
/// | --- | --- | --- |
/// | zero threshold | full linear reward | full linear penalty |
/// | non-zero threshold | `min(x, cap)`, rewarded up to the cap | `max(0, x − bar)`, **only the excess** |
///
/// With threshold 5, coefficient 1 and `x = 9`, on-time gives 5 and not-on-time gives 4. **That difference is the criterion's discriminating power**: writing one in the other's
/// form swaps the answers.
/// </remarks>
public static class TaskNotOnTimeCost {
    /// <summary>
    /// 计算不准时代价 / Compute the not-on-time cost.
    /// </summary>
    /// <param name="notOnTime">不准时量 / The not-on-time amount.</param>
    /// <param name="threshold">阈值；**零表示不封顶** / The threshold; **zero means uncapped**.</param>
    /// <param name="coefficient">系数 / The coefficient.</param>
    /// <returns>代价 / The cost.</returns>
    /// <remarks>
    /// **阈值零表示"全额计罚"，不是"不罚"**。这里与 `TaskOnTimeReward` 的"阈值零 = 不封顶"是同一条约定，
    /// 只是方向相反：准时那边是"全都奖"，不准时这边是"全都罚"。
    /// 把它读成"阈值为零就不罚"会让默认参数下的这条管线**完全不约束滞后**——
    /// 目标是空的，求解器随便排，而调用方以为准时时长在被最小化。
    ///
    /// **A zero threshold means "charge it all", not "charge nothing"**. It is the same convention as `TaskOnTimeReward`'s "zero threshold = uncapped", only mirrored: on the
    /// on-time side everything is rewarded, on this side everything is penalised. Reading it as "no penalty at a zero threshold" makes this pipeline **constrain no lateness at
    /// all** at its default arguments — the objective is empty, the solver schedules arbitrarily, and the caller believes lateness is being minimised.
    /// </remarks>
    public static Flt64 Cost(Flt64 notOnTime, ulong threshold, Flt64 coefficient) {
        if (threshold == 0) {
            return coefficient * notOnTime;
        }

        Flt64 bar = new(threshold);
        // 只罚超出阈值的部分；贴住阈值时为零。
        // Only the excess over the threshold is charged; touching it charges nothing.
        Flt64 excess = notOnTime > bar ? notOnTime - bar : Flt64.Zero;
        return coefficient * excess;
    }

    /// <summary>
    /// 判断是否为"罚超出"形态 / Whether the "charge the excess" form applies.
    /// </summary>
    /// <param name="threshold">阈值 / The threshold.</param>
    /// <returns>是否罚超出 / Whether only the excess is charged.</returns>
    public static bool ChargesOnlyExcess(ulong threshold) => threshold != 0;
}
