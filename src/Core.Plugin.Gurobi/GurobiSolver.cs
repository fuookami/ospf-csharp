#nullable enable

using System;
using System.Threading.Tasks;
using Gurobi;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Plugin.Gurobi;

/// <summary>
/// Gurobi 求解器抽象基类，提供环境初始化、求解和状态分析的通用实现。
/// Gurobi solver abstract base, owns the native GRBEnv/GRBModel lifecycle.
/// </summary>
public abstract class GurobiSolver : IDisposable
{
    private bool _disposed;
    private GRBEnv? _env;
    private GRBModel? _grbModel;
    private SolverStatus _status;

    /// <summary>Gurobi 环境 / Gurobi environment (lazy-initialized).</summary>
    protected GRBEnv Env => _env ?? throw new InvalidOperationException("GRBEnv not initialized.");
    /// <summary>Gurobi 模型 / Gurobi model (lazy-initialized).</summary>
    protected GRBModel GrbModel => _grbModel ?? throw new InvalidOperationException("GRBModel not initialized.");
    /// <summary>求解状态 / Solver status.</summary>
    protected SolverStatus Status { get => _status; set => _status = value; }

    /// <summary>
    /// 关闭 Gurobi 模型和环境，释放资源。
    /// Close Gurobi model and environment, release native resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        try { _grbModel?.Dispose(); } catch { /* swallow on dispose */ }
        try { _env?.Dispose(); } catch { /* swallow on dispose */ }
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 使用远程服务器初始化 Gurobi 环境和模型。
    /// Initialize Gurobi environment and model using a remote compute server.
    /// </summary>
    protected async Task<Result<Success, ErrorCode, Error<ErrorCode>>> InitAsync(
        string server, string password, TimeSpan connectionTime, string name,
        CreatingEnvironmentFunction? callBack = null)
    {
        try
        {
            _env = new GRBEnv(true);
            Env.Set(GRB.IntParam.ServerTimeout, (int)connectionTime.TotalSeconds);
            Env.Set(GRB.DoubleParam.CSQueueTimeout, connectionTime.TotalSeconds);
            Env.Set(GRB.StringParam.ComputeServer, server);
            Env.Set(GRB.StringParam.ServerPassword, password);
            if (callBack is { } cb)
            {
                var r = cb(Env);
                if (r is Failed<Success, ErrorCode, Error<ErrorCode>> or Fatal<Success, ErrorCode, Error<ErrorCode>>) return r;
            }
            Env.Start();
            _grbModel = new GRBModel(Env);
            GrbModel.Set(GRB.StringAttr.ModelName, name);
            return Results.OkInstance;
        }
        catch (GRBException e) { return new Failed<Success, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.OREngineEnvironmentLost, e.Message)); }
        catch { return new Failed<Success, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.OREngineEnvironmentLost)); }
        finally { await Task.CompletedTask.ConfigureAwait(false); }
    }

    /// <summary>
    /// 使用本地环境初始化 Gurobi 模型。
    /// Initialize Gurobi model using a local environment.
    /// </summary>
    protected async Task<Result<Success, ErrorCode, Error<ErrorCode>>> InitAsync(string name, CreatingEnvironmentFunction? callBack = null)
    {
        try
        {
            _env = new GRBEnv();
            if (callBack is { } cb)
            {
                var r = cb(Env);
                if (r is Failed<Success, ErrorCode, Error<ErrorCode>> or Fatal<Success, ErrorCode, Error<ErrorCode>>) return r;
            }
            _grbModel = new GRBModel(Env);
            GrbModel.Set(GRB.StringAttr.ModelName, name);
            return Results.OkInstance;
        }
        catch (GRBException e) { return new Failed<Success, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.OREngineEnvironmentLost, e.Message)); }
        catch { return new Failed<Success, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.OREngineEnvironmentLost)); }
        finally { await Task.CompletedTask.ConfigureAwait(false); }
    }

    /// <summary>执行 Gurobi 求解 / Execute Gurobi optimize.</summary>
    protected async Task<Result<Success, ErrorCode, Error<ErrorCode>>> SolveAsync()
    {
        try { GrbModel.Optimize(); return Results.OkInstance; }
        catch (GRBException e) { return new Failed<Success, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.OREngineSolvingException, e.Message)); }
        catch { return new Failed<Success, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.OREngineTerminated)); }
        finally { await Task.CompletedTask.ConfigureAwait(false); }
    }

    /// <summary>分析 Gurobi 求解状态 / Analyze Gurobi status into SolverStatus.</summary>
    protected async Task<Result<Success, ErrorCode, Error<ErrorCode>>> AnalyzeStatusAsync()
    {
        try
        {
            _status = GrbModel.Get(GRB.IntAttr.Status) switch
            {
                var s when s == GRB.Status.OPTIMAL => SolverStatus.Optimal,
                var s when s == GRB.Status.INFEASIBLE => SolverStatus.Infeasible,
                var s when s == GRB.Status.UNBOUNDED => SolverStatus.Unbounded,
                var s when s == GRB.Status.INF_OR_UNBD => SolverStatus.InfeasibleOrUnbounded,
                _ => GrbModel.Get(GRB.IntAttr.SolCount) > 0
                    ? SolverStatus.Feasible
                    : SolverStatus.SolvingException,
            };
            return Results.OkInstance;
        }
        catch (GRBException e) { return new Failed<Success, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.OREngineSolvingException, e.Message)); }
        catch { return new Failed<Success, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.OREngineSolvingException)); }
        finally { await Task.CompletedTask.ConfigureAwait(false); }
    }
}
