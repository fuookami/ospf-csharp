#nullable enable

using System;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Solver.Report;

namespace Fuookami.Ospf.Core.Solver.Config;

/// <summary>
/// 求解器通用配置，包含时间限制、线程数、间隙容忍度等参数。
/// Common solver configuration including time limit, thread count and gap tolerance.
/// </summary>
/// <remarks>
/// 所有求解器共享同一份运行配置；供应商专有参数通过 <see cref="BackendConfiguration"/> 承载。
/// All solvers share one runtime configuration; vendor-specific values travel in <see cref="BackendConfiguration"/>.
/// 转储标志为空表示“未指定”，由下游流程决定默认值。 / An empty dump flag means "unspecified" and leaves the default to the consuming stage.
/// </remarks>
public record SolverConfig {
    private static readonly INumericConstants<Flt64> _flt64Constants = NumericConstantsRegistry.For<Flt64>();

    /// <summary>求解时间限制 / Solve time limit.</summary>
    public TimeSpan TimeLimit { get; init; } = TimeSpan.FromSeconds(30);
    /// <summary>线程数 / Thread count.</summary>
    public ulong ThreadCount { get; init; } = DefaultThreadCount(Environment.ProcessorCount);
    /// <summary>间隙容忍度 / Gap tolerance.</summary>
    public Flt64 Gap { get; init; } = Flt64.Zero;
    /// <summary>无改进时间限制 / No-improvement time limit.</summary>
    public TimeSpan? NotImprovementTime { get; init; }
    /// <summary>改进阈值 / Improvement threshold.</summary>
    public Flt64 ImproveThreshold { get; init; } = _flt64Constants.DecimalPrecision!.Value;
    /// <summary>可中断时间；求解时间未达该值前不触发无改进中断 / Interruptible time; the no-improvement interrupt is suppressed before it.</summary>
    public TimeSpan? InterruptibleTime { get; init; }
    /// <summary>可中断间隙；当前间隙不小于该值时不触发无改进中断 / Interruptible gap; the no-improvement interrupt is suppressed while the gap is at least this.</summary>
    public Flt64? InterruptibleGap { get; init; }
    /// <summary>是否并发转储机制模型 / Dump the mechanism model concurrently.</summary>
    public bool? DumpMechanismModelConcurrent { get; init; }
    /// <summary>是否阻塞转储机制模型 / Dump the mechanism model blocking.</summary>
    public bool? DumpMechanismModelBlocking { get; init; }
    /// <summary>是否并发转储中间模型 / Dump the intermediate model concurrently.</summary>
    public bool? DumpIntermediateModelConcurrent { get; init; }
    /// <summary>是否转储中间模型边界 / Dump intermediate model bounds.</summary>
    public bool? DumpIntermediateModelBounds { get; init; }
    /// <summary>是否强制转储中间模型边界 / Force dumping intermediate model bounds.</summary>
    public bool? DumpIntermediateModelForceBounds { get; init; }
    /// <summary>
    /// **函数符号展开策略** / The function-symbol expansion policy.
    /// </summary>
    /// <remarks>
    /// 对齐基线 `CoptLinearSolver.kt` 的判断。
    /// 配置上的与模型上的；<b>任意一个是 `EAGER`，就直接走常规求解</b>，不做延迟物化。**
    ///
    /// **"任一为 Eager 就短路"不是偷懒，而是"两个开关里更保守的那个说了算"：**
    /// **配置说"立刻展开"时，某个模型说自己可以延迟，也不能推翻配置——
    /// 否则"全局关掉延迟"这个动作在个别模型上会失效。**
    ///
    /// **默认 `Eager`，与模型侧一致**：**没配置过的求解器不会突然走上延迟物化那条更复杂的路径。**
    ///
    /// **这里只加了这个配置项；它还没有被任何求解入口读取**——
    /// **接线需要一条从 `ILinearTriadModelView` 拿到策略的通路，而那个接口目前没有这个成员。**
    /// **这一点写在这里而不是留白，因为它决定了后续的起点。**
    ///
    /// **follows the baseline's `CoptLinearSolver.kt`: the baseline reads <b>two</b> policies at the solve entry, one
    /// from the config and one from the model, and if <b>either</b> is `EAGER` it takes the ordinary solve with no deferred
    /// materialisation.**
    ///
    /// **"Either Eager short-circuits" is not laziness but "the more conservative of the two switches wins": when the config
    /// says expand immediately, a model claiming it could defer must not override it — otherwise "turn deferring off
    /// globally" would silently fail on individual models.**
    ///
    /// **The default is `Eager`, matching the model side: an unconfigured solver never drifts into the more complicated
    /// materialisation route.**
    ///
 /// ** adds the config item only; no solve entry reads it yet — wiring needs a path from
    /// `ILinearTriadModelView` to the policy, and that interface has no such member.** **Recorded here rather than left
    /// blank, because it determines where the next step starts.**
    /// </remarks>
    public FunctionExpansionPolicy FunctionExpansionPolicy { get; init; } = FunctionExpansionPolicy.Eager;

    /// <summary>可审计后端配置 / Auditable backend configuration.</summary>
    public IBackendConfiguration? BackendConfiguration { get; init; }

    /// <summary>原生整数线程数，超出范围时为 null / Thread count as a native integer, null when it is out of range.</summary>
    public int? NativeThreadCount => ThreadCount is >= 1 and <= int.MaxValue ? (int)ThreadCount : null;

    /// <summary>校验运行参数是否可安全下发到原生求解器 / Validate that the runtime parameters can be applied to a native solver.</summary>
    /// <returns>校验结果 / Validation result.</returns>
    public Result<Success, ErrorCode, Error<ErrorCode>> ValidateRuntime() {
        if (TimeLimit < TimeSpan.Zero) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "时间限制不能为负 / The time limit cannot be negative"));
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
        foreach ((Flt64 value, string name) in new[] {
            (Gap, "gap"),
            (ImproveThreshold, "improveThreshold"),
        }) {
            double number = value.ToDouble();
            if (!double.IsFinite(number) || number < 0) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    $"配置项 {name} 必须是非负的有限数 / Configuration {name} must be a non-negative finite number"));
            }
        }
        if (InterruptibleGap is { } interruptibleGap) {
            double number = interruptibleGap.ToDouble();
            if (!double.IsFinite(number) || number < 0) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "可中断间隙必须是非负的有限数 / The interruptible gap must be a non-negative finite number"));
            }
        }
        return Results.OkInstance;
    }

    /// <summary>按 Kotlin 基线的 CPU 规模启发式推导默认线程数 / Derive the default thread count from the Kotlin CPU-size heuristic.</summary>
    /// <param name="processorCount">可用处理器数 / Available processor count.</param>
    /// <returns>默认线程数 / Default thread count.</returns>
    private static ulong DefaultThreadCount(int processorCount) =>
        processorCount <= 16 ? (ulong)processorCount :
        processorCount < 24 ? 16UL :
        processorCount < 32 ? 24UL : 32UL;
}
