#nullable enable

using System;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Solver.Config;
using Fuookami.Ospf.Core.Solver.Output;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Solver;
/// <summary>
/// 求解器状态支持扩展方法，提供状态到错误码的映射和失败结果构建。
/// Solver status support extension methods, providing status-to-error-code mapping and failure result construction.
/// </summary>
public static class SolverStatusSupportExtensions {
    /// <summary>
    /// 将求解器状态解析为错误码，使用现有的 ErrCode 扩展方法，若为空则回退到指定错误码或 OREngineSolvingException。
    /// Resolve solver status to error code using the existing ErrCode extension, falling back to the specified code or OREngineSolvingException.
    /// </summary>
    /// <param name="status">求解器状态 / Solver status</param>
    /// <param name="fallback">回退错误码（可选）/ Fallback error code (optional)</param>
    /// <returns>解析后的错误码 / Resolved error code</returns>
    public static ErrorCode ResolveErrCode(this SolverStatus status, ErrorCode? fallback = null) {
        return status.ErrCode() ?? fallback ?? ErrorCode.OREngineSolvingException;
    }

    /// <summary>
    /// 根据求解器状态返回失败的 Try 结果。
    /// Return a failed Try result based on the solver status.
    /// </summary>
    /// <param name="status">求解器状态 / Solver status</param>
    /// <param name="fallback">回退错误码（可选）/ Fallback error code (optional)</param>
    /// <returns>失败的 Try 结果 / Failed Try result</returns>
    public static Try FailByStatus(this SolverStatus status, ErrorCode? fallback = null) {
        ErrorCode code = status.ResolveErrCode(fallback);
        return Results.Failed<Success>(new Err<ErrorCode>(code, code.ToReadableString()));
    }

    /// <summary>
    /// 检查回调结果是否失败，若失败则执行中止操作并返回 true。
    /// Check whether the callback result failed; if so, execute the abort action and return true.
    /// </summary>
    /// <param name="callbackResult">回调结果（可选）/ Callback result (optional)</param>
    /// <param name="abort">中止操作 / Abort action</param>
    /// <returns>是否应中止 / Whether should abort</returns>
    public static bool ShouldAbortOnCallbackFailure(Try? callbackResult, Action abort) {
        if (callbackResult is Failed<Success, ErrorCode, Error<ErrorCode>>) {
            abort();
            return true;
        }
        return false;
    }
}

/// <summary>
/// 求解状态回调共享的数值契约 / Shared numeric contracts used by solving-status callbacks.
/// </summary>
public static class SolvingStatusSupport {
    private static readonly INumericConstants<Flt64> _flt64Constants = NumericConstantsRegistry.For<Flt64>();

    /// <summary>
    /// 按共享契约计算回调间隙 <code>(obj - bound + precision) / (obj + precision)</code>。
    /// Compute the callback gap <code>(obj - bound + precision) / (obj + precision)</code>.
    /// </summary>
    /// <param name="obj">当前目标值 / Current objective value.</param>
    /// <param name="bound">当前最优界 / Current best bound.</param>
    /// <returns>回调间隙 / Callback gap.</returns>
    public static Flt64 Gap(Flt64 obj, Flt64 bound) {
        Flt64 precision = _flt64Constants.DecimalPrecision!.Value;
        return obj.Minus(bound).Plus(precision).Div(obj.Plus(precision));
    }
}

/// <summary>
/// 无改进中断监视器：按配置的阈值判断目标与界是否长时间静止。
/// No-improvement monitor: decides whether objective and bound have been stale for long enough to interrupt.
/// </summary>
/// <remarks>
/// 仅在求解回调内按顺序调用，不保证线程安全。/ Invoked sequentially from the solving callback only; not thread safe.
/// </remarks>
public sealed class NoImprovementMonitor {
    private readonly SolverConfig _config;
    private Flt64? _bestObj;
    private Flt64? _bestBound;
    private TimeSpan _bestTime;

    /// <summary>构造监视器 / Create a monitor.</summary>
    /// <param name="config">求解器配置 / Solver configuration.</param>
    public NoImprovementMonitor(SolverConfig config) {
        _config = config;
    }

    /// <summary>记录本次进度并判断是否应当中断求解 / Record the progress and decide whether the solve must be interrupted.</summary>
    /// <param name="time">当前求解时间 / Current solving time.</param>
    /// <param name="obj">当前目标值 / Current objective value.</param>
    /// <param name="bound">当前最优界 / Current best bound.</param>
    /// <returns>是否应当中断 / Whether to interrupt.</returns>
    public bool ShouldAbort(TimeSpan time, Flt64 obj, Flt64 bound) {
        if (_config.NotImprovementTime is not { } notImprovementTime) {
            return false;
        }
        Flt64 threshold = _config.ImproveThreshold;
        if (_bestObj is not { } previousObj
            || _bestBound is not { } previousBound
            || obj.Minus(previousObj).Abs() >= threshold
            || bound.Minus(previousBound).Abs() >= threshold) {
            _bestObj = obj;
            _bestBound = bound;
            _bestTime = time;
            return false;
        }
        if (time - _bestTime < notImprovementTime) {
            return false;
        }
        if (_config.InterruptibleTime is { } interruptibleTime && time < interruptibleTime) {
            return false;
        }
        if (_config.InterruptibleGap is { } interruptibleGap && obj.Minus(bound).Abs() >= interruptibleGap) {
            return false;
        }
        return true;
    }
}
