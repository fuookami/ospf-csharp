#nullable enable

using System;
using System.Linq;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Config;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Functional;
using Mindopt;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Plugin.Mindopt;

internal sealed class MindoptSolveCallback : MDOCallback {
    private readonly MDOModel _model;
    private readonly MDOVar[] _variables;
    private readonly object? _sourceModel;
    private readonly SolverConfig _config;
    private readonly SolvingStatusCallBack? _statusCallBack;
    private readonly ObjectCategory _category;
    private readonly NoImprovementMonitor _noImprovement;
    private Flt64? _initialBestObj;

    internal MindoptSolveCallback(
        MDOModel model,
        MDOVar[] variables,
        object? sourceModel,
        SolverConfig config,
        SolvingStatusCallBack? statusCallBack,
        ObjectCategory category) {
        _model = model;
        _variables = variables;
        _sourceModel = sourceModel;
        _config = config;
        _statusCallBack = statusCallBack;
        _category = category;
        _noImprovement = new NoImprovementMonitor(config);
    }

    internal bool Cancelled { get; private set; }
    internal bool Interrupted { get; private set; }
    internal Exception? CallbackError { get; private set; }
    internal Flt64 InitialBestObj => _initialBestObj ?? Flt64.Zero;
    internal ulong? NodeCount { get; private set; }

    internal void Cancel() {
        Cancelled = true;
        Abort();
    }

    protected override void OnCallback() {
        if (Where != MDO.Callback.MIPNODE || CallbackError is not null || Cancelled) {
            return;
        }
        try {
            Flt64 objective = new(GetDoubleInfo(MDO.Callback.MIP_OBJBST));
            Flt64 bound = new(GetDoubleInfo(MDO.Callback.MIP_OBJBND));
            var solveTime = TimeSpan.FromSeconds(_model.Get(MDO.DoubleAttr.SolverTime));
            ulong nodes = checked((ulong)System.Math.Max(0, GetIntInfo(MDO.Callback.MIP_NODCNT)));
            NodeCount = nodes;
            _initialBestObj ??= objective;
            if (_noImprovement.ShouldAbort(time: solveTime, obj: objective, bound: bound)) {
                Interrupted = true;
                Abort();
                return;
            }
            if (_statusCallBack is null) {
                return;
            }
            Flt64 gap = SolvingStatusSupport.Gap(objective, bound);
            Try callbackResult = _statusCallBack(new SolvingStatus(
                Solver: "mindopt",
                SolverIndex: 0,
                SolverConfig: _config,
                IntermediateModel: _sourceModel,
                SolverModel: _model,
                SolverCallBack: this,
                ObjectCategory: _category,
                Time: solveTime,
                Obj: objective,
                PossibleBestObj: bound,
                InitialBestObj: _initialBestObj.Value,
                Gap: gap,
                CurrentBestSolution: GetSolution(_variables)
                    .Select(value => new Flt64(value))
                    .ToArray(),
                NodeCount: nodes,
                BestBound: bound,
                MipGap: gap,
                SolveTime: solveTime));
            if (!callbackResult.IsOk) {
                Interrupted = true;
                Abort();
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            CallbackError = error;
            Interrupted = true;
            Abort();
        }
    }
}
