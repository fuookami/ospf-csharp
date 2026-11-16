// 远程求解报告的组装 / Remote solve-report assembly.
// 与`RemoteReportMapping.toRemoteSolveReport` 对应。
// `RemoteReportMapping.toRemoteSolveReport`.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Framework.Solver.Remote.Domain;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Solver.Remote.Client;

/// <summary>
/// 远程切片结果到 Core 求解报告的映射 / Mapping from a remote slice result to a Core solve report.
/// </summary>
/// <remarks>
/// 独立成文件而不是继续堆进 <see cref="RemoteReportMapping"/>：后者已接近千行，且本类承担的是"组装完整
/// 报告"这一较重的职责，与那些逐字段的小映射不是同一层次。
/// Kept separate from <see cref="RemoteReportMapping"/> because that type is already near a thousand lines
/// and this class carries the heavier job of assembling a whole report, which is a different layer from the
/// field-by-field mappings there.
/// </remarks>
public static class RemoteSolverReportMapping {
    /// <summary>
    /// 组装远程求解报告 / Assemble the remote solve report.
    /// </summary>
    /// <remarks>
    /// ：报告以**切片结果**为骨架（状态、终止原因、解存在性、证明、统计、诊断、来源、指纹），
    /// 传入的 <paramref name="output"/> 只贡献解本身与最佳界——它来自物化阶段，可能为空（例如只有
    /// incumbent 没有稳定解时）。
    ///
    /// 关键的一点：**`BestBound` 优先取物化结果、回退到统计字段**。统计字段是后端自报，物化结果才是
    /// 经过校验的那一份，因此有物化值时不能又被一个未校验的自报值覆盖。
    ///
    /// the report is built on the **slice result** (status, termination, presence,
    /// proof, statistics, diagnostics, provenance, fingerprints), and the passed-in
    /// <paramref name="output"/> contributes only the solution itself and the best bound — it comes from the
    /// materialization stage and may be absent. Importantly, **`BestBound` prefers the materialized value and
    /// only falls back to the statistics field**: statistics are self-reported by the backend while the
    /// materialized value has been validated, so a validated value must not be overwritten by an
    /// unvalidated self-report.
    /// </remarks>
    /// <param name="slice">切片结果 / Slice result.</param>
    /// <param name="output">物化报告，可为空 / Materialized report, may be null.</param>
    /// <param name="modelTypes">模型类型能力 / Model-type capabilities.</param>
    /// <returns>求解报告 / Solve report.</returns>
    public static Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> ToRemoteSolveReport(
        SliceResult slice,
        SolveReport<Flt64>? output,
        IReadOnlySet<SolverModelType> modelTypes) {
        ArgumentNullException.ThrowIfNull(slice);
        ArgumentNullException.ThrowIfNull(modelTypes);

        return Results.Ok(new SolveReport<Flt64> {
            SchemaVersion = string.IsNullOrWhiteSpace(slice.SchemaVersion)
                ? SolveReport<Flt64>.CurrentSchemaVersion
                : slice.SchemaVersion,
            RunId = slice.RunId is { Length: > 0 } runId ? new SolveRunId(runId) : null,
            ProblemStatus = ToCoreProblemStatus(slice.ProblemStatus),
            TerminationReason = ToCoreTerminationReason(slice.TerminationReason),
            SolutionPresence = ToCoreSolutionPresence(slice.SolutionPresence),
            Solution = output?.Solution,
            Proof = new SolveProof(
                Status: ToCoreProofStatus(slice.ProofStatus),
                Kind: ReadProvenance(slice, "proof.kind")),
            Statistics = new SolveStatistics(
                SolveTime: slice.Elapsed,
                Iterations: ReadStatisticULong(slice, "iterations"),
                Nodes: ReadStatisticULong(slice, "nodes"),
                BestBound: output?.Statistics.BestBound ?? ReadStatisticFlt64(slice, "bestBound"),
                Gap: slice.Gap ?? ReadStatisticFlt64(slice, "gap")),
            Diagnostics = slice.SolveDiagnostics ?? new SolveDiagnostics<Flt64>(),
            Provenance = BuildProvenance(slice, modelTypes),
            Fingerprints = BuildFingerprints(slice),
        });
    }

    /// <summary>
    /// 远程问题状态转 Core 问题状态 / Convert a remote problem status to its Core counterpart.
    /// </summary>
    /// <param name="status">远程状态，可为空 / Remote status, may be null.</param>
    /// <returns>Core 状态；未知或缺失一律 <c>Unknown</c> / The Core status; unknown or absent yields <c>Unknown</c>.</returns>
    public static ProblemStatus ToCoreProblemStatus(RemoteProblemStatus? status) => status switch {
        RemoteProblemStatus.Feasible => ProblemStatus.Feasible,
        RemoteProblemStatus.Infeasible => ProblemStatus.Infeasible,
        RemoteProblemStatus.Unbounded => ProblemStatus.Unbounded,
        RemoteProblemStatus.InfeasibleOrUnbounded => ProblemStatus.InfeasibleOrUnbounded,
        _ => ProblemStatus.Unknown,
    };

    /// <summary>
    /// 远程终止原因转 Core 终止原因 / Convert a remote termination reason to its Core counterpart.
    /// </summary>
    /// <param name="reason">远程终止原因，可为空 / Remote termination reason, may be null.</param>
    /// <returns>Core 终止原因 / The Core termination reason.</returns>
    public static TerminationReason ToCoreTerminationReason(RemoteTerminationReason? reason) =>
        reason switch {
            RemoteTerminationReason.Completed => TerminationReason.Completed,
            RemoteTerminationReason.TimeLimit => TerminationReason.TimeLimit,
            RemoteTerminationReason.NodeLimit => TerminationReason.NodeLimit,
            RemoteTerminationReason.IterationLimit => TerminationReason.IterationLimit,
            RemoteTerminationReason.SolutionLimit => TerminationReason.SolutionLimit,
            RemoteTerminationReason.ObjectiveLimit => TerminationReason.ObjectiveLimit,
            RemoteTerminationReason.Cancelled => TerminationReason.Cancelled,
            RemoteTerminationReason.Interrupted => TerminationReason.Interrupted,
            RemoteTerminationReason.NumericalFailure => TerminationReason.NumericalFailure,
            // ：UNKNOWN 与缺失都归为 BackendFailure——"没给出终止原因"本身就是后端的问题，
            // 不能伪装成 Completed。
            // Both UNKNOWN and a missing reason map to BackendFailure: not reporting a
            // termination reason is itself a backend problem and must not masquerade as Completed.
            _ => TerminationReason.BackendFailure,
        };

    /// <summary>
    /// 远程解存在性转 Core 解存在性 / Convert a remote solution presence to its Core counterpart.
    /// </summary>
    /// <param name="presence">远程存在性，可为空 / Remote presence, may be null.</param>
    /// <returns>Core 存在性 / The Core presence.</returns>
    public static SolutionPresence ToCoreSolutionPresence(RemoteSolutionPresence? presence) =>
        presence switch {
            RemoteSolutionPresence.Optimal => SolutionPresence.Optimal,
            RemoteSolutionPresence.Incumbent => SolutionPresence.Incumbent,
            _ => SolutionPresence.None,
        };

    /// <summary>
    /// 远程证明状态转 Core 证明状态 / Convert a remote proof status to its Core counterpart.
    /// </summary>
    /// <param name="status">远程证明状态，可为空 / Remote proof status, may be null.</param>
    /// <returns>Core 证明状态 / The Core proof status.</returns>
    public static ProofStatus ToCoreProofStatus(RemoteProofStatus? status) => status switch {
        RemoteProofStatus.Verified => ProofStatus.Verified,
        RemoteProofStatus.Claimed => ProofStatus.Claimed,
        _ => ProofStatus.None,
    };

    /// <summary>
    /// 组装线性／二次的 incumbent 报告 / Assemble the linear/quadratic incumbent report.
    /// </summary>
    /// <remarks>
    /// 四条判定：不可行直接报 `ORModelInfeasible`（带上后端给的消息）；没有稳定解时走
    /// <see cref="ToEmptySolutionOutput"/>；稳定解本身不可行同样报 `ORModelInfeasible`；解长度必须等于模型
    /// 变量数——长度不符说明拿到的解不是这个模型的问题，不能截断或补零凑数。
    ///
    /// 目标值与间隙都遵循"**稳定解优先、切片结果回退**"。目标值两侧都没有时**报错而不是填 0**——0 是一个
    /// 合法目标值，拿它当"未知"会让下游误以为目标就是零。
    ///
    /// Four decisions: infeasible reports `ORModelInfeasible` (carrying the backend's message); a
    /// missing stable solution goes to <see cref="ToEmptySolutionOutput"/>; an infeasible stable solution also
    /// reports `ORModelInfeasible`; and the solution length must equal the model's variable count, since a
    /// different length means the solution answers another problem and must not be truncated or zero-padded.
    /// The objective and gap prefer the **validated stable solution** and fall back to the slice result; a
    /// missing objective is an **error rather than 0**, because zero is a legitimate objective and using it for
    /// "unknown" would make downstream read the objective as zero.
    /// </remarks>
    /// <param name="slice">切片结果 / Slice result.</param>
    /// <param name="serialized">稳定解工件，可为空 / Stable-solution artifact, may be null.</param>
    /// <param name="variableCount">模型变量数 / Model variable count.</param>
    /// <returns>报告 / The report.</returns>
    public static Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> ToIncumbentReport(
        SliceResult slice,
        SerializedSolution? serialized,
        int variableCount) {
        ArgumentNullException.ThrowIfNull(slice);

        if (serialized is not null &&
            !RemoteReportMapping.ValidateLinearQuadraticResult(serialized).IsOk) {
            return Invalid<SolveReport<Flt64>>(
                "远程线性／二次结果校验失败 / Remote linear/quadratic result failed validation");
        }
        if (!slice.Feasible) {
            return Failed<SolveReport<Flt64>>(
                ErrorCode.ORModelInfeasible,
                slice.Message ?? "远程线性求解不可行 / Remote linear solve is infeasible.");
        }
        if (serialized is null) {
            return ToEmptySolutionOutput(slice, variableCount);
        }
        if (!serialized.Feasible) {
            return Failed<SolveReport<Flt64>>(
                ErrorCode.ORModelInfeasible,
                serialized.Message ?? "远程线性求解不可行 / Remote linear solve is infeasible.");
        }
        if (serialized.VariableValues.Count != variableCount) {
            return Failed<SolveReport<Flt64>>(
                ErrorCode.ORSolutionInvalid,
                $"远程解变量数 {serialized.VariableValues.Count} 与模型变量数 {variableCount} 不一致 / "
                + $"Remote solution size {serialized.VariableValues.Count} does not match model variable "
                + $"count {variableCount}.");
        }
        double? objective = serialized.ObjectiveValue ?? slice.ObjectiveValue?.ToDouble();
        if (objective is null) {
            return Invalid<SolveReport<Flt64>>(
                "远程线性／二次解缺少目标值 / Remote linear/quadratic solution objective is missing.");
        }
        double? solutionGap = serialized.Gap ?? slice.Gap?.ToDouble();
        return Results.Ok(new SolveReport<Flt64> {
            SchemaVersion = string.IsNullOrWhiteSpace(slice.SchemaVersion)
                ? SolveReport<Flt64>.CurrentSchemaVersion
                : slice.SchemaVersion,
            RunId = slice.RunId is { Length: > 0 } runId ? new SolveRunId(runId) : null,
            ProblemStatus = ToCoreProblemStatus(slice.ProblemStatus),
            TerminationReason = ToCoreTerminationReason(slice.TerminationReason),
            SolutionPresence = ToCoreSolutionPresence(slice.SolutionPresence),
            Solution = new SolveSolution<Flt64>(
                Values: new Solution<Flt64>(
                    serialized.VariableValues.Select(value => new Flt64(value)).ToList()),
                Objective: new Flt64(objective.Value)),
            Proof = new SolveProof(Status: ToCoreProofStatus(slice.ProofStatus)),
            Statistics = new SolveStatistics(
                SolveTime: serialized.ElapsedMs > 0
                    ? TimeSpan.FromMilliseconds(serialized.ElapsedMs)
                    : slice.Elapsed,
                BestBound: ReadStatisticFlt64(serialized.Statistics, "bestBound") ??
                    ReadStatisticFlt64(slice.Statistics, "bestBound"),
                Gap: solutionGap is null ? null : new Flt64(solutionGap.Value)),
        });
    }

    /// <summary>
    /// 组装线性／二次的完整求解报告 / Assemble the complete linear/quadratic solve report.
    /// </summary>
    /// <remarks>
    /// ：只有解存在性非 `NONE` 时才物化 incumbent；否则报告里不带解。这样"报告说没有解"与
    /// "报告里恰好没有解字段"不会被混淆。
    /// The incumbent is materialized only when the presence is not `NONE`; otherwise the
    /// report carries no solution, so "the report says there is no solution" cannot be confused with "the
    /// report happens to have no solution field".
    /// </remarks>
    /// <param name="slice">切片结果 / Slice result.</param>
    /// <param name="serialized">稳定解工件，可为空 / Stable-solution artifact, may be null.</param>
    /// <param name="variableCount">模型变量数 / Model variable count.</param>
    /// <param name="modelTypes">模型类型能力 / Model-type capabilities.</param>
    /// <returns>报告 / The report.</returns>
    public static Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> ToSolveReport(
        SliceResult slice,
        SerializedSolution? serialized,
        int variableCount,
        IReadOnlySet<SolverModelType> modelTypes) {
        ArgumentNullException.ThrowIfNull(slice);

        SolveReport<Flt64>? output = null;
        if (slice.SolutionPresence != RemoteSolutionPresence.None) {
            Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> incumbent =
                ToIncumbentReport(slice, serialized, variableCount);
            if (!incumbent.IsOk) {
                return incumbent;
            }
            output = incumbent.Value;
        }
        return ToRemoteSolveReport(slice, output, modelTypes);
    }

    /// <summary>
    /// 无稳定解时的空解报告 / Empty-solution report when no artifact is available.
    /// </summary>
    /// <remarks>
    /// ：**只有变量数为 0 才允许空解**。变量数不为 0 却说没有可读工件，说明结果不完整，必须报错
    /// 而不是返回一个"空解但成功"的报告。
    /// An empty solution is allowed **only when the model has zero variables**. A non-zero
    /// variable count with no readable artifact means the result is incomplete and must fail instead of
    /// returning an "empty but successful" report.
    /// </remarks>
    /// <param name="slice">切片结果 / Slice result.</param>
    /// <param name="variableCount">模型变量数 / Model variable count.</param>
    /// <returns>报告 / The report.</returns>
    public static Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> ToEmptySolutionOutput(
        SliceResult slice,
        int variableCount) {
        ArgumentNullException.ThrowIfNull(slice);
        if (variableCount != 0) {
            return Invalid<SolveReport<Flt64>>(
                "远程求解结果不包含可读的 SerializedSolution resultRef / "
                + "Remote solve result does not contain a readable SerializedSolution resultRef.");
        }
        double? objective = slice.ObjectiveValue?.ToDouble();
        if (objective is null) {
            return Invalid<SolveReport<Flt64>>(
                "远程线性／二次解缺少目标值 / Remote linear/quadratic solution objective is missing.");
        }
        return Results.Ok(new SolveReport<Flt64> {
            SchemaVersion = string.IsNullOrWhiteSpace(slice.SchemaVersion)
                ? SolveReport<Flt64>.CurrentSchemaVersion
                : slice.SchemaVersion,
            RunId = slice.RunId is { Length: > 0 } runId ? new SolveRunId(runId) : null,
            ProblemStatus = ToCoreProblemStatus(slice.ProblemStatus),
            TerminationReason = ToCoreTerminationReason(slice.TerminationReason),
            SolutionPresence = ToCoreSolutionPresence(slice.SolutionPresence),
            Solution = new SolveSolution<Flt64>(
                Values: new Solution<Flt64>(Array.Empty<Flt64>()),
                Objective: new Flt64(objective.Value)),
            Proof = new SolveProof(Status: ToCoreProofStatus(slice.ProofStatus)),
            Statistics = new SolveStatistics(
                SolveTime: slice.Elapsed,
                BestBound: ReadStatisticFlt64(slice.Statistics, "bestBound"),
                Gap: slice.Gap),
        });
    }

    private static Result<T, ErrorCode, Error<ErrorCode>> Invalid<T>(string message) =>
        Results.Failed<T>(new Err<ErrorCode>(ErrorCode.ORSolutionInvalid, message));

    private static Result<T, ErrorCode, Error<ErrorCode>> Failed<T>(
        ErrorCode code,
        string message) =>
        Results.Failed<T>(new Err<ErrorCode>(code, message));

    private static Flt64? ReadStatisticFlt64(
        IReadOnlyDictionary<string, string>? statistics,
        string key) =>
        statistics is not null &&
        statistics.TryGetValue(key, out string? raw) &&
        double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
            ? new Flt64(parsed)
            : null;

    /// <summary>
    /// 组装 CP 求解报告 / Assemble the CP solve report.
    /// </summary>
    /// <remarks>
    /// 目标值类型是 <c>Int64</c>，不走浮点：CP 的目标是精确整数，不能经过浮点。
    /// 与线性／二次版本的关键差别是诊断：CP 的诊断里带不可行证据，解析失败时**上抛而不是吞掉**，否则下游会
    /// 把"解析失败"读成"没有证据"。
    ///
    /// The objective type is <c>Int64</c>, since a CP
    /// objective is an exact integer and must not pass through floating point. The key difference from the
    /// linear/quadratic variants is diagnostics: CP diagnostics carry infeasibility evidence and a parse
    /// failure is **propagated rather than swallowed**, otherwise downstream would read "parse failed" as "no
    /// evidence".
    /// </remarks>
    /// <param name="slice">切片结果 / Slice result.</param>
    /// <param name="solution">稳定解，可为空 / Stable solution, may be null.</param>
    /// <returns>CP 报告 / CP report.</returns>
    public static Result<
        Fuookami.Ospf.Core.Solver.Report.SolveReport<Fuookami.Ospf.Math.Algebra.Number.Int64>,
        ErrorCode,
        Error<ErrorCode>> ToCpReport(
        SliceResult slice,
        ConstraintProgrammingSolution? solution) {
        ArgumentNullException.ThrowIfNull(slice);

        Result<
            Fuookami.Ospf.Core.Solver.Report.SolveDiagnostics<Fuookami.Ospf.Math.Algebra.Number.Int64>,
            ErrorCode,
            Error<ErrorCode>> diagnostics =
            RemoteReportMapping.ToCpDiagnostics(slice.Diagnostics);
        if (!diagnostics.IsOk) {
            return Results.Failed<
                Fuookami.Ospf.Core.Solver.Report.SolveReport<Fuookami.Ospf.Math.Algebra.Number.Int64>>(
                new Err<ErrorCode>(
                    ErrorCode.ORSolutionInvalid,
                    "远程 CP 诊断解析失败 / Remote CP diagnostics cannot be parsed"));
        }

        Fuookami.Ospf.Core.Solver.Report.SolveSolution<Fuookami.Ospf.Math.Algebra.Number.Int64>?
            reportSolution = null;
        if (solution is not null) {
            // ：值按稳定标识排序，保证同一份解得到同一份报告。
            // Values are ordered by stable identifier so one solution yields one report.
            var ordered = solution.Values
                .OrderBy(entry => entry.Key.Value, StringComparer.Ordinal)
                .Select(entry => entry.Value)
                .ToList();
            reportSolution = new Fuookami.Ospf.Core.Solver.Report.SolveSolution<
                Fuookami.Ospf.Math.Algebra.Number.Int64>(
                Values: new Solution<Fuookami.Ospf.Math.Algebra.Number.Int64>(ordered),
                Objective: slice.ObjectiveValueInt64 is { } exact
                    ? new Fuookami.Ospf.Math.Algebra.Number.Int64(exact)
                    : null);
        }

        return Results.Ok(new Fuookami.Ospf.Core.Solver.Report.SolveReport<
            Fuookami.Ospf.Math.Algebra.Number.Int64> {
            SchemaVersion = string.IsNullOrWhiteSpace(slice.SchemaVersion)
                ? "1.0"
                : slice.SchemaVersion,
            RunId = slice.RunId is { Length: > 0 } runId ? new SolveRunId(runId) : null,
            ProblemStatus = ToCoreProblemStatus(slice.ProblemStatus),
            TerminationReason = ToCoreTerminationReason(slice.TerminationReason),
            SolutionPresence = ToCoreSolutionPresence(slice.SolutionPresence),
            Solution = reportSolution,
            Proof = new SolveProof(
                Status: ToCoreProofStatus(slice.ProofStatus),
                Kind: ReadProvenance(slice, "proof.kind")),
            Statistics = new SolveStatistics(
                SolveTime: slice.Elapsed,
                Iterations: ReadStatisticULong(slice, "iterations"),
                Nodes: ReadStatisticULong(slice, "nodes"),
                BestBound: ReadStatisticFlt64(slice, "bestBound"),
                Gap: slice.Gap),
            Diagnostics = diagnostics.Value,
            Provenance = BuildProvenance(
                slice, new HashSet<SolverModelType> { SolverModelType.Cp }),
            Fingerprints = BuildFingerprints(slice),
        });
    }

    private static SolverProvenance? BuildProvenance(
        SliceResult slice,
        IReadOnlySet<SolverModelType> modelTypes) {
        if (slice.Provenance is not { Count: > 0 } provenance) {
            // ：后端没给来源信息时不编造一个来源。
            // No provenance is invented when the backend supplied none.
            return null;
        }
        var effectiveParameters = new Dictionary<string, string>(StringComparer.Ordinal);
        var environmentSummary = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> entry in provenance) {
            if (entry.Key.StartsWith("parameter.", StringComparison.Ordinal)) {
                effectiveParameters[entry.Key["parameter.".Length..]] = entry.Value;
            }
            else if (entry.Key.StartsWith("environment.", StringComparison.Ordinal)) {
                environmentSummary[entry.Key["environment.".Length..]] = entry.Value;
            }
        }
        return new SolverProvenance(
            Descriptor: new SolverDescriptor(
                SolverId: ReadProvenance(slice, "solverId") ?? "remote",
                BackendName: ReadProvenance(slice, "backend") ?? "remote",
                Capabilities: new SolverCapabilities(ModelTypes: modelTypes),
                BackendVersion: ReadProvenance(slice, "backendVersion"),
                PluginVersion: ReadProvenance(slice, "pluginVersion")),
            NativeVersion: ReadProvenance(slice, "nativeVersion"),
            EffectiveParameters: effectiveParameters,
            ThreadCount: int.TryParse(
                ReadProvenance(slice, "threads"),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int threads)
                ? threads
                : null,
            RandomSeed: long.TryParse(
                ReadProvenance(slice, "randomSeed"),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out long seed)
                ? seed
                : null,
            Deterministic: bool.TryParse(ReadProvenance(slice, "deterministic"), out bool deterministic)
                ? deterministic
                : null,
            EnvironmentSummary: environmentSummary);
    }

    private static SolveFingerprints BuildFingerprints(SliceResult slice) {
        static AuditFingerprint? One(
            SliceResult source,
            string key) => source.Fingerprints is { } fingerprints &&
            fingerprints.TryGetValue(key, out string? value)
                ? new AuditFingerprint(
                    SchemaVersion: source.FingerprintSchemas is { } schemas &&
                        schemas.TryGetValue(key, out string? schema)
                            ? schema
                            : source.SchemaVersion ?? "1.0",
                    Algorithm: "SHA-256",
                    Value: value)
                : null;
        return new SolveFingerprints(
            Model: One(slice, "model"),
            Configuration: One(slice, "configuration"),
            Solver: One(slice, "solver"));
    }

    private static string? ReadProvenance(SliceResult slice, string key) =>
        slice.Provenance is { } provenance && provenance.TryGetValue(key, out string? value)
            ? value
            : null;

    private static ulong? ReadStatisticULong(SliceResult slice, string key) =>
        slice.Statistics is { } statistics &&
        statistics.TryGetValue(key, out string? raw) &&
        ulong.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong parsed)
            ? parsed
            : null;

    private static Flt64? ReadStatisticFlt64(SliceResult slice, string key) =>
        slice.Statistics is { } statistics &&
        statistics.TryGetValue(key, out string? raw) &&
        double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
            ? new Flt64(parsed)
            : null;
}
