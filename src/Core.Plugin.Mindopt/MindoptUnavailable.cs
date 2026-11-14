#nullable enable
#if !MINDOPT_AVAILABLE

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Config;
using Fuookami.Ospf.Core.Solver.Iis;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;

namespace Fuookami.Ospf.Core.Plugin.Mindopt;

/// <summary>
/// 未加载 MindOPT SDK 时的线性入口 / Linear entry when the MindOPT SDK is unavailable.
/// </summary>
/// <remarks>
/// 求解会返回准确的环境错误，模型转储仍可用于无 SDK 构建。/ Solving returns an accurate environment error; model dumping
/// remains available in SDK-free builds.
/// </remarks>
public sealed class MindoptLinearSolver : ILinearSolver {
    /// <inheritdoc/>
    public string Name => "mindopt";

    /// <inheritdoc/>
    public SolverDescriptor? Descriptor => null;

    /// <inheritdoc/>
    public SolverConfig Config { get; }

    /// <summary>
    /// 创建线性入口 / Create the linear entry.
    /// </summary>
    /// <param name="config">求解器配置 / Solver configuration.</param>
    public MindoptLinearSolver(SolverConfig? config = null) {
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

    internal Task<Result<MindoptLinearOutcome, ErrorCode, Error<ErrorCode>>>
        InvokeReportWithDualsAsync(
            ILinearTriadModelView model,
            SolvingStatusCallBack? solvingStatusCallBack = null,
            CancellationToken cancellationToken = default,
            bool requireFarkas = false) =>
        Task.FromResult(Unavailable<MindoptLinearOutcome>());

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
        Task.FromResult(model.ToLinearTriadModel(cancellationToken: cancellationToken));

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

    private static Result<T, ErrorCode, Error<ErrorCode>> Unavailable<T>() =>
        Results.Failed<T>(new Err<ErrorCode>(
            ErrorCode.OREngineEnvironmentLost,
            "未找到 MindOPT 托管 SDK / The MindOPT managed SDK was not found"));
}

/// <summary>
/// 未加载 MindOPT SDK 时的二次入口 / Quadratic entry when the MindOPT SDK is unavailable.
/// </summary>
public sealed class MindoptQuadraticSolver : IQuadraticSolver {
    /// <inheritdoc/>
    public string Name => "mindopt";

    /// <summary>求解器描述符 / Solver descriptor.</summary>
    public SolverDescriptor? Descriptor => null;

    /// <inheritdoc/>
    public SolverConfig Config { get; }

    /// <summary>
    /// 创建二次入口 / Create the quadratic entry.
    /// </summary>
    /// <param name="config">求解器配置 / Solver configuration.</param>
    public MindoptQuadraticSolver(SolverConfig? config = null) {
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
        Task.FromResult(model.ToQuadraticTetradModel(cancellationToken: cancellationToken));

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

    private static Result<T, ErrorCode, Error<ErrorCode>> Unavailable<T>() =>
        Results.Failed<T>(new Err<ErrorCode>(
            ErrorCode.OREngineEnvironmentLost,
            "未找到 MindOPT 托管 SDK / The MindOPT managed SDK was not found"));
}
#endif
