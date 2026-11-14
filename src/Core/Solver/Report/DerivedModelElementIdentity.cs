#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Fuookami.Ospf.Core.Solver.Report;

/// <summary>派生模型元素身份构造 / Derived model-element identity construction.</summary>
public static class DerivedModelElementIdentity {
    private static readonly Encoding IdentityEncoding = Encoding.GetEncoding(
        name: "utf-8",
        encoderFallback: new EncoderReplacementFallback("?"),
        decoderFallback: DecoderFallback.ReplacementFallback
    );
    /// <summary>由源模型元素构造确定性身份 / Build a deterministic identity from a source element.</summary>
    /// <remarks>格式无效的 UTF-16 不可安全映射为稳定身份，因而降为 model-local。 / Ill-formed UTF-16 cannot safely identify a stable element and is downgraded to model-local.</remarks>
    /// <param name="kind">派生元素类别 / Generated element kind.</param>
    /// <param name="role">派生语义角色 / Semantic role.</param>
    /// <param name="sourceId">源元素 ID / Source element identifier.</param>
    /// <param name="sourceScope">源身份作用域 / Source identity scope.</param>
    /// <param name="sourceOrigin">源主来源 / Source primary origin.</param>
    /// <param name="sourceProvenance">源完整来源 / Source complete provenance.</param>
    /// <param name="identityNamespace">身份命名空间 / Identity namespace.</param>
    /// <param name="identitySchemaVersion">身份 schema 版本 / Identity schema version.</param>
    /// <param name="discriminator">确定性区分键 / Deterministic discriminator.</param>
    /// <returns>派生元素身份 / Generated element identity.</returns>
    public static ModelElementIdentity Create(
        ModelElementKind kind,
        string role,
        string? sourceId,
        ModelElementScope sourceScope,
        ModelElementOrigin? sourceOrigin,
        IReadOnlyList<ModelElementOrigin>? sourceProvenance,
        string? identityNamespace,
        string? identitySchemaVersion,
        string discriminator = "0") {
        string sourceKey = sourceId ?? $"model-local:{discriminator}";
        string safeRole = EncodeRole(
            string.IsNullOrWhiteSpace(role) ? "generated" : role.Trim());
        string encodedSourceKey = EncodeIdentityPart(sourceKey);
        string encodedDiscriminator = EncodeIdentityPart(discriminator);
        bool sourceClaimsStableIdentity =
            sourceScope == ModelElementScope.Stable &&
            !string.IsNullOrWhiteSpace(sourceId) &&
            IsWellFormedUtf16(sourceId) && (role is null || IsWellFormedUtf16(role)) && IsWellFormedUtf16(discriminator) &&
            !sourceId.StartsWith("model-local-", StringComparison.Ordinal) &&
            (!sourceId.StartsWith("artifact:", StringComparison.Ordinal) ||
                IsGeneratedArtifactModelElementId(sourceId));
        string generatedPrefix = sourceClaimsStableIdentity
            ? "artifact"
            : "model-local-derived";
        ModelElementId generatedId = new(
            $"{generatedPrefix}:{safeRole}:{kind.ToString().ToLowerInvariant()}:{encodedSourceKey}:{encodedDiscriminator}");
        IReadOnlyList<ModelElementOrigin> provenance = sourceClaimsStableIdentity
            ? (sourceProvenance ?? Array.Empty<ModelElementOrigin>())
                .Append(sourceOrigin)
                .Where(origin => origin is not null)
                .Select(origin => origin!)
                .Distinct()
                .OrderBy(origin => origin.Kind, StringComparer.Ordinal)
                .ThenBy(origin => origin.Key, StringComparer.Ordinal)
                .ToList()
            : Array.Empty<ModelElementOrigin>();
        return new ModelElementIdentity(
            Kind: kind,
            Id: generatedId,
            Scope: sourceClaimsStableIdentity
                ? ModelElementScope.Stable
                : ModelElementScope.ModelLocal,
            Origin: sourceClaimsStableIdentity ? sourceOrigin : null,
            Provenance: provenance);
    }

    /// <summary>校验生成的 artifact ID 语法 / Validate generated artifact ID grammar.</summary>
    /// <param name="id">待校验 ID / Identifier to validate.</param>
    /// <param name="expectedKind">期望类别 / Expected kind.</param>
    /// <returns>是否为合法派生 artifact ID / Whether the ID is a valid generated artifact ID.</returns>
    public static bool IsGeneratedArtifactModelElementId(
        string id,
        ModelElementKind? expectedKind = null) {
        string[] parts = id.Split(':');
        if (parts.Length != 5 || parts[0] != "artifact") {
            return false;
        }
        if (!IsEncodedRole(parts[1]) ||
            !IsEncodedIdentityPart(parts[3]) ||
            !IsEncodedIdentityPart(parts[4])) {
            return false;
        }
        ModelElementKind? parsedKind = parts[2] switch {
            "variable" => ModelElementKind.Variable,
            "constraint" => ModelElementKind.Constraint,
            "objective" => ModelElementKind.Objective,
            _ => null,
        };
        return parsedKind is not null &&
            (expectedKind is null || parsedKind == expectedKind);
    }

    internal static string EncodeIdentityPart(string value) {
        byte[] bytes = IdentityEncoding.GetBytes(value);
        return $"{bytes.Length}h{Convert.ToHexStringLower(bytes)}";
    }

    internal static bool IsWellFormedUtf16(string value) {
        for (int i = 0; i < value.Length; i++) {
            if (char.IsHighSurrogate(value[i])) {
                if (++i >= value.Length || !char.IsLowSurrogate(value[i])) {
                    return false;
                }
            }
            else if (char.IsLowSurrogate(value[i])) {
                return false;
            }
        }
        return true;
    }

    private static string EncodeRole(string value) {
        bool safe = value.Length > 0 &&
            value[0] != '~' &&
            value.All(character =>
                character is >= 'A' and <= 'Z' ||
                character is >= 'a' and <= 'z' ||
                character is >= '0' and <= '9' ||
                character is '.' or '_' or '-');
        return safe ? value : $"~{EncodeIdentityPart(value)}";
    }

    private static bool IsEncodedRole(string value) =>
        value.StartsWith('~')
            ? IsEncodedIdentityPart(value[1..])
            : value.Length > 0 && value.All(character =>
                character is >= 'A' and <= 'Z' ||
                character is >= 'a' and <= 'z' ||
                character is >= '0' and <= '9' ||
                character is '.' or '_' or '-');

    private static bool IsEncodedIdentityPart(string value) {
        int separator = value.IndexOf('h', StringComparison.Ordinal);
        if (separator <= 0 ||
            !int.TryParse(value[..separator], out int byteCount)) {
            return false;
        }
        string hexadecimal = value[(separator + 1)..];
        return hexadecimal.Length == byteCount * 2 &&
            hexadecimal.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    }
}
