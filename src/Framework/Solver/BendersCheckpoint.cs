#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Framework.Solver;

/// <summary>
/// 领域为 Benders 主问题割提供的版本化可移植序列化器。
/// Domain-owned versioned serializer for portable Benders master cuts.
/// </summary>
public interface IBendersCutSerializationSpi {
    /// <summary>领域割 payload 的 schema 版本 / Domain cut-payload schema version.</summary>
    string SchemaVersion { get; }

    /// <summary>编码一个已接受的割 / Encode one accepted cut.</summary>
    /// <param name="cut">已接受的 Benders 割 / Accepted Benders cut.</param>
    /// <returns>可移植割或结构化错误 / Portable cut or a structured error.</returns>
    Result<PortableConstraintProgrammingCut, ErrorCode, Error<ErrorCode>> Encode(
        BendersMasterCut cut);

    /// <summary>解码并校验一个割 / Decode and validate one cut.</summary>
    /// <param name="cut">可移植割 / Portable cut.</param>
    /// <returns>领域割或结构化错误 / Domain cut or a structured error.</returns>
    Result<BendersMasterCut, ErrorCode, Error<ErrorCode>> Decode(
        PortableConstraintProgrammingCut cut);
}

/// <summary>
/// Kotlin SPI 名称的兼容别名 / Compatibility alias for the Kotlin SPI name.
/// </summary>
public interface BendersCutSerializationSpi : IBendersCutSerializationSpi {
}

/// <summary>
/// 内存 Benders 恢复状态；只保存可复验的割和证据，不保存后端句柄。
/// In-memory Benders resume state containing only revalidated cuts and evidence.
/// </summary>
public sealed class BendersResumeState {
    /// <summary>创建恢复状态 / Create a resume state.</summary>
    /// <param name="iteration">下一次要执行的迭代号 / Next iteration to execute.</param>
    /// <param name="cuts">已独立解码的割 / Independently decoded cuts.</param>
    /// <param name="masterIncumbent">主问题 incumbent / Master incumbent.</param>
    /// <param name="masterBestBound">主问题最佳界 / Master best bound.</param>
    /// <param name="trace">仅用于审计的历史轨迹 / Historical trace retained for audit only.</param>
    /// <param name="assumptions">已复验的 assumption 标识 / Revalidated assumption identifiers.</param>
    /// <param name="fixedBindings">已复验的 CP 固定绑定 / Revalidated CP fixed bindings.</param>
    /// <param name="conflicts">已复验的冲突证据 / Revalidated conflict evidence.</param>
    /// <param name="convergenceVerified">历史收敛标记，不作为证明 / Historical convergence marker; never trusted as proof.</param>
    /// <param name="masterFingerprint">主问题模型指纹 / Master model fingerprint.</param>
    /// <param name="subproblemModelFingerprint">CP 子问题模型指纹 / CP subproblem model fingerprint.</param>
    public BendersResumeState(
        int iteration,
        IEnumerable<BendersMasterCut>? cuts = null,
        string? masterIncumbent = null,
        string? masterBestBound = null,
        IEnumerable<string>? trace = null,
        IEnumerable<string>? assumptions = null,
        IReadOnlyDictionary<string, long>? fixedBindings = null,
        IEnumerable<PortableConstraintProgrammingConflict>? conflicts = null,
        bool convergenceVerified = false,
        string? masterFingerprint = null,
        string? subproblemModelFingerprint = null) {
        Iteration = iteration;
        Cuts = Array.AsReadOnly((cuts ?? Array.Empty<BendersMasterCut>()).ToArray());
        MasterIncumbent = masterIncumbent;
        MasterBestBound = masterBestBound;
        Trace = Array.AsReadOnly((trace ?? Array.Empty<string>()).ToArray());
        Assumptions = Array.AsReadOnly(
            (assumptions ?? Array.Empty<string>())
                .Distinct(StringComparer.Ordinal)
                .ToArray());
        FixedBindings = new ReadOnlyDictionary<string, long>(
            new Dictionary<string, long>(
                fixedBindings ?? new Dictionary<string, long>(),
                StringComparer.Ordinal));
        Conflicts = Array.AsReadOnly(
            (conflicts ?? Array.Empty<PortableConstraintProgrammingConflict>()).ToArray());
        ConvergenceVerified = convergenceVerified;
        MasterFingerprint = masterFingerprint;
        SubproblemModelFingerprint = subproblemModelFingerprint;
    }

    /// <summary>下一次要执行的迭代号 / Next iteration to execute.</summary>
    public int Iteration { get; }

    /// <summary>已独立解码的割 / Independently decoded cuts.</summary>
    public IReadOnlyList<BendersMasterCut> Cuts { get; }

    /// <summary>主问题 incumbent / Master incumbent.</summary>
    public string? MasterIncumbent { get; }

    /// <summary>主问题最佳界 / Master best bound.</summary>
    public string? MasterBestBound { get; }

    /// <summary>仅用于审计的历史轨迹 / Historical trace retained for audit only.</summary>
    public IReadOnlyList<string> Trace { get; }

    /// <summary>已复验的 assumption 标识 / Revalidated assumption identifiers.</summary>
    public IReadOnlyList<string> Assumptions { get; }

    /// <summary>已复验的 CP 固定绑定 / Revalidated CP fixed bindings.</summary>
    public IReadOnlyDictionary<string, long> FixedBindings { get; }

    /// <summary>已复验的冲突证据 / Revalidated conflict evidence.</summary>
    public IReadOnlyList<PortableConstraintProgrammingConflict> Conflicts { get; }

    /// <summary>历史收敛标记，不作为证明 / Historical convergence marker; never trusted as proof.</summary>
    public bool ConvergenceVerified { get; }

    /// <summary>主问题模型指纹 / Master model fingerprint.</summary>
    public string? MasterFingerprint { get; }

    /// <summary>CP 子问题模型指纹 / CP subproblem model fingerprint.</summary>
    public string? SubproblemModelFingerprint { get; }
}

/// <summary>
/// 主问题恢复适配器应用 checkpoint 后返回的复验证据。
/// Evidence returned after a master-resume adapter applies persisted state.
/// </summary>
public sealed class BendersMasterResumeEvidence {
    /// <summary>创建恢复证据 / Create resume evidence.</summary>
    /// <param name="masterFingerprint">实际使用的主问题指纹 / Fingerprint of the master actually used.</param>
    /// <param name="incumbent">已应用的主问题 incumbent / Applied master incumbent.</param>
    /// <param name="bestBound">已应用的主问题最佳界 / Applied master best bound.</param>
    /// <param name="assumptions">已应用的 assumption 标识 / Applied assumption identifiers.</param>
    /// <param name="fixedBindings">已应用的固定绑定 / Applied fixed bindings.</param>
    /// <param name="conflicts">已应用的冲突证据 / Applied conflict evidence.</param>
    /// <param name="convergenceVerified">是否应用了收敛标记 / Whether the convergence marker was applied.</param>
    public BendersMasterResumeEvidence(
        string masterFingerprint,
        string? incumbent = null,
        string? bestBound = null,
        IEnumerable<string>? assumptions = null,
        IReadOnlyDictionary<string, long>? fixedBindings = null,
        IEnumerable<PortableConstraintProgrammingConflict>? conflicts = null,
        bool convergenceVerified = false) {
        MasterFingerprint = masterFingerprint;
        Incumbent = incumbent;
        BestBound = bestBound;
        Assumptions = Array.AsReadOnly(
            (assumptions ?? Array.Empty<string>())
                .Distinct(StringComparer.Ordinal)
                .ToArray());
        FixedBindings = new ReadOnlyDictionary<string, long>(
            new Dictionary<string, long>(
                fixedBindings ?? new Dictionary<string, long>(),
                StringComparer.Ordinal));
        Conflicts = Array.AsReadOnly(
            (conflicts ?? Array.Empty<PortableConstraintProgrammingConflict>()).ToArray());
        ConvergenceVerified = convergenceVerified;
    }

    /// <summary>实际使用的主问题指纹 / Fingerprint of the master actually used.</summary>
    public string MasterFingerprint { get; }

    /// <summary>已应用的主问题 incumbent / Applied master incumbent.</summary>
    public string? Incumbent { get; }

    /// <summary>已应用的主问题最佳界 / Applied master best bound.</summary>
    public string? BestBound { get; }

    /// <summary>已应用的 assumption 标识 / Applied assumption identifiers.</summary>
    public IReadOnlyList<string> Assumptions { get; }

    /// <summary>已应用的固定绑定 / Applied fixed bindings.</summary>
    public IReadOnlyDictionary<string, long> FixedBindings { get; }

    /// <summary>已应用的冲突证据 / Applied conflict evidence.</summary>
    public IReadOnlyList<PortableConstraintProgrammingConflict> Conflicts { get; }

    /// <summary>是否应用了收敛标记 / Whether the convergence marker was applied.</summary>
    public bool ConvergenceVerified { get; }
}

/// <summary>
/// 在首次恢复求解前应用持久化主问题状态。
/// Applies persisted master state before the first resumed solve.
/// </summary>
/// <param name="master">可变主问题 / Mutable master.</param>
/// <param name="state">已复验的可移植恢复状态 / Validated portable resume state.</param>
/// <returns>应用后的复验证据或结构化错误 / Applied evidence or a structured error.</returns>
public delegate Result<BendersMasterResumeEvidence, ErrorCode, Error<ErrorCode>>
    BendersMasterResumeApplier(
        LinearMetaModel<Flt64> master,
        BendersResumeState state);

/// <summary>
/// 可移植 Benders checkpoint 的编码、捕获、恢复和 trace codec。
/// Codec for portable Benders checkpoint encoding, capture, restore, and trace data.
/// </summary>
public static class BendersCheckpointCodec {
    /// <summary>当前独立 Benders 文档 schema / Current standalone Benders document schema.</summary>
    public const string CurrentSchema = "1.0";

    /// <summary>当前 CP envelope schema / Current CP envelope schema.</summary>
    public const string CoreEnvelopeSchema = "3.0";

    private const string TraceSchema = "1.0";
    private const string TracePrefix = "v1:";

    private static readonly JsonSerializerOptions JsonOptions = new() {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = false,
    };

    /// <summary>
    /// 从报告捕获可移植 Benders 状态。
    /// Capture portable Benders state from a report.
    /// </summary>
    /// <param name="report">Benders 报告 / Benders report.</param>
    /// <param name="serializer">领域割序列化器 / Domain cut serializer.</param>
    /// <param name="subproblemSnapshot">可选 CP snapshot，用于绑定子问题身份 / Optional CP snapshot used for identity.</param>
    /// <param name="masterFingerprint">可选主问题指纹 / Optional master fingerprint.</param>
    /// <returns>可移植状态或结构化错误 / Portable state or a structured error.</returns>
    public static Result<
        PortableConstraintProgrammingBendersState,
        ErrorCode,
        Error<ErrorCode>> Capture(
        LogicBasedBendersReport report,
        IBendersCutSerializationSpi serializer,
        ConstraintProgrammingModelSnapshot? subproblemSnapshot = null,
        string? masterFingerprint = null) {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(serializer);
        if (string.IsNullOrWhiteSpace(serializer.SchemaVersion)) {
            return Failure<PortableConstraintProgrammingBendersState>(
                ErrorCode.IllegalArgument,
                "Benders cut schema 不能为空 / Benders cut schema must not be blank");
        }
        if ((report.MasterOutput is not null || report.Assignment is not null) &&
            string.IsNullOrWhiteSpace(masterFingerprint)) {
            return Failure<PortableConstraintProgrammingBendersState>(
                ErrorCode.IllegalArgument,
                "Benders 主问题状态必须提供模型指纹 / Benders master state requires a model fingerprint");
        }

        string? subproblemFingerprint = null;
        if (subproblemSnapshot is not null) {
            Result<string, ErrorCode, Error<ErrorCode>> fingerprint =
                ComputeSubproblemFingerprint(subproblemSnapshot);
            if (fingerprint is not Ok<string, ErrorCode, Error<ErrorCode>> fingerprintOk) {
                return Propagate<PortableConstraintProgrammingBendersState, string>(
                    fingerprint);
            }
            subproblemFingerprint = fingerprintOk.Value;
        }

        var encodedCuts = new List<PortableConstraintProgrammingCut>();
        var cutIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (BendersMasterCut cut in report.Cuts) {
            Result<PortableConstraintProgrammingCut, ErrorCode, Error<ErrorCode>> encoded =
                serializer.Encode(cut);
            if (encoded is not Ok<
                    PortableConstraintProgrammingCut,
                    ErrorCode,
                    Error<ErrorCode>> encodedOk) {
                return Propagate<PortableConstraintProgrammingBendersState,
                    PortableConstraintProgrammingCut>(encoded);
            }
            PortableConstraintProgrammingCut portable = NormalizeCut(encodedOk.Value);
            if (string.IsNullOrWhiteSpace(portable.Id) ||
                !string.Equals(
                    portable.SchemaVersion,
                    serializer.SchemaVersion,
                    StringComparison.Ordinal)) {
                return Failure<PortableConstraintProgrammingBendersState>(
                    ErrorCode.IllegalArgument,
                    "Benders checkpoint cut 身份或 schema 无效 / " +
                    "Benders checkpoint cut identity or schema is invalid");
            }
            if (!cutIds.Add(portable.Id)) {
                return Failure<PortableConstraintProgrammingBendersState>(
                    ErrorCode.IllegalArgument,
                    "Benders checkpoint 存在重复 cut ID / " +
                    "Benders checkpoint contains duplicate cut IDs");
            }
            encodedCuts.Add(portable);
        }

        var encodedTraces = new List<string>();
        foreach (BendersIterationTrace trace in report.Iterations) {
            Result<string, ErrorCode, Error<ErrorCode>> encodedTrace = EncodeTrace(trace);
            if (encodedTrace is not Ok<string, ErrorCode, Error<ErrorCode>> traceOk) {
                return Propagate<PortableConstraintProgrammingBendersState, string>(
                    encodedTrace);
            }
            encodedTraces.Add(traceOk.Value);
        }

        long iteration = report.Iterations.Count == 0
            ? 0L
            : (long)report.Iterations[^1].Iteration + 1L;
        var state = new PortableConstraintProgrammingBendersState(
            Iteration: iteration,
            MasterIncumbent: report.MasterOutput?.Solution?.Objective?.ToString(),
            MasterBestBound: report.MasterOutput?.Statistics.BestBound?.ToString(),
            Cuts: encodedCuts,
            Trace: encodedTraces,
            Assumptions: report.Assignment?.Assumptions
                .Select(literal => literal.VariableId?.Value)
                .Where(value => value is not null)
                .Select(value => value!)
                .Distinct(StringComparer.Ordinal)
                .ToList() ?? new List<string>(),
            FixedBindings: report.Assignment?.FixedValues
                .ToDictionary(
                    pair => pair.Key.Value,
                    pair => pair.Value.ToLong(),
                    StringComparer.Ordinal) ?? new Dictionary<string, long>(),
            Conflicts: report.SubproblemResult is InfeasibleSubproblemResult infeasible &&
                infeasible.Conflict is not null
                ? new[] { ToPortableConflict(infeasible.Conflict) }
                : Array.Empty<PortableConstraintProgrammingConflict>(),
            ConvergenceVerified: report.Proof.Status == ProofStatus.Verified,
            MasterFingerprint: masterFingerprint,
            SubproblemModelFingerprint: subproblemFingerprint);
        string? validation = ValidatePortableState(state);
        return validation is null
            ? Results.Ok(state)
            : Failure<PortableConstraintProgrammingBendersState>(
                ErrorCode.IllegalArgument,
                validation);
    }

    /// <summary>捕获并编码报告 / Capture and encode a report in one operation.</summary>
    /// <param name="report">Benders 报告 / Benders report.</param>
    /// <param name="serializer">领域割序列化器 / Domain cut serializer.</param>
    /// <param name="subproblemSnapshot">可选 CP snapshot / Optional CP snapshot.</param>
    /// <param name="masterFingerprint">可选主问题指纹 / Optional master fingerprint.</param>
    /// <returns>带完整性摘要的文档或结构化错误 / Integrity-protected document or a structured error.</returns>
    public static Result<string, ErrorCode, Error<ErrorCode>> CaptureEncoded(
        LogicBasedBendersReport report,
        IBendersCutSerializationSpi serializer,
        ConstraintProgrammingModelSnapshot? subproblemSnapshot = null,
        string? masterFingerprint = null) {
        Result<
            PortableConstraintProgrammingBendersState,
            ErrorCode,
            Error<ErrorCode>> captured = Capture(
                report: report,
                serializer: serializer,
                subproblemSnapshot: subproblemSnapshot,
                masterFingerprint: masterFingerprint);
        return captured is Ok<
            PortableConstraintProgrammingBendersState,
            ErrorCode,
            Error<ErrorCode>> ok
            ? Encode(ok.Value)
            : Propagate<string, PortableConstraintProgrammingBendersState>(captured);
    }

    /// <summary>
    /// 将报告捕获进 Core schema 3.0 的 CP envelope。
    /// Capture the report into the Core schema 3.0 CP envelope.
    /// </summary>
    /// <param name="report">Benders 报告 / Benders report.</param>
    /// <param name="serializer">领域割序列化器 / Domain cut serializer.</param>
    /// <param name="snapshot">CP snapshot / CP snapshot.</param>
    /// <param name="descriptor">求解器描述符 / Solver descriptor.</param>
    /// <param name="checkpointId">Checkpoint 标识 / Checkpoint identifier.</param>
    /// <param name="createdAtEpochMs">创建时间 / Creation time in epoch milliseconds.</param>
    /// <param name="masterFingerprint">可选主问题指纹 / Optional master fingerprint.</param>
    /// <returns>Core envelope 或结构化错误 / Core envelope or a structured error.</returns>
    public static Result<
        ConstraintProgrammingCheckpointEnvelope,
        ErrorCode,
        Error<ErrorCode>> CaptureEnvelope(
        LogicBasedBendersReport report,
        IBendersCutSerializationSpi serializer,
        ConstraintProgrammingModelSnapshot snapshot,
        SolverDescriptor descriptor,
        string checkpointId,
        long createdAtEpochMs,
        string? masterFingerprint = null) {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(descriptor);
        Result<
            PortableConstraintProgrammingBendersState,
            ErrorCode,
            Error<ErrorCode>> captured = Capture(
                report: report,
                serializer: serializer,
                subproblemSnapshot: snapshot,
                masterFingerprint: masterFingerprint);
        if (captured is not Ok<
                PortableConstraintProgrammingBendersState,
                ErrorCode,
                Error<ErrorCode>> capturedOk) {
            return Propagate<ConstraintProgrammingCheckpointEnvelope,
                PortableConstraintProgrammingBendersState>(captured);
        }
        return ConstraintProgrammingCheckpointCodec.Capture(
            snapshot: snapshot,
            descriptor: descriptor,
            checkpointId: checkpointId,
            createdAtEpochMs: createdAtEpochMs,
            benders: capturedOk.Value);
    }

    /// <summary>编码可移植 Benders 状态 / Encode portable Benders state.</summary>
    /// <param name="state">可移植状态 / Portable state.</param>
    /// <returns>规范 JSON 或结构化错误 / Canonical JSON or a structured error.</returns>
    public static Result<string, ErrorCode, Error<ErrorCode>> Encode(
        PortableConstraintProgrammingBendersState state) {
        ArgumentNullException.ThrowIfNull(state);
        string? validation = ValidatePortableState(state);
        if (validation is not null) {
            return Failure<string>(ErrorCode.IllegalArgument, validation);
        }
        try {
            PortableConstraintProgrammingBendersState normalized = NormalizeState(state);
            var document = new BendersCheckpointDocument(
                SchemaVersion: CurrentSchema,
                State: normalized,
                IntegritySha256: string.Empty);
            string digest = Digest(document);
            return Results.Ok(JsonSerializer.Serialize(
                document with { IntegritySha256 = digest },
                JsonOptions));
        }
        catch (Exception error) {
            return Failure<string>(
                ErrorCode.Other,
                $"Benders checkpoint 编码失败：{error.Message} / " +
                $"Benders checkpoint encoding failed: {error.Message}");
        }
    }

    /// <summary>解码并校验可移植 Benders 文档 / Decode and verify a portable Benders document.</summary>
    /// <param name="encoded">JSON 文档 / JSON document.</param>
    /// <returns>可移植状态或结构化错误 / Portable state or a structured error.</returns>
    public static Result<
        PortableConstraintProgrammingBendersState,
        ErrorCode,
        Error<ErrorCode>> Decode(string encoded) {
        ArgumentNullException.ThrowIfNull(encoded);
        try {
            BendersCheckpointDocument? document =
                JsonSerializer.Deserialize<BendersCheckpointDocument>(
                    encoded,
                    JsonOptions);
            if (document is null) {
                return Failure<PortableConstraintProgrammingBendersState>(
                    ErrorCode.IllegalArgument,
                    "Benders checkpoint JSON 为空 / Benders checkpoint JSON is empty");
            }
            if (!string.Equals(
                    document.SchemaVersion,
                    CurrentSchema,
                    StringComparison.Ordinal)) {
                return Failure<PortableConstraintProgrammingBendersState>(
                    ErrorCode.Other,
                    $"不支持的 Benders checkpoint schema：{document.SchemaVersion} / " +
                    $"Unsupported Benders checkpoint schema: {document.SchemaVersion}");
            }

            PortableConstraintProgrammingBendersState current =
                NormalizeState(document.State);
            var currentDocument = new BendersCheckpointDocument(
                SchemaVersion: CurrentSchema,
                State: current,
                IntegritySha256: string.Empty);
            bool currentDigest = !string.IsNullOrWhiteSpace(document.IntegritySha256) &&
                string.Equals(
                    document.IntegritySha256,
                    Digest(currentDocument),
                    StringComparison.Ordinal);
            bool legacy = false;
            PortableConstraintProgrammingBendersState state = current;
            if (!currentDigest) {
                LegacyBendersCheckpointDocument? legacyDocument =
                    JsonSerializer.Deserialize<LegacyBendersCheckpointDocument>(
                        encoded,
                        JsonOptions);
                if (legacyDocument is null ||
                    !string.Equals(
                        legacyDocument.SchemaVersion,
                        CurrentSchema,
                        StringComparison.Ordinal) ||
                    string.IsNullOrWhiteSpace(legacyDocument.IntegritySha256) ||
                    !string.Equals(
                        legacyDocument.IntegritySha256,
                        LegacyDigest(legacyDocument with {
                            IntegritySha256 = string.Empty,
                        }),
                        StringComparison.Ordinal)) {
                    return Failure<PortableConstraintProgrammingBendersState>(
                        ErrorCode.IllegalArgument,
                        "Benders checkpoint 完整性摘要不匹配 / " +
                        "Benders checkpoint integrity digest mismatch");
                }
                state = ToPortableState(legacyDocument.State);
                legacy = true;
            }

            string? validation = ValidatePortableState(
                state,
                allowLegacyUnboundMasterFingerprint: legacy);
            return validation is null
                ? Results.Ok(state)
                : Failure<PortableConstraintProgrammingBendersState>(
                    ErrorCode.IllegalArgument,
                    validation);
        }
        catch (Exception error) {
            return Failure<PortableConstraintProgrammingBendersState>(
                ErrorCode.IllegalArgument,
                $"Benders checkpoint 解码失败：{error.Message} / " +
                $"Benders checkpoint decoding failed: {error.Message}");
        }
    }

    /// <summary>解码、复验并恢复 Benders 状态 / Decode, validate, and restore Benders state.</summary>
    /// <param name="encoded">JSON 文档 / JSON document.</param>
    /// <param name="serializer">领域割序列化器 / Domain cut serializer.</param>
    /// <param name="proofMode">当前证明模式 / Active proof mode.</param>
    /// <param name="expectedSubproblemSnapshot">当前 CP snapshot / Current CP snapshot.</param>
    /// <returns>恢复状态或结构化错误 / Resume state or a structured error.</returns>
    public static Result<BendersResumeState, ErrorCode, Error<ErrorCode>> RestoreEncoded(
        string encoded,
        IBendersCutSerializationSpi serializer,
        BendersProofMode proofMode = BendersProofMode.Exact,
        ConstraintProgrammingModelSnapshot? expectedSubproblemSnapshot = null) {
        Result<
            PortableConstraintProgrammingBendersState,
            ErrorCode,
            Error<ErrorCode>> decoded = Decode(encoded);
        return decoded is Ok<
            PortableConstraintProgrammingBendersState,
            ErrorCode,
            Error<ErrorCode>> ok
            ? Restore(
                state: ok.Value,
                serializer: serializer,
                proofMode: proofMode,
                expectedSubproblemSnapshot: expectedSubproblemSnapshot)
            : Propagate<BendersResumeState, PortableConstraintProgrammingBendersState>(
                decoded);
    }

    /// <summary>复验并恢复可移植 Benders 状态 / Validate and restore portable Benders state.</summary>
    /// <param name="state">可移植状态 / Portable state.</param>
    /// <param name="serializer">领域割序列化器 / Domain cut serializer.</param>
    /// <param name="proofMode">当前证明模式 / Active proof mode.</param>
    /// <param name="expectedSubproblemSnapshot">当前 CP snapshot / Current CP snapshot.</param>
    /// <returns>恢复状态或结构化错误 / Resume state or a structured error.</returns>
    public static Result<BendersResumeState, ErrorCode, Error<ErrorCode>> Restore(
        PortableConstraintProgrammingBendersState state,
        IBendersCutSerializationSpi serializer,
        BendersProofMode proofMode = BendersProofMode.Exact,
        ConstraintProgrammingModelSnapshot? expectedSubproblemSnapshot = null) {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(serializer);
        if (string.IsNullOrWhiteSpace(serializer.SchemaVersion)) {
            return Failure<BendersResumeState>(
                ErrorCode.IllegalArgument,
                "Benders cut schema 不能为空 / Benders cut schema must not be blank");
        }
        string? validation = ValidatePortableState(state);
        if (validation is not null) {
            return Failure<BendersResumeState>(ErrorCode.IllegalArgument, validation);
        }
        if (state.Iteration > int.MaxValue) {
            return Failure<BendersResumeState>(
                ErrorCode.IllegalArgument,
                "Benders checkpoint iteration 超出范围 / " +
                "Benders checkpoint iteration exceeds Int range");
        }

        var cuts = new List<BendersMasterCut>();
        var cutIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (PortableConstraintProgrammingCut portable in
            state.Cuts ?? Array.Empty<PortableConstraintProgrammingCut>()) {
            if (portable is null ||
                string.IsNullOrWhiteSpace(portable.Id) ||
                !string.Equals(
                    portable.SchemaVersion,
                    serializer.SchemaVersion,
                    StringComparison.Ordinal) ||
                !cutIds.Add(portable.Id)) {
                return Failure<BendersResumeState>(
                    ErrorCode.IllegalArgument,
                    "Benders checkpoint cut schema 或 ID 无效 / " +
                    "Benders checkpoint cut schema or ID is invalid");
            }
            Result<BendersMasterCut, ErrorCode, Error<ErrorCode>> decoded =
                serializer.Decode(portable);
            if (decoded is not Ok<BendersMasterCut, ErrorCode, Error<ErrorCode>> decodedOk) {
                return Propagate<BendersResumeState, BendersMasterCut>(decoded);
            }
            BendersMasterCut cut = decodedOk.Value;
            if (proofMode == BendersProofMode.Exact &&
                (cut.Validity != BendersCutValidity.Global ||
                    cut.ProofStatus != ProofStatus.Verified)) {
                return Failure<BendersResumeState>(
                    ErrorCode.IllegalArgument,
                    "Exact 模式拒绝未验证的恢复 cut / " +
                    "Exact mode rejects an unverified restored cut");
            }
            cuts.Add(cut);
        }

        var traces = new List<BendersIterationTrace>();
        int previousIteration = -1;
        foreach (string encodedTrace in state.Trace ?? Array.Empty<string>()) {
            Result<BendersIterationTrace, ErrorCode, Error<ErrorCode>> decodedTrace =
                DecodeTrace(encodedTrace);
            if (decodedTrace is not Ok<
                    BendersIterationTrace,
                    ErrorCode,
                    Error<ErrorCode>> traceOk) {
                return Propagate<BendersResumeState, BendersIterationTrace>(decodedTrace);
            }
            BendersIterationTrace trace = traceOk.Value;
            if (trace.Iteration <= previousIteration ||
                trace.Iteration >= state.Iteration) {
                return Failure<BendersResumeState>(
                    ErrorCode.IllegalArgument,
                    "Benders checkpoint trace 顺序或迭代范围无效 / " +
                    "Benders checkpoint trace order or iteration range is invalid");
            }
            if (trace.ProofMode != proofMode) {
                return Failure<BendersResumeState>(
                    ErrorCode.IllegalArgument,
                    "Benders checkpoint trace proof mode 与当前引擎不一致 / " +
                    "Benders checkpoint trace proof mode disagrees with the active engine");
            }
            previousIteration = trace.Iteration;
            traces.Add(trace);
        }

        var resume = new BendersResumeState(
            iteration: checked((int)state.Iteration),
            cuts: cuts,
            masterIncumbent: state.MasterIncumbent,
            masterBestBound: state.MasterBestBound,
            trace: state.Trace,
            assumptions: state.Assumptions,
            fixedBindings: state.FixedBindings,
            conflicts: state.Conflicts,
            convergenceVerified: state.ConvergenceVerified,
            masterFingerprint: state.MasterFingerprint,
            subproblemModelFingerprint: state.SubproblemModelFingerprint);
        if (expectedSubproblemSnapshot is not null) {
            Result<Success, ErrorCode, Error<ErrorCode>> evidence =
                ValidateResumeEvidence(
                    state: resume,
                    snapshot: expectedSubproblemSnapshot,
                    proofMode: proofMode);
            if (evidence is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
                return Propagate<BendersResumeState, Success>(evidence);
            }
        }
        return Results.Ok(resume);
    }

    /// <summary>
    /// 复验恢复状态与当前 CP snapshot 的身份和证据边界。
    /// Revalidate resume-state identity and evidence against the current CP snapshot.
    /// </summary>
    /// <param name="state">恢复状态 / Resume state.</param>
    /// <param name="snapshot">当前 CP snapshot / Current CP snapshot.</param>
    /// <param name="proofMode">当前证明模式 / Active proof mode.</param>
    /// <returns>成功或结构化错误 / Success or a structured error.</returns>
    public static Result<Success, ErrorCode, Error<ErrorCode>> ValidateResumeEvidence(
        BendersResumeState state,
        ConstraintProgrammingModelSnapshot snapshot,
        BendersProofMode proofMode = BendersProofMode.Exact) {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (state.Iteration < 0) {
            return Failure<Success>(
                ErrorCode.IllegalArgument,
                "Benders checkpoint iteration 不能为负 / " +
                "Benders checkpoint iteration must not be negative");
        }
        bool hasMasterState = HasPersistedMasterState(state);
        if (hasMasterState && string.IsNullOrWhiteSpace(state.MasterFingerprint)) {
            return Failure<Success>(
                ErrorCode.IllegalArgument,
                "Benders 主问题状态缺少模型指纹 / " +
                "Benders master state is missing a model fingerprint");
        }
        if ((state.MasterFingerprint is not null &&
                string.IsNullOrWhiteSpace(state.MasterFingerprint)) ||
            (state.SubproblemModelFingerprint is not null &&
                string.IsNullOrWhiteSpace(state.SubproblemModelFingerprint))) {
            return Failure<Success>(
                ErrorCode.IllegalArgument,
                "Benders checkpoint 模型指纹不能为空 / " +
                "Benders checkpoint model fingerprints must not be blank");
        }
        if (!TryParseFinite(state.MasterIncumbent, out _) ||
            !TryParseFinite(state.MasterBestBound, out _)) {
            return Failure<Success>(
                ErrorCode.IllegalArgument,
                "Benders checkpoint master bound/ incumbent 无效 / " +
                "Benders checkpoint master bound or incumbent is invalid");
        }
        if (state.SubproblemModelFingerprint is not null) {
            Result<string, ErrorCode, Error<ErrorCode>> fingerprint =
                ComputeSubproblemFingerprint(snapshot);
            if (fingerprint is not Ok<string, ErrorCode, Error<ErrorCode>> fingerprintOk) {
                return Propagate<Success, string>(fingerprint);
            }
            if (!string.Equals(
                    fingerprintOk.Value,
                    state.SubproblemModelFingerprint,
                    StringComparison.Ordinal)) {
                return Failure<Success>(
                    ErrorCode.ORSolutionInvalid,
                    "Benders checkpoint 子问题模型指纹不匹配 / " +
                    "Benders checkpoint subproblem model fingerprint mismatch");
            }
        }

        var variableIds = snapshot.Variables
            .Select(variable => variable.Id.Value)
            .ToHashSet(StringComparer.Ordinal);
        var constraintIds = snapshot.Constraints
            .Select(constraint => constraint.Id.Value)
            .ToHashSet(StringComparer.Ordinal);
        foreach (string assumption in state.Assumptions) {
            if (string.IsNullOrWhiteSpace(assumption) ||
                !variableIds.Contains(assumption)) {
                return Failure<Success>(
                    ErrorCode.IllegalArgument,
                    $"Benders checkpoint assumption 引用了未知变量：{assumption} / " +
                    $"Benders checkpoint assumption references an unknown variable: {assumption}");
            }
        }
        foreach ((string id, long raw) in state.FixedBindings) {
            if (string.IsNullOrWhiteSpace(id)) {
                return Failure<Success>(
                    ErrorCode.IllegalArgument,
                    "Benders checkpoint binding 标识不能为空 / " +
                    "Benders checkpoint binding identifiers must not be blank");
            }
            ConstraintProgrammingVariableSnapshot? definition =
                snapshot.Variable(new VariableId(id));
            if (definition is null) {
                return Failure<Success>(
                    ErrorCode.IllegalArgument,
                    $"Benders checkpoint fixed binding 引用了未知变量：{id} / " +
                    $"Benders checkpoint fixed binding references an unknown variable: {id}");
            }
            if (!definition.Domain.Contains(new Int64(raw))) {
                return Failure<Success>(
                    ErrorCode.IllegalArgument,
                    $"Benders checkpoint fixed binding 超出值域：{id}={raw} / " +
                    $"Benders checkpoint fixed binding is outside the domain: {id}={raw}");
            }
        }
        foreach (PortableConstraintProgrammingConflict conflict in state.Conflicts) {
            string? conflictFailure = ValidateConflict(
                conflict,
                variableIds,
                constraintIds);
            if (conflictFailure is not null) {
                return Failure<Success>(ErrorCode.IllegalArgument, conflictFailure);
            }
        }
        if (proofMode is not (BendersProofMode.Exact or BendersProofMode.Heuristic)) {
            return Failure<Success>(
                ErrorCode.IllegalArgument,
                "Benders checkpoint proof mode 无效 / Benders checkpoint proof mode is invalid");
        }
        return Results.OkInstance;
    }

    /// <summary>编码一轮迭代轨迹 / Encode one iteration trace.</summary>
    /// <param name="trace">迭代轨迹 / Iteration trace.</param>
    /// <returns>版本化 trace 文本或结构化错误 / Versioned trace text or a structured error.</returns>
    public static Result<string, ErrorCode, Error<ErrorCode>> EncodeTrace(
        BendersIterationTrace trace) {
        ArgumentNullException.ThrowIfNull(trace);
        try {
            var document = new BendersTraceDocument(
                SchemaVersion: TraceSchema,
                Iteration: trace.Iteration,
                MasterObjective: trace.MasterObjective?.ToString(),
                MasterBestBound: trace.MasterBestBound?.ToString(),
                MasterStatus: trace.MasterStatus?.ToString(),
                SubproblemStatus: trace.SubproblemStatus.ToString(),
                SubproblemObjective: trace.SubproblemObjective?.ToString(),
                CutCount: trace.CutCount,
                ConflictCoreSize: trace.ConflictCoreSize,
                ElapsedNanos: checked(trace.Elapsed.Ticks * 100L),
                ProofMode: trace.ProofMode.ToString(),
                ConvergenceGap: trace.ConvergenceGap?.ToString());
            return Results.Ok(TracePrefix + JsonSerializer.Serialize(document, JsonOptions));
        }
        catch (Exception error) {
            return Failure<string>(
                ErrorCode.Other,
                $"Benders checkpoint trace 编码失败：{error.Message} / " +
                $"Benders checkpoint trace encoding failed: {error.Message}");
        }
    }

    /// <summary>解码一轮迭代轨迹 / Decode one iteration trace.</summary>
    /// <param name="encoded">版本化 trace 文本 / Versioned trace text.</param>
    /// <returns>迭代轨迹或结构化错误 / Iteration trace or a structured error.</returns>
    public static Result<BendersIterationTrace, ErrorCode, Error<ErrorCode>> DecodeTrace(
        string encoded) {
        ArgumentNullException.ThrowIfNull(encoded);
        if (!encoded.StartsWith(TracePrefix, StringComparison.Ordinal)) {
            return Failure<BendersIterationTrace>(
                ErrorCode.IllegalArgument,
                "Benders checkpoint trace 缺少版本 / " +
                "Benders checkpoint trace version is missing");
        }
        try {
            BendersTraceDocument? document =
                JsonSerializer.Deserialize<BendersTraceDocument>(
                    encoded[TracePrefix.Length..],
                    JsonOptions);
            if (document is null ||
                !string.Equals(document.SchemaVersion, TraceSchema, StringComparison.Ordinal) ||
                document.Iteration < 0 ||
                document.CutCount < 0 ||
                document.ConflictCoreSize < 0 ||
                document.ElapsedNanos < 0) {
                return Failure<BendersIterationTrace>(
                    ErrorCode.IllegalArgument,
                    "Benders checkpoint trace 字段无效 / " +
                    "Benders checkpoint trace fields are invalid");
            }
            if (!Enum.TryParse(
                    document.SubproblemStatus,
                    ignoreCase: false,
                    out BendersSubproblemStatus subproblemStatus) ||
                !Enum.IsDefined(subproblemStatus)) {
                return Failure<BendersIterationTrace>(
                    ErrorCode.IllegalArgument,
                    "Benders checkpoint trace 子问题状态无效 / " +
                    "Benders checkpoint trace subproblem status is invalid");
            }
            if (!Enum.TryParse(
                    document.ProofMode,
                    ignoreCase: false,
                    out BendersProofMode proofMode) ||
                !Enum.IsDefined(proofMode)) {
                return Failure<BendersIterationTrace>(
                    ErrorCode.IllegalArgument,
                    "Benders checkpoint trace proof mode 无效 / " +
                    "Benders checkpoint trace proof mode is invalid");
            }
            SolverStatus? masterStatus = null;
            if (document.MasterStatus is not null) {
                if (!Enum.TryParse(
                        document.MasterStatus,
                        ignoreCase: false,
                        out SolverStatus parsedStatus) ||
                    !Enum.IsDefined(parsedStatus)) {
                    return Failure<BendersIterationTrace>(
                        ErrorCode.IllegalArgument,
                        "Benders checkpoint trace 主问题状态无效 / " +
                        "Benders checkpoint trace master status is invalid");
                }
                masterStatus = parsedStatus;
            }
            Result<Flt64?, ErrorCode, Error<ErrorCode>> masterObjective =
                ParseTraceNumber(document.MasterObjective, "masterObjective");
            if (masterObjective is not Ok<Flt64?, ErrorCode, Error<ErrorCode>> objectiveOk) {
                return Propagate<BendersIterationTrace, Flt64?>(masterObjective);
            }
            Result<Flt64?, ErrorCode, Error<ErrorCode>> masterBestBound =
                ParseTraceNumber(document.MasterBestBound, "masterBestBound");
            if (masterBestBound is not Ok<Flt64?, ErrorCode, Error<ErrorCode>> boundOk) {
                return Propagate<BendersIterationTrace, Flt64?>(masterBestBound);
            }
            Result<Flt64?, ErrorCode, Error<ErrorCode>> subproblemObjective =
                ParseTraceNumber(document.SubproblemObjective, "subproblemObjective");
            if (subproblemObjective is not Ok<
                    Flt64?,
                    ErrorCode,
                    Error<ErrorCode>> subproblemObjectiveOk) {
                return Propagate<BendersIterationTrace, Flt64?>(subproblemObjective);
            }
            Result<Flt64?, ErrorCode, Error<ErrorCode>> convergenceGap =
                ParseTraceNumber(document.ConvergenceGap, "convergenceGap");
            if (convergenceGap is not Ok<Flt64?, ErrorCode, Error<ErrorCode>> gapOk) {
                return Propagate<BendersIterationTrace, Flt64?>(convergenceGap);
            }
            return Results.Ok(new BendersIterationTrace(
                Iteration: document.Iteration,
                MasterObjective: objectiveOk.Value,
                MasterBestBound: boundOk.Value,
                MasterStatus: masterStatus,
                SubproblemStatus: subproblemStatus,
                SubproblemObjective: subproblemObjectiveOk.Value,
                CutCount: document.CutCount,
                ConflictCoreSize: document.ConflictCoreSize,
                Elapsed: TimeSpan.FromTicks(document.ElapsedNanos / 100L),
                ProofMode: proofMode,
                ConvergenceGap: gapOk.Value));
        }
        catch (Exception error) {
            return Failure<BendersIterationTrace>(
                ErrorCode.IllegalArgument,
                $"Benders checkpoint trace 解码失败：{error.Message} / " +
                $"Benders checkpoint trace decoding failed: {error.Message}");
        }
    }

    /// <summary>是否存在需要主问题适配器应用的状态 / Whether master state needs an applier.</summary>
    /// <param name="state">恢复状态 / Resume state.</param>
    /// <returns>是否需要应用 / Whether an applier is required.</returns>
    public static bool HasPersistedMasterState(BendersResumeState state) {
        ArgumentNullException.ThrowIfNull(state);
        return state.MasterIncumbent is not null ||
            state.MasterBestBound is not null ||
            state.MasterFingerprint is not null ||
            state.Assumptions.Count > 0 ||
            state.FixedBindings.Count > 0 ||
            state.Conflicts.Count > 0 ||
            state.ConvergenceVerified;
    }

    /// <summary>比较恢复适配器证据与 checkpoint / Compare resume evidence with a checkpoint.</summary>
    /// <param name="evidence">适配器返回的证据 / Evidence returned by the applier.</param>
    /// <param name="state">恢复状态 / Resume state.</param>
    /// <returns>是否一致 / Whether they match.</returns>
    public static bool EvidenceMatches(
        BendersMasterResumeEvidence evidence,
        BendersResumeState state) {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(state);
        return !string.IsNullOrWhiteSpace(evidence.MasterFingerprint) &&
            string.Equals(
                evidence.MasterFingerprint,
                state.MasterFingerprint,
                StringComparison.Ordinal) &&
            string.Equals(evidence.Incumbent, state.MasterIncumbent, StringComparison.Ordinal) &&
            string.Equals(evidence.BestBound, state.MasterBestBound, StringComparison.Ordinal) &&
            evidence.Assumptions.SequenceEqual(state.Assumptions, StringComparer.Ordinal) &&
            evidence.FixedBindings.Count == state.FixedBindings.Count &&
            evidence.FixedBindings.All(pair =>
                state.FixedBindings.TryGetValue(pair.Key, out long value) &&
                value == pair.Value) &&
            evidence.Conflicts.SequenceEqual(state.Conflicts) &&
            evidence.ConvergenceVerified == state.ConvergenceVerified;
    }

    private static Result<string, ErrorCode, Error<ErrorCode>> ComputeSubproblemFingerprint(
        ConstraintProgrammingModelSnapshot snapshot) {
        Result<string, ErrorCode, Error<ErrorCode>> encoded =
            ConstraintProgrammingSnapshotCodec.Encode(snapshot);
        return encoded is Ok<string, ErrorCode, Error<ErrorCode>> ok
            ? Results.Ok(SolveFingerprinting.Sha256(ok.Value).Value)
            : Propagate<string, string>(encoded);
    }

    private static PortableConstraintProgrammingConflict ToPortableConflict(
        ConstraintProgrammingConflict conflict) {
        var members = new HashSet<string>(StringComparer.Ordinal);
        foreach (InfeasibilityMember member in conflict.Members) {
            members.Add(member switch {
                InfeasibilityMember.ConstraintMember constraint =>
                    $"constraint:{constraint.Id.Value}",
                InfeasibilityMember.VariableBoundMember bound =>
                    $"bound:{bound.Ref.VariableId.Value}:{bound.Ref.Side}",
                InfeasibilityMember.VariableDomainMember domain =>
                    $"domain:{domain.Ref.VariableId.Value}",
                _ => string.Empty,
            });
        }
        foreach (ConstraintId constraintId in conflict.ConstraintIds) {
            members.Add($"constraint:{constraintId.Value}");
        }
        foreach (VariableId variableId in conflict.VariableIds) {
            members.Add($"domain:{variableId.Value}");
        }
        members.Remove(string.Empty);
        var assumptions = conflict.Assumptions
            .Select(assumption => assumption.VariableId?.Value)
            .Where(value => value is not null)
            .Select(value => value!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToList();
        var provenance = new Dictionary<string, string>(StringComparer.Ordinal);
        if (conflict.Message is not null) {
            provenance["message"] = conflict.Message;
        }
        if (conflict.VerificationChecks is not null) {
            provenance["verificationChecks"] = conflict.VerificationChecks.Value
                .ToString(CultureInfo.InvariantCulture);
        }
        if (conflict.TerminationReason is not null) {
            provenance["terminationReason"] = conflict.TerminationReason.Value.ToString();
        }
        return new PortableConstraintProgrammingConflict(
            Validity: conflict.Validity.ToString(),
            Minimality: conflict.Minimality.ToString(),
            MemberIds: members
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToList(),
            AssumptionIds: assumptions,
            Provenance: provenance);
    }

    private static string? ValidatePortableState(
        PortableConstraintProgrammingBendersState state,
        bool allowLegacyUnboundMasterFingerprint = false) {
        if (state.Iteration < 0) {
            return "Benders checkpoint iteration 不能为负 / " +
                "Benders checkpoint iteration must not be negative";
        }
        IReadOnlyList<PortableConstraintProgrammingCut> cuts =
            state.Cuts ?? Array.Empty<PortableConstraintProgrammingCut>();
        if (cuts.Any(cut =>
                cut is null ||
                string.IsNullOrWhiteSpace(cut.Id) ||
                string.IsNullOrWhiteSpace(cut.SchemaVersion) ||
                !Enum.TryParse(cut.Validity, false, out BendersCutValidity validity) ||
                !Enum.IsDefined(validity) ||
                (cut.Provenance?.Keys.Any(string.IsNullOrWhiteSpace) ?? false) ||
                (cut.Payload?.Keys.Any(string.IsNullOrWhiteSpace) ?? false))) {
            return "Benders checkpoint state 或 cut ID/schema 无效 / " +
                "Benders checkpoint state or cut ID/schema is invalid";
        }
        if (cuts.Select(cut => cut.Id).Distinct(StringComparer.Ordinal).Count() != cuts.Count) {
            return "Benders checkpoint 存在重复 cut ID / " +
                "Benders checkpoint contains duplicate cut IDs";
        }
        if ((state.MasterFingerprint is not null &&
                string.IsNullOrWhiteSpace(state.MasterFingerprint)) ||
            (state.SubproblemModelFingerprint is not null &&
                string.IsNullOrWhiteSpace(state.SubproblemModelFingerprint))) {
            return "Benders checkpoint 模型指纹不能为空 / " +
                "Benders checkpoint model fingerprints must not be blank";
        }
        bool hasMasterState = state.MasterIncumbent is not null ||
            state.MasterBestBound is not null ||
            (state.Assumptions?.Count ?? 0) > 0 ||
            (state.FixedBindings?.Count ?? 0) > 0 ||
            (state.Conflicts?.Count ?? 0) > 0 ||
            state.ConvergenceVerified;
        if (!allowLegacyUnboundMasterFingerprint &&
            hasMasterState &&
            string.IsNullOrWhiteSpace(state.MasterFingerprint)) {
            return "Benders 主问题状态缺少模型指纹 / " +
                "Benders master state is missing a model fingerprint";
        }
        if (!TryParseFinite(state.MasterIncumbent, out _) ||
            !TryParseFinite(state.MasterBestBound, out _)) {
            return "Benders checkpoint master bound/ incumbent 无效 / " +
                "Benders checkpoint master bound or incumbent is invalid";
        }
        if ((state.Assumptions ?? Array.Empty<string>()).Any(string.IsNullOrWhiteSpace) ||
            (state.FixedBindings?.Keys.Any(string.IsNullOrWhiteSpace) ?? false)) {
            return "Benders checkpoint binding 标识不能为空 / " +
                "Benders checkpoint binding identifiers must not be blank";
        }
        foreach (PortableConstraintProgrammingConflict conflict in
            state.Conflicts ?? Array.Empty<PortableConstraintProgrammingConflict>()) {
            if (conflict is null ||
                conflict.Validity is not ("Verified" or "Heuristic" or "Unknown") ||
                conflict.Minimality is not ("Irreducible" or "Partial" or "NotChecked") ||
                (conflict.MemberIds?.Any(string.IsNullOrWhiteSpace) ?? false) ||
                (conflict.AssumptionIds?.Any(string.IsNullOrWhiteSpace) ?? false) ||
                (conflict.Provenance?.Keys.Any(string.IsNullOrWhiteSpace) ?? false)) {
                return "Benders checkpoint conflict 证据无效 / " +
                    "Benders checkpoint conflict evidence is invalid";
            }
        }
        return null;
    }

    private static string? ValidateConflict(
        PortableConstraintProgrammingConflict conflict,
        IReadOnlySet<string> variableIds,
        IReadOnlySet<string> constraintIds) {
        if (conflict.Validity is not ("Verified" or "Heuristic" or "Unknown") ||
            conflict.Minimality is not ("Irreducible" or "Partial" or "NotChecked")) {
            return "Benders checkpoint conflict 证据枚举无效 / " +
                "Benders checkpoint conflict evidence enum is invalid";
        }
        IReadOnlyList<string> members = conflict.MemberIds ?? Array.Empty<string>();
        IReadOnlyList<string> assumptions = conflict.AssumptionIds ?? Array.Empty<string>();
        if (conflict.Validity == "Verified" && members.Count == 0) {
            return "已验证 Benders checkpoint conflict 不能没有成员 / " +
                "A verified Benders checkpoint conflict must contain members";
        }
        if (assumptions.Any(id => !variableIds.Contains(id))) {
            return "Benders checkpoint conflict assumption 无效 / " +
                "Benders checkpoint conflict assumption is invalid";
        }
        foreach (string member in members) {
            if (member.StartsWith("constraint:", StringComparison.Ordinal)) {
                if (!constraintIds.Contains(member["constraint:".Length..])) {
                    return "Benders checkpoint conflict 约束无效 / " +
                        "Benders checkpoint conflict constraint is invalid";
                }
            }
            else if (member.StartsWith("domain:", StringComparison.Ordinal)) {
                if (!variableIds.Contains(member["domain:".Length..])) {
                    return "Benders checkpoint conflict 域成员无效 / " +
                        "Benders checkpoint conflict domain member is invalid";
                }
            }
            else if (member.StartsWith("bound:", StringComparison.Ordinal)) {
                string encodedMember = member["bound:".Length..];
                int separator = encodedMember.LastIndexOf(':');
                if (separator <= 0 ||
                    !variableIds.Contains(encodedMember[..separator]) ||
                    encodedMember[(separator + 1)..] is not ("Lower" or "Upper")) {
                    return "Benders checkpoint conflict bound 成员无效 / " +
                        "Benders checkpoint conflict bound member is invalid";
                }
            }
            else {
                return "Benders checkpoint conflict 成员类型未知 / " +
                    "Benders checkpoint conflict member type is unknown";
            }
        }
        return null;
    }

    private static PortableConstraintProgrammingBendersState NormalizeState(
        PortableConstraintProgrammingBendersState state) {
        return new PortableConstraintProgrammingBendersState(
            Iteration: state.Iteration,
            MasterIncumbent: state.MasterIncumbent,
            MasterBestBound: state.MasterBestBound,
            Cuts: (state.Cuts ?? Array.Empty<PortableConstraintProgrammingCut>())
                .Select(NormalizeCut)
                .ToList(),
            Trace: (state.Trace ?? Array.Empty<string>()).ToList(),
            Assumptions: (state.Assumptions ?? Array.Empty<string>()).ToList(),
            FixedBindings: (state.FixedBindings ??
                new Dictionary<string, long>())
                .ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value,
                    StringComparer.Ordinal),
            Conflicts: (state.Conflicts ??
                Array.Empty<PortableConstraintProgrammingConflict>())
                .Select(NormalizeConflict)
                .ToList(),
            ConvergenceVerified: state.ConvergenceVerified,
            MasterFingerprint: state.MasterFingerprint,
            SubproblemModelFingerprint: state.SubproblemModelFingerprint);
    }

    private static PortableConstraintProgrammingCut NormalizeCut(
        PortableConstraintProgrammingCut cut) {
        return new PortableConstraintProgrammingCut(
            Id: cut.Id,
            SchemaVersion: cut.SchemaVersion,
            Validity: cut.Validity,
            Provenance: cut.Provenance is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(
                    cut.Provenance,
                    StringComparer.Ordinal),
            Payload: cut.Payload is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(
                    cut.Payload,
                    StringComparer.Ordinal));
    }

    private static PortableConstraintProgrammingConflict NormalizeConflict(
        PortableConstraintProgrammingConflict conflict) {
        return new PortableConstraintProgrammingConflict(
            Validity: conflict.Validity,
            Minimality: conflict.Minimality,
            MemberIds: (conflict.MemberIds ?? Array.Empty<string>()).ToList(),
            AssumptionIds: (conflict.AssumptionIds ?? Array.Empty<string>()).ToList(),
            Provenance: conflict.Provenance is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(
                    conflict.Provenance,
                    StringComparer.Ordinal));
    }

    private static PortableConstraintProgrammingBendersState ToPortableState(
        LegacyBendersState state) {
        return new PortableConstraintProgrammingBendersState(
            Iteration: state.Iteration,
            MasterIncumbent: state.MasterIncumbent,
            MasterBestBound: state.MasterBestBound,
            Cuts: state.Cuts,
            Trace: state.Trace,
            Assumptions: state.Assumptions,
            FixedBindings: state.FixedBindings,
            Conflicts: state.Conflicts,
            ConvergenceVerified: state.ConvergenceVerified,
            MasterFingerprint: null,
            SubproblemModelFingerprint: state.SubproblemModelFingerprint);
    }

    private static string Digest(BendersCheckpointDocument document) {
        return SolveFingerprinting.Sha256(
            JsonSerializer.Serialize(document, JsonOptions)).Value;
    }

    private static string LegacyDigest(LegacyBendersCheckpointDocument document) {
        return SolveFingerprinting.Sha256(
            JsonSerializer.Serialize(document, JsonOptions)).Value;
    }

    private static Result<Flt64?, ErrorCode, Error<ErrorCode>> ParseTraceNumber(
        string? value,
        string field) {
        if (value is null) {
            return Results.Ok<Flt64?>(null);
        }
        if (!double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double parsed) ||
            !double.IsFinite(parsed)) {
            return Failure<Flt64?>(
                ErrorCode.IllegalArgument,
                $"Benders checkpoint trace 数值无效：{field} / " +
                $"Benders checkpoint trace number is invalid: {field}");
        }
        return Results.Ok<Flt64?>(new Flt64(parsed));
    }

    private static bool TryParseFinite(string? value, out double parsed) {
        if (value is null) {
            parsed = 0;
            return true;
        }
        return double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out parsed) &&
            double.IsFinite(parsed);
    }

    private static Result<T, ErrorCode, Error<ErrorCode>> Propagate<T, S>(
        Result<S, ErrorCode, Error<ErrorCode>> result) {
        return result switch {
            Failed<S, ErrorCode, Error<ErrorCode>> failed =>
                Results.Failed<T>(failed.Error),
            Fatal<S, ErrorCode, Error<ErrorCode>> fatal =>
                new Fatal<T, ErrorCode, Error<ErrorCode>>(fatal.Errors),
            _ => Failure<T>(
                ErrorCode.ApplicationError,
                "Benders checkpoint 结果状态无效 / " +
                "Invalid Benders checkpoint result state"),
        };
    }

    private static Result<T, ErrorCode, Error<ErrorCode>> Failure<T>(
        ErrorCode code,
        string message) =>
        Results.Failed<T>(new Err<ErrorCode>(code: code, message: message));
}

internal sealed record BendersCheckpointDocument(
    [property: JsonPropertyName("schemaVersion"), JsonPropertyOrder(0)]
    string SchemaVersion,
    [property: JsonPropertyName("state"), JsonPropertyOrder(1)]
    PortableConstraintProgrammingBendersState State,
    [property: JsonPropertyName("integritySha256"), JsonPropertyOrder(2)]
    string IntegritySha256);

internal sealed record LegacyBendersCheckpointDocument(
    [property: JsonPropertyName("schemaVersion"), JsonPropertyOrder(0)]
    string SchemaVersion,
    [property: JsonPropertyName("state"), JsonPropertyOrder(1)]
    LegacyBendersState State,
    [property: JsonPropertyName("integritySha256"), JsonPropertyOrder(2)]
    string IntegritySha256);

internal sealed record LegacyBendersState(
    [property: JsonPropertyName("iteration"), JsonPropertyOrder(0)]
    long Iteration,
    [property: JsonPropertyName("masterIncumbent"), JsonPropertyOrder(1)]
    string? MasterIncumbent = null,
    [property: JsonPropertyName("masterBestBound"), JsonPropertyOrder(2)]
    string? MasterBestBound = null,
    [property: JsonPropertyName("cuts"), JsonPropertyOrder(3)]
    IReadOnlyList<PortableConstraintProgrammingCut>? Cuts = null,
    [property: JsonPropertyName("trace"), JsonPropertyOrder(4)]
    IReadOnlyList<string>? Trace = null,
    [property: JsonPropertyName("assumptions"), JsonPropertyOrder(5)]
    IReadOnlyList<string>? Assumptions = null,
    [property: JsonPropertyName("fixedBindings"), JsonPropertyOrder(6)]
    IReadOnlyDictionary<string, long>? FixedBindings = null,
    [property: JsonPropertyName("conflicts"), JsonPropertyOrder(7)]
    IReadOnlyList<PortableConstraintProgrammingConflict>? Conflicts = null,
    [property: JsonPropertyName("convergenceVerified"), JsonPropertyOrder(8)]
    bool ConvergenceVerified = false,
    [property: JsonPropertyName("subproblemModelFingerprint"), JsonPropertyOrder(9)]
    string? SubproblemModelFingerprint = null);

internal sealed record BendersTraceDocument(
    [property: JsonPropertyName("schemaVersion"), JsonPropertyOrder(0)]
    string SchemaVersion,
    [property: JsonPropertyName("iteration"), JsonPropertyOrder(1)]
    int Iteration,
    [property: JsonPropertyName("masterObjective"), JsonPropertyOrder(2)]
    string? MasterObjective = null,
    [property: JsonPropertyName("masterBestBound"), JsonPropertyOrder(3)]
    string? MasterBestBound = null,
    [property: JsonPropertyName("masterStatus"), JsonPropertyOrder(4)]
    string? MasterStatus = null,
    [property: JsonPropertyName("subproblemStatus"), JsonPropertyOrder(5)]
    string SubproblemStatus = "",
    [property: JsonPropertyName("subproblemObjective"), JsonPropertyOrder(6)]
    string? SubproblemObjective = null,
    [property: JsonPropertyName("cutCount"), JsonPropertyOrder(7)]
    int CutCount = 0,
    [property: JsonPropertyName("conflictCoreSize"), JsonPropertyOrder(8)]
    int ConflictCoreSize = 0,
    [property: JsonPropertyName("elapsedNanos"), JsonPropertyOrder(9)]
    long ElapsedNanos = 0,
    [property: JsonPropertyName("proofMode"), JsonPropertyOrder(10)]
    string ProofMode = "",
    [property: JsonPropertyName("convergenceGap"), JsonPropertyOrder(11)]
    string? ConvergenceGap = null);
