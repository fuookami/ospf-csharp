#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Fuookami.Ospf.Core.Solver.Report;

/// <summary>确定性 SHA-256 指纹工具 / Deterministic SHA-256 fingerprint utility.</summary>
public static class SolveFingerprinting {
    /// <summary>对已规范化 UTF-8 内容生成指纹 / Fingerprint canonical UTF-8 content.</summary>
    /// <param name="canonicalContent">规范化内容 / Canonical content.</param>
    /// <param name="schemaVersion">schema 版本 / Schema version.</param>
    /// <returns>审计指纹 / Audit fingerprint.</returns>
    public static AuditFingerprint Sha256(
        string canonicalContent,
        string schemaVersion = "1.0") {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalContent));
        return new AuditFingerprint(
            SchemaVersion: schemaVersion,
            Algorithm: "SHA-256",
            Value: Convert.ToHexStringLower(bytes));
    }

    /// <summary>规范化键值配置后生成指纹 / Fingerprint canonicalized key-value configuration.</summary>
    /// <param name="parameters">配置参数 / Configuration parameters.</param>
    /// <param name="schemaVersion">schema 版本 / Schema version.</param>
    /// <returns>配置指纹 / Configuration fingerprint.</returns>
    public static AuditFingerprint Configuration(
        IReadOnlyDictionary<string, string> parameters,
        string schemaVersion = "1.0") {
        string canonical = string.Join(
            separator: "\n",
            values: parameters
                .OrderBy(parameter => parameter.Key, StringComparer.Ordinal)
                .Select(parameter => $"{Escape(parameter.Key)}={Escape(parameter.Value)}"));
        return Sha256(canonical, schemaVersion);
    }

    /// <summary>生成求解器描述符指纹 / Fingerprint a solver descriptor.</summary>
    /// <param name="descriptor">求解器描述符 / Solver descriptor.</param>
    /// <returns>求解器指纹 / Solver fingerprint.</returns>
    public static AuditFingerprint Solver(SolverDescriptor descriptor) {
        SolverCapabilities capabilities = descriptor.Capabilities;
        string modelTypes = string.Join(
            separator: ",",
            values: capabilities.ModelTypes
                .Select(modelType => modelType.ToString())
                .OrderBy(name => name, StringComparer.Ordinal));
        string constraintProgrammingFeatures = string.Join(
            separator: ",",
            values: capabilities.ConstraintProgrammingFeatures
                .OrderBy(feature => feature.Key.ToString(), StringComparer.Ordinal)
                .Select(feature => $"{feature.Key}={feature.Value}"));
        string canonical = string.Join(
            separator: "\n",
            values: new[] {
                $"solverId={descriptor.SolverId}",
                $"backendName={descriptor.BackendName}",
                $"backendVersion={descriptor.BackendVersion ?? string.Empty}",
                $"pluginVersion={descriptor.PluginVersion ?? string.Empty}",
                $"modelTypes={modelTypes}",
                $"nativeIIS={capabilities.NativeIis.ToString(CultureInfo.InvariantCulture)}",
                $"dual={capabilities.Dual.ToString(CultureInfo.InvariantCulture)}",
                $"farkas={capabilities.Farkas.ToString(CultureInfo.InvariantCulture)}",
                $"warmStart={capabilities.WarmStart.ToString(CultureInfo.InvariantCulture)}",
                $"solutionPool={capabilities.SolutionPool.ToString(CultureInfo.InvariantCulture)}",
                $"callback={capabilities.Callback.ToString(CultureInfo.InvariantCulture)}",
                $"interrupt={capabilities.Interrupt.ToString(CultureInfo.InvariantCulture)}",
                $"checkpoint={capabilities.Checkpoint.ToString(CultureInfo.InvariantCulture)}",
                $"resume={capabilities.Resume.ToString(CultureInfo.InvariantCulture)}",
                $"constraintProgrammingFeatures={constraintProgrammingFeatures}",
            });
        return Sha256(canonical, "solver-2");
    }

    private static string Escape(string value) =>
        value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("=", "\\=", StringComparison.Ordinal);
}
