#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Core.Solver;
/// <summary>
/// 线性 Benders 分解求解器接口。
/// Linear Benders decomposition solver interface.
/// </summary>
public interface IBendersDecompositionSolver {
    /// <summary>求解器名称 / Solver name</summary>
    string Name { get; }

    /// <summary>求解线性 Benders 主问题 / Solve linear Benders master problem</summary>
    Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> SolveMasterAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default);

    /// <summary>使用选项求解线性 Benders 主问题 / Solve linear Benders master problem with options</summary>
    Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> SolveMasterAsync(
        LinearMetaModel<Flt64> metaModel,
        FrameworkSolveOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>异步求解线性 Benders 主问题 / Asynchronously solve linear Benders master problem</summary>
    Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> SolveMasterBackgroundAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null)
        => Task.Run(() => SolveMasterAsync(name, metaModel, toLogModel, registrationStatusCallBack, solvingStatusCallBack));

    /// <summary>求解线性 Benders 子问题 / Solve linear Benders sub problem</summary>
    Task<Result<LinearSubResult, ErrorCode, Error<ErrorCode>>> SolveSubAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        IVariableItem objectVariable,
        IReadOnlyDictionary<IVariableItem, Flt64> fixedVariables,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 线性子问题求解结果密封层次根。
    /// Sealed hierarchy root for linear sub problem solving result.
    /// </summary>
    public abstract record LinearSubResult {
        /// <summary>切割（可选）/ Cuts (optional)</summary>
        public List<LinearInequality<Flt64>>? Cuts => (this as ILinearSubResultWithCuts)?.Cuts;
    }

    private interface ILinearSubResultWithCuts {
        List<LinearInequality<Flt64>>? Cuts { get; }
    }

    /// <summary>线性可行子问题结果 / Linear feasible sub problem result</summary>
#pragma warning disable CS8907 // Cuts parameter shadows base LinearSubResult.Cuts by design
    public sealed record LinearFeasibleResult(
        FeasibleSolverOutput<Flt64> Result,
        IReadOnlyDictionary<MathConstraint, Flt64> DualSolution,
        List<LinearInequality<Flt64>>? Cuts)
        : LinearSubResult, ILinearSubResultWithCuts {
        /// <summary>目标值 / Objective value</summary>
        public Flt64 Obj => Result.Obj;
        /// <summary>解向量 / Solution vector</summary>
        public IReadOnlyList<Flt64> Solution => Result.Solution.Values;
        /// <summary>求解时间 / Solve time</summary>
        public System.TimeSpan Time => Result.Time;
        /// <summary>可能的最优目标值 / Possible best objective value</summary>
        public Flt64 PossibleBestObj => Result.PossibleBestObj;
        /// <summary>间隙 / Gap</summary>
        public Flt64 Gap => Result.Gap;
    }
#pragma warning restore CS8907

    /// <summary>线性不可行子问题结果 / Linear infeasible sub problem result</summary>
#pragma warning disable CS8907 // Cuts parameter shadows base LinearSubResult.Cuts by design
    public sealed record LinearInfeasibleResult(
        IReadOnlyDictionary<MathConstraint, Flt64> FarkasDualSolution,
        List<LinearInequality<Flt64>>? Cuts)
        : LinearSubResult, ILinearSubResultWithCuts;
#pragma warning restore CS8907
}

/// <summary>
/// 二次 Benders 分解求解器接口，扩展线性接口。
/// Quadratic Benders decomposition solver interface, extending the linear one.
/// </summary>
public interface IQuadraticBendersDecompositionSolver : IBendersDecompositionSolver {
    // Adds SolveMasterAsync / SolveSubAsync overloads for QuadraticMetaModel<Flt64>
    // and a QuadraticSubResult sealed hierarchy (QuadraticFeasibleResult / QuadraticInfeasibleResult).
}
