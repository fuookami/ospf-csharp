#nullable enable

using Fuookami.Ospf.Core.Analysis;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Math.Algebra.Number;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Core.Plugin.Gurobi;

/// <summary>
/// **把 Gurobi 的原始 ranging 属性折算成报告里的区间** /
/// Turn Gurobi's raw ranging attributes into the ranges a report carries.
/// </summary>
/// <remarks>
/// ### 三件事必须在这里做完
///
/// 1. **哨兵与无界不是区间。** **presolve 打开时返回哨兵，而无界的一侧返回 `±inf`——
///    两者在数值上都看起来像一个合法端点**（基线实测：`c1` 得到 4 而不是 0）。
///    **所以只接受有限的端点，`±inf` 与 `NaN` 折算成"该侧没有边界"。**
/// 2. **两侧都没有边界的条目整个丢掉**，而不是留一个 `(null, null)` 的空壳——
///    **那会让"这个约束没有范围信息"与"这个约束完全不受 RHS 影响"混为一谈。**
/// 3. **按行序配对。** **实测发现这条 triad 路径建出来的 `GRBConstr` 名字是空的，
///    所以"名字就是 `ConstraintId`"不成立**——**按名字索引会得到空字典，而它看起来像"没有范围"。**
///    **行序由共享层给出，它与报告的求值顺序一致。**
///
/// **Three things finish here: sentinels and unbounded sides are not ranges (only finite endpoints are accepted, so
/// `±inf`/`NaN` become "no bound on that side"); a pair with neither bound is dropped rather than kept as an empty shell that
/// would make "no range information" indistinguishable from "unaffected by the RHS"; and pairing is by row order, because
/// measurement showed the `GRBConstr`s on this triad path carry empty names, so indexing by name yields an empty dictionary that
/// looks like "no ranges".**
/// </remarks>
public static class GurobiSensitivityRangeMapper
{
    /// <summary>按行序折算出按 `ConstraintId` 的区间 / Fold raw attributes into ranges using the given row order.</summary>
    /// <param name="raw">按行号的原始属性 / Raw attributes keyed by row index.</param>
    /// <param name="rowOrder">每行对应的约束 / The constraint each row belongs to.</param>
    /// <returns>按约束的区间 / The ranges, keyed by constraint.</returns>
    public static IReadOnlyDictionary<ConstraintId, SensitivityRange?> ToRanges(
        IReadOnlyDictionary<int, (double? Lower, double? Upper)> raw,
        IReadOnlyList<ConstraintId> rowOrder) {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(rowOrder);

        var ranges = new Dictionary<ConstraintId, SensitivityRange?>();
        foreach (KeyValuePair<int, (double? Lower, double? Upper)> pair in raw) {
            if (pair.Key < 0 || pair.Key >= rowOrder.Count) {
                // **行号超出报告的行数 = 这一次的对应关系不成立，不能猜。**
                // **A row index beyond the report's rows means the correspondence does not hold this time; never guess.**
                continue;
            }

            Flt64? lower = Finite(pair.Value.Lower);
            Flt64? upper = Finite(pair.Value.Upper);
            if (lower is null && upper is null) {
                continue;
            }

            ranges[rowOrder[pair.Key]] = new SensitivityRange(lower, upper);
        }
        return ranges;
    }

    private static Flt64? Finite(double? value) {
        if (value is not { } raw) {
            return null;
        }
        if (double.IsNaN(raw) || double.IsInfinity(raw)) {
            return null;
        }
        return new Flt64(raw);
    }
}