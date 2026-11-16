// 任务准时奖励 / Task on-time reward.

using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service.Limits;

/// <summary>
/// 任务准时奖励 / Task on-time reward.
/// </summary>
/// <remarks>
/// 这条是**最大化**，与被最小化的那族恰好相反，而两者的阈值语义也相反：
///
/// | | 最小化（提前/延迟时间） | 最大化（准时） |
/// | --- | --- | --- |
/// | 阈值含义 | **超出阈值才罚** | **奖励到阈值为止** |
/// | 数学形态 | `max(0, x − bar)` | `min(x, cap)` |
///
/// 把两者写成同一个形态是个安静的错：最大化里用 `max(0, x − cap)` 会**奖励超量完成**
/// （超额部分反而加分），最小化里用 `min(x, bar)` 会**在阈值之外停止惩罚**（罚到顶就不再涨）。
/// 两种都是"数值算得出来、看着合理、结果偏掉"。判据因此必须同时覆盖"低于阈值"和"高于阈值"两侧。
///
/// This one is a **maximisation**, the exact opposite of the minimised family, and the threshold semantics are opposite too:
///
/// | | Minimisation (advance/delay) | Maximisation (on time) |
/// | --- | --- | --- |
/// | Threshold meaning | **penalise only beyond it** | **reward up to it** |
/// | Mathematical form | `max(0, x − bar)` | `min(x, cap)` |
///
/// Using one form for both is a quiet error: `max(0, x − cap)` in a maximisation **rewards over-delivery** (the excess earns extra), and `min(x, bar)` in a minimisation **stops
/// penalising beyond the bar** (the penalty stops growing). Both compute, both look plausible, both yield the wrong answer. The criteria therefore cover both the below-threshold
/// and above-threshold sides.
/// </remarks>
public static class TaskOnTimeReward {
    /// <summary>
    /// 计算准时奖励 / Compute the on-time reward.
    /// </summary>
    /// <param name="onTime">准时量 / The on-time amount.</param>
    /// <param name="threshold">阈值；**零表示不封顶** / The threshold; **zero means uncapped**.</param>
    /// <param name="coefficient">系数 / The coefficient.</param>
    /// <returns>奖励 / The reward.</returns>
    /// <remarks>
    /// **阈值零表示"不封顶"，不是"不给奖励"**。阈值为零时直接线性最大化，
    /// 只有阈值非零才引入封顶松弛。把它读成"奖励为 0"会让这条管线在默认参数下**什么都不做**，
    /// 而对象看上去还在最大化准时——目标是空的，求解器自然给出任意解。
    ///
 /// **A zero threshold means "uncapped", not "no reward"**. The implementation maximises linearly at a zero threshold and only introduces a capping slack when the threshold is
    /// non-zero. Reading it as "reward is zero" makes this pipeline **do nothing** at its default arguments while the caller believes on-time is still being maximised — the
    /// objective is empty, so the solver returns an arbitrary solution.
    /// </remarks>
    public static Flt64 Reward(Flt64 onTime, ulong threshold, Flt64 coefficient) {
        if (threshold == 0) {
            return coefficient * onTime;
        }

        Flt64 cap = new(threshold);
        return coefficient * (onTime < cap ? onTime : cap);
    }

    /// <summary>
    /// 判断是否需要封顶 / Whether a cap is needed.
    /// </summary>
    /// <param name="threshold">阈值 / The threshold.</param>
    /// <returns>是否需要引入封顶松弛 / Whether to introduce the capping slack.</returns>
    public static bool NeedsCap(ulong threshold) => threshold != 0;
}
