#nullable enable

using Fuookami.Ospf.Framework.Csp1d.Application.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.LengthAssignment.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Error;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Yield.Model;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Framework.Csp1d.Application.Service;
/// <summary>
/// 恢复求解状态 / Recovery solve status.
/// </summary>
public enum Csp1dRecoveryStatus {
    /// <summary>普通恢复求解完成 / Normal recovery solve completed.</summary>
    Solved,

    /// <summary>warm start 无法使用，已退回普通求解 / Warm start was unusable and normal solve was used.</summary>
    RetriedWithoutWarmStart,

    /// <summary>禁用 fallback，恢复流程未进入求解 / Fallback was disabled and solve was not attempted.</summary>
    FallbackDisabled,

    /// <summary>普通求解失败 / Normal solve failed.</summary>
    SolveFailed
}

/// <summary>
/// warm start 处理状态 / Warm start handling status.
/// </summary>
public enum Csp1dWarmStartStatus {
    /// <summary>未提供 warm start / Warm start was not provided.</summary>
    NotProvided,

    /// <summary>warm start 输入为空，未形成可复用上下文 / Warm start input is empty and has no reusable context.</summary>
    Ignored,

    /// <summary>当前 solver 适配层不支持消费 warm start / Current solver adapter does not support warm start.</summary>
    AdapterUnsupported,

    /// <summary>warm start 已被 adapter 应用 / Warm start was applied by adapter.</summary>
    Applied,

    /// <summary>warm start 与当前问题不匹配 / Warm start does not match the current problem.</summary>
    Invalid
}

/// <summary>
/// CSP1D warm start 输入 / CSP1D warm start input.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
/// <param name="CuttingPlans">预热方案池 / Warm-start cutting plan pool.</param>
/// <param name="PreviousSolution">上一轮解，会提取与当前问题兼容的方案和使用量 / Previous solution whose current-problem-compatible plans and usages are extracted.</param>
public sealed record Csp1dWarmStart<V>(
    IReadOnlyList<CuttingPlan<V>> CuttingPlans,
    Csp1dSolution<V>? PreviousSolution
) where V : struct {
    /// <summary>
    /// 创建空 warm start / Create empty warm start.
    /// </summary>
    public Csp1dWarmStart()
        : this(Array.Empty<CuttingPlan<V>>(), null) { }
}

/// <summary>
/// CSP1D 恢复选项 / CSP1D recovery options.
/// </summary>
/// <param name="RetryWithoutWarmStart">warm start 不可用时是否退回普通求解 / Whether to retry normal solve when warm start is unusable.</param>
public sealed record Csp1dRecoveryOptions(
    bool RetryWithoutWarmStart = true
);

/// <summary>
/// CSP1D 恢复输入 / CSP1D recovery input.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
/// <param name="Problem">问题定义 / Problem definition.</param>
/// <param name="SolveConfig">显式求解配置 / Explicit solve configuration.</param>
/// <param name="WarmStart">warm start 输入 / Warm start input.</param>
/// <param name="Options">恢复选项 / Recovery options.</param>
public sealed record Csp1dRecoveryInput<V>(
    Csp1dProblem<V> Problem,
    Csp1dSolveConfig<V>? SolveConfig = null,
    Csp1dWarmStart<V>? WarmStart = null,
    Csp1dRecoveryOptions? Options = null
) where V : struct {
    /// <summary>恢复选项默认为默认值 / Recovery options default to defaults.</summary>
    public Csp1dRecoveryOptions Options { get; init; } = Options ?? new Csp1dRecoveryOptions();
}

/// <summary>
/// CSP1D 恢复追踪 / CSP1D recovery trace.
/// </summary>
/// <param name="Status">恢复状态 / Recovery status.</param>
/// <param name="WarmStartStatus">warm start 状态 / Warm start status.</param>
/// <param name="AttemptCount">求解尝试次数 / Solve attempt count.</param>
/// <param name="WarmStartPlanCount">可复用 warm start 方案数 / Reusable warm-start plan count.</param>
/// <param name="AppliedWarmStartPlanCount">已应用 warm start 方案数 / Applied warm-start plan count.</param>
/// <param name="AppliedWarmStartUsageCount">已应用 warm start 使用量条目数 / Applied warm-start usage entry count.</param>
/// <param name="Message">补充说明 / Additional message.</param>
public sealed record Csp1dRecoveryTrace(
    Csp1dRecoveryStatus Status,
    Csp1dWarmStartStatus WarmStartStatus,
    long AttemptCount,
    long WarmStartPlanCount = 0L,
    long AppliedWarmStartPlanCount = 0L,
    long AppliedWarmStartUsageCount = 0L,
    string? Message = null
);

/// <summary>
/// CSP1D 恢复结果 / CSP1D recovery result.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
/// <param name="Solution">求解结果 / Solution.</param>
/// <param name="Trace">恢复追踪 / Recovery trace.</param>
public sealed record Csp1dRecoveryResult<V>(
    Csp1dSolution<V> Solution,
    Csp1dRecoveryTrace Trace
) where V : struct;

/// <summary>
/// CSP1D warm start adapter 输入 / CSP1D warm start adapter input.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
/// <param name="Problem">问题定义 / Problem definition.</param>
/// <param name="SolveConfig">显式求解配置 / Explicit solve configuration.</param>
/// <param name="WarmStart">warm start 输入 / Warm start input.</param>
/// <param name="CuttingPlans">已校验兼容的 warm start 方案池 / Compatible warm-start cutting plan pool.</param>
public sealed record Csp1dWarmStartAdapterInput<V>(
    Csp1dProblem<V> Problem,
    Csp1dSolveConfig<V>? SolveConfig,
    Csp1dWarmStart<V> WarmStart,
    IReadOnlyList<CuttingPlan<V>> CuttingPlans
) where V : struct;

/// <summary>
/// CSP1D warm start adapter 结果 / CSP1D warm start adapter result.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
/// <param name="InitialGenerator">应用 warm start 后的初始方案生成器 / Initial plan generator after applying warm start.</param>
/// <param name="InitialPlanUsages">应用 warm start 后的初始方案使用量 / Initial plan usages after applying warm start.</param>
/// <param name="AppliedPlanCount">已应用方案数 / Applied plan count.</param>
/// <param name="AppliedUsageCount">已应用使用量条目数 / Applied usage entry count.</param>
/// <param name="Message">补充说明 / Additional message.</param>
public sealed record Csp1dWarmStartAdapterResult<V>(
    ICsp1dInitialCuttingPlanGenerator<CuttingPlan<V>, GenerationInput<V>>? InitialGenerator,
    IReadOnlyList<CuttingPlanUsage<V>> InitialPlanUsages,
    long AppliedPlanCount,
    long AppliedUsageCount,
    string? Message
) where V : struct {
    /// <summary>
    /// 创建默认结果 / Create default result.
    /// </summary>
    public Csp1dWarmStartAdapterResult()
        : this(null, Array.Empty<CuttingPlanUsage<V>>(), 0L, 0L, null) { }
}

/// <summary>
/// CSP1D warm start adapter 接口 / CSP1D warm start adapter interface.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public interface ICsp1dWarmStartAdapter<V> where V : struct {
    /// <summary>
    /// 应用 warm start / Apply warm start.
    /// </summary>
    /// <param name="input">adapter 输入 / Adapter input.</param>
    /// <returns>adapter 结果 / Adapter result.</returns>
    Csp1dWarmStartAdapterResult<V> Apply(Csp1dWarmStartAdapterInput<V> input);

    /// <summary>
    /// 不支持 warm start 的默认 adapter / Default adapter that does not support warm start.
    /// </summary>
    /// <returns>warm start adapter / Warm start adapter.</returns>
    static ICsp1dWarmStartAdapter<V> Unsupported() => new UnsupportedWarmStartAdapter<V>();
}

/// <summary>
/// 不支持 warm start 的 adapter 实现 / Unsupported warm start adapter implementation.
/// </summary>
internal sealed class UnsupportedWarmStartAdapter<V> : ICsp1dWarmStartAdapter<V> where V : struct {
    /// <inheritdoc/>
    public Csp1dWarmStartAdapterResult<V> Apply(Csp1dWarmStartAdapterInput<V> input) {
        return new Csp1dWarmStartAdapterResult<V>(
            InitialGenerator: null,
            InitialPlanUsages: Array.Empty<CuttingPlanUsage<V>>(),
            AppliedPlanCount: 0L,
            AppliedUsageCount: 0L,
            Message: "Warm start adapter is not configured");
    }
}

/// <summary>
/// 方案池 warm start adapter / Cutting-plan-pool warm start adapter.
///
/// 将 warm start 方案池注入为初始方案，可选追加普通初始方案。
/// Injects warm start plan pool as initial plans, optionally appending normal initial plans.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public sealed class Csp1dWarmStartPlanPoolAdapter<V> : ICsp1dWarmStartAdapter<V>
    where V : struct, IComparable<V> {
    private readonly ICsp1dInitialCuttingPlanGenerator<CuttingPlan<V>, GenerationInput<V>> _fallbackGenerator;
    private readonly bool _appendFallbackPlans;

    /// <summary>
    /// 构造方案池 warm start adapter / Construct plan pool warm start adapter.
    /// </summary>
    /// <param name="fallbackGenerator">追加的普通初始方案生成器 / Appended normal initial plan generator.</param>
    /// <param name="appendFallbackPlans">是否追加普通初始方案 / Whether to append normal initial plans.</param>
    public Csp1dWarmStartPlanPoolAdapter(
        ICsp1dInitialCuttingPlanGenerator<CuttingPlan<V>, GenerationInput<V>>? fallbackGenerator = null,
        bool appendFallbackPlans = true) {
        _fallbackGenerator = fallbackGenerator ?? new SimpleInitialGenerator<V>();
        _appendFallbackPlans = appendFallbackPlans;
    }

    /// <inheritdoc/>
    public Csp1dWarmStartAdapterResult<V> Apply(Csp1dWarmStartAdapterInput<V> input) {
        IReadOnlyList<CuttingPlanUsage<V>> initialPlanUsages = WarmStartPlanUsages(input);
        IReadOnlyList<CuttingPlan<V>> warmStartPlans = input.CuttingPlans;

        // Create a generator that combines warm start plans with optional fallback
        ICsp1dInitialCuttingPlanGenerator<CuttingPlan<V>, GenerationInput<V>> combinedGenerator =
            new WarmStartCombinedGenerator<V>(warmStartPlans, _fallbackGenerator, _appendFallbackPlans);

        return new Csp1dWarmStartAdapterResult<V>(
            InitialGenerator: combinedGenerator,
            InitialPlanUsages: initialPlanUsages,
            AppliedPlanCount: warmStartPlans.Count,
            AppliedUsageCount: initialPlanUsages.Count,
            Message: "Warm start cutting plan pool was applied as initial plan pool");
    }

    private static IReadOnlyList<CuttingPlanUsage<V>> WarmStartPlanUsages(
        Csp1dWarmStartAdapterInput<V> input) {
        Csp1dSolution<V>? previousSolution = input.WarmStart.PreviousSolution;
        if (previousSolution is null) {
            return Array.Empty<CuttingPlanUsage<V>>();
        }

        var compatiblePlanKeys = input.CuttingPlans
            .Select(plan => CuttingPlanCanonicalKey.From(plan))
            .ToHashSet();
        return previousSolution.Produce.CuttingPlans
            .Where(usage => compatiblePlanKeys.Contains(CuttingPlanCanonicalKey.From(usage.Plan)))
            .ToList();
    }
}

/// <summary>
/// warm start 组合生成器 / Warm start combined generator.
///
/// 将 warm start 方案池与可选的 fallback 生成器组合。
/// Combines warm start plan pool with optional fallback generator.
/// </summary>
internal sealed class WarmStartCombinedGenerator<V>
    : ICsp1dInitialCuttingPlanGenerator<CuttingPlan<V>, GenerationInput<V>>
    where V : struct, IComparable<V> {
    private readonly IReadOnlyList<CuttingPlan<V>> _warmStartPlans;
    private readonly ICsp1dInitialCuttingPlanGenerator<CuttingPlan<V>, GenerationInput<V>> _fallbackGenerator;
    private readonly bool _appendFallbackPlans;

    public WarmStartCombinedGenerator(
        IReadOnlyList<CuttingPlan<V>> warmStartPlans,
        ICsp1dInitialCuttingPlanGenerator<CuttingPlan<V>, GenerationInput<V>> fallbackGenerator,
        bool appendFallbackPlans) {
        _warmStartPlans = warmStartPlans;
        _fallbackGenerator = fallbackGenerator;
        _appendFallbackPlans = appendFallbackPlans;
    }

    /// <inheritdoc/>
    public IReadOnlyList<CuttingPlan<V>> Generate(GenerationInput<V> input) {
        if (_appendFallbackPlans) {
            IReadOnlyList<CuttingPlan<V>> fallbackPlans = _fallbackGenerator.Generate(input);
            return _warmStartPlans.Concat(fallbackPlans).ToList();
        }
        return _warmStartPlans;
    }
}

/// <summary>
/// CSP1D recovery fallback 禁用异常 / CSP1D recovery fallback-disabled exception.
/// </summary>
public sealed class Csp1dRecoveryFallbackDisabledException : ArgumentException {
    /// <summary>
    /// 失败时的恢复追踪 / Recovery trace at failure.
    /// </summary>
    public Csp1dRecoveryTrace Trace { get; }

    /// <summary>
    /// 构造 fallback 禁用异常 / Construct fallback-disabled exception.
    /// </summary>
    /// <param name="message">异常信息 / Exception message.</param>
    /// <param name="trace">恢复追踪 / Recovery trace.</param>
    public Csp1dRecoveryFallbackDisabledException(string message, Csp1dRecoveryTrace trace)
        : base(message) {
        Trace = trace;
    }
}

/// <summary>
/// CSP1D recovery 求解异常 / CSP1D recovery solve exception.
/// </summary>
public sealed class Csp1dRecoverySolveException : InvalidOperationException {
    /// <summary>
    /// 失败时的恢复追踪 / Recovery trace at failure.
    /// </summary>
    public Csp1dRecoveryTrace Trace { get; }

    /// <summary>
    /// 构造求解异常 / Construct solve exception.
    /// </summary>
    /// <param name="message">异常信息 / Exception message.</param>
    /// <param name="trace">恢复追踪 / Recovery trace.</param>
    /// <param name="cause">原始异常 / Original exception.</param>
    public Csp1dRecoverySolveException(string message, Csp1dRecoveryTrace trace, Exception cause)
        : base(message, cause) {
        Trace = trace;
    }
}

/// <summary>
/// CSP1D 恢复求解入口 / CSP1D recovery entry point.
///
/// 在异常恢复场景下重新求解。当 warm start 不可用时，根据配置决定是否退回普通求解。
/// Re-solves for recovery scenarios. When warm start is unusable, decides whether to
/// fall back to normal solve based on configuration.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public sealed class Csp1dRecovery<V> where V : struct, IComparable<V>, IRealNumber<V> {
    private readonly IColumnGenerationSolver _solver;
    private readonly Csp1dMilp<V> _milp;
    private readonly ICsp1dWarmStartAdapter<V> _warmStartAdapter;

    /// <summary>
    /// 构造恢复求解入口 / Construct recovery entry point.
    /// </summary>
    /// <param name="solver">列生成求解器 / Column generation solver.</param>
    /// <param name="milp">MILP 求解器（可选）/ MILP solver (optional).</param>
    /// <param name="warmStartAdapter">warm start adapter（可选）/ Warm start adapter (optional).</param>
    public Csp1dRecovery(
        IColumnGenerationSolver solver,
        Csp1dMilp<V>? milp = null,
        ICsp1dWarmStartAdapter<V>? warmStartAdapter = null) {
        _solver = solver;
        _milp = milp ?? new Csp1dMilp<V>(solver);
        _warmStartAdapter = warmStartAdapter ?? ICsp1dWarmStartAdapter<V>.Unsupported();
    }

    /// <summary>
    /// 在异常恢复场景下重新求解 / Re-solve for recovery scenarios.
    /// </summary>
    /// <param name="problem">问题定义 / Problem definition.</param>
    /// <param name="solveConfig">显式求解配置 / Explicit solve configuration.</param>
    /// <returns>求解结果 / Solution.</returns>
    public async Task<Result<Csp1dSolution<V>, ErrorCode, Error<ErrorCode>>> SolveAsync(
        Csp1dProblem<V> problem,
        Csp1dSolveConfig<V>? solveConfig = null) {
        Result<Csp1dRecoveryResult<V>, ErrorCode, Error<ErrorCode>> traceResult = await SolveWithTraceAsync(
            new Csp1dRecoveryInput<V>(
                Problem: problem,
                SolveConfig: solveConfig));
        return traceResult.Map(r => r.Solution);
    }

    /// <summary>
    /// 带恢复追踪求解 / Solve with recovery trace.
    /// </summary>
    /// <param name="input">恢复输入 / Recovery input.</param>
    /// <returns>恢复结果 / Recovery result.</returns>
    public async Task<Result<Csp1dRecoveryResult<V>, ErrorCode, Error<ErrorCode>>> SolveWithTraceAsync(
        Csp1dRecoveryInput<V> input) {
        Csp1dWarmStartResolution<V> warmStart = Csp1dRecoveryHelpers.Csp1dResolveWarmStart(input, _warmStartAdapter);

        // Wire flow policy allowRecoveryFallback into fallback decision
        IReadOnlyList<ICsp1dFlowPolicy<V>> effectiveFlowPolicies = Csp1dRecoveryHelpers.Csp1dRecoveryFlowPolicies(input);
        bool allowFallback;
        if (Csp1dRecoveryHelpers.Csp1dRequiresFallback(warmStart.Status) && effectiveFlowPolicies.Count > 0) {
            ICsp1dFlowContext<V> flowCtx = Csp1dRecoveryHelpers.Csp1dRecoveryFlowContext(
                input,
                warmStartPlanCount: warmStart.Plans.Count,
                warmStartRequiresFallback: Csp1dRecoveryHelpers.Csp1dRequiresFallback(warmStart.Status));
            allowFallback = Csp1dFlowPolicyHelpers.AllowRecoveryFallbackByPolicies(
                effectiveFlowPolicies, flowCtx, input.Options.RetryWithoutWarmStart);
        }
        else {
            allowFallback = input.Options.RetryWithoutWarmStart;
        }

        if (Csp1dRecoveryHelpers.Csp1dRequiresFallback(warmStart.Status) && !allowFallback) {
            Csp1dRecoveryTrace trace = Csp1dRecoveryHelpers.Csp1dFallbackDisabledTrace(warmStart.Status, warmStart.Plans.Count);
            return new Failed<Csp1dRecoveryResult<V>, ErrorCode, Error<ErrorCode>>(
                new Csp1dLifecycleError($"Warm start cannot be applied and fallback is disabled: {warmStart.Status}"));
        }

        Csp1dSolution<V> solution;
        try {
            Csp1dMilp<V> activeMilp;
            if (warmStart.AdapterResult?.InitialGenerator is not null) {
                activeMilp = new Csp1dMilp<V>(
                    solver: _solver,
                    initialGenerator: warmStart.AdapterResult.InitialGenerator,
                    warmStartPlanUsages: warmStart.AdapterResult.InitialPlanUsages);
            }
            else {
                activeMilp = _milp;
            }
            solution = await activeMilp.SolveAsync(input.Problem, input.SolveConfig);
        }
        catch (Exception error) {
            var trace = new Csp1dRecoveryTrace(
                Status: Csp1dRecoveryStatus.SolveFailed,
                WarmStartStatus: warmStart.Status,
                AttemptCount: 1L,
                WarmStartPlanCount: warmStart.Plans.Count,
                AppliedWarmStartPlanCount: warmStart.AdapterResult?.AppliedPlanCount ?? 0L,
                AppliedWarmStartUsageCount: warmStart.AdapterResult?.AppliedUsageCount ?? 0L,
                Message: error.Message ?? "Recovery solve failed");
            return new Failed<Csp1dRecoveryResult<V>, ErrorCode, Error<ErrorCode>>(
                new Csp1dSolvingError(trace.Message ?? "Recovery solve failed"));
        }

        Csp1dRecoveryStatus status = Csp1dRecoveryHelpers.Csp1dRequiresFallback(warmStart.Status)
            ? Csp1dRecoveryStatus.RetriedWithoutWarmStart
            : Csp1dRecoveryStatus.Solved;

        return new Ok<Csp1dRecoveryResult<V>, ErrorCode, Error<ErrorCode>>(
            new Csp1dRecoveryResult<V>(
                Solution: solution,
                Trace: new Csp1dRecoveryTrace(
                    Status: status,
                    WarmStartStatus: warmStart.Status,
                    AttemptCount: 1L,
                    WarmStartPlanCount: warmStart.Plans.Count,
                    AppliedWarmStartPlanCount: warmStart.AdapterResult?.AppliedPlanCount ?? 0L,
                    AppliedWarmStartUsageCount: warmStart.AdapterResult?.AppliedUsageCount ?? 0L,
                    Message: warmStart.AdapterResult?.Message ?? Csp1dRecoveryHelpers.Csp1dWarmStartMessage(warmStart.Status))));
    }
}

/// <summary>
/// CSP1D 列生成恢复求解入口 / CSP1D column-generation recovery entry point.
///
/// 在异常恢复场景下用列生成重新求解。
/// Re-solves with column generation for recovery scenarios.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public sealed class Csp1dColumnGenerationRecovery<V> where V : struct, IComparable<V>, IRealNumber<V> {
    private readonly IColumnGenerationSolver _solver;
    private readonly Csp1dMilp<V> _defaultMilp;
    private readonly ICsp1dInitialCuttingPlanGenerator<CuttingPlan<V>, GenerationInput<V>> _initialGenerator;
    private readonly ICsp1dSolutionAnalyzer<V> _analyzer;
    private readonly YieldModelingConfig<V>? _yieldConfig;
    private readonly WasteMinimizationConfig<V>? _wasteConfig;
    private readonly LengthAssignmentModelingConfig<V>? _lengthConfig;
    private readonly ICsp1dWarmStartAdapter<V> _warmStartAdapter;

    /// <summary>
    /// 构造列生成恢复求解入口 / Construct column generation recovery entry point.
    ///
    /// 当前实现使用 MILP 求解路径（Csp1dColumnGeneration 为 stub）。
    /// Current implementation uses the MILP solve path (Csp1dColumnGeneration is a stub).
    /// </summary>
    /// <param name="solver">列生成求解器 / Column generation solver.</param>
    /// <param name="initialGenerator">默认初始方案生成器 / Default initial plan generator.</param>
    /// <param name="analyzer">解分析器 / Solution analyzer.</param>
    /// <param name="yieldConfig">默认 yield 建模配置 / Default yield modeling config.</param>
    /// <param name="wasteConfig">默认 waste 建模配置 / Default waste modeling config.</param>
    /// <param name="lengthConfig">默认 length 建模配置 / Default length modeling config.</param>
    /// <param name="warmStartAdapter">warm start adapter / Warm start adapter.</param>
    public Csp1dColumnGenerationRecovery(
        IColumnGenerationSolver solver,
        ICsp1dInitialCuttingPlanGenerator<CuttingPlan<V>, GenerationInput<V>>? initialGenerator = null,
        ICsp1dSolutionAnalyzer<V>? analyzer = null,
        YieldModelingConfig<V>? yieldConfig = null,
        WasteMinimizationConfig<V>? wasteConfig = null,
        LengthAssignmentModelingConfig<V>? lengthConfig = null,
        ICsp1dWarmStartAdapter<V>? warmStartAdapter = null) {
        _solver = solver;
        _initialGenerator = initialGenerator ?? new SimpleInitialGenerator<V>();
        _analyzer = analyzer ?? new DefaultCsp1dSolutionAnalyzer<V>();
        _yieldConfig = yieldConfig;
        _wasteConfig = wasteConfig;
        _lengthConfig = lengthConfig;
        _defaultMilp = new Csp1dMilp<V>(solver, _initialGenerator, _analyzer, yieldConfig, wasteConfig, lengthConfig);
        _warmStartAdapter = warmStartAdapter ?? ICsp1dWarmStartAdapter<V>.Unsupported();
    }

    /// <summary>
    /// 在异常恢复场景下用列生成重新求解 / Re-solve with column generation for recovery scenarios.
    /// </summary>
    /// <param name="problem">问题定义 / Problem definition.</param>
    /// <param name="solveConfig">显式求解配置 / Explicit solve configuration.</param>
    /// <returns>求解结果 / Solution.</returns>
    public async Task<Result<Csp1dSolution<V>, ErrorCode, Error<ErrorCode>>> SolveAsync(
        Csp1dProblem<V> problem,
        Csp1dSolveConfig<V>? solveConfig = null) {
        Result<Csp1dRecoveryResult<V>, ErrorCode, Error<ErrorCode>> traceResult = await SolveWithTraceAsync(
            new Csp1dRecoveryInput<V>(
                Problem: problem,
                SolveConfig: solveConfig));
        return traceResult.Map(r => r.Solution);
    }

    /// <summary>
    /// 带恢复追踪的列生成求解 / Solve with recovery trace through column generation.
    /// </summary>
    /// <param name="input">恢复输入 / Recovery input.</param>
    /// <returns>恢复结果 / Recovery result.</returns>
    public async Task<Result<Csp1dRecoveryResult<V>, ErrorCode, Error<ErrorCode>>> SolveWithTraceAsync(
        Csp1dRecoveryInput<V> input) {
        Csp1dWarmStartResolution<V> warmStart = Csp1dRecoveryHelpers.Csp1dResolveWarmStart(input, _warmStartAdapter);

        // Wire flow policy allowRecoveryFallback into fallback decision
        IReadOnlyList<ICsp1dFlowPolicy<V>> effectiveFlowPolicies = Csp1dRecoveryHelpers.Csp1dRecoveryFlowPolicies(input);
        bool allowFallback;
        if (Csp1dRecoveryHelpers.Csp1dRequiresFallback(warmStart.Status) && effectiveFlowPolicies.Count > 0) {
            ICsp1dFlowContext<V> flowCtx = Csp1dRecoveryHelpers.Csp1dRecoveryFlowContext(
                input,
                warmStartPlanCount: warmStart.Plans.Count,
                warmStartRequiresFallback: Csp1dRecoveryHelpers.Csp1dRequiresFallback(warmStart.Status));
            allowFallback = Csp1dFlowPolicyHelpers.AllowRecoveryFallbackByPolicies(
                effectiveFlowPolicies, flowCtx, input.Options.RetryWithoutWarmStart);
        }
        else {
            allowFallback = input.Options.RetryWithoutWarmStart;
        }

        if (Csp1dRecoveryHelpers.Csp1dRequiresFallback(warmStart.Status) && !allowFallback) {
            Csp1dRecoveryTrace trace = Csp1dRecoveryHelpers.Csp1dFallbackDisabledTrace(warmStart.Status, warmStart.Plans.Count);
            return new Failed<Csp1dRecoveryResult<V>, ErrorCode, Error<ErrorCode>>(
                new Csp1dLifecycleError($"Warm start cannot be applied and fallback is disabled: {warmStart.Status}"));
        }

        Csp1dSolution<V> solution;
        try {
            // When warm start provides a generator, create a new Csp1dMilp with it;
            // otherwise use the default MILP path
            if (warmStart.AdapterResult?.InitialGenerator is not null) {
                var activeMilp = new Csp1dMilp<V>(
                    solver: _solver,
                    initialGenerator: warmStart.AdapterResult.InitialGenerator,
                    analyzer: _analyzer,
                    yieldConfig: _yieldConfig,
                    wasteConfig: _wasteConfig,
                    lengthConfig: _lengthConfig,
                    warmStartPlanUsages: warmStart.AdapterResult.InitialPlanUsages);
                solution = await activeMilp.SolveAsync(input.Problem, input.SolveConfig);
            }
            else {
                solution = await _defaultMilp.SolveAsync(input.Problem, input.SolveConfig);
            }
        }
        catch (Exception error) {
            var trace = new Csp1dRecoveryTrace(
                Status: Csp1dRecoveryStatus.SolveFailed,
                WarmStartStatus: warmStart.Status,
                AttemptCount: 1L,
                WarmStartPlanCount: warmStart.Plans.Count,
                AppliedWarmStartPlanCount: warmStart.AdapterResult?.AppliedPlanCount ?? 0L,
                AppliedWarmStartUsageCount: warmStart.AdapterResult?.AppliedUsageCount ?? 0L,
                Message: error.Message ?? "Column generation recovery solve failed");
            return new Failed<Csp1dRecoveryResult<V>, ErrorCode, Error<ErrorCode>>(
                new Csp1dSolvingError(trace.Message ?? "Column generation recovery solve failed"));
        }

        Csp1dRecoveryStatus status = Csp1dRecoveryHelpers.Csp1dRequiresFallback(warmStart.Status)
            ? Csp1dRecoveryStatus.RetriedWithoutWarmStart
            : Csp1dRecoveryStatus.Solved;

        return new Ok<Csp1dRecoveryResult<V>, ErrorCode, Error<ErrorCode>>(
            new Csp1dRecoveryResult<V>(
                Solution: solution,
                Trace: new Csp1dRecoveryTrace(
                    Status: status,
                    WarmStartStatus: warmStart.Status,
                    AttemptCount: 1L,
                    WarmStartPlanCount: warmStart.Plans.Count,
                    AppliedWarmStartPlanCount: warmStart.AdapterResult?.AppliedPlanCount ?? 0L,
                    AppliedWarmStartUsageCount: warmStart.AdapterResult?.AppliedUsageCount ?? 0L,
                    Message: warmStart.AdapterResult?.Message ?? Csp1dRecoveryHelpers.Csp1dWarmStartMessage(warmStart.Status))));
    }
}

// ===== Private helper types and methods =====

/// <summary>
/// warm start 解析结果 / Warm start resolution result.
/// </summary>
internal sealed record Csp1dWarmStartResolution<V>(
    IReadOnlyList<CuttingPlan<V>> Plans,
    Csp1dWarmStartStatus Status,
    Csp1dWarmStartAdapterResult<V>? AdapterResult
) where V : struct;

/// <summary>
/// warm start 方案选择结果 / Warm start plan selection result.
/// </summary>
internal sealed record Csp1dWarmStartPlanSelection<V>(
    IReadOnlyList<CuttingPlan<V>> Plans,
    Csp1dWarmStartStatus? Status = null
) where V : struct;

/// <summary>
/// CSP1D 恢复辅助方法 / CSP1D recovery helper methods.
/// </summary>
internal static class Csp1dRecoveryHelpers {
    /// <summary>
    /// 解析 warm start / Resolve warm start.
    ///
    /// 选择兼容的 warm start 方案，尝试 adapter 应用，确定最终状态。
    /// Selects compatible warm start plans, attempts adapter application, determines final status.
    /// </summary>
    internal static Csp1dWarmStartResolution<V> Csp1dResolveWarmStart<V>(
        Csp1dRecoveryInput<V> input,
        ICsp1dWarmStartAdapter<V> warmStartAdapter)
        where V : struct, IComparable<V>, IRealNumber<V> {
        Csp1dWarmStartPlanSelection<V> planSelection = Csp1dWarmStartPlanSelection(input);
        IReadOnlyList<CuttingPlan<V>> warmStartPlans = planSelection.Plans;
        Csp1dWarmStartStatus initialStatus = Csp1dResolveWarmStartStatus(input, warmStartPlans, planSelection.Status);

        Csp1dWarmStart<V>? warmStart = input.WarmStart;
        Csp1dWarmStartAdapterResult<V>? adapterResult = null;
        if (initialStatus == Csp1dWarmStartStatus.AdapterUnsupported && warmStart is not null) {
            adapterResult = warmStartAdapter.Apply(
                new Csp1dWarmStartAdapterInput<V>(
                    Problem: input.Problem,
                    SolveConfig: input.SolveConfig,
                    WarmStart: warmStart,
                    CuttingPlans: warmStartPlans));
        }

        Csp1dWarmStartStatus status = adapterResult?.InitialGenerator is not null
            ? Csp1dWarmStartStatus.Applied
            : initialStatus;

        return new Csp1dWarmStartResolution<V>(
            Plans: warmStartPlans,
            Status: status,
            AdapterResult: adapterResult);
    }

    /// <summary>
    /// 确定 warm start 状态 / Determine warm start status.
    /// </summary>
    internal static Csp1dWarmStartStatus Csp1dResolveWarmStartStatus<V>(
        Csp1dRecoveryInput<V> input,
        IReadOnlyList<CuttingPlan<V>> plans,
        Csp1dWarmStartStatus? selectedStatus)
        where V : struct {
        if (selectedStatus is not null) {
            return selectedStatus.Value;
        }

        if (input.WarmStart is null) {
            return Csp1dWarmStartStatus.NotProvided;
        }

        if (plans.Count == 0) {
            return Csp1dWarmStartStatus.Ignored;
        }

        if (!Csp1dIsWarmStartCompatible(plans, input.Problem)) {
            return Csp1dWarmStartStatus.Invalid;
        }

        return Csp1dWarmStartStatus.AdapterUnsupported;
    }

    /// <summary>
    /// 选择 warm start 方案 / Select warm start plans.
    /// </summary>
    internal static Csp1dWarmStartPlanSelection<V> Csp1dWarmStartPlanSelection<V>(
        Csp1dRecoveryInput<V> input)
        where V : struct {
        Csp1dWarmStart<V>? warmStart = input.WarmStart;
        if (warmStart is null) {
            return new Csp1dWarmStartPlanSelection<V>(
                Plans: Array.Empty<CuttingPlan<V>>(),
                Status: Csp1dWarmStartStatus.NotProvided);
        }

        if (warmStart.CuttingPlans.Count > 0) {
            if (!Csp1dIsWarmStartCompatible(warmStart.CuttingPlans, input.Problem)) {
                return new Csp1dWarmStartPlanSelection<V>(
                    Plans: warmStart.CuttingPlans,
                    Status: Csp1dWarmStartStatus.Invalid);
            }
            return new Csp1dWarmStartPlanSelection<V>(Plans: warmStart.CuttingPlans);
        }

        var compatiblePlans = warmStart.PreviousSolution?.GeneratedPlans
            ?.Where(plan => Csp1dIsWarmStartCompatible(plan, input.Problem))
            .ToList();
        return new Csp1dWarmStartPlanSelection<V>(
            Plans: compatiblePlans ?? (IReadOnlyList<CuttingPlan<V>>)Array.Empty<CuttingPlan<V>>());
    }

    /// <summary>
    /// 检查 warm start 方案集合是否兼容 / Check if warm start plan collection is compatible.
    /// </summary>
    internal static bool Csp1dIsWarmStartCompatible<V>(
        IReadOnlyList<CuttingPlan<V>> plans,
        Csp1dProblem<V> problem)
        where V : struct => plans.All(plan => Csp1dIsWarmStartCompatible(plan, problem));

    /// <summary>
    /// 检查单个 warm start 方案是否兼容 / Check if a single warm start plan is compatible.
    /// </summary>
    internal static bool Csp1dIsWarmStartCompatible<V>(
        CuttingPlan<V> plan,
        Csp1dProblem<V> problem)
        where V : struct {
        var materialById = problem.Materials.ToDictionary(m => m.Id);
        var machineIds = problem.Machines.Select(m => m.Id).ToHashSet();
        var productIds = problem.Products.Select(p => p.Id).ToHashSet();

        if (!materialById.TryGetValue(plan.Material.Id, out Material<V>? material)) {
            return false;
        }

        if (!material.Enabled(plan, problem.Machines)) {
            return false;
        }

        string? machineId = plan.MachineId;
        if (machineId is not null && machineIds.Count > 0 && !machineIds.Contains(machineId)) {
            return false;
        }

        if (plan.DemandContributions.Any(c => !productIds.Contains(c.Product.Id))) {
            return false;
        }

        return true;
    }

    /// <summary>
    /// 是否需要 fallback / Whether fallback is required.
    /// </summary>
    internal static bool Csp1dRequiresFallback(Csp1dWarmStartStatus status) {
        return status == Csp1dWarmStartStatus.Invalid
            || status == Csp1dWarmStartStatus.AdapterUnsupported;
    }

    /// <summary>
    /// warm start 状态消息 / Warm start status message.
    /// </summary>
    internal static string? Csp1dWarmStartMessage(Csp1dWarmStartStatus status) {
        return status switch {
            Csp1dWarmStartStatus.NotProvided => null,
            Csp1dWarmStartStatus.Ignored => "Warm start was provided without reusable cutting plans; normal solve was used",
            Csp1dWarmStartStatus.AdapterUnsupported => "Warm start is compatible but current adapter does not support applying it; normal solve was used",
            Csp1dWarmStartStatus.Applied => "Warm start was applied by adapter",
            Csp1dWarmStartStatus.Invalid => "Warm start is incompatible with current problem; normal solve was used",
            _ => null
        };
    }

    /// <summary>
    /// 构建 fallback 禁用追踪 / Build fallback-disabled trace.
    /// </summary>
    internal static Csp1dRecoveryTrace Csp1dFallbackDisabledTrace(
        Csp1dWarmStartStatus status,
        long planCount) {
        return new Csp1dRecoveryTrace(
            Status: Csp1dRecoveryStatus.FallbackDisabled,
            WarmStartStatus: status,
            AttemptCount: 0L,
            WarmStartPlanCount: planCount,
            AppliedWarmStartPlanCount: 0L,
            AppliedWarmStartUsageCount: 0L,
            Message: Csp1dFallbackDisabledMessage(status));
    }

    /// <summary>
    /// fallback 禁用消息 / Fallback disabled message.
    /// </summary>
    internal static string Csp1dFallbackDisabledMessage(Csp1dWarmStartStatus status) {
        return status switch {
            Csp1dWarmStartStatus.AdapterUnsupported =>
                "Warm start is compatible but current adapter does not support applying it; fallback is disabled",
            Csp1dWarmStartStatus.Invalid =>
                "Warm start is incompatible with current problem; fallback is disabled",
            _ => "Warm start cannot be applied and fallback is disabled"
        };
    }

    /// <summary>
    /// 获取恢复流程策略列表 / Get recovery flow policy list.
    /// </summary>
    internal static IReadOnlyList<ICsp1dFlowPolicy<V>> Csp1dRecoveryFlowPolicies<V>(
        Csp1dRecoveryInput<V> input)
        where V : struct {
        Csp1dSolveConfig<V>? config = input.SolveConfig ?? input.Problem.SolveConfig;
        return config?.ExtensionSet.FlowPolicies ?? Array.Empty<ICsp1dFlowPolicy<V>>();
    }

    /// <summary>
    /// 构建恢复流程上下文 / Build recovery flow context.
    /// </summary>
    internal static ICsp1dFlowContext<V> Csp1dRecoveryFlowContext<V>(
        Csp1dRecoveryInput<V> input,
        long warmStartPlanCount,
        bool warmStartRequiresFallback)
        where V : struct {
        Csp1dSolveConfig<V>? config = input.SolveConfig ?? input.Problem.SolveConfig;
        return new Csp1dRecoveryFlowContextImpl<V>(
            iteration: 0L,
            currentPlans: Array.Empty<CuttingPlan<V>>(),
            iterationLimit: config?.ColumnGeneration.IterationLimit.ToLong() ?? 0L,
            allowPartialSolution: config?.AllowPartialSolution ?? true,
            warmStartPlanCount: warmStartPlanCount,
            warmStartRequiresFallback: warmStartRequiresFallback);
    }
}

/// <summary>
/// 恢复流程上下文实现 / Recovery flow context implementation.
/// </summary>
internal sealed class Csp1dRecoveryFlowContextImpl<V> : ICsp1dFlowContext<V> where V : struct {
    public Csp1dRecoveryFlowContextImpl(
        long iteration,
        IReadOnlyList<CuttingPlan<V>> currentPlans,
        long iterationLimit,
        bool allowPartialSolution,
        long warmStartPlanCount,
        bool warmStartRequiresFallback) {
        Iteration = iteration;
        CurrentPlans = currentPlans;
        IterationLimit = iterationLimit;
        AllowPartialSolution = allowPartialSolution;
        WarmStartPlanCount = warmStartPlanCount;
        WarmStartRequiresFallback = warmStartRequiresFallback;
    }

    /// <inheritdoc/>
    public long Iteration { get; }

    /// <inheritdoc/>
    public IReadOnlyList<CuttingPlan<V>> CurrentPlans { get; }

    /// <inheritdoc/>
    public long IterationLimit { get; }

    /// <inheritdoc/>
    public bool AllowPartialSolution { get; }

    /// <inheritdoc/>
    public long WarmStartPlanCount { get; }

    /// <inheritdoc/>
    public bool WarmStartRequiresFallback { get; }
}
