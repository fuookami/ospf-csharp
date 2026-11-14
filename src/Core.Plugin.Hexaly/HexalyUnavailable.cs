#nullable enable
#if !HEXALY_AVAILABLE

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Config;
using Fuookami.Ospf.Core.Solver.Iis;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using static Fuookami.Ospf.Core.Plugin.Hexaly.HexalyUnavailableSupport;

namespace Fuookami.Ospf.Core.Plugin.Hexaly;

/// <summary>
/// 未加载 Hexaly SDK 时的线性入口 / Linear entry when the Hexaly SDK is unavailable.
/// </summary>
public sealed class HexalyLinearSolver : ILinearSolver {
    /// <inheritdoc/>
    public string Name => "hexaly";

    /// <inheritdoc/>
    public SolverDescriptor? Descriptor => null;

    /// <inheritdoc/>
    public SolverConfig Config { get; }

    /// <summary>
    /// 创建线性入口 / Create the linear entry.
    /// </summary>
    /// <param name="config">求解器配置 / Solver configuration.</param>
    public HexalyLinearSolver(SolverConfig? config = null) {
        Config = config ?? new SolverConfig();
    }

    /// <inheritdoc/>
    public Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeReportAsync(
        ILinearTriadModelView model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Unavailable<SolveReport<Flt64>>());

    /// <inheritdoc/>
    public Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeReportAsync(
        ILinearTriadModelView model,
        ulong solutionAmount,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Unavailable<SolveReport<Flt64>>());

    /// <inheritdoc/>
    public Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        ILinearTriadModelView model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Unavailable<FeasibleSolverOutput<Flt64>>());

    /// <inheritdoc/>
    public Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        LinearTriadModel model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        IisConfig? iisConfig = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Unavailable<SolverOutput>());

    /// <inheritdoc/>
    public Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions), ErrorCode, Error<ErrorCode>>> InvokeAsync(
        ILinearTriadModelView model,
        ulong solutionAmount,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Unavailable<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions)>());

    /// <inheritdoc/>
    public Task<Result<LinearTriadModel, ErrorCode, Error<ErrorCode>>> DumpAsync(
        LinearMechanismModel<Flt64> model,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(model.ToLinearTriadModel(
            cancellationToken: cancellationToken));

    /// <inheritdoc/>
    public Task<Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>> DumpAsync(
        LinearMetaModel<Flt64> model,
        RegistrationStatusCallBack? registrationStatusCallBack,
        MechanismModelDumpingStatusCallBack? dumpingStatusCallBack,
        CancellationToken cancellationToken = default) =>
        LinearMechanismModel<Flt64>.InvokeAsync(
            metaModel: model,
            concurrent: Config.DumpMechanismModelConcurrent,
            blocking: Config.DumpMechanismModelBlocking,
            registrationStatusCallBack: registrationStatusCallBack,
            dumpingStatusCallBack: dumpingStatusCallBack);
}

/// <summary>
/// 未加载 Hexaly SDK 时的二次入口 / Quadratic entry when the Hexaly SDK is unavailable.
/// </summary>
public sealed class HexalyQuadraticSolver : IQuadraticSolver {
    /// <inheritdoc/>
    public string Name => "hexaly";

    /// <inheritdoc/>
    public SolverDescriptor? Descriptor => null;

    /// <inheritdoc/>
    public SolverConfig Config { get; }

    /// <summary>
    /// 创建二次入口 / Create the quadratic entry.
    /// </summary>
    /// <param name="config">求解器配置 / Solver configuration.</param>
    public HexalyQuadraticSolver(SolverConfig? config = null) {
        Config = config ?? new SolverConfig();
    }

    /// <inheritdoc/>
    public Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeReportAsync(
        IQuadraticTetradModelView model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Unavailable<SolveReport<Flt64>>());

    /// <inheritdoc/>
    public Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeReportAsync(
        IQuadraticTetradModelView model,
        ulong solutionAmount,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Unavailable<SolveReport<Flt64>>());

    /// <inheritdoc/>
    public Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        IQuadraticTetradModelView model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Unavailable<FeasibleSolverOutput<Flt64>>());

    /// <inheritdoc/>
    public Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        IQuadraticTetradModelView model,
        SolvingStatusCallBack? solvingStatusCallBack,
        IisConfig? iisConfig,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Unavailable<SolverOutput>());

    /// <inheritdoc/>
    public Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions), ErrorCode, Error<ErrorCode>>> InvokeAsync(
        IQuadraticTetradModelView model,
        ulong solutionAmount,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Unavailable<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions)>());

    /// <inheritdoc/>
    public Task<Result<QuadraticTetradModel, ErrorCode, Error<ErrorCode>>> DumpAsync(
        QuadraticMechanismModel<Flt64> model,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(model.ToQuadraticTetradModel(
            cancellationToken: cancellationToken));

    /// <inheritdoc/>
    public Task<Result<QuadraticMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>> DumpAsync(
        QuadraticMetaModel<Flt64> model,
        RegistrationStatusCallBack? registrationStatusCallBack,
        MechanismModelDumpingStatusCallBack? dumpingStatusCallBack,
        CancellationToken cancellationToken = default) =>
        QuadraticMechanismModel<Flt64>.InvokeAsync(
            metaModel: model,
            concurrent: Config.DumpMechanismModelConcurrent,
            blocking: Config.DumpMechanismModelBlocking,
            registrationStatusCallBack: registrationStatusCallBack,
            dumpingStatusCallBack: dumpingStatusCallBack);
}

/// <summary>
/// 未加载 Hexaly SDK 时的列生成入口 / Column-generation entry when the Hexaly SDK is unavailable.
/// </summary>
public sealed class HexalyColumnGenerationSolver : IColumnGenerationSolver {
    /// <inheritdoc/>
    public string Name => "hexaly";

    /// <summary>求解器配置 / Solver configuration.</summary>
    public SolverConfig Config { get; }

    /// <summary>
    /// 创建列生成入口 / Create the column-generation entry.
    /// </summary>
    /// <param name="config">求解器配置 / Solver configuration.</param>
    public HexalyColumnGenerationSolver(SolverConfig? config = null) {
        Config = config ?? new SolverConfig();
    }

    /// <inheritdoc/>
    public Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> SolveMILPAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Unavailable<FeasibleSolverOutput<Flt64>>());

    /// <inheritdoc/>
    public Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> SolveMILPAsync(
        LinearMetaModel<Flt64> metaModel,
        FrameworkSolveOptions options,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Unavailable<FeasibleSolverOutput<Flt64>>());

    /// <inheritdoc/>
    public Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions), ErrorCode, Error<ErrorCode>>> SolveMILPAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        ulong amount,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Unavailable<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions)>());

    /// <inheritdoc/>
    public Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions), ErrorCode, Error<ErrorCode>>> SolveMILPWithSolutionPoolAsync(
        LinearMetaModel<Flt64> metaModel,
        FrameworkSolveOptions options,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Unavailable<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions)>());

    /// <inheritdoc/>
    public Task<Result<IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>>> SolveLPAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Unavailable<IColumnGenerationSolver.LpResult>());
}

internal static class HexalyUnavailableSupport {
    internal static Result<T, ErrorCode, Error<ErrorCode>> Unavailable<T>() =>
        Results.Failed<T>(new Err<ErrorCode>(
            ErrorCode.OREngineEnvironmentLost,
            "未找到 Hexaly 托管 SDK / The Hexaly managed SDK was not found"));
}
#endif
