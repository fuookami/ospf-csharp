#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Framework.Bpp2d.Domain.Item.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.ColumnGeneration;

/// <summary>
/// 列生成算法 / The column-generation algorithm.
/// </summary>
/// <remarks>
/// **循环里六件事的顺序不能换**：
///
/// 1. **先查时间上限**，再求解——反了会让最后一步超时后才被记成"因时间结束"。
/// 2. **没有 RMP 求解器就 break**，不报错：它当正常结束，因为只跑初始列也是一种用法。
/// 3. **求解结果先落盘再建列**：影子价格是建列请求的输入，顺序反了会用上一轮的价格。
/// 4. **候选为空就 break**，不算一次迭代——`Iterations` 只在真有新列时加一。
/// 5. **去重放在合并之后**：先各自去重再合并会让两批之间的重复留下。
/// 6. **迭代上限的标记在循环之后判**，不在 break 处判——否则"因上限结束"和"因无候选结束"会混。
///
/// 另外两处口径：
///
/// - **`FinalMilpEnabled` 为真但求解器为空时静默跳过**，`FinalSolved` 保持 `false`；
/// - **失败分析器的返回值参与错误合并**：分析器失败 → 把它的错误也拼进去，而不是丢掉原错误。
///
/// **The order of six things inside the loop must not change**: the time limit is checked **before** solving, or the final step would
/// be recorded as time-terminated only after overrunning; **a missing RMP solver breaks rather than failing**, as the implementation treats it as a normal ending because running only the initial
/// columns is a valid use; **the solve result is stored before the request is built**, since the shadow prices are that request's input and reversing it would use the previous round's
/// prices; **an empty candidate set breaks without counting an iteration**, so `Iterations` only advances when real columns arrive; **deduplication happens after merging**, because
/// deduplicating each batch first lets cross-batch duplicates through; and **the iteration-limit flag is evaluated after the loop**, not at the break, or "stopped at the limit" and "stopped
/// with no candidates" would blur together. Two more conventions: **a non-null `FinalMilpEnabled` with a null solver is skipped silently** (
/// **the failure analyser's own result joins the error list** rather than replacing the original errors.
/// </remarks>
public sealed class ColumnGenerationAlgorithm
{
    private readonly IBpp2dLayerGenerator<FltX> _layerGenerator;
    private readonly IColumnGenerationRmpSolver? _rmpSolver;
    private readonly IColumnGenerationFinalSolver? _finalMilpSolver;
    private readonly IColumnGenerationSolutionAnalyzer? _solutionAnalyzer;
    private readonly IColumnGenerationHeartbeat? _heartbeat;
    private readonly IColumnGenerationLayerRequestBuilder? _layerRequestBuilder;
    private readonly Func<Task<Result<IReadOnlyList<Layer<FltX>>, ErrorCode, Error<ErrorCode>>>>? _initialColumns;
    private readonly Func<ColumnGenerationState, IReadOnlyList<Bpp2dLayerGenerationResult<FltX>>, Task<Result<IReadOnlyList<Bpp2dLayerGenerationResult<FltX>>, ErrorCode, Error<ErrorCode>>>>? _filterByReducedCost;
    private readonly Func<IReadOnlyList<Layer<FltX>>, IReadOnlyList<Layer<FltX>>> _deduplicateColumns;
    private readonly IColumnGenerationFailureAnalyzer? _failureAnalyzer;

    /// <summary>
    /// 构造列生成算法 / Construct the column-generation algorithm.
    /// </summary>
    /// <param name="layerGenerator">建层器 / The layer generator.</param>
    /// <param name="rmpSolver">受限主问题求解器 / The RMP solver.</param>
    /// <param name="finalMilpSolver">最终 MILP 求解器 / The final-MILP solver.</param>
    /// <param name="solutionAnalyzer">解分析器 / The solution analyser.</param>
    /// <param name="heartbeat">心跳 / The heartbeat.</param>
    /// <param name="layerRequestBuilder">建列请求构造器 / The request builder.</param>
    /// <param name="initialColumns">初始列 / The initial columns.</param>
    /// <param name="filterByReducedCost">检验数筛选 / The reduced-cost filter.</param>
    /// <param name="deduplicateColumns">去重，null 表示按默认去重 / The deduplicator, null meaning the default one.</param>
    /// <param name="failureAnalyzer">失败分析器 / The failure analyser.</param>
    public ColumnGenerationAlgorithm(
        IBpp2dLayerGenerator<FltX> layerGenerator,
        IColumnGenerationRmpSolver? rmpSolver = null,
        IColumnGenerationFinalSolver? finalMilpSolver = null,
        IColumnGenerationSolutionAnalyzer? solutionAnalyzer = null,
        IColumnGenerationHeartbeat? heartbeat = null,
        IColumnGenerationLayerRequestBuilder? layerRequestBuilder = null,
        Func<Task<Result<IReadOnlyList<Layer<FltX>>, ErrorCode, Error<ErrorCode>>>>? initialColumns = null,
        Func<ColumnGenerationState, IReadOnlyList<Bpp2dLayerGenerationResult<FltX>>, Task<Result<IReadOnlyList<Bpp2dLayerGenerationResult<FltX>>, ErrorCode, Error<ErrorCode>>>>? filterByReducedCost = null,
        Func<IReadOnlyList<Layer<FltX>>, IReadOnlyList<Layer<FltX>>>? deduplicateColumns = null,
        IColumnGenerationFailureAnalyzer? failureAnalyzer = null)
    {
        ArgumentNullException.ThrowIfNull(layerGenerator);
        _layerGenerator = layerGenerator;
        _rmpSolver = rmpSolver;
        _finalMilpSolver = finalMilpSolver;
        _solutionAnalyzer = solutionAnalyzer;
        _heartbeat = heartbeat;
        _layerRequestBuilder = layerRequestBuilder;
        _initialColumns = initialColumns;
        _filterByReducedCost = filterByReducedCost;
        _deduplicateColumns = deduplicateColumns ?? Deduplicate;
        _failureAnalyzer = failureAnalyzer;
    }

    /// <summary>
    /// 求解 / Solve.
    /// </summary>
    /// <param name="config">配置 / The configuration.</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token.</param>
    /// <returns>总结果 / The overall result.</returns>
    public async Task<Result<ColumnGenerationResult, ErrorCode, Error<ErrorCode>>> SolveAsync(
        ColumnGenerationConfig? config = null,
        CancellationToken cancellationToken = default)
    {
        config ??= new ColumnGenerationConfig();
        var startedAt = Stopwatch.StartNew();
        var initialState = new ColumnGenerationState(0, Array.Empty<Layer<FltX>>());

        IReadOnlyList<Layer<FltX>> columns;
        if (_initialColumns is null)
        {
            columns = Array.Empty<Layer<FltX>>();
        }
        else
        {
            Result<IReadOnlyList<Layer<FltX>>, ErrorCode, Error<ErrorCode>> initial = await _initialColumns().ConfigureAwait(false);
            if (initial is Failed<IReadOnlyList<Layer<FltX>>, ErrorCode, Error<ErrorCode>> initialFailed)
            {
                return await FailedAsync<ColumnGenerationResult>(
                    ColumnGenerationFailureStage.InitialColumns, initialState,
                    new[] { initialFailed.Error }, cancellationToken).ConfigureAwait(false);
            }
            columns = _deduplicateColumns(
                ((Ok<IReadOnlyList<Layer<FltX>>, ErrorCode, Error<ErrorCode>>)initial).Value);
        }

        bool terminatedByIterationLimit = false;
        bool terminatedByTimeLimit = false;
        int iterations = 0;
        int lpSolvedTimes = 0;
        bool finalSolved = false;
        IReadOnlyDictionary<DemandModeKey, FltX> latestShadowPrices = new Dictionary<DemandModeKey, FltX>();
        IReadOnlyDictionary<string, FltX> latestAdditional = new Dictionary<string, FltX>();
        var lpObjectives = new List<FltX?>();
        var lpInfos = new List<IReadOnlyDictionary<string, string>>();
        FltX? finalObjective = null;
        IReadOnlyDictionary<string, string> finalInfo = new Dictionary<string, string>();
        IReadOnlyList<Sheet<FltX>> sheets = Array.Empty<Sheet<FltX>>();

        while (iterations < config.IterationLimit)
        {
            // **先查时间上限**，再求解。
            // **Check the time limit before solving.**
            if (config.Time > TimeSpan.Zero && startedAt.Elapsed >= config.Time)
            {
                terminatedByTimeLimit = true;
                break;
            }
            var rmpState = new ColumnGenerationState(
                iterations, columns, null, latestShadowPrices, latestAdditional);
            if (_rmpSolver is null)
            {
                // **没有求解器就正常结束**，不报错。
                // **A missing solver ends normally**, not as a failure.
                break;
            }
            Result<ColumnGenerationLpResult, ErrorCode, Error<ErrorCode>> lp = await _rmpSolver.SolveAsync(rmpState, cancellationToken).ConfigureAwait(false);
            if (lp is Failed<ColumnGenerationLpResult, ErrorCode, Error<ErrorCode>> lpFailed)
            {
                return await FailedAsync<ColumnGenerationResult>(
                    ColumnGenerationFailureStage.RestrictedMasterProblem, rmpState,
                    new[] { lpFailed.Error }, cancellationToken).ConfigureAwait(false);
            }
            ColumnGenerationLpResult lpResult = ((Ok<ColumnGenerationLpResult, ErrorCode, Error<ErrorCode>>)lp).Value;

            // **求解结果先落盘**，影子价格是建列请求的输入。
            // **Store the result first**: the shadow prices feed the request.
            latestShadowPrices = lpResult.ShadowPrices;
            latestAdditional = lpResult.AdditionalShadowPrices ?? latestAdditional;
            lpObjectives.Add(lpResult.Objective);
            lpInfos.Add(lpResult.Info ?? new Dictionary<string, string>());
            ++lpSolvedTimes;

            var pricedState = new ColumnGenerationState(
                iterations, columns, null, latestShadowPrices, latestAdditional);
            Bpp2dLayerGenerationRequest<FltX> request = _layerRequestBuilder?.Build(pricedState, config)
                ?? new Bpp2dLayerGenerationRequest<FltX>(
                    Iteration: iterations,
                    ExistingLayers: columns,
                    ShadowPrices: latestShadowPrices,
                    MaxCandidates: config.MaxColumnsPerIteration);
            IReadOnlyList<Bpp2dLayerGenerationResult<FltX>> candidates = _layerGenerator.Generate(request);

            IReadOnlyList<Bpp2dLayerGenerationResult<FltX>> accepted = candidates;
            if (_filterByReducedCost is not null)
            {
                Result<IReadOnlyList<Bpp2dLayerGenerationResult<FltX>>, ErrorCode, Error<ErrorCode>> filtered = await _filterByReducedCost(pricedState, candidates).ConfigureAwait(false);
                if (filtered is Failed<IReadOnlyList<Bpp2dLayerGenerationResult<FltX>>, ErrorCode, Error<ErrorCode>> filterFailed)
                {
                    return await FailedAsync<ColumnGenerationResult>(
                        ColumnGenerationFailureStage.CandidateFilter, pricedState,
                        new[] { filterFailed.Error }, cancellationToken).ConfigureAwait(false);
                }
                accepted = ((Ok<IReadOnlyList<Bpp2dLayerGenerationResult<FltX>>, ErrorCode, Error<ErrorCode>>)filtered).Value;
            }

            // **候选为空就 break，不算一次迭代。**
            // **An empty candidate set breaks without counting an iteration.**
            if (accepted.Count == 0)
            {
                break;
            }
            // **先去重合并后的全集**，不是各自去重再合并。
            // **Deduplicate after merging**, not each batch first.
            columns = _deduplicateColumns(
                columns.Concat(accepted.Select(candidate => candidate.Layer)).ToArray());

            if (_heartbeat is not null)
            {
                await _heartbeat.InvokeAsync(
                    new ColumnGenerationState(iterations, columns, null, latestShadowPrices, latestAdditional),
                    cancellationToken).ConfigureAwait(false);
            }
            ++iterations;
        }

        // **上限标记在循环之后判**，不在 break 处判。
        // **The limit flag is evaluated after the loop**, not at the break.
        if (iterations >= config.IterationLimit)
        {
            terminatedByIterationLimit = true;
        }

        var finalState = new ColumnGenerationState(
            iterations, columns, sheets, latestShadowPrices, latestAdditional);
        if (config.FinalMilpEnabled && _finalMilpSolver is not null)
        {
            Result<ColumnGenerationFinalResult, ErrorCode, Error<ErrorCode>> solved = await _finalMilpSolver.SolveAsync(finalState, cancellationToken).ConfigureAwait(false);
            if (solved is Failed<ColumnGenerationFinalResult, ErrorCode, Error<ErrorCode>> finalFailed)
            {
                return await FailedAsync<ColumnGenerationResult>(
                    ColumnGenerationFailureStage.FinalMilp, finalState,
                    new[] { finalFailed.Error }, cancellationToken).ConfigureAwait(false);
            }
            ColumnGenerationFinalResult finalResult = ((Ok<ColumnGenerationFinalResult, ErrorCode, Error<ErrorCode>>)solved).Value;
            columns = _deduplicateColumns(finalResult.Columns);
            sheets = finalResult.Sheets ?? Array.Empty<Sheet<FltX>>();
            finalObjective = finalResult.Objective;
            finalInfo = finalResult.Info ?? new Dictionary<string, string>();
            finalState = finalState with { Columns = columns, Sheets = sheets };
            finalSolved = true;
        }

        if (_solutionAnalyzer is not null)
        {
            Try analyzed = await _solutionAnalyzer.AnalyzeAsync(finalState, cancellationToken).ConfigureAwait(false);
            if (analyzed is Failed<Success, ErrorCode, Error<ErrorCode>> analysisFailed)
            {
                return await FailedAsync<ColumnGenerationResult>(
                    ColumnGenerationFailureStage.SolutionAnalysis, finalState,
                    new[] { analysisFailed.Error }, cancellationToken).ConfigureAwait(false);
            }
        }

        startedAt.Stop();
        return Results.Ok<ColumnGenerationResult>(new ColumnGenerationResult(
            Columns: columns,
            Sheets: sheets,
            IterationCount: iterations,
            TerminatedByIterationLimit: terminatedByIterationLimit,
            TerminatedByTimeLimit: terminatedByTimeLimit,
            LpSolvedTimes: lpSolvedTimes,
            FinalSolved: finalSolved,
            LpObjectives: lpObjectives,
            FinalObjective: finalObjective,
            Elapsed: startedAt.Elapsed,
            LpInfos: lpInfos,
            FinalInfo: finalInfo));
    }

    /// <summary>
    /// 组装失败结果 / Assemble a failure result.
    /// </summary>
    /// <typeparam name="T">结果类型 / The result type.</typeparam>
    /// <param name="stage">阶段 / The stage.</param>
    /// <param name="state">状态 / The state.</param>
    /// <param name="errors">错误 / The errors.</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token.</param>
    /// <returns>失败 / The failure.</returns>
    /// <remarks>
    /// **分析器失败时把它的错误也拼进去**，不丢掉原错误——只要有一个错误就报 `Failed`，多个报 `Fatal`。
    ///
    /// **When the analyser fails, its errors are appended** rather than dropping the originals: one error yields `Failed`, several yield `Fatal`.
    /// </remarks>
    private async Task<Result<T, ErrorCode, Error<ErrorCode>>> FailedAsync<T>(
        ColumnGenerationFailureStage stage,
        ColumnGenerationState state,
        IReadOnlyList<Error<ErrorCode>> errors,
        CancellationToken cancellationToken)
    {
        var merged = new List<Error<ErrorCode>>(errors);
        if (_failureAnalyzer is not null)
        {
            Try analyzed = await _failureAnalyzer.AnalyzeAsync(
                new ColumnGenerationFailure(stage, state, errors), cancellationToken).ConfigureAwait(false);
            if (analyzed is Failed<Success, ErrorCode, Error<ErrorCode>> analyzerFailed)
            {
                merged.Add(analyzerFailed.Error);
            }
        }
        return merged.Count == 1
            ? Results.Failed<T>(merged[0])
            : new Fatal<T, ErrorCode, Error<ErrorCode>>(merged.ToArray());
    }

    /// <summary>
    /// 默认去重 / The default deduplication.
    /// </summary>
    /// <param name="layers">层 / The layers.</param>
    /// <returns>去重后的层 / The deduplicated layers.</returns>
    private static IReadOnlyList<Layer<FltX>> Deduplicate(IReadOnlyList<Layer<FltX>> layers) =>
        layers.Distinct().ToArray();
}
