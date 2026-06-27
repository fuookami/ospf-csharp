#nullable enable

using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;

/// <summary>
/// 求解策略。Solve policy controlling Benders vs MILP path selection.
/// </summary>
/// <param name="PreferBenders">优先使用 Benders 分解 / Prefer Benders decomposition</param>
/// <param name="BendersFallbackToMilp">Benders 失败时回退到 MILP / Fallback to MILP on Benders failure</param>
public sealed record SolvePolicy(
    bool PreferBenders = false,
    bool BendersFallbackToMilp = true);

/// <summary>
/// 货物输入数据。Cargo input data for stowage planning.
/// </summary>
/// <param name="Name">货物名称 / Cargo name</param>
/// <param name="Weight">货物重量 / Cargo weight (kg)</param>
/// <param name="Priority">优先级 / Priority level</param>
/// <param name="Source">始发站 / Source station</param>
/// <param name="Destination">目的站 / Destination station</param>
/// <param name="RequiresSeparation">需要分离装载 / Requires separation loading</param>
public sealed record CargoInput(
    string Name,
    double Weight,
    int Priority,
    string Source,
    string Destination,
    bool RequiresSeparation = false);

/// <summary>
/// 装载位置输入数据。Position input data for cargo stowage.
/// </summary>
/// <param name="Name">位置名称 / Position name</param>
/// <param name="MaxWeight">最大装载重量 / Maximum weight capacity (kg)</param>
/// <param name="LongitudinalArm">纵向力臂 / Longitudinal arm (inch)</param>
/// <param name="LateralArm">横向力臂 / Lateral arm (inch)</param>
public sealed record PositionInput(
    string Name,
    double MaxWeight,
    double LongitudinalArm,
    double LateralArm);

/// <summary>
/// 飞机类型输入枚举。Aircraft type input enum for request DTO.
/// </summary>
public enum AircraftTypeInput
{
    B737,
    B757,
    B767,
    B747,
    Unknown
}

/// <summary>
/// Benders 自适应配置。Benders decomposition adaptive configuration.
/// </summary>
/// <param name="MinBinaryVariables">最小二元变量数 / Minimum binary variables threshold</param>
/// <param name="MaxIterations">最大迭代次数 / Maximum iterations</param>
/// <param name="Tolerance">收敛容差 / Convergence tolerance</param>
public sealed record BendersAdaptiveConfig(
    int MinBinaryVariables = 4,
    int MaxIterations = 64,
    double Tolerance = 1e-6);

/// <summary>
/// Benders 质量覆盖配置。Benders quality override configuration.
/// </summary>
public sealed record BendersQualityOverrideConfig(
    double? WeakGapMultiplier = null,
    double? WeakGapFloor = null,
    int? IterationPressurePercent = null,
    int? CutDensityMinIterations = null,
    double? CutDensityThreshold = null,
    int? TrajectoryMinSnapshots = null,
    double? TrajectoryStepMultiplier = null,
    double? TrajectoryStepFloor = null,
    long? TimeGuardMinMs = null,
    double? ScoreGapWeight = null,
    double? ScoreTimeWeight = null,
    double? ScoreIterationWeight = null,
    double? ScoreCutDensityWeight = null,
    double? ScoreTrajectoryWeight = null);

/// <summary>
/// 建议打板目标配置。Weight recommendation objective configuration.
/// </summary>
/// <param name="BalancePriority">平衡优先级 / Balance priority weight</param>
/// <param name="PayloadPriority">业载优先级 / Payload priority weight</param>
public sealed record WeightRecommendationObjectiveConfig(
    double BalancePriority = 1000.0,
    double PayloadPriority = 1.0);

/// <summary>
/// 配载请求 DTO。Stowage planning request data transfer object.
/// </summary>
public sealed record RequestDTO(
    string Id,
    IReadOnlyList<CargoInput> Cargos,
    IReadOnlyList<PositionInput> Positions,
    AircraftTypeInput AircraftType = AircraftTypeInput.B737,
    SolvePolicy? SolvePolicy = null,
    BendersAdaptiveConfig? BendersAdaptive = null,
    BendersQualityOverrideConfig? BendersQualityOverrides = null,
    WeightRecommendationObjectiveConfig? WeightRecommendationObjective = null,
    double PayloadUpperBound = 20.0,
    double MinPayloadRatio = 0.6,
    double MaxAdjacentLoadGap = 8.0,
    double MaxCumulativeForwardLoad = 20.0,
    double MaxCumulativeBackwardLoad = 20.0,
    double EnvelopeLongitudinalMomentMin = -20.0,
    double EnvelopeLongitudinalMomentMax = 20.0,
    double TargetLongitudinalMoment = 0.0,
    double MaxLongitudinalMomentDeviation = 20.0,
    double MaxLateralImbalance = 12.0)
{
    /// <summary>解析后的求解策略（默认值） / Resolved solve policy with defaults</summary>
    public SolvePolicy ResolvedSolvePolicy => SolvePolicy ?? new SolvePolicy();
    /// <summary>解析后的 Benders 配置（默认值） / Resolved Benders config with defaults</summary>
    public BendersAdaptiveConfig ResolvedBendersAdaptive => BendersAdaptive ?? new BendersAdaptiveConfig();
    /// <summary>解析后的建议打板目标配置（默认值） / Resolved weight recommendation config with defaults</summary>
    public WeightRecommendationObjectiveConfig ResolvedWeightRecommendationObjective => WeightRecommendationObjective ?? new WeightRecommendationObjectiveConfig();
}

/// <summary>
/// 诊断说明。Diagnostic note for structured output.
/// </summary>
/// <param name="Level">诊断级别 / Diagnostic level</param>
/// <param name="Group">诊断分组 / Diagnostic group</param>
/// <param name="Code">诊断代码 / Diagnostic code</param>
/// <param name="Message">诊断消息 / Diagnostic message</param>
public sealed record DiagnosticNote(
    string Level,
    string? Group = null,
    string? Code = null,
    string Message = "");

/// <summary>
/// 配载响应 DTO。Stowage planning response data transfer object.
/// </summary>
/// <param name="Succeed">是否成功 / Whether succeeded</param>
/// <param name="Status">状态描述 / Status description</param>
/// <param name="Objective">目标函数值 / Objective function value</param>
/// <param name="Assignments">装载分配结果 / Loading assignments</param>
/// <param name="Notes">备注列表 / Notes list</param>
/// <param name="Diagnostics">诊断信息 / Diagnostic notes</param>
public sealed record ResponseDTO(
    bool Succeed,
    string Status = "",
    double? Objective = null,
    IReadOnlyList<string>? Assignments = null,
    IReadOnlyList<string>? Notes = null,
    IReadOnlyList<DiagnosticNote>? Diagnostics = null)
{
    /// <summary>
    /// 创建无解响应。Create no-solution response.
    /// </summary>
    public static ResponseDTO NoSolution(string status, IReadOnlyList<string> notes) =>
        new(Succeed: false, Status: status, Notes: notes, Diagnostics: DiagnosticsHelper.BuildStructured(notes));

    /// <summary>
    /// 创建最优解响应。Create optimal solution response.
    /// </summary>
    public static ResponseDTO Optimal(double objective, IReadOnlyList<string> assignments, IReadOnlyList<string> notes) =>
        new(Succeed: true, Status: "Optimal", Objective: objective, Assignments: assignments, Notes: notes, Diagnostics: DiagnosticsHelper.BuildStructured(notes));
}

/// <summary>
/// KPI 响应 DTO。KPI response data transfer object.
/// </summary>
/// <param name="Succeed">是否成功 / Whether succeeded</param>
public sealed record KPIResponseDTO(bool Succeed);

/// <summary>
/// 装载顺序响应 DTO。Loading order response data transfer object.
/// </summary>
/// <param name="Succeed">是否成功 / Whether succeeded</param>
/// <param name="Status">状态描述 / Status description</param>
/// <param name="Orders">装载顺序列表 / Loading order list</param>
/// <param name="Notes">备注列表 / Notes list</param>
/// <param name="Diagnostics">诊断信息 / Diagnostic notes</param>
public sealed record LoadingOrderResponseDTO(
    bool Succeed,
    string Status = "",
    IReadOnlyList<string>? Orders = null,
    IReadOnlyList<string>? Notes = null,
    IReadOnlyList<DiagnosticNote>? Diagnostics = null)
{
    /// <summary>
    /// 创建成功响应。Create success response.
    /// </summary>
    public static LoadingOrderResponseDTO Success(IReadOnlyList<string> orders, IReadOnlyList<string>? notes = null) =>
        new(Succeed: true, Status: "Optimal", Orders: orders, Notes: notes ?? Array.Empty<string>(), Diagnostics: DiagnosticsHelper.BuildStructured(notes ?? Array.Empty<string>()));
}

/// <summary>
/// 报告响应 DTO。Report response data transfer object.
/// </summary>
/// <param name="Succeed">是否成功 / Whether succeeded</param>
public sealed record ReportResponseDTO(bool Succeed);

/// <summary>
/// 渲染 DTO（占位）。Render data transfer object (placeholder).
/// </summary>
public sealed record RenderDTO;
