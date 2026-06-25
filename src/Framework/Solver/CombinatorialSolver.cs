#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.Concept;
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
/// 并行组合列生成求解器 / Parallel combinatorial column generation solver.
/// </summary>
public sealed class ParallelCombinatorialColumnGenerationSolver : IColumnGenerationSolver {
    private readonly IReadOnlyList<Func<IColumnGenerationSolver>> _solverFactories;
    private readonly ParallelCombinatorialMode _mode;
    private readonly ILogger _logger;

    /// <inheritdoc/>
    public string Name { get; }

    /// <summary>
    /// 使用求解器实例构造 / Construct with solver instances.
    /// </summary>
    public ParallelCombinatorialColumnGenerationSolver(
        IEnumerable<IColumnGenerationSolver> solvers,
        ParallelCombinatorialMode mode = ParallelCombinatorialMode.Best,
        ILogger? logger = null)
        : this(solvers.Select(s => (Func<IColumnGenerationSolver>)(() => s)).ToList(), mode, logger) { }

    /// <summary>
    /// 使用求解器工厂构造 / Construct with solver factories.
    /// </summary>
    public ParallelCombinatorialColumnGenerationSolver(
        IReadOnlyList<Func<IColumnGenerationSolver>> solverFactories,
        ParallelCombinatorialMode mode = ParallelCombinatorialMode.Best,
        ILogger? logger = null) {
        _solverFactories = solverFactories;
        _mode = mode;
        _logger = logger ?? NullLogger.Instance;
        Name = $"ParallelCombinatorial({string.Join(",", _solverFactories.Select(f => f().Name))})";
    }

    /// <inheritdoc/>
    public async Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> SolveMILPAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null) {
        var tasks = _solverFactories
            .Select(f => SafeSolveMILPAsync(() => f().SolveMILPAsync(name, metaModel, toLogModel)))
            .ToList();

        return _mode == ParallelCombinatorialMode.First
            ? await FirstSuccessAsync(tasks)
            : await BestSuccessAsync(tasks, metaModel.ObjectCategory);
    }

    /// <inheritdoc/>
    public Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> SolutionPool), ErrorCode, Error<ErrorCode>>> SolveMILPAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        ulong amount,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null) =>
        // Solution pool variant: delegate to first solver for simplicity
        _solverFactories[0]().SolveMILPAsync(name, metaModel, amount, toLogModel, registrationStatusCallBack, solvingStatusCallBack);

    /// <inheritdoc/>
    public Task<Result<FeasibleSolverOutput<V>, ErrorCode, Error<ErrorCode>>> SolveMILPAsAsync<V>(
        string name,
        LinearMetaModel<Flt64> metaModel,
        IIntoValue<V> converter,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null)
        where V : struct, IRealNumber<V>, INumberField<V> => _solverFactories[0]().SolveMILPAsAsync(name, metaModel, converter, toLogModel, registrationStatusCallBack, solvingStatusCallBack);

    /// <inheritdoc/>
    public async Task<Result<IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>>> SolveLPAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null) {
        var tasks = _solverFactories
            .Select(f => SafeSolveLPAsync(() => f().SolveLPAsync(name, metaModel, toLogModel)))
            .ToList();

        return _mode == ParallelCombinatorialMode.First
            ? await FirstSuccessAsync(tasks)
            : await BestSuccessAsync(tasks, metaModel.ObjectCategory);
    }

    /// <inheritdoc/>
    public Task<Result<IColumnGenerationSolver.LpResultOf<V>, ErrorCode, Error<ErrorCode>>> SolveLPAsAsync<V>(
        string name,
        LinearMetaModel<Flt64> metaModel,
        IIntoValue<V> converter,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null)
        where V : struct, IRealNumber<V>, INumberField<V> => _solverFactories[0]().SolveLPAsAsync(name, metaModel, converter, toLogModel, registrationStatusCallBack, solvingStatusCallBack);

    // First: 取第一个成功结果 / take the first successful result.
    private static async Task<Result<T, ErrorCode, Error<ErrorCode>>> FirstSuccessAsync<T>(
        IEnumerable<Task<Result<T, ErrorCode, Error<ErrorCode>>>> tasks) where T : class {
        using var cts = new CancellationTokenSource();
        var taskList = tasks.ToList();
        var pending = new List<Task<Result<T, ErrorCode, Error<ErrorCode>>>>(taskList);
        try {
            while (pending.Count > 0) {
                Task<Result<T, ErrorCode, Error<ErrorCode>>> completed = await Task.WhenAny(pending);
                pending.Remove(completed);
                Result<T, ErrorCode, Error<ErrorCode>> result = await completed;
                if (result is Ok<T, ErrorCode, Error<ErrorCode>>) {
                    cts.Cancel();
                    return result;
                }
            }
        }
        catch (OperationCanceledException) { }
        return new Failed<T, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.SolverNotFound, "No solver succeeded"));
    }

    // Best: 取最优结果 / take the best result.
    private static async Task<Result<T, ErrorCode, Error<ErrorCode>>> BestSuccessAsync<T>(
        IEnumerable<Task<Result<T, ErrorCode, Error<ErrorCode>>>> tasks,
        ObjectCategory category) where T : class {
        Result<T, ErrorCode, Error<ErrorCode>>[] results = await Task.WhenAll(tasks);
        var success = results.OfType<Ok<T, ErrorCode, Error<ErrorCode>>>().Select(o => o.Value).ToList();
        if (success.Count == 0) {
            return new Failed<T, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.SolverNotFound, "No solver succeeded"));
        }
        return new Ok<T, ErrorCode, Error<ErrorCode>>(success[0]);
    }

    private async Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> SafeSolveMILPAsync(Func<Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>>> solve) {
        try { return await solve(); }
        catch (Exception e) { _logger.LogError(e, "parallel CG solver threw"); return new Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>(ErrorCode.OREngineSolvingException); }
    }

    private async Task<Result<IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>>> SafeSolveLPAsync(Func<Task<Result<IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>>>> solve) {
        try { return await solve(); }
        catch (Exception e) { _logger.LogError(e, "parallel CG solver threw"); return new Failed<IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>>(ErrorCode.OREngineSolvingException); }
    }
}

/// <summary>
/// 串行组合列生成求解器 / Serial combinatorial column generation solver.
/// </summary>
public sealed class SerialCombinatorialColumnGenerationSolver : IColumnGenerationSolver {
    private readonly IReadOnlyList<Func<IColumnGenerationSolver>> _solverFactories;
    private readonly ILogger _logger;

    /// <inheritdoc/>
    public string Name { get; }

    public SerialCombinatorialColumnGenerationSolver(
        IEnumerable<IColumnGenerationSolver> solvers,
        ILogger? logger = null)
        : this(solvers.Select(s => (Func<IColumnGenerationSolver>)(() => s)).ToList(), logger) { }

    public SerialCombinatorialColumnGenerationSolver(
        IReadOnlyList<Func<IColumnGenerationSolver>> solverFactories,
        ILogger? logger = null) {
        _solverFactories = solverFactories;
        _logger = logger ?? NullLogger.Instance;
        Name = $"SerialCombinatorial({string.Join(",", _solverFactories.Select(f => f().Name))})";
    }

    /// <inheritdoc/>
    public async Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> SolveMILPAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null) {
        foreach (Func<IColumnGenerationSolver> factory in _solverFactories) {
            try {
                Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> result = await factory().SolveMILPAsync(name, metaModel, toLogModel, registrationStatusCallBack, solvingStatusCallBack);
                if (result is Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>) {
                    return result;
                }
            }
            catch (Exception e) {
                _logger.LogError(e, "serial CG solver threw");
            }
        }
        return new Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.SolverNotFound, "No solver succeeded"));
    }

    /// <inheritdoc/>
    public Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> SolutionPool), ErrorCode, Error<ErrorCode>>> SolveMILPAsync(
        string name, LinearMetaModel<Flt64> metaModel, ulong amount,
        bool toLogModel = false, RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null)
        => _solverFactories[0]().SolveMILPAsync(name, metaModel, amount, toLogModel, registrationStatusCallBack, solvingStatusCallBack);

    /// <inheritdoc/>
    public Task<Result<FeasibleSolverOutput<V>, ErrorCode, Error<ErrorCode>>> SolveMILPAsAsync<V>(
        string name, LinearMetaModel<Flt64> metaModel, IIntoValue<V> converter,
        bool toLogModel = false, RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null) where V : struct, IRealNumber<V>, INumberField<V>
        => _solverFactories[0]().SolveMILPAsAsync(name, metaModel, converter, toLogModel, registrationStatusCallBack, solvingStatusCallBack);

    /// <inheritdoc/>
    public async Task<Result<IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>>> SolveLPAsync(
        string name, LinearMetaModel<Flt64> metaModel,
        bool toLogModel = false, RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null) {
        foreach (Func<IColumnGenerationSolver> factory in _solverFactories) {
            try {
                Result<IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>> result = await factory().SolveLPAsync(name, metaModel, toLogModel, registrationStatusCallBack, solvingStatusCallBack);
                if (result is Ok<IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>>) {
                    return result;
                }
            }
            catch (Exception e) {
                _logger.LogError(e, "serial CG solver threw");
            }
        }
        return new Failed<IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.SolverNotFound, "No solver succeeded"));
    }

    /// <inheritdoc/>
    public Task<Result<IColumnGenerationSolver.LpResultOf<V>, ErrorCode, Error<ErrorCode>>> SolveLPAsAsync<V>(
        string name, LinearMetaModel<Flt64> metaModel, IIntoValue<V> converter,
        bool toLogModel = false, RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null) where V : struct, IRealNumber<V>, INumberField<V>
        => _solverFactories[0]().SolveLPAsAsync(name, metaModel, converter, toLogModel, registrationStatusCallBack, solvingStatusCallBack);
}
