#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Core.Solver;
/// <summary>
/// 列生成求解器接口。
/// Column generation solver interface.
/// </summary>
public interface IColumnGenerationSolver {
    /// <summary>求解器名称 / Solver name</summary>
    string Name { get; }

    /// <summary>求解 MILP 问题 / Solve MILP problem</summary>
    Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> SolveMILPAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default);

    /// <summary>使用选项求解 MILP 问题 / Solve MILP problem with options</summary>
    Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> SolveMILPAsync(
        LinearMetaModel<Flt64> metaModel,
        FrameworkSolveOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>异步求解 MILP 问题 / Asynchronously solve MILP problem (Task.Run)</summary>
    Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> SolveMILPBackgroundAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null)
        => Task.Run(() => SolveMILPAsync(name, metaModel, toLogModel, registrationStatusCallBack, solvingStatusCallBack));

    /// <summary>求解 MILP 问题并返回指定数量的解 / Solve MILP and return a solution pool</summary>
    Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions), ErrorCode, Error<ErrorCode>>> SolveMILPAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        ulong amount,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default);

    /// <summary>使用选项求解 MILP 并返回解池 / Solve MILP with options and return solution pool</summary>
    Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions), ErrorCode, Error<ErrorCode>>> SolveMILPWithSolutionPoolAsync(
        LinearMetaModel<Flt64> metaModel,
        FrameworkSolveOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>求解 LP 问题 / Solve LP problem</summary>
    Task<Result<LpResult, ErrorCode, Error<ErrorCode>>> SolveLPAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// LP 求解结果。
    /// LP solve result.
    /// </summary>
    public sealed record LpResult(
        FeasibleSolverOutput<Flt64> Result,
        IReadOnlyDictionary<MathConstraint, Flt64> DualSolution) {
        /// <summary>目标值 / Objective value</summary>
        public Flt64 Obj => Result.Obj;
        /// <summary>解向量 / Solution vector</summary>
        public IReadOnlyList<Flt64> Solution => Result.Solution.Values;
        /// <summary>求解时间 / Solve time</summary>
        public TimeSpan Time => Result.Time;
        /// <summary>可能的最优目标值 / Possible best objective value</summary>
        public Flt64 PossibleBestObj => Result.PossibleBestObj;
        /// <summary>间隙 / Gap</summary>
        public Flt64 Gap => Result.Gap;
    }
}
