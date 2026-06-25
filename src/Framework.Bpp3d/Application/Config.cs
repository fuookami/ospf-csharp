#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Framework.Bpp3d.Infra;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Bpp3d.Application
{
    /// <summary>列生成配置 / Column generation configuration.</summary>
    public sealed record ColumnGenerationConfig(
        int IterationLimit = 128,
        TimeSpan? TimeLimit = null,
        int MaxColumnsPerIteration = 64,
        bool FinalMilpEnabled = true)
    {
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

    public static class ContinuousRadiusSolverHelpers
    {
        public static IReadOnlyList<ContinuousCylinderRadiusSolverPrototype> PrototypesFromItems(IReadOnlyList<Item> items)
        {
            var prototypes = new Dictionary<string, ContinuousCylinderRadiusSolverPrototype>();
            // Stub: in full implementation, would inspect item shapes
            return prototypes.Values.ToList();
        }

        public static IReadOnlyDictionary<string, FltX> ExtractResults(IReadOnlyDictionary<string, string> info)
        {
            const string prefix = "continuous_radius_solver_selected_";
            var results = new Dictionary<string, FltX>();
            foreach (var (key, value) in info)
            {
                if (key.StartsWith(prefix) && double.TryParse(value, out var d))
                    results[key[prefix.Length..]] = new FltX((double)d);
            }
            return results;
        }
    }
}
