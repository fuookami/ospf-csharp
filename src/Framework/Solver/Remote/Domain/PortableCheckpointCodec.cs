#nullable enable

using System;
using Fuookami.Ospf.Core.Solver.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Solver.Remote.Domain;

/// <summary>
/// 远端可移植 checkpoint 编解码 / Remote portable checkpoint codec.
/// </summary>
/// <remarks>
/// 与固定基线一致：本类型只做转发，字段顺序、schema 规范化、string-map 排序与摘要全部由 Core 的
/// <see cref="ConstraintProgrammingCheckpointCodec"/> 决定，框架侧不再维护第二份字段列表或摘要算法。
/// 解码入口刻意分开：<see cref="DecodeOrNull"/> 只接受当前 schema，历史格式迁移必须显式走
/// <see cref="DecodeCompatibleOrNull"/>。
/// Matches the pinned baseline: this type only forwards. Field order, schema normalization, string-map
/// ordering and the digest all come from Core's <see cref="ConstraintProgrammingCheckpointCodec"/>, and
/// no second field list or digest implementation is kept here. The decode entries stay separate:
/// <see cref="DecodeOrNull"/> accepts only the current schema, and historical migration requires
/// <see cref="DecodeCompatibleOrNull"/> explicitly.
/// </remarks>
public static class PortableCheckpointCodec {
    /// <summary>
    /// 编码 envelope 并计算完整性摘要 / Encode an envelope and compute its integrity digest.
    /// </summary>
    /// <remarks>
    /// 返回类型无法表达失败（既有公开形态必须保留），因此无法编码时显式抛 <see cref="ArgumentException"/>，
    /// 而不是静默产出一份永远无法解码的文档。
    /// The return type cannot express failure (the existing public shape must be preserved), so an
    /// envelope that cannot be encoded throws <see cref="ArgumentException"/> explicitly instead of
    /// silently producing a document that can never be decoded.
    /// </remarks>
    /// <param name="envelope">检查点信封 / Checkpoint envelope.</param>
    /// <returns>规范 JSON / Canonical JSON.</returns>
    /// <exception cref="ArgumentException">envelope 无法编码时抛出 / Thrown when the envelope cannot be encoded.</exception>
    public static string Encode(ConstraintProgrammingCheckpointEnvelope envelope) {
        ArgumentNullException.ThrowIfNull(envelope);
        Result<string, ErrorCode, Error<ErrorCode>> encoded =
            ConstraintProgrammingCheckpointCodec.Encode(envelope);
        return encoded switch {
            Ok<string, ErrorCode, Error<ErrorCode>> ok => ok.Value,
            Failed<string, ErrorCode, Error<ErrorCode>> failed => throw new ArgumentException(
                $"可移植 checkpoint 编码失败 / Portable checkpoint encoding failed: {failed.Error.Message}"),
            Fatal<string, ErrorCode, Error<ErrorCode>> fatal => throw new ArgumentException(
                "可移植 checkpoint 编码失败 / Portable checkpoint encoding failed: "
                + string.Join("; ", System.Linq.Enumerable.Select(fatal.Errors, error => error.Message ?? string.Empty))),
            _ => throw new ArgumentException(
                "可移植 checkpoint 编码失败 / Portable checkpoint encoding failed"),
        };
    }

    /// <summary>
    /// 仅解码当前 schema 的已验证 envelope / Decode only a verified envelope of the current schema.
    /// </summary>
    /// <param name="encoded">规范 JSON / Canonical JSON.</param>
    /// <returns>envelope 或 null / Envelope, or null.</returns>
    public static ConstraintProgrammingCheckpointEnvelope? DecodeOrNull(string encoded) {
        if (encoded is null) {
            return null;
        }
        Result<
            ConstraintProgrammingCheckpointEnvelope,
            ErrorCode,
            Error<ErrorCode>> decoded = ConstraintProgrammingCheckpointCodec.Decode(encoded);
        return decoded is Ok<
            ConstraintProgrammingCheckpointEnvelope,
            ErrorCode,
            Error<ErrorCode>> ok
            ? ok.Value
            : null;
    }

    /// <summary>
    /// 解码当前 schema、已发布的 schema 2.0 形状或 legacy v1 形状 / Decode the current schema, the published schema 2.0 shape or the legacy v1 shape.
    /// </summary>
    /// <param name="encoded">JSON 文本 / JSON text.</param>
    /// <returns>envelope 或 null / Envelope, or null.</returns>
    public static ConstraintProgrammingCheckpointEnvelope? DecodeCompatibleOrNull(string encoded) {
        if (encoded is null) {
            return null;
        }
        Result<
            ConstraintProgrammingCheckpointEnvelope,
            ErrorCode,
            Error<ErrorCode>> decoded =
            ConstraintProgrammingCheckpointCodec.DecodeCompatible(encoded);
        return decoded is Ok<
            ConstraintProgrammingCheckpointEnvelope,
            ErrorCode,
            Error<ErrorCode>> ok
            ? ok.Value
            : null;
    }

    /// <summary>
    /// 计算 UTF-8 内容的 SHA-256 摘要 / Compute the SHA-256 digest of UTF-8 content.
    /// </summary>
    /// <param name="value">待摘要文本 / Text to digest.</param>
    /// <returns>小写十六进制摘要 / Lowercase hexadecimal digest.</returns>
    public static string Sha256(string value) {
        ArgumentNullException.ThrowIfNull(value);
        return SolveFingerprinting.Sha256(value).Value;
    }
}
