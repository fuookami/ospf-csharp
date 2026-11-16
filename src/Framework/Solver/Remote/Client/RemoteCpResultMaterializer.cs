// 远程 CP 结果物化 / Remote CP result materialization.
// 与`solveOutput` 的判定逻辑对应；Core 输出记录由调用方构造，原因见 `Classify`。
// `solveOutput` decision logic; the Core output record is
// built by the caller, for the reason given on `Classify`.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.Solver.Remote.Domain;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using NInt64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Framework.Solver.Remote.Client;

/// <summary>
/// 远程 CP 结果的物化分类 / Materialization classification of a remote CP result.
/// </summary>
/// <remarks>
/// 这是一个封闭的三态：不可行、未知、可行。用类型而不是布尔组合表达，调用方无法"忘记"某个分支。
/// This is a closed three-state result: infeasible, unknown or feasible. Types rather than a combination
/// of booleans mean the caller cannot forget a branch.
/// </remarks>
public abstract record CpMaterialization {
    /// <summary>不可行且已给出证明 / Infeasible with a proof.</summary>
    /// <param name="ProofStatus">证明状态 / Proof status.</param>
    public sealed record Infeasible(RemoteProofStatus? ProofStatus) : CpMaterialization;

    /// <summary>结果未知 / Outcome unknown.</summary>
    /// <param name="TerminationReason">终止原因 / Termination reason.</param>
    public sealed record Unknown(RemoteTerminationReason? TerminationReason) : CpMaterialization;

    /// <summary>可行解已物化 / A feasible solution has been materialized.</summary>
    /// <param name="Solution">稳定解 / Stable solution.</param>
    /// <param name="ExactObjective">精确整数目标 / Exact integer objective.</param>
    /// <param name="BestBound">最佳界（浮点兼容值）/ Best bound (compatibility float).</param>
    /// <param name="ProvenOptimal">是否已证明最优 / Whether optimality is proven.</param>
    public sealed record Feasible(
        ConstraintProgrammingSolution Solution,
        long? ExactObjective,
        Flt64? BestBound,
        bool ProvenOptimal) : CpMaterialization;
}

/// <summary>
/// 远程 CP 结果物化器 / Remote CP result materializer.
/// </summary>
public static class RemoteCpResultMaterializer {
    /// <summary>
    /// 判定结果属于哪一态，并校验稳定解与本次 snapshot 相符 / Classify the result and validate that
    /// the stable solution matches this snapshot.
    /// </summary>
    /// <remarks>
    /// 与基线 `solveOutput` 的判定顺序一致：
    /// 1. 不可行 + 问题状态 `INFEASIBLE` + 终止原因 `COMPLETED` → **不可行**（有明确"不存在解"的结论）。
    /// 2. 其余不可行 → **未知**（不能把"没算完"当成"无解"）。
    /// 3. 可行但没有稳定解：若 snapshot 里存在变量或 interval，说明解本应被物化却缺失 → 报错，而不是
    ///    伪造一个空解；只有模型确实为空时才允许空解。
    /// 4. 可行且有稳定解：变量集合必须与 snapshot **完全一致**（多一个或少一个都拒绝，否则模型的解与
    ///    返回的解不是同一个问题）、每个值必须落在其值域内、interval 集合必须一致且满足其定义。
    ///
    /// **不在这里构造 Core 输出记录**：`ConstraintProgrammingFeasibleOutput` 需要一个
    /// `SolveReport&lt;Int64&gt;`，而 C# 的 `SolverProvenance` 与形状不同
    /// （`Descriptor`+`Configuration` 对比 `nativeVersion`/`effectiveParameters`/`threadCount`/…），
    /// 如何把provenance 字段落进 C# 结构是一个 **Core 设计决定**，不该由远程适配层顺手发明。
    /// 因此本方法只做判定与校验，输出记录留给调用方；这样不会产出"字段缺失但看起来成功"的对象。
    ///
    /// The decision order matches the code: infeasible with `INFEASIBLE` and `COMPLETED` is a proven
    /// infeasibility; any other infeasible result is **unknown**, because "did not finish" must not be
    /// reported as "no solution exists"; a feasible result without a stable solution is an error whenever
    /// the snapshot actually has variables or intervals, since the solution should have been materialized
    /// — no empty solution is fabricated; and a feasible result with a stable solution must match the
    /// snapshot's variable set **exactly** (one extra or missing means the returned solution answers a
    /// different problem), keep every value inside its domain, and match the interval set with each interval
    /// satisfying its definition.
    ///
    /// **The Core output record is not built here**: `ConstraintProgrammingFeasibleOutput` needs a
    /// `SolveReport&lt;Int64&gt;`, and C#'s `SolverProvenance` has a different shape from its
    /// (`Descriptor`+`Configuration` versus `nativeVersion`/`effectiveParameters`/`threadCount`/…). Deciding
    /// how its provenance fields map onto the C# structure is a **Core design decision** that the
    /// remote adapter must not invent on the side, so this method only classifies and validates. That keeps
    /// it from producing a successfully-typed object with silently missing fields.
    /// </remarks>
    /// <param name="snapshot">本次请求的 snapshot / The snapshot of this request.</param>
    /// <param name="slice">原始切片结果 / Raw slice result.</param>
    /// <param name="serialized">稳定解工件，可为空 / Stable-solution artifact, may be null.</param>
    /// <returns>物化分类 / Materialization classification.</returns>
    public static Result<CpMaterialization, ErrorCode, Error<ErrorCode>> Classify(
        ConstraintProgrammingModelSnapshot snapshot,
        SliceResult slice,
        SerializedSolution? serialized) {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(slice);

        if (!slice.Feasible) {
            return slice.ProblemStatus == RemoteProblemStatus.Infeasible &&
                slice.TerminationReason == RemoteTerminationReason.Completed
                ? Results.Ok<CpMaterialization>(
                    new CpMaterialization.Infeasible(slice.ProofStatus))
                : Results.Ok<CpMaterialization>(
                    new CpMaterialization.Unknown(slice.TerminationReason));
        }

        if (serialized is null) {
            if (snapshot.Variables.Count > 0 || snapshot.Intervals.Count > 0) {
                return Invalid<CpMaterialization>(
                    "远程 CP 解缺少 resultRef：无法物化稳定解 / "
                    + "Remote CP resultRef is missing; stable solution cannot be materialized");
            }
            return Results.Ok<CpMaterialization>(new CpMaterialization.Feasible(
                Solution: new ConstraintProgrammingSolution(
                    new Dictionary<VariableId, NInt64>(),
                    new Dictionary<IntervalId, IntervalValue>()),
                ExactObjective: slice.ObjectiveValueInt64,
                BestBound: ParseBestBound(slice),
                ProvenOptimal: slice.SolutionPresence == RemoteSolutionPresence.Optimal &&
                    slice.ProofStatus == RemoteProofStatus.Verified));
        }

        if (!serialized.Feasible) {
            return Invalid<CpMaterialization>(
                "远程 CP 协议结果与稳定解可行性不一致 / "
                + "Remote CP feasibility disagrees with serialized solution");
        }

        var expectedVariables = new HashSet<string>(
            snapshot.Variables.Select(variable => variable.Id.Value), StringComparer.Ordinal);
        var actualVariables = new HashSet<string>(
            serialized.VariableValuesById?.Keys ?? Array.Empty<string>(), StringComparer.Ordinal);
        string[] unknownVariables = actualVariables.Except(expectedVariables).OrderBy(
            id => id, StringComparer.Ordinal).ToArray();
        string[] missingVariables = expectedVariables.Except(actualVariables).OrderBy(
            id => id, StringComparer.Ordinal).ToArray();
        if (unknownVariables.Length > 0 || missingVariables.Length > 0) {
            return Invalid<CpMaterialization>(
                $"远程 CP 稳定变量集合不匹配：unknown={string.Join(",", unknownVariables)}, "
                + $"missing={string.Join(",", missingVariables)} / "
                + "Remote CP stable variable IDs do not match the snapshot");
        }

        var expectedIntervals = new HashSet<string>(
            snapshot.Intervals.Select(interval => interval.Id.Value), StringComparer.Ordinal);
        // 缺失的 interval 字典等同空集合：没有 interval 变量的模型，后端完全可以不下发该字段。直接解引用
        // 会空引用崩溃，而不是报出"集合不匹配"这种可诊断的错误。
        // A missing interval map means an empty set: a backend modelling no interval variables may legitimately
        // omit the field. Dereferencing it crashes instead of reporting the diagnosable set mismatch.
        var actualIntervals = new HashSet<string>(
            serialized.IntervalValues?.Keys ?? Array.Empty<string>(), StringComparer.Ordinal);
        string[] unknownIntervals = actualIntervals.Except(expectedIntervals).OrderBy(
            id => id, StringComparer.Ordinal).ToArray();
        string[] missingIntervals = expectedIntervals.Except(actualIntervals).OrderBy(
            id => id, StringComparer.Ordinal).ToArray();
        if (unknownIntervals.Length > 0 || missingIntervals.Length > 0) {
            return Invalid<CpMaterialization>(
                $"远程 CP interval 集合不匹配：unknown={string.Join(",", unknownIntervals)}, "
                + $"missing={string.Join(",", missingIntervals)} / "
                + "Remote CP interval IDs do not match the snapshot");
        }

        var values = new Dictionary<VariableId, NInt64>(
            serialized.VariableValuesById?.Count ?? 0);
        foreach (KeyValuePair<string, long> entry in
            serialized.VariableValuesById ?? new Dictionary<string, long>()) {
            values[new VariableId(entry.Key)] = new NInt64(entry.Value);
        }
        foreach (ConstraintProgrammingVariableSnapshot definition in snapshot.Variables) {
            NInt64 value = values[definition.Id];
            if (!definition.Domain.Contains(value)) {
                return Invalid<CpMaterialization>(
                    $"远程 CP 变量值超出值域：{definition.Id.Value}={value} / "
                    + $"Remote CP variable value is outside its domain: {definition.Id.Value}={value}");
            }
        }

        var intervals = new Dictionary<IntervalId, IntervalValue>(
            serialized.IntervalValues?.Count ?? 0);
        foreach (KeyValuePair<string, SerializedIntervalValue> entry in
            serialized.IntervalValues ?? new Dictionary<string, SerializedIntervalValue>()) {
            intervals[new IntervalId(entry.Key)] = new IntervalValue(
                Start: new NInt64(entry.Value.Start),
                Size: new NInt64(entry.Value.Size),
                End: new NInt64(entry.Value.End),
                Present: entry.Value.Present);
        }
        foreach (IntervalVariable definition in snapshot.Intervals) {
            IntervalValue actual = intervals[definition.Id];
            Result<IntervalValue, ErrorCode, Error<ErrorCode>> evaluated =
                definition.Evaluate(values);
            if (!evaluated.IsOk) {
                return Invalid<CpMaterialization>(
                    $"远程 CP interval 解无法按定义求值：{definition.Id.Value} / "
                    + $"Remote CP interval solution cannot be evaluated: {definition.Id.Value}");
            }
            if (!Equals(evaluated.Value, actual)) {
                return Invalid<CpMaterialization>(
                    $"远程 CP interval 解不满足定义：{definition.Id.Value} / "
                    + $"Remote CP interval solution violates its definition: {definition.Id.Value}");
            }
        }

        long? exactObjective = serialized.ObjectiveValueInt64 ?? slice.ObjectiveValueInt64;
        Result<Success, ErrorCode, Error<ErrorCode>> validated = ValidateAgainstSnapshot(
            snapshot: snapshot,
            values: values,
            reportedObjective: exactObjective);
        if (validated is Failed<Success, ErrorCode, Error<ErrorCode>> failure) {
            // 透传原始消息：里面带着具体的约束／目标标识，泛化掉会让错误变得不可用。
            // Propagate the original message: it names the offending constraint or objective, and
            // generalizing it would make the error useless.
            return Results.Failed<CpMaterialization>(failure.Error);
        }
        if (!validated.IsOk) {
            return Invalid<CpMaterialization>(
                "远程 CP 解未通过模型校验 / Remote CP solution failed model validation");
        }
        return Results.Ok<CpMaterialization>(new CpMaterialization.Feasible(
            Solution: new ConstraintProgrammingSolution(values, intervals),
            ExactObjective: exactObjective,
            BestBound: ParseBestBound(slice),
            ProvenOptimal: slice.SolutionPresence == RemoteSolutionPresence.Optimal &&
                slice.ProofStatus == RemoteProofStatus.Verified));
    }

    /// <summary>
    /// 用 snapshot 重新校验解与目标 / Re-validate the solution and objective against the snapshot.
    /// </summary>
    /// <remarks>
    /// ：**不信任后端算出来的东西**——用本次请求的 snapshot 把每条约束重新求值一遍，并独立计算
    /// 目标值再与后端报告的目标比较。这一步能抓住"后端返回了自相一致、但根本不满足这个模型的解"，而防串台
    /// 指纹抓不到这种情况（那只保证量的是同一份输入，不保证后端算对了）。
    ///
    /// 两条容易漏的规则：snapshot 没有目标却返回了目标值要拒绝（否则凭空冒出一个目标）；有目标却没有报告
    /// 目标值也要拒绝（不能拿别处的值凑数）。
    ///
    /// As in the code this **does not trust what the backend computed**: every constraint is re-evaluated
    /// against this request's snapshot and the objective is recomputed independently and compared with the
    /// reported value. This catches "the backend returned a self-consistent solution that does not satisfy
    /// this model", which the cross-talk fingerprints cannot — those only prove the same input was measured,
    /// not that the backend computed correctly. Two easy-to-miss rules: a snapshot without an objective must
    /// not report one, and a snapshot with an objective must report a value rather than borrowing one.
    /// </remarks>
    /// <param name="snapshot">本次请求的 snapshot / The snapshot of this request.</param>
    /// <param name="values">解中的变量值 / Variable values from the solution.</param>
    /// <param name="reportedObjective">后端报告的目标值 / Objective reported by the backend.</param>
    /// <returns>失败时给出具体违约项 / A structured failure naming the offending element.</returns>
    private static Result<Success, ErrorCode, Error<ErrorCode>> ValidateAgainstSnapshot(
        ConstraintProgrammingModelSnapshot snapshot,
        IReadOnlyDictionary<VariableId, NInt64> values,
        long? reportedObjective) {
        foreach (ConstraintProgrammingConstraintSnapshot definition in snapshot.Constraints) {
            Result<bool, ErrorCode, Error<ErrorCode>> satisfied =
                definition.Constraint.IsSatisfied(values);
            if (!satisfied.IsOk) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ORSolutionInvalid,
                    $"远程 CP 约束无法求值：{definition.Id.Value} / "
                    + $"Remote CP constraint cannot be evaluated: {definition.Id.Value}"));
            }
            if (!satisfied.Value) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ORSolutionInvalid,
                    $"远程 CP 解违反约束：{definition.Id.Value} / "
                    + $"Remote CP solution violates constraint: {definition.Id.Value}"));
            }
        }

        ConstraintProgrammingObjectiveSnapshot? objective = snapshot.Objectives.Count > 0
            ? snapshot.Objectives[0]
            : null;
        if (objective is null) {
            return reportedObjective is null
                ? Results.OkInstance
                : Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ORSolutionInvalid,
                    "snapshot 没有目标但远程 CP 返回了目标值 / "
                    + "Remote CP returned an objective for a snapshot without an objective"));
        }
        Result<NInt64, ErrorCode, Error<ErrorCode>> evaluated = objective.Expression.Evaluate(values);
        if (!evaluated.IsOk) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ORSolutionInvalid,
                $"远程 CP 目标无法求值：{objective.Id.Value} / "
                + $"Remote CP objective cannot be evaluated: {objective.Id.Value}"));
        }
        if (reportedObjective is null) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ORSolutionInvalid,
                $"远程 CP 解缺少目标值：{objective.Id.Value} / "
                + $"Remote CP result is missing the objective value: {objective.Id.Value}"));
        }
        long expected = evaluated.Value.ToLong();
        return reportedObjective.Value == expected
            ? Results.OkInstance
            : Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ORSolutionInvalid,
                $"远程 CP 目标值与 snapshot 求值不一致：reported={reportedObjective.Value}, "
                + $"expected={expected} / Remote CP objective disagrees with the snapshot evaluation: "
                + $"reported={reportedObjective.Value}, expected={expected}"));
    }

    /// <summary>
    /// 把物化分类转成 Core 输出记录 / Turn a materialization classification into a Core output record.
    /// </summary>
    /// <remarks>
    /// 的状态判定：**只有"已证明最优"（解存在性为 `OPTIMAL` 且证明状态为 `Verified`）才是
    /// `SolverStatus.Optimal`**，其余一律 `Feasible`。声称最优但没证明时降级，而不是沿用后端的乐观说法。
    ///
    /// 诊断解析失败会让本方法整体失败，而不是产出一份缺诊断的报告——CP 的诊断里带不可行证据。
    ///
    /// The status rule matches the code: only a **proven** optimum (`OPTIMAL` presence with a `Verified`
    /// proof) is `SolverStatus.Optimal`; everything else is `Feasible`. An unproven optimality claim is
    /// downgraded rather than copied from the backend's optimism. A diagnostics parse failure fails the whole
    /// call instead of producing a report without diagnostics, because CP diagnostics carry infeasibility
    /// evidence.
    /// </remarks>
    /// <param name="classification">物化分类 / Materialization classification.</param>
    /// <param name="slice">切片结果 / Slice result.</param>
    /// <returns>CP 求解输出 / CP solver output.</returns>
    public static Result<ConstraintProgrammingSolverOutput, ErrorCode, Error<ErrorCode>> ToOutput(
        CpMaterialization classification,
        SliceResult slice) {
        ArgumentNullException.ThrowIfNull(classification);
        ArgumentNullException.ThrowIfNull(slice);

        switch (classification) {
            case CpMaterialization.Infeasible infeasible: {
                Result<SolveReport<NInt64>, ErrorCode, Error<ErrorCode>> report =
                    RemoteSolverReportMapping.ToCpReport(slice, solution: null);
                return report.IsOk
                    ? Results.Ok<ConstraintProgrammingSolverOutput>(
                        new ConstraintProgrammingInfeasibleOutput(
                            proofStatus: RemoteSolverReportMapping.ToCoreProofStatus(
                                infeasible.ProofStatus),
                            report: report.Value))
                    : Results.Failed<ConstraintProgrammingSolverOutput>(
                        new Err<ErrorCode>(ErrorCode.ORSolutionInvalid, DiagnosticsFailedMessage));
            }
            case CpMaterialization.Unknown unknown: {
                Result<SolveReport<NInt64>, ErrorCode, Error<ErrorCode>> report =
                    RemoteSolverReportMapping.ToCpReport(slice, solution: null);
                return report.IsOk
                    ? Results.Ok<ConstraintProgrammingSolverOutput>(
                        new ConstraintProgrammingUnknownOutput(
                            terminationReason: RemoteSolverReportMapping.ToCoreTerminationReason(
                                unknown.TerminationReason),
                            report: report.Value))
                    : Results.Failed<ConstraintProgrammingSolverOutput>(
                        new Err<ErrorCode>(ErrorCode.ORSolutionInvalid, DiagnosticsFailedMessage));
            }
            case CpMaterialization.Feasible feasible: {
                Result<SolveReport<NInt64>, ErrorCode, Error<ErrorCode>> report =
                    RemoteSolverReportMapping.ToCpReport(slice, feasible.Solution);
                return report.IsOk
                    ? Results.Ok<ConstraintProgrammingSolverOutput>(
                        new ConstraintProgrammingFeasibleOutput(
                            solution: feasible.Solution,
                            objective: feasible.ExactObjective is { } exact
                                ? new Flt64(exact)
                                : null,
                            bestBound: feasible.BestBound,
                            status: feasible.ProvenOptimal
                                ? SolverStatus.Optimal
                                : SolverStatus.Feasible,
                            proofStatus: RemoteSolverReportMapping.ToCoreProofStatus(
                                slice.ProofStatus),
                            report: report.Value,
                            exactObjective: feasible.ExactObjective is { } exactValue
                                ? new NInt64(exactValue)
                                : null))
                    : Results.Failed<ConstraintProgrammingSolverOutput>(
                        new Err<ErrorCode>(ErrorCode.ORSolutionInvalid, DiagnosticsFailedMessage));
            }
            default:
                return Results.Failed<ConstraintProgrammingSolverOutput>(
                    new Err<ErrorCode>(
                        ErrorCode.ApplicationFailed,
                        "未知的物化分类 / Unknown materialization classification"));
        }
    }

    private const string DiagnosticsFailedMessage =
        "远程 CP 诊断解析失败，拒绝产出缺诊断的报告 / "
        + "Remote CP diagnostics cannot be parsed; refusing to emit a report without diagnostics";

    private static Flt64? ParseBestBound(SliceResult slice) {
        if (slice.Statistics is null ||
            !slice.Statistics.TryGetValue("bestBound", out string? raw) ||
            !double.TryParse(
                raw,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out double parsed)) {
            return null;
        }
        return new Flt64(parsed);
    }

    private static Result<T, ErrorCode, Error<ErrorCode>> Invalid<T>(string message) =>
        Results.Failed<T>(new Err<ErrorCode>(ErrorCode.ORSolutionInvalid, message));
}
