#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Solver.Output
{
    // ===== SolverOutput hierarchy =====

    /// <summary>
    /// 求解器输出的抽象根。
    /// Abstract root for solver output.
    /// </summary>
    public abstract record SolverOutput;

    /// <summary>
    /// 统一求解器输出，包含通用求解统计信息。
    /// Unified solver output, containing common solving statistics.
    /// </summary>
    public abstract record UnifiedSolverOutput : SolverOutput
    {
        /// <summary>迭代次数（可选）/ Iteration count (optional)</summary>
        public ulong? Iterations { get; init; }
        /// <summary>节点数（可选）/ Node count (optional)</summary>
        public ulong? NodeCount { get; init; }
        /// <summary>最优界（可选）/ Best bound (optional)</summary>
        public Flt64? BestBound { get; init; }
        /// <summary>MIP 间隙（可选）/ MIP gap (optional)</summary>
        public Flt64? MipGap { get; init; }
        /// <summary>求解时间（可选）/ Solve time (optional)</summary>
        public TimeSpan? SolveTime { get; init; }
    }

    // ===== Marker interfaces (Kotlin sealed sub-interfaces → C# marker interfaces) =====

    /// <summary>
    /// 线性求解器输出标记接口。
    /// Linear solver output marker interface.
    /// </summary>
    public interface ILinearSolverOutputMarker { }

    /// <summary>
    /// 二次求解器输出标记接口。
    /// Quadratic solver output marker interface.
    /// </summary>
    public interface IQuadraticSolverOutputMarker { }

    // ===== FeasibleSolverOutput<V> =====

    /// <summary>
    /// 可行求解器输出，包含目标值、解和求解统计信息。
    /// Feasible solver output: objective, solution, and solving statistics.
    /// </summary>
    public sealed record FeasibleSolverOutput<V>(
        Flt64 Obj,
        Solution<V> Solution,
        TimeSpan Time,
        Flt64 PossibleBestObj,
        Flt64 Gap,
        ulong? Iterations = null,
        ulong? NodeCount = null,
        Flt64? BestBound = null,
        Flt64? MipGap = null,
        TimeSpan? SolveTime = null,
        V? ObjValueOrNull = null,
        V? PossibleBestObjValueOrNull = null,
        V? BestBoundValueOrNull = null)
        : UnifiedSolverOutput
          , ILinearSolverOutputMarker
          , IQuadraticSolverOutputMarker
        where V : struct, IRealNumber<V>, INumberField<V>
    {
        /// <summary>获取目标值，缺失时返回失败 / Get objective value, failing when missing</summary>
        public Result<V, ErrorCode, Error<ErrorCode>> ObjValue() =>
            ObjValueOrNull is { } v
                ? Results.Ok(v)
                : Results.Failed<V>(new Err<ErrorCode>(ErrorCode.IllegalArgument,
                    "FeasibleSolverOutput.objValue is unavailable. Provide explicit ObjValueOrNull for non-Flt64 solution."));

        /// <summary>获取可能的最优目标值 / Get possible best objective value</summary>
        public Result<V, ErrorCode, Error<ErrorCode>> PossibleBestObjValue() =>
            PossibleBestObjValueOrNull is { } v
                ? Results.Ok(v)
                : Results.Failed<V>(new Err<ErrorCode>(ErrorCode.IllegalArgument,
                    "FeasibleSolverOutput.possibleBestObjValue is unavailable for non-Flt64 solution."));

        /// <summary>获取最优界值 / Get best bound value</summary>
        public Result<V, ErrorCode, Error<ErrorCode>> BestBoundValue() =>
            BestBoundValueOrNull is { } v
                ? Results.Ok(v)
                : Results.Failed<V>(new Err<ErrorCode>(ErrorCode.IllegalArgument,
                    "FeasibleSolverOutput.bestBoundValue is unavailable when BestBound is present."));

        /// <summary>转换为另一种值类型（通用版本）/ Convert to another value type (generic version)</summary>
        public FeasibleSolverOutput<U> ConvertTo<U>(Func<V, U> valueConverter)
            where U : struct, IRealNumber<U>, INumberField<U>
        {
            return new FeasibleSolverOutput<U>(
                Obj,
                new Solution<U>(Solution.Values.Select(valueConverter).ToList()),
                Time,
                PossibleBestObj,
                Gap,
                Iterations,
                NodeCount,
                BestBound,
                MipGap,
                SolveTime);
        }
    }

    // ===== Infeasible outputs =====

    /// <summary>
    /// 线性不可行求解器输出 / Linear infeasible solver output.
    /// </summary>
    public sealed record LinearInfeasibleSolverOutput(
        SolverStatus Status,
        ulong? Iterations = null,
        ulong? NodeCount = null,
        TimeSpan? SolveTime = null)
        : UnifiedSolverOutput
          , ILinearSolverOutputMarker;

    /// <summary>
    /// 二次不可行求解器输出 / Quadratic infeasible solver output.
    /// </summary>
    public sealed record QuadraticInfeasibleSolverOutput(
        SolverStatus Status,
        ulong? Iterations = null,
        ulong? NodeCount = null,
        TimeSpan? SolveTime = null)
        : UnifiedSolverOutput
          , IQuadraticSolverOutputMarker;

    // ===== SolverOutputWithIIS =====

    /// <summary>
    /// 带 IIS 的求解器输出 / Solver output with IIS.
    /// </summary>
    /// <typeparam name="IIS">IIS 类型 / IIS type</typeparam>
    public sealed record SolverOutputWithIIS<IIS>(
        SolverOutput Output,
        IIS? Iis);

    // ===== Extension methods =====

    /// <summary>
    /// SolverOutput 扩展方法 / SolverOutput extension methods.
    /// </summary>
    public static class SolverOutputExtensions
    {
        /// <summary>附加 IIS / Attach IIS</summary>
        public static SolverOutputWithIIS<IIS> WithIIS<IIS>(this SolverOutput output, IIS iis) =>
            new(output, iis);

        /// <summary>移除 IIS / Remove IIS</summary>
        public static SolverOutputWithoutIIS WithoutIIS(this SolverOutput output) =>
            new(output);
    }

    /// <summary>
    /// 无 IIS 的求解器输出包装 / Solver output wrapper without IIS.
    /// </summary>
    public sealed record SolverOutputWithoutIIS(SolverOutput Output);
}
