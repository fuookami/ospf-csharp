#nullable enable

using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Report;

namespace Fuookami.Ospf.Core.Analysis;

/// <summary>原始模型报告边界的证据类型 / Evidence kind used at the original-model report boundary.</summary>
/// <remarks>
/// 逐成员对应 `ActivityEvidenceKind`。
/// 逐成员对应 `ActivityEvidenceKind` / One member per `ActivityEvidenceKind`.
/// </remarks>
public enum ActivityEvidenceKind
{
    /// <summary>带标量松弛的整数比较 / An integer comparison with a scalar slack metric.</summary>
    Comparison,

    /// <summary>变量下界或上界 / A variable lower or upper bound.</summary>
    VariableBound,

    /// <summary>无标量松弛的稀疏整数值域 / A sparse integer domain without a scalar slack metric.</summary>
    SparseDomain,

    /// <summary>无标量松弛的 CP 语义约束 / A semantic CP constraint without a scalar slack metric.</summary>
    Semantic,

    /// <summary>无法完成求值 / Evaluation was not possible.</summary>
    Unknown,
}

/// <summary>
/// 原始模型一条活动性记录的证据 / Original-model evidence for one activity record.
/// </summary>
/// <remarks>
/// **数值以 `double` 暴露以便报告互操作，同时保留 `Int64` 的精确字符串**——
/// 调用方不该把舍入过的浮点值再拿去做 CP 输入。这条不是洁癖：`9007199254740993` 走一趟 `double` 就变成
/// `9007199254740992`，**而两者作为约束边界是完全不同的两个模型。**
///
/// **Values are exposed as `double` for report interoperability while the exact `Int64` spelling is retained** —
/// a consumer should not feed a rounded float back into CP. This is not fastidiousness: `9007199254740993` through a `double` becomes `9007199254740992`, and **as a constraint
/// boundary those are two different models.**
/// </remarks>
/// <param name="Source">原始稳定来源 / Original stable source.</param>
/// <param name="Kind">证据类型 / Evidence kind.</param>
/// <param name="Lhs">适用时的左侧求值 / Evaluated left-hand side, when applicable.</param>
/// <param name="Rhs">适用时的右侧或边界值 / Right-hand side or bound, when applicable.</param>
/// <param name="Relation">适用时的比较关系 / Comparison relation, when applicable.</param>
/// <param name="Value">适用时的变量求值 / Evaluated variable value, when applicable.</param>
/// <param name="Boundary">适用时的变量或值域边界 / Variable or domain boundary, when applicable.</param>
/// <param name="ExactLhs">精确左侧值字符串 / Exact left-hand-side spelling.</param>
/// <param name="ExactRhs">精确右侧值字符串 / Exact right-hand-side spelling.</param>
/// <param name="ExactValue">精确变量值字符串 / Exact variable-value spelling.</param>
/// <param name="ExactBoundary">精确边界值字符串 / Exact boundary spelling.</param>
/// <param name="Satisfied">求值完成时的精确语义结果 / Exact semantic satisfaction result, when evaluated.</param>
/// <param name="Message">双语诊断详情，通常用于 `Unknown` / Bilingual diagnostic detail, normally used for `Unknown`.</param>
public sealed record ActivityEvidence(
    DiagnosticSource Source,
    ActivityEvidenceKind Kind,
    double? Lhs = null,
    double? Rhs = null,
    ConstraintProgrammingComparison? Relation = null,
    double? Value = null,
    double? Boundary = null,
    string? ExactLhs = null,
    string? ExactRhs = null,
    string? ExactValue = null,
    string? ExactBoundary = null,
    bool? Satisfied = null,
    string? Message = null);
