#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Framework.Solver;
/// <summary>
/// 线性 Benders 分解求解器接口 / Linear Benders decomposition solver interface.
/// </summary>
public interface IBendersDecompositionSolver {
    /// <summary>求解器名称 / Solver name.</summary>
    string Name { get; }

    /// <summary>求解线性 Benders 主问题 / Solve linear Benders master problem.</summary>
    Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> SolveMasterAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null);

    /// <summary>使用选项求解线性 Benders 主问题 / Solve linear Benders master problem with options.</summary>
    Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> SolveMasterAsync(
        LinearMetaModel<Flt64> metaModel,
        FrameworkSolveOptions options)
        => SolveMasterAsync(
            options.SolveName(metaModel.Name),
            metaModel,
            options.ToLogModel,
            options.RegistrationStatusCallBack,
            options.SolvingStatusCallBack);

    /// <summary>使用值转换器求解线性 Benders 主问题 / Solve linear Benders master with value converter.</summary>
    Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> SolveMasterAsAsync<V>(
        string name,
        LinearMetaModel<Flt64> metaModel,
        IIntoValue<V> converter,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null)
        where V : struct, IRealNumber<V>, INumberField<V>;

    /// <summary>求解线性 Benders 子问题 / Solve linear Benders sub problem.</summary>
    Task<Result<LinearSubResult, ErrorCode, Error<ErrorCode>>> SolveSubAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        IVariableItem objectVariable,
        IReadOnlyDictionary<IVariableItem, Flt64> fixedVariables,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null);

    /// <summary>使用选项求解线性 Benders 子问题 / Solve linear Benders sub problem with options.</summary>
    Task<Result<LinearSubResult, ErrorCode, Error<ErrorCode>>> SolveSubAsync(
        LinearMetaModel<Flt64> metaModel,
        IVariableItem objectVariable,
        IReadOnlyDictionary<IVariableItem, Flt64> fixedVariables,
        FrameworkSolveOptions options)
        => SolveSubAsync(
            options.SolveName(metaModel.Name),
            metaModel,
            objectVariable,
            fixedVariables,
            options.ToLogModel,
            options.RegistrationStatusCallBack,
            options.SolvingStatusCallBack);

    /// <summary>使用值转换器求解线性 Benders 子问题 / Solve linear Benders sub with value converter.</summary>
    Task<Result<LinearSubResultOf<V>, ErrorCode, Error<ErrorCode>>> SolveSubAsAsync<V>(
        LinearMetaModel<Flt64> metaModel,
        IVariableItem objectVariable,
        IReadOnlyDictionary<IVariableItem, Flt64> fixedVariables,
        FrameworkSolveOptions options)
        where V : struct, IRealNumber<V>, INumberField<V>;

    /// <summary>线性子问题求解结果 / Sealed linear sub-result.</summary>
    abstract record LinearSubResult(IReadOnlyList<LinearInequality<Flt64>>? Cuts);

    /// <summary>线性可行子问题结果 / Linear feasible sub-result.</summary>
    sealed record LinearFeasibleResult(
        FeasibleSolverOutput<Flt64> Result,
        IReadOnlyDictionary<MathConstraint, Flt64> DualSolution,
        IReadOnlyList<LinearInequality<Flt64>>? Cuts) : LinearSubResult(Cuts) {
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

    /// <summary>线性不可行子问题结果 / Linear infeasible sub-result (Farkas dual).</summary>
    sealed record LinearInfeasibleResult(
        IReadOnlyDictionary<MathConstraint, Flt64> FarkasDualSolution,
        IReadOnlyList<LinearInequality<Flt64>>? Cuts) : LinearSubResult(Cuts);

    /// <summary>带值转换的线性子问题结果 / Linear sub-result with value conversion.</summary>
    abstract record LinearSubResultOf<V>(IReadOnlyList<LinearInequality<Flt64>>? Cuts)
        where V : struct, IRealNumber<V>, INumberField<V>;

    /// <summary>带值转换的线性可行子问题结果 / Linear feasible sub-result with value conversion.</summary>
    sealed record LinearFeasibleResultOf<V>(
        FeasibleSolverOutput<V> Result,
        IReadOnlyDictionary<MathConstraint, Flt64> DualSolution,
        IReadOnlyList<LinearInequality<Flt64>>? Cuts) : LinearSubResultOf<V>(Cuts)
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

    /// <summary>带值转换的线性不可行子问题结果 / Linear infeasible sub-result with value conversion (Farkas dual).</summary>
    sealed record LinearInfeasibleResultOf<V>(
        IReadOnlyDictionary<MathConstraint, Flt64> FarkasDualSolution,
        IReadOnlyList<LinearInequality<Flt64>>? Cuts) : LinearSubResultOf<V>(Cuts)
        where V : struct, IRealNumber<V>, INumberField<V>;
}

/// <summary>
/// 二次 Benders 分解求解器接口 / Quadratic Benders decomposition solver interface.
/// </summary>
public interface IQuadraticBendersDecompositionSolver : IBendersDecompositionSolver {
    /// <summary>求解二次 Benders 主问题 / Solve quadratic Benders master problem.</summary>
    Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> SolveMasterAsync(
        string name,
        QuadraticMetaModel<Flt64> metaModel,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null);

    /// <summary>使用选项求解二次 Benders 主问题 / Solve quadratic Benders master with options.</summary>
    Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> SolveMasterAsync(
        QuadraticMetaModel<Flt64> metaModel,
        FrameworkSolveOptions options)
        => SolveMasterAsync(
            options.SolveName(metaModel.Name),
            metaModel,
            options.ToLogModel,
            options.RegistrationStatusCallBack,
            options.SolvingStatusCallBack);
}
