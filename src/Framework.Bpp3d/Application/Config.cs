#nullable enable

using Fuookami.Ospf.Framework.Bpp3d.Infra;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Geometry;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Framework.Bpp3d.Application;
/// <summary>列生成配置 / Column generation configuration.</summary>
public sealed record ColumnGenerationConfig(
    int IterationLimit = 128,
    TimeSpan? TimeLimit = null,
    int MaxColumnsPerIteration = 64,
    bool FinalMilpEnabled = true) {
    public static ColumnGenerationConfig Default { get; } = new();
    public static TimeSpan InfiniteTimeLimit => System.Threading.Timeout.InfiniteTimeSpan;
}

/// <summary>列生成状态 / Column generation state.</summary>
public sealed record ColumnGenerationState<V>(
    int Iteration,
    IReadOnlyList<BinLayer> Columns,
    IReadOnlyList<Bin<BinLayer, FltX>> Bins,
    IReadOnlyDictionary<DemandModeKey, V> ShadowPrices,
    IReadOnlyList<ContinuousCylinderRadiusSolverPrototype> ContinuousRadiusSolverPrototypes,
    IReadOnlyDictionary<string, FltX> ContinuousRadiusSolverResults,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, FltX>> PwlContinuousRadiusResults)
    where V : struct, IRealNumber<V>, INumberField<V>;

/// <summary>列生成 LP 求解结果 / Column generation LP solve result.</summary>
public sealed record ColumnGenerationLpResult<V>(
    IReadOnlyDictionary<DemandModeKey, V> ShadowPrices,
    V? Objective = default,
    IReadOnlyDictionary<string, string>? Info = null)
    where V : struct, IRealNumber<V>, INumberField<V>;

/// <summary>列生成最终 MILP 求解结果 / Column generation final MILP solve result.</summary>
public sealed record ColumnGenerationFinalResult<V>(
    IReadOnlyList<BinLayer> Columns,
    IReadOnlyList<Bin<BinLayer, FltX>>? Bins = null,
    V? Objective = default,
    IReadOnlyDictionary<string, string>? Info = null,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, FltX>>? PwlContinuousRadiusResults = null)
    where V : struct, IRealNumber<V>, INumberField<V>;

/// <summary>列生成完整结果 / Column generation complete result.</summary>
public sealed record ColumnGenerationResult<V>(
    IReadOnlyList<BinLayer> Columns,
    int IterationCount,
    bool TerminatedByIterationLimit,
    bool TerminatedByTimeLimit,
    int LpSolvedTimes,
    bool FinalSolved,
    IReadOnlyList<V?> LpObjectives,
    V? FinalObjective,
    TimeSpan Elapsed,
    IReadOnlyList<IReadOnlyDictionary<string, string>> LpInfos,
    IReadOnlyDictionary<string, string> FinalInfo,
    IReadOnlyDictionary<string, FltX> ContinuousRadiusSolverResults,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, FltX>> PwlContinuousRadiusResults)
    where V : struct, IRealNumber<V>, INumberField<V>;

// ===== Delegates =====

public delegate Task<Result<ColumnGenerationLpResult<V>, ErrorCode, Error<ErrorCode>>> ColumnGenerationRmpSolver<V>(ColumnGenerationState<V> state)
    where V : struct, IRealNumber<V>, INumberField<V>;

public delegate Task<Result<ColumnGenerationFinalResult<V>, ErrorCode, Error<ErrorCode>>> ColumnGenerationFinalSolver<V>(ColumnGenerationState<V> state)
    where V : struct, IRealNumber<V>, INumberField<V>;

public delegate Task<Result<Success, ErrorCode, Error<ErrorCode>>> ColumnGenerationSolutionAnalyzer<V>(ColumnGenerationState<V> state)
    where V : struct, IRealNumber<V>, INumberField<V>;

public delegate Task ColumnGenerationHeartbeat<V>(ColumnGenerationState<V> state)
    where V : struct, IRealNumber<V>, INumberField<V>;

public delegate Task<Bpp3dLayerGenerationRequest<V>> ColumnGenerationLayerRequestBuilder<V>(ColumnGenerationState<V> state, IReadOnlyList<Item> items, ColumnGenerationConfig config)
    where V : struct, IRealNumber<V>, INumberField<V>;

// ===== Helpers =====

public static class ContinuousRadiusSolverHelpers {
    /// <summary>
    /// 从货物列表抽取连续半径 solver 变量原型 / Extract continuous-radius solver prototypes from the item list.
    /// </summary>
    /// <param name="items">货物列表 / Item list.</param>
    /// <returns>原型列表，按货物顺序、变量名去重 / Prototypes in item order, de-duplicated by variable name.</returns>
    /// <remarks>
    /// 基线 `continuousRadiusSolverPrototypesFromItems` 的做法：**只取竖直圆柱**，为每个这样的货物生成一个原型，
    /// 按变量名去重（先到先得），并保持插入顺序。
    ///
    /// **门控是半径权重函数键**：`continuousRadiusSelectionResult` 在该键为 null 时返回 null，
    /// 于是**没有键的竖直圆柱不产生原型**。
    ///
    /// 实现时 C# 的形状模型**还没有这个键**，只能对所有竖直圆柱产出原型，并在文档里记下这处偏差。
    /// 给 `CylinderPackingShape3` 补上了 `RadiusWeightFunctionKey`（以及 `RadiusMin / RadiusMax`），
    /// **门控随之恢复**，实现。
    ///
    /// The code takes **vertical cylinders only**, builds one prototype per such item, de-duplicates by variable name (first wins) and preserves insertion
    /// order.
    ///
    /// **The gate is the radius weight-function key**: its `continuousRadiusSelectionResult` returns null when that key is null, so **a vertical
    /// cylinder without the key produces no prototype**.
    ///
    /// implemented this while the C# shape model **still lacked that key**, so it produced prototypes for every vertical cylinder and recorded the
    /// deviation in the documentation. added `RadiusWeightFunctionKey` (plus `RadiusMin / RadiusMax`) to `CylinderPackingShape3`, **restoring the gate** and
    /// bringing the implementation in line with the code.
    /// </remarks>
    public static IReadOnlyList<ContinuousCylinderRadiusSolverPrototype> PrototypesFromItems(IReadOnlyList<Item> items) {
        var prototypes = new Dictionary<string, ContinuousCylinderRadiusSolverPrototype>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (Item item in items) {
            // 只取竖直圆柱；横置圆柱不进入连续半径路径。
            // Vertical cylinders only; horizontally oriented ones stay out of the continuous-radius path.
            if (item.Shape is not CylinderPackingShape3 cylinder || cylinder.Cylinder.Axis != Axis3.Y) {
                continue;
            }

            // **没有半径权重函数键的竖直圆柱不参与连续半径优化**。
            // **A vertical cylinder without a radius weight-function key does not participate in continuous-radius optimisation**, matching the code.
            if (cylinder.RadiusWeightFunctionKey is null) {
                continue;
            }

            ContinuousCylinderRadiusSolverPrototype prototype = ContinuousRadiusHelpers.Prototype(
                source: ContinuousRadiusHelpers.Source(item),
                key: cylinder.RadiusWeightFunctionKey,
                axis: cylinder.Cylinder.Axis,
                min: cylinder.RadiusMin,
                max: cylinder.RadiusMax
            );
            if (prototypes.TryAdd(prototype.VariableName, prototype)) {
                order.Add(prototype.VariableName);
            }
        }

        var ordered = new List<ContinuousCylinderRadiusSolverPrototype>(order.Count);
        foreach (string variableName in order) {
            ordered.Add(prototypes[variableName]);
        }
        return ordered;
    }

    public static IReadOnlyDictionary<string, FltX> ExtractResults(IReadOnlyDictionary<string, string> info) {
        const string prefix = "continuous_radius_solver_selected_";
        var results = new Dictionary<string, FltX>();
        foreach ((string? key, string? value) in info) {
            if (key.StartsWith(prefix) && double.TryParse(value, out double d)) {
                results[key[prefix.Length..]] = new FltX((double)d);
            }
        }
        return results;
    }
}
