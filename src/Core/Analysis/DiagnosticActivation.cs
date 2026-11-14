#nullable enable

using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Analysis;

/// <summary>原始证据的激活状态 / Activation state of one original evidence source.</summary>
public enum ActivationState
{
    /// <summary>该原始证据参与派生模型 / The original evidence participates in the derived model.</summary>
    Activated,

    /// <summary>该原始证据被移出派生模型 / The original evidence is removed from the derived model.</summary>
    Deactivated,
}

/// <summary>
/// 一条原始证据的激活记录 / Activation record for one original evidence source.
/// </summary>
/// <param name="Source">原始证据来源 / Original evidence source.</param>
/// <param name="State">激活状态 / Activation state.</param>
public sealed record DiagnosticActivation(DiagnosticSource Source, ActivationState State)
{
    /// <summary>是否为激活 / Whether this is an activated state.</summary>
    public bool IsActivated => State == ActivationState.Activated;

    /// <summary>稳定身份 / Stable identity.</summary>
    /// <remarks>
    /// **状态进身份**：同一条来源在"激活"和"停用"下是两个不同的键，否则一份改变了激活状态的报告
    /// 会和原来那份**共用缓存键**。
    ///
    /// **The state is part of the identity**: the same source under activated and deactivated is two different keys, or a report whose activation changed would **share a
    /// cache key with the one before it.**
    /// </remarks>
    public string StableId => (IsActivated ? "activated:" : "deactivated:") + Source.StableId;

    /// <summary>激活一条原始证据 / Activate one original evidence source.</summary>
    /// <param name="source">原始证据来源 / Original evidence source.</param>
    /// <returns>激活记录 / Activation record.</returns>
    public static DiagnosticActivation Activate(DiagnosticSource source) =>
        new(source ?? throw new ArgumentNullException(nameof(source)), ActivationState.Activated);

    /// <summary>停用一条原始证据 / Deactivate one original evidence source.</summary>
    /// <param name="source">原始证据来源 / Original evidence source.</param>
    /// <returns>停用记录 / Activation record.</returns>
    public static DiagnosticActivation Deactivate(DiagnosticSource source) =>
        new(source ?? throw new ArgumentNullException(nameof(source)), ActivationState.Deactivated);
}

/// <summary>
/// 后端冲突提取层级——即计划事项 K 的优先顺序 / Backend conflict-extraction tier, i.e. plan item K's priority order.
/// </summary>
/// <remarks>
/// 顺序固定为：原生 unsat core → assumption 提取 → 重复求解回退。**能力必须来自后端声明，未声明的能力一律不得被当作可用。**
///
/// The order is fixed: native unsat core, then assumption extraction, then repeated solving. **Capability must come from the backend declaration; an undeclared capability is
/// never treated as available.**
/// </remarks>
public enum ConflictExtractionTier
{
    /// <summary>后端原生 unsat core / Native backend unsat core.</summary>
    NativeUnsatCore,

    /// <summary>基于 assumption 的框架提取 / Framework extraction over assumptions.</summary>
    AssumptionExtraction,

    /// <summary>重复求解回退 / Repeated-solving fallback.</summary>
    RepeatedSolving,

    /// <summary>没有任何可用路径 / No usable path is declared.</summary>
    Unavailable,
}

/// <summary>冲突提取层级的判定与命名 / Tier predicates and stable names.</summary>
public static class ConflictExtractionTiers
{
    /// <summary>该层级是否避免重复求解 / Whether this tier avoids repeated solving.</summary>
    /// <param name="tier">层级 / The tier.</param>
    /// <returns>是否为原生层级 / Whether it is the native tier.</returns>
    public static bool IsNative(this ConflictExtractionTier tier) => tier == ConflictExtractionTier.NativeUnsatCore;

    /// <summary>该层级是否可用 / Whether the tier is usable.</summary>
    /// <param name="tier">层级 / The tier.</param>
    /// <returns>是否可用 / Whether it is usable.</returns>
    public static bool IsAvailable(this ConflictExtractionTier tier) => tier != ConflictExtractionTier.Unavailable;

    /// <summary>稳定层级名称 / Stable tier name.</summary>
    /// <param name="tier">层级 / The tier.</param>
    /// <returns>稳定名称 / The stable name.</returns>
    public static string TierName(this ConflictExtractionTier tier) => tier switch
    {
        ConflictExtractionTier.NativeUnsatCore => "native-unsat-core",
        ConflictExtractionTier.AssumptionExtraction => "assumption-extraction",
        ConflictExtractionTier.RepeatedSolving => "repeated-solving",
        _ => "unavailable",
    };

    /// <summary>
    /// 依据能力矩阵选择层级 / Select a tier from a capability matrix.
    /// </summary>
    /// <param name="matrix">分析能力矩阵 / Analysis capability matrix.</param>
    /// <returns>选择的冲突提取层级 / Selected conflict-extraction tier.</returns>
    /// <remarks>
    /// **`Conditional` 不许冒充原生能力**：只有矩阵明确 `IsNative`（或显式 `Supported`）才进前两级。
    /// 否则一个"可能支持"声明会让上层走一条**实际没实现的**原生路径。
    ///
    /// **`Conditional` never masquerades as a native capability**: only an explicit `IsNative` (or an explicit `Supported`) reaches the first two tiers. Otherwise a
    /// "might support" declaration sends the layer above down **a native path that is not implemented.**
    /// </remarks>
    public static ConflictExtractionTier Select(CapabilityMatrix matrix) {
        ArgumentNullException.ThrowIfNull(matrix);

        if (matrix.IsNative(AnalysisCapability.NativeUnsatCore)
            || matrix.Support(AnalysisCapability.NativeUnsatCore) == CapabilitySupport.Supported) {
            return ConflictExtractionTier.NativeUnsatCore;
        }
        if (matrix.IsNative(AnalysisCapability.AssumptionSolving)
            || matrix.Support(AnalysisCapability.AssumptionSolving) == CapabilitySupport.Supported) {
            return ConflictExtractionTier.AssumptionExtraction;
        }
        if (matrix.FallbackSatisfactionOnly
            && matrix.Support(AnalysisCapability.Conflict) != CapabilitySupport.Unsupported) {
            return ConflictExtractionTier.RepeatedSolving;
        }
        return ConflictExtractionTier.Unavailable;
    }
}

/// <summary>
/// 一次求解中激活的原始证据集合 / The set of original evidence active in one solve.
/// </summary>
/// <remarks>
/// **这是框架层的求解请求描述，与任何后端表示无关**：后端只看到派生模型或 assumption，
/// 公开报告只看到这里的原始 `DiagnosticSource`。
///
/// **A framework-level request description independent of any backend representation**: the backend only sees a derived model or assumptions, while public reports only ever see
/// the original `DiagnosticSource` values.
/// </remarks>
public sealed class DiagnosticActivationSet
{
    private readonly List<DiagnosticActivation> _entries;

    private DiagnosticActivationSet(List<DiagnosticActivation> entries) => _entries = entries;

    /// <summary>全部记录（只读）/ Every record (read-only).</summary>
    public IReadOnlyList<DiagnosticActivation> Activations => _entries.ToArray();

    /// <summary>记录数量 / Number of records.</summary>
    public int Size => _entries.Count;

    /// <summary>集合是否为空 / Whether the set has no records.</summary>
    public bool IsEmpty => _entries.Count == 0;

    /// <summary>
    /// 记录一条激活状态，后者覆盖前者 / Record an activation state, overriding any earlier one.
    /// </summary>
    /// <param name="activation">要记录的激活状态 / Activation state to record.</param>
    public void Put(DiagnosticActivation activation) {
        ArgumentNullException.ThrowIfNull(activation);
        _entries.RemoveAll(existing => existing.Source == activation.Source);
        _entries.Add(activation);
    }

    /// <summary>激活一条来源 / Activate a source.</summary>
    /// <param name="source">原始证据来源 / Original evidence source.</param>
    public void Activate(DiagnosticSource source) => Put(DiagnosticActivation.Activate(source));

    /// <summary>停用一条来源 / Deactivate a source.</summary>
    /// <param name="source">原始证据来源 / Original evidence source.</param>
    public void Deactivate(DiagnosticSource source) => Put(DiagnosticActivation.Deactivate(source));

    /// <summary>
    /// 某个来源是否激活 / Whether a source is active.
    /// </summary>
    /// <param name="source">要检查的原始证据来源 / Original evidence source to check.</param>
    /// <returns>来源是否激活 / Whether the source is active.</returns>
    /// <remarks>
    /// **未出现的来源按未激活处理**：调用方必须先显式声明其证据全集，否则"没提到"会变成默认可用。
    /// **An absent source is inactive**: callers must declare their evidence universe explicitly, or "not mentioned" becomes "available by default".
    /// </remarks>
    public bool IsActive(DiagnosticSource source) =>
        _entries.Any(entry => entry.Source == source && entry.IsActivated);

    /// <summary>全部激活来源 / Every active source.</summary>
    /// <returns>激活来源列表 / Active source list.</returns>
    public IReadOnlyList<DiagnosticSource> ActiveSources() =>
        _entries.Where(entry => entry.IsActivated).Select(entry => entry.Source).ToArray();

    /// <summary>全部激活的原始约束 ID / Every active original constraint ID.</summary>
    /// <returns>激活的原始约束 ID 集合 / Active original constraint IDs.</returns>
    public IReadOnlySet<ConstraintId> ActiveConstraintIds() =>
        _entries.Where(entry => entry.IsActivated)
            .Select(entry => (entry.Source as DiagnosticSource.Constraint)?.Id)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .ToHashSet();

    /// <summary>全部停用来源 / Every deactivated source.</summary>
    /// <returns>停用来源列表 / Deactivated source list.</returns>
    public IReadOnlyList<DiagnosticSource> DeactivatedSources() =>
        _entries.Where(entry => !entry.IsActivated).Select(entry => entry.Source).ToArray();

    /// <summary>
    /// 把后端返回的不可行证据成员回映为原始证据 / Remap backend infeasibility evidence back to original evidence.
    /// </summary>
    /// <param name="members">后端返回的不可行证据成员 / Backend infeasibility evidence members.</param>
    /// <returns>回映后的原始证据列表 / Remapped original evidence list.</returns>
    /// <remarks>
    /// **以本激活集合为唯一来源**：先枚举激活的原始证据，再检查后端证据是否指向其中之一。
    /// **后端返回的任何未知成员都会被丢弃**——所以求解器生成的辅助元素不可能出现在结果里。
    ///
    /// **This activation set is the only source**: the active original evidence is enumerated first, then checked against the backend members. **Any unrecognised backend member is
    /// dropped**, so a solver-generated auxiliary element can never appear in the result.
    /// </remarks>
    public IReadOnlyList<DiagnosticSource> RemapBackendEvidence(IReadOnlyCollection<InfeasibilityMember> members) {
        ArgumentNullException.ThrowIfNull(members);
        var wanted = members.ToHashSet();
        return ActiveSources()
            .Where(source => source.AsInfeasibilityMember() is { } member && wanted.Contains(member))
            .ToArray();
    }

    /// <summary>
    /// 校验激活集合不变量 / Validate activation-set invariants.
    /// </summary>
    /// <returns>结构化校验结果 / Structured validation result.</returns>
    /// <remarks>
    /// 公开证据边界在这里强制：任何来源都必须能回映为原始模型身份，且不得重复。
    /// The public evidence boundary is enforced here: every source maps back to an original-model identity and none repeats.
    /// </remarks>
    public Try Validate() {
        var seen = new HashSet<string>();
        foreach (DiagnosticActivation entry in _entries) {
            if (!seen.Add(entry.Source.StableId)) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "激活集合包含重复来源 / Activation set contains a duplicate source: " + entry.Source.StableId));
            }
        }
        return Results.OkInstance;
    }

    /// <summary>空集合 / An empty set.</summary>
    /// <returns>空的激活集合 / Empty activation set.</returns>
    public static DiagnosticActivationSet Empty() => new([]);

    /// <summary>
    /// 从快照构造：所有原始约束与固定背景证据均激活 / Build from a snapshot: every original constraint and fixed-background source is active.
    /// </summary>
    /// <param name="snapshot">CP 模型 snapshot / CP model snapshot.</param>
    /// <returns>包含原始证据激活记录的集合 / Set containing original-evidence activation records.</returns>
    /// <remarks>
    /// 变量边界按两侧分别登记；稀疏值域额外登记为 `DiagnosticSource.SparseDomain`。
    /// Variable bounds are registered per side, and a sparse domain is additionally registered as `DiagnosticSource.SparseDomain`.
    /// </remarks>
    public static DiagnosticActivationSet FromSnapshot(ConstraintProgrammingModelSnapshot snapshot) {
        ArgumentNullException.ThrowIfNull(snapshot);

        DiagnosticActivationSet set = Empty();
        foreach (ConstraintProgrammingConstraintSnapshot constraint in snapshot.Constraints) {
            set.Activate(new DiagnosticSource.Constraint(constraint.Id));
        }
        foreach (ConstraintProgrammingVariableSnapshot variable in snapshot.Variables) {
            set.Activate(new DiagnosticSource.VariableLowerBound(variable.Id));
            set.Activate(new DiagnosticSource.VariableUpperBound(variable.Id));
            if (variable.Domain is IntegerDomain.Values) {
                set.Activate(new DiagnosticSource.SparseDomain(variable.Id));
            }
        }
        return set;
    }
}
