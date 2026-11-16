#nullable enable

using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Math.Algebra.Number;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Core.Analysis;

/// <summary>
/// 固定整数 LP 三阶段求解的门控 / The gates of the fixed-integer LP three-stage solve.
/// </summary>
/// <remarks>
/// **这是三阶段求解前的四道判定。** 求解本身要真实 LP 后端，
/// **但"什么情况下不许下结论"是纯判定**，可以单独搬、单独测。
///
/// **These are the four verdicts before the three-stage solve.** The solve itself needs a real LP backend, **but "when must we not conclude anything" is pure judgement**, portable and testable on its own.
///
/// **四道门，每一道挡的都是"把没证出来的东西说成证出来了"：**
///
/// | 门 | 挡住的假结论 |
/// | --- | --- |
/// | 证明门控 | **受限求解返回的 incumbent 被读成"已证明最优"** |
/// | 对偶失败 | 对偶求解失败，却报告"局部有效性" |
/// | 无可映射对偶 | 模型有约束、后端却一条对偶都没给，却报告"敏感度为零" |
/// | 单行回映 | **多行降阶冒充单个约束的范围** |
///
/// **Four gates, each blocking a different false conclusion**: a budget-limited incumbent read as proven optimal, a failed dual solve reported as local validity, a model with constraints
/// and no mappable duals reported as zero sensitivity, and a multi-row lowering impersonating a single constraint's range.
/// </remarks>
public static class FixedIntegerLpGates
{
    /// <summary>
    /// 固定值投影失败时的判定 / Decide what a failed fixed-value projection means.
    /// </summary>
    /// <param name="fixedValuesAvailable">固定值是否投影成功 / Whether the fixed values projected.</param>
    /// <param name="backendAvailable">是否有后端且能力已声明 / Whether a backend exists and the capability is declared.</param>
    /// <returns>应当报告的状态，全部通过时为 <c>null</c> / The status to report, or <c>null</c> when the pipeline may continue.</returns>
    /// <remarks>
    /// **这是 `FixedIntegerLpSensitivityAnalyzer.analyze` 前两道门控的移植。**
    ///
    /// **顺序不能换。** 固定值投影失败必须先判，因为它是 `Unknown`（**试过了，得不出固定值**），
    /// 而后端/能力缺失是 `Unsupported`（**这条路走不通**）。
    ///
    /// | 先判能力门 | 先判固定值门 |
    /// | --- | --- |
    /// | 一个求不出固定值的模型，被报成"后端不支持" | 报成 `Unknown`，**指向真正的原因** |
    ///
    /// **The order cannot change.** A fixed-value failure has to be judged first because it is `Unknown` (**tried, and no fixed values came out**), while a missing backend/capability is `Unsupported`
    /// (**this route is closed**).
    ///
    /// | capability gate first | fixed-value gate first |
    /// | --- | --- |
    /// | a model with no fixed values gets reported as "backend unsupported" | reported `Unknown`, **pointing at the real cause** |
    ///
    /// **That difference decides whether the caller swaps the backend or fixes the model.** Checking capability first sends them to swap a backend that was never the problem.
    /// </remarks>
    public static AnalysisStatus? Preflight(bool fixedValuesAvailable, bool backendAvailable) {
        if (!fixedValuesAvailable) {
            // **试过了，得不出固定值——这是 Unknown，不是 Unsupported。**
            // **Tried, and no fixed values came out — that is Unknown, not Unsupported.**
            return AnalysisStatus.Unknown;
        }

        if (!backendAvailable) {
            return AnalysisStatus.Unsupported;
        }

        return null;
    }

    /// <summary>
    /// 证明门控 / The proof gate.
    /// </summary>
    /// <param name="provenOptimal">后端是否真的证明了最优 / Whether the backend actually proved optimality.</param>
    /// <param name="hasSolution">是否有 incumbent / Whether an incumbent exists.</param>
    /// <returns>应当报告的状态 / The status to report.</returns>
    /// <remarks>
    /// **只有真正证明最优才升格为 `Reachable`。** 受限求解返回的 incumbent **必须保持 `Unknown`**，
    /// **不得被读成"已证明最优"**——一个带时间上限的求解器几乎总能返回点什么，
    /// **而"返回了东西"和"证明了最优"之间隔着整个搜索空间。**
    ///
    /// **Only a genuine optimality proof upgrades to `Reachable`.** An incumbent from a budget-limited solve **must stay `Unknown`** and **must never be read as proven optimal** — a
    /// time-limited solver almost always returns something, **and "returned something" is a whole search space away from "proved optimal".**
    /// </remarks>
    public static AnalysisStatus ProofStatus(bool provenOptimal, bool hasSolution) =>
        provenOptimal
            ? AnalysisStatus.Reachable
            : hasSolution
                ? AnalysisStatus.Unknown
                : AnalysisStatus.Unreachable;

    /// <summary>
    /// 对偶失败时的结论 / The verdict when the dual solve failed.
    /// </summary>
    /// <param name="objective">目标值，可用时报告 / The objective value, reported when available.</param>
    /// <param name="failures">失败原因 / The failure messages.</param>
    /// <returns>状态为 `Unsupported` 的结果 / A result whose status is `Unsupported`.</returns>
    /// <remarks>
    /// **`Unsupported` 而不是 `Unknown`**：这不是"这次没算出来"，而是**"这条路径根本给不出证据"**。
    /// 两者的区别决定了调用方是重试还是放弃。
    ///
    /// **`Unsupported`, not `Unknown`**: this is not "it did not come out this time" but **"this path cannot produce evidence at all"**. The difference decides whether the caller retries
    /// or gives up.
    /// </remarks>
    public static FixedIntegerLpSolveResult DualFailed(Flt64? objective, IReadOnlyList<string> failures) {
        ArgumentNullException.ThrowIfNull(failures);

        string detail = failures.Count == 0 ? "unknown" : string.Join("; ", failures);
        return new FixedIntegerLpSolveResult(
            Status: AnalysisStatus.Unsupported,
            ObjectiveValue: objective,
            Message: $"LP 对偶求解失败，无法形成局部有效性证据：{detail} / LP dual solve failed; local-sensitivity evidence is unsupported");
    }

    /// <summary>
    /// 有约束却没有任何可映射对偶时的结论 / The verdict when the model has constraints but no mappable duals.
    /// </summary>
    /// <param name="constraintCount">模型约束条数 / The number of model constraints.</param>
    /// <param name="mappableDualCount">可映射的对偶条数 / The number of mappable duals.</param>
    /// <param name="objective">目标值 / The objective value.</param>
    /// <returns>需要拒绝时给出结果，否则为 <c>null</c> / The result when it must be refused, otherwise <c>null</c>.</returns>
    /// <remarks>
    /// **空对偶会被读成"敏感度为零"**，而真相是**"后端没告诉我们"**。
    /// 模型里没有约束时这条不适用——**那时"零敏感度"是正确答案。**
    ///
    /// **Empty duals read as "zero sensitivity"** while the truth is **"the backend told us nothing"**. It does not apply when the model has no constraints — **there "zero sensitivity" is
    /// the correct answer.**
    /// </remarks>
    public static FixedIntegerLpSolveResult? NoMappableDuals(
        int constraintCount, int mappableDualCount, Flt64? objective) =>
        constraintCount > 0 && mappableDualCount == 0
            ? new FixedIntegerLpSolveResult(
                Status: AnalysisStatus.Unsupported,
                ObjectiveValue: objective,
                Message: "LP 后端未返回可映射的原始约束对偶值 / LP backend returned no mappable original-constraint duals")
            : null;

    /// <summary>
    /// 单行回映：只有唯一降阶行才允许映射 / Single-row remapping: only a unique lowered row may be mapped.
    /// </summary>
    /// <typeparam name="T">来源键的类型 / The origin key type.</typeparam>
    /// <param name="rowProvenance">每行对应的原始约束 / The original constraint per row.</param>
    /// <param name="rowOrigins">每行的来源键 / The origin key per row.</param>
    /// <param name="rangesByOrigin">按来源键给出的区间 / The range keyed by origin.</param>
    /// <param name="originalIds">允许出现的原始约束 / The admissible original constraints.</param>
    /// <returns>原始约束到区间的映射 / A map from original constraint to range.</returns>
    /// <remarks>
 /// **直接转发到 `ProvenanceDualMapping.MapRanges`**，因为规则完全一样。
    /// **这里单开一个名字，是为了让调用点直接读出规则**：那一行写的就是
    /// "多行 lowering 不得冒充单个约束的范围"。
    ///
    /// **It forwards to `ProvenanceDualMapping.MapRanges`** because the rule is identical (implemented). **The separate name makes the call site read as the rule itself**, which
    /// says at that line "a multi-row lowering must not impersonate a single constraint's range".
    /// </remarks>
    public static IReadOnlyDictionary<ConstraintId, SensitivityRange> RemapSingleRowRanges<T>(
        IReadOnlyList<ConstraintId?> rowProvenance,
        IReadOnlyList<T?> rowOrigins,
        IReadOnlyDictionary<T, SensitivityRange> rangesByOrigin,
        IReadOnlySet<ConstraintId> originalIds)
        where T : notnull =>
        ProvenanceDualMapping.MapRanges(rowProvenance, rowOrigins, rangesByOrigin, originalIds);

    /// <summary>
    /// 把一次求解结果摊到每条约束上 / Spread one solve result across every constraint.
    /// </summary>
    /// <param name="constraintIds">约束顺序，按快照顺序 / The constraint order, as in the snapshot.</param>
    /// <param name="solved">求解结果 / The solve result.</param>
    /// <param name="activities">与约束顺序对齐的活跃度 / Activities aligned with the constraint order.</param>
    /// <param name="dualTolerance">判定"这条约束在起作用"的对偶阈值 / The dual threshold for "this constraint is doing work".</param>
    /// <returns>逐约束的敏感性记录 / The per-constraint sensitivity records.</returns>
    /// <remarks>
    /// **这是 `FixedIntegerLpSensitivityAnalyzer.analyze` 最后那段组装的移植。**
    ///
    /// **`LocalEffective` 在没有对偶时是 `null`，不是 `false`。**
    /// 无对偶意味着**后端没给出这个数**，而 `false` 意味着**给出了，且它接近零**。
    /// **把前者写成后者，报告会说"这条约束不影响结果"——一个没测过的结论。**
    ///
    /// **`LocalEffective` is `null`, not `false`, when there is no dual.** No dual means **the backend did not give the number**, while `false` means **it did, and the number is near zero.** Writing
    /// the first as the second makes the report say "this constraint does not matter" — **a conclusion nobody measured.**
    ///
    /// **`constraintIds` 的长度必须与 `activities` 一致。** 不一致说明调用方把两次不同快照的列表对上了，
    /// **而错位的活跃度会让每一条约束都挂上邻居的证据**——这种错误不会抛异常，只会让报告安静地错。
    ///
    /// **`constraintIds` and `activities` must have the same length.** A mismatch means the caller paired lists from two different snapshots, **and misaligned activities attach the neighbour's
    /// evidence to every constraint** — an error that throws nothing and simply makes the report quietly wrong.
    /// </remarks>
    public static IReadOnlyList<LocalConstraintSensitivity> AssembleSensitivities(
        IReadOnlyList<ConstraintId> constraintIds,
        FixedIntegerLpSolveResult solved,
        IReadOnlyList<ConstraintActivity?> activities,
        double dualTolerance) {
        ArgumentNullException.ThrowIfNull(constraintIds);
        ArgumentNullException.ThrowIfNull(solved);
        ArgumentNullException.ThrowIfNull(activities);

        if (constraintIds.Count != activities.Count) {
            throw new ArgumentException(
                "约束与活跃度必须一一对应 / constraints and activities must be one to one",
                nameof(activities));
        }

        IReadOnlyDictionary<ConstraintId, Flt64?> duals = solved.DualValues;
        IReadOnlyDictionary<ConstraintId, SensitivityRange?> ranges = solved.SensitivityRanges
            ?? new Dictionary<ConstraintId, SensitivityRange?>();

        var sensitivities = new LocalConstraintSensitivity[constraintIds.Count];
        for (int index = 0; index < constraintIds.Count; ++index) {
            ConstraintId id = constraintIds[index];
            Flt64? dual = duals.TryGetValue(id, out Flt64? found) ? found : null;

            sensitivities[index] = new LocalConstraintSensitivity(
                ConstraintId: id,
                DualValue: dual,
                Activity: activities[index],
                // **没有对偶就没有判断**，而不是"判断为无效"。
                // **No dual means no verdict**, not "a verdict of ineffective".
                LocalEffective: dual is { } value
                    ? global::System.Math.Abs(value.ToDouble()) > dualTolerance
                    : null,
                SensitivityRange: ranges.TryGetValue(id, out SensitivityRange? range) ? range : null,
                Scope: LocalSensitivityScope.FixedIntegerIncumbent);
        }

        return sensitivities;
    }
}
