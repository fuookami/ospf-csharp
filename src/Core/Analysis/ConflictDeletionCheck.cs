#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Core.Analysis;

/// <summary>
/// 冲突删除复验的判定 / The verdict of one conflict deletion check.
/// </summary>
/// <remarks>
/// `ConflictAnalyzer.analyze` 的删除复验循环体。**求解那一层由调用方提供**，
/// 于是这个判定可以脱离后端单独钉死。
///
/// Ported from the deletion-check loop body of `ConflictAnalyzer.analyze`. **The solve is supplied by the caller**, so the verdict can be pinned down without a backend.
/// </remarks>
public static class ConflictDeletionCheck
{
    /// <summary>
    /// 判断删掉一个来源之后它在冲突里的去留 / Decide whether a source stays in the conflict after removing it.
    /// </summary>
    /// <param name="source">被删除的来源 / The source being removed.</param>
    /// <param name="status">删除后目标的状态 / The target status after removal.</param>
    /// <returns>一条复验，以及该来源是否应被移除 / One verification, and whether the source should be dropped.</returns>
    /// <remarks>
    /// **三种状态的语义完全不同，而它们只差一个枚举值：**
    ///
    /// | 删除后目标 | 结论 | 为什么 |','| --- | --- | --- |
    /// | `Reachable` | **保留**（`removed: false`） | 没有它目标就够了，**说明它是冲突的一部分** |
    /// | `Unreachable` | **移除**（`removed: true`） | 删掉它仍不可达，**说明它不是** |
    /// | `Unknown` / `Unsupported` | **都不改，标记为部分** | 没测出来，**不能当成任何一端的证据** |
    ///
    /// **第三种是最容易写错的**：把"没测出来"当成 `Unreachable`，会**把一个真实原因从冲突里删掉**——
    /// 于是报告给出的那份"最小冲突"少了一条，而它看起来完全正常。
    ///
    /// **The three outcomes differ completely and are one enum apart.** **The third is the easy mistake**: treating "not measured" as `Unreachable` **deletes a real cause from the
    /// conflict**, so the "minimal conflict" the report hands back is missing a member and looks entirely normal.
    ///
    /// 注意 `Reachable` 的结论方向：**删除后可达，意味着这个来源是必要的，所以要保留。** 这跟直觉相反，
    /// 而反过来写会让整份冲突恰好取补集。
    ///
    /// Note the direction on `Reachable`: **reachable after removal means the source was necessary, so it is kept.** That is the opposite of intuition, and flipping it makes the whole
    /// conflict come out as its complement.
    /// </remarks>
    public static (ConflictVerification Verification, bool RemoveFromActive) Check(
        DiagnosticSource source,
        AnalysisStatus status) {
        ArgumentNullException.ThrowIfNull(source);

        return status switch {
            AnalysisStatus.Reachable => (
                new ConflictVerification(
                    Source: source,
                    Status: status,
                    Removed: false,
                    Retained: true,
                    Message: "删除后 target 可达，保留来源 / Target is reachable after deletion; source retained"),
                false),
            AnalysisStatus.Unreachable => (
                new ConflictVerification(
                    Source: source,
                    Status: status,
                    Removed: true,
                    Retained: false,
                    Message: "删除后 target 仍不可达，移除来源 / Target remains unreachable after deletion; source removed"),
                true),
            _ => (
                new ConflictVerification(
                    Source: source,
                    Status: status,
                    // **两个都 `false`**：没测出来既不是"移除"也不是"保留"。
                    // **Both `false`**: an unmeasured check is neither a removal nor a retention.
                    Removed: false,
                    Retained: false,
                    Message: "删除复验未得出结论，保留来源并标记为部分 / The deletion check reached no conclusion; the source is kept and the conflict is marked partial"),
                false),
        };
    }

    /// <summary>
    /// 这次复验是否让整份冲突变成"部分" / Whether this verification makes the whole conflict partial.
    /// </summary>
    /// <param name="verification">复验 / The verification.</param>
    /// <returns>是否部分 / Whether it is partial.</returns>
    /// <remarks>
    /// **只有"两个都 `false`"才算部分。** 用 `Status != Reachable &amp;&amp; Status != Unreachable` 判断也可以，
    /// 但那样一来**新增一个状态就会悄悄变成"部分"**，而"部分"会让最小性结论从 `Irreducible` 退到 `Partial`。
    ///
    /// **Only "both `false`" counts as partial.** Testing the status would also work, but then **a newly added status would silently become "partial"**, which downgrades the minimality
    /// conclusion from `Irreducible` to `Partial`.
    /// </remarks>
    public static bool MakesPartial(ConflictVerification verification) {
        ArgumentNullException.ThrowIfNull(verification);
        return !verification.Removed && !verification.Retained;
    }

    /// <summary>
    /// 由一组复验汇总最小性 / Summarise minimality from a set of verifications.
    /// </summary>
    /// <param name="verifications">复验 / The verifications.</param>
    /// <param name="maxDeletionChecks">允许的复验次数上限 / The cap on deletion checks.</param>
    /// <returns>最小性结论 / The minimality conclusion.</returns>
    /// <remarks>
    /// **一次复验都没做时不能报 `Irreducible`。** 上限为 0 是"不做复验"的合法配置，
    /// **而 `Irreducible` 的意思是"每一条都试过了、每一条都必要"——一次没试过就说它，是凭空断言。**
    ///
    /// **Reporting `Irreducible` after zero checks is not allowed.** A cap of zero is a legitimate "do not check" setting, and **`Irreducible` means "every member was tried and
    /// every member proved necessary" — saying that having tried nothing is an assertion from nothing.**
    /// </remarks>
    public static ConflictMinimality SummarizeMinimality(
        IReadOnlyList<ConflictVerification> verifications,
        int maxDeletionChecks) {
        ArgumentNullException.ThrowIfNull(verifications);

        if (maxDeletionChecks <= 0 || verifications.Count == 0) {
            return ConflictMinimality.NotChecked;
        }
        return verifications.Any(MakesPartial)
            ? ConflictMinimality.Partial
            : ConflictMinimality.Irreducible;
    }

    /// <summary>
    /// 删除复验是否该停下 / Whether the deletion sweep has to stop.
    /// </summary>
    /// <param name="checksDone">已经做过的复验次数 / The checks already done.</param>
    /// <param name="maxDeletionChecks">上限 / The cap.</param>
    /// <param name="elapsed">已用时间 / The elapsed time.</param>
    /// <param name="budget">时间预算，可为 <c>null</c> / The time budget, or <c>null</c>.</param>
    /// <returns>该停下时为 <c>true</c> / <c>true</c> when it has to stop.</returns>
    /// <remarks>
    /// **这是 `ConflictAnalyzer.analyze` 删除循环里那道中止条件的实现。**
    ///
    /// **`checks &gt;= max` 而不是 `checks &gt; max`。** 用后者会**多做一次复验**，
    /// **而每一次复验都是一次完整求解**——多出来的那一次正好是最贵的那一次。
    ///
    /// **`checks &gt;= max`, not `checks &gt; max`.** The latter **runs one extra check**, **and every check is a full solve** — the extra one is exactly the expensive one.
    ///
    /// **`max == 0` 时第一次就停。** 那是"不做复验"的合法请求，**不是"做一次"**。
    ///
    /// **With `max == 0` it stops before the first check.** That is the legitimate "do not check" request, **not "check once"**.
    /// </remarks>
    public static bool ShouldStop(
        int checksDone,
        int maxDeletionChecks,
        TimeSpan elapsed,
        TimeSpan? budget) =>
        checksDone >= maxDeletionChecks || BudgetGate.Exceeded(elapsed, budget);

    /// <summary>
    /// 一次删除收缩的结果 / The result of one deletion shrink.
    /// </summary>
    /// <param name="Members">收缩后剩下的成员 / The members left after shrinking.</param>
    /// <param name="Verifications">逐项复验 / The per-source verifications.</param>
    /// <param name="Minimality">最小性结论 / The minimality conclusion.</param>
    /// <param name="Partial">是否至少一项未得出结论 / Whether at least one check reached no conclusion.</param>
    /// <param name="BackgroundBlocking">阻塞是否来自固定背景 / Whether the blocking comes from fixed background.</param>
    /// <param name="Checks">实际执行的复验次数 / How many checks actually ran.</param>
    public sealed record ShrinkResult(
        IReadOnlyList<DiagnosticSource> Members,
        IReadOnlyList<ConflictVerification> Verifications,
        ConflictMinimality Minimality,
        bool Partial,
        bool BackgroundBlocking,
        int Checks);

    /// <summary>
    /// 跑一轮删除收缩 / Run one pass of the deletion shrink.
    /// </summary>
    /// <param name="evidence">全部证据 / All the evidence.</param>
    /// <param name="initialStatus">初始目标状态 / The initial target status.</param>
    /// <param name="maxDeletionChecks">复验次数上限 / The cap on deletion checks.</param>
    /// <param name="timeBudget">时间预算，可为 <c>null</c> / The time budget, or <c>null</c>.</param>
    /// <param name="elapsed">已用时间 / Time already spent.</param>
    /// <param name="probe">对一个候选集合求目标状态 / The callback that evaluates a candidate set.</param>
    /// <returns>收缩结果 / The shrink result.</returns>
    /// <remarks>
    /// **单轮就够**，因为可行性判定是单调的：某成员删除后已证明可达，继续删别的来源
    /// **不会让那个候选重新变得不可行**。所以不需要反复迭代。
    ///
    /// **两条"只标记、不改动"的规则**：
    ///
    /// | 情况 | 处理 |
    /// | --- | --- |
    /// | 预算耗尽 | `Partial`，**不再删任何来源** |
    /// | 复验没结论 | `Partial`，**那个来源留在 `active` 里** |
    ///
    /// **第二条最容易写错**：把没结论的来源从 `active` 删掉，报告会少一个真实原因，
    /// 而 `members` 是从 `active` 推出来的——**于是整份报告一致地错。**
    ///
    /// **One pass suffices** because feasibility is monotone: once a member's deletion is proven reachable, deleting further sources **cannot make that candidate infeasible again**.
    /// Two rules only mark and never change the set: **running out of budget stops all removals**, and **an inconclusive check leaves its source in `active`**. The second is the easy
    /// mistake — dropping an inconclusive source from `active` loses a real cause, and since `members` is derived from `active` **the whole report is consistently wrong.**
    /// </remarks>
    public static ShrinkResult Shrink(
        IReadOnlyList<DiagnosticSource> evidence,
        AnalysisStatus initialStatus,
        int maxDeletionChecks,
        TimeSpan? timeBudget,
        TimeSpan elapsed,
        Func<IReadOnlyList<DiagnosticSource>, AnalysisStatus> probe) {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(probe);

        var active = new List<DiagnosticSource>(evidence);
        var verifications = new List<ConflictVerification>();
        bool partial = maxDeletionChecks == 0;
        int checks = 0;

        foreach (DiagnosticSource source in evidence) {
            if (!active.Contains(source)) {
                continue;
            }
            if (ShouldStop(checks, maxDeletionChecks, elapsed, timeBudget)) {
                partial = true;
                break;
            }

            var candidate = active.Where(item => !Equals(item, source)).ToList();
            (ConflictVerification verification, bool remove) = Check(source, probe(candidate));
            if (remove) {
                active.Remove(source);
            }
            if (MakesPartial(verification)) {
                partial = true;
            }
            verifications.Add(verification);
            checks++;
        }

        DiagnosticSource[] members = evidence.Where(active.Contains).ToArray();

        // **约束投影为空时，阻塞来自固定背景**（布尔值域、稀疏值域等），
        // 这种结果是有用的证据，**但不是约束级 MUS，绝不能标成 `Irreducible`。**
        //
        // **An empty constraint projection means the blocking comes from fixed background** (Boolean or sparse domains). That is useful evidence, **but it is not a
        // constraint-level MUS and must never be labelled `Irreducible`.**
        bool backgroundBlocking = initialStatus == AnalysisStatus.Unreachable
            && !members.Any(member => member is DiagnosticSource.Constraint);

        // **最终证据门槛**：每一个留下来的成员都必须有自己的、target-fixed、已证明可达的删除复验，
        // 否则不能声称 `Irreducible`。单轮收缩本来满足这个条件，**这道门槛是防止未来改动破坏它。**
        //
        // **The final evidence gate**: every remaining member needs its own target-fixed, proven-reachable deletion check, or `Irreducible` cannot be claimed. One pass already
        // satisfies it; **the gate exists so a future change cannot quietly break that.**
        bool everyMemberHasProvenDeletionCheck = members.All(member => verifications.Any(verification =>
            Equals(verification.Source, member)
            && verification.TargetFixed
            && verification.Retained
            && verification.Status == AnalysisStatus.Reachable));
        if (!everyMemberHasProvenDeletionCheck) {
            partial = true;
        }

        ConflictMinimality minimality = maxDeletionChecks == 0
            ? ConflictMinimality.NotChecked
            : backgroundBlocking || partial
                ? ConflictMinimality.Partial
                : ConflictMinimality.Irreducible;

        return new ShrinkResult(members, verifications, minimality, partial, backgroundBlocking, checks);
    }
}
