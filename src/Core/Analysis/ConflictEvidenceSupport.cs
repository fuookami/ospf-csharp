#nullable enable

using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Core.Analysis;

/// <summary>
/// 冲突解释的纯函数部分 / The pure part of conflict explanation.
/// </summary>
/// <remarks>
/// `ConflictAnalyzer` 的 `sourceOf` 与 `groupSummaries`。**两个种子策略（原生 IIS、假设式）没搬。**
///
/// Ported from `sourceOf` and `groupSummaries` of `ConflictAnalyzer`. **The two seed strategies (native IIS, assumption-based) are not implemented.**
/// </remarks>
public static class ConflictEvidenceSupport
{
    /// <summary>
    /// 把一个不可行成员映射成证据来源 / Map one infeasibility member to its evidence source.
    /// </summary>
    /// <param name="member">不可行成员 / The infeasibility member.</param>
    /// <returns>证据来源 / The evidence source.</returns>
    /// <remarks>
    /// **三种成员的映射互不相同，其中两处最容易写错：**
    ///
    /// | 成员 | 来源 | 写错会怎样 |
    /// | --- | --- | --- |
    /// | 约束 | `Constraint` | — |
    /// | 变量边界 | 按 `Side` 分下界/上界 | **下界和上界合并成一个，用户不知道该松哪一边** |
    /// | 变量值域 | `SparseDomain`，**不是边界** | **指错位置：让人去调边界，而问题在值域** |
    ///
    /// **三种 members map differently, and two of them are easy to get wrong**: collapsing a lower and an upper bound into one leaves the user unable to tell which side to relax,
    /// and reporting a sparse domain as a bound **points at an edit that would not help.**
    /// </remarks>
    public static DiagnosticSource SourceOf(InfeasibilityMember member) {
        ArgumentNullException.ThrowIfNull(member);

        return member switch {
            InfeasibilityMember.ConstraintMember constraint => new DiagnosticSource.Constraint(constraint.Id),
            InfeasibilityMember.VariableBoundMember bound => bound.Ref.Side == BoundSide.Lower
                ? new DiagnosticSource.VariableLowerBound(bound.Ref.VariableId)
                : new DiagnosticSource.VariableUpperBound(bound.Ref.VariableId),
            InfeasibilityMember.VariableDomainMember domain => new DiagnosticSource.SparseDomain(domain.Ref.VariableId),
            _ => throw new ArgumentOutOfRangeException(nameof(member), member, "未知的不可行成员 / unknown infeasibility member"),
        };
    }

    /// <summary>
    /// 按原始约束组汇总证据 / Summarize the evidence by original constraint group.
    /// </summary>
    /// <param name="snapshot">基线快照 / The baseline snapshot.</param>
    /// <param name="sources">证据来源 / The evidence sources.</param>
    /// <returns>按组计数 / Per-group counts.</returns>
    /// <remarks>
    /// **只有 `Constraint` 来源能落到组上**：变量边界与值域都不带组名，
    /// **而找不到组名的约束也不计入任何一组**（不是计入一个 `null` 组）。
    ///
    /// 这个区别会直接改变读法：**一份"某组有 3 条冲突"的汇总，说的是三条有组名的约束**，
    /// 不是"这三条里有一条组名未知"。把它们混进同一行，用户就会去查一个不存在的组。
    ///
    /// **Only `Constraint` sources can be attributed to a group**: variable bounds and domains carry no group name, and **a constraint whose group name is absent is not counted
    /// in any group** — it does not become a `null` group. That distinction changes how the summary reads: **"3 conflicts in this group" means three constraints that have a
    /// group**, not "three of which one has an unknown group"; merging them would send the user looking for a group that does not exist.
    /// </remarks>
    public static IReadOnlyList<ConflictGroupSummary> GroupSummaries(
        ConstraintProgrammingModelSnapshot snapshot,
        IReadOnlyList<DiagnosticSource> sources) {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(sources);

        var groupOf = snapshot.Constraints
            .ToDictionary(entry => entry.Id.Value, entry => entry.GroupName);

        var counts = new List<(string Group, int Count)>();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (DiagnosticSource source in sources) {
            if (source is not DiagnosticSource.Constraint constraint) {
                // 变量边界与值域不带组名，不参与按组汇总。
                // Variable bounds and domains carry no group name and do not take part.
                continue;
            }
            if (!groupOf.TryGetValue(constraint.Id.Value, out string? group) || string.IsNullOrEmpty(group)) {
                // **组名缺失就不计入任何一组**，不塞进一个 `null` 行。
                // **A missing group name is not counted anywhere**, rather than being pushed into a `null` row.
                continue;
            }
            if (index.TryGetValue(group, out int existing)) {
                counts[existing] = (group, counts[existing].Count + 1);
            }
            else {
                index[group] = counts.Count;
                counts.Add((group, 1));
            }
        }

        return counts.Select(entry => new ConflictGroupSummary(entry.Group, entry.Count)).ToArray();
    }

    /// <summary>
    /// 后端没有冲突能力时的解释 / The explanation when the backend has no conflict support.
    /// </summary>
    /// <param name="snapshot">基线快照 / The baseline snapshot.</param>
    /// <param name="target">目标 / The target.</param>
    /// <param name="tier">提取路径 / The extraction path.</param>
    /// <returns>状态为 Unsupported 的解释 / An explanation whose status is Unsupported.</returns>
    /// <remarks>
    /// **三处与"正常冲突解释"不同，每一处都是刻意的：**
    ///
    /// | 字段 | 这里 | 正常路径 | 为什么 |
    /// | --- | --- | --- | --- |
    /// | `Validity` | `Unknown` | `Verified` | **什么都没证过** |
    /// | `Minimality` | `NotChecked` | 由收缩定 | **一次复验都没跑** |
    /// | **`Members`** | **等于全部证据** | 收缩后的成员 | **没有依据排除任何一条** |
    /// | **`TargetFixed`** | **`false`** | `true` | **目标根本没被固定住去验证** |
    ///
    /// **`Members` 取全集最容易写错**：空集合看起来更"诚实"（"没有证出任何成员"），
    /// **但它会让下游按"最小冲突为空"去读，而真相是"这次分析根本没做"。**
    /// 取全集的意思是"这些都可能有关，只是没证"，**这与 `Unsupported` 的状态是一致的。**
    ///
    /// **`TargetFixed = false` 也是刻意的**：正常路径固定目标后才谈冲突，
    /// **这里目标没有被固定，把它写成 `true` 会让下游以为做过那一步。**
    ///
    /// **Three fields differ from the normal path on purpose.** **`Members` as the whole evidence set is the easy one to get wrong**: an empty set looks more honest, but **it
    /// makes downstream read "the minimal conflict is empty", while the truth is "this analysis never ran".** The full set says "these might all matter, it was simply not proven"
    /// — **which agrees with the `Unsupported` status.**
    /// </remarks>
    public static ConflictExplanation UnsupportedExplanation(
        ConstraintProgrammingModelSnapshot snapshot,
        ObjectiveTarget target,
        ConflictExtractionTier tier) {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(target);

        // **证据全集从快照的激活集合来，与正常路径同一套推导。**
        // **The evidence universe comes from the snapshot activations, the same derivation the normal path uses.**
        var activations = DiagnosticActivationSet.FromSnapshot(snapshot);
        DiagnosticSource[] evidence = activations.ActiveSources().ToArray();

        var report = new TargetFeasibilityReport(
            Target: target,
            Status: AnalysisStatus.Unsupported,
            TargetFixed: false,
            Message: "当前求解器未声明 conflict 能力 / Solver does not declare conflict support");

        return new ConflictExplanation(
            Target: target,
            TargetFeasibility: report,
            ExtractionTier: tier,
            Status: AnalysisStatus.Unsupported,
            AvailableEvidence: evidence,
            Members: evidence,
            TargetSource: null,
            Validity: ConflictValidity.Unknown,
            Minimality: ConflictMinimality.NotChecked,
            Verifications: [],
            GroupSummaries: GroupSummaries(snapshot, evidence),
            BackgroundBlocking: false,
            Message: report.Message);
    }

    /// <summary>
    /// 一个字面量的稳定键 / The stable key of one literal.
    /// </summary>
    /// <param name="literal">字面量 / The literal.</param>
    /// <returns>稳定键 / The stable key.</returns>
    /// <remarks>
    /// **键里必须有 `negated`。** 少了它，一个假设和它的否定会是同一个键——
    /// **而"取反之后仍然不可行"和"可行"是相反的结论。**
    ///
    /// **The key has to contain `negated`.** Without it an assumption and its negation share a key — **and "still infeasible after negating" and "feasible" are opposite conclusions.**
    /// </remarks>
    public static string LiteralKey(BooleanLiteral literal) {
        ArgumentNullException.ThrowIfNull(literal);
        return $"{literal.VariableId?.Value}:{literal.Negated}:{literal.Constant}";
    }

    /// <summary>
    /// 把后端报告的冲突映射回原始证据 / Remap a backend-reported conflict onto the original evidence.
    /// </summary>
    /// <param name="snapshot">基线快照 / The baseline snapshot.</param>
    /// <param name="conflict">后端报告的冲突，可为 <c>null</c> / The backend conflict, or <c>null</c>.</param>
    /// <param name="requested">调用方请求的证据 / The evidence the caller asked about.</param>
    /// <param name="literals">假设字面量到约束的映射 / The literal-to-constraint map.</param>
    /// <returns>属于请求集合的那部分证据 / The part of the evidence that is in the requested set.</returns>
    /// <remarks>
    /// **最后一步是 `requested.filter { it in wanted }`，不是直接返回 `wanted`。**
    ///
    /// 后端会报告它自己认定的一切，**包括调用方没问的东西**。直接返回 `wanted`，
    /// **一份"关于目标 X 的冲突解释"里会冒出目标 Y 的成员**——而读的人没有任何线索知道那不属于这次分析。
    ///
    /// **The last step is `requested.filter { it in wanted }`, not returning `wanted`.** The backend reports everything it considers relevant, **including things the caller never
    /// asked about**; returning `wanted` directly would let **an explanation about target X carry a member belonging to target Y**, with nothing telling the reader it is not part
    /// of this analysis.
    /// </remarks>
    public static IReadOnlyList<DiagnosticSource> RemapBackendConflict(
        ConstraintProgrammingModelSnapshot snapshot,
        ConstraintProgrammingConflict? conflict,
        IReadOnlyList<DiagnosticSource> requested,
        IReadOnlyDictionary<ConstraintId, BooleanLiteral>? literals = null) {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(requested);

        if (conflict is null) {
            return [];
        }

        var wanted = new HashSet<DiagnosticSource>();

        // 1. 后端直接给出的成员。
        // 1. The members the backend named directly.
        foreach (InfeasibilityMember member in conflict.Members) {
            wanted.Add(SourceOf(member));
        }

        // 2. 后端点名的约束 id。**未注册的 id 直接跳过**：它不在这个模型里，映射不出原始证据。
        // 2. Constraint ids the backend named. **An unregistered id is skipped**: it is not in this model, so there is no original evidence to map it onto.
        var knownConstraints = new HashSet<string>(snapshot.Constraints.Select(entry => entry.Id.Value), StringComparer.Ordinal);
        foreach (ConstraintId id in conflict.ConstraintIds) {
            if (knownConstraints.Contains(id.Value)) {
                wanted.Add(new DiagnosticSource.Constraint(id));
            }
        }

        // 3. 假设字面量。**用 `conflict.Assumptions` 为空来判断"后端没报假设"**，此时退回调用方给的假设。
        // 3. Assumption literals. **An empty `conflict.Assumptions` means the backend reported none**, and the caller-supplied ones are used instead.
        if (literals is { Count: > 0 }) {
            var sourceByLiteral = new Dictionary<string, DiagnosticSource>(StringComparer.Ordinal);
            foreach (KeyValuePair<ConstraintId, BooleanLiteral> pair in literals) {
                sourceByLiteral[LiteralKey(pair.Value)] = new DiagnosticSource.Constraint(pair.Key);
            }
            foreach (BooleanLiteral literal in conflict.Assumptions) {
                if (sourceByLiteral.TryGetValue(LiteralKey(literal), out DiagnosticSource? source)) {
                    wanted.Add(source);
                }
            }
        }

        return requested.Where(wanted.Contains).ToArray();
    }
}
