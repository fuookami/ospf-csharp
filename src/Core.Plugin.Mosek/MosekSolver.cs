#nullable enable

using System;
using System.Threading;
using mosek;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using MosekTask = mosek.Task;

namespace Fuookami.Ospf.Core.Plugin.Mosek;

/// <summary>
/// MOSEK 求解器抽象基类，提供环境初始化和原生状态读取。
/// MOSEK solver abstract base, providing environment initialization and native-state readback.
/// </summary>
public abstract class MosekSolver : IDisposable {
    private bool _disposed;
    private Env? _env;
    private MosekTask? _mosekModel;

    /// <summary>MOSEK 环境 / MOSEK environment.</summary>
    protected Env Env => _env ?? throw new InvalidOperationException("MOSEK Env not initialized.");
    /// <summary>MOSEK 任务 / MOSEK task.</summary>
    protected MosekTask MosekModel =>
        _mosekModel ?? throw new InvalidOperationException("MOSEK Task not initialized.");
    /// <summary>最近一次求解状态 / Latest solver status.</summary>
    protected SolverStatus Status { get; private set; } = SolverStatus.SolvingException;
    /// <summary>原生优化返回值 / Native optimization result code.</summary>
    protected rescode ResultCode { get; private set; } = rescode.ok;
    /// <summary>用于读取解的原生解类型 / Native solution type used for readback.</summary>
    protected soltype SolutionType { get; private set; } = soltype.bas;

    /// <summary>
    /// 关闭 MOSEK 任务和环境，释放资源。
    /// Close MOSEK task and environment, release native resources.
    /// </summary>
    public void Dispose() {
        if (_disposed) {
            return;
        }
        try { _mosekModel?.Dispose(); }
        catch (mosek.Exception) { }
        try { _env?.Dispose(); }
        catch (mosek.Exception) { }
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 初始化 MOSEK 求解器 / Initialize the MOSEK solver.
    /// </summary>
    /// <param name="name">任务名称 / Task name.</param>
    /// <param name="callBack">创建环境回调 / Environment-creation callback.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>初始化结果 / Initialization result.</returns>
    protected Result<Success, ErrorCode, Error<ErrorCode>> Init(
        string name,
        CreatingEnvironmentFunction? callBack,
        CancellationToken cancellationToken) {
        if (cancellationToken.IsCancellationRequested) {
            return Stopped();
        }
        try {
            _env = new Env();
            if (callBack is { } callback) {
                Result<Success, ErrorCode, Error<ErrorCode>> callbackResult = callback(Env);
                if (!callbackResult.IsOk) {
                    return callbackResult;
                }
            }
            if (cancellationToken.IsCancellationRequested) {
                return Stopped();
            }
            _mosekModel = new MosekTask(Env);
            MosekModel.puttaskname(name);
            return Results.OkInstance;
        }
        catch (System.Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            return Failure(
                MosekReportSupport.ClassifyErrorCode(error, ErrorCode.OREngineEnvironmentLost),
                $"MOSEK 环境创建失败：{error.Message} / " +
                $"MOSEK environment creation failed: {error.Message}");
        }
    }

    /// <summary>
    /// 执行 MOSEK 求解 / Execute MOSEK optimization.
    /// </summary>
    /// <returns>调用结果 / Invocation result.</returns>
    protected Result<Success, ErrorCode, Error<ErrorCode>> Solve() {
        try {
            ResultCode = MosekModel.optimize();
            return Results.OkInstance;
        }
        catch (System.Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            return Failure(
                MosekReportSupport.ClassifyErrorCode(error, ErrorCode.OREngineSolvingException),
                $"MOSEK 求解失败：{error.Message} / MOSEK solve failed: {error.Message}");
        }
    }

    /// <summary>
    /// 分析 MOSEK 原生解状态 / Analyze the native MOSEK solution status.
    /// </summary>
    /// <param name="isMip">是否为混合整数模型 / Whether the model is mixed-integer.</param>
    /// <returns>状态读取结果 / Status readback result.</returns>
    protected Result<Success, ErrorCode, Error<ErrorCode>> AnalyzeStatus(bool isMip) {
        try {
            solsta basisStatus = MosekModel.getsolsta(soltype.bas);
            if (isMip) {
                solsta integerStatus = MosekModel.getsolsta(soltype.itg);
                SolverStatus mappedIntegerStatus = Map(integerStatus);
                if (mappedIntegerStatus != SolverStatus.SolvingException) {
                    SolutionType = soltype.itg;
                    Status = mappedIntegerStatus;
                    return Results.OkInstance;
                }
            }
            SolutionType = soltype.bas;
            Status = Map(basisStatus);
            return Results.OkInstance;
        }
        catch (System.Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            Status = SolverStatus.SolvingException;
            return Failure(
                MosekReportSupport.ClassifyErrorCode(error, ErrorCode.OREngineSolvingException),
                $"MOSEK 状态读取失败：{error.Message} / " +
                $"MOSEK status readback failed: {error.Message}");
        }
    }

    private static SolverStatus Map(solsta status) => status switch {
        solsta.optimal or solsta.integer_optimal => SolverStatus.Optimal,
        solsta.prim_feas or solsta.dual_feas or solsta.prim_and_dual_feas =>
            SolverStatus.Feasible,
        solsta.prim_infeas_cer => SolverStatus.Infeasible,
        solsta.dual_infeas_cer => SolverStatus.Unbounded,
        _ => SolverStatus.SolvingException,
    };

    private static Result<Success, ErrorCode, Error<ErrorCode>> Stopped() =>
        Results.Failed<Success>(new Err<ErrorCode>(
            ErrorCode.ApplicationStopped,
            "MOSEK 初始化已取消 / MOSEK initialization was cancelled"));

    private static Result<Success, ErrorCode, Error<ErrorCode>> Failure(
        ErrorCode code,
        string message) =>
        Results.Failed<Success>(new Err<ErrorCode>(code, message));
}
