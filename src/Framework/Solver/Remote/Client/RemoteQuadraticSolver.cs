// 远程二次求解器 / Remote quadratic solver.
// 与`RemoteQuadraticSolver` 对应：同一个求解器接口，后端换成远程执行。
// `RemoteQuadraticSolver`: the same solver interface with the
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
/// 远程二次求解器 / Remote quadratic solver.
/// </summary>
/// <remarks>
/// 与 <see cref="RemoteLinearSolver"/> 完全对称，只有两处不同：序列化走
/// <c>OspfRemoteModelSerializer</c> 的二次分支，且 <c>targetType</c> 为 <c>quadratic</c>。远程后端据此才知道
/// 该按二次模型处理载荷——写错会让后端按线性模型解读，从而静默丢掉二次项。
///
/// 旧版阻塞入口同样**不转发给本地求解器**（理由见 <see cref="RemoteLinearSolver"/> 的类型注释）；转储入口
/// 仍然转发，因为它只做模型转换、不涉及求解后端。
///
/// Fully symmetric to <see cref="RemoteLinearSolver"/> with two differences: serialization takes the
/// quadratic branch and <c>targetType</c> is <c>quadratic</c>. The backend relies on that to read the payload
/// as a quadratic model; getting it wrong would make the backend interpret it as linear and silently drop the
/// quadratic terms. The legacy blocking entries likewise do **not** forward to the local solver (see the
/// remarks on <see cref="RemoteLinearSolver"/>), while dumping still forwards because it only converts models
/// and never touches a solving backend.
/// </remarks>
public sealed class RemoteQuadraticSolver : IQuadraticSolver {
    private readonly IQuadraticSolver _delegate;
    private readonly RemoteSolverClient _remoteClient;
    private readonly IObjectStoragePort? _resultStorage;
    private readonly RemoteSolverRuntimeOptions _options;

    /// <summary>
    /// 构造远程二次求解器 / Construct a remote quadratic solver.
    /// </summary>
    /// <param name="delegateSolver">本地求解器委托 / Local solver delegate.</param>
    /// <param name="remoteClient">远程求解客户端 / Remote solve client.</param>
    /// <param name="resultStorage">结果对象存储，可为空 / Result object storage, may be null.</param>
    /// <param name="options">运行时配置，为空时取默认值 / Runtime options; defaults when null.</param>
    public RemoteQuadraticSolver(
        IQuadraticSolver delegateSolver,
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
    public CoreSolverConfig Config => _delegate.Config;

    /// <inheritdoc/>
    public IReadOnlyList<IInfeasibilityAnalyzer<IQuadraticTetradModelView>> DiagnosticAnalyzers(
        IisConfig config) => _delegate.DiagnosticAnalyzers(config);

    /// <summary>
    /// 经远程后端求解并返回统一报告 / Solve through the remote backend and return a unified report.
    /// </summary>
    /// <param name="model">二次四元模型视图 / Quadratic tetrad model view.</param>
    /// <param name="solvingStatusCallBack">求解状态回调 / Solving status callback.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>求解报告 / Solve report.</returns>
    public async Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeReportAsync(
        IQuadraticTetradModelView model,
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
                "远程二次模型序列化失败 / Remote quadratic model serialization failed"));
        }

        Result<SliceResult, ErrorCode, Error<ErrorCode>> solved =
            await _remoteClient.SolveSliceAsync(
                payload: new SolvePayload(
                    ModelData: modelData.Value,
                    TaskMeta: new TaskMeta(TargetType: new TargetTypeName("quadratic"))),
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
                "远程二次求解未返回终态切片 / Remote quadratic solve returned no terminal slice"));
        }

        Result<SerializedSolution?, ErrorCode, Error<ErrorCode>> artifact =
            await RemoteCpResultArtifact.ReadSerializedCpResultAsync(
                slice: solved.Value,
                storage: _resultStorage,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!artifact.IsOk) {
            return Results.Failed<SolveReport<Flt64>>(new Err<ErrorCode>(
                ErrorCode.ORSolutionInvalid,
                "远程二次结果工件不可读 / Remote quadratic result artifact cannot be read"));
        }

        return RemoteSolverReportMapping.ToSolveReport(
            slice: solved.Value,
            serialized: artifact.Value,
            variableCount: model.Variables.Count,
            modelTypes: RemoteLinearSolver.LinearModelTypes);
    }

    /// <inheritdoc cref="RemoteLinearSolver.InvokeAsync(ILinearTriadModelView, SolvingStatusCallBack, CancellationToken)"/>
    /// <param name="model">二次四元模型视图 / Quadratic tetrad model view.</param>
    /// <param name="solvingStatusCallBack">求解状态回调 / Solving status callback.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>始终返回结构化错误 / Always a structured error.</returns>
    public Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        IQuadraticTetradModelView model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) =>
        LegacyEntryUnsupported<FeasibleSolverOutput<Flt64>>();

    /// <inheritdoc cref="InvokeAsync(IQuadraticTetradModelView, SolvingStatusCallBack, CancellationToken)"/>
    /// <param name="model">二次四元模型视图 / Quadratic tetrad model view.</param>
    /// <param name="solvingStatusCallBack">求解状态回调 / Solving status callback.</param>
    /// <param name="iisConfig">IIS 配置 / IIS config.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>始终返回结构化错误 / Always a structured error.</returns>
    public Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        IQuadraticTetradModelView model,
        SolvingStatusCallBack? solvingStatusCallBack,
        IisConfig? iisConfig,
        CancellationToken cancellationToken = default) =>
        LegacyEntryUnsupported<SolverOutput>();

    /// <inheritdoc cref="InvokeAsync(IQuadraticTetradModelView, SolvingStatusCallBack, CancellationToken)"/>
    /// <param name="model">二次四元模型视图 / Quadratic tetrad model view.</param>
    /// <param name="solutionAmount">解池容量 / Solution pool capacity.</param>
    /// <param name="solvingStatusCallBack">求解状态回调 / Solving status callback.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>始终返回结构化错误 / Always a structured error.</returns>
    public Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions),
        ErrorCode, Error<ErrorCode>>> InvokeAsync(
        IQuadraticTetradModelView model,
        ulong solutionAmount,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) =>
        LegacyEntryUnsupported<
            (FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions)>();

    /// <summary>
    /// 转储交由本地委托 / Dumping is delegated to the local solver.
    /// </summary>
    /// <param name="model">二次机制模型 / Quadratic mechanism model.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>四元模型 / Tetrad model.</returns>
    public Task<Result<QuadraticTetradModel, ErrorCode, Error<ErrorCode>>> DumpAsync(
        QuadraticMechanismModel<Flt64> model,
        CancellationToken cancellationToken = default) =>
        _delegate.DumpAsync(model, cancellationToken);

    /// <inheritdoc cref="DumpAsync(QuadraticMechanismModel{Flt64}, CancellationToken)"/>
    /// <param name="model">二次元模型 / Quadratic meta model.</param>
    /// <param name="registrationStatusCallBack">注册状态回调 / Registration status callback.</param>
    /// <param name="dumpingStatusCallBack">转储状态回调 / Dumping status callback.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>机制模型 / Mechanism model.</returns>
    public Task<Result<QuadraticMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>> DumpAsync(
        QuadraticMetaModel<Flt64> model,
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
