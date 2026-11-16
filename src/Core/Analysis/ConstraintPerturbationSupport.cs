#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System.Threading;
using System.Threading.Tasks;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Math.Algebra.Number;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Core.Analysis;

/// <summary>
/// 扰动分析的纯函数部分 / The pure part of perturbation analysis.
/// </summary>
/// <remarks>
/// 移植自 `ConstraintPerturbationAnalyzer` 的几个 private helper：`requestedDeltas`、`objectiveImprovement`、
/// `classifyOutcome`、`aggregateStatus`。**求解循环与阈值二分没搬。**
///
/// Ported from the private helpers of `ConstraintPerturbationAnalyzer`. **The solve loop and the threshold bisection are not implemented.**
///
/// **这四个函数决定了"一次扰动算不算证据"，而它们是纯的——所以能一次钉死。**
/// **These four decide whether a perturbation counts as evidence, and they are pure — so they can be pinned down in one go.**
/// </remarks>
public static class ConstraintPerturbationSupport
{
    /// <summary>
    /// 去重并保持顺序的 delta 列表 / The delta list, deduplicated with order preserved.
    /// </summary>
    /// <param name="policy">扰动策略 / The perturbation policy.</param>
    /// <returns>去重后的 delta / The deduplicated deltas.</returns>
    /// <remarks>
    /// **同一个 delta 跑两次不是两条证据。** 重复项会让"有效 delta 个数"看起来翻倍，而那个数字会直接进报告。
    ///
    /// **The same delta run twice is not two pieces of evidence.** A duplicate doubles the apparent count of effective deltas, and that number goes straight into the report.
    /// </remarks>
    public static IReadOnlyList<Flt64> RequestedDeltas(ConstraintPerturbationPolicy policy) {
        ArgumentNullException.ThrowIfNull(policy);

        var ordered = new List<Flt64>();
        var seen = new HashSet<double>();
        foreach (Flt64 delta in policy.DeltaList) {
            if (seen.Add(delta.ToDouble())) {
                ordered.Add(delta);
            }
        }
        return ordered;
    }

    /// <summary>
    /// 目标改善量，带方向 / The objective improvement, with its direction.
    /// </summary>
    /// <param name="category">优化方向 / Optimisation direction.</param>
    /// <param name="baseline">基线目标值 / The baseline objective.</param>
    /// <param name="current">扰动后的目标值 / The perturbed objective.</param>
    /// <returns>改善量，或 <c>null</c> / The improvement, or <c>null</c>.</returns>
    /// <remarks>
    /// **方向搞反，一个变差的解会看起来是改善。** 最大化时改善是 `current - baseline`，最小化时是 `baseline - current`——
    /// 同一个差值，两种方向下的符号必须相反。
    ///
    /// **Getting the direction backwards makes a worse solution look like an improvement.** For a maximisation the improvement is `current - baseline` and for a minimisation it
    /// is `baseline - current`: the same difference, with opposite signs depending on the direction.
    /// </remarks>
    public static Flt64? ObjectiveImprovement(ObjectCategory category, Flt64? baseline, Flt64? current) {
        if (baseline is not { } baselineValue || current is not { } currentValue) {
            return null;
        }
        double value = category == ObjectCategory.Maximum
            ? currentValue.ToDouble() - baselineValue.ToDouble()
            : baselineValue.ToDouble() - currentValue.ToDouble();
        return double.IsNaN(value) || double.IsInfinity(value) ? null : new Flt64(value);
    }

    /// <summary>
    /// 把一次求解结果分类 / Classify one solve result.
    /// </summary>
    /// <param name="status">求解状态 / The solve status.</param>
    /// <param name="improvement">目标改善量 / The objective improvement.</param>
    /// <param name="tolerance">判定"有效"的阈值 / The tolerance for calling it effective.</param>
    /// <returns>扰动结果分类 / The perturbation outcome.</returns>
    /// <remarks>
    /// **`Unknown` 与 `NoObservedEffect` 必须分开**：前者是"这次没测出来"，后者是"测了，没有效果"。
    /// 合起来报会让一份缺证据的报告看起来结论明确。
    ///
    /// 而阈值比较是**严格大于**：`improvement == tolerance` 仍在噪声范围内，**不该算作有效**。
    ///
    /// **`Unknown` and `NoObservedEffect` have to stay apart**: the first is "this run measured nothing", the second is "measured, no effect". Merging them makes a report with
    /// missing evidence look conclusive. The tolerance comparison is **strictly greater than**: an improvement exactly at the tolerance is still noise and **must not count**.
    /// </remarks>
    public static PerturbationOutcome ClassifyOutcome(AnalysisStatus status, Flt64? improvement, double tolerance) {
        if (status != AnalysisStatus.Reachable || improvement is not { } value) {
            return PerturbationOutcome.Unknown;
        }
        return value.ToDouble() > tolerance
            ? PerturbationOutcome.Effective
            : PerturbationOutcome.NoObservedEffect;
    }

    /// <summary>
    /// 汇总若干观测的整体状态 / Aggregate the status of several observations.
    /// </summary>
    /// <param name="observations">观测 / The observations.</param>
    /// <param name="budgetStopped">是否因预算中止 / Whether the budget stopped the run.</param>
    /// <returns>汇总状态 / The aggregated status.</returns>
    /// <remarks>
    /// **`Unreachable` 只在每一个观测都是 `Unreachable` 时给出。**
    /// 只要有一个观测没测出结论、或有一个后端不支持，就不能说"这个约束不可达"——
    /// **一个可能可达的东西被报成不可达，是最难被后续工作纠正的错误。**
    ///
    /// **`Unreachable` is only reported when every single observation is `Unreachable`.** A single inconclusive run, or a single backend that would not do it, is enough to deny the
    /// word — **reporting something possibly reachable as unreachable is the error later work finds hardest to undo.**
    ///
    /// 另外两处同样不是随手写的：**空观测在预算中止时是 `Unknown`、不是 `Unsupported`**（是没跑完，不是跑不了）；
    /// **`Unsupported` 混着 `Reachable` 时给 `Unknown`**（一部分证据没适用，整体既不能算可达也不能算不支持）。
    ///
    /// The other two are equally deliberate: **an empty set under a budget stop is `Unknown`, not `Unsupported`** (it did not finish, it was not refused); and **`Unsupported`
    /// mixed with `Reachable` gives `Unknown`** (part of the evidence did not apply, so the whole is neither reachable nor unsupported).
    /// </remarks>
    public static AnalysisStatus AggregateStatus(
        IReadOnlyList<ConstraintPerturbationObservation> observations,
        bool budgetStopped) {
        ArgumentNullException.ThrowIfNull(observations);

        if (observations.Count == 0) {
            return budgetStopped ? AnalysisStatus.Unknown : AnalysisStatus.Unsupported;
        }
        if (observations.Any(observation => observation.Status == AnalysisStatus.Unsupported)) {
            return observations.Any(observation => observation.Status == AnalysisStatus.Reachable)
                ? AnalysisStatus.Unknown
                : AnalysisStatus.Unsupported;
        }
        if (observations.Any(observation => observation.Status == AnalysisStatus.Unknown)) {
            return AnalysisStatus.Unknown;
        }
        if (observations.Any(observation => observation.Status == AnalysisStatus.Reachable)) {
            return AnalysisStatus.Reachable;
        }
        return AnalysisStatus.Unreachable;
    }

    /// <summary>
    /// 派生一条 RHS 被扰动的快照 / Derive a snapshot with one RHS perturbed.
    /// </summary>
    /// <param name="snapshot">源快照 / The source snapshot.</param>
    /// <param name="constraintId">要扰动的约束 / The constraint to perturb.</param>
    /// <param name="delta">RHS 增量 / The RHS delta.</param>
    /// <returns>派生结果 / The derived result.</returns>
    /// <remarks>
    /// 三种"不能扰动"的情形都**显式返回原因**，而不是给一份没改过的快照：
    /// 约束不存在、不是整数比较、delta 不是整数。
    ///
    /// **第三种最容易含糊**：连续后端用 `0.5` 扰动 RHS 是合理的，但 CP 的 RHS 是 `Int64`。
    /// **悄悄取整会让"扰动了 0.5"和"扰动了 0"给出同一份模型**，而报告里写的是 0.5。
    ///
    /// All three "cannot perturb" cases **return a reason explicitly** instead of an unchanged snapshot: the constraint is missing, it is not an integer comparison, or the delta is
    /// not integral. **The third is the subtle one**: a continuous backend may reasonably perturb an RHS by `0.5`, but a CP RHS is an `Int64`. **Rounding silently would make "perturbed
    /// by 0.5" and "perturbed by 0" produce the same model**, while the report says 0.5.
    /// </remarks>
    public static RhsPerturbationResult DeriveRhsSnapshot(
        ConstraintProgrammingModelSnapshot snapshot,
        ConstraintId constraintId,
        Flt64 delta) {
        ArgumentNullException.ThrowIfNull(snapshot);

        ConstraintProgrammingConstraintSnapshot? definition = snapshot.Constraints
            .FirstOrDefault(entry => entry.Id == constraintId);
        if (definition is null) {
            return new RhsPerturbationUnsupported("扰动候选约束不存在 / Perturbation candidate does not exist");
        }
        if (definition.Constraint is not ConstraintProgrammingConstraint.IntegerComparison comparison) {
            return new RhsPerturbationUnsupported(
                "仅支持整数比较约束的精确 RHS 派生 / Exact RHS derivation currently supports integer comparisons only");
        }

        double rawDelta = delta.ToDouble();
        if (double.IsNaN(rawDelta) || double.IsInfinity(rawDelta) || rawDelta != global::System.Math.Floor(rawDelta)) {
            return new RhsPerturbationUnsupported(
                "CP RHS 扰动必须为整数；连续后端需自行派生模型 / CP RHS perturbations must be integral; continuous backends must derive their own model");
        }

        // **加之前先查范围**：`long` 溢出在 C# 里默认是静默回绕，**扰动一下 RHS 变成负数，而没有任何一处报错。**
        // **Check the range before adding**: `long` overflow wraps silently in C# by default, **turning a perturbed RHS negative with nothing reporting it.**
        double newRhsValue = comparison.Rhs.ToFlt64().ToDouble() + rawDelta;
        if (newRhsValue < -9.2e18 || newRhsValue > 9.2e18) {
            return new RhsPerturbationUnsupported("RHS 扰动超出 Int64 范围 / Perturbed RHS exceeds Int64 range");
        }

        var newRhs = new Int64((long)newRhsValue);
        var updated = new ConstraintProgrammingConstraint.IntegerComparison(
            comparison.Expression, comparison.Comparison, newRhs);
        if (!snapshot.TryWithConstraint(constraintId, updated, out ConstraintProgrammingModelSnapshot derived)) {
            return new RhsPerturbationUnsupported("扰动候选约束不存在 / Perturbation candidate does not exist");
        }
        return new RhsPerturbationReady(derived, newRhs.ToFlt64());
    }

    /// <summary>
    /// 固定 delta 扫描的结果 / The result of a fixed-delta sweep.
    /// </summary>
    /// <param name="Deltas">逐个 delta 的结论 / The per-delta outcomes.</param>
    /// <param name="FirstEffective">首个有效结论，可为 <c>null</c> / The first effective outcome, or <c>null</c>.</param>
    /// <param name="PreviousNoEffect">最后一个无效果结论，可为 <c>null</c> / The last no-effect outcome, or <c>null</c>.</param>
    /// <param name="BudgetStopped">是否因预算停下 / Whether the sweep stopped on budget.</param>
    public sealed record DeltaSweepResult(
        IReadOnlyList<Flt64> Deltas,
        Flt64? FirstEffective,
        Flt64? PreviousNoEffect,
        bool BudgetStopped);

    /// <summary>
    /// 按固定 delta 逐个扫描 / Sweep the fixed deltas one by one.
    /// </summary>
    /// <param name="deltas">要探测的 delta / The deltas to probe.</param>
    /// <param name="probe">对一个 delta 求值 / Evaluate one delta.</param>
    /// <param name="budgetStopped">预算是否已耗尽 / Whether the budget is already spent.</param>
    /// <returns>扫描结果 / The sweep result.</returns>
    /// <remarks>
    /// **三条停止规则，其中两条最容易写错：**
    ///
    /// | 情况 | 处理 | 写错会怎样 |
    /// | --- | --- | --- |
    /// | 首个 `Effective` | **记下并停** | 继续扫会把后面的有效 delta 覆盖掉前面的 |
    /// | `NoObservedEffect` | 记下，**继续** | 停在这里会漏掉真正有效的那个 |
    /// | **`Unknown`** | **既不记也不停** | 当成"无效果"会伪造下界；**停在这里会让扫描提前结束** |
    ///
    /// **第三条是那个家族的第六次**：把"没测出来"当成"无效果"，
    /// 报告的 `PreviousNoEffect` 会指向一个根本没测的 delta。
    ///
    /// **Three stopping rules, two of them easy to get wrong**: the first `Effective` is recorded and stops; `NoObservedEffect` is recorded and continues; **`Unknown` neither records nor
    /// stops** — treating it as "no effect" fabricates a lower bound, and stopping on it ends the sweep early. **The third is the sixth appearance of that family.**
    /// </remarks>
    public static DeltaSweepResult RunDeltas(
        IReadOnlyList<Flt64> deltas,
        Func<Flt64, PerturbationOutcome> probe,
        bool budgetStopped = false) {
        ArgumentNullException.ThrowIfNull(deltas);
        ArgumentNullException.ThrowIfNull(probe);

        var observed = new List<Flt64>();
        Flt64? firstEffective = null;
        Flt64? previousNoEffect = null;
        bool stoppedOnBudget = budgetStopped;

        foreach (Flt64 delta in deltas) {
            PerturbationOutcome outcome = probe(delta);
            observed.Add(delta);

            if (outcome == PerturbationOutcome.Effective) {
                firstEffective = delta;
                break;
            }
            if (outcome == PerturbationOutcome.NoObservedEffect) {
                previousNoEffect = delta;
            }
            // **`Unknown` 只被记进观察列表，不改变任何一边。**
            // **`Unknown` is only recorded in the observation list and moves neither side.**
            if (stoppedOnBudget) {
                break;
            }
        }

        return new DeltaSweepResult(observed, firstEffective, previousNoEffect, stoppedOnBudget);
    }

    /// <summary>
    /// 扰动分析的前置门结论 / The conclusion of the perturbation pre-flight gate.
    /// </summary>
    public enum PreflightVerdict {
        /// <summary>可以开始分析 / The analysis may start.</summary>
        Proceed,

        /// <summary>整份报告就是 Unsupported / The whole report is Unsupported.</summary>
        WholeReportUnsupported,
    }

    /// <summary>
    /// 扰动分析的前置门 / The pre-flight gate of the perturbation analysis.
    /// </summary>
    /// <param name="backendAvailable">是否提供了后端 / Whether a backend was supplied.</param>
    /// <param name="needsRhsPerturbation">策略是否要求 RHS 扰动 / Whether the policy asks for RHS perturbation.</param>
    /// <param name="rhsAllowed">后端是否允许 RHS 扰动 / Whether RHS perturbation is allowed.</param>
    /// <param name="removalRequested">策略是否要求删除测试 / Whether the policy asks for the removal test.</param>
    /// <param name="removalAllowed">后端是否允许删除测试 / Whether the removal test is allowed.</param>
    /// <returns>前置结论 / The pre-flight verdict.</returns>
    /// <remarks>
    /// **两条"整份 Unsupported"的路径不一样，合并它们会丢掉一半信息：**
    ///
    /// | 情况 | 结论 | 为什么 |
    /// | --- | --- | --- |
    /// | 没后端 | 整份 Unsupported | **什么都做不了** |
    /// | **RHS 与删除都不被允许** | 整份 Unsupported | **两项都做不了，报告没有可说的东西** |
    /// | 只要求 RHS 且不允许 | 整份 Unsupported | **策略只问了 RHS，而它就是做不了** |
    ///
    /// **第二条最容易漏**：只看"没后端"会把"有后端但两项都不允许"漏过去，
    /// **于是分析照样开始，跑完给出一份什么都没有的报告。**
    ///
    /// **反过来也不能过度拒绝**：只要求删除、且删除被允许时，
    /// **哪怕 RHS 不允许，也必须开始**——否则一个纯删除测试会被无理由地拒掉。
    ///
    /// **The two "whole report Unsupported" paths differ and merging them loses half the information**: no backend at all, versus a backend that allows neither stage. **The second is the easy
    /// omission**: checking only for a missing backend lets "backend present, both stages disallowed" through, **so the analysis starts and finishes with a report that says nothing.** The
    /// opposite over-rejection is also wrong: **when only the removal test is requested and it is allowed, the analysis must start even if RHS is not allowed**, or a pure removal test
    /// would be refused for no reason.
    /// </remarks>
    public static PreflightVerdict Preflight(
        bool backendAvailable,
        bool needsRhsPerturbation,
        bool rhsAllowed,
        bool removalRequested,
        bool removalAllowed) {
        if (!backendAvailable) {
            return PreflightVerdict.WholeReportUnsupported;
        }

        bool rhsUsable = !needsRhsPerturbation || rhsAllowed;
        bool removalUsable = !removalRequested || removalAllowed;

        // **两项都不可用 → 整份 Unsupported**；只要有一项可用就继续，
        // **不可用的那一项会在自己的阶段里给出 Unsupported 观察。**
        //
        // **Both unusable means the whole report is Unsupported**; as long as one is usable the analysis continues, and **the unusable one reports its Unsupported observation in
        // its own stage.**
        return !rhsUsable && !removalUsable ? PreflightVerdict.WholeReportUnsupported : PreflightVerdict.Proceed;
    }

    /// <summary>
    /// 这个后端能不能做这项分析 / Whether this backend can perform this analysis.
    /// </summary>
    /// <param name="matrix">后端能力矩阵 / The backend capability matrix.</param>
    /// <param name="capability">要问的能力 / The capability being asked about.</param>
    /// <returns>是否允许 / Whether it is allowed.</returns>
    /// <remarks>
    /// **显式声明优先于矩阵推断。** 声明里有这一项，就以它为准——**哪怕矩阵别处看起来是支持的。**
    ///
    /// **模型类型列表为空意味着"没有限制"，不是"什么都不支持"。** 写成"列表为空就拒绝"，
    /// **一个没声明模型类型的后端会被判成什么都不会做**，而它其实没有限制。
    ///
    /// **An explicit declaration outranks what the matrix implies**: when the override names this capability, that is the answer — **even if the matrix elsewhere looks like it
    /// supports it.** An **empty model-type list means "no restriction", not "nothing works"**: reading it the other way **would judge a backend that declared no model types as unable
    /// to do anything**, when in fact it is unrestricted.
    /// </remarks>
    public static bool CapabilityAllowed(CapabilityMatrix matrix, AnalysisCapability capability) {
        ArgumentNullException.ThrowIfNull(matrix);

        IReadOnlyDictionary<AnalysisCapability, CapabilitySupport>? overrides = matrix.AnalysisCapabilities;
        if (overrides is not null && overrides.TryGetValue(capability, out CapabilitySupport declared)) {
            return declared != CapabilitySupport.Unsupported;
        }
        return matrix.ModelTypes.Count == 0 || matrix.Support(capability) != CapabilitySupport.Unsupported;
    }

    /// <summary>
    /// 整数解的模式有没有变 / Whether the integer pattern changed.
    /// </summary>
    /// <param name="baseline">基线解，可为 <c>null</c> / The baseline solution, or <c>null</c>.</param>
    /// <param name="current">当前解，可为 <c>null</c> / The current solution, or <c>null</c>.</param>
    /// <returns>变了为 <c>true</c>，没变为 <c>false</c>，**任一侧缺失为 `null`** / True when it changed, false when it did not, and **`null` when either side is missing**.</returns>
    /// <remarks>
    /// **这一条又是"没测不能写成没变"**（第五次）：任一侧解缺失时返回 `null`，不是 `false`。
    ///
    /// 写成 `false` 会让报告声称"扰动之后整数解的模式没变"——**而那份基线解根本没拿到，
    /// 于是"没变"这个结论是从空气里来的。**
    ///
    /// **This is the "not measured must not be written as unchanged" family again, for the fifth time**: when either solution is missing it returns `null`, not `false`. Returning `false`
    /// would let the report claim "the integer pattern did not change after the perturbation" — **while the code solution was never obtained, so the claim comes from nothing.**
    /// </remarks>
    public static bool? IntegerPatternChanged(
        ConstraintProgrammingSolution? baseline,
        ConstraintProgrammingSolution? current) {
        if (baseline is null || current is null) {
            return null;
        }

        // **逐个比较，并且要区分"缺失"与"值不同"。** 用 `SequenceEqual` 之类的集合比较会把
        // "一侧少了某个变量"当成"不同"，那是对的；但把两个都空当成"相同"也说得过去——
        // 而 `null` 的情况已经在上面挡住了。
        //
        // **Compare entry by entry and keep "absent" apart from "different".** A set comparison would treat "one side is missing a variable" as a difference, which is right, but would
        // also treat two empty dictionaries as equal — which is fine, and the `null` case is already handled above.
        if (baseline.Values.Count != current.Values.Count) {
            return true;
        }
        foreach (KeyValuePair<VariableId, Int64> pair in baseline.Values) {
            if (!current.Values.TryGetValue(pair.Key, out Int64 value) || value != pair.Value) {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 阈值二分的纯形式 / The threshold bisection, in pure form.
    /// </summary>
    /// <param name="lower">已证明无效的下端 / A proven no-effect lower endpoint.</param>
    /// <param name="upper">已证明有效的上端 / A proven effective upper endpoint.</param>
    /// <param name="iterations">最多探测次数 / The maximum number of probes.</param>
    /// <param name="probe">对一个 delta 求值的回调 / The callback that evaluates one delta.</param>
    /// <returns>自适应结论 / The adaptive conclusion.</returns>
    /// <remarks>
    /// 三条规则，每条都有个"顺手写会错"的地方：
    ///
    /// | 规则 | 顺手写会怎样 |
    /// | --- | --- |
    /// | 中点取整后必须严格落在区间内 | **整数中点会停在原地，循环空转** |
    /// | 探测 `Unknown` 就停 | **当成"无效"会把下界推高** |
    /// | 次数用完要如实说 | 报一个"已经收敛"的假结论 |
    ///
    /// **第二条最关键**：一次没测出结论的探测，若被当成 `NoObservedEffect`，下界会被推到中点，
    /// **于是报告声称"在这个 delta 以下确认无效果"——而那一半根本没测过。**
    ///
    /// Three rules, each with a way to get it wrong by accident. **The second is the important one**: treating an inconclusive probe as `NoObservedEffect` moves the lower bound up,
    /// **so the report claims "confirmed no effect below this delta" about a range that was never measured.**
    /// </remarks>
    public static AdaptivePerturbationResult RefineThreshold(
        Flt64 lower,
        Flt64 upper,
        int iterations,
        Func<Flt64, PerturbationOutcome> probe) {
        ArgumentNullException.ThrowIfNull(probe);

        double lo = lower.ToDouble();
        double hi = upper.ToDouble();
        int attempts = 0;
        bool stoppedInconclusive = false;

        for (int round = 0; round < iterations; round++) {
            double midpoint = global::System.Math.Floor((lo + hi) / 2.0);
            if (midpoint <= lo || midpoint >= hi) {
                // **整数中点卡住时必须退出**：否则同样的 delta 被反复探测，次数耗尽而区间没变。
                // **An integer midpoint that cannot move has to end the loop**: otherwise the same delta is probed over and over and the interval never shrinks.
                break;
            }

            attempts++;
            PerturbationOutcome outcome = probe(new Flt64(midpoint));
            if (outcome == PerturbationOutcome.Effective) {
                hi = midpoint;
            }
            else if (outcome == PerturbationOutcome.NoObservedEffect) {
                lo = midpoint;
            }
            else {
                // **没结论就停**，不往任何一边推。
                // **No conclusion means stop** — it is not evidence for either side.
                stoppedInconclusive = true;
                break;
            }
        }

        AdaptivePerturbationOutcome result = (lo, hi, attempts, stoppedInconclusive) switch {
            _ when attempts == 0 => AdaptivePerturbationOutcome.UnknownDueToBudget,
            (_, _, _, true) => AdaptivePerturbationOutcome.UnknownDueToBudget,
            (double l, double h, _, _) when h - l <= 1.0 => AdaptivePerturbationOutcome.FirstEffective,
            _ => AdaptivePerturbationOutcome.ThresholdInterval,
        };

        return new AdaptivePerturbationResult(
            Outcome: result,
            FirstEffectiveDelta: result == AdaptivePerturbationOutcome.FirstEffective ? new Flt64(hi) : null,
            LowerBound: new Flt64(lo),
            UpperBound: new Flt64(hi),
            Attempts: attempts);
    }

    /// <summary>
    /// 单约束删除测试 / The single-constraint removal test.
    /// </summary>
    /// <param name="snapshot">基线快照 / The baseline snapshot.</param>
    /// <param name="constraintId">要删除的约束 / The constraint to remove.</param>
    /// <param name="baselineObjective">基线目标值 / The baseline objective.</param>
    /// <param name="baselineSolution">基线解 / The baseline solution.</param>
    /// <param name="policy">扰动策略 / The perturbation policy.</param>
    /// <param name="solver">CP 求解器 / The CP solver.</param>
    /// <param name="budgetExhausted">预算是否已耗尽，由调用方判定 / Whether the budget is spent, decided by the caller.</param>
    /// <param name="options">求解选项 / Solve options.</param>
    /// <param name="cancellationToken">调用方取消令牌 / Caller cancellation token.</param>
    /// <returns>一条删除观测 / One removal observation.</returns>
    /// <remarks>
    /// 顺序是这批的重点：**先查预算，再派生，最后求解。**
    ///
    /// **预算耗尽必须报 `Unknown`，不能报"删除无效"**：一次因为没预算而没跑成的测试，
    /// 和一次跑成了、发现没效果的测试，**结论完全不同，而它们看起来一样。**
    ///
    /// **约束不存在也必须报 `Unknown`**：这不是"删了没用"，是"根本没删"。
    ///
    /// The order is the point: **check the budget, derive, then solve.** **A spent budget must report `Unknown`, never "removal ineffective"**: a test that never ran
    /// and a test that ran and found nothing **have completely different meanings and look identical.** And **a missing constraint is `Unknown` too**: that is not "removing it did
    /// nothing", it is "nothing was removed".
    /// </remarks>
    public static async Task<ConstraintPerturbationObservation> RunRemovalAsync(
        ConstraintProgrammingModelSnapshot snapshot,
        ConstraintId constraintId,
        Flt64? baselineObjective,
        ConstraintProgrammingSolution? baselineSolution,
        ConstraintPerturbationPolicy policy,
        IConstraintProgrammingSolver solver,
        bool budgetExhausted,
        ConstraintProgrammingSolveOptions? options = null,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(solver);

        if (budgetExhausted) {
            return Unknown(constraintId, "删除测试预算已耗尽 / Removal test budget was exhausted");
        }
        if (!snapshot.TryWithoutConstraint(constraintId, out ConstraintProgrammingModelSnapshot derived)) {
            return Unknown(constraintId, $"扰动候选约束不存在：{constraintId.Value} / Perturbation candidate does not exist: {constraintId.Value}");
        }

        Result<ConstraintProgrammingModel, ErrorCode, Error<ErrorCode>> rebuilt =
            SnapshotModelRebuilder.Rebuild(derived);
        if (rebuilt is not Ok<ConstraintProgrammingModel, ErrorCode, Error<ErrorCode>> okRebuilt) {
            return Unknown(constraintId, "派生模型构造失败 / Derived model construction failed");
        }

        try {
            Result<ConstraintProgrammingSolverOutput, ErrorCode, Error<ErrorCode>> solved =
                await solver.SolveAsync(okRebuilt.Value!, options, cancellationToken).ConfigureAwait(false);
            if (solved is not Ok<ConstraintProgrammingSolverOutput, ErrorCode, Error<ErrorCode>> okSolved) {
                return Unknown(constraintId, "删除测试求解失败 / The removal test solve failed");
            }
            return FromOutput(snapshot.ObjectCategory, constraintId, baselineObjective, baselineSolution,
                policy, okSolved.Value!);
        }
        finally {
            // **派生模型一定会被释放**：它持有原生句柄，**漏一次不是测试变红，而是内存慢慢涨。**
            // **The derived model is always disposed**: it holds native handles, and **a leak shows up as slowly growing memory, not as a red test.**
            okRebuilt.Value!.Dispose();
        }
    }

    private static ConstraintPerturbationObservation FromOutput(
        ObjectCategory category,
        ConstraintId constraintId,
        Flt64? baselineObjective,
        ConstraintProgrammingSolution? baselineSolution,
        ConstraintPerturbationPolicy policy,
        ConstraintProgrammingSolverOutput output) {
        (AnalysisStatus status, Flt64? newObjective, ConstraintProgrammingSolution? solution, TimeSpan? solveTime) =
            output switch {
                ConstraintProgrammingFeasibleOutput feasible =>
                    (AnalysisStatus.Reachable, feasible.Objective, feasible.Solution, (TimeSpan?)null),
                ConstraintProgrammingInfeasibleOutput =>
                    (AnalysisStatus.Unreachable, null, null, null),
                _ => (AnalysisStatus.Unknown, null, null, null),
            };

        Flt64? improvement = ObjectiveImprovement(category, baselineObjective, newObjective);
        return new ConstraintPerturbationObservation(
            ConstraintId: constraintId,
            Kind: ConstraintPerturbationKind.Removal,
            Delta: null,
            PerturbedRhs: null,
            Status: status,
            BaselineObjective: baselineObjective,
            NewObjective: newObjective,
            ObjectiveImprovement: improvement,
            IntegerPatternChanged: baselineSolution is null || solution is null
                ? null
                : !baselineSolution.Values.SequenceEqual(solution.Values),
            SolveTime: solveTime,
            Outcome: ClassifyOutcome(status, improvement, policy.ObjectiveTolerance));
    }

    private static ConstraintPerturbationObservation Unknown(ConstraintId constraintId, string message) =>
        new(constraintId,
            ConstraintPerturbationKind.Removal,
            Delta: null,
            PerturbedRhs: null,
            Status: AnalysisStatus.Unknown,
            BaselineObjective: null,
            NewObjective: null,
            ObjectiveImprovement: null,
            IntegerPatternChanged: null,
            SolveTime: null,
            Outcome: PerturbationOutcome.Unknown,
            Message: message);
}

/// <summary>RHS 派生结果 / The result of deriving a perturbed RHS.</summary>
public abstract record RhsPerturbationResult;

/// <summary>派生成功 / The derivation succeeded.</summary>
/// <param name="Snapshot">派生后的快照 / The derived snapshot.</param>
/// <param name="NewRhs">新的 RHS / The new RHS.</param>
public sealed record RhsPerturbationReady(ConstraintProgrammingModelSnapshot Snapshot, Flt64 NewRhs) : RhsPerturbationResult;

/// <summary>无法派生，并给出原因 / The derivation was refused, with a reason.</summary>
/// <param name="Message">原因 / The reason.</param>
public sealed record RhsPerturbationUnsupported(string Message) : RhsPerturbationResult;
