#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Config;
using Fuookami.Ospf.Core.Solver.Output;
using ILOG.Concert;
using CplexSdk = ILOG.CPLEX.Cplex;
using Exception = System.Exception;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Plugin.Cplex;

internal sealed class CplexCallbackCore {
    private readonly object? _sourceModel;
    private readonly object _solverModel;
    private readonly SolverConfig _config;
    private readonly SolvingStatusCallBack? _statusCallBack;
    private readonly CancellationToken _cancellationToken;
    private readonly ObjectCategory _category;
    private readonly NoImprovementMonitor _monitor;
    private Flt64? _initialBestObj;

    internal CplexCallbackCore(
        object solverModel,
        object? sourceModel,
        SolverConfig config,
        SolvingStatusCallBack? statusCallBack,
        CancellationToken cancellationToken,
        ObjectCategory category) {
        _solverModel = solverModel;
        _sourceModel = sourceModel;
        _config = config;
        _statusCallBack = statusCallBack;
        _cancellationToken = cancellationToken;
        _category = category;
        _monitor = new NoImprovementMonitor(config);
    }

    internal bool Cancelled { get; private set; }
    internal bool Interrupted { get; private set; }
    internal Exception? CallbackError { get; private set; }

    internal bool Observe(
        double objective,
        double bound,
        double seconds,
        Func<IReadOnlyList<Flt64>?> incumbent,
        Action abort) {
        try {
            if (_cancellationToken.IsCancellationRequested) {
                Stop(cancelled: true, abort);
                return true;
            }
            TimeSpan currentTime = double.IsFinite(seconds) && seconds >= 0
                ? TimeSpan.FromSeconds(seconds)
                : TimeSpan.Zero;
            Flt64 currentObjective = Finite(objective) ? new Flt64(objective) : Flt64.Zero;
            Flt64 currentBound = Finite(bound) ? new Flt64(bound) : currentObjective;
            _initialBestObj ??= currentObjective;

            if (Finite(objective)
                && Finite(bound)
                && _monitor.ShouldAbort(
                    time: currentTime,
                    obj: currentObjective,
                    bound: currentBound)) {
                Stop(cancelled: false, abort);
                return true;
            }
            if (_statusCallBack is null) {
                return false;
            }
            Try callbackResult = _statusCallBack(new SolvingStatus(
                Solver: "cplex",
                SolverIndex: 0,
                SolverConfig: _config,
                IntermediateModel: _sourceModel,
                SolverModel: _solverModel,
                SolverCallBack: this,
                ObjectCategory: _category,
                Time: currentTime,
                Obj: currentObjective,
                PossibleBestObj: currentBound,
                InitialBestObj: _initialBestObj.Value,
                Gap: SolvingStatusSupport.Gap(currentObjective, currentBound),
                CurrentBestSolution: incumbent(),
                BestBound: currentBound));
            return SolverStatusSupportExtensions.ShouldAbortOnCallbackFailure(
                callbackResult,
                () => Stop(cancelled: false, abort));
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            CallbackError = error;
            Stop(cancelled: false, abort);
            return true;
        }
    }

    private void Stop(bool cancelled, Action abort) {
        if (Interrupted || Cancelled) {
            return;
        }
        try {
            abort();
            Cancelled = cancelled;
            Interrupted = !cancelled;
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            CallbackError ??= error;
        }
    }

    private static bool Finite(double value) =>
        double.IsFinite(value) && System.Math.Abs(value) < 1e20;
}

internal sealed class CplexSolveCallback : CplexSdk.MIPInfoCallback {
    private readonly INumVar[] _variables;
    private readonly CplexCallbackCore _core;

    internal CplexSolveCallback(
        object solverModel,
        INumVar[] variables,
        object? sourceModel,
        SolverConfig config,
        SolvingStatusCallBack? statusCallBack,
        CancellationToken cancellationToken,
        ObjectCategory category) {
        _variables = variables;
        _core = new CplexCallbackCore(
            solverModel: solverModel,
            sourceModel: sourceModel,
            config: config,
            statusCallBack: statusCallBack,
            cancellationToken: cancellationToken,
            category: category);
    }

    internal bool Cancelled => _core.Cancelled;
    internal bool Interrupted => _core.Interrupted;
    internal Exception? CallbackError => _core.CallbackError;

    public override void Main() {
        double objective = HasIncumbent() ? GetIncumbentObjValue() : GetBestObjValue();
        _core.Observe(
            objective: objective,
            bound: GetBestObjValue(),
            seconds: GetCplexTime(),
            incumbent: () => HasIncumbent()
                ? Array.AsReadOnly(GetIncumbentValues(_variables)
                    .Select(value => new Flt64(value))
                    .ToArray())
                : null,
            abort: Abort);
    }
}

internal sealed class CplexContinuousCallback : CplexSdk.ContinuousCallback {
    private readonly CplexCallbackCore _core;

    internal CplexContinuousCallback(
        object solverModel,
        object? sourceModel,
        SolverConfig config,
        SolvingStatusCallBack? statusCallBack,
        CancellationToken cancellationToken,
        ObjectCategory category) {
        _core = new CplexCallbackCore(
            solverModel: solverModel,
            sourceModel: sourceModel,
            config: config,
            statusCallBack: statusCallBack,
            cancellationToken: cancellationToken,
            category: category);
    }

    internal bool Cancelled => _core.Cancelled;
    internal bool Interrupted => _core.Interrupted;
    internal Exception? CallbackError => _core.CallbackError;

    public override void Main() {
        double objective = GetObjValue();
        _core.Observe(
            objective: objective,
            bound: objective,
            seconds: GetCplexTime(),
            incumbent: () => null,
            abort: Abort);
    }
}
