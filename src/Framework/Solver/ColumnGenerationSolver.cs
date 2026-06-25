#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Framework.Solver;
/// <summary>
/// 列生成求解器接口 / Column generation solver interface.
/// </summary>
public interface IColumnGenerationSolver {
    /// <summary>求解器名称 / Solver name.</summary>
    string Name { get; }

    /// <summary>
    /// 求解 MILP 问题 / Solve MILP problem.
    /// </summary>
    Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> SolveMILPAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null);

    /// <summary>
    /// 使用选项求解 MILP 问题 / Solve MILP problem with options.
    /// </summary>
    async Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> SolveMILPAsync(
        LinearMetaModel<Flt64> metaModel,
        FrameworkSolveOptions options) {
        ulong? solutionAmount = options.SolutionAmount;
        if (solutionAmount is null) {
            return await SolveMILPAsync(
                options.SolveName(metaModel.Name),
                metaModel,
                options.ToLogModel,
                options.RegistrationStatusCallBack,
                options.SolvingStatusCallBack);
        }
        Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> SolutionPool), ErrorCode, Error<ErrorCode>> poolResult = await SolveMILPAsync(
            options.SolveName(metaModel.Name),
            metaModel,
            solutionAmount.Value,
            options.ToLogModel,
            options.RegistrationStatusCallBack,
            options.SolvingStatusCallBack);
        return poolResult.Map(r => r.Output);
    }

    /// <summary>
    /// 求解 MILP 问题并返回解池 / Solve MILP problem and return a solution pool.
    /// </summary>
    Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> SolutionPool), ErrorCode, Error<ErrorCode>>> SolveMILPAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        ulong amount,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null);

    /// <summary>
    /// 使用值转换器求解 MILP 问题 / Solve MILP problem with value converter.
    /// </summary>
    Task<Result<FeasibleSolverOutput<V>, ErrorCode, Error<ErrorCode>>> SolveMILPAsAsync<V>(
        string name,
        LinearMetaModel<Flt64> metaModel,
        IIntoValue<V> converter,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null)
        where V : struct, IRealNumber<V>, INumberField<V>;

    /// <summary>
    /// 求解 LP 问题 / Solve LP problem.
    /// </summary>
    Task<Result<LpResult, ErrorCode, Error<ErrorCode>>> SolveLPAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null);

    /// <summary>
    /// 使用值转换器求解 LP 问题 / Solve LP problem with value converter.
    /// </summary>
    Task<Result<LpResultOf<V>, ErrorCode, Error<ErrorCode>>> SolveLPAsAsync<V>(
        string name,
        LinearMetaModel<Flt64> metaModel,
        IIntoValue<V> converter,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null)
        where V : struct, IRealNumber<V>, INumberField<V>;

    /// <summary>
    /// LP 求解结果 / LP solve result.
    /// </summary>
    sealed record LpResult(
        FeasibleSolverOutput<Flt64> Result,
        IReadOnlyDictionary<MathConstraint, Flt64> DualSolution) {
        /// <summary>目标值 / Objective value</summary>
        public Flt64 Obj => Result.Obj;
        /// <summary>解向量 / Solution vector</summary>
        public IReadOnlyList<Flt64> Solution => Result.Solution.Values;
        /// <summary>求解时间 / Solve time</summary>
        public TimeSpan Time => Result.Time;
        /// <summary>可能的最优目标值 / Possible best objective</summary>
        public Flt64? PossibleBestObj => Result.PossibleBestObj;
        /// <summary>间隙 / Gap</summary>
        public Flt64 Gap => Result.Gap;
    }

    /// <summary>
    /// 带值转换的 LP 求解结果 / LP solve result with value conversion.
    /// </summary>
    sealed record LpResultOf<V>(
        FeasibleSolverOutput<V> Result,
        IReadOnlyDictionary<MathConstraint, Flt64> DualSolution)
        where V : struct, IRealNumber<V>, INumberField<V> {
        /// <summary>目标值 / Objective value</summary>
        public Flt64 Obj => Result.Obj;
        /// <summary>解向量 / Solution vector</summary>
        public IReadOnlyList<V> Solution => Result.Solution.Values;
        /// <summary>求解时间 / Solve time</summary>
        public TimeSpan Time => Result.Time;
        /// <summary>可能的最优目标值 / Possible best objective</summary>
        public Flt64? PossibleBestObj => Result.PossibleBestObj;
        /// <summary>间隙 / Gap</summary>
        public Flt64 Gap => Result.Gap;
    }
}
