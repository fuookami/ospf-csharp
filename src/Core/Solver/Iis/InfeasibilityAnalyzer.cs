#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Core.Solver.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Report;

namespace Fuookami.Ospf.Core.Solver.Iis;

/// <summary>
/// 后端无关的不可行证据分析器。
/// Backend-neutral infeasibility evidence analyzer.
/// </summary>
/// <typeparam name="M">待分析的模型类型 / Model type to analyze.</typeparam>
public interface IInfeasibilityAnalyzer<M> {
    /// <summary>证据来源 / Evidence source.</summary>
    InfeasibilityEvidenceSource Source { get; }

    /// <summary>分析一个已证明不可行的模型 / Analyze a model proven infeasible.</summary>
    /// <param name="model">待分析模型 / Model to analyze.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>结构化证据或错误 / Structured evidence or an error.</returns>
    Task<Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>>> AnalyzeAsync(
        M model,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 兼容 facade 保留的物化诊断结果。
/// Materialized diagnostic result retained by compatibility facades.
/// </summary>
/// <typeparam name="A">兼容 artifact 类型 / Compatibility artifact type.</typeparam>
/// <param name="Evidence">结构化不可行证据 / Structured infeasibility evidence.</param>
/// <param name="Artifact">兼容 artifact；分析器不提供时为空 / Compatibility artifact; null when the analyzer does not provide one.</param>
public sealed record MaterializedInfeasibilityEvidence<A>(
    InfeasibilityEvidence Evidence,
    A? Artifact);

/// <summary>
/// 同时能返回旧模型 artifact 的分析器。
/// Analyzer that can also return a legacy model artifact.
/// </summary>
/// <typeparam name="M">待分析的模型类型 / Model type to analyze.</typeparam>
/// <typeparam name="A">兼容 artifact 类型 / Compatibility artifact type.</typeparam>
public interface IMaterializingInfeasibilityAnalyzer<M, A> : IInfeasibilityAnalyzer<M> {
    /// <summary>分析模型并保留兼容 artifact / Analyze a model and retain a compatibility artifact.</summary>
    /// <param name="model">待分析模型 / Model to analyze.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>物化证据或错误 / Materialized evidence or an error.</returns>
    Task<Result<MaterializedInfeasibilityEvidence<A>, ErrorCode, Error<ErrorCode>>> AnalyzeMaterializedAsync(
        M model,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 不可行分析器的能力声明。
/// Capability declaration for an infeasibility analyzer.
/// </summary>
/// <param name="Source">证据来源 / Evidence source.</param>
/// <param name="ModelTypes">支持的模型类型；空集合表示不受限 / Supported model types; an empty set is unrestricted.</param>
/// <param name="Exact">是否提供精确证据 / Whether exact evidence is provided.</param>
public sealed record InfeasibilityAnalyzerCapabilities(
    InfeasibilityEvidenceSource Source,
    IReadOnlySet<SolverModelType>? ModelTypes = null,
    bool Exact = false) {
    /// <summary>支持的模型类型 / Supported model types.</summary>
    public IReadOnlySet<SolverModelType> ModelTypes { get; init; } =
        ModelTypes ?? new HashSet<SolverModelType>();
}

/// <summary>
/// 能够参与策略选择的分析器。
/// Analyzer participating in capability-based selection.
/// </summary>
/// <typeparam name="M">待分析的模型类型 / Model type to analyze.</typeparam>
public interface ICapabilityAwareInfeasibilityAnalyzer<M> : IInfeasibilityAnalyzer<M> {
    /// <summary>用于策略选择的分析器能力 / Analyzer capabilities used for strategy selection.</summary>
    InfeasibilityAnalyzerCapabilities Capabilities { get; }
}

/// <summary>
/// 原生 IIS 分析器标记接口；默认来源为原生 IIS。
/// Native IIS analyzer marker whose default source is native IIS.
/// </summary>
/// <remarks>实现类应显式声明 Source，否则默认实现只能经接口访问。 / Concrete analyzers should declare the source explicitly; otherwise the default is visible only through the interface.</remarks>
/// <typeparam name="M">待分析的模型类型 / Model type to analyze.</typeparam>
public interface INativeIisAnalyzer<M> : ICapabilityAwareInfeasibilityAnalyzer<M> {
    /// <inheritdoc/>
    InfeasibilityEvidenceSource IInfeasibilityAnalyzer<M>.Source => InfeasibilityEvidenceSource.NativeIis;
}

/// <summary>
/// Farkas 分析器标记接口；默认来源为 Farkas 证书。
/// Farkas analyzer marker whose default source is a Farkas certificate.
/// </summary>
/// <remarks>实现类应显式声明 Source，否则默认实现只能经接口访问。 / Concrete analyzers should declare the source explicitly; otherwise the default is visible only through the interface.</remarks>
/// <typeparam name="M">待分析的模型类型 / Model type to analyze.</typeparam>
public interface IFarkasInfeasibilityAnalyzer<M> : ICapabilityAwareInfeasibilityAnalyzer<M> {
    /// <inheritdoc/>
    InfeasibilityEvidenceSource IInfeasibilityAnalyzer<M>.Source => InfeasibilityEvidenceSource.Farkas;
}

/// <summary>
/// 把后端原生诊断函数接入统一 SPI。
/// Adapt a backend-native diagnostic function to the common SPI.
/// </summary>
/// <typeparam name="M">待分析的模型类型 / Model type to analyze.</typeparam>
public sealed class DelegatingInfeasibilityAnalyzer<M> : ICapabilityAwareInfeasibilityAnalyzer<M> {
    private readonly Func<M, CancellationToken, Task<Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>>>> _delegate;

    /// <inheritdoc/>
    public InfeasibilityAnalyzerCapabilities Capabilities { get; }
    /// <inheritdoc/>
    public InfeasibilityEvidenceSource Source => Capabilities.Source;

    /// <summary>创建委托分析器 / Create a delegating analyzer.</summary>
    /// <param name="capabilities">分析器能力 / Analyzer capabilities.</param>
    /// <param name="delegate">原生分析函数 / Native analysis function.</param>
    public DelegatingInfeasibilityAnalyzer(
        InfeasibilityAnalyzerCapabilities capabilities,
        Func<M, CancellationToken, Task<Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>>>> @delegate) {
        Capabilities = capabilities;
        _delegate = @delegate;
    }

    /// <inheritdoc/>
    public Task<Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>>> AnalyzeAsync(
        M model,
        CancellationToken cancellationToken = default) =>
        _delegate(model, cancellationToken);
}

/// <summary>
/// 原生 IIS 委托适配器。
/// Native IIS delegate adapter.
/// </summary>
/// <typeparam name="M">待分析的模型类型 / Model type to analyze.</typeparam>
public sealed class NativeIisAnalyzerAdapter<M> : INativeIisAnalyzer<M> {
    private readonly Func<M, CancellationToken, Task<Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>>>> _delegate;

    /// <inheritdoc/>
    public InfeasibilityAnalyzerCapabilities Capabilities { get; }
    /// <inheritdoc/>
    public InfeasibilityEvidenceSource Source => InfeasibilityEvidenceSource.NativeIis;

    /// <summary>创建原生 IIS 适配器 / Create a native IIS adapter.</summary>
    /// <param name="delegate">原生 IIS 分析函数 / Native IIS analysis function.</param>
    /// <param name="modelTypes">支持的模型类型 / Supported model types.</param>
    public NativeIisAnalyzerAdapter(
        Func<M, CancellationToken, Task<Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>>>> @delegate,
        IReadOnlySet<SolverModelType>? modelTypes = null) {
        Capabilities = new InfeasibilityAnalyzerCapabilities(
            Source: InfeasibilityEvidenceSource.NativeIis,
            ModelTypes: modelTypes,
            Exact: true);
        _delegate = @delegate;
    }

    /// <inheritdoc/>
    public Task<Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>>> AnalyzeAsync(
        M model,
        CancellationToken cancellationToken = default) =>
        _delegate(model, cancellationToken);
}

/// <summary>
/// Farkas 委托适配器。
/// Farkas delegate adapter.
/// </summary>
/// <typeparam name="M">待分析的模型类型 / Model type to analyze.</typeparam>
public sealed class FarkasAnalyzerAdapter<M> : IFarkasInfeasibilityAnalyzer<M> {
    private readonly Func<M, CancellationToken, Task<Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>>>> _delegate;

    /// <inheritdoc/>
    public InfeasibilityAnalyzerCapabilities Capabilities { get; }
    /// <inheritdoc/>
    public InfeasibilityEvidenceSource Source => InfeasibilityEvidenceSource.Farkas;

    /// <summary>创建 Farkas 适配器 / Create a Farkas adapter.</summary>
    /// <param name="delegate">Farkas 分析函数 / Farkas analysis function.</param>
    /// <param name="modelTypes">支持的模型类型 / Supported model types.</param>
    public FarkasAnalyzerAdapter(
        Func<M, CancellationToken, Task<Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>>>> @delegate,
        IReadOnlySet<SolverModelType>? modelTypes = null) {
        Capabilities = new InfeasibilityAnalyzerCapabilities(
            Source: InfeasibilityEvidenceSource.Farkas,
            ModelTypes: modelTypes,
            Exact: true);
        _delegate = @delegate;
    }

    /// <inheritdoc/>
    public Task<Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>>> AnalyzeAsync(
        M model,
        CancellationToken cancellationToken = default) =>
        _delegate(model, cancellationToken);
}

/// <summary>
/// 没有可用精确或降级策略时返回结构化 unavailable 证据。
/// Return structured unavailable evidence when no exact or fallback strategy is available.
/// </summary>
/// <typeparam name="M">待分析的模型类型 / Model type to analyze.</typeparam>
public sealed class UnavailableInfeasibilityAnalyzer<M> : IInfeasibilityAnalyzer<M> {
    /// <summary>默认不可用原因 / Default unavailable reason.</summary>
    public static readonly SolveIssue DefaultIssue = new(
        Code: "infeasibility-analysis-unavailable",
        Category: SolveIssueCategory.Unsupported,
        Message: "没有可用的不可行诊断路径 / No infeasibility diagnostic path is available");

    private readonly SolveIssue _issue;

    /// <inheritdoc/>
    public InfeasibilityEvidenceSource Source => InfeasibilityEvidenceSource.None;

    /// <summary>创建不可用分析器 / Create an unavailable analyzer.</summary>
    /// <param name="issue">不可用原因 / Unavailable reason.</param>
    public UnavailableInfeasibilityAnalyzer(SolveIssue? issue = null) {
        _issue = issue ?? DefaultIssue;
    }

    /// <inheritdoc/>
    public Task<Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>>> AnalyzeAsync(
        M model,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Results.Ok(new InfeasibilityEvidence {
            Source = InfeasibilityEvidenceSource.None,
            Exactness = EvidenceExactness.Unknown,
            Completeness = EvidenceCompleteness.Unavailable,
            Validity = EvidenceValidity.Unknown,
            Minimality = EvidenceMinimality.NotChecked,
            UnavailableReason = _issue,
        }));
}

/// <summary>不可行诊断策略顺序 / Ordered infeasibility diagnostic strategy.</summary>
public enum InfeasibilityDiagnosticStrategy {
    /// <summary>后端原生 IIS / Backend-native IIS.</summary>
    NativeIis,
    /// <summary>Farkas 证书 / Farkas certificate.</summary>
    Farkas,
    /// <summary>约束冲突 / Constraint conflict.</summary>
    ConstraintConflict,
    /// <summary>旧弹性过滤 / Legacy elastic filter.</summary>
    Legacy,
}

/// <summary>
/// 按声明顺序尝试分析器。
/// Try analyzers in declaration order.
/// </summary>
/// <typeparam name="M">待分析的模型类型 / Model type to analyze.</typeparam>
public sealed class InfeasibilityAnalyzerChain<M> {
    private readonly IReadOnlyList<IInfeasibilityAnalyzer<M>> _analyzers;

    /// <summary>创建分析器链 / Create an analyzer chain.</summary>
    /// <param name="analyzers">候选分析器 / Candidate analyzers.</param>
    public InfeasibilityAnalyzerChain(IReadOnlyList<IInfeasibilityAnalyzer<M>> analyzers) {
        _analyzers = analyzers;
    }

    /// <summary>返回首个成功证据，全部失败时返回最后一个错误 / Return the first evidence, otherwise the last failure.</summary>
    /// <param name="model">待分析模型 / Model to analyze.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>结构化证据或错误 / Structured evidence or an error.</returns>
    public async Task<Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>>> AnalyzeAsync(
        M model,
        CancellationToken cancellationToken = default) {
        Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>>? last = null;
        foreach (IInfeasibilityAnalyzer<M> analyzer in _analyzers) {
            Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>> result =
                await analyzer.AnalyzeAsync(model, cancellationToken);
            switch (result) {
                case Ok<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>> ok:
                    return ok;
                case Failed<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>>:
                    last = result;
                    break;
                case Fatal<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>> fatal:
                    return new Fatal<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>>(fatal.Errors);
            }
        }
        return last ?? Results.Failed<InfeasibilityEvidence>(new Err<ErrorCode>(
            ErrorCode.Other,
            "没有可用的不可行分析器 / No infeasibility analyzer is available"));
    }
}

/// <summary>
/// 按求解器能力选择原生 IIS、Farkas、冲突或旧策略的后端无关编排器。
/// Backend-neutral orchestrator selecting native IIS, Farkas, conflict and legacy analyzers by solver capability.
/// </summary>
/// <typeparam name="M">待分析的模型类型 / Model type to analyze.</typeparam>
public sealed class InfeasibilityDiagnosticOrchestrator<M> {
    private static readonly InfeasibilityEvidenceSource[] PreferredOrder = {
        InfeasibilityEvidenceSource.NativeIis,
        InfeasibilityEvidenceSource.Farkas,
        InfeasibilityEvidenceSource.ConstraintConflict,
        InfeasibilityEvidenceSource.ElasticFilter,
        InfeasibilityEvidenceSource.DeletionFilter,
    };

    private readonly IReadOnlyList<IInfeasibilityAnalyzer<M>> _analyzers;
    private readonly SolverCapabilities? _solverCapabilities;

    /// <summary>创建诊断编排器 / Create a diagnostic orchestrator.</summary>
    /// <param name="analyzers">候选分析器 / Candidate analyzers.</param>
    /// <param name="solverCapabilities">求解器能力；为空时不做能力过滤 / Solver capabilities; no capability filtering when null.</param>
    public InfeasibilityDiagnosticOrchestrator(
        IReadOnlyList<IInfeasibilityAnalyzer<M>> analyzers,
        SolverCapabilities? solverCapabilities = null) {
        _analyzers = analyzers;
        _solverCapabilities = solverCapabilities;
    }

    /// <summary>按策略优先级返回可用分析器 / Return available analyzers in strategy order.</summary>
    /// <returns>按优先级排序的分析器 / Analyzers ordered by priority.</returns>
    public IReadOnlyList<IInfeasibilityAnalyzer<M>> AvailableAnalyzers() =>
        _analyzers
            .Where(analyzer => analyzer is not ICapabilityAwareInfeasibilityAnalyzer<M> capability
                || Supports(capability.Capabilities))
            .OrderBy(analyzer => Array.IndexOf(PreferredOrder, analyzer.Source) is int index && index >= 0
                ? index
                : int.MaxValue)
            .ToArray();

    /// <summary>执行首个成功的诊断策略 / Execute the first successful diagnostic strategy.</summary>
    /// <param name="model">待分析模型 / Model to analyze.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>结构化证据或错误 / Structured evidence or an error.</returns>
    public Task<Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>>> AnalyzeAsync(
        M model,
        CancellationToken cancellationToken = default) {
        IReadOnlyList<IInfeasibilityAnalyzer<M>> available = AvailableAnalyzers();
        if (available.Count == 0) {
            return new UnavailableInfeasibilityAnalyzer<M>().AnalyzeAsync(model, cancellationToken);
        }
        return new InfeasibilityAnalyzerChain<M>(available).AnalyzeAsync(model, cancellationToken);
    }

    /// <summary>执行选中的分析器，并保留可选的兼容 artifact / Execute the selected analyzer while preserving an optional compatibility artifact.</summary>
    /// <typeparam name="A">兼容 artifact 类型 / Compatibility artifact type.</typeparam>
    /// <param name="model">待分析模型 / Model to analyze.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>物化证据或错误 / Materialized evidence or an error.</returns>
    public async Task<Result<MaterializedInfeasibilityEvidence<A>, ErrorCode, Error<ErrorCode>>>
        AnalyzeMaterializedAsync<A>(M model, CancellationToken cancellationToken = default) {
        Result<MaterializedInfeasibilityEvidence<A>, ErrorCode, Error<ErrorCode>>? last = null;
        foreach (IInfeasibilityAnalyzer<M> analyzer in AvailableAnalyzers()) {
            Result<MaterializedInfeasibilityEvidence<A>, ErrorCode, Error<ErrorCode>> result =
                analyzer is IMaterializingInfeasibilityAnalyzer<M, A> materializer
                ? await materializer.AnalyzeMaterializedAsync(model, cancellationToken)
                : Wrap<A>(await analyzer.AnalyzeAsync(model, cancellationToken));
            switch (result) {
                case Ok<MaterializedInfeasibilityEvidence<A>, ErrorCode, Error<ErrorCode>> ok:
                    return ok;
                case Failed<MaterializedInfeasibilityEvidence<A>, ErrorCode, Error<ErrorCode>>:
                    last = result;
                    break;
                case Fatal<MaterializedInfeasibilityEvidence<A>, ErrorCode, Error<ErrorCode>> fatal:
                    return new Fatal<MaterializedInfeasibilityEvidence<A>, ErrorCode, Error<ErrorCode>>(fatal.Errors);
            }
        }
        return last ?? Wrap<A>(
            await new UnavailableInfeasibilityAnalyzer<M>().AnalyzeAsync(model, cancellationToken));
    }

    /// <summary>把诊断结果附加到已有报告；分析失败只进入诊断错误，不改变原求解结论。</summary>
    /// <remarks>Attach diagnostic results without changing the original solve conclusion when the analysis fails.</remarks>
    /// <typeparam name="V">报告数值类型 / Report value type.</typeparam>
    /// <param name="report">已有求解报告 / Existing solve report.</param>
    /// <param name="model">待分析模型 / Model to analyze.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>附加证据或诊断错误后的报告 / Report with attached evidence or diagnostic errors.</returns>
    public async Task<SolveReport<V>> AnalyzeIntoAsync<V>(
        SolveReport<V> report,
        M model,
        CancellationToken cancellationToken = default) where V : struct {
        Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>> result =
            await AnalyzeAsync(model, cancellationToken);
        return result switch {
            Ok<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>> ok => report with {
                Diagnostics = report.Diagnostics with { InfeasibilityEvidence = ok.Value },
            },
            Failed<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>> failed => report with {
                Diagnostics = report.Diagnostics with {
                    Errors = [.. report.Diagnostics.Errors, ToIssue(failed.Error)],
                },
            },
            Fatal<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>> fatal => report with {
                Diagnostics = report.Diagnostics with {
                    Errors = [.. report.Diagnostics.Errors, .. fatal.Errors.Select(ToIssue)],
                },
            },
            _ => report,
        };
    }

    private static Result<MaterializedInfeasibilityEvidence<A>, ErrorCode, Error<ErrorCode>> Wrap<A>(
        Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>> result) =>
        result switch {
            Ok<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>> ok =>
                Results.Ok(new MaterializedInfeasibilityEvidence<A>(ok.Value, default)),
            Failed<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>> failed =>
                Results.Failed<MaterializedInfeasibilityEvidence<A>>(failed.Error),
            Fatal<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>> fatal =>
                new Fatal<MaterializedInfeasibilityEvidence<A>, ErrorCode, Error<ErrorCode>>(fatal.Errors),
            _ => Results.Failed<MaterializedInfeasibilityEvidence<A>>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                "不可行分析结果状态无效 / Invalid infeasibility analyzer result state")),
        };

    private bool Supports(InfeasibilityAnalyzerCapabilities capabilities) {
        if (_solverCapabilities is not { } solver) {
            return true;
        }
        if (capabilities.Source == InfeasibilityEvidenceSource.NativeIis && !solver.NativeIis) {
            return false;
        }
        if (capabilities.Source == InfeasibilityEvidenceSource.Farkas && !solver.Farkas) {
            return false;
        }
        if (capabilities.Source == InfeasibilityEvidenceSource.ConstraintConflict
            && solver.ConstraintProgrammingSupport(ConstraintProgrammingFeature.ConflictCore)
                == ConstraintProgrammingSupportLevel.Unsupported) {
            return false;
        }
        return capabilities.ModelTypes.Count == 0
            || solver.ModelTypes.Count == 0
            || capabilities.ModelTypes.Any(solver.ModelTypes.Contains);
    }

    private static SolveIssue ToIssue(Error<ErrorCode> error) => new(
        Code: error.Code.ToString() ?? "",
        Category: SolveIssueCategory.Backend,
        Message: error.Message);
}
