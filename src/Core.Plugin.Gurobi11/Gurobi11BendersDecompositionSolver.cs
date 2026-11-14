#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Config;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using GurobiCallback = Fuookami.Ospf.Core.Plugin.Gurobi.GurobiLinearSolverCallBack;
using GurobiIdentity = Fuookami.Ospf.Core.Plugin.Gurobi.GurobiBackendIdentity;
using GurobiSolver = Fuookami.Ospf.Core.Plugin.Gurobi.GurobiBendersDecompositionSolver;

namespace Fuookami.Ospf.Core.Plugin.Gurobi11;

/// <summary>
/// Gurobi 11 线性 Benders 求解器兼容门面。
/// Gurobi 11 linear Benders solver compatibility facade.
/// </summary>
public sealed class Gurobi11BendersDecompositionSolver : Framework.Solver.IBendersDecompositionSolver {
    private readonly GurobiSolver _solver;

    /// <summary>求解器名称 / Solver name.</summary>
    public string Name => "gurobi11";

    /// <summary>求解器配置 / Solver configuration.</summary>
    public SolverConfig Config => _solver.Config;

    /// <summary>创建 Gurobi 11 Benders 求解器 / Create a Gurobi 11 Benders solver.</summary>
    public Gurobi11BendersDecompositionSolver(
        SolverConfig? config = null,
        GurobiCallback? callBack = null) {
        _solver = new GurobiSolver(config, callBack, GurobiIdentity.Gurobi11);
    }

    private Gurobi11BendersDecompositionSolver(GurobiSolver solver) {
        _solver = solver;
    }

    /// <inheritdoc/>
    public Framework.Solver.IBendersDecompositionSolver WithTimeLimit(TimeSpan timeLimit) =>
        new Gurobi11BendersDecompositionSolver(_solver.WithTimeLimitCore(timeLimit));

    /// <inheritdoc/>
    public Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> SolveMasterAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null) =>
        _solver.SolveMasterAsync(
            name,
            metaModel,
            toLogModel,
            registrationStatusCallBack,
            solvingStatusCallBack);

    /// <inheritdoc/>
    public Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> SolveMasterReportAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null) =>
        _solver.SolveMasterReportAsync(
            name,
            metaModel,
            toLogModel,
            registrationStatusCallBack,
            solvingStatusCallBack);

    /// <inheritdoc/>
    public Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> SolveMasterAsAsync<V>(
        string name,
        LinearMetaModel<Flt64> metaModel,
        IIntoValue<V> converter,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        _solver.SolveMasterAsAsync(
            name,
            metaModel,
            converter,
            toLogModel,
            registrationStatusCallBack,
            solvingStatusCallBack);

    /// <inheritdoc/>
    public Task<Result<Framework.Solver.IBendersDecompositionSolver.LinearSubResult, ErrorCode, Error<ErrorCode>>> SolveSubAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        IVariableItem objectVariable,
        IReadOnlyDictionary<IVariableItem, Flt64> fixedVariables,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null) =>
        _solver.SolveSubAsync(
            name,
            metaModel,
            objectVariable,
            fixedVariables,
            toLogModel,
            registrationStatusCallBack,
            solvingStatusCallBack);

    /// <inheritdoc/>
    public Task<Result<Framework.Solver.IBendersDecompositionSolver.LinearSubResultOf<V>, ErrorCode, Error<ErrorCode>>> SolveSubAsAsync<V>(
        LinearMetaModel<Flt64> metaModel,
        IVariableItem objectVariable,
        IReadOnlyDictionary<IVariableItem, Flt64> fixedVariables,
        Framework.Solver.FrameworkSolveOptions options)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        _solver.SolveSubAsAsync<V>(
            metaModel,
            objectVariable,
            fixedVariables,
            options);
}
