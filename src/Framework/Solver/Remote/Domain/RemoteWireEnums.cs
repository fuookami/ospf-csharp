#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fuookami.Ospf.Framework.Solver.Remote.Domain;

// 远程 wire 枚举统一带 Unknown 兜底：服务端新增枚举值不得让解码抛异常或误判为成功。
// Every remote wire enum carries an Unknown fallback so a newer server value never throws or reads as success.

/// <summary>远程问题结论 / Remote problem conclusion.</summary>
public enum RemoteProblemStatus {
    /// <summary>可行 / Feasible</summary>
    Feasible,
    /// <summary>不可行 / Infeasible</summary>
    Infeasible,
    /// <summary>无界 / Unbounded</summary>
    Unbounded,
    /// <summary>不可行或无界 / Infeasible or unbounded</summary>
    InfeasibleOrUnbounded,
    /// <summary>未知 / Unknown</summary>
    Unknown
}

/// <summary>远程终止原因 / Remote termination reason.</summary>
public enum RemoteTerminationReason {
    /// <summary>正常完成 / Completed</summary>
    Completed,
    /// <summary>时间限制 / Time limit</summary>
    TimeLimit,
    /// <summary>节点限制 / Node limit</summary>
    NodeLimit,
    /// <summary>迭代限制 / Iteration limit</summary>
    IterationLimit,
    /// <summary>解数量限制 / Solution limit</summary>
    SolutionLimit,
    /// <summary>目标限制 / Objective limit</summary>
    ObjectiveLimit,
    /// <summary>已取消 / Cancelled</summary>
    Cancelled,
    /// <summary>已中断 / Interrupted</summary>
    Interrupted,
    /// <summary>数值失败 / Numerical failure</summary>
    NumericalFailure,
    /// <summary>后端失败 / Backend failure</summary>
    BackendFailure,
    /// <summary>未知；不得视为成功 / Unknown; never treat it as success</summary>
    Unknown
}

/// <summary>远程解存在性 / Remote solution presence.</summary>
public enum RemoteSolutionPresence {
    /// <summary>无解 / None</summary>
    None,
    /// <summary>当前解 / Incumbent</summary>
    Incumbent,
    /// <summary>最优解 / Optimal</summary>
    Optimal,
    /// <summary>未知 / Unknown</summary>
    Unknown
}

/// <summary>远程证明状态 / Remote proof status.</summary>
public enum RemoteProofStatus {
    /// <summary>无证明 / None</summary>
    None,
    /// <summary>声明 / Claimed</summary>
    Claimed,
    /// <summary>已验证 / Verified</summary>
    Verified,
    /// <summary>未知 / Unknown</summary>
    Unknown
}

/// <summary>切片结果结论 / Slice outcome.</summary>
public enum SliceOutcome {
    /// <summary>已完成 / Completed</summary>
    Completed,
    /// <summary>已抢占 / Preempted</summary>
    Preempted,
    /// <summary>已保存检查点 / Checkpointed</summary>
    Checkpointed,
    /// <summary>可恢复 / Resumable</summary>
    Resumable,
    /// <summary>已取消 / Cancelled</summary>
    Cancelled,
    /// <summary>已失败 / Failed</summary>
    Failed,
    /// <summary>未知 / Unknown</summary>
    Unknown
}

/// <summary>抢占模式 / Preemption mode.</summary>
public enum PreemptionMode {
    /// <summary>不可抢占 / Non-preemptible</summary>
    NonPreemptible,
    /// <summary>受控返回 / Controlled return</summary>
    ControlledReturn,
    /// <summary>原生抢占 / Native</summary>
    Native,
    /// <summary>未知 / Unknown</summary>
    Unknown
}

/// <summary>恢复模式 / Resume mode.</summary>
public enum ResumeMode {
    /// <summary>不可恢复 / None</summary>
    None,
    /// <summary>热启动 / Warm start</summary>
    WarmStart,
    /// <summary>基 / Basis</summary>
    Basis,
    /// <summary>原生检查点 / Native checkpoint</summary>
    NativeCheckpoint,
    /// <summary>未知 / Unknown</summary>
    Unknown
}

/// <summary>
/// 远程 wire 枚举编解码 / Remote wire enum codec.
/// </summary>
/// <remarks>
/// 线上使用 SCREAMING_SNAKE 名称；未识别值映射到 Unknown 并保留原文，供诊断与写回。
/// Wire values use SCREAMING_SNAKE names; unrecognized values map to Unknown and keep their raw text for diagnostics and write-back.
/// </remarks>
public static class RemoteWireEnums {
    /// <summary>解析问题结论 / Parse a problem status.</summary>
    /// <param name="wire">线上值 / Wire value.</param>
    /// <returns>枚举值；未识别为 Unknown / Enum value; Unknown when unrecognized.</returns>
    public static RemoteProblemStatus ParseProblemStatus(string? wire) => wire switch {
        "FEASIBLE" => RemoteProblemStatus.Feasible,
        "INFEASIBLE" => RemoteProblemStatus.Infeasible,
        "UNBOUNDED" => RemoteProblemStatus.Unbounded,
        "INFEASIBLE_OR_UNBOUNDED" => RemoteProblemStatus.InfeasibleOrUnbounded,
        _ => RemoteProblemStatus.Unknown,
    };

    /// <summary>解析终止原因 / Parse a termination reason.</summary>
    /// <param name="wire">线上值 / Wire value.</param>
    /// <returns>枚举值；未识别为 Unknown / Enum value; Unknown when unrecognized.</returns>
    public static RemoteTerminationReason ParseTerminationReason(string? wire) => wire switch {
        "COMPLETED" => RemoteTerminationReason.Completed,
        "TIME_LIMIT" => RemoteTerminationReason.TimeLimit,
        "NODE_LIMIT" => RemoteTerminationReason.NodeLimit,
        "ITERATION_LIMIT" => RemoteTerminationReason.IterationLimit,
        "SOLUTION_LIMIT" => RemoteTerminationReason.SolutionLimit,
        "OBJECTIVE_LIMIT" => RemoteTerminationReason.ObjectiveLimit,
        "CANCELLED" => RemoteTerminationReason.Cancelled,
        "INTERRUPTED" => RemoteTerminationReason.Interrupted,
        "NUMERICAL_FAILURE" => RemoteTerminationReason.NumericalFailure,
        "BACKEND_FAILURE" => RemoteTerminationReason.BackendFailure,
        _ => RemoteTerminationReason.Unknown,
    };

    /// <summary>解析解存在性 / Parse a solution presence.</summary>
    /// <param name="wire">线上值 / Wire value.</param>
    /// <returns>枚举值；未识别为 Unknown / Enum value; Unknown when unrecognized.</returns>
    public static RemoteSolutionPresence ParseSolutionPresence(string? wire) => wire switch {
        "NONE" => RemoteSolutionPresence.None,
        "INCUMBENT" => RemoteSolutionPresence.Incumbent,
        "OPTIMAL" => RemoteSolutionPresence.Optimal,
        _ => RemoteSolutionPresence.Unknown,
    };

    /// <summary>解析证明状态 / Parse a proof status.</summary>
    /// <param name="wire">线上值 / Wire value.</param>
    /// <returns>枚举值；未识别为 Unknown / Enum value; Unknown when unrecognized.</returns>
    public static RemoteProofStatus ParseProofStatus(string? wire) => wire switch {
        "NONE" => RemoteProofStatus.None,
        "CLAIMED" => RemoteProofStatus.Claimed,
        "VERIFIED" => RemoteProofStatus.Verified,
        _ => RemoteProofStatus.Unknown,
    };

    /// <summary>解析切片结论 / Parse a slice outcome.</summary>
    /// <param name="wire">线上值 / Wire value.</param>
    /// <returns>枚举值；未识别为 Unknown / Enum value; Unknown when unrecognized.</returns>
    public static SliceOutcome ParseSliceOutcome(string? wire) => wire switch {
        "COMPLETED" => SliceOutcome.Completed,
        "PREEMPTED" => SliceOutcome.Preempted,
        "CHECKPOINTED" => SliceOutcome.Checkpointed,
        "RESUMABLE" => SliceOutcome.Resumable,
        "CANCELLED" => SliceOutcome.Cancelled,
        "FAILED" => SliceOutcome.Failed,
        _ => SliceOutcome.Unknown,
    };

    /// <summary>解析抢占模式 / Parse a preemption mode.</summary>
    /// <param name="wire">线上值 / Wire value.</param>
    /// <returns>枚举值；未识别为 Unknown / Enum value; Unknown when unrecognized.</returns>
    public static PreemptionMode ParsePreemptionMode(string? wire) => wire switch {
        "NON_PREEMPTIBLE" => PreemptionMode.NonPreemptible,
        "CONTROLLED_RETURN" => PreemptionMode.ControlledReturn,
        "NATIVE" => PreemptionMode.Native,
        _ => PreemptionMode.Unknown,
    };

    /// <summary>解析恢复模式 / Parse a resume mode.</summary>
    /// <param name="wire">线上值 / Wire value.</param>
    /// <returns>枚举值；未识别为 Unknown / Enum value; Unknown when unrecognized.</returns>
    public static ResumeMode ParseResumeMode(string? wire) => wire switch {
        "NONE" => ResumeMode.None,
        "WARM_START" => ResumeMode.WarmStart,
        "BASIS" => ResumeMode.Basis,
        "NATIVE_CHECKPOINT" => ResumeMode.NativeCheckpoint,
        _ => ResumeMode.Unknown,
    };

    /// <summary>解析任务状态 / Parse a task status.</summary>
    /// <param name="wire">线上值 / Wire value.</param>
    /// <param name="status">解析结果 / Parsed status.</param>
    /// <returns>是否识别 / Whether recognized.</returns>
    public static bool TryParseTaskStatus(string? wire, out TaskStatus status) {
        status = wire switch {
            "CREATED" => TaskStatus.Created,
            "ACCEPTED" => TaskStatus.Accepted,
            "QUEUED" => TaskStatus.Queued,
            "DISPATCHING" => TaskStatus.Dispatching,
            "RUNNING" => TaskStatus.Running,
            "SUSPENDED" => TaskStatus.Suspended,
            "COMPLETED" => TaskStatus.Completed,
            "STOPPING" => TaskStatus.Stopping,
            "STOPPED" => TaskStatus.Stopped,
            "FAILED" => TaskStatus.Failed,
            "WAITING_FOR_BUDGET" => TaskStatus.WaitingForBudget,
            _ => TaskStatus.Unknown,
        };
        return status != TaskStatus.Unknown;
    }

    /// <summary>写出任务状态 / Write a task status.</summary>
    /// <param name="value">枚举值 / Enum value.</param>
    /// <returns>线上值 / Wire value.</returns>
    public static string Write(TaskStatus value) => value switch {
        TaskStatus.Created => "CREATED",
        TaskStatus.Accepted => "ACCEPTED",
        TaskStatus.Queued => "QUEUED",
        TaskStatus.Dispatching => "DISPATCHING",
        TaskStatus.Running => "RUNNING",
        TaskStatus.Suspended => "SUSPENDED",
        TaskStatus.Completed => "COMPLETED",
        TaskStatus.Stopping => "STOPPING",
        TaskStatus.Stopped => "STOPPED",
        TaskStatus.Failed => "FAILED",
        TaskStatus.WaitingForBudget => "WAITING_FOR_BUDGET",
        _ => "UNKNOWN",
    };

    /// <summary>解析任务复杂度 / Parse a task complexity.</summary>
    /// <param name="wire">线上值 / Wire value.</param>
    /// <returns>枚举值；未识别为 null / Enum value; null when unrecognized.</returns>
    public static TaskComplexity? ParseTaskComplexity(string? wire) => wire switch {
        "SIMPLE" => TaskComplexity.Simple,
        "COMPLEX" => TaskComplexity.Complex,
        _ => null,
    };

    /// <summary>写出任务复杂度 / Write a task complexity.</summary>
    /// <param name="value">枚举值 / Enum value.</param>
    /// <returns>线上值 / Wire value.</returns>
    public static string? Write(TaskComplexity value) => value switch {
        TaskComplexity.Simple => "SIMPLE",
        TaskComplexity.Complex => "COMPLEX",
        _ => null,
    };

    /// <summary>解析时间敏感度 / Parse a time sensitivity.</summary>
    /// <param name="wire">线上值 / Wire value.</param>
    /// <returns>枚举值；未识别为 null / Enum value; null when unrecognized.</returns>
    public static TimeSensitivity? ParseTimeSensitivity(string? wire) => wire switch {
        "REALTIME" => TimeSensitivity.Realtime,
        "NON_REALTIME" => TimeSensitivity.NonRealtime,
        _ => null,
    };

    /// <summary>写出时间敏感度 / Write a time sensitivity.</summary>
    /// <param name="value">枚举值 / Enum value.</param>
    /// <returns>线上值 / Wire value.</returns>
    public static string? Write(TimeSensitivity value) => value switch {
        TimeSensitivity.Realtime => "REALTIME",
        TimeSensitivity.NonRealtime => "NON_REALTIME",
        _ => null,
    };

    /// <summary>写出问题结论 / Write a problem status.</summary>
    /// <param name="value">枚举值 / Enum value.</param>
    /// <returns>线上值；Unknown 写出为空 / Wire value; Unknown writes null.</returns>
    public static string? Write(RemoteProblemStatus value) => value switch {
        RemoteProblemStatus.Feasible => "FEASIBLE",
        RemoteProblemStatus.Infeasible => "INFEASIBLE",
        RemoteProblemStatus.Unbounded => "UNBOUNDED",
        RemoteProblemStatus.InfeasibleOrUnbounded => "INFEASIBLE_OR_UNBOUNDED",
        _ => null,
    };

    /// <summary>写出终止原因 / Write a termination reason.</summary>
    /// <param name="value">枚举值 / Enum value.</param>
    /// <returns>线上值；Unknown 写出为空 / Wire value; Unknown writes null.</returns>
    public static string? Write(RemoteTerminationReason value) => value switch {
        RemoteTerminationReason.Completed => "COMPLETED",
        RemoteTerminationReason.TimeLimit => "TIME_LIMIT",
        RemoteTerminationReason.NodeLimit => "NODE_LIMIT",
        RemoteTerminationReason.IterationLimit => "ITERATION_LIMIT",
        RemoteTerminationReason.SolutionLimit => "SOLUTION_LIMIT",
        RemoteTerminationReason.ObjectiveLimit => "OBJECTIVE_LIMIT",
        RemoteTerminationReason.Cancelled => "CANCELLED",
        RemoteTerminationReason.Interrupted => "INTERRUPTED",
        RemoteTerminationReason.NumericalFailure => "NUMERICAL_FAILURE",
        RemoteTerminationReason.BackendFailure => "BACKEND_FAILURE",
        _ => null,
    };

    /// <summary>写出解存在性 / Write a solution presence.</summary>
    /// <param name="value">枚举值 / Enum value.</param>
    /// <returns>线上值；Unknown 写出为空 / Wire value; Unknown writes null.</returns>
    public static string? Write(RemoteSolutionPresence value) => value switch {
        RemoteSolutionPresence.None => "NONE",
        RemoteSolutionPresence.Incumbent => "INCUMBENT",
        RemoteSolutionPresence.Optimal => "OPTIMAL",
        _ => null,
    };

    /// <summary>写出证明状态 / Write a proof status.</summary>
    /// <param name="value">枚举值 / Enum value.</param>
    /// <returns>线上值；Unknown 写出为空 / Wire value; Unknown writes null.</returns>
    public static string? Write(RemoteProofStatus value) => value switch {
        RemoteProofStatus.None => "NONE",
        RemoteProofStatus.Claimed => "CLAIMED",
        RemoteProofStatus.Verified => "VERIFIED",
        _ => null,
    };

    /// <summary>写出切片结论 / Write a slice outcome.</summary>
    /// <param name="value">枚举值 / Enum value.</param>
    /// <returns>线上值；Unknown 写出为空 / Wire value; Unknown writes null.</returns>
    public static string? Write(SliceOutcome value) => value switch {
        SliceOutcome.Completed => "COMPLETED",
        SliceOutcome.Preempted => "PREEMPTED",
        SliceOutcome.Checkpointed => "CHECKPOINTED",
        SliceOutcome.Resumable => "RESUMABLE",
        SliceOutcome.Cancelled => "CANCELLED",
        SliceOutcome.Failed => "FAILED",
        _ => null,
    };

    /// <summary>写出抢占模式 / Write a preemption mode.</summary>
    /// <param name="value">枚举值 / Enum value.</param>
    /// <returns>线上值；Unknown 写出为空 / Wire value; Unknown writes null.</returns>
    public static string? Write(PreemptionMode value) => value switch {
        PreemptionMode.NonPreemptible => "NON_PREEMPTIBLE",
        PreemptionMode.ControlledReturn => "CONTROLLED_RETURN",
        PreemptionMode.Native => "NATIVE",
        _ => null,
    };

    /// <summary>写出恢复模式 / Write a resume mode.</summary>
    /// <param name="value">枚举值 / Enum value.</param>
    /// <returns>线上值；Unknown 写出为空 / Wire value; Unknown writes null.</returns>
    public static string? Write(ResumeMode value) => value switch {
        ResumeMode.None => "NONE",
        ResumeMode.WarmStart => "WARM_START",
        ResumeMode.Basis => "BASIS",
        ResumeMode.NativeCheckpoint => "NATIVE_CHECKPOINT",
        _ => null,
    };
}

/// <summary>
/// 可空 wire 枚举解码器 / Nullable wire enum converter.
/// </summary>
/// <remarks>
/// 用于可缺省的枚举字段：缺失或未识别均写出 null，不伪造值。
/// Used for wire enum fields that may be absent: missing or unrecognized values write null instead of a fabricated value.
/// </remarks>
/// <typeparam name="TEnum">枚举类型 / Enum type.</typeparam>
public sealed class RemoteNullableWireEnumConverter<TEnum> : JsonConverter<TEnum?>
    where TEnum : struct, Enum {
    private readonly Func<string?, TEnum?> _parse;
    private readonly Func<TEnum, string?> _write;

    /// <summary>创建解码器 / Create the converter.</summary>
    /// <param name="parse">线上值到枚举的解析 / Wire-to-enum parse.</param>
    /// <param name="write">枚举到线上值的写出 / Enum-to-wire write.</param>
    public RemoteNullableWireEnumConverter(
        Func<string?, TEnum?> parse,
        Func<TEnum, string?> write) {
        _parse = parse;
        _write = write;
    }

    /// <inheritdoc/>
    public override TEnum? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) {
        if (reader.TokenType == JsonTokenType.Null) {
            return null;
        }
        return _parse(reader.GetString());
    }

    /// <inheritdoc/>
    public override void Write(
        Utf8JsonWriter writer,
        TEnum? value,
        JsonSerializerOptions options) {
        if (value is null) {
            writer.WriteNullValue();
            return;
        }
        string? wire = _write(value.Value);
        if (wire is null) {
            writer.WriteNullValue();
            return;
        }
        writer.WriteStringValue(wire);
    }
}

/// <summary>
/// 远程 wire 枚举解码器 / Remote wire enum converter.
/// </summary>
/// <remarks>
/// 未识别值映射到枚举的 Unknown 成员，绝不抛异常，也绝不读成成功；写回未知值输出 <c>UNKNOWN</c>。
/// Unrecognized values map to the Unknown member, never throw and never read as success; writing Unknown emits <c>UNKNOWN</c>.
/// </remarks>
/// <typeparam name="TEnum">枚举类型 / Enum type.</typeparam>
public sealed class RemoteWireEnumConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum {
    private readonly Func<string?, TEnum> _parse;
    private readonly Func<TEnum, string?> _write;
    private readonly TEnum _unknown;

    /// <summary>创建解码器 / Create the converter.</summary>
    /// <param name="parse">线上值到枚举的解析 / Wire-to-enum parse.</param>
    /// <param name="write">枚举到线上值的写出 / Enum-to-wire write.</param>
    /// <param name="unknown">未知成员 / Unknown member.</param>
    public RemoteWireEnumConverter(
        Func<string?, TEnum> parse,
        Func<TEnum, string?> write,
        TEnum unknown) {
        _parse = parse;
        _write = write;
        _unknown = unknown;
    }

    /// <inheritdoc/>
    public override TEnum Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) {
        if (reader.TokenType == JsonTokenType.Null) {
            return _unknown;
        }
        return _parse(reader.GetString());
    }

    /// <inheritdoc/>
    public override void Write(
        Utf8JsonWriter writer,
        TEnum value,
        JsonSerializerOptions options) {
        string? wire = _write(value);
        if (wire is null) {
            writer.WriteStringValue("UNKNOWN");
            return;
        }
        writer.WriteStringValue(wire);
    }
}

/// <summary>
/// 通用 wire 枚举名映射 / Generic wire enum-name mapping.
/// </summary>
/// <remarks>
/// 不可行证据的五个维度（以及 <c>BoundSide</c>）在 wire 上的名字是**枚举成员原名**
/// （`Lower`、`Verified`、`Exact`、`Complete`、`Irreducible`、`NotChecked`），因为对端直接用
/// `valueOf(name)` 比较，并不做大小写转换。因此这里不能按 SCREAMING_SNAKE 转换——那会让
/// `Verified` 变成 `VERIFIED` 而对不上。
///
/// 唯一需要显式映射的是 <c>NativeIis</c>／<c>NativeIIS</c>：C# 按 .NET 命名惯例写作 `NativeIis`，
/// wire 上写作 `NativeIIS`，两者必须统一到拼写，否则该来源的证据会被判为无效。
///
/// The five infeasibility-evidence axes (and <c>BoundSide</c>) use the **raw enum member name** on the
/// wire (`Lower`, `Verified`, `Exact`, `Complete`, `Irreducible`,
/// `NotChecked`) because the other side compares with `valueOf(name)` and never changes case. Converting to
/// SCREAMING_SNAKE here would break that (`Verified` would become `VERIFIED`).
///
/// The only explicit mapping needed is <c>NativeIis</c>/<c>NativeIIS</c>: C# follows .NET naming
/// (`NativeIis`) while the wire form is `NativeIIS`, so the wire spelling must be `NativeIIS` or
/// that evidence source would be rejected as invalid.
/// </remarks>
public static class RemoteWireEnumNames {
    private static readonly System.Collections.Generic.Dictionary<string, string> WireSpellings =
        new(StringComparer.Ordinal) {
            // C# 成员名 → wire 名 / C# member name → wire name.
            ["NativeIis"] = "NativeIIS",
        };

    private static readonly System.Collections.Generic.Dictionary<string, string> MemberSpellings =
        new(StringComparer.Ordinal) {
            ["NativeIIS"] = "NativeIis",
        };

    /// <summary>
    /// 枚举值转 wire 名 / Convert an enum value to its wire name.
    /// </summary>
    /// <typeparam name="TEnum">枚举类型 / Enum type.</typeparam>
    /// <param name="value">枚举值 / Enum value.</param>
    /// <returns>wire 名 / Wire name.</returns>
    public static string ToWireName<TEnum>(TEnum value)
        where TEnum : struct, Enum {
        string name = value.ToString();
        return WireSpellings.TryGetValue(name, out string? wire) ? wire : name;
    }

    /// <summary>
    /// 解析 wire 名 / Parse a wire name.
    /// </summary>
    /// <typeparam name="TEnum">枚举类型 / Enum type.</typeparam>
    /// <param name="name">wire 名 / Wire name.</param>
    /// <param name="value">解析结果 / Parsed value.</param>
    /// <returns>是否解析成功 / Whether parsing succeeded.</returns>
    public static bool TryParse<TEnum>(string? name, out TEnum value)
        where TEnum : struct, Enum {
        if (!string.IsNullOrEmpty(name)) {
            string member = MemberSpellings.TryGetValue(name, out string? mapped) ? mapped : name;
            foreach (TEnum candidate in Enum.GetValues<TEnum>()) {
                if (string.Equals(candidate.ToString(), member, StringComparison.Ordinal)) {
                    value = candidate;
                    return true;
                }
            }
        }
        value = default;
        return false;
    }
}
