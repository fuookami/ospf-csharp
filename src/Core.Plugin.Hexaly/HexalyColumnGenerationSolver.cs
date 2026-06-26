#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Config;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Core.Plugin.Hexaly;

/// <summary>
/// Hexaly 列生成求解器 / Hexaly column generation solver.
/// </summary>
public sealed class HexalyColumnGenerationSolver : IColumnGenerationSolver {
    /// <summary>求解器名称 / Solver name.</summary>
    public string Name => "hexaly";

    /// <summary>求解器配置 / Solver configuration.</summary>
    public SolverConfig Config { get; }

    private readonly HexalySolverCallBack _callBack;

    public HexalyColumnGenerationSolver(
        SolverConfig? config = null,
        HexalySolverCallBack? callBack = null) {
        Config = config ?? new HexalySolverConfig();
        _callBack = callBack ?? new HexalySolverCallBack();
    }

    /// <summary>
    /// 求解 MILP 问题 / Solve MILP problem.
    /// </summary>
    public async Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> SolveMILPAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) {
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> mechanismResult =
            await LinearMechanismModel<Flt64>.InvokeAsync(
                metaModel,
                concurrent: Config.DumpMechanismModelConcurrent,
                blocking: Config.DumpMechanismModelBlocking,
                registrationStatusCallBack: registrationStatusCallBack);

        if (mechanismResult is Failed<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> f) {
            return Results.Failed<FeasibleSolverOutput<Flt64>>(f.Error);
        }
        if (mechanismResult is Fatal<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> fat) {
            return new Fatal<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>(fat.Errors);
        }

        var mechOk = (Ok<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>)mechanismResult;
        using LinearMechanismModel<Flt64> mechanismModel = mechOk.Value;
        var linearSolver = new HexalyLinearSolver(Config, _callBack.Copy());
        LinearTriadModel model = await linearSolver.DumpAsync(mechanismModel, cancellationToken);

        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solverResult =
            await linearSolver.InvokeAsync(model, solvingStatusCallBack, cancellationToken);

        if (solverResult is Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> ok) {
            metaModel.Tokens.SetSolution(ok.Value.Solution.Values);
            return Results.Ok(ok.Value);
        }
        if (solverResult is Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> sf) {
            return Results.Failed<FeasibleSolverOutput<Flt64>>(sf.Error);
        }
        if (solverResult is Fatal<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> sfa) {
            return new Fatal<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>(sfa.Errors);
        }
        throw new InvalidOperationException();
    }

    /// <summary>
    /// 使用选项求解 MILP 问题 / Solve MILP problem with options.
    /// </summary>
    public async Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> SolveMILPAsync(
        LinearMetaModel<Flt64> metaModel,
        FrameworkSolveOptions options,
        CancellationToken cancellationToken = default) {
        return await SolveMILPAsync(
            Name, metaModel,
            toLogModel: options.LogModel,
            registrationStatusCallBack: null,
            solvingStatusCallBack: options.SolveOptions?.SolvingStatusCallBack,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// 异步求解 MILP 问题 / Asynchronously solve MILP problem.
    /// </summary>
    public Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> SolveMILPBackgroundAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null) =>
        Task.Run(() => SolveMILPAsync(name, metaModel, toLogModel, registrationStatusCallBack, solvingStatusCallBack));

    /// <summary>
    /// 求解 MILP 问题并返回指定数量的解 / Solve MILP and return a solution pool.
    /// </summary>
    public async Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions), ErrorCode, Error<ErrorCode>>> SolveMILPAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        ulong amount,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) {
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await SolveMILPAsync(name, metaModel, toLogModel, registrationStatusCallBack, solvingStatusCallBack, cancellationToken);

        if (result is Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> ok) {
            return Results.Ok((ok.Value, new List<List<Flt64>> { new List<Flt64>(ok.Value.Solution.Values) }));
        }
        if (result is Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> f) {
            return Results.Failed<(FeasibleSolverOutput<Flt64>, List<List<Flt64>>)>(f.Error);
        }
        if (result is Fatal<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> fat) {
            return new Fatal<(FeasibleSolverOutput<Flt64>, List<List<Flt64>>), ErrorCode, Error<ErrorCode>>(fat.Errors);
        }
        throw new InvalidOperationException();
    }

    /// <summary>
    /// 使用选项求解 MILP 并返回解池 / Solve MILP with options and return solution pool.
    /// </summary>
    public async Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions), ErrorCode, Error<ErrorCode>>> SolveMILPWithSolutionPoolAsync(
        LinearMetaModel<Flt64> metaModel,
        FrameworkSolveOptions options,
        CancellationToken cancellationToken = default) {
        return await SolveMILPAsync(
            Name, metaModel, options.SolveOptions?.SolutionAmount ?? 1,
            toLogModel: options.LogModel,
            registrationStatusCallBack: null,
            solvingStatusCallBack: options.SolveOptions?.SolvingStatusCallBack,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// 求解 LP 问题 / Solve LP problem.
    /// </summary>
    public async Task<Result<IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>>> SolveLPAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) {
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> mechanismResult =
            await LinearMechanismModel<Flt64>.InvokeAsync(
                metaModel,
                concurrent: Config.DumpMechanismModelConcurrent,
                blocking: Config.DumpMechanismModelBlocking,
                registrationStatusCallBack: registrationStatusCallBack);

        if (mechanismResult is Failed<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> f) {
            return Results.Failed<IColumnGenerationSolver.LpResult>(f.Error);
        }
        if (mechanismResult is Fatal<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> fat) {
            return new Fatal<IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>>(fat.Errors);
        }

        var mechOk = (Ok<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>)mechanismResult;
        using LinearMechanismModel<Flt64> mechanismModel = mechOk.Value;
        var linearSolver = new HexalyLinearSolver(Config, _callBack.Copy());
        LinearTriadModel model = await linearSolver.DumpAsync(mechanismModel, cancellationToken);

        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solverResult =
            await linearSolver.InvokeAsync(model, solvingStatusCallBack, cancellationToken);

        if (solverResult is Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> ok) {
            metaModel.Tokens.SetSolution(ok.Value.Solution.Values);
            IReadOnlyDictionary<MathConstraint, Flt64> dualSolution = new Dictionary<MathConstraint, Flt64>();
            return Results.Ok(new IColumnGenerationSolver.LpResult(ok.Value, dualSolution));
        }
        if (solverResult is Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> sf) {
            return Results.Failed<IColumnGenerationSolver.LpResult>(sf.Error);
        }
        if (solverResult is Fatal<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> sfa) {
            return new Fatal<IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>>(sfa.Errors);
        }
        throw new InvalidOperationException();
    }
}
