#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Solver.ConstraintProgramming;

/// <summary>
/// 不包含后端或 native 句柄的 CP checkpoint envelope（schema 3.0）。
/// Portable CP checkpoint envelope without backend or native handles (schema 3.0).
/// </summary>
/// <param name="SchemaVersion">Envelope schema 版本 / Envelope schema version.</param>
/// <param name="SourceFormat">Checkpoint 来源族 / Checkpoint source family.</param>
/// <param name="MigratedFromLegacy">是否由 legacy v1 迁移 / Whether migrated from legacy v1.</param>
/// <param name="CheckpointId">Checkpoint 标识 / Checkpoint identifier.</param>
/// <param name="IdentitySchemaVersion">身份 schema 版本 / Identity schema version.</param>
/// <param name="IdentityNamespace">身份命名空间 / Identity namespace.</param>
/// <param name="ModelName">模型名称 / Model name.</param>
/// <param name="ModelFingerprint">模型指纹 / Model fingerprint.</param>
/// <param name="ConfigurationFingerprint">配置指纹 / Configuration fingerprint.</param>
/// <param name="SolverFingerprint">求解器指纹 / Solver fingerprint.</param>
/// <param name="RunId">运行标识 / Run identifier.</param>
/// <param name="AttemptId">尝试标识 / Attempt identifier.</param>
/// <param name="ParentCheckpointId">父 checkpoint 标识 / Parent checkpoint identifier.</param>
/// <param name="CreatedAtEpochMs">创建时间 / Creation time in epoch milliseconds.</param>
/// <param name="SnapshotJson">规范 snapshot JSON / Canonical snapshot JSON.</param>
/// <param name="Incumbent">已验证 incumbent / Verified incumbent.</param>
/// <param name="BestBound">历史最佳界 / Historical best bound.</param>
/// <param name="Gap">历史相对 gap / Historical relative gap.</param>
/// <param name="Assumptions">Assumption 变量标识 / Assumption variable identifiers.</param>
/// <param name="Conflicts">冲突证据 / Conflict evidence.</param>
/// <param name="Benders">Benders 状态 / Benders state.</param>
/// <param name="CancellationChain">按发生顺序排列的取消链 / Cancellation chain in occurrence order.</param>
/// <param name="Provenance">求解器执行来源 / Solver execution provenance.</param>
/// <param name="Metadata">非敏感算法元数据 / Non-sensitive algorithm metadata.</param>
/// <param name="IntegritySha256">完整性摘要 / Integrity digest.</param>
public sealed record ConstraintProgrammingCheckpointEnvelope(
    [property: JsonPropertyName("schemaVersion"), JsonPropertyOrder(0)] string SchemaVersion,
    [property: JsonPropertyName("sourceFormat"), JsonPropertyOrder(1)] string SourceFormat,
    [property: JsonPropertyName("migratedFromLegacy"), JsonPropertyOrder(2)] bool MigratedFromLegacy,
    [property: JsonPropertyName("checkpointId"), JsonPropertyOrder(3)] string CheckpointId,
    [property: JsonPropertyName("identitySchemaVersion"), JsonPropertyOrder(4)] string IdentitySchemaVersion,
    [property: JsonPropertyName("identityNamespace"), JsonPropertyOrder(5)] string IdentityNamespace,
    [property: JsonPropertyName("modelName"), JsonPropertyOrder(6)] string ModelName,
    [property: JsonPropertyName("modelFingerprint"), JsonPropertyOrder(7)] string ModelFingerprint,
    [property: JsonPropertyName("configurationFingerprint"), JsonPropertyOrder(8)] string? ConfigurationFingerprint,
    [property: JsonPropertyName("solverFingerprint"), JsonPropertyOrder(9)] string? SolverFingerprint,
    [property: JsonPropertyName("runId"), JsonPropertyOrder(10)] string? RunId,
    [property: JsonPropertyName("attemptId"), JsonPropertyOrder(11)] string? AttemptId,
    [property: JsonPropertyName("parentCheckpointId"), JsonPropertyOrder(12)] string? ParentCheckpointId,
    [property: JsonPropertyName("createdAtEpochMs"), JsonPropertyOrder(13)] long CreatedAtEpochMs,
    [property: JsonPropertyName("snapshotJson"), JsonPropertyOrder(14)] string SnapshotJson,
    [property: JsonPropertyName("incumbent"), JsonPropertyOrder(15)] PortableConstraintProgrammingIncumbent? Incumbent,
    [property: JsonPropertyName("bestBound"), JsonPropertyOrder(16)] string? BestBound,
    [property: JsonPropertyName("gap"), JsonPropertyOrder(17)] string? Gap,
    [property: JsonPropertyName("assumptions"), JsonPropertyOrder(18)] IReadOnlyList<string>? Assumptions,
    [property: JsonPropertyName("conflicts"), JsonPropertyOrder(19)] IReadOnlyList<PortableConstraintProgrammingConflict>? Conflicts,
    [property: JsonPropertyName("benders"), JsonPropertyOrder(20)] PortableConstraintProgrammingBendersState? Benders,
    [property: JsonPropertyName("cancellationChain"), JsonPropertyOrder(21)] IReadOnlyList<PortableCancellationRecord>? CancellationChain,
    [property: JsonPropertyName("provenance"), JsonPropertyOrder(22)] PortableSolverProvenance? Provenance,
    [property: JsonPropertyName("metadata"), JsonPropertyOrder(23)] IReadOnlyDictionary<string, string>? Metadata,
    [property: JsonPropertyName("integritySha256"), JsonPropertyOrder(24)] string IntegritySha256);

/// <summary>取消事实的线格式 / Wire shape of a cancellation fact.</summary>
/// <param name="Origin">取消来源规范代码 / Canonical cancellation-origin code.</param>
/// <param name="RequestedAtEpochMs">请求时间 / Request time in epoch milliseconds.</param>
/// <param name="Reason">取消原因 / Cancellation reason.</param>
public sealed record PortableCancellationRecord(
    [property: JsonPropertyName("origin"), JsonPropertyOrder(0)] string Origin,
    [property: JsonPropertyName("requestedAtEpochMs"), JsonPropertyOrder(1)] long RequestedAtEpochMs,
    [property: JsonPropertyName("reason"), JsonPropertyOrder(2)] string? Reason = null);

/// <summary>求解器执行来源的线格式 / Wire shape of solver execution provenance.</summary>
/// <param name="SolverId">求解器标识 / Solver identifier.</param>
/// <param name="BackendName">后端名称 / Backend name.</param>
/// <param name="BackendVersion">后端版本 / Backend version.</param>
/// <param name="PluginVersion">插件版本 / Plugin version.</param>
/// <param name="RequestedConfiguration">请求配置 / Requested configuration.</param>
/// <param name="EffectiveConfiguration">生效配置 / Effective configuration.</param>
/// <param name="ThreadCount">线程数 / Thread count.</param>
/// <param name="RandomSeed">随机种子 / Random seed.</param>
/// <param name="Deterministic">是否确定性执行 / Whether deterministic.</param>
/// <param name="EnvironmentSummary">脱敏环境摘要 / Redacted environment summary.</param>
public sealed record PortableSolverProvenance(
    [property: JsonPropertyName("solverId"), JsonPropertyOrder(0)] string SolverId,
    [property: JsonPropertyName("backendName"), JsonPropertyOrder(1)] string BackendName,
    [property: JsonPropertyName("backendVersion"), JsonPropertyOrder(2)] string? BackendVersion = null,
    [property: JsonPropertyName("pluginVersion"), JsonPropertyOrder(3)] string? PluginVersion = null,
    [property: JsonPropertyName("requestedConfiguration"), JsonPropertyOrder(4)] IReadOnlyDictionary<string, string>? RequestedConfiguration = null,
    [property: JsonPropertyName("effectiveConfiguration"), JsonPropertyOrder(5)] IReadOnlyDictionary<string, string>? EffectiveConfiguration = null,
    [property: JsonPropertyName("threadCount"), JsonPropertyOrder(6)] int? ThreadCount = null,
    [property: JsonPropertyName("randomSeed"), JsonPropertyOrder(7)] long? RandomSeed = null,
    [property: JsonPropertyName("deterministic"), JsonPropertyOrder(8)] bool? Deterministic = null,
    [property: JsonPropertyName("environmentSummary"), JsonPropertyOrder(9)] IReadOnlyDictionary<string, string>? EnvironmentSummary = null);

/// <summary>可移植 incumbent / Portable incumbent.</summary>
/// <param name="ValuesById">稳定变量值 / Stable variable values.</param>
/// <param name="IntervalsById">稳定 interval 值 / Stable interval values.</param>
/// <param name="Objective">目标值 / Objective value.</param>
public sealed record PortableConstraintProgrammingIncumbent(
    [property: JsonPropertyName("valuesById"), JsonPropertyOrder(0)] IReadOnlyDictionary<string, long>? ValuesById = null,
    [property: JsonPropertyName("intervalsById"), JsonPropertyOrder(1)] IReadOnlyDictionary<string, PortableConstraintProgrammingIntervalValue>? IntervalsById = null,
    [property: JsonPropertyName("objective"), JsonPropertyOrder(2)] string? Objective = null);

/// <summary>可移植 interval 值 / Portable interval value.</summary>
/// <param name="Start">开始值 / Start value.</param>
/// <param name="Size">长度 / Size.</param>
/// <param name="End">结束值 / End value.</param>
/// <param name="Present">是否存在 / Whether present.</param>
public sealed record PortableConstraintProgrammingIntervalValue(
    [property: JsonPropertyName("start"), JsonPropertyOrder(0)] long Start,
    [property: JsonPropertyName("size"), JsonPropertyOrder(1)] long Size,
    [property: JsonPropertyName("end"), JsonPropertyOrder(2)] long End,
    [property: JsonPropertyName("present"), JsonPropertyOrder(3)] bool Present = true);

/// <summary>可移植冲突证据 / Portable conflict evidence.</summary>
/// <param name="Validity">证据有效性 / Evidence validity.</param>
/// <param name="Minimality">证据最小性 / Evidence minimality.</param>
/// <param name="MemberIds">原始成员标识 / Original member identifiers.</param>
/// <param name="AssumptionIds">Assumption 变量标识 / Assumption variable identifiers.</param>
/// <param name="Provenance">脱敏证据元数据 / Sanitized evidence metadata.</param>
public sealed record PortableConstraintProgrammingConflict(
    [property: JsonPropertyName("validity"), JsonPropertyOrder(0)] string Validity,
    [property: JsonPropertyName("minimality"), JsonPropertyOrder(1)] string Minimality,
    [property: JsonPropertyName("memberIds"), JsonPropertyOrder(2)] IReadOnlyList<string>? MemberIds = null,
    [property: JsonPropertyName("assumptionIds"), JsonPropertyOrder(3)] IReadOnlyList<string>? AssumptionIds = null,
    [property: JsonPropertyName("provenance"), JsonPropertyOrder(4)] IReadOnlyDictionary<string, string>? Provenance = null);

/// <summary>可移植 Benders 状态 / Portable Benders state.</summary>
/// <param name="Iteration">当前迭代 / Current iteration.</param>
/// <param name="MasterIncumbent">Master incumbent.</param>
/// <param name="MasterBestBound">Master best bound.</param>
/// <param name="Cuts">去重 cut 记录 / Deduplicated cut records.</param>
/// <param name="Trace">迭代轨迹 / Iteration trace.</param>
/// <param name="Assumptions">已验证 assumption 标识 / Revalidated assumption identifiers.</param>
/// <param name="FixedBindings">已验证固定绑定 / Revalidated fixed bindings.</param>
/// <param name="Conflicts">已验证冲突证据 / Revalidated conflict evidence.</param>
/// <param name="ConvergenceVerified">精确收敛证明 / Exact convergence proof.</param>
/// <param name="MasterFingerprint">主问题模型指纹 / Master model fingerprint.</param>
/// <param name="SubproblemModelFingerprint">子问题模型指纹 / Subproblem model fingerprint.</param>
public sealed record PortableConstraintProgrammingBendersState(
    [property: JsonPropertyName("iteration"), JsonPropertyOrder(0)] long Iteration,
    [property: JsonPropertyName("masterIncumbent"), JsonPropertyOrder(1)] string? MasterIncumbent = null,
    [property: JsonPropertyName("masterBestBound"), JsonPropertyOrder(2)] string? MasterBestBound = null,
    [property: JsonPropertyName("cuts"), JsonPropertyOrder(3)] IReadOnlyList<PortableConstraintProgrammingCut>? Cuts = null,
    [property: JsonPropertyName("trace"), JsonPropertyOrder(4)] IReadOnlyList<string>? Trace = null,
    [property: JsonPropertyName("assumptions"), JsonPropertyOrder(5)] IReadOnlyList<string>? Assumptions = null,
    [property: JsonPropertyName("fixedBindings"), JsonPropertyOrder(6)] IReadOnlyDictionary<string, long>? FixedBindings = null,
    [property: JsonPropertyName("conflicts"), JsonPropertyOrder(7)] IReadOnlyList<PortableConstraintProgrammingConflict>? Conflicts = null,
    [property: JsonPropertyName("convergenceVerified"), JsonPropertyOrder(8)] bool ConvergenceVerified = false,
    [property: JsonPropertyName("masterFingerprint"), JsonPropertyOrder(9)] string? MasterFingerprint = null,
    [property: JsonPropertyName("subproblemModelFingerprint"), JsonPropertyOrder(10)] string? SubproblemModelFingerprint = null);

/// <summary>排除闭包和 native 句柄的版本化 cut / Versioned cut without closures or native handles.</summary>
/// <param name="Id">稳定 cut 标识 / Stable cut identifier.</param>
/// <param name="SchemaVersion">Cut payload schema 版本 / Cut payload schema version.</param>
/// <param name="Validity">有效性 / Validity.</param>
/// <param name="Provenance">稳定来源 / Stable provenance.</param>
/// <param name="Payload">基础类型 payload / Primitive payload.</param>
public sealed record PortableConstraintProgrammingCut(
    [property: JsonPropertyName("id"), JsonPropertyOrder(0)] string Id,
    [property: JsonPropertyName("schemaVersion"), JsonPropertyOrder(1)] string SchemaVersion,
    [property: JsonPropertyName("validity"), JsonPropertyOrder(2)] string Validity,
    [property: JsonPropertyName("provenance"), JsonPropertyOrder(3)] IReadOnlyDictionary<string, string>? Provenance = null,
    [property: JsonPropertyName("payload"), JsonPropertyOrder(4)] IReadOnlyDictionary<string, string>? Payload = null);

/// <summary>恢复并复验的 checkpoint 数据 / Restored and verified checkpoint data.</summary>
/// <param name="Envelope">解码后的 envelope / Decoded envelope.</param>
/// <param name="Incumbent">复验后的 incumbent / Revalidated incumbent.</param>
/// <param name="Assumptions">已复验 assumption 标识 / Revalidated assumption identifiers.</param>
/// <param name="Conflicts">已复验冲突证据 / Revalidated conflict evidence.</param>
/// <param name="BestBound">历史最佳界 / Historical best bound.</param>
/// <param name="Gap">历史相对 gap / Historical relative gap.</param>
public sealed record ConstraintProgrammingCheckpointRestore(
    ConstraintProgrammingCheckpointEnvelope Envelope,
    ConstraintProgrammingSolution? Incumbent,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<PortableConstraintProgrammingConflict> Conflicts,
    string? BestBound,
    string? Gap);

/// <summary>取消事实线格式扩展 / Cancellation-fact wire extensions.</summary>
public static class PortableCancellationRecordExtensions {
    /// <summary>转换为线格式并保留未知来源代码 / Convert to wire format, preserving unknown origin text.</summary>
    /// <param name="record">取消事实 / Cancellation fact.</param>
    /// <returns>线格式记录 / Wire record.</returns>
    public static PortableCancellationRecord ToPortableCancellationRecord(
        this CancellationRecord record) {
        ArgumentNullException.ThrowIfNull(record);
        return new PortableCancellationRecord(
            Origin: record.WireOrigin ?? record.Source.ToWireCode(),
            RequestedAtEpochMs: record.RequestedAt.ToUnixTimeMilliseconds(),
            Reason: record.Reason);
    }

    /// <summary>解析线格式并原样保留未知来源代码 / Parse wire format while preserving unknown origin text.</summary>
    /// <param name="record">线格式记录 / Wire record.</param>
    /// <returns>取消事实 / Cancellation fact.</returns>
    public static CancellationRecord ToCancellationRecord(
        this PortableCancellationRecord record) {
        ArgumentNullException.ThrowIfNull(record);
        CancellationSource source = CancellationSourceExtensions.FromWireCode(record.Origin);
        return new CancellationRecord(
            Source: source,
            RequestedAt: DateTimeOffset.FromUnixTimeMilliseconds(record.RequestedAtEpochMs),
            Reason: record.Reason,
            WireOrigin: source == CancellationSource.Other ? record.Origin : null);
    }
}

/// <summary>CP checkpoint envelope 的编码、迁移与复验器 / CP checkpoint envelope codec.</summary>
public static class ConstraintProgrammingCheckpointCodec {
    /// <summary>当前 envelope schema / Current envelope schema.</summary>
    public const string CurrentSchema = "3.0";

    private const string SourceFormatV2 = "v2";
    private const string SourceFormatLegacyV1 = "legacy-v1";
    private const string LegacyCheckpointId = "legacy-v1";
    private const string MigratedLegacyCheckpointId = "legacy-v1-migrated";

    private static readonly HashSet<string> V2MarkerFields = new(
        new[] {
            "schemaVersion",
            "sourceFormat",
            "migratedFromLegacy",
            "checkpointId",
            "identitySchemaVersion",
            "identityNamespace",
            "modelFingerprint",
            "configurationFingerprint",
            "solverFingerprint",
            "runId",
            "attemptId",
            "parentCheckpointId",
            "createdAtEpochMs",
            "incumbent",
            "bestBound",
            "gap",
            "assumptions",
            "conflicts",
            "benders",
            "cancellationChain",
            "provenance",
            "integritySha256",
        },
        StringComparer.Ordinal);

    private static readonly HashSet<string> LegacyV1Fields = new(
        new[] { "schema", "modelName", "solverId", "snapshotJson" },
        StringComparer.Ordinal);

    private static readonly JsonSerializerOptions JsonOptions =
        ConstraintProgrammingSnapshotCodec.JsonOptions;

    /// <summary>从 snapshot 与可选 incumbent 捕获 envelope / Capture an envelope from a snapshot and optional incumbent.</summary>
    /// <param name="snapshot">CP 模型 snapshot / CP model snapshot.</param>
    /// <param name="descriptor">求解器描述符 / Solver descriptor.</param>
    /// <param name="checkpointId">Checkpoint 标识 / Checkpoint identifier.</param>
    /// <param name="createdAtEpochMs">创建时间 / Creation time.</param>
    /// <param name="incumbent">可选 incumbent / Optional incumbent.</param>
    /// <param name="configurationFingerprint">配置指纹 / Configuration fingerprint.</param>
    /// <param name="runId">运行标识 / Run identifier.</param>
    /// <param name="attemptId">尝试标识 / Attempt identifier.</param>
    /// <param name="parentCheckpointId">父 checkpoint 标识 / Parent checkpoint identifier.</param>
    /// <param name="bestBound">历史最佳界 / Historical best bound.</param>
    /// <param name="gap">历史相对 gap / Historical relative gap.</param>
    /// <param name="assumptions">已激活 assumption 变量 ID / Activated assumption variable identifiers.</param>
    /// <param name="conflicts">冲突证据 / Conflict evidence.</param>
    /// <param name="benders">Benders 状态 / Benders state.</param>
    /// <param name="cancellationChain">取消事实链 / Cancellation chain.</param>
    /// <param name="provenance">求解器执行来源 / Solver execution provenance.</param>
    /// <param name="metadata">额外元数据 / Additional metadata.</param>
    /// <returns>Envelope 或结构化错误 / Envelope or a structured error.</returns>
    public static Result<
        ConstraintProgrammingCheckpointEnvelope,
        ErrorCode,
        Error<ErrorCode>> Capture(
        ConstraintProgrammingModelSnapshot snapshot,
        SolverDescriptor descriptor,
        string checkpointId,
        long createdAtEpochMs,
        ConstraintProgrammingSolution? incumbent = null,
        string? configurationFingerprint = null,
        string? runId = null,
        string? attemptId = null,
        string? parentCheckpointId = null,
        string? bestBound = null,
        string? gap = null,
        IReadOnlyList<string>? assumptions = null,
        IReadOnlyList<PortableConstraintProgrammingConflict>? conflicts = null,
        PortableConstraintProgrammingBendersState? benders = null,
        IReadOnlyList<CancellationRecord>? cancellationChain = null,
        PortableSolverProvenance? provenance = null,
        IReadOnlyDictionary<string, string>? metadata = null) {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(descriptor);
        string? parentFailure = ParentLinkFailure(
            checkpointId: checkpointId,
            parentCheckpointId: parentCheckpointId,
            validateParentLink: true,
            expectedParentCheckpointId: null);
        if (parentFailure is not null) {
            return Failure<ConstraintProgrammingCheckpointEnvelope>(
                ErrorCode.IllegalArgument,
                parentFailure);
        }
        Result<string, ErrorCode, Error<ErrorCode>> encoded =
            ConstraintProgrammingSnapshotCodec.Encode(snapshot);
        if (encoded.IsFailed) {
            return encoded.Map<ConstraintProgrammingCheckpointEnvelope>(_ => default!);
        }
        PortableConstraintProgrammingIncumbent? portableIncumbent = null;
        if (incumbent is not null) {
            Result<
                PortableConstraintProgrammingIncumbent,
                ErrorCode,
                Error<ErrorCode>> encodedIncumbent =
                ToPortable(incumbent, snapshot);
            if (encodedIncumbent.IsFailed) {
                return encodedIncumbent.Map<ConstraintProgrammingCheckpointEnvelope>(
                    _ => default!);
            }
            portableIncumbent = encodedIncumbent.Value;
        }
        string? bendersFailure = ValidateBendersState(benders);
        if (bendersFailure is not null) {
            return Failure<ConstraintProgrammingCheckpointEnvelope>(
                ErrorCode.IllegalArgument,
                bendersFailure);
        }
        var chain = (cancellationChain
                ?? Array.Empty<CancellationRecord>())
            .Select(record => record.ToPortableCancellationRecord())
            .ToList();
        string? cancellationFailure = ValidateCancellationChain(chain);
        if (cancellationFailure is not null) {
            return Failure<ConstraintProgrammingCheckpointEnvelope>(
                ErrorCode.IllegalArgument,
                cancellationFailure);
        }
        string? provenanceFailure = ValidateProvenance(provenance);
        if (provenanceFailure is not null) {
            return Failure<ConstraintProgrammingCheckpointEnvelope>(
                ErrorCode.IllegalArgument,
                provenanceFailure);
        }
        var envelope = new ConstraintProgrammingCheckpointEnvelope(
            SchemaVersion: CurrentSchema,
            SourceFormat: SourceFormatV2,
            MigratedFromLegacy: false,
            CheckpointId: checkpointId,
            IdentitySchemaVersion: snapshot.IdentitySchemaVersion,
            IdentityNamespace: snapshot.IdentityNamespace,
            ModelName: snapshot.Name,
            ModelFingerprint: SolveFingerprinting.Sha256(encoded.Value).Value,
            ConfigurationFingerprint: configurationFingerprint,
            SolverFingerprint: SolveFingerprinting.Sha256(
                $"{descriptor.SolverId}|{descriptor.BackendName}|{descriptor.BackendVersion ?? string.Empty}")
                .Value,
            RunId: runId,
            AttemptId: attemptId,
            ParentCheckpointId: parentCheckpointId,
            CreatedAtEpochMs: createdAtEpochMs,
            SnapshotJson: encoded.Value,
            Incumbent: portableIncumbent,
            BestBound: bestBound,
            Gap: gap,
            Assumptions: (assumptions ?? Array.Empty<string>()).ToList(),
            Conflicts: (conflicts
                ?? Array.Empty<PortableConstraintProgrammingConflict>()).ToList(),
            Benders: benders,
            CancellationChain: chain,
            Provenance: provenance,
            Metadata: metadata ?? new Dictionary<string, string>(),
            IntegritySha256: string.Empty);
        ConstraintProgrammingCheckpointEnvelope normalized = WithDigest(envelope);
        Result<
            ConstraintProgrammingCheckpointRestore,
            ErrorCode,
            Error<ErrorCode>> restored = Restore(
                envelope: normalized,
                snapshot: snapshot,
                expectedConfigurationFingerprint: configurationFingerprint,
                expectedSolverFingerprint: normalized.SolverFingerprint,
                allowLegacyConfigurationFingerprint:
                    string.IsNullOrWhiteSpace(configurationFingerprint));
        return restored.IsOk
            ? Results.Ok(normalized)
            : restored.Map<ConstraintProgrammingCheckpointEnvelope>(_ => default!);
    }

    /// <summary>编码 envelope 并刷新完整性摘要 / Encode an envelope and refresh its integrity digest.</summary>
    /// <param name="envelope">Checkpoint envelope.</param>
    /// <returns>规范 JSON 或结构化错误 / Canonical JSON or a structured error.</returns>
    public static Result<string, ErrorCode, Error<ErrorCode>> Encode(
        ConstraintProgrammingCheckpointEnvelope envelope) {
        ArgumentNullException.ThrowIfNull(envelope);
        try {
            Result<
                ConstraintProgrammingCheckpointEnvelope,
                ErrorCode,
                Error<ErrorCode>> canonical =
                CanonicalizeSnapshotEnvelope(envelope);
            if (canonical.IsFailed) {
                return canonical.Map<string>(_ => string.Empty);
            }
            bool migrated = canonical.Value.SourceFormat == SourceFormatLegacyV1 ||
                canonical.Value.MigratedFromLegacy;
            ConstraintProgrammingCheckpointEnvelope normalized = WithDigest(
                canonical.Value with {
                    SourceFormat = SourceFormatV2,
                    MigratedFromLegacy = migrated,
                    CheckpointId = migrated
                        ? MigratedLegacyCheckpointId
                        : canonical.Value.CheckpointId,
                });
            return Results.Ok(JsonSerializer.Serialize(normalized, JsonOptions));
        }
        catch (Exception error) {
            return Failure<string>(
                ErrorCode.Other,
                $"CP checkpoint 编码失败：{error.Message} / " +
                $"CP checkpoint encoding failed: {error.Message}");
        }
    }

    /// <summary>严格解码并复验 schema 3.0 envelope / Strictly decode and verify a schema 3.0 envelope.</summary>
    /// <param name="encoded">Checkpoint JSON.</param>
    /// <returns>规范 envelope 或结构化错误 / Canonical envelope or a structured error.</returns>
    public static Result<
        ConstraintProgrammingCheckpointEnvelope,
        ErrorCode,
        Error<ErrorCode>> Decode(string encoded) {
        ArgumentNullException.ThrowIfNull(encoded);
        try {
            ConstraintProgrammingCheckpointEnvelope? decoded =
                JsonSerializer.Deserialize<ConstraintProgrammingCheckpointEnvelope>(
                    encoded,
                    JsonOptions);
            if (decoded is null) {
                return Failure<ConstraintProgrammingCheckpointEnvelope>(
                    ErrorCode.IllegalArgument,
                    "CP checkpoint JSON 为空 / CP checkpoint JSON is empty");
            }
            ConstraintProgrammingCheckpointEnvelope envelope =
                CanonicalizeMapKeys(decoded);
            if (envelope.SchemaVersion != CurrentSchema) {
                return Failure<ConstraintProgrammingCheckpointEnvelope>(
                    ErrorCode.Other,
                    $"不支持的 checkpoint schema：{envelope.SchemaVersion} / " +
                    $"Unsupported checkpoint schema: {envelope.SchemaVersion}");
            }
            if (envelope.SourceFormat != SourceFormatV2) {
                return Failure<ConstraintProgrammingCheckpointEnvelope>(
                    ErrorCode.IllegalArgument,
                    "checkpoint 来源格式无效 / Checkpoint source format is invalid");
            }
            string? identityFailure = CheckpointIdentityFailure(envelope);
            if (identityFailure is not null) {
                return Failure<ConstraintProgrammingCheckpointEnvelope>(
                    ErrorCode.IllegalArgument,
                    identityFailure);
            }
            if (string.IsNullOrWhiteSpace(envelope.IntegritySha256) ||
                !string.Equals(
                    envelope.IntegritySha256,
                    Digest(envelope with { IntegritySha256 = string.Empty }),
                    StringComparison.Ordinal)) {
                return Failure<ConstraintProgrammingCheckpointEnvelope>(
                    ErrorCode.IllegalArgument,
                    "checkpoint 完整性摘要不匹配 / Checkpoint integrity digest mismatch");
            }
            string? parentFailure = ParentLinkFailure(
                checkpointId: envelope.CheckpointId,
                parentCheckpointId: envelope.ParentCheckpointId,
                validateParentLink: true,
                expectedParentCheckpointId: null);
            if (parentFailure is not null) {
                return Failure<ConstraintProgrammingCheckpointEnvelope>(
                    ErrorCode.IllegalArgument,
                    parentFailure);
            }
            if (!string.Equals(
                    SolveFingerprinting.Sha256(envelope.SnapshotJson).Value,
                    envelope.ModelFingerprint,
                    StringComparison.Ordinal)) {
                return Failure<ConstraintProgrammingCheckpointEnvelope>(
                    ErrorCode.IllegalArgument,
                    "checkpoint snapshot 与模型指纹不一致 / " +
                    "Checkpoint snapshot disagrees with model fingerprint");
            }
            Try valid = ConstraintProgrammingSnapshotCodec.Validate(envelope.SnapshotJson);
            return valid.IsOk
                ? Results.Ok(envelope)
                : Failure<ConstraintProgrammingCheckpointEnvelope>(
                    ErrorCode.IllegalArgument,
                    "checkpoint snapshot 结构无效 / Checkpoint snapshot is invalid");
        }
        catch (Exception error) {
            return Failure<ConstraintProgrammingCheckpointEnvelope>(
                ErrorCode.IllegalArgument,
                $"CP checkpoint 解码失败：{error.Message} / " +
                $"CP checkpoint decoding failed: {error.Message}");
        }
    }

    /// <summary>宽松解码 schema 3.0、schema 2.0 或 legacy v1 / Leniently decode schema 3.0, schema 2.0, or legacy v1.</summary>
    /// <param name="encoded">Checkpoint JSON.</param>
    /// <returns>已验证的兼容 envelope 或结构化错误 / Verified compatible envelope or a structured error.</returns>
    public static Result<
        ConstraintProgrammingCheckpointEnvelope,
        ErrorCode,
        Error<ErrorCode>> DecodeCompatible(string encoded) {
        ArgumentNullException.ThrowIfNull(encoded);
        JsonDocument document;
        try {
            document = JsonDocument.Parse(encoded);
        }
        catch (Exception error) {
            return Failure<ConstraintProgrammingCheckpointEnvelope>(
                ErrorCode.IllegalArgument,
                $"checkpoint JSON 无效：{error.Message} / " +
                $"Checkpoint JSON is invalid: {error.Message}");
        }
        using (document) {
            Result<
                ConstraintProgrammingCheckpointEnvelope,
                ErrorCode,
                Error<ErrorCode>> current = Decode(encoded);
            if (current.IsOk) {
                return CanonicalizeSnapshotEnvelope(current.Value);
            }
            Result<
                ConstraintProgrammingCheckpointEnvelope,
                ErrorCode,
                Error<ErrorCode>>? legacyV2 =
                DecodeLegacyV2(encoded);
            if (legacyV2 is { IsOk: true }) {
                string? parentFailure = ParentLinkFailure(
                    checkpointId: legacyV2.Value.CheckpointId,
                    parentCheckpointId: legacyV2.Value.ParentCheckpointId,
                    validateParentLink: true,
                    expectedParentCheckpointId: null);
                return parentFailure is null
                    ? legacyV2
                    : Failure<ConstraintProgrammingCheckpointEnvelope>(
                        ErrorCode.IllegalArgument,
                        parentFailure);
            }
            var keys = document.RootElement
                .EnumerateObject()
                .Select(property => property.Name)
                .ToHashSet(StringComparer.Ordinal);
            if (keys.Any(V2MarkerFields.Contains)) {
                string reason = current is Failed<
                    ConstraintProgrammingCheckpointEnvelope,
                    ErrorCode,
                    Error<ErrorCode>> failed
                    ? failed.Error.Message
                    : string.Empty;
                return Failure<ConstraintProgrammingCheckpointEnvelope>(
                    ErrorCode.IllegalArgument,
                    "损坏的 v2 checkpoint 不得降级为 legacy / " +
                    "A malformed v2 checkpoint must not downgrade to legacy" +
                    (string.IsNullOrWhiteSpace(reason) ? string.Empty : $"：{reason}"));
            }
            if (keys.Any(key => !LegacyV1Fields.Contains(key))) {
                return Failure<ConstraintProgrammingCheckpointEnvelope>(
                    ErrorCode.IllegalArgument,
                    "legacy checkpoint 包含未知字段 / Legacy checkpoint contains unknown fields");
            }
            return DecodeLegacyV1(document.RootElement);
        }
    }

    /// <summary>刷新完整性摘要 / Refresh the integrity digest.</summary>
    /// <param name="envelope">Checkpoint envelope.</param>
    /// <returns>带有效摘要的 envelope / Envelope with a valid digest.</returns>
    public static ConstraintProgrammingCheckpointEnvelope WithIntegrity(
        ConstraintProgrammingCheckpointEnvelope envelope) {
        ArgumentNullException.ThrowIfNull(envelope);
        return WithDigest(envelope);
    }

    /// <summary>根据当前 snapshot 复验 checkpoint / Revalidate a checkpoint against the current snapshot.</summary>
    /// <param name="envelope">Checkpoint envelope.</param>
    /// <param name="snapshot">当前模型 snapshot / Current model snapshot.</param>
    /// <param name="expectedConfigurationFingerprint">当前配置指纹 / Current configuration fingerprint.</param>
    /// <param name="expectedSolverFingerprint">当前求解器指纹 / Current solver fingerprint.</param>
    /// <param name="allowLegacyConfigurationFingerprint">是否允许已独立复验的旧配置指纹 / Whether to allow an independently verified legacy configuration fingerprint.</param>
    /// <param name="allowLegacySolverFingerprint">是否允许已独立复验的旧求解器指纹 / Whether to allow an independently verified legacy solver fingerprint.</param>
    /// <param name="validateParentLink">是否校验父链 / Whether to validate the parent link.</param>
    /// <param name="expectedParentCheckpointId">期望父 checkpoint 标识 / Expected parent checkpoint identifier.</param>
    /// <param name="expectedRunId">期望运行标识 / Expected run identifier.</param>
    /// <param name="expectedAttemptId">期望尝试标识 / Expected attempt identifier.</param>
    /// <param name="expectedCancellationChain">期望取消链前缀 / Expected cancellation-chain prefix.</param>
    /// <param name="expectedProvenance">期望 provenance / Expected provenance.</param>
    /// <returns>恢复结果或结构化错误 / Restore result or a structured error.</returns>
    public static Result<
        ConstraintProgrammingCheckpointRestore,
        ErrorCode,
        Error<ErrorCode>> Restore(
        ConstraintProgrammingCheckpointEnvelope envelope,
        ConstraintProgrammingModelSnapshot snapshot,
        string? expectedConfigurationFingerprint,
        string? expectedSolverFingerprint,
        bool allowLegacyConfigurationFingerprint = false,
        bool allowLegacySolverFingerprint = false,
        bool validateParentLink = false,
        string? expectedParentCheckpointId = null,
        string? expectedRunId = null,
        string? expectedAttemptId = null,
        IReadOnlyList<PortableCancellationRecord>? expectedCancellationChain = null,
        PortableSolverProvenance? expectedProvenance = null) {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(snapshot);
        try {
            return RestoreCore(
                envelope: envelope,
                snapshot: snapshot,
                expectedConfigurationFingerprint: expectedConfigurationFingerprint,
                expectedSolverFingerprint: expectedSolverFingerprint,
                allowLegacyConfigurationFingerprint: allowLegacyConfigurationFingerprint,
                allowLegacySolverFingerprint: allowLegacySolverFingerprint,
                validateParentLink: validateParentLink,
                expectedParentCheckpointId: expectedParentCheckpointId,
                expectedRunId: expectedRunId,
                expectedAttemptId: expectedAttemptId,
                expectedCancellationChain: expectedCancellationChain,
                expectedProvenance: expectedProvenance);
        }
        catch (Exception error) {
            return Failure<ConstraintProgrammingCheckpointRestore>(
                ErrorCode.IllegalArgument,
                $"CP checkpoint 恢复失败：{error.Message} / " +
                $"CP checkpoint restore failed: {error.Message}");
        }
    }

    /// <summary>校验子 checkpoint 是否正确指向源 checkpoint / Validate that a resumed child points at its source.</summary>
    /// <param name="parent">源 checkpoint / Source checkpoint.</param>
    /// <param name="child">恢复出的子 checkpoint / Resumed child checkpoint.</param>
    /// <returns>失败消息或 null / Failure message or null.</returns>
    public static string? ValidateResumedChild(
        ConstraintProgrammingCheckpointEnvelope parent,
        ConstraintProgrammingCheckpointEnvelope child) {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(child);
        if (!string.Equals(child.RunId, parent.RunId, StringComparison.Ordinal)) {
            return "恢复出的子 checkpoint 不属于源运行 / " +
                "Resumed child checkpoint does not belong to the source run";
        }
        if (!string.Equals(
                child.ParentCheckpointId,
                parent.CheckpointId,
                StringComparison.Ordinal)) {
            return "恢复出的子 checkpoint 的父链接与源 checkpoint 不一致 / " +
                "Resumed child checkpoint parent does not match the source checkpoint";
        }
        if (!string.Equals(child.ModelFingerprint, parent.ModelFingerprint, StringComparison.Ordinal) ||
            !string.Equals(
                child.ConfigurationFingerprint,
                parent.ConfigurationFingerprint,
                StringComparison.Ordinal) ||
            !string.Equals(child.SolverFingerprint, parent.SolverFingerprint, StringComparison.Ordinal) ||
            !ProvenanceEquals(child.Provenance, parent.Provenance)) {
            return "恢复出的子 checkpoint 未保持源身份 / " +
                "Resumed child checkpoint does not preserve source identity";
        }
        IReadOnlyList<PortableCancellationRecord> parentChain =
            parent.CancellationChain ?? Array.Empty<PortableCancellationRecord>();
        IReadOnlyList<PortableCancellationRecord> childChain =
            child.CancellationChain ?? Array.Empty<PortableCancellationRecord>();
        if (childChain.Count < parentChain.Count ||
            !childChain.Take(parentChain.Count).SequenceEqual(parentChain)) {
            return "恢复出的子 checkpoint 丢失了源的取消历史 / " +
                "Resumed child checkpoint lost source cancellation history";
        }
        return null;
    }

    /// <summary>派生下一次 attempt 的 checkpoint / Derive the next attempt checkpoint.</summary>
    /// <param name="parent">源 checkpoint / Source checkpoint.</param>
    /// <param name="checkpointId">子 checkpoint 标识 / Child checkpoint identifier.</param>
    /// <param name="createdAtEpochMs">子创建时间 / Child creation time.</param>
    /// <param name="attemptId">子尝试标识 / Child attempt identifier.</param>
    /// <returns>子 envelope 或结构化错误 / Child envelope or a structured error.</returns>
    public static Result<
        ConstraintProgrammingCheckpointEnvelope,
        ErrorCode,
        Error<ErrorCode>> ForkForResume(
        ConstraintProgrammingCheckpointEnvelope parent,
        string checkpointId,
        long createdAtEpochMs,
        string? attemptId = null) {
        ArgumentNullException.ThrowIfNull(parent);
        if (string.IsNullOrWhiteSpace(checkpointId)) {
            return Failure<ConstraintProgrammingCheckpointEnvelope>(
                ErrorCode.IllegalArgument,
                "fork 出的 checkpoint 标识不能为空白 / " +
                "A forked checkpoint identifier must not be blank");
        }
        if (string.Equals(checkpointId, parent.CheckpointId, StringComparison.Ordinal)) {
            return Failure<ConstraintProgrammingCheckpointEnvelope>(
                ErrorCode.IllegalArgument,
                "fork 出的 checkpoint 标识不能与源相同 / " +
                "A forked checkpoint identifier must differ from its source");
        }
        ConstraintProgrammingCheckpointEnvelope child = WithIntegrity(parent with {
            CheckpointId = checkpointId,
            AttemptId = attemptId ?? parent.AttemptId,
            ParentCheckpointId = parent.CheckpointId,
            CreatedAtEpochMs = createdAtEpochMs,
        });
        string? validation = ValidateResumedChild(parent, child);
        return validation is null
            ? Results.Ok(child)
            : Failure<ConstraintProgrammingCheckpointEnvelope>(
                ErrorCode.IllegalArgument,
                validation);
    }

    private static Result<
        ConstraintProgrammingCheckpointRestore,
        ErrorCode,
        Error<ErrorCode>> RestoreCore(
        ConstraintProgrammingCheckpointEnvelope envelope,
        ConstraintProgrammingModelSnapshot snapshot,
        string? expectedConfigurationFingerprint,
        string? expectedSolverFingerprint,
        bool allowLegacyConfigurationFingerprint,
        bool allowLegacySolverFingerprint,
        bool validateParentLink,
        string? expectedParentCheckpointId,
        string? expectedRunId,
        string? expectedAttemptId,
        IReadOnlyList<PortableCancellationRecord>? expectedCancellationChain,
        PortableSolverProvenance? expectedProvenance) {
        if (envelope.SourceFormat is not (SourceFormatV2 or SourceFormatLegacyV1)) {
            return Failure<ConstraintProgrammingCheckpointRestore>(
                ErrorCode.IllegalArgument,
                "checkpoint 来源格式未知 / Checkpoint source format is unknown");
        }
        if (envelope.SourceFormat == SourceFormatV2 &&
            envelope.SchemaVersion != CurrentSchema) {
            return Failure<ConstraintProgrammingCheckpointRestore>(
                ErrorCode.Other,
                $"不支持的 checkpoint schema：{envelope.SchemaVersion} / " +
                $"Unsupported checkpoint schema: {envelope.SchemaVersion}");
        }
        if (envelope.SourceFormat == SourceFormatLegacyV1 &&
            envelope.CheckpointId != LegacyCheckpointId) {
            return Failure<ConstraintProgrammingCheckpointRestore>(
                ErrorCode.IllegalArgument,
                "legacy checkpoint 来源标识无效 / Legacy checkpoint source identifier is invalid");
        }
        if (envelope.SourceFormat == SourceFormatV2 &&
            envelope.CheckpointId == LegacyCheckpointId) {
            return Failure<ConstraintProgrammingCheckpointRestore>(
                ErrorCode.IllegalArgument,
                "v2 checkpoint 不能伪装为 legacy-v1 / " +
                "A v2 checkpoint must not masquerade as legacy-v1");
        }
        bool migratedLegacyV2 =
            envelope.SourceFormat == SourceFormatV2 && envelope.MigratedFromLegacy;
        if (migratedLegacyV2 && envelope.CheckpointId != MigratedLegacyCheckpointId) {
            return Failure<ConstraintProgrammingCheckpointRestore>(
                ErrorCode.IllegalArgument,
                "legacy 迁移 checkpoint 标识无效 / Migrated legacy checkpoint identifier is invalid");
        }
        string? parentFailure = ParentLinkFailure(
            checkpointId: envelope.CheckpointId,
            parentCheckpointId: envelope.ParentCheckpointId,
            validateParentLink: validateParentLink || expectedParentCheckpointId is not null,
            expectedParentCheckpointId: expectedParentCheckpointId);
        if (parentFailure is not null) {
            return Failure<ConstraintProgrammingCheckpointRestore>(
                ErrorCode.IllegalArgument,
                parentFailure);
        }
        if (expectedProvenance is not null &&
            (string.IsNullOrWhiteSpace(expectedProvenance.SolverId) ||
                string.IsNullOrWhiteSpace(expectedProvenance.BackendName))) {
            return Failure<ConstraintProgrammingCheckpointRestore>(
                ErrorCode.IllegalArgument,
                "恢复请求的 provenance 身份不能为空白 / " +
                "Resume request provenance identity cannot be blank");
        }
        if (expectedRunId is not null) {
            if (string.IsNullOrWhiteSpace(expectedRunId)) {
                return Failure<ConstraintProgrammingCheckpointRestore>(
                    ErrorCode.IllegalArgument,
                    "期望的 checkpoint 运行标识不能为空白 / " +
                    "Expected checkpoint run identifier must not be blank");
            }
            if (!string.Equals(envelope.RunId, expectedRunId, StringComparison.Ordinal)) {
                return Failure<ConstraintProgrammingCheckpointRestore>(
                    ErrorCode.IllegalArgument,
                    "checkpoint 运行标识与恢复请求不一致 / " +
                    "Checkpoint run identity does not match the resume request");
            }
        }
        if (expectedAttemptId is not null) {
            if (string.IsNullOrWhiteSpace(expectedAttemptId)) {
                return Failure<ConstraintProgrammingCheckpointRestore>(
                    ErrorCode.IllegalArgument,
                    "期望的 checkpoint 尝试标识不能为空白 / " +
                    "Expected checkpoint attempt identifier must not be blank");
            }
            if (!string.Equals(envelope.AttemptId, expectedAttemptId, StringComparison.Ordinal)) {
                return Failure<ConstraintProgrammingCheckpointRestore>(
                    ErrorCode.IllegalArgument,
                    "checkpoint 尝试标识与恢复请求不一致 / " +
                    "Checkpoint attempt identity does not match the resume request");
            }
        }
        if (envelope.SourceFormat == SourceFormatV2 && !migratedLegacyV2) {
            if ((!allowLegacyConfigurationFingerprint &&
                    string.IsNullOrWhiteSpace(expectedConfigurationFingerprint)) ||
                (!allowLegacySolverFingerprint &&
                    string.IsNullOrWhiteSpace(expectedSolverFingerprint))) {
                return Failure<ConstraintProgrammingCheckpointRestore>(
                    ErrorCode.IllegalArgument,
                    "恢复 v2 checkpoint 必须提供当前配置和求解器指纹 / " +
                    "Current configuration and solver fingerprints are required to restore a V2 checkpoint");
            }
            if (string.IsNullOrWhiteSpace(envelope.SolverFingerprint) ||
                (!allowLegacySolverFingerprint &&
                    !string.Equals(
                        envelope.SolverFingerprint,
                        expectedSolverFingerprint,
                        StringComparison.Ordinal))) {
                return Failure<ConstraintProgrammingCheckpointRestore>(
                    ErrorCode.IllegalArgument,
                    "checkpoint 求解器指纹缺失或不匹配 / " +
                    "Checkpoint solver fingerprint is missing or mismatched");
            }
            if (!allowLegacyConfigurationFingerprint &&
                !string.Equals(
                    envelope.ConfigurationFingerprint,
                    expectedConfigurationFingerprint,
                    StringComparison.Ordinal)) {
                return Failure<ConstraintProgrammingCheckpointRestore>(
                    ErrorCode.IllegalArgument,
                    "checkpoint 配置指纹缺失或不匹配 / " +
                    "Checkpoint configuration fingerprint is missing or mismatched");
            }
        }
        if (expectedProvenance is not null &&
            !ProvenanceEquals(envelope.Provenance, expectedProvenance)) {
            return Failure<ConstraintProgrammingCheckpointRestore>(
                ErrorCode.IllegalArgument,
                "checkpoint provenance 与恢复请求不一致 / " +
                "Checkpoint provenance does not match the resume request");
        }
        IReadOnlyList<PortableCancellationRecord> envelopeChain =
            envelope.CancellationChain ?? Array.Empty<PortableCancellationRecord>();
        if (expectedCancellationChain is not null &&
            (expectedCancellationChain.Count > envelopeChain.Count ||
                !envelopeChain
                    .Take(expectedCancellationChain.Count)
                    .SequenceEqual(expectedCancellationChain))) {
            return Failure<ConstraintProgrammingCheckpointRestore>(
                ErrorCode.IllegalArgument,
                "checkpoint 取消链未保留期望前缀 / " +
                "Checkpoint cancellation chain does not preserve the expected prefix");
        }
        if (string.IsNullOrWhiteSpace(envelope.IntegritySha256) ||
            !string.Equals(
                envelope.IntegritySha256,
                Digest(envelope with { IntegritySha256 = string.Empty }),
                StringComparison.Ordinal)) {
            return Failure<ConstraintProgrammingCheckpointRestore>(
                ErrorCode.IllegalArgument,
                "checkpoint 完整性摘要无效 / Checkpoint integrity digest is invalid");
        }
        if (!string.Equals(
                SolveFingerprinting.Sha256(envelope.SnapshotJson).Value,
                envelope.ModelFingerprint,
                StringComparison.Ordinal)) {
            return Failure<ConstraintProgrammingCheckpointRestore>(
                ErrorCode.IllegalArgument,
                "checkpoint snapshot 与原始模型指纹不一致 / " +
                "Checkpoint snapshot disagrees with its raw model fingerprint");
        }
        Result<
            ConstraintProgrammingCheckpointEnvelope,
            ErrorCode,
            Error<ErrorCode>> canonical =
            CanonicalizeSnapshotEnvelope(envelope);
        if (canonical.IsFailed) {
            return canonical.Map<ConstraintProgrammingCheckpointRestore>(_ => default!);
        }
        ConstraintProgrammingCheckpointEnvelope normalized = canonical.Value;
        Result<string, ErrorCode, Error<ErrorCode>> encodedSnapshot =
            ConstraintProgrammingSnapshotCodec.Encode(snapshot);
        if (encodedSnapshot.IsFailed) {
            return encodedSnapshot.Map<ConstraintProgrammingCheckpointRestore>(_ => default!);
        }
        string fingerprint = SolveFingerprinting.Sha256(encodedSnapshot.Value).Value;
        if (!string.Equals(fingerprint, normalized.ModelFingerprint, StringComparison.Ordinal) ||
            !string.Equals(
                snapshot.IdentitySchemaVersion,
                normalized.IdentitySchemaVersion,
                StringComparison.Ordinal) ||
            !string.Equals(
                snapshot.IdentityNamespace,
                normalized.IdentityNamespace,
                StringComparison.Ordinal) ||
            !string.Equals(
                normalized.SnapshotJson,
                encodedSnapshot.Value,
                StringComparison.Ordinal)) {
            return Failure<ConstraintProgrammingCheckpointRestore>(
                ErrorCode.IllegalArgument,
                "checkpoint 与模型身份或指纹不匹配 / " +
                "Checkpoint model identity or fingerprint mismatch");
        }
        var variableIds = snapshot.Variables
            .Select(variable => variable.Id.Value)
            .ToHashSet(StringComparer.Ordinal);
        var constraintIds = snapshot.Constraints
            .Select(constraint => constraint.Id.Value)
            .ToHashSet(StringComparer.Ordinal);
        IReadOnlyList<string> assumptions =
            normalized.Assumptions ?? Array.Empty<string>();
        if (assumptions.Any(id => !variableIds.Contains(id))) {
            return Failure<ConstraintProgrammingCheckpointRestore>(
                ErrorCode.IllegalArgument,
                "checkpoint assumption 引用了未知变量 / " +
                "Checkpoint assumption references an unknown variable");
        }
        IReadOnlyList<PortableConstraintProgrammingConflict> conflicts =
            normalized.Conflicts ?? Array.Empty<PortableConstraintProgrammingConflict>();
        foreach (PortableConstraintProgrammingConflict conflict in conflicts) {
            string? conflictFailure = ValidateConflict(
                conflict: conflict,
                variableIds: variableIds,
                constraintIds: constraintIds);
            if (conflictFailure is not null) {
                return Failure<ConstraintProgrammingCheckpointRestore>(
                    ErrorCode.IllegalArgument,
                    conflictFailure);
            }
        }
        string? bendersFailure = ValidateBendersState(normalized.Benders);
        if (bendersFailure is not null) {
            return Failure<ConstraintProgrammingCheckpointRestore>(
                ErrorCode.IllegalArgument,
                bendersFailure);
        }
        string? cancellationFailure = ValidateCancellationChain(envelopeChain);
        if (cancellationFailure is not null) {
            return Failure<ConstraintProgrammingCheckpointRestore>(
                ErrorCode.IllegalArgument,
                cancellationFailure);
        }
        string? provenanceFailure = ValidateProvenance(normalized.Provenance);
        if (provenanceFailure is not null) {
            return Failure<ConstraintProgrammingCheckpointRestore>(
                ErrorCode.IllegalArgument,
                provenanceFailure);
        }
        string? boundsFailure = ValidateHistoricalBounds(
            incumbent: normalized.Incumbent,
            bestBound: normalized.BestBound,
            gap: normalized.Gap);
        if (boundsFailure is not null) {
            return Failure<ConstraintProgrammingCheckpointRestore>(
                ErrorCode.IllegalArgument,
                boundsFailure);
        }
        Result<
            ConstraintProgrammingSolution?,
            ErrorCode,
            Error<ErrorCode>> restoredIncumbent =
            ToSolution(normalized.Incumbent, snapshot);
        if (restoredIncumbent.IsFailed) {
            return restoredIncumbent.Map<ConstraintProgrammingCheckpointRestore>(_ => default!);
        }
        string? restoredBestBound =
            allowLegacyConfigurationFingerprint || migratedLegacyV2
                ? null
                : normalized.BestBound;
        string? restoredGap =
            allowLegacyConfigurationFingerprint || migratedLegacyV2
                ? null
                : normalized.Gap;
        return Results.Ok(new ConstraintProgrammingCheckpointRestore(
            Envelope: normalized,
            Incumbent: restoredIncumbent.Value,
            Assumptions: assumptions,
            Conflicts: conflicts,
            BestBound: restoredBestBound,
            Gap: restoredGap));
    }

    private static string? CheckpointIdentityFailure(
        ConstraintProgrammingCheckpointEnvelope envelope) {
        if (!envelope.MigratedFromLegacy && envelope.CheckpointId == LegacyCheckpointId) {
            return "v2 checkpoint 不能伪装为 legacy-v1 / " +
                "A v2 checkpoint must not masquerade as legacy-v1";
        }
        if (envelope.MigratedFromLegacy &&
            envelope.CheckpointId != MigratedLegacyCheckpointId) {
            return "legacy 迁移 checkpoint 标识无效 / " +
                "Migrated legacy checkpoint identifier is invalid";
        }
        return null;
    }

    private static Result<
        ConstraintProgrammingCheckpointEnvelope,
        ErrorCode,
        Error<ErrorCode>> CanonicalizeSnapshotEnvelope(
        ConstraintProgrammingCheckpointEnvelope envelope) {
        Result<string, ErrorCode, Error<ErrorCode>> canonical =
            ConstraintProgrammingSnapshotCodec.Canonicalize(envelope.SnapshotJson);
        if (canonical.IsFailed) {
            return canonical.Map<ConstraintProgrammingCheckpointEnvelope>(_ => default!);
        }
        try {
            using var snapshot =
                JsonDocument.Parse(canonical.Value);
            JsonElement root = snapshot.RootElement;
            ConstraintProgrammingCheckpointEnvelope normalized = envelope with {
                IdentitySchemaVersion = root.TryGetProperty(
                    "identitySchemaVersion",
                    out JsonElement identitySchema)
                    ? identitySchema.GetString() ?? envelope.IdentitySchemaVersion
                    : envelope.IdentitySchemaVersion,
                IdentityNamespace = root.TryGetProperty(
                    "identityNamespace",
                    out JsonElement identityNamespace)
                    ? identityNamespace.GetString() ?? envelope.IdentityNamespace
                    : envelope.IdentityNamespace,
                ModelName = root.TryGetProperty("name", out JsonElement name)
                    ? name.GetString() ?? envelope.ModelName
                    : envelope.ModelName,
                ModelFingerprint = SolveFingerprinting.Sha256(canonical.Value).Value,
                SnapshotJson = canonical.Value,
            };
            return Results.Ok(normalized == envelope
                ? envelope
                : WithDigest(normalized));
        }
        catch (Exception error) {
            return Failure<ConstraintProgrammingCheckpointEnvelope>(
                ErrorCode.IllegalArgument,
                $"checkpoint snapshot 迁移失败：{error.Message} / " +
                $"Checkpoint snapshot migration failed: {error.Message}");
        }
    }

    private static Result<
        ConstraintProgrammingCheckpointEnvelope,
        ErrorCode,
        Error<ErrorCode>>? DecodeLegacyV2(string encoded) {
        try {
            LegacyV2Envelope? legacy =
                JsonSerializer.Deserialize<LegacyV2Envelope>(encoded, JsonOptions);
            if (legacy is null) {
                return null;
            }
            Result<string, ErrorCode, Error<ErrorCode>> canonical =
                ConstraintProgrammingSnapshotCodec.Canonicalize(legacy.SnapshotJson);
            if (canonical.IsFailed) {
                return null;
            }
            using var snapshot = JsonDocument.Parse(canonical.Value);
            JsonElement root = snapshot.RootElement;
            if (legacy.SchemaVersion != "2.0" ||
                legacy.SourceFormat != SourceFormatV2 ||
                (!legacy.MigratedFromLegacy && legacy.CheckpointId == LegacyCheckpointId) ||
                (legacy.MigratedFromLegacy &&
                    legacy.CheckpointId != MigratedLegacyCheckpointId) ||
                !string.Equals(
                    SolveFingerprinting.Sha256(legacy.SnapshotJson).Value,
                    legacy.ModelFingerprint,
                    StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(legacy.IntegritySha256) ||
                !string.Equals(
                    legacy.IntegritySha256,
                    LegacyDigest(legacy with { IntegritySha256 = string.Empty }),
                    StringComparison.Ordinal)) {
                return null;
            }
            PortableConstraintProgrammingBendersState? benders =
                legacy.Benders is null
                    ? null
                    : new PortableConstraintProgrammingBendersState(
                        Iteration: legacy.Benders.Iteration,
                        MasterIncumbent: legacy.Benders.MasterIncumbent,
                        MasterBestBound: legacy.Benders.MasterBestBound,
                        Cuts: legacy.Benders.Cuts,
                        Trace: legacy.Benders.Trace,
                        Assumptions: legacy.Benders.Assumptions,
                        FixedBindings: legacy.Benders.FixedBindings,
                        Conflicts: legacy.Benders.Conflicts,
                        ConvergenceVerified: legacy.Benders.ConvergenceVerified,
                        MasterFingerprint: null,
                        SubproblemModelFingerprint:
                            legacy.Benders.SubproblemModelFingerprint);
            ConstraintProgrammingCheckpointEnvelope converted = WithDigest(
                new ConstraintProgrammingCheckpointEnvelope(
                    SchemaVersion: legacy.SchemaVersion,
                    SourceFormat: legacy.SourceFormat,
                    MigratedFromLegacy: legacy.MigratedFromLegacy,
                    CheckpointId: legacy.CheckpointId,
                    IdentitySchemaVersion: root.TryGetProperty(
                        "identitySchemaVersion",
                        out JsonElement identitySchema)
                        ? identitySchema.GetString() ?? legacy.IdentitySchemaVersion
                        : legacy.IdentitySchemaVersion,
                    IdentityNamespace: root.TryGetProperty(
                        "identityNamespace",
                        out JsonElement identityNamespace)
                        ? identityNamespace.GetString() ?? legacy.IdentityNamespace
                        : legacy.IdentityNamespace,
                    ModelName: root.TryGetProperty("name", out JsonElement name)
                        ? name.GetString() ?? legacy.ModelName
                        : legacy.ModelName,
                    ModelFingerprint: SolveFingerprinting.Sha256(canonical.Value).Value,
                    ConfigurationFingerprint: legacy.ConfigurationFingerprint,
                    SolverFingerprint: legacy.SolverFingerprint,
                    RunId: legacy.RunId,
                    AttemptId: legacy.AttemptId,
                    ParentCheckpointId: legacy.ParentCheckpointId,
                    CreatedAtEpochMs: legacy.CreatedAtEpochMs,
                    SnapshotJson: canonical.Value,
                    Incumbent: legacy.Incumbent,
                    BestBound: legacy.BestBound,
                    Gap: legacy.Gap,
                    Assumptions: legacy.Assumptions,
                    Conflicts: legacy.Conflicts,
                    Benders: benders,
                    CancellationChain: Array.Empty<PortableCancellationRecord>(),
                    Provenance: null,
                    Metadata: new Dictionary<string, string>(),
                    IntegritySha256: legacy.IntegritySha256));
            return Results.Ok(converted);
        }
        catch {
            return null;
        }
    }

    private static Result<
        ConstraintProgrammingCheckpointEnvelope,
        ErrorCode,
        Error<ErrorCode>> DecodeLegacyV1(JsonElement root) {
        if (!root.TryGetProperty("schema", out JsonElement schema) ||
            schema.ValueKind != JsonValueKind.Number ||
            !schema.TryGetInt32(out int schemaVersion) ||
            schemaVersion != 1) {
            return Failure<ConstraintProgrammingCheckpointEnvelope>(
                ErrorCode.IllegalArgument,
                "不支持的 checkpoint 格式 / Unsupported checkpoint format");
        }
        if (!root.TryGetProperty("snapshotJson", out JsonElement snapshotElement) ||
            snapshotElement.ValueKind != JsonValueKind.String) {
            return Failure<ConstraintProgrammingCheckpointEnvelope>(
                ErrorCode.IllegalArgument,
                "legacy checkpoint 缺少 snapshotJson / Legacy checkpoint is missing snapshotJson");
        }
        string snapshotJson = snapshotElement.GetString()!;
        Result<string, ErrorCode, Error<ErrorCode>> canonical =
            ConstraintProgrammingSnapshotCodec.Canonicalize(snapshotJson);
        if (canonical.IsFailed) {
            return Failure<ConstraintProgrammingCheckpointEnvelope>(
                ErrorCode.IllegalArgument,
                "legacy checkpoint snapshot 无效 / Legacy checkpoint snapshot is invalid");
        }
        using var snapshot = JsonDocument.Parse(canonical.Value);
        JsonElement snapshotRoot = snapshot.RootElement;
        string modelName = root.TryGetProperty("modelName", out JsonElement modelNameElement)
            ? modelNameElement.GetString() ?? "legacy"
            : snapshotRoot.TryGetProperty("name", out JsonElement snapshotName)
                ? snapshotName.GetString() ?? "legacy"
                : "legacy";
        ConstraintProgrammingCheckpointEnvelope envelope = WithDigest(
            new ConstraintProgrammingCheckpointEnvelope(
                SchemaVersion: CurrentSchema,
                SourceFormat: SourceFormatLegacyV1,
                MigratedFromLegacy: false,
                CheckpointId: LegacyCheckpointId,
                IdentitySchemaVersion: snapshotRoot.TryGetProperty(
                    "identitySchemaVersion",
                    out JsonElement identitySchema)
                    ? identitySchema.GetString() ?? "1.0"
                    : "1.0",
                IdentityNamespace: snapshotRoot.TryGetProperty(
                    "identityNamespace",
                    out JsonElement identityNamespace)
                    ? identityNamespace.GetString() ?? "model-local"
                    : "model-local",
                ModelName: modelName,
                ModelFingerprint: SolveFingerprinting.Sha256(canonical.Value).Value,
                ConfigurationFingerprint: null,
                SolverFingerprint: null,
                RunId: null,
                AttemptId: null,
                ParentCheckpointId: null,
                CreatedAtEpochMs: 0,
                SnapshotJson: canonical.Value,
                Incumbent: null,
                BestBound: null,
                Gap: null,
                Assumptions: Array.Empty<string>(),
                Conflicts: Array.Empty<PortableConstraintProgrammingConflict>(),
                Benders: null,
                CancellationChain: Array.Empty<PortableCancellationRecord>(),
                Provenance: null,
                Metadata: new Dictionary<string, string>(),
                IntegritySha256: string.Empty));
        return Results.Ok(envelope);
    }

    private static Result<
        PortableConstraintProgrammingIncumbent,
        ErrorCode,
        Error<ErrorCode>> ToPortable(
        ConstraintProgrammingSolution? incumbent,
        ConstraintProgrammingModelSnapshot snapshot) {
        if (incumbent is null) {
            return Results.Ok(new PortableConstraintProgrammingIncumbent());
        }
        var variables = snapshot.Variables.ToDictionary(
            variable => variable.Id,
            variable => variable);
        if (!incumbent.Values.Keys.ToHashSet().SetEquals(variables.Keys)) {
            return Failure<PortableConstraintProgrammingIncumbent>(
                ErrorCode.IllegalArgument,
                "checkpoint incumbent 变量集合不匹配 / " +
                "Checkpoint incumbent variable set mismatch");
        }
        foreach ((VariableId id, Int64 value) in incumbent.Values) {
            if (!variables[id].Domain.Contains(value)) {
                return Failure<PortableConstraintProgrammingIncumbent>(
                    ErrorCode.IllegalArgument,
                    "checkpoint incumbent 超出变量值域 / " +
                    "Checkpoint incumbent is outside a variable domain");
            }
        }
        foreach (ConstraintProgrammingConstraintSnapshot constraint in snapshot.Constraints) {
            Result<bool, ErrorCode, Error<ErrorCode>> satisfied =
                constraint.Constraint.IsSatisfied(incumbent.Values);
            if (satisfied.IsFailed || !satisfied.Value) {
                return Failure<PortableConstraintProgrammingIncumbent>(
                    ErrorCode.IllegalArgument,
                    $"checkpoint incumbent 违反约束：{constraint.Id.Value} / " +
                    $"Checkpoint incumbent violates constraint: {constraint.Id.Value}");
            }
        }
        var evaluatedIntervals = new Dictionary<IntervalId, IntervalValue>();
        foreach (IntervalVariable interval in snapshot.Intervals) {
            Result<IntervalValue, ErrorCode, Error<ErrorCode>> evaluated =
                interval.Evaluate(incumbent.Values);
            if (evaluated.IsFailed) {
                return evaluated.Map<PortableConstraintProgrammingIncumbent>(_ => default!);
            }
            if (incumbent.Intervals.TryGetValue(interval.Id, out IntervalValue? supplied) &&
                supplied != evaluated.Value) {
                return Failure<PortableConstraintProgrammingIncumbent>(
                    ErrorCode.IllegalArgument,
                    "checkpoint incumbent interval 与 snapshot 求值不一致 / " +
                    "Checkpoint incumbent interval disagrees with snapshot evaluation");
            }
            evaluatedIntervals[interval.Id] = evaluated.Value;
        }
        if (incumbent.Intervals.Keys.Any(id => !evaluatedIntervals.ContainsKey(id))) {
            return Failure<PortableConstraintProgrammingIncumbent>(
                ErrorCode.IllegalArgument,
                "checkpoint incumbent 包含未知 interval / " +
                "Checkpoint incumbent contains an unknown interval");
        }
        string? objective = null;
        if (snapshot.Objectives.Count > 0) {
            Result<Int64, ErrorCode, Error<ErrorCode>> evaluated =
                snapshot.Objectives[0].Expression.Evaluate(incumbent.Values);
            if (evaluated.IsFailed) {
                return evaluated.Map<PortableConstraintProgrammingIncumbent>(_ => default!);
            }
            objective = evaluated.Value.ToString();
        }
        return Results.Ok(new PortableConstraintProgrammingIncumbent(
            ValuesById: incumbent.Values.ToDictionary(
                pair => pair.Key.Value,
                pair => pair.Value.ToLong(),
                StringComparer.Ordinal),
            IntervalsById: evaluatedIntervals.ToDictionary(
                pair => pair.Key.Value,
                pair => new PortableConstraintProgrammingIntervalValue(
                    Start: pair.Value.Start.ToLong(),
                    Size: pair.Value.Size.ToLong(),
                    End: pair.Value.End.ToLong(),
                    Present: pair.Value.Present),
                StringComparer.Ordinal),
            Objective: objective));
    }

    private static Result<
        ConstraintProgrammingSolution?,
        ErrorCode,
        Error<ErrorCode>> ToSolution(
        PortableConstraintProgrammingIncumbent? incumbent,
        ConstraintProgrammingModelSnapshot snapshot) {
        if (incumbent is null) {
            return Results.Ok<ConstraintProgrammingSolution?>(null);
        }
        IReadOnlyDictionary<string, long> portableValues =
            incumbent.ValuesById ?? new Dictionary<string, long>();
        IReadOnlyDictionary<string, PortableConstraintProgrammingIntervalValue>
            portableIntervals = incumbent.IntervalsById
                ?? new Dictionary<string, PortableConstraintProgrammingIntervalValue>();
        var variables = snapshot.Variables.ToDictionary(
            variable => variable.Id.Value,
            variable => variable,
            StringComparer.Ordinal);
        if (!portableValues.Keys.ToHashSet(StringComparer.Ordinal)
                .SetEquals(variables.Keys)) {
            return Failure<ConstraintProgrammingSolution?>(
                ErrorCode.IllegalArgument,
                "checkpoint incumbent 变量集合不匹配 / " +
                "Checkpoint incumbent variable set mismatch");
        }
        var values = portableValues.ToDictionary(
            pair => new VariableId(pair.Key),
            pair => new Int64(pair.Value));
        foreach ((VariableId id, Int64 value) in values) {
            if (!variables[id.Value].Domain.Contains(value)) {
                return Failure<ConstraintProgrammingSolution?>(
                    ErrorCode.IllegalArgument,
                    "checkpoint incumbent 超出变量值域 / " +
                    "Checkpoint incumbent is outside a variable domain");
            }
        }
        foreach (ConstraintProgrammingConstraintSnapshot constraint in snapshot.Constraints) {
            Result<bool, ErrorCode, Error<ErrorCode>> satisfied =
                constraint.Constraint.IsSatisfied(values);
            if (satisfied.IsFailed || !satisfied.Value) {
                return Failure<ConstraintProgrammingSolution?>(
                    ErrorCode.IllegalArgument,
                    $"checkpoint incumbent 违反约束：{constraint.Id.Value} / " +
                    $"Checkpoint incumbent violates constraint: {constraint.Id.Value}");
            }
        }
        if (snapshot.Objectives.Count > 0) {
            Result<Int64, ErrorCode, Error<ErrorCode>> evaluated =
                snapshot.Objectives[0].Expression.Evaluate(values);
            if (evaluated.IsFailed) {
                return evaluated.Map<ConstraintProgrammingSolution?>(_ => null);
            }
            if (incumbent.Objective is not null &&
                !string.Equals(
                    incumbent.Objective,
                    evaluated.Value.ToString(),
                    StringComparison.Ordinal)) {
                return Failure<ConstraintProgrammingSolution?>(
                    ErrorCode.IllegalArgument,
                    "checkpoint incumbent 目标值不一致 / " +
                    "Checkpoint incumbent objective is inconsistent");
            }
        }
        else if (incumbent.Objective is not null) {
            return Failure<ConstraintProgrammingSolution?>(
                ErrorCode.IllegalArgument,
                "无目标模型不应包含 checkpoint 目标值 / " +
                "A model without objectives must not contain a checkpoint objective");
        }
        var intervals = new Dictionary<IntervalId, IntervalValue>();
        var expectedIntervals = snapshot.Intervals
            .Select(interval => interval.Id.Value)
            .ToHashSet(StringComparer.Ordinal);
        if (!portableIntervals.Keys.ToHashSet(StringComparer.Ordinal)
                .SetEquals(expectedIntervals)) {
            return Failure<ConstraintProgrammingSolution?>(
                ErrorCode.IllegalArgument,
                "checkpoint incumbent interval 集合不匹配 / " +
                "Checkpoint incumbent interval set mismatch");
        }
        foreach (IntervalVariable interval in snapshot.Intervals) {
            Result<IntervalValue, ErrorCode, Error<ErrorCode>> evaluated =
                interval.Evaluate(values);
            if (evaluated.IsFailed) {
                return evaluated.Map<ConstraintProgrammingSolution?>(_ => null);
            }
            PortableConstraintProgrammingIntervalValue serialized =
                portableIntervals[interval.Id.Value];
            if (serialized.Start != evaluated.Value.Start.ToLong() ||
                serialized.Size != evaluated.Value.Size.ToLong() ||
                serialized.End != evaluated.Value.End.ToLong() ||
                serialized.Present != evaluated.Value.Present) {
                return Failure<ConstraintProgrammingSolution?>(
                    ErrorCode.IllegalArgument,
                    "checkpoint interval 与 snapshot 求值不一致 / " +
                    "Checkpoint interval disagrees with snapshot evaluation");
            }
            intervals[interval.Id] = evaluated.Value;
        }
        return Results.Ok<ConstraintProgrammingSolution?>(
            new ConstraintProgrammingSolution(values: values, intervals: intervals));
    }

    private static string? ParentLinkFailure(
        string checkpointId,
        string? parentCheckpointId,
        bool validateParentLink,
        string? expectedParentCheckpointId) {
        if (validateParentLink && parentCheckpointId is not null) {
            if (string.IsNullOrWhiteSpace(parentCheckpointId)) {
                return "checkpoint 父标识不能为空白 / " +
                    "Checkpoint parent identifier must not be blank";
            }
            if (string.Equals(parentCheckpointId, checkpointId, StringComparison.Ordinal)) {
                return "checkpoint 父标识不能等于自身标识 / " +
                    "Checkpoint parent identifier must not equal its own identifier";
            }
        }
        if (expectedParentCheckpointId is not null) {
            if (string.IsNullOrWhiteSpace(expectedParentCheckpointId)) {
                return "期望的 checkpoint 父标识不能为空白 / " +
                    "Expected checkpoint parent identifier must not be blank";
            }
            if (!string.Equals(
                    parentCheckpointId,
                    expectedParentCheckpointId,
                    StringComparison.Ordinal)) {
                return "checkpoint 父标识与恢复请求不一致 / " +
                    "Checkpoint parent identifier does not match the resume request";
            }
        }
        return null;
    }

    private static string? ValidateCancellationChain(
        IReadOnlyList<PortableCancellationRecord> chain) {
        long previousTimestamp = 0;
        foreach (PortableCancellationRecord record in chain) {
            if (string.IsNullOrWhiteSpace(record.Origin)) {
                return "checkpoint 取消来源代码不能为空白 / " +
                    "Checkpoint cancellation origin code must not be blank";
            }
            if (record.RequestedAtEpochMs < 0) {
                return "checkpoint 取消时间戳不能为负 / " +
                    "Checkpoint cancellation timestamp must not be negative";
            }
            if (record.RequestedAtEpochMs < previousTimestamp) {
                return "checkpoint 取消链顺序无效 / " +
                    "Checkpoint cancellation chain is not ordered";
            }
            previousTimestamp = record.RequestedAtEpochMs;
        }
        return null;
    }

    private static string? ValidateProvenance(PortableSolverProvenance? provenance) {
        if (provenance is null) {
            return null;
        }
        if (string.IsNullOrWhiteSpace(provenance.SolverId) ||
            string.IsNullOrWhiteSpace(provenance.BackendName)) {
            return "checkpoint provenance 缺少求解器或后端标识 / " +
                "Checkpoint provenance is missing the solver or backend identifier";
        }
        if (provenance.ThreadCount < 0) {
            return "checkpoint provenance 线程数不能为负 / " +
                "Checkpoint provenance thread count must not be negative";
        }
        return null;
    }

    private static bool ProvenanceEquals(
        PortableSolverProvenance? left,
        PortableSolverProvenance? right) {
        if (ReferenceEquals(left, right)) {
            return true;
        }
        if (left is null || right is null) {
            return false;
        }
        return string.Equals(left.SolverId, right.SolverId, StringComparison.Ordinal) &&
            string.Equals(left.BackendName, right.BackendName, StringComparison.Ordinal) &&
            string.Equals(left.BackendVersion, right.BackendVersion, StringComparison.Ordinal) &&
            string.Equals(left.PluginVersion, right.PluginVersion, StringComparison.Ordinal) &&
            left.ThreadCount == right.ThreadCount &&
            left.RandomSeed == right.RandomSeed &&
            left.Deterministic == right.Deterministic &&
            StringMapEquals(left.RequestedConfiguration, right.RequestedConfiguration) &&
            StringMapEquals(left.EffectiveConfiguration, right.EffectiveConfiguration) &&
            StringMapEquals(left.EnvironmentSummary, right.EnvironmentSummary);
    }

    private static bool StringMapEquals(
        IReadOnlyDictionary<string, string>? left,
        IReadOnlyDictionary<string, string>? right) {
        int leftCount = left?.Count ?? 0;
        int rightCount = right?.Count ?? 0;
        if (leftCount != rightCount) {
            return false;
        }
        if (left is null) {
            return true;
        }
        foreach ((string key, string value) in left) {
            if (right is null ||
                !right.TryGetValue(key, out string? rightValue) ||
                !string.Equals(value, rightValue, StringComparison.Ordinal)) {
                return false;
            }
        }
        return true;
    }

    private static string? ValidateHistoricalBounds(
        PortableConstraintProgrammingIncumbent? incumbent,
        string? bestBound,
        string? gap) {
        if (!TryParseFinite(bestBound, out double boundValue)) {
            return "checkpoint best bound 不是有效数值 / Checkpoint best bound is not a valid number";
        }
        if (!TryParseFinite(gap, out double gapValue)) {
            return "checkpoint relative gap 不是有效数值 / Checkpoint relative gap is not a valid number";
        }
        if (!TryParseFinite(
                incumbent?.Objective,
                out double objectiveValue)) {
            return "checkpoint incumbent objective 不是有效数值 / " +
                "Checkpoint incumbent objective is not a valid number";
        }
        if (gap is not null && gapValue < 0) {
            return "checkpoint 相对 gap 不能为负 / Checkpoint relative gap cannot be negative";
        }
        if (gap is not null &&
            (incumbent?.Objective is null || bestBound is null)) {
            return "checkpoint 相对 gap 需要同时提供 incumbent 目标值与最佳界 / " +
                "Checkpoint relative gap requires both an incumbent objective and a best bound";
        }
        if (gap is not null && bestBound is not null && incumbent?.Objective is not null) {
            double scale = System.Math.Max(System.Math.Abs(objectiveValue), 1.0);
            double expected = System.Math.Abs(objectiveValue - boundValue) / scale;
            double tolerance = 1E-9 * System.Math.Max(
                System.Math.Max(System.Math.Abs(expected), System.Math.Abs(gapValue)),
                1.0);
            if (System.Math.Abs(expected - gapValue) > tolerance) {
                return "checkpoint 相对 gap 与 incumbent 及最佳界不一致 / " +
                    "Checkpoint relative gap does not match the incumbent and bound";
            }
        }
        return null;
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

    private static string? ValidateConflict(
        PortableConstraintProgrammingConflict conflict,
        IReadOnlySet<string> variableIds,
        IReadOnlySet<string> constraintIds) {
        if (conflict.Validity is not ("Verified" or "Heuristic" or "Unknown") ||
            conflict.Minimality is not ("Irreducible" or "Partial" or "NotChecked")) {
            return "checkpoint conflict 证据枚举无效 / " +
                "Checkpoint conflict evidence enum is invalid";
        }
        IReadOnlyList<string> members = conflict.MemberIds ?? Array.Empty<string>();
        IReadOnlyList<string> assumptions =
            conflict.AssumptionIds ?? Array.Empty<string>();
        if (conflict.Validity == "Verified" && members.Count == 0) {
            return "已验证 checkpoint conflict 不能没有成员 / " +
                "A verified checkpoint conflict must contain members";
        }
        if (assumptions.Any(id => !variableIds.Contains(id))) {
            return "checkpoint conflict 引用了未知 assumption / " +
                "Checkpoint conflict references an unknown assumption";
        }
        foreach (string member in members) {
            if (member.StartsWith("constraint:", StringComparison.Ordinal)) {
                string id = member["constraint:".Length..];
                if (!constraintIds.Contains(id)) {
                    return "checkpoint conflict 引用了未知约束 / " +
                        "Checkpoint conflict references an unknown constraint";
                }
            }
            else if (member.StartsWith("domain:", StringComparison.Ordinal)) {
                string id = member["domain:".Length..];
                if (!variableIds.Contains(id)) {
                    return "checkpoint conflict 引用了未知变量域 / " +
                        "Checkpoint conflict references an unknown variable domain";
                }
            }
            else if (member.StartsWith("bound:", StringComparison.Ordinal)) {
                string encodedMember = member["bound:".Length..];
                int separator = encodedMember.LastIndexOf(':');
                string variableId = separator > 0
                    ? encodedMember[..separator]
                    : string.Empty;
                string side = separator > 0
                    ? encodedMember[(separator + 1)..]
                    : string.Empty;
                if (!variableIds.Contains(variableId) ||
                    side is not ("Lower" or "Upper")) {
                    return "checkpoint conflict 边界成员无效 / " +
                        "Checkpoint conflict bound member is invalid";
                }
            }
            else {
                return "checkpoint conflict 成员类型未知 / " +
                    "Checkpoint conflict member type is unknown";
            }
        }
        return null;
    }

    private static string? ValidateBendersState(
        PortableConstraintProgrammingBendersState? state) {
        if (state is null) {
            return null;
        }
        if (state.Iteration < 0) {
            return "Benders checkpoint iteration 不能为负 / " +
                "Benders checkpoint iteration must not be negative";
        }
        bool hasMasterState =
            state.MasterIncumbent is not null ||
            state.MasterBestBound is not null ||
            (state.Assumptions?.Count ?? 0) > 0 ||
            (state.FixedBindings?.Count ?? 0) > 0 ||
            (state.Conflicts?.Count ?? 0) > 0 ||
            state.ConvergenceVerified;
        if (hasMasterState && string.IsNullOrWhiteSpace(state.MasterFingerprint)) {
            return "Benders 主问题状态缺少模型指纹 / " +
                "Benders master state is missing a model fingerprint";
        }
        if ((state.MasterFingerprint is not null &&
                string.IsNullOrWhiteSpace(state.MasterFingerprint)) ||
            (state.SubproblemModelFingerprint is not null &&
                string.IsNullOrWhiteSpace(state.SubproblemModelFingerprint))) {
            return "Benders checkpoint 模型指纹不能为空 / " +
                "Benders checkpoint model fingerprints must not be blank";
        }
        return null;
    }

    private static ConstraintProgrammingCheckpointEnvelope WithDigest(
        ConstraintProgrammingCheckpointEnvelope envelope) {
        ConstraintProgrammingCheckpointEnvelope normalized =
            CanonicalizeMapKeys(envelope) with { SchemaVersion = CurrentSchema };
        return normalized with {
            IntegritySha256 = Digest(normalized with { IntegritySha256 = string.Empty }),
        };
    }

    private static ConstraintProgrammingCheckpointEnvelope CanonicalizeMapKeys(
        ConstraintProgrammingCheckpointEnvelope envelope) {
        return envelope with {
            Assumptions = (envelope.Assumptions ?? Array.Empty<string>()).ToList(),
            Conflicts = (envelope.Conflicts
                ?? Array.Empty<PortableConstraintProgrammingConflict>())
                .Select(CanonicalizeMapKeys)
                .ToList(),
            CancellationChain = (envelope.CancellationChain
                ?? Array.Empty<PortableCancellationRecord>())
                .ToList(),
            Incumbent = envelope.Incumbent is null
                ? null
                : envelope.Incumbent with {
                    ValuesById = Sorted(envelope.Incumbent.ValuesById),
                    IntervalsById = Sorted(envelope.Incumbent.IntervalsById),
                },
            Benders = envelope.Benders is null
                ? null
                : envelope.Benders with {
                    FixedBindings = Sorted(envelope.Benders.FixedBindings),
                    Cuts = (envelope.Benders.Cuts
                        ?? Array.Empty<PortableConstraintProgrammingCut>())
                        .Select(CanonicalizeMapKeys)
                        .ToList(),
                    Conflicts = (envelope.Benders.Conflicts
                        ?? Array.Empty<PortableConstraintProgrammingConflict>())
                        .Select(CanonicalizeMapKeys)
                        .ToList(),
                    Trace = (envelope.Benders.Trace ?? Array.Empty<string>()).ToList(),
                    Assumptions = (envelope.Benders.Assumptions
                        ?? Array.Empty<string>()).ToList(),
                },
            Provenance = envelope.Provenance is null
                ? null
                : envelope.Provenance with {
                    RequestedConfiguration = Sorted(
                        envelope.Provenance.RequestedConfiguration),
                    EffectiveConfiguration = Sorted(
                        envelope.Provenance.EffectiveConfiguration),
                    EnvironmentSummary = Sorted(
                        envelope.Provenance.EnvironmentSummary),
                },
            Metadata = Sorted(envelope.Metadata),
        };
    }

    private static PortableConstraintProgrammingConflict CanonicalizeMapKeys(
        PortableConstraintProgrammingConflict conflict) =>
        conflict with {
            MemberIds = (conflict.MemberIds ?? Array.Empty<string>()).ToList(),
            AssumptionIds = (conflict.AssumptionIds ?? Array.Empty<string>()).ToList(),
            Provenance = Sorted(conflict.Provenance),
        };

    private static PortableConstraintProgrammingCut CanonicalizeMapKeys(
        PortableConstraintProgrammingCut cut) =>
        cut with {
            Provenance = Sorted(cut.Provenance),
            Payload = Sorted(cut.Payload),
        };

    private static IReadOnlyDictionary<string, T> Sorted<T>(
        IReadOnlyDictionary<string, T>? values) =>
        values is null
            ? new Dictionary<string, T>(StringComparer.Ordinal)
            : values
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value,
                    StringComparer.Ordinal);

    private static string Digest(ConstraintProgrammingCheckpointEnvelope envelope) =>
        SolveFingerprinting.Sha256(
            JsonSerializer.Serialize(CanonicalizeMapKeys(envelope), JsonOptions))
            .Value;

    private static string LegacyDigest(LegacyV2Envelope envelope) =>
        SolveFingerprinting.Sha256(JsonSerializer.Serialize(envelope, JsonOptions))
            .Value;

    private static Result<T, ErrorCode, Error<ErrorCode>> Failure<T>(
        ErrorCode code,
        string message) =>
        Results.Failed<T>(new Err<ErrorCode>(code, message));
}

internal sealed record LegacyV2Envelope(
    [property: JsonPropertyName("schemaVersion"), JsonPropertyOrder(0)] string SchemaVersion,
    [property: JsonPropertyName("sourceFormat"), JsonPropertyOrder(1)] string SourceFormat,
    [property: JsonPropertyName("migratedFromLegacy"), JsonPropertyOrder(2)] bool MigratedFromLegacy,
    [property: JsonPropertyName("checkpointId"), JsonPropertyOrder(3)] string CheckpointId,
    [property: JsonPropertyName("identitySchemaVersion"), JsonPropertyOrder(4)] string IdentitySchemaVersion,
    [property: JsonPropertyName("identityNamespace"), JsonPropertyOrder(5)] string IdentityNamespace,
    [property: JsonPropertyName("modelName"), JsonPropertyOrder(6)] string ModelName,
    [property: JsonPropertyName("modelFingerprint"), JsonPropertyOrder(7)] string ModelFingerprint,
    [property: JsonPropertyName("configurationFingerprint"), JsonPropertyOrder(8)] string? ConfigurationFingerprint,
    [property: JsonPropertyName("solverFingerprint"), JsonPropertyOrder(9)] string? SolverFingerprint,
    [property: JsonPropertyName("runId"), JsonPropertyOrder(10)] string? RunId,
    [property: JsonPropertyName("attemptId"), JsonPropertyOrder(11)] string? AttemptId,
    [property: JsonPropertyName("parentCheckpointId"), JsonPropertyOrder(12)] string? ParentCheckpointId,
    [property: JsonPropertyName("createdAtEpochMs"), JsonPropertyOrder(13)] long CreatedAtEpochMs,
    [property: JsonPropertyName("snapshotJson"), JsonPropertyOrder(14)] string SnapshotJson,
    [property: JsonPropertyName("incumbent"), JsonPropertyOrder(15)] PortableConstraintProgrammingIncumbent? Incumbent,
    [property: JsonPropertyName("bestBound"), JsonPropertyOrder(16)] string? BestBound,
    [property: JsonPropertyName("gap"), JsonPropertyOrder(17)] string? Gap,
    [property: JsonPropertyName("assumptions"), JsonPropertyOrder(18)] IReadOnlyList<string>? Assumptions,
    [property: JsonPropertyName("conflicts"), JsonPropertyOrder(19)] IReadOnlyList<PortableConstraintProgrammingConflict>? Conflicts,
    [property: JsonPropertyName("benders"), JsonPropertyOrder(20)] LegacyV2BendersState? Benders,
    [property: JsonPropertyName("integritySha256"), JsonPropertyOrder(21)] string IntegritySha256);

internal sealed record LegacyV2BendersState(
    [property: JsonPropertyName("iteration"), JsonPropertyOrder(0)] long Iteration,
    [property: JsonPropertyName("masterIncumbent"), JsonPropertyOrder(1)] string? MasterIncumbent,
    [property: JsonPropertyName("masterBestBound"), JsonPropertyOrder(2)] string? MasterBestBound,
    [property: JsonPropertyName("cuts"), JsonPropertyOrder(3)] IReadOnlyList<PortableConstraintProgrammingCut>? Cuts,
    [property: JsonPropertyName("trace"), JsonPropertyOrder(4)] IReadOnlyList<string>? Trace,
    [property: JsonPropertyName("assumptions"), JsonPropertyOrder(5)] IReadOnlyList<string>? Assumptions,
    [property: JsonPropertyName("fixedBindings"), JsonPropertyOrder(6)] IReadOnlyDictionary<string, long>? FixedBindings,
    [property: JsonPropertyName("conflicts"), JsonPropertyOrder(7)] IReadOnlyList<PortableConstraintProgrammingConflict>? Conflicts,
    [property: JsonPropertyName("convergenceVerified"), JsonPropertyOrder(8)] bool ConvergenceVerified,
    [property: JsonPropertyName("subproblemModelFingerprint"), JsonPropertyOrder(9)] string? SubproblemModelFingerprint);
