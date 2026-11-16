// 活动排程的线性行 / Linear rows of an activity schedule.
// 与固定 Kotlin 里的
// `CompiledActivityRow`，以及 里的存在性行构造对应。
//.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.Activity.Model;

/// <summary>
/// 一条编译后的线性行 / A compiled linear row.
/// </summary>
/// <param name="Id">稳定行标识 / The stable row identity.</param>
/// <param name="Coefficients">列下标到系数的映射，省略即零 / The map from column index to coefficient; absent means zero.</param>
/// <param name="Lower">可选下界 / The optional lower bound.</param>
/// <param name="Upper">可选上界 / The optional upper bound.</param>
/// <remarks>
/// **系数表里"不出现"就是零，不需要显式写零**。显式写零会让行看起来参与了某个变量，而它实际不参与——
/// 在诊断"这条约束为什么没生效"时，一串零系数比一张短表更难读。
///
/// **A missing entry means zero; explicit zeros are unnecessary.** Writing zeros makes a row look like it involves a variable that it does not, and when diagnosing "why did this
/// constraint not bite" a string of zero coefficients is harder to read than a short table.
/// </remarks>
public sealed record CompiledActivityRow(
    string Id,
    IReadOnlyDictionary<int, Flt64> Coefficients,
    Flt64? Lower = null,
    Flt64? Upper = null);

/// <summary>
/// 存在性行构造 / Presence row construction.
/// </summary>
/// <remarks>
/// 存在性规则的线性化是"**前驱侧之和 − 后继侧之和 ≤ 常数**"，其中固定事实（已执行）不进系数、只进常数。
/// 这个区分是全部要点：一个已经执行完的任务没有决策变量，把它写进系数表会让一列**并不存在**，
/// 而把它写进常数才对——但写错时模型依然可解，只是行约束了一个不存在的变量。
///
/// The linearisation of a presence rule is "**sum over the predecessor side − sum over the successor side ≤ constant**", where fixed facts (already executed) contribute to the
/// constant only, never to the coefficients. That distinction is the whole point: an already-executed task has no decision variable, and putting it in the coefficient table
/// references **a column that does not exist**, while putting it in the constant is correct — yet when done the wrong way the model stays solvable and the row merely
/// constrains a variable that is not there.
/// </remarks>
public static class ActivityPresenceRow {
    /// <summary>
    /// 构造一条存在性行 / Build one presence row.
    /// </summary>
    /// <param name="id">行标识 / The row identity.</param>
    /// <param name="from">减项一侧的候选：下标为空表示固定事实 / The subtracted side; a null index denotes a fixed fact.</param>
    /// <param name="to">被减一侧的候选：下标为空表示固定事实 / The side being subtracted; a null index denotes a fixed fact.</param>
    /// <returns>线性行 / The linear row.</returns>
    /// <remarks>
    /// 参数顺序与基线 `requirePresence(from, to, suffix)` 一致：`from` 的系数取 `+1`，`to` 的系数取 `−1`，
    /// 上界为 `−(from 中固定事实数 − to 中固定事实数)`。
    ///
    /// **两侧都有固定事实时，常数会互相抵消**：这正是"已完成的前驱与已完成的后续之间无需再约束"的表达。
    ///
    /// The argument order matches its `requirePresence(from, to, suffix)`: `from` gets `+1`, `to` gets `−1`, and the upper bound is
    /// `−(fixed facts in from − fixed facts in to)`.
    ///
    /// **When both sides carry fixed facts the constants cancel**, which is exactly how "no constraint is needed between an executed predecessor and an executed successor" is
    /// expressed.
    /// </remarks>
    public static CompiledActivityRow Build(
        string id,
        IReadOnlyList<int?> from,
        IReadOnlyList<int?> to) {
        System.ArgumentNullException.ThrowIfNull(from);
        System.ArgumentNullException.ThrowIfNull(to);

        var coefficients = new Dictionary<int, Flt64>();
        foreach (int? index in from) {
            if (index is not { } value) {
                continue;
            }
            coefficients[value] = coefficients.TryGetValue(value, out Flt64 known) ? known + new Flt64(1.0) : new Flt64(1.0);
        }
        foreach (int? index in to) {
            if (index is not { } value) {
                continue;
            }
            coefficients[value] = coefficients.TryGetValue(value, out Flt64 known) ? known - new Flt64(1.0) : new Flt64(-1.0);
        }

        int fixedFrom = CountFixed(from);
        int fixedTo = CountFixed(to);
        return new CompiledActivityRow(id, coefficients, Upper: new Flt64(-(fixedFrom - fixedTo)));
    }

    /// <summary>统计固定事实的数量 / Count the fixed facts.</summary>
    /// <param name="side">一侧的候选 / The candidates on one side.</param>
    /// <returns>固定事实数 / The number of fixed facts.</returns>
    private static int CountFixed(IReadOnlyList<int?> side) {
        int count = 0;
        foreach (int? index in side) {
            if (index is null) {
                count++;
            }
        }
        return count;
    }
}
