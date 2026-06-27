#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;

/// <summary>
/// Benders 质量保护配置。Benders quality guard configuration.
/// </summary>
public sealed record BendersQualityGuardConfig(
    double WeakGapMultiplier = 20.0,
    double WeakGapFloor = 1e-5,
    int IterationPressurePercent = 90,
    int CutDensityMinIterations = 8,
    double CutDensityThreshold = 0.25,
    int TrajectoryMinSnapshots = 6,
    double TrajectoryStepMultiplier = 20.0,
    double TrajectoryStepFloor = 1e-6,
    long TimeGuardMinMs = 500,
    double ScoreGapWeight = 0.35,
    double ScoreTimeWeight = 0.2,
    double ScoreIterationWeight = 0.2,
    double ScoreCutDensityWeight = 0.15,
    double ScoreTrajectoryWeight = 0.1);

/// <summary>
/// Benders 策略工具类。Benders strategy utility class for solve mode resolution and quality guards.
/// </summary>
public static class BendersStrategy
{
    private const string BendersQualityReasonGapGuardExceeded = "gap_guard_exceeded";
    private const string BendersQualityReasonTimeGuardExceeded = "time_guard_exceeded";
    private const string BendersQualityReasonProgressGuardTriggered = "progress_guard_triggered";
    private const string BendersQualityReasonCutEfficiencyLow = "cut_efficiency_low";
    private const string BendersQualityReasonTrajectoryWeak = "trajectory_weak";

    /// <summary>
    /// 判断飞机类型是否支持 Benders。Check if aircraft type supports Benders decomposition.
    /// </summary>
    public static bool SupportedAircraft(AircraftTypeInput aircraftType) =>
        aircraftType == AircraftTypeInput.B737 || aircraftType == AircraftTypeInput.B757;

    /// <summary>
    /// 调优自适应配置。Tune adaptive configuration based on problem size.
    /// </summary>
    public static EffectiveBendersAdaptiveConfig TuneAdaptiveConfig(BendersAdaptiveConfig configured, int binaryVariables)
    {
        if (configured.MaxIterations == 0)
        {
            return new EffectiveBendersAdaptiveConfig(
                configured.MinBinaryVariables,
                configured.MaxIterations,
                configured.Tolerance);
        }

        int iterationBoost = binaryVariables switch
        {
            >= 400 => 64,
            >= 200 => 32,
            >= 100 => 16,
            >= 60 => 8,
            _ => 0
        };

        double baseTolerance = configured.Tolerance > 0.0 ? configured.Tolerance : 1e-6;
        double tunedTolerance = binaryVariables switch
        {
            >= 400 => global::System.Math.Max(baseTolerance, 5e-5),
            >= 200 => global::System.Math.Max(baseTolerance, 2e-5),
            >= 100 => global::System.Math.Max(baseTolerance, 1e-5),
            >= 60 => global::System.Math.Max(baseTolerance, 5e-6),
            _ => baseTolerance
        };

        int tunedMaxIterations = configured.MaxIterations + iterationBoost;
        int stallWindowBase = binaryVariables switch
        {
            >= 400 => 24,
            >= 200 => 16,
            >= 100 => 12,
            >= 60 => 8,
            _ => 6
        };
        int objectiveStallWindowBase = binaryVariables switch
        {
            >= 400 => 6,
            >= 200 => 5,
            >= 100 => 4,
            >= 60 => 3,
            _ => 2
        };

        return new EffectiveBendersAdaptiveConfig(
            configured.MinBinaryVariables,
            tunedMaxIterations,
            tunedTolerance,
            global::System.Math.Min(stallWindowBase, global::System.Math.Max(tunedMaxIterations, 1)),
            global::System.Math.Min(objectiveStallWindowBase, global::System.Math.Max(tunedMaxIterations, 1)));
    }

    /// <summary>
    /// 解析求解模式。Resolve solve mode from request.
    /// </summary>
    public static SolveMode ResolveSolveMode(RequestDTO request, List<string> notes)
    {
        int binaryVariables = request.Cargos.Count * request.Positions.Count;
        var tunedAdaptive = TuneAdaptiveConfig(request.ResolvedBendersAdaptive, binaryVariables);
        var qualityGuard = ResolveQualityGuardConfig(request.BendersQualityOverrides);

        if (request.ResolvedSolvePolicy.PreferBenders)
        {
            if (binaryVariables < tunedAdaptive.MinBinaryVariables)
            {
                notes.Add($"Benders requested but skipped: binary_variables={binaryVariables} < threshold={tunedAdaptive.MinBinaryVariables}");
                return new SolveMode.Milp();
            }
            notes.Add("Benders requested; adaptive Benders path enabled in application layer");
            notes.Add($"benders_adaptive=min_binary_variables={request.ResolvedBendersAdaptive.MinBinaryVariables},max_iterations={request.ResolvedBendersAdaptive.MaxIterations},tolerance={request.ResolvedBendersAdaptive.Tolerance:F6}");
            notes.Add($"benders_adaptive_effective=min_binary_variables={tunedAdaptive.MinBinaryVariables},max_iterations={tunedAdaptive.MaxIterations},tolerance={tunedAdaptive.Tolerance:F6},max_stall_iterations={tunedAdaptive.MaxStallIterations?.ToString() ?? "none"},objective_stall_iterations={tunedAdaptive.ObjectiveStallIterations?.ToString() ?? "none"}");
            DiagnosticsHelper.PushGroupedNote(
                notes, DiagnosticsHelper.LevelDiagnostic, DiagnosticsHelper.GroupSolver,
                DiagnosticsHelper.CodeBendersAdaptiveEffective,
                $"effective min_binary_variables={tunedAdaptive.MinBinaryVariables},max_iterations={tunedAdaptive.MaxIterations},tolerance={tunedAdaptive.Tolerance:F6},max_stall_iterations={tunedAdaptive.MaxStallIterations?.ToString() ?? "none"},objective_stall_iterations={tunedAdaptive.ObjectiveStallIterations?.ToString() ?? "none"}");
            notes.Add($"benders_problem_size_binary_variables={binaryVariables}");
            DiagnosticsHelper.PushGroupedNote(
                notes, DiagnosticsHelper.LevelDiagnostic, DiagnosticsHelper.GroupSolver,
                DiagnosticsHelper.CodeBendersProblemSizeBinaryVariables,
                $"binary_variables={binaryVariables}");
            return new SolveMode.Benders(tunedAdaptive);
        }
        return new SolveMode.Milp();
    }

    /// <summary>
    /// 解析质量保护配置。Resolve quality guard configuration.
    /// </summary>
    public static BendersQualityGuardConfig ResolveQualityGuardConfig(BendersQualityOverrideConfig? @override)
    {
        var @default = new BendersQualityGuardConfig();
        if (@override == null) return @default;

        var rawWeights = new[]
        {
            @override.ScoreGapWeight ?? @default.ScoreGapWeight,
            @override.ScoreTimeWeight ?? @default.ScoreTimeWeight,
            @override.ScoreIterationWeight ?? @default.ScoreIterationWeight,
            @override.ScoreCutDensityWeight ?? @default.ScoreCutDensityWeight,
            @override.ScoreTrajectoryWeight ?? @default.ScoreTrajectoryWeight
        };
        double weightSum = 0;
        foreach (var w in rawWeights) weightSum += w;
        var normalizedWeights = weightSum <= 1e-12
            ? new[] { 0.35, 0.2, 0.2, 0.15, 0.1 }
            : Array.ConvertAll(rawWeights, w => w / weightSum);

        return new BendersQualityGuardConfig(
            WeakGapMultiplier: global::System.Math.Max(@override.WeakGapMultiplier ?? @default.WeakGapMultiplier, 1.0),
            WeakGapFloor: global::System.Math.Max(@override.WeakGapFloor ?? @default.WeakGapFloor, 1e-12),
            IterationPressurePercent: global::System.Math.Clamp(@override.IterationPressurePercent ?? @default.IterationPressurePercent, 1, 100),
            CutDensityMinIterations: global::System.Math.Max(@override.CutDensityMinIterations ?? @default.CutDensityMinIterations, 1),
            CutDensityThreshold: global::System.Math.Max(@override.CutDensityThreshold ?? @default.CutDensityThreshold, 0.0),
            TrajectoryMinSnapshots: global::System.Math.Max(@override.TrajectoryMinSnapshots ?? @default.TrajectoryMinSnapshots, 2),
            TrajectoryStepMultiplier: global::System.Math.Max(@override.TrajectoryStepMultiplier ?? @default.TrajectoryStepMultiplier, 1.0),
            TrajectoryStepFloor: global::System.Math.Max(@override.TrajectoryStepFloor ?? @default.TrajectoryStepFloor, 1e-12),
            TimeGuardMinMs: global::System.Math.Max(@override.TimeGuardMinMs ?? @default.TimeGuardMinMs, 1),
            ScoreGapWeight: normalizedWeights[0],
            ScoreTimeWeight: normalizedWeights[1],
            ScoreIterationWeight: normalizedWeights[2],
            ScoreCutDensityWeight: normalizedWeights[3],
            ScoreTrajectoryWeight: normalizedWeights[4]);
    }

    /// <summary>
    /// 解析间隙保护阈值。Resolve gap guard threshold.
    /// </summary>
    public static double ResolveGapGuard(EffectiveBendersAdaptiveConfig adaptive) =>
        global::System.Math.Clamp(adaptive.Tolerance * 100.0, 1e-4, 0.2);

    /// <summary>
    /// 解析时间保护阈值（毫秒）。Resolve time guard threshold in milliseconds.
    /// </summary>
    public static long ResolveTimeGuardMs(EffectiveBendersAdaptiveConfig adaptive, BendersQualityGuardConfig qualityGuard)
    {
        long perIterationMs = adaptive.Tolerance switch
        {
            >= 1e-4 => 40,
            >= 1e-5 => 60,
            _ => 80
        };
        long stallBonus = (long)(adaptive.MaxStallIterations ?? 0) * 30;
        return global::System.Math.Max(global::System.Math.Max(adaptive.MaxIterations, 1) * perIterationMs + stallBonus, qualityGuard.TimeGuardMinMs);
    }

    /// <summary>
    /// 解析质量原因。Resolve quality reason for Benders termination.
    /// </summary>
    public static string? ResolveQualityReason(
        EffectiveBendersAdaptiveConfig adaptive,
        BendersQualityGuardConfig qualityGuard,
        int bendersIterations,
        double bendersGap,
        long bendersTimeMs,
        int? executedIterations = null,
        int? totalCuts = null,
        List<double>? iterationSnapshots = null)
    {
        double gapGuard = ResolveGapGuard(adaptive);
        if (bendersGap > gapGuard + 1e-12) return BendersQualityReasonGapGuardExceeded;

        long timeGuardMs = ResolveTimeGuardMs(adaptive, qualityGuard);
        bool weakGap = bendersGap > global::System.Math.Max(adaptive.Tolerance * qualityGuard.WeakGapMultiplier, qualityGuard.WeakGapFloor);
        if (weakGap && bendersTimeMs > timeGuardMs) return BendersQualityReasonTimeGuardExceeded;

        int effectiveIterations = executedIterations ?? bendersIterations;
        int effectiveCuts = totalCuts ?? 0;
        bool iterationPressure = adaptive.MaxIterations > 0 &&
            effectiveIterations * 100 >= adaptive.MaxIterations * qualityGuard.IterationPressurePercent;
        if (iterationPressure && weakGap) return BendersQualityReasonProgressGuardTriggered;

        if (weakGap && iterationSnapshots != null && iterationSnapshots.Count >= qualityGuard.TrajectoryMinSnapshots)
        {
            double absStepSum = 0.0;
            for (int i = 1; i < iterationSnapshots.Count; i++)
            {
                absStepSum += global::System.Math.Abs(iterationSnapshots[i] - iterationSnapshots[i - 1]);
            }
            double avgStepImprovement = absStepSum / (iterationSnapshots.Count - 1);
            double stepThreshold = global::System.Math.Max(adaptive.Tolerance * qualityGuard.TrajectoryStepMultiplier, qualityGuard.TrajectoryStepFloor);
            if (avgStepImprovement < stepThreshold) return BendersQualityReasonTrajectoryWeak;
        }

        if (weakGap && effectiveIterations >= qualityGuard.CutDensityMinIterations)
        {
            double cutDensity = (double)effectiveCuts / effectiveIterations;
            if (cutDensity < qualityGuard.CutDensityThreshold) return BendersQualityReasonCutEfficiencyLow;
        }

        return null;
    }

    /// <summary>
    /// 解析质量评分。Resolve quality score (0-100) for Benders result assessment.
    /// Higher score indicates worse quality (higher risk).
    /// </summary>
    public static double ResolveQualityScore(
        EffectiveBendersAdaptiveConfig adaptive,
        BendersQualityGuardConfig qualityGuard,
        int bendersIterations,
        double bendersGap,
        long bendersTimeMs,
        int? executedIterations = null,
        int? totalCuts = null,
        List<double>? iterationSnapshots = null)
    {
        double gapGuard = ResolveGapGuard(adaptive);
        double gapRisk = gapGuard > 0.0 ? global::System.Math.Clamp(bendersGap / gapGuard, 0.0, 1.0) : 0.0;

        long timeGuardMs = ResolveTimeGuardMs(adaptive, qualityGuard);
        double timeRisk = timeGuardMs > 0 ? global::System.Math.Clamp(bendersTimeMs / (double)timeGuardMs, 0.0, 1.0) : 0.0;

        int effectiveIterations = executedIterations ?? bendersIterations;
        int effectiveCuts = totalCuts ?? 0;
        double iterationRisk = adaptive.MaxIterations > 0
            ? global::System.Math.Clamp((double)effectiveIterations / adaptive.MaxIterations, 0.0, 1.0)
            : 0.0;

        double cutDensityRisk = 0.0;
        if (effectiveIterations >= qualityGuard.CutDensityMinIterations)
        {
            double cutDensity = (double)effectiveCuts / effectiveIterations;
            cutDensityRisk = qualityGuard.CutDensityThreshold <= 1e-12
                ? 0.0
                : global::System.Math.Clamp(1.0 - cutDensity / qualityGuard.CutDensityThreshold, 0.0, 1.0);
        }

        double trajectoryRisk = 0.0;
        if (iterationSnapshots != null && iterationSnapshots.Count >= qualityGuard.TrajectoryMinSnapshots)
        {
            double absStepSum = 0.0;
            for (int i = 1; i < iterationSnapshots.Count; i++)
            {
                absStepSum += global::System.Math.Abs(iterationSnapshots[i] - iterationSnapshots[i - 1]);
            }
            double avgStepImprovement = absStepSum / (iterationSnapshots.Count - 1);
            double stepThreshold = global::System.Math.Max(
                adaptive.Tolerance * qualityGuard.TrajectoryStepMultiplier,
                qualityGuard.TrajectoryStepFloor);
            trajectoryRisk = avgStepImprovement <= 1e-12
                ? 1.0
                : global::System.Math.Clamp(stepThreshold / avgStepImprovement, 0.0, 1.0);
        }

        double score = gapRisk * qualityGuard.ScoreGapWeight
            + timeRisk * qualityGuard.ScoreTimeWeight
            + iterationRisk * qualityGuard.ScoreIterationWeight
            + cutDensityRisk * qualityGuard.ScoreCutDensityWeight
            + trajectoryRisk * qualityGuard.ScoreTrajectoryWeight;

        return global::System.Math.Clamp(score * 100.0, 0.0, 100.0);
    }
}
