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
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Plugin.Scip;

/// <summary>
/// SCIP 原生事件处理器：承担取消检查与求解状态监听两项职责。
/// SCIP native event handler covering both cancellation checks and solving-status monitoring.
/// </summary>
/// <remarks>
/// 监听依赖原生事件触发，因此中断只在下一个事件点生效，不保证响应时限；事件回调要求在同一 SCIP 实例上
/// 串行求解，不宣称并发安全。/ Interruption only takes effect at the next native event, so no response-time bound is
/// claimed; the handler assumes serialized solving on one SCIP instance and makes no thread-safety claim.
/// </remarks>
internal sealed class ScipSolveEvents {
    private const int Okay = 1;
    // shortcut: 取消依赖原生事件；线程安全的直接中断可用后升级 / Cancellation waits for native events; upgrade when direct interruption is thread-safe.
    private const ulong Presolveround = 0x000020000UL;
    private const ulong NodeEvent = 0x00040000UL | 0x00080000UL | 0x00100000UL | 0x00200000UL;
    private const ulong LpEvent = 0x000800000UL | 0x001000000UL;
    private const ulong SolEvent = 0x002000000UL | 0x004000000UL;
    private const ulong Events = Presolveround | NodeEvent | LpEvent | SolEvent;
    private readonly ScipNative _native;
    private readonly CancellationToken _token;
    private readonly SolverConfig _config;
    private readonly SolvingStatusCallBack? _statusCallBack;
    private readonly ObjectCategory? _category;
    private readonly object? _sourceModel;
    private readonly IntPtr[] _variables;
    private readonly int _variableCount;
    private readonly ScipNative.EventExecution _execute;
    private readonly ScipNative.EventLifecycle _init;
    private readonly ScipNative.EventLifecycle _exit;
    private readonly NoImprovementMonitor _monitor;
    private Flt64? _initialBestObj;
    private bool _caught;
    private bool _interrupted;

    internal ScipSolveEvents(
        ScipNative native,
        CancellationToken token,
        SolverConfig config,
        ObjectCategory? category,
        IntPtr[] variables,
        int variableCount,
        SolvingStatusCallBack? statusCallBack,
        object? sourceModel) {
        _native = native;
        _token = token;
        _config = config;
        _category = category;
        _variables = variables;
        _variableCount = variableCount;
        _statusCallBack = statusCallBack;
        _sourceModel = sourceModel;
        _execute = Execute;
        _init = Init;
        _exit = Exit;
        _monitor = new NoImprovementMonitor(config);
    }

    internal bool Cancelled { get; private set; }

    /// <summary>本适配器是否成功发起过原生中断 / Whether this adapter successfully requested a native interrupt.</summary>
    internal bool Interrupted => _interrupted;
    internal Exception? CallbackError { get; private set; }

    internal bool IsActive => _token.CanBeCanceled ||
        _statusCallBack is not null ||
        _config.NotImprovementTime is not null;

    internal int Install(IntPtr scip) {
        IntPtr handler = IntPtr.Zero;
        int code = _native.IncludeEvent(
            scip: scip,
            handler: ref handler,
            name: "ospf-solve-events",
            description: "OSPF cancellation and solving status",
            execute: _execute,
            data: IntPtr.Zero);
        if (code != Okay || handler == IntPtr.Zero) {
            return code == Okay ? 0 : code;
        }
        code = _native.SetEventInit(
            scip: scip,
            handler: handler,
            callback: _init);
        return code == Okay ? _native.SetEventExit(
            scip: scip,
            handler: handler,
            callback: _exit) : code;
    }

    private int Init(IntPtr scip, IntPtr handler) {
        try {
            int code = _native.CatchGlobalEvent(
                scip: scip,
                eventMask: Events,
                handler: handler,
                data: IntPtr.Zero,
                filterPosition: IntPtr.Zero);
            if (code != Okay) {
                return code;
            }
            _caught = true;
            return ExecuteCore(scip);
        }
        catch (Exception error) {
            CallbackError = error;
            return 0;
        }
    }

    private int Exit(IntPtr scip, IntPtr handler) {
        try {
            if (!_caught) {
                return Okay;
            }
            _caught = false;
            return _native.DropGlobalEvent(
                scip: scip,
                eventMask: Events,
                handler: handler,
                data: IntPtr.Zero,
                filterPosition: -1);
        }
        catch (Exception error) {
            CallbackError = error;
            return 0;
        }
    }

    private int Execute(IntPtr scip, IntPtr handler, IntPtr eventPointer, IntPtr data) {
        try {
            return ExecuteCore(scip);
        }
        catch (Exception error) {
            CallbackError = error;
            return 0;
        }
    }

    private int ExecuteCore(IntPtr scip) {
        if (_token.IsCancellationRequested) {
            Interrupt(scip, cancelled: true);
            return Okay;
        }
        if (_statusCallBack is null && _config.NotImprovementTime is null) {
            return Okay;
        }
        int stage = _native.GetStage(scip);
        if (stage is < 1 or > 12 or 8) {
            return Okay;
        }
        IntPtr bestSolution = _native.GetBestSolution(scip);
        Flt64 currentObj = new(bestSolution != IntPtr.Zero
            ? _native.GetSolutionObjective(scip, bestSolution)
            : _native.GetPrimalBound(scip));
        Flt64 currentBound = new(_native.GetDualBound(scip));
        double seconds = _native.GetSolvingTime(scip);
        TimeSpan currentTime = double.IsFinite(seconds) && seconds >= 0
            ? TimeSpan.FromSeconds(seconds)
            : TimeSpan.Zero;
        _initialBestObj ??= currentObj;
        if (_monitor.ShouldAbort(currentTime, currentObj, currentBound)) {
            Interrupt(scip, cancelled: false);
            return Okay;
        }
        if (_statusCallBack is null) {
            return Okay;
        }
        IReadOnlyList<Flt64>? currentBestSolution = bestSolution == IntPtr.Zero
            ? null
            : Array.AsReadOnly(_variables
                .Take(_variableCount)
                .Select(variable => new Flt64(_native.GetSolutionValue(
                    scip: scip,
                    solution: bestSolution,
                    variable: variable)))
                .ToArray());
        Try callbackResult = _statusCallBack(new SolvingStatus(
            Solver: "scip",
            SolverIndex: 0,
            SolverConfig: _config,
            IntermediateModel: _sourceModel,
            SolverModel: scip,
            SolverCallBack: this,
            ObjectCategory: _category,
            Time: currentTime,
            Obj: currentObj,
            PossibleBestObj: currentBound,
            InitialBestObj: _initialBestObj!.Value,
            Gap: SolvingStatusSupport.Gap(currentObj, currentBound),
            CurrentBestSolution: currentBestSolution,
            BestBound: currentBound));
        if (SolverStatusSupportExtensions.ShouldAbortOnCallbackFailure(callbackResult, () => Interrupt(scip, cancelled: false))) {
            return Okay;
        }
        return Okay;
    }

    private void Interrupt(IntPtr scip, bool cancelled) {
        if (_interrupted) {
            return;
        }
        if (_native.InterruptSolve(scip) == Okay) {
            _interrupted = true;
            Cancelled |= cancelled;
        }
    }
}
