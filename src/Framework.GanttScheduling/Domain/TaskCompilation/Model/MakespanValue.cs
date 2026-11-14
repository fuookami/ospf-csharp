// 最大完工时间的取值 / The value of the makespan.
// 与固定 Kotlin 基线 `task_compilation/model/Makespan.kt` 的 `register` 对应：
// `extra` 分支用 `MinMaxFunction`，否则用 `MaxFunction`，两者都作用在 `estimateEndTime[task]` 上。
// Mirrors `register` in the pinned baseline's `task_compilation/model/Makespan.kt`: the `extra` branch uses
// `MinMaxFunction`, otherwise `MaxFunction`, both over `estimateEndTime[task]`.

using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;

/// <summary>
/// 最大完工时间的取值 / The value of the makespan.
/// </summary>
/// <remarks>
/// 基线有两个分支：`extra = true` 用 `MinMaxFunction`，否则用 `MaxFunction`。
/// 名字看着像"最大减最小"或"最小化最大值"，但读 `MinMax.kt` 的文档就清楚了：
///
/// > 最小-最大值函数：`result = max(polynomials[0], polynomials[1], ...)`
/// > 命名为"MinMax"是因为在优化上下文中它计算最大值的最小值。
///
/// 也就是说**两者的数值完全相同，都是最大值**，`extra` 只多出选择变量与辅助变量——
/// 用来在求解后**指认是哪个任务决定了最大完工时间**。
///
/// 把 `extra` 理解成"最大减最小"（跨度）是个安静的错：数值仍然是"一个完工时间"，
/// 但目标从"尽早全部做完"变成了"把排程压紧"，**滞后与否完全不再被计入**。
///
/// The baseline has two branches: `extra = true` uses `MinMaxFunction`, otherwise `MaxFunction`. The names suggest "max minus min" or "minimise the maximum", but `MinMax.kt`'s
/// own documentation settles it:
///
/// > Min-max function: `result = max(polynomials[0], polynomials[1], ...)`
/// > Named "MinMax" because it computes the minimum of the maximum values.
///
/// So **both branches produce exactly the same number — the maximum**. `extra` merely adds selector and helper variables, used after solving to **identify which task sets the
/// makespan**.
///
/// Reading `extra` as "max minus min" (the spread) is a quiet error: the number is still "a completion time", but the objective changes from "finish everything early" to
/// "squeeze the schedule together", and **lateness stops counting at all**.
/// </remarks>
public static class MakespanValue {
    /// <summary>
    /// 计算最大完工时间 / Compute the makespan.
    /// </summary>
    /// <param name="endTimes">各任务估计结束 / Each task's estimated end.</param>
    /// <returns>最大值；空列表为零 / The maximum; zero for an empty list.</returns>
    /// <remarks>
    /// **空列表取零**：基线的 `MaxFunction` 作用在空集合上没有意义，而"没有任务"的排程完工时间确实是零。
    /// 返回 `-∞` 或抛异常都会让调用方在空输入上直接崩掉，而空输入是列生成初期的常态。
    ///
    /// **An empty list yields zero**: the baseline's `MaxFunction` over an empty set is meaningless, and "no tasks" really does finish at zero. Returning `-∞` or throwing would
    /// crash the caller on empty input, which is the normal state early in column generation.
    /// </remarks>
    public static double Of(IReadOnlyList<double> endTimes) {
        ArgumentNullException.ThrowIfNull(endTimes);

        if (endTimes.Count == 0) {
            return 0.0;
        }

        double max = endTimes[0];
        for (int i = 1; i < endTimes.Count; i++) {
            if (endTimes[i] > max) {
                max = endTimes[i];
            }
        }
        return max;
    }

    /// <summary>
    /// 该分支的取值 / The value of that branch.
    /// </summary>
    /// <param name="endTimes">各任务估计结束 / Each task's estimated end.</param>
    /// <param name="extra">是否走 `MinMaxFunction` 分支 / Whether the `MinMaxFunction` branch is used.</param>
    /// <returns>最大完工时间 / The makespan.</returns>
    /// <remarks>
    /// **`extra` 不改变取值**，只改变建模时是否需要选择变量。用同一个函数表达这一点，
    /// 比写两个只差一行的实现更不容易分叉。
    ///
    /// **`extra` does not change the value**, only whether selector variables are needed when building the model. Expressing it with one function makes forking less likely than
    /// two implementations differing by a line.
    /// </remarks>
    public static double OfBranch(IReadOnlyList<double> endTimes, bool extra) {
        _ = extra;
        return Of(endTimes);
    }

    /// <summary>
    /// 该分支是否需要选择变量 / Whether that branch needs selector variables.
    /// </summary>
    /// <param name="extra">是否走 `MinMaxFunction` 分支 / Whether the `MinMaxFunction` branch is used.</param>
    /// <returns>是否需要 / Whether they are needed.</returns>
    public static bool RequiresSelectors(bool extra) => extra;
}
