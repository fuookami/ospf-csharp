#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Solver.Config;

namespace Fuookami.Ospf.Core.Solver.Report;

/// <summary>
/// 由求解器配置构造可审计的快照与运行来源 / Build auditable snapshots and provenance from solver configuration.
/// </summary>
/// <remarks>
/// 时间与间隙等数值统一使用不变文化的十进制秒文本；Duration 文本（如 "30s"）不在这个形状里，
/// 因此配置指纹尚未互通，属 P4 互通项。
/// Time and gap values use invariant decimal-second text rather than Duration text ("30s"),
/// so configuration fingerprints are not yet interoperable and remain a P4 interop item.
/// </remarks>
public static class SolverReportMetadata {
    /// <summary>构造脱敏配置快照 / Build a redacted configuration snapshot.</summary>
    /// <param name="config">求解器配置 / Solver configuration.</param>
    /// <returns>配置快照 / Configuration snapshot.</returns>
    public static BackendConfigurationSnapshot ConfigurationSnapshot(this SolverConfig config) {
        var parameters = new List<BackendParameter> {
            new("time", new BackendParameterValue.Text(Seconds(config.TimeLimit))),
            new("threadNum", new BackendParameterValue.Integer(unchecked((long)config.ThreadCount))),
            new("gap", new BackendParameterValue.Decimal(Decimal(config.Gap))),
            new("improveThreshold", new BackendParameterValue.Decimal(Decimal(config.ImproveThreshold))),
        };
        if (config.NotImprovementTime is { } notImprovementTime) {
            parameters.Add(new BackendParameter(
                "notImprovementTime",
                new BackendParameterValue.Text(Seconds(notImprovementTime))));
        }
        if (config.InterruptibleTime is { } interruptibleTime) {
            parameters.Add(new BackendParameter(
                "interruptibleTime",
                new BackendParameterValue.Text(Seconds(interruptibleTime))));
        }
        if (config.InterruptibleGap is { } interruptibleGap) {
            parameters.Add(new BackendParameter(
                "interruptibleGap",
                new BackendParameterValue.Decimal(Decimal(interruptibleGap))));
        }
        foreach (BackendParameter parameter in BackendParameters(config)) {
            parameters.Add(parameter with { Name = $"backend.{parameter.Name}" });
        }
        parameters.Sort((left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
        return new BackendConfigurationSnapshot(
            Type: config.BackendConfiguration?.Type ?? "solver",
            Parameters: parameters);
    }

    /// <summary>构造后端求解运行来源 / Build auditable provenance for a backend solve.</summary>
    /// <param name="config">生效配置 / Effective configuration.</param>
    /// <param name="descriptor">后端描述符 / Backend descriptor.</param>
    /// <param name="nativeVersion">原生库版本，缺省取描述符版本 / Native library version, defaulting to the descriptor version.</param>
    /// <param name="ignoredParameters">已声明但未生效的参数 / Requested parameters that were not applied.</param>
    /// <returns>运行来源 / Solve provenance.</returns>
    public static SolverProvenance Provenance(
        this SolverConfig config,
        SolverDescriptor descriptor,
        string? nativeVersion = null,
        IReadOnlyDictionary<string, string>? ignoredParameters = null) {
        var effective = new SortedDictionary<string, string>(StringComparer.Ordinal) {
            ["time"] = Seconds(config.TimeLimit),
            ["threadNum"] = config.ThreadCount.ToString(CultureInfo.InvariantCulture),
            ["gap"] = Decimal(config.Gap),
            ["improveThreshold"] = Decimal(config.ImproveThreshold),
        };
        if (config.NotImprovementTime is { } notImprovementTime) {
            effective["notImprovementTime"] = Seconds(notImprovementTime);
        }
        if (config.InterruptibleTime is { } interruptibleTime) {
            effective["interruptibleTime"] = Seconds(interruptibleTime);
        }
        if (config.InterruptibleGap is { } interruptibleGap) {
            effective["interruptibleGap"] = Decimal(interruptibleGap);
        }
        foreach (BackendParameter parameter in BackendParameters(config)) {
            effective[$"backend.{parameter.Name}"] = parameter.RedactedValue();
        }
        var scip = config.BackendConfiguration as ScipSolverConfig;
        return new SolverProvenance(
            Descriptor: descriptor,
            NativeVersion: nativeVersion ?? descriptor.BackendVersion,
            EffectiveParameters: effective,
            IgnoredParameters: ignoredParameters,
            Configuration: config.ConfigurationSnapshot(),
            ThreadCount: config.NativeThreadCount,
            RandomSeed: scip?.RandomSeed,
            Deterministic: scip?.Deterministic);
    }

    private static IReadOnlyList<BackendParameter> BackendParameters(SolverConfig config) =>
        config.BackendConfiguration?.Parameters() ?? Array.Empty<BackendParameter>();

    private static string Seconds(TimeSpan value) =>
        value.TotalSeconds.ToString(CultureInfo.InvariantCulture);

    private static string Decimal(Flt64 value) =>
        value.ToDouble().ToString(CultureInfo.InvariantCulture);
}
