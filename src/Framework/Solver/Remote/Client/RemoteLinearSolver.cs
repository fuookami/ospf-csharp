// 远程线性求解器 / Remote linear solver.
// 与`RemoteLinearSolver` 对应：同一个求解器接口，后端换成远程执行。
// `RemoteLinearSolver`: the same solver interface with the
// backend swapped for remote execution.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Iis;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Framework.Solver.Remote.Adapter;
using Fuookami.Ospf.Framework.Solver.Remote.Domain;
using Fuookami.Ospf.Framework.Solver.Remote.Port;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using CoreSolverConfig = Fuookami.Ospf.Core.Solver.Config.SolverConfig;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Solver.Remote.Client;

/// <summary>
/// 远程线性求解器 / Remote linear solver.
/// </summary>
/// <remarks>
/// 装饰一个本地求解器，但把**统一报告入口**（<see cref="InvokeReportAsync(ILinearTriadModelView,
/// SolvingStatusCallBack?, CancellationToken)"/>）改走远程执行：模型经
/// <see cref="OspfRemoteModelSerializer"/> 序列化、提交为远程任务、拿回**未压缩**的终态切片、读回稳定解工件，
/// 最后经 <see cref="RemoteSolverReportMapping.ToSolveReport"/> 组装成 Core 报告。
///
/// **旧版阻塞入口刻意不转发到本地求解器**：那会让"远程求解器"在某些入口上悄悄变成本地求解，调用方无从
/// 察觉。这些入口改为返回明确的结构化错误，指向统一报告入口。这是一个有意的能力边界，不是遗漏。
///
/// Decorates a local solver but routes the **unified report entry**
/// (<see cref="InvokeReportAsync(ILinearTriadModelView, SolvingStatusCallBack?, CancellationToken)"/>) over
/// remote execution: the model is serialized by <see cref="OspfRemoteModelSerializer"/>, submitted as a
/// remote task, the **unreduced** terminal slice comes back, the stable-solution artifact is read, and
/// <see cref="RemoteSolverReportMapping.ToSolveReport"/> assembles the Core report.
///
/// The legacy blocking entries are **deliberately not forwarded to the local solver**: that would make a
/// "remote solver" silently fall back to local execution on some entries with the caller none the wiser.
/// They return an explicit structured error pointing at the unified report entry. That is an intentional
/// capability boundary, not an omission.
/// </remarks>
public sealed class RemoteLinearSolver : ILinearSolver {
    private readonly ILinearSolver _delegate;
    private readonly RemoteSolverClient _remoteClient;
    private readonly IObjectStoragePort? _resultStorage;
    private readonly RemoteSolverRuntimeOptions _options;

    /// <summary>
    /// 构造远程线性求解器 / Construct a remote linear solver.
    /// </summary>
    /// <param name="delegateSolver">本地求解器委托 / Local solver delegate.</param>
    /// <param name="remoteClient">远程求解客户端 / Remote solve client.</param>
    /// <param name="resultStorage">结果对象存储，可为空 / Result object storage, may be null.</param>
    /// <param name="options">运行时配置，为空时取默认值 / Runtime options; defaults when null.</param>
    public RemoteLinearSolver(
        ILinearSolver delegateSolver,
        RemoteSolverClient remoteClient,
        IObjectStoragePort? resultStorage = null,
        RemoteSolverRuntimeOptions? options = null) {
        _delegate = delegateSolver ?? throw new ArgumentNullException(nameof(delegateSolver));
        _remoteClient = remoteClient ?? throw new ArgumentNullException(nameof(remoteClient));
        _resultStorage = resultStorage;
        _options = options ?? new RemoteSolverRuntimeOptions();
    }

    /// <inheritdoc/>
    public string Name => _delegate.Name;

    /// <inheritdoc/>
    public SolverDescriptor? Descriptor => _delegate.Descriptor;

    /// <inheritdoc/>
    public CoreSolverConfig Config => _delegate.Config;

    /// <inheritdoc/>
    public IReadOnlyList<IInfeasibilityAnalyzer<ILinearTriadModelView>> DiagnosticAnalyzers(
        IisConfig config) => _delegate.DiagnosticAnalyzers(config);

    /// <summary>
    /// 经远程后端求解并返回统一报告 / Solve through the remote backend and return a unified report.
    /// </summary>
    /// <param name="model">线性三元模型视图 / Linear triad model view.</param>
    /// <param name="solvingStatusCallBack">求解状态回调 / Solving status callback.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>求解报告 / Solve report.</returns>
    public async Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeReportAsync(
        ILinearTriadModelView model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(model);

        Try identityValidation = model.ValidateIdentity();
        if (!identityValidation.IsOk) {
            return Results.Failed<SolveReport<Flt64>>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "模型元素身份元数据无效，不能提交远程模型 / "
                + "Model element identity metadata is invalid; remote submission is rejected"));
        }
        Result<ModelData, ErrorCode, Error<ErrorCode>> modelData =
            OspfRemoteModelSerializer.ModelData(model);
        if (!modelData.IsOk) {
            return Results.Failed<SolveReport<Flt64>>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "远程线性模型序列化失败 / Remote linear model serialization failed"));
        }

        Result<SliceResult, ErrorCode, Error<ErrorCode>> solved =
            await _remoteClient.SolveSliceAsync(
                payload: new SolvePayload(
                    ModelData: modelData.Value,
                    TaskMeta: new TaskMeta(TargetType: new TargetTypeName("linear"))),
                taskId: _options.TaskIdProvider(),
                sliceId: _options.SliceIdProvider(),
                nodeId: new NodeId(_options.NodeId),
                tenantId: new TenantId(_options.TenantId),
                quantum: _options.Quantum,
                maxRounds: _options.MaxRounds,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!solved.IsOk) {
            return Results.Failed<SolveReport<Flt64>>(new Err<ErrorCode>(
                ErrorCode.ApplicationFailed,
                "远程线性求解未返回终态切片 / Remote linear solve returned no terminal slice"));
        }

        Result<SerializedSolution?, ErrorCode, Error<ErrorCode>> artifact =
            await RemoteCpResultArtifact.ReadSerializedCpResultAsync(
                slice: solved.Value,
                storage: _resultStorage,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!artifact.IsOk) {
            return Results.Failed<SolveReport<Flt64>>(new Err<ErrorCode>(
                ErrorCode.ORSolutionInvalid,
                "远程线性结果工件不可读 / Remote linear result artifact cannot be read"));
        }

        return RemoteSolverReportMapping.ToSolveReport(
            slice: solved.Value,
            serialized: artifact.Value,
            variableCount: model.Variables.Count,
            modelTypes: LinearModelTypes);
    }

    /// <summary>线性模型类型能力 / Linear model-type capabilities.</summary>
    public static readonly IReadOnlySet<SolverModelType> LinearModelTypes =
        new HashSet<SolverModelType> { SolverModelType.Lp, SolverModelType.Mip };

    /// <summary>
    /// 旧版阻塞入口不支持 / The legacy blocking entry is not supported.
    /// </summary>
    /// <remarks>
    /// 见类型注释：转发给本地求解器会让"远程求解器"在某些入口上悄悄变成本地求解。
    /// See the type remarks: forwarding to the local solver would silently turn a remote solver into a local
    /// one on some entries.
    /// </remarks>
    /// <param name="model">线性三元模型视图 / Linear triad model view.</param>
    /// <param name="solvingStatusCallBack">求解状态回调 / Solving status callback.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>始终返回结构化错误 / Always a structured error.</returns>
    public Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        ILinearTriadModelView model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) =>
        LegacyEntryUnsupported<FeasibleSolverOutput<Flt64>>();

    /// <inheritdoc cref="InvokeAsync(ILinearTriadModelView, SolvingStatusCallBack?, CancellationToken)"/>
    /// <param name="model">线性三元模型 / Linear triad model.</param>
    /// <param name="solvingStatusCallBack">求解状态回调 / Solving status callback.</param>
    /// <param name="iisConfig">IIS 配置 / IIS config.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>始终返回结构化错误 / Always a structured error.</returns>
    public Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        LinearTriadModel model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        IisConfig? iisConfig = null,
        CancellationToken cancellationToken = default) =>
        LegacyEntryUnsupported<SolverOutput>();

    /// <inheritdoc cref="InvokeAsync(ILinearTriadModelView, SolvingStatusCallBack?, CancellationToken)"/>
    /// <param name="model">线性三元模型视图 / Linear triad model view.</param>
    /// <param name="solutionAmount">解池容量 / Solution pool capacity.</param>
    /// <param name="solvingStatusCallBack">求解状态回调 / Solving status callback.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>始终返回结构化错误 / Always a structured error.</returns>
    public Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions),
        ErrorCode, Error<ErrorCode>>> InvokeAsync(
        ILinearTriadModelView model,
        ulong solutionAmount,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) =>
        LegacyEntryUnsupported<
            (FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions)>();

    /// <summary>
    /// 转储交由本地委托 / Dumping is delegated to the local solver.
    /// </summary>
    /// <remarks>
    /// 转储只做模型转换、不涉及求解后端，转发不会让求解悄悄变成本地执行。
    /// Dumping only converts models and never touches a solving backend, so forwarding cannot silently
    /// switch solving to local execution.
    /// </remarks>
    /// <param name="model">线性机制模型 / Linear mechanism model.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>三元模型 / Triad model.</returns>
    public Task<Result<LinearTriadModel, ErrorCode, Error<ErrorCode>>> DumpAsync(
        LinearMechanismModel<Flt64> model,
        CancellationToken cancellationToken = default) =>
        _delegate.DumpAsync(model, cancellationToken);

    /// <inheritdoc cref="DumpAsync(LinearMechanismModel{Flt64}, CancellationToken)"/>
    /// <param name="model">线性元模型 / Linear meta model.</param>
    /// <param name="registrationStatusCallBack">注册状态回调 / Registration status callback.</param>
    /// <param name="dumpingStatusCallBack">转储状态回调 / Dumping status callback.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>机制模型 / Mechanism model.</returns>
    public Task<Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>> DumpAsync(
        LinearMetaModel<Flt64> model,
        RegistrationStatusCallBack? registrationStatusCallBack,
        MechanismModelDumpingStatusCallBack? dumpingStatusCallBack,
        CancellationToken cancellationToken = default) =>
        _delegate.DumpAsync(
            model,
            registrationStatusCallBack,
            dumpingStatusCallBack,
            cancellationToken);

    private static Task<Result<T, ErrorCode, Error<ErrorCode>>> LegacyEntryUnsupported<T>() =>
        Task.FromResult(Results.Failed<T>(new Err<ErrorCode>(
            ErrorCode.IllegalArgument,
            "远程求解器只支持统一报告入口 InvokeReportAsync / "
            + "A remote solver only supports the unified report entry InvokeReportAsync")));
}
