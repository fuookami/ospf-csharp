#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Framework.Bpp2d.Domain.Item.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.ColumnGeneration;

/// <summary>
/// 列生成配置 / Column-generation configuration.
/// </summary>
/// <param name="IterationLimit">迭代上限 / The iteration limit.</param>
/// <param name="TimeLimit">时间上限，<see cref="TimeSpan.Zero"/> 表示不限 / The time limit, zero meaning unbounded.</param>
/// <param name="MaxColumnsPerIteration">每轮最多新增列数 / The maximum number of new columns per iteration.</param>
/// <param name="FinalMilpEnabled">是否启用最终 MILP / Whether the final MILP is enabled.</param>
/// <param name="ReducedCostTolerance">检验数容差 / The reduced-cost tolerance.</param>
/// <remarks>
/// 与的 `ColumnGenerationConfig` 对齐。**默认值就是默认值**：
/// 迭代 64、每轮 32 列、最终 MILP 开启、容差 `1e-6`。**容差不是零**——浮点检验数永远差一点点，
/// 用零会让"改进列"永远成立，迭代到上限为止。
///
/// Aligned with its `ColumnGenerationConfig`, and **the defaults are its defaults**: 64 iterations, 32 columns per iteration, the final MILP on, tolerance `1e-6`.
/// **The tolerance is not zero** — a floating-point reduced cost is never exactly zero, and using zero would make "improving column" always true and run to the iteration limit.
/// </remarks>
public sealed record ColumnGenerationConfig(
    int IterationLimit = 64,
    TimeSpan? TimeLimit = null,
    int MaxColumnsPerIteration = 32,
    bool FinalMilpEnabled = true,
    FltX? ReducedCostTolerance = null)
{
    /// <summary>检验数容差（零表示沿用默认 <c>1e-6</c>）/ The reduced-cost tolerance, zero meaning the default 1e-6</summary>
    public FltX Tolerance => ReducedCostTolerance ?? new FltX(1e-6);

    /// <summary>时间上限（零表示不限）/ The time limit, zero meaning unbounded</summary>
    public TimeSpan Time => TimeLimit ?? TimeSpan.Zero;
}

/// <summary>
/// 列生成的一轮状态 / One iteration's state.
/// </summary>
/// <param name="Iteration">迭代序号 / The iteration number.</param>
/// <param name="Columns">当前列 / The current columns.</param>
/// <param name="Sheets">当前板材 / The current sheets.</param>
/// <param name="ShadowPrices">需求影子价格 / The demand shadow prices.</param>
/// <param name="AdditionalShadowPrices">附加影子价格 / The additional shadow prices.</param>
/// <remarks>
/// 与基线 `ColumnGenerationState` 对齐。**影子价格用 <see cref="DemandModeKey"/> 而不是 `Bpp2dDemandShadowPriceKey`**：
/// 前者是求解器边界的字符串键（模式 + 货物 + 单位），后者是模型内部的带载荷值对象。**两层不合并**——
/// 求解器返回的是字符串，硬塞进值对象要么丢单位、要么在边界上做字符串解析。
///
/// Aligned with its `ColumnGenerationState`. **Shadow prices use <see cref="DemandModeKey"/> rather than `Bpp2dDemandShadowPriceKey`**: the former is the solver boundary's string
/// key (mode + item + unit) and the latter the model's internal value object. **The two layers stay separate** — the solver returns strings, and forcing them into the value object would
/// either drop the unit or demand string parsing at the boundary.
/// </remarks>
public sealed record ColumnGenerationState(
    int Iteration,
    IReadOnlyList<Layer<FltX>> Columns,
    IReadOnlyList<Sheet<FltX>>? Sheets = null,
    IReadOnlyDictionary<DemandModeKey, FltX>? ShadowPrices = null,
    IReadOnlyDictionary<string, FltX>? AdditionalShadowPrices = null);

/// <summary>
/// 一轮 LP 的结果 / One LP's result.
/// </summary>
/// <param name="ShadowPrices">需求影子价格 / The demand shadow prices.</param>
/// <param name="Objective">目标值 / The objective value.</param>
/// <param name="Info">附加信息 / Extra information.</param>
/// <param name="AdditionalShadowPrices">附加影子价格 / The additional shadow prices.</param>
public sealed record ColumnGenerationLpResult(
    IReadOnlyDictionary<DemandModeKey, FltX> ShadowPrices,
    FltX? Objective = null,
    IReadOnlyDictionary<string, string>? Info = null,
    IReadOnlyDictionary<string, FltX>? AdditionalShadowPrices = null);

/// <summary>
/// 最终结果 / The final result.
/// </summary>
/// <param name="Columns">最终列 / The final columns.</param>
/// <param name="Sheets">最终板材 / The final sheets.</param>
/// <param name="Objective">目标值 / The objective value.</param>
/// <param name="Info">附加信息 / Extra information.</param>
public sealed record ColumnGenerationFinalResult(
    IReadOnlyList<Layer<FltX>> Columns,
    IReadOnlyList<Sheet<FltX>>? Sheets = null,
    FltX? Objective = null,
    IReadOnlyDictionary<string, string>? Info = null);

/// <summary>
/// 列生成的失败阶段 / The column-generation failure stage.
/// </summary>
/// <remarks>
/// **成员照做**（`InitialColumns` / `RestrictedMasterProblem` / `CandidateFilter` / `FinalMilp` / `SolutionAnalysis`）。
/// 凭印象写过一版 `Registration` / `LpSolving` / …，与基线不符—— 读回基线源码时发现并改正。
/// 五个阶段正好对应主循环里五个可能失败的位置，多一个少一个都会让失败报告指错地方。
///
/// **Members copied from the code.** wrote a version from memory (`Registration` / `LpSolving` / …) that did not match, and caught it when reading the code source back.
/// The five stages line up exactly with the five places the main loop can fail, so adding or dropping one would misattribute failures.
/// </remarks>
public enum ColumnGenerationFailureStage
{
    /// <summary>初始列 / Initial columns.</summary>
    InitialColumns,

    /// <summary>受限主问题 / Restricted master problem.</summary>
    RestrictedMasterProblem,

    /// <summary>候选筛选 / Candidate filter.</summary>
    CandidateFilter,

    /// <summary>最终 MILP / Final MILP.</summary>
    FinalMilp,

    /// <summary>解分析 / Solution analysis.</summary>
    SolutionAnalysis,
}

/// <summary>
/// 列生成失败 / A column-generation failure.
/// </summary>
/// <param name="Stage">失败阶段 / The stage.</param>
/// <param name="State">失败时的状态 / The state at failure.</param>
/// <param name="Errors">错误 / The errors.</param>
/// <remarks>
/// 带 `State` 而不是只带一句话：失败分析器要看到当时有哪些列、影子价格是多少，只有字符串什么都诊断不出来。
///
/// Carries `State` rather than a bare message: a failure analyser needs to see which columns existed and what the shadow prices were, and a string alone diagnoses nothing.
/// </remarks>
public sealed record ColumnGenerationFailure(
    ColumnGenerationFailureStage Stage,
    ColumnGenerationState State,
    IReadOnlyList<Error<ErrorCode>> Errors);

/// <summary>
/// 列生成总结果 / The overall column-generation result.
/// </summary>
/// <param name="Columns">列 / The columns.</param>
/// <param name="Sheets">板材 / The sheets.</param>
/// <param name="IterationCount">迭代次数 / The iteration count.</param>
/// <param name="TerminatedByIterationLimit">是否因迭代上限结束 / Whether it stopped at the iteration limit.</param>
/// <param name="TerminatedByTimeLimit">是否因时间上限结束 / Whether it stopped at the time limit.</param>
/// <param name="LpSolvedTimes">LP 求解次数 / How many LPs were solved.</param>
/// <param name="FinalSolved">最终是否求解 / Whether the final stage was solved.</param>
/// <param name="LpObjectives">各轮目标值 / The per-iteration objectives.</param>
/// <param name="FinalObjective">最终目标值 / The final objective.</param>
/// <param name="Elapsed">耗时 / The elapsed time.</param>
/// <param name="LpInfos">各轮附加信息 / The per-iteration extra information.</param>
/// <param name="FinalInfo">最终附加信息 / The final extra information.</param>
/// <remarks>
/// **两个"因上限结束"的标记分开存**，不合并成一个 `Terminated`：迭代上限与时间上限对调用方的含义不同——
/// 前者可以"再跑几轮"，后者通常要重新估计规模。合并之后调用方只能靠猜。
///
/// **The two "stopped at a limit" flags are stored separately** rather than merged into one `Terminated`: an iteration limit means "run a few more rounds" while a time limit usually means
/// "re-estimate the problem size", and merging them leaves the caller guessing.
/// </remarks>
public sealed record ColumnGenerationResult(
    IReadOnlyList<Layer<FltX>> Columns,
    IReadOnlyList<Sheet<FltX>> Sheets,
    int IterationCount,
    bool TerminatedByIterationLimit,
    bool TerminatedByTimeLimit,
    int LpSolvedTimes,
    bool FinalSolved,
    IReadOnlyList<FltX?> LpObjectives,
    FltX? FinalObjective,
    TimeSpan Elapsed,
    IReadOnlyList<IReadOnlyDictionary<string, string>>? LpInfos = null,
    IReadOnlyDictionary<string, string>? FinalInfo = null);
