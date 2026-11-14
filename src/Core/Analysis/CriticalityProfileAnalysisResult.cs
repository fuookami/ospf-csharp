#nullable enable

using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Core.Analysis;

/// <summary>
/// 多目标 pipeline 运行的完整结果 / The complete result of a multi-target pipeline run.
/// </summary>
/// <remarks>
/// **这是 `CriticalityProfile.kt` 里 `CriticalityProfileAnalysisResult` 的移植。**
///
/// **各个报告被保留下来**，使调用方无需从汇总 profile 重建目标、扰动与冲突证据就能检查它们。
///
/// **The individual reports are kept**, so a caller can inspect target, perturbation and conflict evidence without reconstructing it from the aggregate profile.
///
/// | 字段 | 为什么必须在 | Why it has to be here |
/// | --- | --- | --- |
/// | `Reports` | **汇总会把个别目标的结论抹平** | **an aggregate flattens the per-target conclusions** |
/// | `Profile` | 跨目标的分类 | the cross-target classification |
/// | `ImprovementPlans` | 只来自删除已验证的冲突 | derived only from deletion-verified conflicts |
/// | `Status` | **批次里最弱的那个结论** | **the weakest conclusion in the batch** |
///
/// **`Status` 是"最弱"而不是"最后一个"。** 取最后一个会让批次末尾的一个成功
/// **盖住前面某个 `Unknown`**——而 `Unknown` 恰恰是那份报告唯一想让人看见的东西。
///
/// **`Status` is the weakest, not the last.** Taking the last lets a success at the end of the batch **cover an earlier `Unknown`** — and that `Unknown` is the one thing the report exists to show.
/// </remarks>
public sealed record CriticalityProfileAnalysisResult(
    IReadOnlyList<CriticalConstraintAnalysisReport> Reports,
    CriticalityProfile Profile,
    IReadOnlyList<AlternativeImprovementPlan> ImprovementPlans,
    AnalysisStatus Status,
    string SchemaVersion = "1.0")
{
    /// <summary>
    /// 与 <see cref="Reports"/> 同义的兼容名 / A compatibility name for <see cref="Reports"/>.
    /// </summary>
    /// <remarks>
    /// **基线里这是一个 `get()` 别名。** 保留它是因为调用方读的是"目标报告"这个意图。
    ///
    /// **The baseline has this as a `get()` alias.** It is kept because the caller reads the intent "target reports".
    /// </remarks>
    public IReadOnlyList<CriticalConstraintAnalysisReport> TargetReports => Reports;

    /// <summary>
    /// 与 <see cref="ImprovementPlans"/> 同义的兼容名 / A compatibility name for <see cref="ImprovementPlans"/>.
    /// </summary>
    /// <remarks>
    /// **别名不是冗余**：读 `Recommendations` 的人以为自己在看"建议"，
    /// 而读 `ImprovementPlans` 的人知道**这些方案背后有已验证的最小冲突。** 同一个列表，两种预期。
    ///
    /// **The alias is not redundant**: someone reading `Recommendations` thinks they are looking at advice, while someone reading `ImprovementPlans` knows **each plan is backed by a verified
    /// minimal conflict.** The same list under two expectations.
    /// </remarks>
    public IReadOnlyList<AlternativeImprovementPlan> Recommendations => ImprovementPlans;

    /// <summary>
    /// 从一批报告构造结果，并算出最弱状态 / Build the result and derive the weakest status.
    /// </summary>
    /// <param name="reports">每个目标一份报告，按输入顺序 / One report per target, in input order.</param>
    /// <param name="profile">跨目标分类 / The cross-target classification.</param>
    /// <param name="improvementPlans">改进方案 / The improvement plans.</param>
    /// <param name="schemaVersion">schema 版本 / The schema version.</param>
    /// <returns>完整结果 / The complete result.</returns>
    /// <remarks>
    /// **最弱状态由调用方决定不了，所以由这里算。** 让每个调用点自己算，
    /// **总会有人写成"取最后一个"或"全成功才算成功"。**
    ///
    /// **The weakest status cannot be left to the caller.** Let every call site compute it and **someone always writes "the last one" or "success only if all succeeded".**
    /// </remarks>
    public static CriticalityProfileAnalysisResult Build(
        IReadOnlyList<CriticalConstraintAnalysisReport> reports,
        CriticalityProfile profile,
        IReadOnlyList<AlternativeImprovementPlan>? improvementPlans = null,
        string schemaVersion = "1.0") {
        ArgumentNullException.ThrowIfNull(reports);
        ArgumentNullException.ThrowIfNull(profile);

        return new CriticalityProfileAnalysisResult(
            Reports: reports,
            Profile: profile,
            ImprovementPlans: improvementPlans ?? Array.Empty<AlternativeImprovementPlan>(),
            Status: WeakestStatus(reports),
            SchemaVersion: schemaVersion);
    }

    /// <summary>
    /// 在一批状态里找最弱的 / Find the weakest among a batch of statuses.
    /// </summary>
    /// <param name="statuses">状态序列 / The statuses.</param>
    /// <returns>最弱状态 / The weakest status.</returns>
    /// <remarks>
    /// 顺序是 **`Unsupported` &lt; `Unknown` &lt; `Unreachable` &lt; `Reachable`**：
    /// 越靠左，这个批次能下的结论越少。**空批次给 `Unknown`**——没有报告就没有结论，
    /// **而不是"全部可达"。**
    ///
    /// The order is **`Unsupported` &lt; `Unknown` &lt; `Unreachable` &lt; `Reachable`**: further left means fewer conclusions this batch can support. **An empty batch yields `Unknown`** — no report
    /// means no conclusion, **never "everything is reachable".**
    /// </remarks>
    public static AnalysisStatus WeakestStatus(IReadOnlyList<AnalysisStatus> statuses) {
        ArgumentNullException.ThrowIfNull(statuses);

        if (statuses.Count == 0) {
            return AnalysisStatus.Unknown;
        }

        int weakest = int.MaxValue;
        foreach (AnalysisStatus status in statuses) {
            weakest = global::System.Math.Min(weakest, WeaknessRank(status));
        }

        return weakest switch {
            0 => AnalysisStatus.Unsupported,
            1 => AnalysisStatus.Unknown,
            2 => AnalysisStatus.Unreachable,
            _ => AnalysisStatus.Reachable,
        };
    }

    /// <summary>
    /// 在一批报告里找最弱的 / Find the weakest among a batch of reports.
    /// </summary>
    /// <param name="reports">报告列表 / The reports.</param>
    /// <returns>最弱状态 / The weakest status.</returns>
    public static AnalysisStatus WeakestStatus(IReadOnlyList<CriticalConstraintAnalysisReport> reports) {
        ArgumentNullException.ThrowIfNull(reports);

        var statuses = new AnalysisStatus[reports.Count];
        for (int index = 0; index < reports.Count; ++index) {
            statuses[index] = reports[index].Status;
        }

        return WeakestStatus(statuses);
    }

    private static int WeaknessRank(AnalysisStatus status) => status switch {
        AnalysisStatus.Unsupported => 0,
        AnalysisStatus.Unknown => 1,
        AnalysisStatus.Unreachable => 2,
        _ => 3,
    };

    /// <summary>
    /// 校验报告与观测逐位配对 / Validate that the reports and observations are paired element by element.
    /// </summary>
    /// <returns>成功，或结构化错误 / Success, or a structured error.</returns>
    /// <remarks>
    /// **这是基线里那三道 `require` 的移植。** 它们挡的不是"参数为空"，
    /// **而是"两份列表来自不同的运行"**——
    /// **而那是这个结果类型唯一能错得悄无声息的方式。**
    ///
    /// 分类、阻塞摘要、修复建议全部按**位置**把报告和观测配在一起。
    /// **错位一位不会抛异常，只会让结论看着依然合理**：
    /// **状态来自 A 号目标，阻塞集合来自 B 号目标，而两者都合法。**
    ///
    /// **This is the port of the three `require` guards in the baseline.** They do not guard against empty arguments — **they guard against two lists coming from different runs**, **which is the one way this result type can be silently wrong.**
    /// Classification, the blocking summary, and the repair advice all pair reports and observations **by position**.
    /// **One step of misalignment raises nothing; it only makes the conclusion look reasonable**: **status from target A, blocking set from target B, both valid.**
    /// </remarks>
    public Result<Success, ErrorCode, Error<ErrorCode>> Validate() {
        if (string.IsNullOrWhiteSpace(SchemaVersion)) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "多目标 profile 的 schema 版本不得为空 / Multi-target profile schema version must not be blank"));
        }

        if (Reports.Count != Profile.Observations.Count) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                $"每个目标报告都需要一条 profile 观测：{Reports.Count} 份报告 / {Profile.Observations.Count} 条观测"
                + " / One profile observation is required for every target report: "
                + $"{Reports.Count} reports / {Profile.Observations.Count} observations"));
        }

        for (int index = 0; index < Reports.Count; index++) {
            CriticalConstraintAnalysisReport report = Reports[index];
            CriticalityObservation observation = Profile.Observations[index];

            if (report.Target is not { } target) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    $"第 {index} 份报告没有目标 / Report {index} carries no target"));
            }

            // **目标与状态各查一次，而不是只查一个。**
            // **两个都对的组合才叫"同一份分析"**：目标对而状态错，说明这是同名目标的两次不同运行。
            // **Check both the target and the status, not just one.**
            // **Only both being right makes it "the same analysis"**: the right target with the wrong status means two different runs over the same-named target.
            if (!Equals(target.Target, observation.Target)) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    $"第 {index} 条 profile 观测的目标与其报告不符 / Profile observation {index} does not match the target of its report"));
            }

            if (target.Status != observation.Status) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    $"第 {index} 条 profile 观测的状态与其报告不符 / Profile observation {index} does not match the status of its report"));
            }
        }

        return Results.OkInstance;
    }
}
