#nullable enable

using Fuookami.Ospf.Core.Solver.Output;
using System;

namespace Fuookami.Ospf.Core.Plugin.Cplex;

/// <summary>
/// CPLEX 求解器基类 / CPLEX solver base class.
/// </summary>
public abstract class CplexSolver : IDisposable {
    private bool _disposed;
    protected SolverStatus _status;

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

    protected SolverStatus AnalyzeStatusNative() => SolverStatus.SolvingException;
}
