#nullable enable

using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Hexaly.Optimizer;
using System;

namespace Fuookami.Ospf.Core.Plugin.Hexaly;

/// <summary>
/// Hexaly 求解器抽象基类，提供环境初始化、求解和状态分析的通用实现。
/// Hexaly solver abstract base, provides common implementation for environment initialization, solving, and status analysis.
/// </summary>
public abstract class HexalySolver : IDisposable {
    private bool _disposed;
    private HexalyOptimizer? _optimizer;
    private HxModel? _hexalyModel;
    private HxSolution? _hexalySolution;
    private SolverStatus _status;
    private DateTime? _beginTime;
    private TimeSpan? _solvingTime;

    /// <summary>Hexaly 优化器 / Hexaly optimizer (lazy-initialized).</summary>
    protected HexalyOptimizer Optimizer => _optimizer ?? throw new InvalidOperationException("HexalyOptimizer not initialized.");
    /// <summary>Hexaly 模型 / Hexaly model (lazy-initialized).</summary>
    protected HxModel HexalyModel => _hexalyModel ?? throw new InvalidOperationException("HxModel not initialized.");
    /// <summary>Hexaly 解 / Hexaly solution.</summary>
    protected HxSolution HexalySolution {
        get => _hexalySolution ?? throw new InvalidOperationException("HxSolution not initialized.");
        set => _hexalySolution = value;
    }
    /// <summary>求解状态 / Solver status.</summary>
    protected SolverStatus Status { get => _status; set => _status = value; }
    /// <summary>求解开始时间 / Solving begin time.</summary>
    protected DateTime? BeginTime { get => _beginTime; set => _beginTime = value; }
    /// <summary>求解耗时 / Solving duration.</summary>
    protected TimeSpan? SolvingTime { get => _solvingTime; set => _solvingTime = value; }

    /// <summary>
    /// 关闭 Hexaly 模型和优化器，释放资源。
    /// Close Hexaly model and optimizer, release native resources.
    /// </summary>
    public void Dispose() {
        if (_disposed) {
            return;
        }

        // HxModel is not IDisposable; optimizer.Dispose() handles cleanup
        try { _optimizer?.Dispose(); } catch { /* swallow on dispose */ }
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 初始化 Hexaly 优化器。
    /// Initialize Hexaly optimizer.
    /// </summary>
    /// <param name="name">模型名称 / Model name.</param>
    /// <param name="callBack">创建环境回调函数 / Creating environment callback.</param>
    /// <returns>操作结果 / Operation result.</returns>
    protected Result<Success, ErrorCode, Error<ErrorCode>> Init(
        string name,
        CreatingEnvironmentFunction? callBack = null) {
        try {
            _optimizer = new HexalyOptimizer();
            if (callBack is { } cb) {
                Result<Success, ErrorCode, Error<ErrorCode>> r = cb(Optimizer);
                if (r is Failed<Success, ErrorCode, Error<ErrorCode>> or Fatal<Success, ErrorCode, Error<ErrorCode>>) {
                    return r;
                }
            }
            _hexalyModel = Optimizer.GetModel();
            return Results.OkInstance;
        }
        catch (HxException e) { return new Failed<Success, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.OREngineEnvironmentLost, e.Message)); }
        catch { return new Failed<Success, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.OREngineEnvironmentLost)); }
    }

    /// <summary>执行 Hexaly 求解 / Execute Hexaly solve.</summary>
    protected Result<Success, ErrorCode, Error<ErrorCode>> Solve() {
        try {
            _beginTime = DateTime.UtcNow;
            HexalyModel.Close();
            Optimizer.Solve();
            _solvingTime = DateTime.UtcNow - _beginTime.Value;
            return Results.OkInstance;
        }
        catch (HxException e) { return new Failed<Success, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.OREngineSolvingException, e.Message)); }
        catch { return new Failed<Success, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.OREngineTerminated)); }
    }

    /// <summary>分析 Hexaly 求解状态 / Analyze Hexaly solving status into SolverStatus.</summary>
    protected Result<Success, ErrorCode, Error<ErrorCode>> AnalyzeStatus() {
        try {
            _hexalySolution = Optimizer.GetSolution();
            _status = HexalySolution.GetStatus() switch {
                var s when s == HxSolutionStatus.Optimal => SolverStatus.Optimal,
                var s when s == HxSolutionStatus.Feasible => SolverStatus.Feasible,
                var s when s == HxSolutionStatus.Infeasible => SolverStatus.Infeasible,
                var s when s == HxSolutionStatus.Inconsistent => SolverStatus.Unbounded,
                _ => SolverStatus.SolvingException,
            };
            return Results.OkInstance;
        }
        catch (HxException e) { return new Failed<Success, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.OREngineSolvingException, e.Message)); }
        catch { return new Failed<Success, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.OREngineSolvingException)); }
    }
}
