// 远程 CP 结果工件读取 / Remote CP result-artifact loading.
// 与`readSerializedSolution` 对应，但**只实现与序列化格式无关的部分**，
// 原因见 `ReadSerializedCpResultAsync` 的 remarks。
// `readSerializedSolution` but implements only the parts that
// do not depend on the serialization format; see `ReadSerializedCpResultAsync` for why.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Fuookami.Ospf.Framework.Solver.Remote.Domain;
using Fuookami.Ospf.Framework.Solver.Remote.Port;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Solver.Remote.Client;

/// <summary>
/// 远程 CP 结果工件的读取器 / Reader for remote CP result artifacts.
/// </summary>
/// <remarks>
/// 独立成文件而不是塞进 <see cref="RemoteReportMapping"/>：本类需要对象存储与异步 IO，而
/// <see cref="RemoteReportMapping"/> 是纯函数映射层，混进 IO 会让那一层的可测性变差。
/// Kept separate from <see cref="RemoteReportMapping"/> because this type needs object storage and async
/// IO, whereas <see cref="RemoteReportMapping"/> is a pure mapping layer; mixing IO into it would hurt
/// testability.
/// </remarks>
public static class RemoteCpResultArtifact {
    /// <summary>严格 v2 artifact 必须存在的审计字段 / Audit fields a strict v2 artifact must contain.</summary>
    private static readonly string[] _requiredAuditFields = {
        "schemaVersion",
        "feasible",
        "optimal",
        "objectiveValue",
        "objectiveValueInt64",
        "elapsedMs",
        "message",
        "variableValuesById",
        "intervalValues",
        "problemStatus",
        "solutionPresence",
        "proofStatus",
        "terminationReason",
        "gap",
        "runId",
        "attemptId",
        "artifactDigest",
        "fingerprints",
        "fingerprintSchemas",
        "provenance",
        "statistics",
        "diagnostics",
    };

    /// <summary>严格 v2 artifact 必须非空的审计字段 / Audit fields a strict v2 artifact must not leave blank.</summary>
    private static readonly string[] _requiredNonNullFields = {
        "schemaVersion",
        "problemStatus",
        "solutionPresence",
        "proofStatus",
        "terminationReason",
        "runId",
        "attemptId",
        "artifactDigest",
    };

    /// <summary>
    /// 读取稳定解工件 / Load the stable-solution artifact.
    /// </summary>
    /// <remarks>
    /// 的骨架：无 <c>resultRef</c> 时返回 <c>null</c>（不是错误——旧后端可能不下发）；有引用但
    /// 未配置对象存储、对象不存在、读取抛错，都各自给出结构化错误，而不是当成"没有结果"。
    ///
    /// **刻意未实现：摘要复算。** 摘要取自 `Json { encodeDefaults = true }` 序列化后的 SHA-256，
    /// 该规范化形式对 `SerializedSolution` 的**字段声明顺序**、默认值集合、`Flt64` 自定义序列化与枚举
    /// 命名都敏感。本环境无法运行对端实现，因此无法验证 C# 复现出的字节与之逐字节相同。若照自己对
    /// 字段顺序的理解实现复算，一旦顺序假设有误，就会把**合法的**产物判为损坏——这比不校验更糟。
    /// 因此这里只做与格式无关的校验：结构完善性、v2 标记与 schema 版本冲突、以及工件声明的摘要与原始结果
    /// 声明的摘要一致。**摘要复算在跨语言实测完成前不作为能力宣称。**
    ///
    /// The skeleton matches the code: no <c>resultRef</c> yields <c>null</c> rather than an error, since
    /// a legacy backend may not emit one; a reference without configured storage, a missing object and a
    /// failed read each produce a structured error instead of being treated as "no result".
    ///
    /// **Deliberately not implemented: digest recomputation.** The digest is the SHA-256 of a
    /// `Json { encodeDefaults = true }` serialization; that canonical form is sensitive to the **declaration
    /// order** of `SerializedSolution`'s fields, its default-value set, `Flt64`'s custom serializer and enum
    /// naming. The other implementation cannot be executed here, so C# reproducing its bytes cannot be verified. Guessing
    /// the field order and shipping a recomputation would, if the guess is wrong, reject *valid*
    /// artifacts — worse than not checking at all. Only format-independent checks are performed: structural
    /// completeness, v2 markers versus legacy schema, and agreement between the artifact's declared digest
    /// and the one declared by the raw result. Digest recomputation is **not** claimed as a capability until
    /// it has been measured across languages.
    /// </remarks>
    /// <param name="slice">原始切片结果 / Raw slice result.</param>
    /// <param name="storage">对象存储端口，可为空 / Object-storage port, may be null.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>
    /// 稳定解工件；无结果引用时为 <c>null</c> / The artifact, or <c>null</c> when there is no result reference.
    /// </returns>
    public static async Task<Result<SerializedSolution?, ErrorCode, Error<ErrorCode>>>
        ReadSerializedCpResultAsync(
            SliceResult slice,
            IObjectStoragePort? storage,
            CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(slice);

        ObjectRef? reference = slice.ResultRef;
        if (reference is null) {
            return Results.Ok<SerializedSolution?>(null);
        }
        if (storage is null) {
            return Invalid<SerializedSolution?>(
                "远程 CP 结果引用存在但未配置对象存储 / "
                + "Remote CP resultRef exists but no object storage is configured");
        }

        Result<StoredObject, ErrorCode, Error<ErrorCode>> stored;
        try {
            stored = await storage.GetAsync(reference, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            return Failed<SerializedSolution?>(
                ErrorCode.ApplicationFailed,
                $"读取远程 CP 结果失败：{error.Message} / Failed to read remote CP result: {error.Message}");
        }
        if (!stored.IsOk) {
            return Failed<SerializedSolution?>(
                ErrorCode.ORSolutionInvalid,
                $"读取远程 CP 结果失败：{reference.Path.Value} / "
                + $"Failed to read remote CP result: {reference.Path.Value}");
        }
        byte[] bytes = stored.Value.Data;
        if (bytes is null || bytes.Length == 0) {
            // 存储层返回空对象等同于"不存在"：没有内容就没有可物化的解。
            // An empty object from storage is equivalent to "does not exist": with no content there is no
            // solution to materialize.
            return Invalid<SerializedSolution?>(
                $"远程 CP 结果对象不存在：{reference.Path.Value} / "
                + $"Remote CP result object does not exist: {reference.Path.Value}");
        }

        string raw = System.Text.Encoding.UTF8.GetString(bytes);
        JsonDocument document;
        try {
            document = JsonDocument.Parse(raw);
        }
        catch (JsonException error) {
            return Invalid<SerializedSolution?>(
                $"远程 CP 结果 artifact 不是合法 JSON：{error.Message} / "
                + $"Remote CP result artifact is not valid JSON: {error.Message}");
        }

        using (document) {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) {
                return Invalid<SerializedSolution?>(
                    "远程 CP 结果 artifact 不是 JSON 对象 / "
                    + "Remote CP result artifact is not a JSON object");
            }

            SerializedSolution? serialized;
            try {
                serialized = JsonSerializer.Deserialize<SerializedSolution>(
                    raw, RemoteWireJson.Options);
            }
            catch (JsonException error) {
                return Invalid<SerializedSolution?>(
                    $"远程 CP 结果 artifact 无法解码：{error.Message} / "
                    + $"Remote CP result artifact cannot be decoded: {error.Message}");
            }
            if (serialized is null) {
                return Invalid<SerializedSolution?>(
                    "远程 CP 结果 artifact 解码为空 / "
                    + "Remote CP result artifact decoded to null");
            }

            int? artifactMajor = MajorVersionOf(
                root.TryGetProperty("schemaVersion", out JsonElement schemaElement)
                    ? schemaElement.GetString()
                    : null);
            bool hasNonEmptyFingerprintSchemas =
                root.TryGetProperty("fingerprintSchemas", out JsonElement schemas) &&
                schemas.ValueKind == JsonValueKind.Object &&
                schemas.EnumerateObject().Any();
            bool hasV2Markers = (artifactMajor is { } major && major >= 2) ||
                hasNonEmptyFingerprintSchemas;
            if (hasV2Markers && artifactMajor is { } legacy && legacy < 2) {
                return Invalid<SerializedSolution?>(
                    "远程 CP artifact 的 v2 字段与 schema 版本冲突 / "
                    + "Remote CP artifact has v2 fields with a legacy schema");
            }

            bool strict = MajorVersionOf(slice.SchemaVersion) is { } sliceMajor && sliceMajor >= 2;
            if (strict || hasV2Markers) {
                string[] missing = _requiredAuditFields
                    .Where(field => !root.TryGetProperty(field, out JsonElement _))
                    .ToArray();
                if (missing.Length > 0) {
                    return Invalid<SerializedSolution?>(
                        $"严格 v2 结果 artifact 缺少审计字段：{string.Join(",", missing)} / "
                        + $"Strict v2 result artifact is missing audit fields: {string.Join(",", missing)}");
                }

                var blank = new List<string>();
                foreach (string field in _requiredNonNullFields) {
                    if (!root.TryGetProperty(field, out JsonElement value) ||
                        value.ValueKind == JsonValueKind.Null ||
                        (value.ValueKind == JsonValueKind.String &&
                            string.IsNullOrWhiteSpace(value.GetString()))) {
                        blank.Add(field);
                    }
                }
                if (blank.Count > 0) {
                    return Invalid<SerializedSolution?>(
                        $"严格 v2 结果 artifact 含空审计字段：{string.Join(",", blank.Distinct())} / "
                        + $"Strict v2 result artifact contains blank audit fields: "
                        + $"{string.Join(",", blank.Distinct())}");
                }
            }

            if (string.IsNullOrWhiteSpace(serialized.ArtifactDigest)) {
                return Invalid<SerializedSolution?>(
                    "远程 CP 结果 artifact 缺少摘要 / "
                    + "Remote CP result artifact is missing its digest");
            }
            // 摘要复算未实现（原因见方法 remarks）；这里只校验"工件自称的摘要"与"原始结果自称的摘要"
            // 是否是同一个值——两者不一致说明拿到的工件不是这次结果引用的那一份。
            // Digest recomputation is not implemented (see the remarks); this only checks that the digest
            // the artifact claims equals the one the raw result claims. A mismatch means the loaded artifact
            // is not the one this result referenced.
            if (slice.ArtifactDigest is { } expectedDigest &&
                !string.Equals(serialized.ArtifactDigest, expectedDigest, StringComparison.Ordinal)) {
                return Invalid<SerializedSolution?>(
                    $"远程 CP 结果 artifact 摘要与结果声明不一致：artifact={serialized.ArtifactDigest}, "
                    + $"result={expectedDigest} / "
                    + "Remote CP result artifact digest disagrees with the result's declared digest");
            }
            return Results.Ok<SerializedSolution?>(serialized);
        }
    }

    private static int? MajorVersionOf(string? schemaVersion) {
        if (string.IsNullOrWhiteSpace(schemaVersion)) {
            return null;
        }
        int separator = schemaVersion.IndexOf('.', StringComparison.Ordinal);
        string major = separator < 0 ? schemaVersion : schemaVersion[..separator];
        return int.TryParse(
            major,
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out int parsed)
            ? parsed
            : null;
    }

    private static Result<T, ErrorCode, Error<ErrorCode>> Invalid<T>(string message) =>
        Results.Failed<T>(new Err<ErrorCode>(ErrorCode.ORSolutionInvalid, message));

    private static Result<T, ErrorCode, Error<ErrorCode>> Failed<T>(
        ErrorCode code,
        string message) =>
        Results.Failed<T>(new Err<ErrorCode>(code, message));
}
