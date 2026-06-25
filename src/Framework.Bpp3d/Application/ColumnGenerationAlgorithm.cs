#nullable enable

using Fuookami.Ospf.Framework.Bpp3d.Infra;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Framework.Bpp3d.Application;
/// <summary>列生成算法 / Column generation algorithm.</summary>
public sealed class ColumnGenerationAlgorithm<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly IBpp3dLayerGenerator<V> _layerGenerator;
    private readonly ColumnGenerationRmpSolver<V>? _rmpSolver;
    private readonly ColumnGenerationFinalSolver<V>? _finalMilpSolver;
    private readonly ColumnGenerationSolutionAnalyzer<V>? _solutionAnalyzer;
    private readonly ColumnGenerationHeartbeat<V>? _heartbeat;
    private readonly ColumnGenerationLayerRequestBuilder<V>? _layerRequestBuilder;
    private readonly Func<Task<IReadOnlyList<BinLayer>>> _initialColumns;
    private readonly Func<ColumnGenerationState<V>, Task<IReadOnlyDictionary<DemandModeKey, V>>> _solveRmpAndExtractShadowPrice;
    private readonly Func<ColumnGenerationState<V>, Task<ColumnGenerationLpResult<V>>>? _solveRmpWithResult;
    private readonly Func<ColumnGenerationState<V>, IReadOnlyList<Bpp3dLayerGenerationResult<V>>, Task<IReadOnlyList<Bpp3dLayerGenerationResult<V>>>> _filterByReducedCost;
    private readonly Func<IReadOnlyList<BinLayer>, IReadOnlyList<BinLayer>> _deduplicateColumns;
    private readonly Func<ColumnGenerationState<V>, Task> _solveFinalMilp;
    private readonly Func<ColumnGenerationState<V>, Task<ColumnGenerationFinalResult<V>>>? _solveFinalMilpWithResult;
    private readonly Func<ColumnGenerationState<V>, Task> _analyzeSolution;
    private readonly Func<ColumnGenerationState<V>, Task> _onIterationHeartbeat;

    public ColumnGenerationAlgorithm(
        IBpp3dLayerGenerator<V> layerGenerator,
        ColumnGenerationRmpSolver<V>? rmpSolver = null,
        ColumnGenerationFinalSolver<V>? finalMilpSolver = null,
        ColumnGenerationSolutionAnalyzer<V>? solutionAnalyzer = null,
        ColumnGenerationHeartbeat<V>? heartbeat = null,
        ColumnGenerationLayerRequestBuilder<V>? layerRequestBuilder = null,
        Func<Task<IReadOnlyList<BinLayer>>>? initialColumns = null,
        Func<ColumnGenerationState<V>, Task<IReadOnlyDictionary<DemandModeKey, V>>>? solveRmpAndExtractShadowPrice = null,
        Func<ColumnGenerationState<V>, Task<ColumnGenerationLpResult<V>>>? solveRmpWithResult = null,
        Func<ColumnGenerationState<V>, IReadOnlyList<Bpp3dLayerGenerationResult<V>>, Task<IReadOnlyList<Bpp3dLayerGenerationResult<V>>>>? filterByReducedCost = null,
        Func<IReadOnlyList<BinLayer>, IReadOnlyList<BinLayer>>? deduplicateColumns = null,
        Func<ColumnGenerationState<V>, Task>? solveFinalMilp = null,
        Func<ColumnGenerationState<V>, Task<ColumnGenerationFinalResult<V>>>? solveFinalMilpWithResult = null,
        Func<ColumnGenerationState<V>, Task>? analyzeSolution = null,
        Func<ColumnGenerationState<V>, Task>? onIterationHeartbeat = null) {
        _layerGenerator = layerGenerator;
        _rmpSolver = rmpSolver;
        _finalMilpSolver = finalMilpSolver;
        _solutionAnalyzer = solutionAnalyzer;
        _heartbeat = heartbeat;
        _layerRequestBuilder = layerRequestBuilder;
        _initialColumns = initialColumns ?? (() => Task.FromResult<IReadOnlyList<BinLayer>>(Array.Empty<BinLayer>()));
        _solveRmpAndExtractShadowPrice = solveRmpAndExtractShadowPrice ?? (_ => Task.FromResult<IReadOnlyDictionary<DemandModeKey, V>>(new Dictionary<DemandModeKey, V>()));
        _solveRmpWithResult = solveRmpWithResult;
        _filterByReducedCost = filterByReducedCost ?? ((_, layers) => Task.FromResult(layers));
        _deduplicateColumns = deduplicateColumns ?? (cols => cols.Distinct().ToList());
        _solveFinalMilp = solveFinalMilp ?? (_ => Task.CompletedTask);
        _solveFinalMilpWithResult = solveFinalMilpWithResult;
        _analyzeSolution = analyzeSolution ?? (_ => Task.CompletedTask);
        _onIterationHeartbeat = onIterationHeartbeat ?? (_ => Task.CompletedTask);
    }

    /// <summary>执行列生成求解 / Execute column generation solving.</summary>
    public async Task<Result<ColumnGenerationResult<V>, ErrorCode, Error<ErrorCode>>> SolveAsync(
        IReadOnlyList<Item> items,
        ColumnGenerationConfig? config = null) {
        config ??= ColumnGenerationConfig.Default;
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        IReadOnlyList<BinLayer> columns = _deduplicateColumns(await _initialColumns());
        bool terminatedByIterationLimit = false;
        bool terminatedByTimeLimit = false;
        int iterations = 0;
        int lpSolvedTimes = 0;
        bool finalSolved = false;
        var latestShadowPrices = new Dictionary<DemandModeKey, V>();
        var lpObjectives = new List<V?>();
        var lpInfos = new List<IReadOnlyDictionary<string, string>>();
        V? finalObjective = default;
        var finalInfo = new Dictionary<string, string>();
        IReadOnlyList<ContinuousCylinderRadiusSolverPrototype> prototypes = ContinuousRadiusSolverHelpers.PrototypesFromItems(items);

        while (iterations < config.IterationLimit) {
            if (config.TimeLimit is not null &&
                DateTimeOffset.UtcNow - startedAt >= config.TimeLimit.Value) {
                terminatedByTimeLimit = true;
                break;
            }

            var state = new ColumnGenerationState<V>(
                iterations, columns, Array.Empty<Bin<BinLayer, FltX>>(),
                latestShadowPrices, prototypes,
                new Dictionary<string, FltX>(),
                new Dictionary<string, IReadOnlyDictionary<string, FltX>>());

            ColumnGenerationLpResult<V> lpResult;
            if (_rmpSolver is not null) {
                Result<ColumnGenerationLpResult<V>, ErrorCode, Error<ErrorCode>> rmpRet = await _rmpSolver(state);
                if (rmpRet is not Ok<ColumnGenerationLpResult<V>, ErrorCode, Error<ErrorCode>> ok) {
                    Error<ErrorCode> err = (rmpRet as Failed<ColumnGenerationLpResult<V>, ErrorCode, Error<ErrorCode>>)?.Error
                        ?? new Err<ErrorCode>(ErrorCode.Unknown);
                    return new Failed<ColumnGenerationResult<V>, ErrorCode, Error<ErrorCode>>(err);
                }
                lpResult = ok.Value;
            }
            else if (_solveRmpWithResult is not null) {
                lpResult = await _solveRmpWithResult(state);
            }
            else {
                lpResult = new ColumnGenerationLpResult<V>(await _solveRmpAndExtractShadowPrice(state));
            }

            latestShadowPrices = new Dictionary<DemandModeKey, V>(lpResult.ShadowPrices);
            lpObjectives.Add(lpResult.Objective);
            lpInfos.Add(lpResult.Info ?? new Dictionary<string, string>());
            lpSolvedTimes++;

            var refreshState = new ColumnGenerationState<V>(
                iterations, columns, Array.Empty<Bin<BinLayer, FltX>>(),
                lpResult.ShadowPrices, prototypes,
                new Dictionary<string, FltX>(),
                new Dictionary<string, IReadOnlyDictionary<string, FltX>>());

            Bpp3dLayerGenerationRequest<V> request = _layerRequestBuilder is not null
                ? await _layerRequestBuilder(refreshState, items, config)
                : new Bpp3dLayerGenerationRequest<V>(iterations, items.Cast<object>().ToList(), columns, lpResult.ShadowPrices, config.MaxColumnsPerIteration);

            IReadOnlyList<Bpp3dLayerGenerationResult<V>> candidates = await _layerGenerator.GenerateAsync(request);
            IReadOnlyList<Bpp3dLayerGenerationResult<V>> accepted = await _filterByReducedCost(refreshState, candidates.ToList());
            if (accepted.Count == 0) {
                break;
            }

            columns = _deduplicateColumns(columns.Concat(accepted.Select(r => r.Layer)).ToList());
            var heartbeatState = new ColumnGenerationState<V>(
                iterations, columns, Array.Empty<Bin<BinLayer, FltX>>(),
                lpResult.ShadowPrices, prototypes,
                new Dictionary<string, FltX>(),
                new Dictionary<string, IReadOnlyDictionary<string, FltX>>());
            await _onIterationHeartbeat(heartbeatState);
            if (_heartbeat is not null) {
                await _heartbeat(heartbeatState);
            }

            iterations++;
        }

        if (iterations >= config.IterationLimit) {
            terminatedByIterationLimit = true;
        }

        var finalState = new ColumnGenerationState<V>(
            iterations, columns, Array.Empty<Bin<BinLayer, FltX>>(),
            latestShadowPrices, prototypes,
            new Dictionary<string, FltX>(),
            new Dictionary<string, IReadOnlyDictionary<string, FltX>>());

        if (config.FinalMilpEnabled) {
            ColumnGenerationFinalResult<V>? finalResult = null;
            if (_finalMilpSolver is not null) {
                Result<ColumnGenerationFinalResult<V>, ErrorCode, Error<ErrorCode>> finalRet = await _finalMilpSolver(finalState);
                if (finalRet is not Ok<ColumnGenerationFinalResult<V>, ErrorCode, Error<ErrorCode>> finalOk) {
                    Error<ErrorCode> fErr = (finalRet as Failed<ColumnGenerationFinalResult<V>, ErrorCode, Error<ErrorCode>>)?.Error
                        ?? new Err<ErrorCode>(ErrorCode.Unknown);
                    return new Failed<ColumnGenerationResult<V>, ErrorCode, Error<ErrorCode>>(fErr);
                }
                finalResult = finalOk.Value;
            }
            else if (_solveFinalMilpWithResult is not null) {
                finalResult = await _solveFinalMilpWithResult(finalState);
            }

            if (finalResult is not null) {
                columns = _deduplicateColumns(finalResult.Columns);
                finalObjective = finalResult.Objective;
                finalInfo = new Dictionary<string, string>(finalResult.Info ?? new Dictionary<string, string>());
                IReadOnlyDictionary<string, FltX> solverResults = ContinuousRadiusSolverHelpers.ExtractResults(finalResult.Info ?? new Dictionary<string, string>());
                finalState = new ColumnGenerationState<V>(
                    iterations, columns, finalResult.Bins ?? Array.Empty<Bin<BinLayer, FltX>>(),
                    latestShadowPrices, prototypes,
                    new Dictionary<string, FltX>(solverResults),
                    finalResult.PwlContinuousRadiusResults ?? new Dictionary<string, IReadOnlyDictionary<string, FltX>>());
            }
            else {
                await _solveFinalMilp(finalState);
            }
            finalSolved = true;
        }

        await _analyzeSolution(finalState);
        if (_solutionAnalyzer is not null) {
            Result<Success, ErrorCode, Error<ErrorCode>> ar = await _solutionAnalyzer(finalState);
            if (ar is Failed<Success, ErrorCode, Error<ErrorCode>> af) {
                return new Failed<ColumnGenerationResult<V>, ErrorCode, Error<ErrorCode>>(af.Error);
            }

            if (ar is Fatal<Success, ErrorCode, Error<ErrorCode>> aFat) {
                return new Fatal<ColumnGenerationResult<V>, ErrorCode, Error<ErrorCode>>(aFat.Errors);
            }
        }

        return new Ok<ColumnGenerationResult<V>, ErrorCode, Error<ErrorCode>>(
            new ColumnGenerationResult<V>(
                columns, iterations, terminatedByIterationLimit, terminatedByTimeLimit,
                lpSolvedTimes, finalSolved, lpObjectives, finalObjective,
                DateTimeOffset.UtcNow - startedAt, lpInfos, finalInfo,
                finalState.ContinuousRadiusSolverResults, finalState.PwlContinuousRadiusResults));
    }
}
