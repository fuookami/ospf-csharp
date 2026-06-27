#nullable enable

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;

/// <summary>
/// 配载模式。Stowage mode controlling optimization behavior.
/// </summary>
public enum StowageMode
{
    /// <summary>预配载 / Predistribution mode</summary>
    Predistribution,
    /// <summary>全配载 / Full load mode</summary>
    FullLoad,
    /// <summary>建议打板 / Weight recommendation mode</summary>
    WeightRecommendation
}

/// <summary>
/// StowageMode 扩展方法。Extension methods for StowageMode.
/// </summary>
public static class StowageModeExtensions
{
    /// <summary>是否启用重心优化 / Whether MAC optimization is enabled</summary>
    public static bool WithMacOptimization(this StowageMode mode) =>
        mode != StowageMode.WeightRecommendation;

    /// <summary>是否启用软性安全 / Whether soft security is enabled</summary>
    public static bool WithSoftSecurity(this StowageMode mode) =>
        mode != StowageMode.WeightRecommendation;

    /// <summary>是否启用业载最大化 / Whether payload maximization is enabled</summary>
    public static bool WithPayloadMaximization(this StowageMode mode) =>
        mode == StowageMode.WeightRecommendation;
}

/// <summary>
/// 求解模式。Solve mode selecting MILP or Benders path.
/// </summary>
public abstract record SolveMode
{
    /// <summary>直接 MILP 求解 / Direct MILP solve</summary>
    public sealed record Milp : SolveMode;

    /// <summary>Benders 分解求解 / Benders decomposition solve</summary>
    /// <param name="Config">有效 Benders 自适应配置 / Effective Benders adaptive config</param>
    public sealed record Benders(EffectiveBendersAdaptiveConfig Config) : SolveMode;
}

/// <summary>
/// 有效 Benders 自适应配置。Effective Benders adaptive configuration after tuning.
/// </summary>
/// <param name="MinBinaryVariables">最小二元变量数 / Minimum binary variables threshold</param>
/// <param name="MaxIterations">最大迭代次数 / Maximum iterations</param>
/// <param name="Tolerance">收敛容差 / Convergence tolerance</param>
/// <param name="MaxStallIterations">最大停滞迭代数 / Maximum stall iterations</param>
/// <param name="ObjectiveStallIterations">目标停滞迭代数 / Objective stall iterations</param>
public sealed record EffectiveBendersAdaptiveConfig(
    int MinBinaryVariables,
    int MaxIterations,
    double Tolerance,
    int? MaxStallIterations = null,
    int? ObjectiveStallIterations = null);
