#nullable enable

using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Analysis;

/// <summary>冲突解释的最小性结论 / Minimality conclusion for a conflict explanation.</summary>
public enum ConflictMinimality
{
    /// <summary>每个保留来源均通过已证明的删除复验 / Every remaining source passed a proven deletion check.</summary>
    Irreducible,

    /// <summary>至少一个删除复验未形成证明 / At least one deletion check was not proven.</summary>
    Partial,

    /// <summary>未请求或未执行删除复验 / No deletion checks were requested or performed.</summary>
    NotChecked,
}

/// <summary>完整目标冲突的有效性 / Validity of the full target conflict.</summary>
public enum ConflictValidity
{
    /// <summary>目标不可行已被证明 / Target infeasibility was proven.</summary>
    Verified,

    /// <summary>目标可行性未知或不受支持 / Target feasibility was unknown or unsupported.</summary>
    Unknown,
}

/// <summary>一次删除复验结果 / One deletion verification result.</summary>
/// <param name="Source">尝试删除的候选来源 / Candidate source considered for deletion.</param>
/// <param name="Status">从活动背景移除候选后的状态 / Status after removing the candidate from the active background.</param>
/// <param name="Removed">候选是否实际被移除 / Whether the candidate was actually removed.</param>
/// <param name="Retained">候选是否仍在当前冲突中 / Whether the candidate remains in the current conflict.</param>
/// <param name="TargetFixed">此复验中 target 仍保持固定 / Target remains fixed in this verification.</param>
/// <param name="Message">复验详情 / Verification detail.</param>
public sealed record ConflictVerification(
    DiagnosticSource Source,
    AnalysisStatus Status,
    bool Removed,
    bool Retained,
    bool TargetFixed = true,
    string? Message = null);

/// <summary>冲突按组统计 / Group-level conflict counts.</summary>
/// <param name="Group">组名称，<c>null</c> 表示未分组 / Group name; null means ungrouped.</param>
/// <param name="Count">组内剩余冲突来源数 / Number of remaining conflict sources in this group.</param>
public sealed record ConflictGroupSummary(string? Group, int Count);

/// <summary>
/// 一个固定目标条件的冲突解释 / Conflict explanation for one fixed objective target.
/// </summary>
/// <remarks>
/// `AvailableEvidence` 是完整的原始激活集合；`Members` 是当前冲突/MUS 候选，**绝不包含目标**。
///
/// **B976 只搬报告侧**：基线第 630 行有 `solver.solve(...)`，第 755 行有 `session.solve(assumptions = ...)`，
/// 而 `ConflictAnalyzer` 与 `MinimalConflictAnalyzer` 都在这之后。**那部分要真实 CP 后端，没搬也没空跑。**
///
/// `AvailableEvidence` is the complete original activation universe; `Members` is the current conflict/MUS candidate and **never includes the target**. **B976 ports the
/// report side only**: baseline line 630 has `solver.solve(...)` and line 755 has `session.solve(assumptions = ...)`, and both `ConflictAnalyzer` and `MinimalConflictAnalyzer`
/// come after them. **That needs a real CP backend, so it was neither ported nor stubbed.**
/// </remarks>
/// <param name="Target">固定的目标条件 / Fixed objective target.</param>
/// <param name="TargetFeasibility">所有证据激活时的初始目标可行性结果 / Initial target-feasibility result with all evidence active.</param>
/// <param name="ExtractionTier">后端实际选择的冲突提取路径 / Extraction path actually selected for the backend.</param>
/// <param name="Status">目标聚合状态 / Aggregate target status.</param>
/// <param name="AvailableEvidence">完整原始证据全集 / Complete original evidence universe.</param>
/// <param name="Members">当前冲突或 MUS 成员 / Current conflict or MUS members.</param>
/// <param name="TargetSource">固定目标的稳定来源 / Stable source for the fixed target.</param>
/// <param name="Validity">初始目标冲突是否已证明 / Whether the initial target conflict is proven.</param>
/// <param name="Minimality">基于删除的最小性状态 / Deletion-based minimality status.</param>
/// <param name="Verifications">每次删除复验记录 / Every attempted deletion verification.</param>
/// <param name="GroupSummaries">按原始约束组统计剩余成员 / Remaining members grouped by original constraint group.</param>
/// <param name="BackgroundBlocking">目标是否由固定背景而非原始约束阻塞 / Whether the target is blocked by fixed background rather than an original constraint.</param>
/// <param name="Message">面向诊断的详情 / Human-readable detail.</param>
public sealed record ConflictExplanation(
    ObjectiveTarget Target,
    TargetFeasibilityReport TargetFeasibility,
    ConflictExtractionTier ExtractionTier,
    AnalysisStatus Status,
    IReadOnlyList<DiagnosticSource> AvailableEvidence,
    IReadOnlyList<DiagnosticSource> Members,
    DiagnosticSource? TargetSource,
    ConflictValidity Validity,
    ConflictMinimality Minimality,
    IReadOnlyList<ConflictVerification>? Verifications = null,
    IReadOnlyList<ConflictGroupSummary>? GroupSummaries = null,
    bool BackgroundBlocking = false,
    string? Message = null)
{
    /// <summary>仅约束 ID；变量边界和值域仍保留在 `Members` / Constraint-only IDs; variable bounds and domains remain in `Members`.</summary>
    public IReadOnlySet<ConstraintId> ConstraintIds => Members
        .Select(source => (source as DiagnosticSource.Constraint)?.Id)
        .Where(id => id is not null)
        .Select(id => id!.Value)
        .ToHashSet();

    /// <summary>供既有 IIS 调用方使用的结构化原始成员 / Structured original members for existing IIS consumers.</summary>
    public IReadOnlySet<InfeasibilityMember> InfeasibilityMembers => Members
        .Select(source => source.AsInfeasibilityMember())
        .Where(member => member is not null)
        .Select(member => member!)
        .ToHashSet();

    /// <summary>删除复验兼容别名 / Compatibility alias for deletion checks.</summary>
    public IReadOnlyList<ConflictVerification> DeletionChecks =>
        Verifications ?? Array.Empty<ConflictVerification>();

    /// <summary>剩余成员是否全部通过已证明的删除复验 / Whether all remaining members passed proven deletion checks.</summary>
    public bool MinimalityVerified => Minimality == ConflictMinimality.Irreducible;

    /// <summary>使用集合术语的调用方别名 / Alias used by callers naming the set explicitly.</summary>
    public IReadOnlyList<DiagnosticSource> MinimalBlockingSet => Members;

    /// <summary>使用较短名称的调用方兼容别名 / Compatibility alias for callers using the shorter name.</summary>
    public ConflictExtractionTier Tier => ExtractionTier;

    /// <summary>组汇总，未给出时为空 / The group summaries, empty when absent.</summary>
    public IReadOnlyList<ConflictGroupSummary> Groups => GroupSummaries ?? Array.Empty<ConflictGroupSummary>();

    /// <summary>固定目标的稳定来源，缺省从目标条件派生 / Stable source for the fixed target, derived from the target when omitted.</summary>
    public DiagnosticSource EvidenceTargetSource => TargetSource ?? new DiagnosticSource.ObjectiveTargetSource(Target);

    /// <summary>
    /// 校验证明与原始证据不变量 / Validate proof and original-evidence invariants.
    /// </summary>
    /// <returns>结构化校验结果 / Structured validation result.</returns>
    /// <remarks>
    /// 这里挡的每一条，**都是"报告看起来有结论，但证据撑不住"的形状**：
    ///
    /// | 检查 | 不查会怎样 |
    /// | --- | --- |
    /// | 目标一致 | 冲突挂在一个目标上、可行性报告挂在另一个上 |
    /// | 证据不含目标 | **目标自己出现在"被阻塞的成员"里**，于是删掉它"冲突就没了" |
    /// | 证据不重复 | 同一个来源算两次，**让最小性看起来更强** |
    /// | 成员必须是可用证据的子集 | 成员里出现一条从未激活的证据 |
    /// | `Reachable` 不得有成员 | 一个可达目标带着一堆"阻塞成员" |
    /// | 不可约必须有逐成员证明 | **声称最小，却没有任何删除复验** |
    ///
    /// Every check here blocks a shape where **the report looks concluded but the evidence does not carry it**: a conflict attached to one target while the feasibility report
    /// describes another; **the target itself appearing among the blocking members** (so deleting it "resolves" the conflict); the same source counted twice, **making minimality look
    /// stronger**; a member that was never active; a reachable target carrying blocking members; and **claiming irreducibility with no deletion verification at all.**
    /// </remarks>
    public Try Validate() {
        Try targetValidation = Target.Validate();
        if (!targetValidation.IsOk) {
            return targetValidation;
        }

        // **目标必须两边一致**：冲突挂在 A 上、可行性报告挂在 B 上，会让结论指向一个没被分析过的目标。
        // **The target has to agree on both sides**: a conflict on A with a feasibility report on B points the conclusion at a target nobody analysed.
        if (TargetFeasibility.Target != Target) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "冲突与 target 可行性报告的目标不一致 / conflict and target-feasibility targets do not match"));
        }

        if (AvailableEvidence.Any(source => source is DiagnosticSource.ObjectiveTargetSource)) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "原始 conflict evidence 不得包含目标 / original conflict evidence must not contain the objective target"));
        }
        if (AvailableEvidence.Select(source => source.StableId).Distinct().Count() != AvailableEvidence.Count) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "原始 conflict evidence 不得重复 / original conflict evidence must not contain duplicates"));
        }

        var available = AvailableEvidence.ToHashSet();
        if (Members.Select(source => source.StableId).Distinct().Count() != Members.Count
            || Members.Any(source => source is DiagnosticSource.ObjectiveTargetSource || !available.Contains(source))) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "冲突成员必须是唯一的原始 evidence / conflict members must be unique original evidence"));
        }

        switch (Status) {
            case AnalysisStatus.Reachable:
                if (TargetFeasibility.Status != AnalysisStatus.Reachable || Members.Count != 0) {
                    return Results.Failed<Success>(new Err<ErrorCode>(
                        ErrorCode.IllegalArgument,
                        "可达 target 不得包含 blocking members / reachable target must not contain blocking members"));
                }
                break;
            case AnalysisStatus.Unreachable:
                if (TargetFeasibility.Status != AnalysisStatus.Unreachable || Validity != ConflictValidity.Verified) {
                    return Results.Failed<Success>(new Err<ErrorCode>(
                        ErrorCode.IllegalArgument,
                        "已证明不可达冲突必须保留一致的 Verified target evidence / proven unreachable conflicts require consistent Verified target evidence"));
                }
                // **背景阻塞不得宣称约束级最小性**：阻塞来自背景时，删掉任何一条原始约束都解不开它。
                // **Background blocking may not claim constraint-level minimality**: when the block comes from the background, removing any original constraint cannot lift it.
                if (BackgroundBlocking && Minimality == ConflictMinimality.Irreducible) {
                    return Results.Failed<Success>(new Err<ErrorCode>(
                        ErrorCode.IllegalArgument,
                        "背景阻塞不得宣称约束级最小性 / background blocking must not claim constraint-level minimality"));
                }
                break;
            default:
                if (TargetFeasibility.Status != Status || Validity != ConflictValidity.Unknown) {
                    return Results.Failed<Success>(new Err<ErrorCode>(
                        ErrorCode.IllegalArgument,
                        "未知或不支持的冲突必须保留相同状态 / unknown or unsupported conflicts must preserve their status"));
                }
                break;
        }

        if (Minimality == ConflictMinimality.Irreducible) {
            if (Members.Count == 0) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "不可约冲突必须至少包含一个 evidence 成员 / irreducible conflicts require at least one evidence member"));
            }

            // **逐成员都要有 target-fixed 的删除证明**：只要求"有过复验"，会让一份只验了一条的记录声称整组最小。
            // **Every member needs its own target-fixed deletion proof**: requiring only "some verification happened" lets a record that checked one member claim the whole set minimal.
            var memberSet = Members.ToHashSet();
            var proven = DeletionChecks
                .Where(check => check.TargetFixed
                    && check.Status == AnalysisStatus.Reachable
                    && check.Retained
                    && memberSet.Contains(check.Source))
                .Select(check => check.Source)
                .ToHashSet();

            if (proven.Count != Members.Count || Members.Any(member => !proven.Contains(member))) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "不可约冲突必须有每个成员的 target-fixed 删除证明 / irreducible conflicts require target-fixed deletion proofs"));
            }
        }
        return Results.OkInstance;
    }
}
