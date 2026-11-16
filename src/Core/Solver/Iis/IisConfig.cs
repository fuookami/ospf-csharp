#nullable enable

using System;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Solver.Report;

namespace Fuookami.Ospf.Core.Solver.Iis;

/// <summary>
/// 不可行诊断运行配置，与求解器配置分离，专用于 IIS、Farkas 等诊断流程。
/// Infeasibility diagnostic runtime configuration, separate from the solver configuration and used by IIS, Farkas and
/// other diagnostic flows.
/// </summary>
/// <remarks>
/// 诊断在独立的原生实例中执行，因此时间限制与线程数默认取自本机 CPU 规模，而不是主求解配置。
/// Diagnostics run in an isolated native instance, so the time limit and thread count default from the local CPU size
/// rather than the primary solving configuration.
/// </remarks>
public record IisConfig {
    /// <summary>诊断时间限制 / Diagnostic time limit.</summary>
    public TimeSpan Time { get; init; } = TimeSpan.FromSeconds(30);
    /// <summary>线程数 / Thread count.</summary>
    public ulong ThreadCount { get; init; } = DefaultThreadCount(Environment.ProcessorCount);
    /// <summary>无改进时间限制 / No-improvement time limit.</summary>
    public TimeSpan? NotImprovementTime { get; init; }
    /// <summary>可中断时间；诊断耗时未达该值前不触发无改进中断 / Interruptible time; the no-improvement interrupt is suppressed before it.</summary>
    public TimeSpan? InterruptibleTime { get; init; }
    /// <summary>诊断进度回调 / Diagnostic progress callback.</summary>
    public IisComputingStatusCallBack? ComputingStatusCallBack { get; init; }
    /// <summary>割与松弛判定容差 / Cut and slack tolerance.</summary>
    public Flt64 SlackTolerance { get; init; } = new(1e-6);
    /// <summary>可审计后端配置 / Auditable backend configuration.</summary>
    public IBackendConfiguration? BackendConfiguration { get; init; }

    /// <summary>原生整数线程数，超出范围时为 null / Thread count as a native integer, null when it is out of range.</summary>
    public int? NativeThreadCount => ThreadCount is >= 1 and <= int.MaxValue ? (int)ThreadCount : null;

    /// <summary>校验诊断参数是否可安全下发到原生求解器 / Validate that the diagnostic parameters can be applied to a native solver.</summary>
    /// <returns>校验结果 / Validation result.</returns>
    public Result<Success, ErrorCode, Error<ErrorCode>> ValidateRuntime() {
        if (Time < TimeSpan.Zero) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "诊断时间限制不能为负 / The diagnostic time limit cannot be negative"));
        }
        if (NativeThreadCount is null) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                $"线程数超出原生整数范围：{ThreadCount} / Thread count exceeds the native integer range: {ThreadCount}"));
        }
        if (NotImprovementTime is { } notImprovementTime && notImprovementTime < TimeSpan.Zero) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "无改进时间限制不能为负 / The no-improvement time limit cannot be negative"));
        }
        if (InterruptibleTime is { } interruptibleTime && interruptibleTime < TimeSpan.Zero) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "可中断时间不能为负 / The interruptible time cannot be negative"));
        }
        double tolerance = SlackTolerance.ToDouble();
        if (!double.IsFinite(tolerance) || tolerance < 0) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "松弛容差必须是非负的有限数 / The slack tolerance must be a non-negative finite number"));
        }
        return Results.OkInstance;
    }

    /// <summary>按 Kotlin CPU 规模启发式推导默认线程数 / Derive the default thread count from the Kotlin CPU-size heuristic.</summary>
    /// <param name="processorCount">可用处理器数 / Available processor count.</param>
    /// <returns>默认线程数 / Default thread count.</returns>
    private static ulong DefaultThreadCount(int processorCount) =>
        processorCount <= 16 ? (ulong)processorCount :
        processorCount < 24 ? 16UL :
        processorCount < 32 ? 24UL : 32UL;
}
