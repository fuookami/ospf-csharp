#nullable enable

using System.Collections.Generic;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;

/// <summary>
/// 诊断工具类。Diagnostics utility class for structured diagnostic output.
/// </summary>
public static class DiagnosticsHelper
{
    /// <summary>诊断级别 / Diagnostic level</summary>
    public const string LevelDiagnostic = "diagnostic";
    /// <summary>严重级别 / Critical level</summary>
    public const string LevelCritical = "critical";

    /// <summary>适航性分组 / Airworthiness group</summary>
    public const string GroupAirworthiness = "airworthiness";
    /// <summary>业载分组 / Payload group</summary>
    public const string GroupPayload = "payload";
    /// <summary>重心优化分组 / MAC optimization group</summary>
    public const string GroupMacOptimization = "mac_optimization";
    /// <summary>余度分组 / Redundancy group</summary>
    public const string GroupRedundancy = "redundancy";
    /// <summary>求解器分组 / Solver group</summary>
    public const string GroupSolver = "solver";

    /// <summary>包络范围无效 / Envelope range invalid</summary>
    public const string CodeEnvelopeRangeInvalid = "envelope_range_invalid";
    /// <summary>业载上限为负 / Payload upper negative</summary>
    public const string CodePayloadUpperNegative = "payload_upper_negative";
    /// <summary>最小业载比例超出范围 / Min payload ratio out of range</summary>
    public const string CodeMinPayloadRatioOutOfRange = "min_payload_ratio_out_of_range";
    /// <summary>最小业载大于上限 / Min payload greater than upper</summary>
    public const string CodeMinPayloadGtUpper = "min_payload_gt_upper";
    /// <summary>最小业载大于总容量 / Min payload greater than total capacity</summary>
    public const string CodeMinPayloadGtTotalCapacity = "min_payload_gt_total_capacity";
    /// <summary>货物超出所有位置 / Cargo exceeds all positions</summary>
    public const string CodeCargoExceedsAllPositions = "cargo_exceeds_all_positions";

    /// <summary>容量利用率高 / Capacity utilization high</summary>
    public const string CodeCapacityUtilizationHigh = "capacity_utilization_high";
    /// <summary>业载上限利用率高 / Payload upper utilization high</summary>
    public const string CodePayloadUpperUtilizationHigh = "payload_upper_utilization_high";
    /// <summary>业载下限接近 / Payload lower close</summary>
    public const string CodePayloadLowerClose = "payload_lower_close";
    /// <summary>纵向力矩上限接近 / Envelope longitudinal max close</summary>
    public const string CodeEnvelopeLongitudinalMaxClose = "envelope_longitudinal_max_close";
    /// <summary>纵向力矩下限接近 / Envelope longitudinal min close</summary>
    public const string CodeEnvelopeLongitudinalMinClose = "envelope_longitudinal_min_close";
    /// <summary>横向不平衡接近 / Lateral imbalance close</summary>
    public const string CodeLateralImbalanceClose = "lateral_imbalance_close";
    /// <summary>目的地集中度高 / Destination concentration high</summary>
    public const string CodeRedundancyDestinationConcentrationHigh = "redundancy_destination_concentration_high";

    /// <summary>Benders 迭代次数 / Benders iterations</summary>
    public const string CodeBendersIterations = "benders_iterations";
    /// <summary>Benders 间隙 / Benders gap</summary>
    public const string CodeBendersGap = "benders_gap";
    /// <summary>Benders 耗时 / Benders time ms</summary>
    public const string CodeBendersTimeMs = "benders_time_ms";
    /// <summary>Benders 自适应有效 / Benders adaptive effective</summary>
    public const string CodeBendersAdaptiveEffective = "benders_adaptive_effective";
    /// <summary>Benders 问题规模二元变量 / Benders problem size binary variables</summary>
    public const string CodeBendersProblemSizeBinaryVariables = "benders_problem_size_binary_variables";
    /// <summary>Benders 间隙保护超出 / Benders gap guard exceeded</summary>
    public const string CodeBendersGapGuardExceeded = "benders_gap_guard_exceeded";
    /// <summary>Benders 时间保护超出 / Benders time guard exceeded</summary>
    public const string CodeBendersTimeGuardExceeded = "benders_time_guard_exceeded";
    /// <summary>Benders 进度保护触发 / Benders progress guard triggered</summary>
    public const string CodeBendersProgressGuardTriggered = "benders_progress_guard_triggered";
    /// <summary>Benders 切割效率低 / Benders cut efficiency low</summary>
    public const string CodeBendersCutEfficiencyLow = "benders_cut_efficiency_low";
    /// <summary>Benders 轨迹弱 / Benders trajectory weak</summary>
    public const string CodeBendersTrajectoryWeak = "benders_trajectory_weak";
    /// <summary>Benders 质量保护有效 / Benders quality guard effective</summary>
    public const string CodeBendersQualityGuardEffective = "benders_quality_guard_effective";
    /// <summary>Benders 质量评分 / Benders quality score</summary>
    public const string CodeBendersQualityScore = "benders_quality_score";
    /// <summary>Benders 质量动作 / Benders quality action</summary>
    public const string CodeBendersQualityAction = "benders_quality_action";
    /// <summary>Benders 失败 / Benders failed</summary>
    public const string CodeBendersFailed = "benders_failed";
    /// <summary>求解器路径 / Solver path</summary>
    public const string CodeSolverPath = "solver_path";

    /// <summary>
    /// 推送分组诊断说明。Push grouped diagnostic note to notes list.
    /// </summary>
    public static void PushGroupedNote(
        List<string> notes,
        string level,
        string group,
        string code,
        string message)
    {
        notes.Add($"{level}|group={group}|code={code}|msg={message}");
    }

    /// <summary>
    /// 从备注列表构建结构化诊断信息。Build structured diagnostic notes from notes list.
    /// </summary>
    public static IReadOnlyList<DiagnosticNote> BuildStructured(IReadOnlyList<string> notes)
    {
        var result = new List<DiagnosticNote>(notes.Count);
        foreach (var note in notes)
        {
            result.Add(ParseGroupedNote(note) ?? new DiagnosticNote(LevelDiagnostic, Message: note));
        }
        return result;
    }

    private static DiagnosticNote? ParseGroupedNote(string note)
    {
        var segments = note.Split('|');
        var level = segments.Length > 0 ? segments[0].Trim() : null;
        if (string.IsNullOrEmpty(level)) return null;

        string? group = null;
        string? code = null;
        string? message = null;

        for (int i = 1; i < segments.Length; i++)
        {
            var kv = segments[i].Split('=', 2);
            var key = kv.Length > 0 ? kv[0].Trim() : "";
            var value = kv.Length > 1 ? kv[1].Trim() : "";
            switch (key)
            {
                case "group": group = value; break;
                case "code": code = value; break;
                case "msg": message = value; break;
            }
        }

        return message != null
            ? new DiagnosticNote(Level: level, Group: group, Code: code, Message: message)
            : null;
    }
}
