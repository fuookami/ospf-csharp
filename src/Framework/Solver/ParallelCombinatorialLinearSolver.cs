#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Config;
using Fuookami.Ospf.Core.Solver.Iis;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Framework.Solver;
/// <summary>
/// 并行组合线性求解器 / Parallel combinatorial linear solver.
/// </summary>
public sealed class ParallelCombinatorialLinearSolver : ILinearSolver {
    private readonly IReadOnlyList<Func<ILinearSolver>> _solverFactories;
    private readonly ParallelCombinatorialMode _mode;
    private readonly ILogger _logger;

    public string Name { get; }
    public SolverConfig Config => _solverFactories[0]().Config;

    public ParallelCombinatorialLinearSolver(
        IEnumerable<ILinearSolver> solvers,
        ParallelCombinatorialMode mode = ParallelCombinatorialMode.Best,
        ILogger? logger = null)
        : this(solvers.Select(s => (Func<ILinearSolver>)(() => s)).ToList(), mode, logger) { }

    public ParallelCombinatorialLinearSolver(
        IReadOnlyList<Func<ILinearSolver>> solverFactories,
        ParallelCombinatorialMode mode = ParallelCombinatorialMode.Best,
        ILogger? logger = null) {
        _solverFactories = solverFactories;
        _mode = mode;
        _logger = logger ?? NullLogger.Instance;
        Name = $"ParallelCombinatorialLinear({string.Join(",", _solverFactories.Select(f => f().Name))})";
    }

    public async Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        ILinearTriadModelView model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) {
        var tasks = _solverFactories
            .Select(f => SafeInvokeAsync(() => f().InvokeAsync(model, solvingStatusCallBack, cancellationToken)))
            .ToList();

        return _mode == ParallelCombinatorialMode.First
            ? await FirstSuccessAsync(tasks)
            : await BestSuccessAsync(tasks);
    }

    public async Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        LinearTriadModel model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        IisConfig? iisConfig = null,
        CancellationToken cancellationToken = default) {
        var tasks = _solverFactories
            .Select(f => SafeInvokeSolverOutputAsync(() => f().InvokeAsync(model, solvingStatusCallBack, iisConfig, cancellationToken)))
            .ToList();

        return _mode == ParallelCombinatorialMode.First
            ? await FirstSuccessAsync(tasks)
            : await BestSuccessAsync(tasks);
    }

    public Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions), ErrorCode, Error<ErrorCode>>> InvokeAsync(
        ILinearTriadModelView model,
        ulong solutionAmount,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default)
        => _solverFactories[0]().InvokeAsync(model, solutionAmount, solvingStatusCallBack, cancellationToken);

    public Task<LinearTriadModel> DumpAsync(
        LinearMechanismModel<Flt64> model,
        CancellationToken cancellationToken = default)
        => _solverFactories[0]().DumpAsync(model, cancellationToken);

    public Task<Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>> DumpAsync(
        LinearMetaModel<Flt64> model,
        RegistrationStatusCallBack? registrationStatusCallBack,
        MechanismModelDumpingStatusCallBack? dumpingStatusCallBack,
        CancellationToken cancellationToken = default)
        => _solverFactories[0]().DumpAsync(model, registrationStatusCallBack, dumpingStatusCallBack, cancellationToken);

    private static async Task<Result<T, ErrorCode, Error<ErrorCode>>> FirstSuccessAsync<T>(IEnumerable<Task<Result<T, ErrorCode, Error<ErrorCode>>>> tasks) where T : class {
        using var cts = new CancellationTokenSource();
        var taskList = tasks.ToList();
        var pending = new List<Task<Result<T, ErrorCode, Error<ErrorCode>>>>(taskList);
        try {
            while (pending.Count > 0) {
                Task<Result<T, ErrorCode, Error<ErrorCode>>> completed = await Task.WhenAny(pending);
                pending.Remove(completed);
                Result<T, ErrorCode, Error<ErrorCode>> result = await completed;
                if (result is Ok<T, ErrorCode, Error<ErrorCode>>) { cts.Cancel(); return result; }
            }
        }
        catch (OperationCanceledException) { }
        return new Failed<T, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.SolverNotFound, "No solver succeeded"));
    }

    private static async Task<Result<T, ErrorCode, Error<ErrorCode>>> BestSuccessAsync<T>(IEnumerable<Task<Result<T, ErrorCode, Error<ErrorCode>>>> tasks) where T : class {
        Result<T, ErrorCode, Error<ErrorCode>>[] results = await Task.WhenAll(tasks);
        var success = results.OfType<Ok<T, ErrorCode, Error<ErrorCode>>>().Select(o => o.Value).ToList();
        if (success.Count == 0) {
            return new Failed<T, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.SolverNotFound, "No solver succeeded"));
        }

        return new Ok<T, ErrorCode, Error<ErrorCode>>(success[0]);
    }

    private async Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> SafeInvokeAsync(Func<Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>>> solve) {
        try { return await solve(); }
        catch (Exception e) { _logger.LogError(e, "parallel linear solver threw"); return new Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>(ErrorCode.OREngineSolvingException); }
    }

    private async Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> SafeInvokeSolverOutputAsync(Func<Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>>> solve) {
        try { return await solve(); }
        catch (Exception e) { _logger.LogError(e, "parallel linear solver threw"); return new Failed<SolverOutput, ErrorCode, Error<ErrorCode>>(ErrorCode.OREngineSolvingException); }
    }
}

/// <summary>
/// 串行组合线性求解器 / Serial combinatorial linear solver.
/// </summary>
public sealed class SerialCombinatorialLinearSolver : ILinearSolver {
    private readonly IReadOnlyList<Func<ILinearSolver>> _solverFactories;
    private readonly ILogger _logger;

    public string Name { get; }
    public SolverConfig Config => _solverFactories[0]().Config;

    public SerialCombinatorialLinearSolver(
        IEnumerable<ILinearSolver> solvers, ILogger? logger = null)
        : this(solvers.Select(s => (Func<ILinearSolver>)(() => s)).ToList(), logger) { }

    public SerialCombinatorialLinearSolver(
        IReadOnlyList<Func<ILinearSolver>> solverFactories, ILogger? logger = null) {
        _solverFactories = solverFactories;
        _logger = logger ?? NullLogger.Instance;
        Name = $"SerialCombinatorialLinear({string.Join(",", _solverFactories.Select(f => f().Name))})";
    }

    public async Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        ILinearTriadModelView model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) {
        foreach (Func<ILinearSolver> factory in _solverFactories) {
            try {
                Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> result = await factory().InvokeAsync(model, solvingStatusCallBack, cancellationToken);
                if (result is Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>) {
                    return result;
                }
            }
            catch (Exception e) { _logger.LogError(e, "serial linear solver threw"); }
        }
        return new Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.SolverNotFound, "No solver succeeded"));
    }

    public async Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        LinearTriadModel model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        IisConfig? iisConfig = null,
        CancellationToken cancellationToken = default) {
        foreach (Func<ILinearSolver> factory in _solverFactories) {
            try {
                Result<SolverOutput, ErrorCode, Error<ErrorCode>> result = await factory().InvokeAsync(model, solvingStatusCallBack, iisConfig, cancellationToken);
                if (result is Ok<SolverOutput, ErrorCode, Error<ErrorCode>>) {
                    return result;
                }
            }
            catch (Exception e) { _logger.LogError(e, "serial linear solver threw"); }
        }
        return new Failed<SolverOutput, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.SolverNotFound, "No solver succeeded"));
    }

    public Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions), ErrorCode, Error<ErrorCode>>> InvokeAsync(
        ILinearTriadModelView model, ulong solutionAmount,
        SolvingStatusCallBack? solvingStatusCallBack = null, CancellationToken cancellationToken = default)
        => _solverFactories[0]().InvokeAsync(model, solutionAmount, solvingStatusCallBack, cancellationToken);

    public Task<LinearTriadModel> DumpAsync(LinearMechanismModel<Flt64> model, CancellationToken cancellationToken = default)
        => _solverFactories[0]().DumpAsync(model, cancellationToken);

    public Task<Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>> DumpAsync(
        LinearMetaModel<Flt64> model, RegistrationStatusCallBack? registrationStatusCallBack,
        MechanismModelDumpingStatusCallBack? dumpingStatusCallBack, CancellationToken cancellationToken = default)
        => _solverFactories[0]().DumpAsync(model, registrationStatusCallBack, dumpingStatusCallBack, cancellationToken);
}
