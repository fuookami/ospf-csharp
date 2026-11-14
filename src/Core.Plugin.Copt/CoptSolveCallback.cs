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
using CoptSdk = Copt;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Plugin.Copt;

/// <summary>
/// COPT 求解回调：负责取消、无改进中断和统一状态通知。
/// COPT solve callback for cancellation, no-improvement interruption, and unified status notifications.
/// </summary>
internal sealed class CoptSolveCallback : CoptSdk.CallbackBase {
    private readonly CoptSdk.Model _model;
    private readonly IReadOnlyList<CoptSdk.Var> _variables;
    private readonly object? _sourceModel;
    private readonly SolverConfig _config;
    private readonly SolvingStatusCallBack? _statusCallBack;
    private readonly CancellationToken _cancellationToken;
    private readonly NoImprovementMonitor _monitor;
    private Flt64? _initialBestObj;

    internal CoptSolveCallback(
        CoptSdk.Model model,
        IReadOnlyList<CoptSdk.Var> variables,
        object? sourceModel,
        SolverConfig config,
        SolvingStatusCallBack? statusCallBack,
        CancellationToken cancellationToken) {
        _model = model;
        _variables = variables;
        _sourceModel = sourceModel;
        _config = config;
        _statusCallBack = statusCallBack;
        _cancellationToken = cancellationToken;
        _monitor = new NoImprovementMonitor(config);
    }

    /// <summary>是否因调用方取消而中断 / Whether interruption was caused by caller cancellation.</summary>
    internal bool Cancelled { get; private set; }

    /// <summary>是否发起了无改进或回调失败中断 / Whether a no-improvement or callback-failure interrupt was raised.</summary>
    internal bool Interrupted { get; private set; }

    /// <summary>回调失败详情 / Callback failure details.</summary>
    internal Exception? CallbackError { get; private set; }

    /// <inheritdoc/>
    public override void callback() {
        try {
            if (_cancellationToken.IsCancellationRequested) {
                Interrupt(cancelled: true);
                return;
            }
            double objective = GetDblInfo("BestObj");
            double bound = GetDblInfo("BestBnd");
            double seconds = _model.GetDblAttr("SolvingTime");
            TimeSpan currentTime = double.IsFinite(seconds) && seconds >= 0
                ? TimeSpan.FromSeconds(seconds)
                : TimeSpan.Zero;
            Flt64 currentObjective = Finite(objective) ? new Flt64(objective) : Flt64.Zero;
            Flt64 currentBound = Finite(bound) ? new Flt64(bound) : Flt64.Zero;
            _initialBestObj ??= currentObjective;

            if (Finite(objective)
                && Finite(bound)
                && _monitor.ShouldAbort(
                    time: currentTime,
                    obj: currentObjective,
                    bound: currentBound)) {
                Interrupt(cancelled: false);
                return;
            }
            if (_statusCallBack is null) {
                return;
            }
            bool hasIncumbent = GetIntInfo("HasIncumbent") != 0;
            IReadOnlyList<Flt64>? values = hasIncumbent
                ? Array.AsReadOnly(GetIncumbent(_variables.ToArray())
                    .Select(value => new Flt64(value))
                    .ToArray())
                : null;
            int sense = _model.GetIntAttr("ObjSense");
            ObjectCategory? category = sense == 1
                ? ObjectCategory.Minimum
                : sense == -1
                    ? ObjectCategory.Maximum
                    : null;
            Try callbackResult = _statusCallBack(new SolvingStatus(
                Solver: "copt",
                SolverIndex: 0,
                SolverConfig: _config,
                IntermediateModel: _sourceModel,
                SolverModel: _model,
                SolverCallBack: this,
                ObjectCategory: category,
                Time: currentTime,
                Obj: currentObjective,
                PossibleBestObj: currentBound,
                InitialBestObj: _initialBestObj.Value,
                Gap: SolvingStatusSupport.Gap(currentObjective, currentBound),
                CurrentBestSolution: values,
                BestBound: currentBound));
            if (SolverStatusSupportExtensions.ShouldAbortOnCallbackFailure(
                    callbackResult,
                    () => Interrupt(cancelled: false))) {
                return;
            }
            if (_cancellationToken.IsCancellationRequested) {
                Interrupt(cancelled: true);
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            CallbackError = error;
            Interrupt(cancelled: false);
        }
    }

    private void Interrupt(bool cancelled) {
        if (Interrupted || Cancelled) {
            return;
        }
        try {
            Interrupt();
            Cancelled = cancelled;
            Interrupted = !cancelled;
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            CallbackError ??= error;
        }
    }

    private static bool Finite(double value) =>
        double.IsFinite(value) && System.Math.Abs(value) < 1e30;
}
