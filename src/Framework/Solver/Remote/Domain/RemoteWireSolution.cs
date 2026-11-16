#nullable enable

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Solver.Remote.Domain;

/// <summary>CP interval 稳定值 / Stable CP interval value.</summary>
/// <param name="Start">开始值 / Start value.</param>
/// <param name="Size">长度 / Size.</param>
/// <param name="End">结束值 / End value.</param>
/// <param name="Present">是否存在 / Whether present.</param>
public sealed record SerializedIntervalValue(
    long Start,
    long Size,
    long End,
    bool Present = true);

/// <summary>
/// 远程序列化解 wire 契约 / Remote serialized-solution wire contract.
/// </summary>
/// <remarks>
/// 精确整数目标值走 <see cref="ObjectiveValueInt64"/>；<see cref="ObjectiveValue"/> 只承载线性/二次浮点值。
/// CP 结果的整数目标与稳定变量值必须无损保留，不能经 double 往返。
/// The exact objective uses <see cref="ObjectiveValueInt64"/>; <see cref="ObjectiveValue"/> carries only
/// linear/quadratic floating values. CP integer objectives and stable variable values must survive without a double round-trip.
/// </remarks>
public sealed record SerializedSolution {
    /// <summary>是否可行 / Whether feasible.</summary>
    public bool Feasible { get; init; }

    /// <summary>是否最优 / Whether optimal.</summary>
    public bool Optimal { get; init; }

    /// <summary>线性/二次兼容浮点目标；CP 结果必须为 null / Legacy floating objective for linear/quadratic; null for CP.</summary>
    public double? ObjectiveValue { get; init; }

    /// <summary>CP 精确整数目标值 / Exact Int64 CP objective value.</summary>
    public long? ObjectiveValueInt64 { get; init; }

    /// <summary>最优间隙 / Optimality gap.</summary>
    public double? Gap { get; init; }

    /// <summary>按求解器顺序的变量值 / Variable values in solver order.</summary>
    public IReadOnlyList<double> VariableValues { get; init; } = System.Array.Empty<double>();

    /// <summary>CP 稳定变量值 / CP stable variable values by ID.</summary>
    public IReadOnlyDictionary<string, long> VariableValuesById { get; init; } =
        new Dictionary<string, long>();

    /// <summary>CP 稳定 interval 值 / CP stable interval values by ID.</summary>
    public IReadOnlyDictionary<string, SerializedIntervalValue> IntervalValues { get; init; } =
        new Dictionary<string, SerializedIntervalValue>();

    /// <summary>结果问题状态 / Problem status reported by the artifact.</summary>
    public RemoteProblemStatus? ProblemStatus { get; init; }

    /// <summary>解存在性 / Solution presence reported by the artifact.</summary>
    public RemoteSolutionPresence? SolutionPresence { get; init; }

    /// <summary>证明状态 / Proof status reported by the artifact.</summary>
    public RemoteProofStatus? ProofStatus { get; init; }

    /// <summary>终止原因 / Termination reason.</summary>
    public RemoteTerminationReason? TerminationReason { get; init; }

    /// <summary>结果协议版本 / Result protocol version.</summary>
    public string SchemaVersion { get; init; } = "1.0";

    /// <summary>脱敏执行来源 / Redacted execution provenance.</summary>
    public IReadOnlyDictionary<string, string> Provenance { get; init; } =
        new Dictionary<string, string>();

    /// <summary>审计指纹 / Audit fingerprints.</summary>
    public IReadOnlyDictionary<string, string> Fingerprints { get; init; } =
        new Dictionary<string, string>();

    /// <summary>指纹 schema / Fingerprint schemas.</summary>
    public IReadOnlyDictionary<string, string> FingerprintSchemas { get; init; } =
        new Dictionary<string, string>();

    /// <summary>求解统计 / Solve statistics.</summary>
    public IReadOnlyDictionary<string, string> Statistics { get; init; } =
        new Dictionary<string, string>();

    /// <summary>结构化诊断 / Structured diagnostics.</summary>
    public IReadOnlyDictionary<string, string> Diagnostics { get; init; } =
        new Dictionary<string, string>();

    /// <summary>求解运行标识 / Solve run identifier.</summary>
    public string? RunId { get; init; }

    /// <summary>求解尝试标识 / Solve attempt identifier.</summary>
    public string? AttemptId { get; init; }

    /// <summary>结果 artifact 摘要 / Result artifact digest.</summary>
    public string? ArtifactDigest { get; init; }

    /// <summary>耗时毫秒；缺失为 0 / Elapsed milliseconds; 0 when absent.</summary>
    public long ElapsedMs { get; init; }

    /// <summary>求解器状态原文 / Raw solver status.</summary>
    public string SolverStatus { get; init; } = "";

    /// <summary>结果消息 / Result message.</summary>
    public string? Message { get; init; }

    /// <summary>
    /// 是否为可声明的精确最优结论 / Whether an exact optimum may be claimed.
    /// </summary>
    /// <remarks>
    /// 规则是：<c>optimal</c> 是权威字段，且必须可行、携带非 NONE 证明状态、解存在性为 OPTIMAL。
    /// 间隙为 0 或解存在性单独都不是充分条件；字段缺失一律不推断为最优。
    /// Rule: <c>optimal</c> is authoritative and must be feasible, carry a non-NONE proof status
    /// and report OPTIMAL presence. Neither a zero gap nor the presence value alone is sufficient, and absent fields never infer optimality.
    /// </remarks>
    /// <returns>是否声明精确最优 / Whether an exact optimum is claimed.</returns>
    [JsonIgnore]
    public bool ClaimsExactOptimum =>
        Optimal &&
        Feasible &&
        ProofStatus is not null &&
        ProofStatus != RemoteProofStatus.None &&
        SolutionPresence == RemoteSolutionPresence.Optimal;

    /// <summary>
    /// 解码 wire JSON / Decode wire JSON.
    /// </summary>
    /// <param name="json">wire JSON / Wire JSON.</param>
    /// <returns>解或结构化错误 / Solution or a structured error.</returns>
    public static Result<SerializedSolution, ErrorCode, Error<ErrorCode>> Decode(string json) =>
        RemoteWireJson.DecodeSolution(json);

    /// <summary>
    /// 编码为 wire JSON / Encode to wire JSON.
    /// </summary>
    /// <returns>wire JSON / Wire JSON.</returns>
    public string Encode() => RemoteWireJson.EncodeSolution(this);
}

/// <summary>
/// 远程 wire JSON 编解码 / Remote wire JSON codec.
/// </summary>
/// <remarks>
/// 只接受文档声明字段；未知枚举映射为 Unknown，不抛异常。缺省字段按 Kotlin 默认值补齐。
/// Accepts only documented fields; unknown enum values map to Unknown without throwing. Absent fields take the Kotlin defaults.
/// </remarks>
public static class RemoteWireJson {
    internal static readonly JsonSerializerOptions Options = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.Strict,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
        Converters = {
            new RemoteWireEnumConverter<RemoteProblemStatus>(
                parse: RemoteWireEnums.ParseProblemStatus,
                write: RemoteWireEnums.Write,
                unknown: RemoteProblemStatus.Unknown),
            new RemoteWireEnumConverter<RemoteTerminationReason>(
                parse: RemoteWireEnums.ParseTerminationReason,
                write: RemoteWireEnums.Write,
                unknown: RemoteTerminationReason.Unknown),
            new RemoteWireEnumConverter<RemoteSolutionPresence>(
                parse: RemoteWireEnums.ParseSolutionPresence,
                write: RemoteWireEnums.Write,
                unknown: RemoteSolutionPresence.Unknown),
            new RemoteWireEnumConverter<RemoteProofStatus>(
                parse: RemoteWireEnums.ParseProofStatus,
                write: RemoteWireEnums.Write,
                unknown: RemoteProofStatus.Unknown),
            new RemoteWireEnumConverter<SliceOutcome>(
                parse: RemoteWireEnums.ParseSliceOutcome,
                write: RemoteWireEnums.Write,
                unknown: SliceOutcome.Unknown),
            new RemoteWireEnumConverter<PreemptionMode>(
                parse: RemoteWireEnums.ParsePreemptionMode,
                write: RemoteWireEnums.Write,
                unknown: PreemptionMode.Unknown),
            new RemoteWireEnumConverter<ResumeMode>(
                parse: RemoteWireEnums.ParseResumeMode,
                write: RemoteWireEnums.Write,
                unknown: ResumeMode.Unknown),
            new RemoteNullableWireEnumConverter<TaskComplexity>(
                parse: RemoteWireEnums.ParseTaskComplexity,
                write: RemoteWireEnums.Write),
            new RemoteNullableWireEnumConverter<TimeSensitivity>(
                parse: RemoteWireEnums.ParseTimeSensitivity,
                write: RemoteWireEnums.Write),
            // 标识值类型必须写成裸字符串，与固定 Kotlin 的 value class 一致。
            // Identifier value types must be written as bare strings to match the pinned Kotlin value classes.
            new RemoteWireIdentifierConverterFactory(),
        },
    };

    /// <summary>解码 wire 解 / Decode a wire solution.</summary>
    /// <param name="json">wire JSON / Wire JSON.</param>
    /// <returns>解或结构化错误 / Solution or a structured error.</returns>
    public static Result<SerializedSolution, ErrorCode, Error<ErrorCode>> DecodeSolution(string json) {
        if (json is null) {
            return Results.Failed<SerializedSolution>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "远程结果 JSON 为空 / Remote result JSON is null"));
        }
        try {
            SerializedSolution? solution = JsonSerializer.Deserialize<SerializedSolution>(json, Options);
            if (solution is null) {
                return Results.Failed<SerializedSolution>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "远程结果 JSON 无法解析 / Remote result JSON cannot be parsed"));
            }
            return Results.Ok(solution);
        }
        catch (JsonException exception) {
            return Results.Failed<SerializedSolution>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                $"远程结果 JSON 无效：{exception.Message} / Invalid remote result JSON: {exception.Message}"));
        }
    }

    /// <summary>编码 wire 解 / Encode a wire solution.</summary>
    /// <param name="solution">解 / Solution.</param>
    /// <returns>wire JSON / Wire JSON.</returns>
    public static string EncodeSolution(SerializedSolution solution) =>
        JsonSerializer.Serialize(solution, Options);

    /// <summary>解码能力摘要 / Decode a capability summary.</summary>
    /// <param name="json">wire JSON / Wire JSON.</param>
    /// <returns>能力摘要或结构化错误 / Capability summary or a structured error.</returns>
    public static Result<RemoteSolverCapabilities, ErrorCode, Error<ErrorCode>> DecodeCapabilities(string json) {
        if (json is null) {
            return Results.Failed<RemoteSolverCapabilities>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "远程能力 JSON 为空 / Remote capability JSON is null"));
        }
        try {
            RemoteSolverCapabilities? capabilities =
                JsonSerializer.Deserialize<RemoteSolverCapabilities>(json, Options);
            if (capabilities is null) {
                return Results.Failed<RemoteSolverCapabilities>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "远程能力 JSON 无法解析 / Remote capability JSON cannot be parsed"));
            }
            return Results.Ok(capabilities);
        }
        catch (JsonException exception) {
            return Results.Failed<RemoteSolverCapabilities>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                $"远程能力 JSON 无效：{exception.Message} / Invalid remote capability JSON: {exception.Message}"));
        }
    }

    /// <summary>编码能力摘要 / Encode a capability summary.</summary>
    /// <param name="capabilities">能力摘要 / Capability summary.</param>
    /// <returns>wire JSON / Wire JSON.</returns>
    public static string EncodeCapabilities(RemoteSolverCapabilities capabilities) =>
        JsonSerializer.Serialize(capabilities, Options);
}
