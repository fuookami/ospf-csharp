#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using System;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Core.Analysis;

/// <summary>
/// 目标值到 CP 整数边界的精确映射 / The exact mapping from an objective target to a CP integer bound.
/// </summary>
/// <remarks>
/// **这是 `TargetFeasibilityAnalyzer.targetIntegerBound` 的移植。**
///
/// **舍入方向是全部价值所在：**
///
/// | 关系 | 舍入 | 理由 |
/// | --- | --- | --- |
/// | `AtLeast`（≥ v） | **向上取整** | 需要**最小的**整数 ≥ v |
/// | `AtMost`（≤ v） | **向下取整** | 需要**最大的**整数 ≤ v |
///
/// **方向反了不会报错，只会让可行域悄悄变宽或变窄：**
/// `AtLeast` 用向下取整时，"≥ 2.7" 会变成 "≥ 2"——**于是一个目标值 2 的不可行解会被判为可行。**
///
/// **Getting the direction wrong raises nothing; it quietly widens or narrows the feasible region:** with floor, `AtLeast` turns "≥ 2.7" into "≥ 2" — **making an infeasible solution with
/// objective 2 pass as feasible.**
///
/// **超出 `long` 范围时返回 `null`，不是抛异常。** 那是"这个目标无法用 CP 的整数边界表达"，
/// **是一个合法的分析结论（`Unsupported`），不是程序错误。**
///
/// **Out of `long` range returns `null` instead of throwing.** That means "this target cannot be expressed as a CP integer bound" — **a legitimate analysis result (`Unsupported`), not a program error.**
/// </remarks>
public static class ObjectiveTargetBounds
{
    /// <summary>
    /// 把目标映射成 CP 的整数边界 / Map a target to a CP integer bound.
    /// </summary>
    /// <param name="target">目标 / The target.</param>
    /// <returns>整数边界，无法精确表达时为 <c>null</c> / The integer bound, or <c>null</c> when it cannot be expressed exactly.</returns>
    public static Int64? IntegerBound(ObjectiveTarget target) {
        ArgumentNullException.ThrowIfNull(target);

        double value = target.Value.ToDouble();

        // **NaN 与无穷没有整数边界。** 它们不是"很大"或"很小"，**而是"不是数"**——
        // 舍入任何一个都会给出一个看起来合法的整数。
        // **NaN and infinity have no integer bound.** They are not "very large" or "very small" — **they are not numbers** — and rounding either yields an integer that looks legitimate.
        if (double.IsNaN(value) || double.IsInfinity(value)) {
            return null;
        }

        double rounded = target.Relation switch
        {
            ObjectiveTargetRelation.AtLeast => global::System.Math.Ceiling(value),
            ObjectiveTargetRelation.AtMost => global::System.Math.Floor(value),
            _ => double.NaN,
        };

        // **上界的比较必须是严格小于 `(double)long.MaxValue`。**
        // `long.MaxValue` 转成 `double` 恰好是 2^63，**而 2^63 已经超出 `long` 一位**——
        // 写成 `rounded > long.MaxValue` 会放过这个值，**于是 `(long)rounded` 溢出成 `long.MinValue`**：
        // 一个巨大的正目标变成一个巨大的负目标，**而两者都会被当成合法边界。**
        //
        // **The upper comparison has to be strictly less than `(double)long.MaxValue`.** `long.MaxValue` as a `double` is exactly 2^63, **which is one past `long`** — writing `rounded > long.MaxValue` lets it
        // through, **so `(long)rounded` overflows to `long.MinValue`**: a huge positive target becomes a huge negative one, **and both look like legal bounds.**
        const double upperExclusive = 9223372036854775808.0;
        if (double.IsNaN(rounded) || rounded < -upperExclusive || rounded >= upperExclusive) {
            return null;
        }

        return new Int64((long)rounded);
    }

    /// <summary>
    /// 目标无法精确映射时的报告 / The report produced when a target cannot be mapped exactly.
    /// </summary>
    /// <param name="target">目标 / The target.</param>
    /// <returns>一份 `Unsupported` 报告 / An `Unsupported` report.</returns>
    /// <remarks>
    /// **`Unsupported` 而不是 `Unknown`，也不是失败。** "这个目标表达不出来"与求解器无关，
    /// **换个求解器也不会变；但它也不是"算错了"。**
    ///
    /// **`Unsupported`, not `Unknown`, and not a failure.** "This target cannot be expressed" has nothing to do with the solver, **so swapping solvers cannot help; but it is also not "computed wrong".**
    /// </remarks>
    public static TargetFeasibilityReport UnsupportedReport(ObjectiveTarget target) {
        ArgumentNullException.ThrowIfNull(target);

        return new TargetFeasibilityReport(
            Target: target,
            Status: AnalysisStatus.Unsupported,
            Message: "ObjectiveTarget 无法精确映射为 CP Int64 边界 / ObjectiveTarget cannot be mapped exactly to a CP Int64 bound");
    }
}
