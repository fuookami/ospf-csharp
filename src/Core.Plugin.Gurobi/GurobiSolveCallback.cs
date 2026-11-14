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
using Gurobi;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Plugin.Gurobi;

/// <summary>
/// Gurobi 线性与二次求解共用的原生回调。
/// Native solve callback shared by the Gurobi linear and quadratic solvers.
/// </summary>
/// <remarks>
/// 回调运行在 Gurobi 原生线程上：取消、无改进停滞与回调返回失败都会转为原生 abort；
/// 用户回调抛出的异常被吞掉并转为中断。不保证进度事件的顺序、数量与去重。
/// The callback runs on Gurobi native threads: cancellation, a no-improvement stall and a failed callback
/// result all become a native abort; exceptions thrown by user callbacks are swallowed and interrupt the solve.
/// Event order, frequency and de-duplication are not guaranteed.
/// </remarks>
internal sealed class GurobiSolveCallback : GRBCallback {
    private readonly SolverConfig _config;
    private readonly GRBModel _model;
    private readonly GRBVar[] _vars;
    private readonly object? _intermediateModel;
    private readonly NativeCallBack? _nativeCallBack;
    private readonly SolvingStatusCallBack? _statusCallBack;
    private readonly CancellationToken _cancellationToken;
    private readonly NoImprovementMonitor _monitor;
    private Flt64? _initialBestObj;
    private IReadOnlyList<Flt64>? _bestSolution;

    internal GurobiSolveCallback(
        SolverConfig config,
        GRBModel model,
        IReadOnlyList<GRBVar> vars,
        object? intermediateModel,
        NativeCallBack? nativeCallBack,
        SolvingStatusCallBack? statusCallBack,
        CancellationToken cancellationToken) {
        _config = config;
        _model = model;
        _vars = vars.ToArray();
        _intermediateModel = intermediateModel;
        _nativeCallBack = nativeCallBack;
        _statusCallBack = statusCallBack;
        _cancellationToken = cancellationToken;
        _monitor = new NoImprovementMonitor(config);
    }

    protected override void Callback() {
        try {
            if (_cancellationToken.IsCancellationRequested) {
                Abort();
                return;
            }
            _nativeCallBack?.Invoke(this);
            if (_cancellationToken.IsCancellationRequested) {
                Abort();
                return;
            }
            if (where == GRB.Callback.MIPSOL) {
                _bestSolution = Array.AsReadOnly(GetSolution(_vars).Select(value => new Flt64(value)).ToArray());
            }
            if (where != GRB.Callback.MIP) {
                return;
            }
            var currentObj = new Flt64(GetDoubleInfo(GRB.Callback.MIP_OBJBST));
            var currentBound = new Flt64(GetDoubleInfo(GRB.Callback.MIP_OBJBND));
            double seconds = GetDoubleInfo(GRB.Callback.RUNTIME);
            TimeSpan currentTime = double.IsFinite(seconds) && seconds >= 0
                ? TimeSpan.FromSeconds(seconds)
                : TimeSpan.Zero;
            _initialBestObj ??= currentObj;
            if (_monitor.ShouldAbort(currentTime, currentObj, currentBound)) {
                Abort();
                return;
            }
            if (_statusCallBack is null) {
                return;
            }
            int sense = _model.Get(GRB.IntAttr.ModelSense);
            ObjectCategory? category = sense == GRB.MINIMIZE ? ObjectCategory.Minimum
                : sense == GRB.MAXIMIZE ? ObjectCategory.Maximum
                : null;
            Try callbackResult = _statusCallBack(new SolvingStatus(
                Solver: "gurobi",
                SolverIndex: 0,
                SolverConfig: _config,
                IntermediateModel: _intermediateModel,
                SolverModel: _model,
                SolverCallBack: this,
                ObjectCategory: category,
                Time: currentTime,
                Obj: currentObj,
                PossibleBestObj: currentBound,
                InitialBestObj: _initialBestObj!.Value,
                Gap: SolvingStatusSupport.Gap(currentObj, currentBound),
                CurrentBestSolution: _bestSolution,
                BestBound: currentBound));
            if (SolverStatusSupportExtensions.ShouldAbortOnCallbackFailure(callbackResult, Abort)) {
                return;
            }
        }
        catch (GRBException) {
            // Swallow Gurobi callback exceptions to avoid crashing the solve
        }
        catch {
            // Swallow unexpected callback exceptions
        }
    }
}
