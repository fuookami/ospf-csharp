#nullable enable

using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Core.Plugin.Copt;

/// <summary>
/// COPT 求解器基类 / COPT solver base class.
/// </summary>
public abstract class CoptSolver : IDisposable {
    private bool _disposed;
    private SolverStatus _status;

    protected SolverStatus Status { get => _status; set => _status = value; }

    public void Dispose() {
        if (_disposed) {
            return;
        }

        DisposeNative();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    protected virtual void DisposeNative() { }

    protected async Task<Result<Success, ErrorCode, Error<ErrorCode>>> InitAsync(string name) {
        try {
            InitNative(name);
            return Results.OkInstance;
        }
        catch (Exception e) {
            return new Failed<Success, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.OREngineEnvironmentLost, e.Message));
        }
        finally { await Task.CompletedTask.ConfigureAwait(false); }
    }

    protected virtual void InitNative(string name) { }

    protected async Task<Result<Success, ErrorCode, Error<ErrorCode>>> SolveAsync() {
        try {
            SolveNative();
            return Results.OkInstance;
        }
        catch (Exception e) {
            return new Failed<Success, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.OREngineSolvingException, e.Message));
        }
        finally { await Task.CompletedTask.ConfigureAwait(false); }
    }

    protected virtual void SolveNative() { }

    protected async Task<Result<Success, ErrorCode, Error<ErrorCode>>> AnalyzeStatusAsync() {
        try {
            _status = AnalyzeStatusNative();
            return Results.OkInstance;
        }
        catch (Exception e) {
            return new Failed<Success, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.OREngineSolvingException, e.Message));
        }
        finally { await Task.CompletedTask.ConfigureAwait(false); }
    }

    protected virtual SolverStatus AnalyzeStatusNative() => SolverStatus.SolvingException;
}
