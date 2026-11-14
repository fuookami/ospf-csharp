#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Config;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System.Collections.Generic;
using System.Threading.Tasks;

using GurobiCallback = Fuookami.Ospf.Core.Plugin.Gurobi.GurobiLinearSolverCallBack;
using GurobiIdentity = Fuookami.Ospf.Core.Plugin.Gurobi.GurobiBackendIdentity;
using GurobiSolver = Fuookami.Ospf.Core.Plugin.Gurobi.GurobiColumnGenerationSolver;

namespace Fuookami.Ospf.Core.Plugin.Gurobi11;

/// <summary>
/// Gurobi 11 列生成求解器兼容门面。
/// Gurobi 11 column-generation solver compatibility facade.
/// </summary>
public sealed class Gurobi11ColumnGenerationSolver : Framework.Solver.IColumnGenerationSolver {
    private readonly GurobiSolver _solver;

    /// <summary>求解器名称 / Solver name.</summary>
    public string Name => "gurobi11";

    /// <summary>求解器配置 / Solver configuration.</summary>
    public SolverConfig Config => _solver.Config;

    /// <summary>创建 Gurobi 11 列生成求解器 / Create a Gurobi 11 column-generation solver.</summary>
    public Gurobi11ColumnGenerationSolver(
        SolverConfig? config = null,
        GurobiCallback? callBack = null) {
        _solver = new GurobiSolver(config, callBack, GurobiIdentity.Gurobi11);
    }

    /// <inheritdoc/>
    public Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> SolveMILPAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null) =>
        _solver.SolveMILPAsync(
            name,
            metaModel,
            toLogModel,
            registrationStatusCallBack,
            solvingStatusCallBack);

    /// <inheritdoc/>
    public Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> SolveMILPAsync(
        LinearMetaModel<Flt64> metaModel,
        Framework.Solver.FrameworkSolveOptions options) =>
        ((Framework.Solver.IColumnGenerationSolver)_solver).SolveMILPAsync(metaModel, options);

    /// <inheritdoc/>
    public Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> SolutionPool), ErrorCode, Error<ErrorCode>>> SolveMILPAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        ulong amount,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null) =>
        _solver.SolveMILPAsync(
            name,
            metaModel,
            amount,
            toLogModel,
            registrationStatusCallBack,
            solvingStatusCallBack);

    /// <inheritdoc/>
    public Task<Result<FeasibleSolverOutput<V>, ErrorCode, Error<ErrorCode>>> SolveMILPAsAsync<V>(
        string name,
        LinearMetaModel<Flt64> metaModel,
        IIntoValue<V> converter,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        _solver.SolveMILPAsAsync(
            name,
            metaModel,
            converter,
            toLogModel,
            registrationStatusCallBack,
            solvingStatusCallBack);

    /// <inheritdoc/>
    public Task<Result<Framework.Solver.IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>>> SolveLPAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null) =>
        _solver.SolveLPAsync(
            name,
            metaModel,
            toLogModel,
            registrationStatusCallBack,
            solvingStatusCallBack);

    /// <inheritdoc/>
    public Task<Result<Framework.Solver.IColumnGenerationSolver.LpResultOf<V>, ErrorCode, Error<ErrorCode>>> SolveLPAsAsync<V>(
        string name,
        LinearMetaModel<Flt64> metaModel,
        IIntoValue<V> converter,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        _solver.SolveLPAsAsync(
            name,
            metaModel,
            converter,
            toLogModel,
            registrationStatusCallBack,
            solvingStatusCallBack);
}
