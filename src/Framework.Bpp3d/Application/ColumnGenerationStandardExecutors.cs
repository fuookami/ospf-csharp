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
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp3d.Domain.LayerAssignment.Service.Limits;
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

        // **开关必须真的做事**：`EnableFinalBinCapacityConstraint` 过去只是一个没有任何读取方的布尔值，
        // 于是"容量约束已启用"与"容量约束什么都不做"同时成立。这里让它真正拒绝超载的箱子。
        // **The switch has to do something**: `EnableFinalBinCapacityConstraint` used to be a boolean nobody read, so "the capacity constraint is
        // enabled" and "the capacity constraint does nothing" both held. Here it actually rejects an overweight bin.
        // 深度开关同样必须真的做事：装满的层数超过箱子深度时直接失败，而不是等求解器给出不可行。
        // The depth switch must do something too: fail as soon as the loaded layers exceed the bin depth instead of waiting for the solver to report
        // infeasibility.
        if (_config.EnableFinalBinDepthConstraint) {
            Result<Success, ErrorCode, Error<ErrorCode>> depthCheck = CheckBinDepths(bins);
            if (depthCheck is Failed<Success, ErrorCode, Error<ErrorCode>> depthFailed) {
                return new Failed<ColumnGenerationFinalResult<FltX>, ErrorCode, Error<ErrorCode>>(depthFailed.Error);
            }
        }

        if (_config.EnableFinalBinCapacityConstraint) {
            Result<Success, ErrorCode, Error<ErrorCode>> capacityCheck = CheckBinCapacities(bins);
            if (capacityCheck is Failed<Success, ErrorCode, Error<ErrorCode>> capacityFailed) {
                return new Failed<ColumnGenerationFinalResult<FltX>, ErrorCode, Error<ErrorCode>>(capacityFailed.Error);
            }
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
            ["bin_depth_constraint"] = _config.EnableFinalBinDepthConstraint ? "checked" : "disabled",
            ["bin_depth_right_hand_sides"] = bins.Count.ToString(),
            ["bin_capacity_constraint"] = _config.EnableFinalBinCapacityConstraint ? "checked" : "disabled",
            ["bin_capacity_right_hand_sides"] = BuildCapacityRightHandSides(bins).Count.ToString(),
            ["continuous_radius_solver_prototype_count"] = state.ContinuousRadiusSolverPrototypes.Count.ToString(),
            ["continuous_radius_solver_prototype_variables"] = string.Join("|", state.ContinuousRadiusSolverPrototypes.Select(p => p.VariableName)),
        };

        return new Ok<ColumnGenerationFinalResult<FltX>, ErrorCode, Error<ErrorCode>>(
            new ColumnGenerationFinalResult<FltX>(state.Columns, bins, new FltX(solved.Obj.ToDouble()), info));
    };

    /// <summary>创建层请求构建器 / Create layer request builder.</summary>
    /// <remarks>
    /// **开关在这里才真正起作用**：`EnableShadowPriceAwareRequestScore` 为真时把评分函数交给请求，
    /// 为假时留空——而"留空"与"给了评分"必须是**可区分**的两条路径，否则开关又成了摆设。
    ///
    /// **The switch finally does something here**: when `EnableShadowPriceAwareRequestScore` is true the scorer is attached to the request, and when
    /// false it stays null — and "null" versus "a scorer attached" have to be **distinguishable** paths, or the switch is decoration again.
    /// </remarks>
    public ColumnGenerationLayerRequestBuilder<FltX> RequestBuilder() => async (state, items, cgConfig) => {
        return new Bpp3dLayerGenerationRequest<FltX>(
            Iteration: state.Iteration,
            Items: items.Cast<object>().ToList(),
            ExistingLayers: state.Columns,
            ShadowPrices: state.ShadowPrices,
            MaxCandidates: cgConfig.MaxColumnsPerIteration,
            ScoreByShadowPrice: _config.EnableShadowPriceAwareRequestScore
                ? Bpp3dShadowPriceScoring.Scorer<FltX>()
                : null);
    };

    /// <summary>
    /// 校验每个最终箱子的已装层深都不超过箱深 / Validate that the loaded layer depths of every final bin stay within the bin depth.
    /// </summary>
    /// <remarks>
    /// **层深是逐层累加的**：只看最后一层或者只看单层，都会放过"三层各占一半、合起来超过箱深"的箱子。
    /// 单位混用同样直接失败——`Quantity.Add` 只加数值不换单位，静默相加永远算得出来。
    ///
    /// **Layer depths accumulate**: looking at the last layer only, or at a single layer only, lets through a bin whose three half-depth layers together
    /// exceed the bin. Mixed units fail too — `Quantity.Add` adds the numbers without converting, so a silent addition always produces a number.
    /// </remarks>
    /// <param name="bins">最终箱子 / The final bins.</param>
    /// <returns>校验结果 / The validation result.</returns>
    private Result<Success, ErrorCode, Error<ErrorCode>> CheckBinDepths(IReadOnlyList<Bin<BinLayer, FltX>> bins) {
        for (int binIndex = 0; binIndex < bins.Count; ++binIndex) {
            Bin<BinLayer, FltX> bin = bins[binIndex];
            Quantity<FltX> binDepth = bin.Type.Depth;
            Quantity<FltX> used = new(new FltX(0), binDepth.Unit);
            foreach (QuantityPlacement3<BinLayer, FltX> layerPlacement in bin.Units) {
                Quantity<FltX> layerDepth = layerPlacement.Unit.Depth;
                if (!used.Unit.Equals(layerDepth.Unit)) {
                    return Results.Failed<Success>(
                        new Err<ErrorCode>(ErrorCode.IllegalArgument,
                            $"bin[{binIndex}] mixes depth units {used.Unit} and {layerDepth.Unit}"));
                }
                used = used.Add(layerDepth);
            }

            var constraint = new BinDepthConstraint<FltX>(new[] { binDepth });
            Result<bool, ErrorCode, Error<ErrorCode>> holds =
                constraint.CanHold(0, used, new Quantity<FltX>(new FltX(0), binDepth.Unit));
            if (holds is Failed<bool, ErrorCode, Error<ErrorCode>> holdFailed) {
                return Results.Failed<Success>(holdFailed.Error);
            }
            if (!((Ok<bool, ErrorCode, Error<ErrorCode>>)holds).Value) {
                return Results.Failed<Success>(
                    new Err<ErrorCode>(ErrorCode.IllegalArgument,
                        $"bin[{binIndex}] type '{bin.Type.TypeCode}' exceeds its depth: layers use {used.ToDouble()} of {binDepth.ToDouble()}"));
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 校验每个最终箱子都没有超载 / Validate that no final bin is overweight.
    /// </summary>
    /// <remarks>
    /// **重量未知与重量为零是两件事**：`Item.Weight` 为 `null` 表示该货物没有重量信息，此时**不能当成 0 通过**——
    /// 那会让"有载重约束"与"没有载重信息"看起来一模一样，正是本项目反复出现的失败模式。
    ///
    /// **Unknown weight and zero weight are two different things**: a `null` `Item.Weight` means the item carries no weight information, and that must
    /// **not** pass as 0 — otherwise "the capacity constraint applies" and "there is no weight information" look exactly alike, which is the failure
    /// mode this repository keeps hitting.
    /// </remarks>
    /// <param name="bins">最终箱子 / The final bins.</param>
    /// <returns>校验结果 / The validation result.</returns>
    private Result<Success, ErrorCode, Error<ErrorCode>> CheckBinCapacities(IReadOnlyList<Bin<BinLayer, FltX>> bins) {
        for (int binIndex = 0; binIndex < bins.Count; ++binIndex) {
            Bin<BinLayer, FltX> bin = bins[binIndex];
            Quantity<FltX>? used = null;
            foreach (QuantityPlacement3<BinLayer, FltX> layerPlacement in bin.Units) {
                foreach (QuantityPlacement3<Item, FltX> itemPlacement in layerPlacement.Unit.Units) {
                    Quantity<FltX>? weight = itemPlacement.Unit.Weight;
                    if (weight is null) {
                        return Results.Failed<Success>(
                            new Err<ErrorCode>(ErrorCode.IllegalArgument,
                                $"bin[{binIndex}] item '{itemPlacement.Unit.Id}' carries no weight; the capacity constraint cannot be verified"));
                    }
                    if (used is null) {
                        used = weight;
                        continue;
                    }
                    // 单位不一致时 `Add` 只加数值、不换单位——**必须自己拦，不能让它静默相加**。
                    // When units differ `Add` only adds the numbers and keeps the left unit — **this has to be caught here rather than silently added**.
                    if (!used.Unit.Equals(weight.Unit)) {
                        return Results.Failed<Success>(
                            new Err<ErrorCode>(ErrorCode.IllegalArgument,
                                $"bin[{binIndex}] mixes weight units {used.Unit} and {weight.Unit}"));
                    }
                    used = used.Add(weight);
                }
            }

            if (used is null) {
                continue;
            }
            var constraint = new BinCapacityConstraint<FltX>(new[] { bin.Type.Capacity });
            Result<bool, ErrorCode, Error<ErrorCode>> holds = constraint.CanHold(0, used, ZeroWeightOf(used));
            if (holds is Failed<bool, ErrorCode, Error<ErrorCode>> holdFailed) {
                return Results.Failed<Success>(holdFailed.Error);
            }
            if (!((Ok<bool, ErrorCode, Error<ErrorCode>>)holds).Value) {
                return Results.Failed<Success>(
                    new Err<ErrorCode>(ErrorCode.IllegalArgument,
                        $"bin[{binIndex}] type '{bin.Type.TypeCode}' exceeds its weight capacity"));
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>同单位的零重量 / A zero weight in the same unit.</summary>
    /// <param name="like">同单位的参考量 / A reference quantity with the same unit.</param>
    /// <returns>零重量 / The zero weight.</returns>
    private static Quantity<FltX> ZeroWeightOf(Quantity<FltX> like) =>
        new(new FltX(0), like.Unit);

    /// <summary>每只箱子的载重右端项 / The capacity right-hand sides of the final bins.</summary>
    /// <param name="bins">最终箱子 / The final bins.</param>
    /// <returns>右端项字典 / The right-hand sides.</returns>
    private static IReadOnlyDictionary<DemandShadowPriceKey, Quantity<FltX>> BuildCapacityRightHandSides(
        IReadOnlyList<Bin<BinLayer, FltX>> bins) {
        var constraint = new BinCapacityConstraint<FltX>(
            bins.Select(bin => bin.Type.Capacity).ToList());
        Result<IReadOnlyDictionary<DemandShadowPriceKey, Quantity<FltX>>, ErrorCode, Error<ErrorCode>> sides =
            constraint.RightHandSide();
        return sides is Ok<IReadOnlyDictionary<DemandShadowPriceKey, Quantity<FltX>>, ErrorCode, Error<ErrorCode>> ok
            ? ok.Value
            : new Dictionary<DemandShadowPriceKey, Quantity<FltX>>();
    }

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
