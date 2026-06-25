#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Iis;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Core.Solver;
/// <summary>
/// 并行组合二次求解器，将多个二次求解器并行运行，取第一个或最优结果。
/// Parallel combinatorial quadratic solver: runs multiple quadratic solvers in
/// parallel, taking the first or best result.
/// </summary>
public sealed class ParallelCombinatorialQuadraticSolver : IAbstractQuadraticSolver {
    private readonly IReadOnlyList<Func<IAbstractQuadraticSolver>> _solverFactories;
    private readonly ParallelCombinatorialMode _mode;

    /// <inheritdoc/>
    public string Name { get; }

    public ParallelCombinatorialQuadraticSolver(
        IEnumerable<IAbstractQuadraticSolver> solvers,
        ParallelCombinatorialMode mode = ParallelCombinatorialMode.Best)
        : this(solvers.Select(s => (Func<IAbstractQuadraticSolver>)(() => s)).ToList(), mode) { }

    public ParallelCombinatorialQuadraticSolver(
        IEnumerable<Func<IAbstractQuadraticSolver>> solverExtractors,
        ParallelCombinatorialMode mode = ParallelCombinatorialMode.Best) {
        _solverFactories = solverExtractors.ToList();
        _mode = mode;
        Name = $"ParallelCombinatorial({string.Join(",", _solverFactories.Select(f => f().Name))})";
    }

    /// <inheritdoc/>
    public async Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        IQuadraticTetradModelView model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) {
        return _mode == ParallelCombinatorialMode.First
            ? await SolveFirstAsync(model, solvingStatusCallBack, cancellationToken)
            : await SolveBestAsync(model, solvingStatusCallBack, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        IQuadraticTetradModelView model,
        SolvingStatusCallBack? solvingStatusCallBack,
        IisConfig? iisConfig,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("IIS not supported by ParallelCombinatorialQuadraticSolver.");

    /// <inheritdoc/>
    public Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions), ErrorCode, Error<ErrorCode>>> InvokeAsync(
        IQuadraticTetradModelView model,
        ulong solutionAmount,
        SolvingStatusCallBack? solvingStatusCallBack,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("Solution pool not supported by ParallelCombinatorialQuadraticSolver.");

    /// <inheritdoc/>
    public Task<QuadraticTetradModel> DumpAsync(QuadraticMechanismModel<Flt64> model, CancellationToken ct = default) =>
        throw new NotSupportedException();

    /// <inheritdoc/>
    public Task<Result<QuadraticMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>> DumpAsync(
        QuadraticMetaModel<Flt64> model,
        RegistrationStatusCallBack? rcb,
        MechanismModelDumpingStatusCallBack? dcb,
        CancellationToken ct = default) =>
        throw new NotSupportedException();

    /// <summary>
    /// First 模式：首个成功即返回，取消其余。
    /// First mode: return the first successful result, cancel the rest.
    /// </summary>
    private async Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> SolveFirstAsync(
        IQuadraticTetradModelView model,
        SolvingStatusCallBack? solvingStatusCallBack,
        CancellationToken cancellationToken) {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var tasks = _solverFactories.Select(f => Task.Run(async () => {
            IAbstractQuadraticSolver solver = f();
            Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> ret = await solver.InvokeAsync(model, solvingStatusCallBack, cts.Token);
            return (solver, ret);
        }, cts.Token)).ToList();

        while (tasks.Count > 0) {
            Task<(IAbstractQuadraticSolver solver, Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> ret)> completed = await Task.WhenAny(tasks);
            tasks.Remove(completed);
            (IAbstractQuadraticSolver? solver, Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>? ret) = await completed;
            if (ret is Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> ok) {
                cts.Cancel();
                return Results.Ok(ok.Value);
            }
        }
        return Results.Failed<FeasibleSolverOutput<Flt64>>(new Err<ErrorCode>(ErrorCode.SolverNotFound, "No solver produced a feasible result."));
    }

    /// <summary>
    /// Best 模式：等待全部完成，按目标方向选最优 obj。
    /// Best mode: await all, pick best objective by direction.
    /// </summary>
    private async Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> SolveBestAsync(
        IQuadraticTetradModelView model,
        SolvingStatusCallBack? solvingStatusCallBack,
        CancellationToken cancellationToken) {
        var tasks = _solverFactories.Select(f => Task.Run(async () => {
            IAbstractQuadraticSolver solver = f();
            Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> ret = await solver.InvokeAsync(model, solvingStatusCallBack, cancellationToken);
            return (solver, ret);
        }, cancellationToken)).ToList();

        (IAbstractQuadraticSolver solver, Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> ret)[] results = await Task.WhenAll(tasks);
        var oks = results
            .Where(r => r.ret is Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>)
            .Select(r => ((Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>)r.ret!).Value)
            .ToList();
        if (oks.Count == 0) {
            return Results.Failed<FeasibleSolverOutput<Flt64>>(new Err<ErrorCode>(ErrorCode.SolverNotFound, "No solver produced a feasible result."));
        }

        bool isMax = model.Objective.Category == ObjectCategory.Maximum;
        FeasibleSolverOutput<Flt64>? best = isMax
            ? oks.MaxBy(o => o.Obj.ToDouble())
            : oks.MinBy(o => o.Obj.ToDouble());
        return Results.Ok(best!);
    }
}

/// <summary>
/// 并行组合线性求解器，将多个线性求解器并行运行，取第一个或最优结果。
/// Parallel combinatorial linear solver: runs multiple linear solvers in
/// parallel, taking the first or best result.
/// </summary>
public sealed class ParallelCombinatorialLinearSolver : IAbstractLinearSolver {
    private readonly IReadOnlyList<Func<IAbstractLinearSolver>> _solverFactories;
    private readonly ParallelCombinatorialMode _mode;

    /// <inheritdoc/>
    public string Name { get; }

    public ParallelCombinatorialLinearSolver(
        IEnumerable<IAbstractLinearSolver> solvers,
        ParallelCombinatorialMode mode = ParallelCombinatorialMode.Best)
        : this(solvers.Select(s => (Func<IAbstractLinearSolver>)(() => s)).ToList(), mode) { }

    public ParallelCombinatorialLinearSolver(
        IEnumerable<Func<IAbstractLinearSolver>> solverExtractors,
        ParallelCombinatorialMode mode = ParallelCombinatorialMode.Best) {
        _solverFactories = solverExtractors.ToList();
        _mode = mode;
        Name = $"ParallelCombinatorial({string.Join(",", _solverFactories.Select(f => f().Name))})";
    }

    /// <inheritdoc/>
    public async Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        ILinearTriadModelView model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) {
        return _mode == ParallelCombinatorialMode.First
            ? await SolveFirstAsync(model, solvingStatusCallBack, cancellationToken)
            : await SolveBestAsync(model, solvingStatusCallBack, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        LinearTriadModel model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        IisConfig? iisConfig = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("IIS not supported by ParallelCombinatorialLinearSolver.");

    /// <inheritdoc/>
    public Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions), ErrorCode, Error<ErrorCode>>> InvokeAsync(
        ILinearTriadModelView model,
        ulong solutionAmount,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Solution pool not supported by ParallelCombinatorialLinearSolver.");

    /// <inheritdoc/>
    public Task<LinearTriadModel> DumpAsync(LinearMechanismModel<Flt64> model, CancellationToken ct = default) =>
        throw new NotSupportedException();

    /// <inheritdoc/>
    public Task<Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>> DumpAsync(
        LinearMetaModel<Flt64> model,
        RegistrationStatusCallBack? rcb,
        MechanismModelDumpingStatusCallBack? dcb,
        CancellationToken ct = default) =>
        throw new NotSupportedException();

    private async Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> SolveFirstAsync(
        ILinearTriadModelView model,
        SolvingStatusCallBack? solvingStatusCallBack,
        CancellationToken cancellationToken) {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var tasks = _solverFactories.Select(f => Task.Run(async () => {
            IAbstractLinearSolver solver = f();
            Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> ret = await solver.InvokeAsync(model, solvingStatusCallBack, cts.Token);
            return (solver, ret);
        }, cts.Token)).ToList();

        while (tasks.Count > 0) {
            Task<(IAbstractLinearSolver solver, Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> ret)> completed = await Task.WhenAny(tasks);
            tasks.Remove(completed);
            (IAbstractLinearSolver? solver, Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>? ret) = await completed;
            if (ret is Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> ok) {
                cts.Cancel();
                return Results.Ok(ok.Value);
            }
        }
        return Results.Failed<FeasibleSolverOutput<Flt64>>(new Err<ErrorCode>(ErrorCode.SolverNotFound, "No solver produced a feasible result."));
    }

    private async Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> SolveBestAsync(
        ILinearTriadModelView model,
        SolvingStatusCallBack? solvingStatusCallBack,
        CancellationToken cancellationToken) {
        var tasks = _solverFactories.Select(f => Task.Run(async () => {
            IAbstractLinearSolver solver = f();
            Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> ret = await solver.InvokeAsync(model, solvingStatusCallBack, cancellationToken);
            return (solver, ret);
        }, cancellationToken)).ToList();

        (IAbstractLinearSolver solver, Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> ret)[] results = await Task.WhenAll(tasks);
        var oks = results
            .Where(r => r.ret is Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>)
            .Select(r => ((Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>)r.ret!).Value)
            .ToList();
        if (oks.Count == 0) {
            return Results.Failed<FeasibleSolverOutput<Flt64>>(new Err<ErrorCode>(ErrorCode.SolverNotFound, "No solver produced a feasible result."));
        }

        bool isMax = model.Objective.Category == ObjectCategory.Maximum;
        FeasibleSolverOutput<Flt64>? best = isMax
            ? oks.MaxBy(o => o.Obj.ToDouble())
            : oks.MinBy(o => o.Obj.ToDouble());
        return Results.Ok(best!);
    }
}

/// <summary>
/// 并行组合列生成求解器，将多个列生成求解器并行运行，取第一个或最优结果。
/// Parallel combinatorial column generation solver: runs multiple column generation solvers in
/// parallel, taking the first or best result.
/// </summary>
public sealed class ParallelCombinatorialColumnGenerationSolver : IColumnGenerationSolver {
    private readonly IReadOnlyList<Func<IColumnGenerationSolver>> _solverFactories;
    private readonly ParallelCombinatorialMode _mode;

    /// <inheritdoc/>
    public string Name { get; }

    public ParallelCombinatorialColumnGenerationSolver(
        IEnumerable<IColumnGenerationSolver> solvers,
        ParallelCombinatorialMode mode = ParallelCombinatorialMode.Best)
        : this(solvers.Select(s => (Func<IColumnGenerationSolver>)(() => s)).ToList(), mode) { }

    public ParallelCombinatorialColumnGenerationSolver(
        IEnumerable<Func<IColumnGenerationSolver>> solverExtractors,
        ParallelCombinatorialMode mode = ParallelCombinatorialMode.Best) {
        _solverFactories = solverExtractors.ToList();
        _mode = mode;
        Name = $"ParallelCombinatorial({string.Join(",", _solverFactories.Select(f => f().Name))})";
    }

    /// <inheritdoc/>
    public async Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> SolveMILPAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) {
        return _mode == ParallelCombinatorialMode.First
            ? await SolveMILPFirstAsync(name, metaModel, toLogModel, registrationStatusCallBack, solvingStatusCallBack, cancellationToken)
            : await SolveMILPBestAsync(name, metaModel, toLogModel, registrationStatusCallBack, solvingStatusCallBack, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> SolveMILPAsync(
        LinearMetaModel<Flt64> metaModel,
        FrameworkSolveOptions options,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("FrameworkSolveOptions not supported by ParallelCombinatorialColumnGenerationSolver.");

    /// <inheritdoc/>
    public Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions), ErrorCode, Error<ErrorCode>>> SolveMILPAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        ulong amount,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Solution pool not supported by ParallelCombinatorialColumnGenerationSolver.");

    /// <inheritdoc/>
    public Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions), ErrorCode, Error<ErrorCode>>> SolveMILPWithSolutionPoolAsync(
        LinearMetaModel<Flt64> metaModel,
        FrameworkSolveOptions options,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Solution pool not supported by ParallelCombinatorialColumnGenerationSolver.");

    /// <inheritdoc/>
    public Task<Result<IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>>> SolveLPAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("LP not supported by ParallelCombinatorialColumnGenerationSolver.");

    private async Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> SolveMILPFirstAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        bool toLogModel,
        RegistrationStatusCallBack? registrationStatusCallBack,
        SolvingStatusCallBack? solvingStatusCallBack,
        CancellationToken cancellationToken) {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var tasks = _solverFactories.Select(f => Task.Run(async () => {
            IColumnGenerationSolver solver = f();
            Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> ret = await solver.SolveMILPAsync(name, metaModel, toLogModel, registrationStatusCallBack, solvingStatusCallBack, cts.Token);
            return (solver, ret);
        }, cts.Token)).ToList();

        while (tasks.Count > 0) {
            Task<(IColumnGenerationSolver solver, Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> ret)> completed = await Task.WhenAny(tasks);
            tasks.Remove(completed);
            (IColumnGenerationSolver? solver, Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>? ret) = await completed;
            if (ret is Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> ok) {
                cts.Cancel();
                return Results.Ok(ok.Value);
            }
        }
        return Results.Failed<FeasibleSolverOutput<Flt64>>(new Err<ErrorCode>(ErrorCode.SolverNotFound, "No solver produced a feasible result."));
    }

    private async Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> SolveMILPBestAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        bool toLogModel,
        RegistrationStatusCallBack? registrationStatusCallBack,
        SolvingStatusCallBack? solvingStatusCallBack,
        CancellationToken cancellationToken) {
        var tasks = _solverFactories.Select(f => Task.Run(async () => {
            IColumnGenerationSolver solver = f();
            Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> ret = await solver.SolveMILPAsync(name, metaModel, toLogModel, registrationStatusCallBack, solvingStatusCallBack, cancellationToken);
            return (solver, ret);
        }, cancellationToken)).ToList();

        (IColumnGenerationSolver solver, Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> ret)[] results = await Task.WhenAll(tasks);
        var oks = results
            .Where(r => r.ret is Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>)
            .Select(r => ((Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>)r.ret!).Value)
            .ToList();
        if (oks.Count == 0) {
            return Results.Failed<FeasibleSolverOutput<Flt64>>(new Err<ErrorCode>(ErrorCode.SolverNotFound, "No solver produced a feasible result."));
        }

        FeasibleSolverOutput<Flt64>? best = oks.MinBy(o => o.Obj.ToDouble());
        return Results.Ok(best!);
    }
}
