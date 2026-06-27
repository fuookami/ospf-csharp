#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;

/// <summary>
/// 可行性诊断工具。Feasibility diagnostics for pre-solve and post-solve analysis.
/// </summary>
public static class FeasibilityDiagnostics
{
    private const double CriticalRatio = 0.98;
    private const double Eps = 1e-6;

    /// <summary>
    /// 追加核心可行性诊断。Append core feasibility diagnostics before solving.
    /// </summary>
    public static void AppendCoreFeasibilityDiagnostics(RequestDTO request, List<string> notes)
    {
        double totalCapacity = request.Positions.Sum(p => p.MaxWeight);
        double totalCargoWeight = request.Cargos.Sum(c => c.Weight);
        double minPayloadRequired = global::System.Math.Min(request.PayloadUpperBound, totalCargoWeight) * request.MinPayloadRatio;

        if (request.EnvelopeLongitudinalMomentMin > request.EnvelopeLongitudinalMomentMax)
        {
            DiagnosticsHelper.PushGroupedNote(
                notes, DiagnosticsHelper.LevelDiagnostic, DiagnosticsHelper.GroupAirworthiness,
                DiagnosticsHelper.CodeEnvelopeRangeInvalid,
                "infeasible: envelope_longitudinal_moment_min > envelope_longitudinal_moment_max");
        }
        if (request.PayloadUpperBound < 0.0)
        {
            DiagnosticsHelper.PushGroupedNote(
                notes, DiagnosticsHelper.LevelDiagnostic, DiagnosticsHelper.GroupPayload,
                DiagnosticsHelper.CodePayloadUpperNegative,
                "infeasible: payload_upper_bound < 0");
        }
        if (request.MinPayloadRatio < 0.0 || request.MinPayloadRatio > 1.0)
        {
            DiagnosticsHelper.PushGroupedNote(
                notes, DiagnosticsHelper.LevelDiagnostic, DiagnosticsHelper.GroupPayload,
                DiagnosticsHelper.CodeMinPayloadRatioOutOfRange,
                "infeasible: min_payload_ratio must be within [0, 1]");
        }
        if (minPayloadRequired > request.PayloadUpperBound)
        {
            DiagnosticsHelper.PushGroupedNote(
                notes, DiagnosticsHelper.LevelDiagnostic, DiagnosticsHelper.GroupPayload,
                DiagnosticsHelper.CodeMinPayloadGtUpper,
                "infeasible: min payload requirement exceeds payload upper bound");
        }
        if (minPayloadRequired > totalCapacity)
        {
            DiagnosticsHelper.PushGroupedNote(
                notes, DiagnosticsHelper.LevelDiagnostic, DiagnosticsHelper.GroupAirworthiness,
                DiagnosticsHelper.CodeMinPayloadGtTotalCapacity,
                "infeasible: min payload requirement exceeds total position capacity");
        }
        foreach (var cargo in request.Cargos)
        {
            bool canFit = request.Positions.Any(p => p.MaxWeight + Eps >= cargo.Weight);
            if (!canFit)
            {
                DiagnosticsHelper.PushGroupedNote(
                    notes, DiagnosticsHelper.LevelDiagnostic, DiagnosticsHelper.GroupAirworthiness,
                    DiagnosticsHelper.CodeCargoExceedsAllPositions,
                    $"infeasible: cargo {cargo.Name} ({cargo.Weight:F2}) exceeds every position max_weight");
            }
        }
    }

    /// <summary>
    /// 追加关键约束诊断。Append critical constraint diagnostics after solving.
    /// </summary>
    public static void AppendCriticalConstraintNotes(
        RequestDTO request,
        List<List<int>> xIdx,
        double[] solution,
        List<string> notes)
    {
        var positionLoads = new double[request.Positions.Count];
        for (int c = 0; c < request.Cargos.Count; c++)
        {
            for (int p = 0; p < request.Positions.Count; p++)
            {
                double value = xIdx[c].Count > p ? solution[xIdx[c][p]] : 0.0;
                positionLoads[p] += value * request.Cargos[c].Weight;
            }
        }

        for (int p = 0; p < request.Positions.Count; p++)
        {
            double capacity = request.Positions[p].MaxWeight;
            if (capacity > Eps)
            {
                double ratio = positionLoads[p] / capacity;
                if (ratio + Eps >= CriticalRatio)
                {
                    DiagnosticsHelper.PushGroupedNote(
                        notes, DiagnosticsHelper.LevelCritical, DiagnosticsHelper.GroupAirworthiness,
                        DiagnosticsHelper.CodeCapacityUtilizationHigh,
                        $"airworthiness_capacity_{request.Positions[p].Name} utilization {ratio * 100:F2}%");
                }
            }
        }

        double totalPayload = positionLoads.Sum();
        double totalCargoWeight = request.Cargos.Sum(c => c.Weight);
        double minPayload = global::System.Math.Min(request.PayloadUpperBound, totalCargoWeight) * request.MinPayloadRatio;
        if (request.PayloadUpperBound > Eps && totalPayload / request.PayloadUpperBound + Eps >= CriticalRatio)
        {
            DiagnosticsHelper.PushGroupedNote(
                notes, DiagnosticsHelper.LevelCritical, DiagnosticsHelper.GroupPayload,
                DiagnosticsHelper.CodePayloadUpperUtilizationHigh,
                $"airworthiness_payload_upper utilization {totalPayload / request.PayloadUpperBound * 100:F2}%");
        }
        if (minPayload > Eps && totalPayload / minPayload <= 1.0 + (1.0 - CriticalRatio))
        {
            DiagnosticsHelper.PushGroupedNote(
                notes, DiagnosticsHelper.LevelCritical, DiagnosticsHelper.GroupPayload,
                DiagnosticsHelper.CodePayloadLowerClose,
                $"airworthiness_payload_lower payload {totalPayload:F2} close to minimum {minPayload:F2}");
        }

        double longitudinalMoment = 0.0;
        double lateralMoment = 0.0;
        for (int c = 0; c < request.Cargos.Count; c++)
        {
            for (int p = 0; p < request.Positions.Count; p++)
            {
                double value = xIdx[c].Count > p ? solution[xIdx[c][p]] : 0.0;
                double weight = value * request.Cargos[c].Weight;
                longitudinalMoment += weight * request.Positions[p].LongitudinalArm;
                lateralMoment += weight * request.Positions[p].LateralArm;
            }
        }

        double upperGap = request.EnvelopeLongitudinalMomentMax - longitudinalMoment;
        double lowerGap = longitudinalMoment - request.EnvelopeLongitudinalMomentMin;
        double envelopeSpan = request.EnvelopeLongitudinalMomentMax - request.EnvelopeLongitudinalMomentMin;
        if (envelopeSpan > Eps)
        {
            if (upperGap / envelopeSpan <= (1.0 - CriticalRatio) + Eps)
            {
                DiagnosticsHelper.PushGroupedNote(
                    notes, DiagnosticsHelper.LevelCritical, DiagnosticsHelper.GroupAirworthiness,
                    DiagnosticsHelper.CodeEnvelopeLongitudinalMaxClose,
                    $"airworthiness_envelope_longitudinal_max close ({longitudinalMoment:F3})");
            }
            if (lowerGap / envelopeSpan <= (1.0 - CriticalRatio) + Eps)
            {
                DiagnosticsHelper.PushGroupedNote(
                    notes, DiagnosticsHelper.LevelCritical, DiagnosticsHelper.GroupAirworthiness,
                    DiagnosticsHelper.CodeEnvelopeLongitudinalMinClose,
                    $"airworthiness_envelope_longitudinal_min close ({longitudinalMoment:F3})");
            }
        }

        double absLateralMoment = global::System.Math.Abs(lateralMoment);
        if (request.MaxLateralImbalance > Eps && absLateralMoment / request.MaxLateralImbalance + Eps >= CriticalRatio)
        {
            DiagnosticsHelper.PushGroupedNote(
                notes, DiagnosticsHelper.LevelCritical, DiagnosticsHelper.GroupMacOptimization,
                DiagnosticsHelper.CodeLateralImbalanceClose,
                $"mac_lateral imbalance {absLateralMoment:F3} close to limit {request.MaxLateralImbalance:F3}");
        }
    }
}
