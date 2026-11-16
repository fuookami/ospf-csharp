// 换型时间的取值 / The value of a switch time.
// 由 `MaskingFunction(x = 时距, mask = thisSwitch)` 构造。
// Built as `MaskingFunction(x = gap, mask = thisSwitch)`.

using System;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;

/// <summary>
/// 换型时间的取值 / The value of a switch time.
/// </summary>
/// <remarks>
/// `switchTime[t1, t2] = thisSwitch × 时距`，其中
///
/// | 分支 | 时距 |
/// | --- | --- |
/// | 有 `taskTime` | `estimateStartTime[t2] − estimateEndTime[t1]` |
/// | 无 `taskTime` | 常量 `distance(t1.time.end → t2.time.start)` |
///
/// `thisSwitch` 是该对在**各执行器**上 `switch[e, t1, t2]` 之和，并加上 `≤ 1` 上界。
///
/// **遮蔽（mask）不是钳制（clamp）**：mask 为 0 时结果恒 0，但 mask 为 1 时会把**负时距原样传出**。
/// 判定 `frontOf[t1, t2]` 是 `start[t1] ≤ start[t2]`，它**不能推出** `end[t1] ≤ start[t2]`——
/// 两个任务在时间上重叠时，时距是负的。
///
/// 于是"换型时间"这项代价在重叠排布上会**变成负贡献**，也就是给求解器一个"让任务重叠就更省"的信号。
/// 正常模型里同一执行器的时间冲突约束会先排除重叠，所以这里没有钳制。
///
/// 这里**如此**：不钳制。见下方 `ponytail:` 说明。
///
/// `switchTime[t1, t2] = thisSwitch × gap`, where
///
/// | Branch | Gap |
/// | --- | --- |
/// | with `taskTime` | `estimateStartTime[t2] − estimateEndTime[t1]` |
/// | without it | the constant `distance(t1.time.end → t2.time.start)` |
///
/// `thisSwitch` is the sum of `switch[e, t1, t2]` **over executors**, with a `≤ 1` bound.
///
/// **Masking is not clamping**: a zero mask forces zero, but a mask of 1 passes a **negative gap through unchanged**. Its `frontOf[t1, t2]` is
/// `start[t1] ≤ start[t2]`, which **does not imply** `end[t1] ≤ start[t2]` — when two tasks overlap in time the gap is negative.
///
/// So the switch-time cost turns into a **negative contribution** on overlapping arrangements, giving the solver a signal that overlapping is cheaper. In a full model the
/// per-executor time-conflict constraints exclude overlaps first, which is why this implementation does not clamp here.
///
/// This implementation **copies the implementation**: no clamping. See the `ponytail:` note below.
/// </remarks>
public static class SwitchTimeValue {
    /// <summary>
    /// 计算时距 / Compute the gap.
    /// </summary>
    /// <param name="endOfLeft">左任务结束 / The left task's end.</param>
    /// <param name="startOfRight">右任务开始 / The right task's start.</param>
    /// <returns>时距，重叠时为负 / The gap, negative when they overlap.</returns>
    /// <remarks>
    /// ponytail: 不钳制负时距，如此；若模型里缺少时间冲突约束（或有别的路径让重叠变得可行），
    /// 换型时间会变成"重叠补贴"。升级路径：在这里钳到 0，或在调用处要求 `endOfLeft ≤ startOfRight`。
    ///
 /// ponytail: negative gaps are not clamped, copying the implementation; if the model lacks the time-conflict constraints (or some path makes overlap feasible), switch time
    /// becomes an "overlap subsidy". Upgrade path: clamp to 0 here, or require `endOfLeft ≤ startOfRight` at the call site.
    /// </remarks>
    public static double Gap(double endOfLeft, double startOfRight) => startOfRight - endOfLeft;

    /// <summary>
    /// 用遮蔽量取值 / Apply the mask.
    /// </summary>
    /// <param name="gap">时距 / The gap.</param>
    /// <param name="mask">遮蔽量（0 或 1）/ The mask (0 or 1).</param>
    /// <returns>遮蔽后的值 / The masked value.</returns>
    /// <remarks>
    /// **遮蔽量捎带符号**：mask 为 0 时不管时距是正是负都得到 0。若实现写成 `mask == 1 ? gap : 0` 之外的形态
    /// （比如先取绝对值再遮蔽），负时距会变成正的换型时间，凭空多罚。
    ///
    /// **The mask carries the sign**: with a zero mask the result is 0 whether the gap is positive or negative. Any form other than `mask == 1 ? gap : 0` (taking an absolute
    /// value first, say) turns a negative gap into a positive switch time and over-penalises out of nowhere.
    /// </remarks>
    public static double Masked(double gap, int mask) => mask == 0 ? 0.0 : gap;

    /// <summary>
    /// 判断遮蔽量之和是否越界 / Whether the summed mask exceeds its bound.
    /// </summary>
    /// <param name="occurrences">各执行器上的换型指示量之和 / The summed switch indicators over executors.</param>
    /// <returns>是否越界 / Whether the bound is exceeded.</returns>
    /// <remarks>
    /// `thisSwitch` 有 `≤ 1` 上界：一对任务只可能在**一个**执行器上相邻。
    /// 这个上界不要紧——它约束的是"同一对任务在两个执行器上同时换型"，那在指派互斥下本来就不可行。
    ///
 /// The implementation bounds `thisSwitch` by `≤ 1`: a task pair can be adjacent on **one** executor only. The bound is a no-op under assignment exclusivity — it rules out "the same
    /// pair switching on two executors at once", which is already infeasible.
    /// </remarks>
    public static bool ExceedsBound(int occurrences) => occurrences > 1;
}
