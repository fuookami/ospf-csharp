#nullable enable

using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using mosek;
using System;

namespace Fuookami.Ospf.Core.Plugin.Mosek;

/// <summary>
/// MOSEK 求解器抽象基类，提供环境初始化和状态分析的通用实现。
/// MOSEK solver abstract base, provides common implementation for environment initialization and status analysis.
/// </summary>
public abstract class MosekSolver : IDisposable {
    private bool _disposed;
    private Env? _env;
    private Task? _mosekModel;
    private SolverStatus _status;

    /// <summary>MOSEK 环境 / MOSEK environment (lazy-initialized).</summary>
    protected Env Env => _env ?? throw new InvalidOperationException("MOSEK Env not initialized.");
    /// <summary>MOSEK 任务 / MOSEK task (lazy-initialized).</summary>
    protected Task MosekModel => _mosekModel ?? throw new InvalidOperationException("MOSEK Task not initialized.");
    /// <summary>求解状态 / Solver status.</summary>
    protected SolverStatus Status { get => _status; set => _status = value; }

    /// <summary>
    /// 关闭 MOSEK 任务和环境，释放资源。
    /// Close MOSEK task and environment, release native resources.
    /// </summary>
    public void Dispose() {
        if (_disposed) {
            return;
        }

        try { _mosekModel?.Dispose(); } catch { /* swallow on dispose */ }
        try { _env?.Dispose(); } catch { /* swallow on dispose */ }
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 初始化 MOSEK 求解器。
    /// Initialize MOSEK solver.
    ///
    /// 注意：MOSEK 环境初始化尚未完全实现，当前返回失败。
    /// Note: MOSEK environment initialization is not fully implemented yet; currently returns failure.
    /// </summary>
    protected Result<Success, ErrorCode, Error<ErrorCode>> Init(
        string name,
        CreatingEnvironmentFunction? callBack = null) {
        try {
            _env = new Env();
            if (callBack is { } cb) {
                Result<Success, ErrorCode, Error<ErrorCode>> r = cb(Env);
                if (r is Failed<Success, ErrorCode, Error<ErrorCode>> or Fatal<Success, ErrorCode, Error<ErrorCode>>) {
                    return r;
                }
            }
            _mosekModel = new Task(Env);
            MosekModel.puttaskname(name);
            return Results.OkInstance;
        }
        catch (mosek.Exception e) { return new Failed<Success, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.OREngineEnvironmentLost, e.Message)); }
        catch { return new Failed<Success, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.OREngineEnvironmentLost)); }
    }

    /// <summary>执行 MOSEK 求解 / Execute MOSEK optimize.</summary>
    protected Result<Success, ErrorCode, Error<ErrorCode>> Solve() {
        try { MosekModel.optimize(); return Results.OkInstance; }
        catch (mosek.Exception e) { return new Failed<Success, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.OREngineSolvingException, e.Message)); }
        catch { return new Failed<Success, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.OREngineTerminated)); }
    }

    /// <summary>分析 MOSEK 求解状态 / Analyze MOSEK solving status into SolverStatus.</summary>
    protected Result<Success, ErrorCode, Error<ErrorCode>> AnalyzeStatus() {
        try {
            _status = MosekModel.getsolsta(soltype.bas) switch {
                var s when s == solsta.optimal => SolverStatus.Optimal,
                var s when s == solsta.prim_and_dual_feas => SolverStatus.Feasible,
                var s when s == solsta.dual_infeas_cer => SolverStatus.Unbounded,
                var s when s == solsta.prim_infeas_cer => SolverStatus.Infeasible,
                _ => SolverStatus.SolvingException,
            };
            return Results.OkInstance;
        }
        catch (mosek.Exception e) { return new Failed<Success, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.OREngineSolvingException, e.Message)); }
        catch { return new Failed<Success, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.OREngineSolvingException)); }
    }
}
