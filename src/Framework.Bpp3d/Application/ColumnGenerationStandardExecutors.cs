#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Framework.Bpp3d.Infra;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Framework.Bpp3d.Application;
/// <summary>列生成标准执行器配置 / Column generation standard executor configuration.</summary>
public sealed record ColumnGenerationStandardExecutorConfig(
    string RmpSolveNamePrefix = "bpp3d-rmp",
    string FinalSolveNamePrefix = "bpp3d-final",
    bool RmpToLogModel = false,
    bool FinalToLogModel = false,
    FltX? RmpVolumeCoefficient = null,
    FltX? FinalBinAmountCoefficient = null,
    bool EnableFinalBinDepthConstraint = true,
    bool EnableFinalBinCapacityConstraint = true,
    bool EnableShadowPriceAwareRequestScore = true,
    FltX? IntegralityTolerance = null,
    DepthBoundaryLayerOrientationPolicy? DepthBoundaryLayerOrientationPolicy = null) {
    public static ColumnGenerationStandardExecutorConfig Default { get; } = new();
}

/// <summary>列生成标准执行器 / Column generation standard executors.</summary>
public sealed class ColumnGenerationStandardExecutors {
    private readonly IColumnGenerationSolver _solver;
    private readonly IReadOnlyList<(Item Item, ulong Amount)> _itemDemands;
    private readonly IReadOnlyList<Bpp3dDemandEntry<FltX>> _demandEntries;
    private readonly IReadOnlyList<Bin<BinLayer, FltX>> _finalBins;
    private readonly ColumnGenerationStandardExecutorConfig _config;

    public ColumnGenerationStandardExecutors(
        IColumnGenerationSolver solver,
        IReadOnlyList<(Item Item, ulong Amount)> itemDemands,
        IReadOnlyList<Bpp3dDemandEntry<FltX>>? demandEntries = null,
        IReadOnlyList<Bin<BinLayer, FltX>>? finalBins = null,
        ColumnGenerationStandardExecutorConfig? config = null) {
        _solver = solver;
        _itemDemands = itemDemands;
        _demandEntries = demandEntries ?? BuildDemandEntries(itemDemands);
        _finalBins = finalBins ?? Array.Empty<Bin<BinLayer, FltX>>();
        _config = config ?? ColumnGenerationStandardExecutorConfig.Default;
    }

    public static ColumnGenerationStandardExecutors FromDemandEntries(
        IColumnGenerationSolver solver,
        IReadOnlyList<(Item Item, ulong Amount)> itemDemands,
        IReadOnlyList<Bpp3dDemandEntry<FltX>> demandEntries,
        IReadOnlyList<Bin<BinLayer, FltX>>? finalBins = null,
        ColumnGenerationStandardExecutorConfig? config = null)
        => new(solver, itemDemands, demandEntries, finalBins, config);

    /// <summary>创建 RMP 求解器 / Create RMP solver.</summary>
    public ColumnGenerationRmpSolver<FltX> RmpSolver() => async state => {
        var model = new LinearMetaModel<Flt64>(
            name: $"{_config.RmpSolveNamePrefix}-{state.Iteration}",
            objectCategory: ObjectCategory.Minimum,
            configuration: new MetaModelConfiguration());

        Result<IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>> lpResult = await _solver.SolveLPAsync(
            name: $"{_config.RmpSolveNamePrefix}-{state.Iteration}",
            metaModel: model,
            toLogModel: _config.RmpToLogModel);

        if (lpResult is not Ok<IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>> lpOk) {
            Error<ErrorCode> lpErr = (lpResult as Failed<IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>>)?.Error
                ?? new Err<ErrorCode>(ErrorCode.Unknown);
            return new Failed<ColumnGenerationLpResult<FltX>, ErrorCode, Error<ErrorCode>>(lpErr);
        }
        IColumnGenerationSolver.LpResult solved = lpOk.Value;

        // *** REFLECTION-FREE: MetaDualSolution direct construction ***
        var metaDual = new MetaDualSolution(
            solved.DualSolution,
            new Dictionary<IIntermediateSymbol, IReadOnlyList<(IConstraint<Flt64, LinearCategory>, Flt64)>>());

        var shadowPrices = new Dictionary<DemandModeKey, FltX>();
        var info = new Dictionary<string, string> {
            ["solver"] = _solver.Name,
            ["model"] = model.Name,
            ["lp_time_ms"] = solved.Time.TotalMilliseconds.ToString(),
            ["lp_gap"] = solved.Gap.ToString(),
            ["lp_objective"] = solved.Obj.ToString(),
            ["continuous_radius_solver_prototype_count"] = state.ContinuousRadiusSolverPrototypes.Count.ToString(),
            ["continuous_radius_solver_prototype_variables"] = string.Join("|", state.ContinuousRadiusSolverPrototypes.Select(p => p.VariableName)),
        };

        return new Ok<ColumnGenerationLpResult<FltX>, ErrorCode, Error<ErrorCode>>(
            new ColumnGenerationLpResult<FltX>(shadowPrices, new FltX(solved.Obj.ToDouble()), info));
    };

    /// <summary>创建最终 MILP 求解器 / Create final MILP solver.</summary>
    public ColumnGenerationFinalSolver<FltX> FinalSolver() => async state => {
        IReadOnlyList<Bin<BinLayer, FltX>> bins = _finalBins.Count > 0 ? _finalBins : FallbackFinalBins(state.Columns);
        if (bins.Count == 0) {
            return new Ok<ColumnGenerationFinalResult<FltX>, ErrorCode, Error<ErrorCode>>(
                new ColumnGenerationFinalResult<FltX>(state.Columns, Info: new Dictionary<string, string> { ["skipped"] = "no_final_bins" }));
        }

        var model = new LinearMetaModel<Flt64>(
            name: $"{_config.FinalSolveNamePrefix}-{state.Iteration}",
            objectCategory: ObjectCategory.Minimum,
            configuration: new MetaModelConfiguration());

        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> milpResult = await _solver.SolveMILPAsync(
            name: $"{_config.FinalSolveNamePrefix}-{state.Iteration}",
            metaModel: model,
            toLogModel: _config.FinalToLogModel);

        if (milpResult is not Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> milpOk) {
            Error<ErrorCode> milpErr = (milpResult as Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>)?.Error
                ?? new Err<ErrorCode>(ErrorCode.Unknown);
            return new Failed<ColumnGenerationFinalResult<FltX>, ErrorCode, Error<ErrorCode>>(milpErr);
        }
        FeasibleSolverOutput<Flt64> solved = milpOk.Value;

        var info = new Dictionary<string, string> {
            ["solver"] = _solver.Name,
            ["milp_time_ms"] = solved.Time.TotalMilliseconds.ToString(),
            ["milp_gap"] = solved.Gap.ToString(),
            ["milp_objective"] = solved.Obj.ToString(),
            ["selected_bin_count"] = bins.Count.ToString(),
            ["selected_layer_count"] = state.Columns.Count.ToString(),
            ["continuous_radius_solver_prototype_count"] = state.ContinuousRadiusSolverPrototypes.Count.ToString(),
            ["continuous_radius_solver_prototype_variables"] = string.Join("|", state.ContinuousRadiusSolverPrototypes.Select(p => p.VariableName)),
        };

        return new Ok<ColumnGenerationFinalResult<FltX>, ErrorCode, Error<ErrorCode>>(
            new ColumnGenerationFinalResult<FltX>(state.Columns, bins, new FltX(solved.Obj.ToDouble()), info));
    };

    /// <summary>创建层请求构建器 / Create layer request builder.</summary>
    public ColumnGenerationLayerRequestBuilder<FltX> RequestBuilder() => async (state, items, cgConfig) => {
        return new Bpp3dLayerGenerationRequest<FltX>(
            Iteration: state.Iteration,
            Items: items.Cast<object>().ToList(),
            ExistingLayers: state.Columns,
            ShadowPrices: state.ShadowPrices,
            MaxCandidates: cgConfig.MaxColumnsPerIteration);
    };

    private IReadOnlyList<Bin<BinLayer, FltX>> FallbackFinalBins(IReadOnlyList<BinLayer> columns) {
        var result = new List<Bin<BinLayer, FltX>>();
        foreach (BinLayer layer in columns) {
            if (layer.Bin is null) {
                continue;
            }

            result.Add(BinLayerHelpers.LayerBinOf(layer.Bin, Array.Empty<QuantityPlacement3<BinLayer, FltX>>()));
        }
        return result;
    }

    private static IReadOnlyList<Bpp3dDemandEntry<FltX>> BuildDemandEntries(IReadOnlyList<(Item Item, ulong Amount)> itemDemands) {
        return itemDemands.Select(d => new Bpp3dDemandEntry<FltX>(
            new Bpp3dDemandMode.ItemAmount(),
            new Bpp3dDemandKey.ItemKey(d.Item.Id),
            new FltX((double)d.Amount))).ToList();
    }
}
