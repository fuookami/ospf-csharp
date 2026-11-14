#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Iis;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
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
public sealed class ParallelCombinatorialLinearSolver : IAbstractLinearSolver {
    private readonly IReadOnlyList<Func<IAbstractLinearSolver>> _solverFactories;
    private readonly ParallelCombinatorialMode _mode;
    private readonly ILogger _logger;
    private readonly Lazy<string> _name;

    public string Name => _name.Value;
    public ParallelCombinatorialLinearSolver(
        IEnumerable<IAbstractLinearSolver> solvers,
        ParallelCombinatorialMode mode = ParallelCombinatorialMode.Best,
        ILogger? logger = null)
        : this(solvers.Select(s => (Func<IAbstractLinearSolver>)(() => s)).ToList(), mode, logger) { }

    public ParallelCombinatorialLinearSolver(
        IReadOnlyList<Func<IAbstractLinearSolver>> solverFactories,
        ParallelCombinatorialMode mode = ParallelCombinatorialMode.Best,
        ILogger? logger = null) {
        _solverFactories = solverFactories.ToArray();
        _mode = mode;
        _logger = logger ?? NullLogger.Instance;
        _name = new Lazy<string>(() => $"ParallelCombinatorialLinear({string.Join(",", _solverFactories.Select(f => f().Name))})");
    }

    /// <summary>并行求解并保留所有尝试 / Solve in parallel and retain every attempt.</summary>
    /// <remarks>等待所有尝试结束；First 按工厂顺序选择。Best 在所有候选目标均为有限值时比较，否则退回 First。调用方须保证模型不被修改或关闭，并保证后端和回调可并发使用。 / Waits for every attempt; First uses factory order. Best compares only when all candidate objectives are finite, otherwise it falls back to First. The caller must keep the model unchanged and open, and ensure backends and callbacks are concurrency-safe.</remarks>
    /// <param name="model">线性矩阵模型 / Linear matrix model.</param>
    /// <param name="solvingStatusCallBack">求解状态回调 / Solving status callback.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>组合报告或校验错误 / Combinatorial report or validation error.</returns>
    public Task<Result<CombinatorialSolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> SolveCombinatorialReportAsync(
        ILinearTriadModelView model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) =>
        ParallelCombinatorialReportSupport.SolveAsync(
            validation: model.ValidateIdentity(),
            factories: _solverFactories,
            mode: _mode,
            category: () => model.Objective.Category,
            name: solver => solver.Name,
            solve: solver => solver.InvokeReportAsync(
                model: model,
                solvingStatusCallBack: solvingStatusCallBack,
                cancellationToken: cancellationToken),
            logger: _logger,
            cancellationToken: cancellationToken);

    /// <summary>并行求解并保留解池和尝试 / Solve in parallel and retain solution pools and attempts.</summary>
    /// <remarks>选择与生命周期规则同普通报告入口。 / Selection and lifetime rules match the ordinary report entry.</remarks>
    /// <param name="model">线性矩阵模型 / Linear matrix model.</param>
    /// <param name="solutionAmount">每个后端的解池容量 / Solution pool capacity per backend.</param>
    /// <param name="solvingStatusCallBack">求解状态回调 / Solving status callback.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>组合报告或校验错误 / Combinatorial report or validation error.</returns>
    public Task<Result<CombinatorialSolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> SolveCombinatorialReportAsync(
        ILinearTriadModelView model,
        ulong solutionAmount,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) =>
        ParallelCombinatorialReportSupport.SolveAsync(
            validation: model.ValidateIdentity(),
            factories: _solverFactories,
            mode: _mode,
            category: () => model.Objective.Category,
            name: solver => solver.Name,
            solve: solver => solver.InvokeReportAsync(
                model: model,
                solutionAmount: solutionAmount,
                solvingStatusCallBack: solvingStatusCallBack,
                cancellationToken: cancellationToken),
            logger: _logger,
            cancellationToken: cancellationToken);

    /// <inheritdoc/>
    /// <remarks>等待所有尝试；模型不得并发修改或关闭，后端和回调须支持并发。 / Waits for all attempts; do not mutate or close the model concurrently, and ensure backends and callbacks are concurrency-safe.</remarks>
    public async Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeReportAsync(
        ILinearTriadModelView model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) =>
        (await SolveCombinatorialReportAsync(
            model: model,
            solvingStatusCallBack: solvingStatusCallBack,
            cancellationToken: cancellationToken)).Map(SerialCombinatorialReportSupport.ToTerminalReport);

    /// <inheritdoc/>
    /// <remarks>等待所有尝试；模型不得并发修改或关闭，后端和回调须支持并发。 / Waits for all attempts; do not mutate or close the model concurrently, and ensure backends and callbacks are concurrency-safe.</remarks>
    public async Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeReportAsync(
        ILinearTriadModelView model,
        ulong solutionAmount,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) =>
        (await SolveCombinatorialReportAsync(
            model: model,
            solutionAmount: solutionAmount,
            solvingStatusCallBack: solvingStatusCallBack,
            cancellationToken: cancellationToken)).Map(SerialCombinatorialReportSupport.ToTerminalReport);

    /// <inheritdoc/>
    /// <remarks>等待所有已调度后端，模型不得并发修改或关闭。 / Waits for all scheduled backends; do not mutate or close the model concurrently.</remarks>
    public Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        ILinearTriadModelView model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) =>
        CombinatorialLegacySupport.SolveAsync(
            validation: model.ValidateIdentity(),
            factories: _solverFactories,
            mode: _mode,
            category: () => model.Objective.Category,
            solve: solver => solver.InvokeAsync(
                model: model,
                solvingStatusCallBack: solvingStatusCallBack,
                cancellationToken: cancellationToken),
            describe: CombinatorialLegacySupport.Describe,
            logger: _logger,
            cancellationToken: cancellationToken);

    /// <inheritdoc/>
    /// <remarks>保留所选后端原始输出；等待所有已调度后端。 / Preserves the selected backend output and waits for all scheduled backends.</remarks>
    public Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        LinearTriadModel model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        IisConfig? iisConfig = null,
        CancellationToken cancellationToken = default) =>
        CombinatorialLegacySupport.SolveAsync(
            validation: model.ValidateIdentity(),
            factories: _solverFactories,
            mode: _mode,
            category: () => model.Objective.Category,
            solve: solver => solver.InvokeAsync(
                model: model,
                solvingStatusCallBack: solvingStatusCallBack,
                iisConfig: iisConfig,
                cancellationToken: cancellationToken),
            describe: CombinatorialLegacySupport.Describe,
            logger: _logger,
            cancellationToken: cancellationToken);

    /// <inheritdoc/>
    /// <remarks>保留所选后端的解池；等待所有已调度后端。 / Preserves the selected backend's pool and waits for all scheduled backends.</remarks>
    public Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions), ErrorCode, Error<ErrorCode>>> InvokeAsync(
        ILinearTriadModelView model,
        ulong solutionAmount,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) =>
        CombinatorialLegacySupport.SolveAsync(
            validation: model.ValidateIdentity(),
            factories: _solverFactories,
            mode: _mode,
            category: () => model.Objective.Category,
            solve: solver => solver.InvokeAsync(
                model: model,
                solutionAmount: solutionAmount,
                solvingStatusCallBack: solvingStatusCallBack,
                cancellationToken: cancellationToken),
            describe: CombinatorialLegacySupport.Describe,
            logger: _logger,
            cancellationToken: cancellationToken);

    /// <inheritdoc/>
    /// <remarks>只使用首个工厂，不重试其他后端。 / Uses only the first factory; does not retry other backends.</remarks>
    public Task<Result<LinearTriadModel, ErrorCode, Error<ErrorCode>>> DumpAsync(
        LinearMechanismModel<Flt64> model,
        CancellationToken cancellationToken = default) =>
        CombinatorialDumpSupport.DumpAsync(
            factories: _solverFactories,
            dump: solver => solver.DumpAsync(model, cancellationToken),
            logger: _logger,
            cancellationToken: cancellationToken);

    /// <inheritdoc/>
    /// <remarks>只使用首个工厂，不重试其他后端。 / Uses only the first factory; does not retry other backends.</remarks>
    public Task<Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>> DumpAsync(
        LinearMetaModel<Flt64> model,
        RegistrationStatusCallBack? registrationStatusCallBack,
        MechanismModelDumpingStatusCallBack? dumpingStatusCallBack,
        CancellationToken cancellationToken = default) =>
        CombinatorialDumpSupport.DumpAsync(
            factories: _solverFactories,
            dump: solver => solver.DumpAsync(
                model: model,
                registrationStatusCallBack: registrationStatusCallBack,
                dumpingStatusCallBack: dumpingStatusCallBack,
                cancellationToken: cancellationToken),
            logger: _logger,
            cancellationToken: cancellationToken);

}

/// <summary>
/// 串行组合线性求解器 / Serial combinatorial linear solver.
/// </summary>
public sealed class SerialCombinatorialLinearSolver : IAbstractLinearSolver {
    private readonly IReadOnlyList<Func<IAbstractLinearSolver>> _solverFactories;
    private readonly ILogger _logger;
    private readonly IReadOnlySet<ErrorCode> _stopCodes;
    private readonly Lazy<string> _name;

    public string Name => _name.Value;
    /// <summary>创建串行线性组合器 / Create a serial linear combinator.</summary>
    /// <param name="solvers">按顺序尝试的求解器 / Solvers attempted in order.</param>
    /// <param name="logger">异常记录器 / Exception logger.</param>
    /// <param name="stopCodes">停止重试的错误码；默认不可行或无界 / Stop codes, defaulting to infeasible or unbounded.</param>
    public SerialCombinatorialLinearSolver(
        IEnumerable<IAbstractLinearSolver> solvers,
        ILogger? logger = null,
        IReadOnlySet<ErrorCode>? stopCodes = null)
        : this(
            solverFactories: solvers.Select(s => (Func<IAbstractLinearSolver>)(() => s)).ToList(),
            logger: logger,
            stopCodes: stopCodes) { }

    /// <summary>使用工厂创建串行线性组合器 / Create a serial linear combinator from factories.</summary>
    /// <param name="solverFactories">按顺序调用的工厂 / Factories invoked in order.</param>
    /// <param name="logger">异常记录器 / Exception logger.</param>
    /// <param name="stopCodes">停止重试的错误码；默认不可行或无界 / Stop codes, defaulting to infeasible or unbounded.</param>
    public SerialCombinatorialLinearSolver(
        IReadOnlyList<Func<IAbstractLinearSolver>> solverFactories,
        ILogger? logger = null,
        IReadOnlySet<ErrorCode>? stopCodes = null) {
        _solverFactories = solverFactories.ToArray();
        _logger = logger ?? NullLogger.Instance;
        _stopCodes = new HashSet<ErrorCode>(stopCodes ?? SerialCombinatorialReportSupport.DefaultStopCodes);
        _name = new Lazy<string>(() => $"SerialCombinatorialLinear({string.Join(",", _solverFactories.Select(f => f().Name))})");
    }

    /// <summary>串行求解并保留所有尝试 / Solve serially and retain every attempt.</summary>
    /// <param name="model">线性矩阵模型 / Linear matrix model.</param>
    /// <param name="solvingStatusCallBack">求解状态回调 / Solving status callback.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>组合报告或校验错误 / Combinatorial report or validation error.</returns>
    public Task<Result<CombinatorialSolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> SolveCombinatorialReportAsync(
        ILinearTriadModelView model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) =>
        SerialCombinatorialReportSupport.SolveAsync(
            validation: model.ValidateIdentity(),
            factories: _solverFactories,
            stopCodes: _stopCodes,
            name: solver => solver.Name,
            solve: solver => solver.InvokeReportAsync(
                model: model,
                solvingStatusCallBack: solvingStatusCallBack,
                cancellationToken: cancellationToken),
            logger: _logger,
            cancellationToken: cancellationToken);

    /// <summary>串行求解并保留解池和尝试 / Solve serially and retain the solution pool and attempts.</summary>
    /// <param name="model">线性矩阵模型 / Linear matrix model.</param>
    /// <param name="solutionAmount">解池容量 / Solution pool capacity.</param>
    /// <param name="solvingStatusCallBack">求解状态回调 / Solving status callback.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>组合报告或校验错误 / Combinatorial report or validation error.</returns>
    public Task<Result<CombinatorialSolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> SolveCombinatorialReportAsync(
        ILinearTriadModelView model,
        ulong solutionAmount,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) =>
        SerialCombinatorialReportSupport.SolveAsync(
            validation: model.ValidateIdentity(),
            factories: _solverFactories,
            stopCodes: _stopCodes,
            name: solver => solver.Name,
            solve: solver => solver.InvokeReportAsync(
                model: model,
                solutionAmount: solutionAmount,
                solvingStatusCallBack: solvingStatusCallBack,
                cancellationToken: cancellationToken),
            logger: _logger,
            cancellationToken: cancellationToken);

    /// <inheritdoc/>
    public async Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeReportAsync(
        ILinearTriadModelView model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) =>
        (await SolveCombinatorialReportAsync(
            model: model,
            solvingStatusCallBack: solvingStatusCallBack,
            cancellationToken: cancellationToken)).Map(SerialCombinatorialReportSupport.ToTerminalReport);

    /// <inheritdoc/>
    public async Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeReportAsync(
        ILinearTriadModelView model,
        ulong solutionAmount,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) =>
        (await SolveCombinatorialReportAsync(
            model: model,
            solutionAmount: solutionAmount,
            solvingStatusCallBack: solvingStatusCallBack,
            cancellationToken: cancellationToken)).Map(SerialCombinatorialReportSupport.ToTerminalReport);

    /// <inheritdoc/>
    public Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        ILinearTriadModelView model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) =>
        CombinatorialLegacySupport.SolveSerialAsync(
            validation: model.ValidateIdentity(),
            factories: _solverFactories,
            stopCodes: _stopCodes,
            solve: solver => solver.InvokeAsync(
                model: model,
                solvingStatusCallBack: solvingStatusCallBack,
                cancellationToken: cancellationToken),
            describe: CombinatorialLegacySupport.Describe,
            logger: _logger,
            cancellationToken: cancellationToken);

    /// <inheritdoc/>
    /// <remarks>保留终态或首个非终态原始诊断；不生成 IIS。 / Retains the terminal or first nonterminal raw diagnostic; does not generate an IIS.</remarks>
    public Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        LinearTriadModel model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        IisConfig? iisConfig = null,
        CancellationToken cancellationToken = default) =>
        CombinatorialLegacySupport.SolveSerialAsync(
            validation: model.ValidateIdentity(),
            factories: _solverFactories,
            stopCodes: _stopCodes,
            solve: solver => solver.InvokeAsync(
                model: model,
                solvingStatusCallBack: solvingStatusCallBack,
                iisConfig: iisConfig,
                cancellationToken: cancellationToken),
            describe: CombinatorialLegacySupport.Describe,
            logger: _logger,
            cancellationToken: cancellationToken);

    /// <inheritdoc/>
    public Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions), ErrorCode, Error<ErrorCode>>> InvokeAsync(
        ILinearTriadModelView model,
        ulong solutionAmount,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) =>
        CombinatorialLegacySupport.SolveSerialAsync(
            validation: model.ValidateIdentity(),
            factories: _solverFactories,
            stopCodes: _stopCodes,
            solve: solver => solver.InvokeAsync(
                model: model,
                solutionAmount: solutionAmount,
                solvingStatusCallBack: solvingStatusCallBack,
                cancellationToken: cancellationToken),
            describe: CombinatorialLegacySupport.Describe,
            logger: _logger,
            cancellationToken: cancellationToken);

    /// <inheritdoc/>
    /// <remarks>只使用首个工厂，不重试其他后端。 / Uses only the first factory; does not retry other backends.</remarks>
    public Task<Result<LinearTriadModel, ErrorCode, Error<ErrorCode>>> DumpAsync(
        LinearMechanismModel<Flt64> model,
        CancellationToken cancellationToken = default) =>
        CombinatorialDumpSupport.DumpAsync(
            factories: _solverFactories,
            dump: solver => solver.DumpAsync(model, cancellationToken),
            logger: _logger,
            cancellationToken: cancellationToken);

    /// <inheritdoc/>
    /// <remarks>只使用首个工厂，不重试其他后端。 / Uses only the first factory; does not retry other backends.</remarks>
    public Task<Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>> DumpAsync(
        LinearMetaModel<Flt64> model,
        RegistrationStatusCallBack? registrationStatusCallBack,
        MechanismModelDumpingStatusCallBack? dumpingStatusCallBack,
        CancellationToken cancellationToken = default) =>
        CombinatorialDumpSupport.DumpAsync(
            factories: _solverFactories,
            dump: solver => solver.DumpAsync(
                model: model,
                registrationStatusCallBack: registrationStatusCallBack,
                dumpingStatusCallBack: dumpingStatusCallBack,
                cancellationToken: cancellationToken),
            logger: _logger,
            cancellationToken: cancellationToken);
}
