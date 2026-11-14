#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Solver.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Output;

namespace Fuookami.Ospf.Core.Solver.Report;

/// <summary>问题结论 / Problem conclusion.</summary>
public enum ProblemStatus {
    /// <summary>可行 / Feasible.</summary>
    Feasible,
    /// <summary>不可行 / Infeasible.</summary>
    Infeasible,
    /// <summary>无界 / Unbounded.</summary>
    Unbounded,
    /// <summary>不可行或无界 / Infeasible or unbounded.</summary>
    InfeasibleOrUnbounded,
    /// <summary>未知 / Unknown.</summary>
    Unknown,
}

/// <summary>求解终止原因 / Solve termination reason.</summary>
public enum TerminationReason {
    /// <summary>正常完成 / Completed.</summary>
    Completed,
    /// <summary>时间限制 / Time limit.</summary>
    TimeLimit,
    /// <summary>节点限制 / Node limit.</summary>
    NodeLimit,
    /// <summary>迭代限制 / Iteration limit.</summary>
    IterationLimit,
    /// <summary>解数量限制 / Solution limit.</summary>
    SolutionLimit,
    /// <summary>目标限制 / Objective limit.</summary>
    ObjectiveLimit,
    /// <summary>无新割停滞 / No-new-cut stall.</summary>
    Stalled,
    /// <summary>调用方取消 / Cancelled.</summary>
    Cancelled,
    /// <summary>外部中断 / Interrupted.</summary>
    Interrupted,
    /// <summary>数值失败 / Numerical failure.</summary>
    NumericalFailure,
    /// <summary>后端失败 / Backend failure.</summary>
    BackendFailure,
}

/// <summary>解存在性 / Solution presence.</summary>
public enum SolutionPresence {
    /// <summary>没有解 / No solution.</summary>
    None,
    /// <summary>有 incumbent / Incumbent exists.</summary>
    Incumbent,
    /// <summary>已证明最优 / Proven optimal.</summary>
    Optimal,
}

/// <summary>证明状态 / Proof status.</summary>
public enum ProofStatus {
    /// <summary>没有证明 / No proof.</summary>
    None,
    /// <summary>后端声称有证明 / Proof claimed by backend.</summary>
    Claimed,
    /// <summary>证明已复验 / Proof verified.</summary>
    Verified,
}

/// <summary>模型类型 / Solver model type.</summary>
public enum SolverModelType {
    /// <summary>线性规划 / Linear program.</summary>
    Lp,
    /// <summary>混合整数规划 / Mixed-integer program.</summary>
    Mip,
    /// <summary>二次规划 / Quadratic program.</summary>
    Qp,
    /// <summary>二次约束规划 / Quadratically constrained program.</summary>
    Qcp,
    /// <summary>约束规划 / Constraint program.</summary>
    Cp,
}

/// <summary>模型元素稳定标识 / Stable model-element identifier.</summary>
/// <param name="Value">标识文本 / Identifier text.</param>
public readonly record struct ModelElementId(string Value);

/// <summary>模型元素身份作用域 / Model-element identity scope.</summary>
public enum ModelElementScope {
    /// <summary>跨模型重建稳定 / Stable across model rebuilds.</summary>
    Stable,
    /// <summary>仅当前模型实例有效 / Valid only in the current model instance.</summary>
    ModelLocal,
}

/// <summary>模型元素来源 / Model-element origin.</summary>
/// <param name="Kind">来源类型 / Origin kind.</param>
/// <param name="Key">来源键 / Origin key.</param>
public sealed record ModelElementOrigin(string Kind, string Key);

/// <summary>模型元素类别 / Model-element kind.</summary>
public enum ModelElementKind {
    /// <summary>变量 / Variable.</summary>
    Variable,
    /// <summary>约束 / Constraint.</summary>
    Constraint,
    /// <summary>目标 / Objective.</summary>
    Objective,
}

/// <summary>变量稳定标识 / Stable variable identifier.</summary>
/// <param name="Value">标识文本 / Identifier text.</param>
public readonly record struct VariableId(string Value);

/// <summary>约束稳定标识 / Stable constraint identifier.</summary>
/// <param name="Value">标识文本 / Identifier text.</param>
public readonly record struct ConstraintId(string Value);

/// <summary>目标稳定标识 / Stable objective identifier.</summary>
/// <param name="Value">标识文本 / Identifier text.</param>
public readonly record struct ObjectiveId(string Value);

/// <summary>求解运行标识 / Solve run identifier.</summary>
/// <param name="Value">标识文本 / Identifier text.</param>
public readonly record struct SolveRunId(string Value);

/// <summary>求解尝试标识 / Solve attempt identifier.</summary>
/// <param name="Value">标识文本 / Identifier text.</param>
public readonly record struct SolveAttemptId(string Value);

/// <summary>变量边界方向 / Variable bound side.</summary>
public enum BoundSide {
    /// <summary>下界 / Lower bound.</summary>
    Lower,
    /// <summary>上界 / Upper bound.</summary>
    Upper,
}

/// <summary>稳定变量边界引用 / Stable variable-bound reference.</summary>
/// <param name="VariableId">变量标识 / Variable identifier.</param>
/// <param name="Side">边界方向 / Bound side.</param>
public sealed record VariableBoundRef(VariableId VariableId, BoundSide Side);

/// <summary>稳定变量值域引用 / Stable variable-domain reference.</summary>
/// <param name="VariableId">变量标识 / Variable identifier.</param>
public sealed record VariableDomainRef(VariableId VariableId);

/// <summary>诊断问题类别 / Diagnostic issue category.</summary>
public enum SolveIssueCategory {
    /// <summary>非法输入 / Invalid input.</summary>
    InvalidInput,
    /// <summary>环境问题 / Environment.</summary>
    Environment,
    /// <summary>许可问题 / License.</summary>
    License,
    /// <summary>数值问题 / Numerical.</summary>
    Numerical,
    /// <summary>回调问题 / Callback.</summary>
    Callback,
    /// <summary>解析问题 / Parsing.</summary>
    Parsing,
    /// <summary>后端问题 / Backend.</summary>
    Backend,
    /// <summary>协议问题 / Protocol.</summary>
    Protocol,
    /// <summary>不支持 / Unsupported.</summary>
    Unsupported,
}

/// <summary>结构化求解问题 / Structured solve issue.</summary>
/// <param name="Code">稳定问题码 / Stable issue code.</param>
/// <param name="Category">问题类别 / Issue category.</param>
/// <param name="Message">脱敏消息 / Redacted message.</param>
/// <param name="Details">结构化详情 / Structured details.</param>
public sealed record SolveIssue(
    string Code,
    SolveIssueCategory Category,
    string Message,
    IReadOnlyDictionary<string, string>? Details = null) {
    /// <summary>结构化详情 / Structured details.</summary>
    public IReadOnlyDictionary<string, string> Details { get; init; } =
        Details ?? ReadOnlyDictionary<string, string>.Empty;
}

/// <summary>求解器能力声明 / Solver capability declaration.</summary>
public sealed record SolverCapabilities(
    IReadOnlySet<SolverModelType> ModelTypes,
    bool NativeIis = false,
    bool Dual = false,
    bool Farkas = false,
    bool WarmStart = false,
    bool SolutionPool = false,
    bool Callback = false,
    bool Interrupt = false,
    bool Checkpoint = false,
    bool Resume = false,
    IReadOnlyDictionary<ConstraintProgrammingFeature, ConstraintProgrammingSupportLevel>? ConstraintProgrammingFeatures = null) {
    /// <summary>CP 特性及支持级别 / CP features and support levels.</summary>
    public IReadOnlyDictionary<ConstraintProgrammingFeature, ConstraintProgrammingSupportLevel>
        ConstraintProgrammingFeatures { get; init; } =
            ConstraintProgrammingFeatures
            ?? new Dictionary<ConstraintProgrammingFeature, ConstraintProgrammingSupportLevel>();
}

/// <summary>求解器描述符 / Solver descriptor.</summary>
public sealed record SolverDescriptor(
    string SolverId,
    string BackendName,
    SolverCapabilities Capabilities,
    string? BackendVersion = null,
    string? PluginVersion = null);

/// <summary>后端配置参数值 / Backend configuration parameter value.</summary>
public abstract record BackendParameterValue {
    /// <summary>文本值 / Text value.</summary>
    /// <param name="Value">文本值 / Text value.</param>
    public sealed record Text(string Value) : BackendParameterValue;

    /// <summary>整数值 / Integer value.</summary>
    /// <param name="Value">整数值 / Integer value.</param>
    public sealed record Integer(long Value) : BackendParameterValue;

    /// <summary>十进制值 / Decimal value.</summary>
    /// <param name="Value">确定性十进制文本 / Deterministic decimal text.</param>
    public sealed record Decimal(string Value) : BackendParameterValue;

    /// <summary>布尔值 / Boolean value.</summary>
    /// <param name="Value">布尔值 / Boolean value.</param>
    public sealed record BooleanValue(bool Value) : BackendParameterValue;
}

/// <summary>脱敏后端参数 / Redacted backend parameter.</summary>
public sealed record BackendParameter(
    string Name,
    BackendParameterValue Value,
    bool Sensitive = false) {
    /// <summary>获取可公开的脱敏值 / Get the redacted public value.</summary>
    /// <returns>脱敏值文本 / Redacted value text.</returns>
    public string RedactedValue() {
        if (Sensitive) {
            return "<redacted>";
        }
        return Value switch {
            BackendParameterValue.Text text => text.Value,
            BackendParameterValue.Integer integer => integer.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            BackendParameterValue.Decimal decimalValue => decimalValue.Value,
            BackendParameterValue.BooleanValue boolean => boolean.Value.ToString(),
            _ => string.Empty,
        };
    }

    internal string CanonicalValue() {
        if (Sensitive) {
            return "redacted:<redacted>";
        }
        return Value switch {
            BackendParameterValue.Text text => $"text:{text.Value}",
            BackendParameterValue.Integer integer => $"integer:{integer.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            BackendParameterValue.Decimal decimalValue => $"decimal:{decimalValue.Value}",
            BackendParameterValue.BooleanValue boolean => $"boolean:{boolean.Value}",
            _ => string.Empty,
        };
    }
}

/// <summary>可序列化后端配置快照 / Serializable backend configuration snapshot.</summary>
public sealed record BackendConfigurationSnapshot(
    string Type,
    IReadOnlyList<BackendParameter> Parameters) {
    /// <summary>生成确定性配置指纹 / Generate a deterministic configuration fingerprint.</summary>
    /// <returns>配置指纹 / Configuration fingerprint.</returns>
    public AuditFingerprint Fingerprint() {
        string canonical = string.Join(
            separator: "\n",
            values: Parameters
                .OrderBy(parameter => parameter.Name, StringComparer.Ordinal)
                .Select(parameter =>
                    $"{parameter.Name}={parameter.CanonicalValue()}|sensitive={parameter.Sensitive}"));
        return SolveFingerprinting.Sha256(
            canonicalContent: $"type={Type}\n{canonical}",
            schemaVersion: "backend-config-1");
    }
}

/// <summary>可审计后端配置 / Auditable backend configuration.</summary>
public interface IBackendConfiguration {
    /// <summary>配置类型 / Configuration type.</summary>
    string Type { get; }

    /// <summary>获取脱敏参数 / Get redacted parameters.</summary>
    /// <returns>脱敏参数 / Redacted parameters.</returns>
    IReadOnlyList<BackendParameter> Parameters();

    /// <summary>生成脱敏快照 / Build a redacted snapshot.</summary>
    /// <returns>配置快照 / Configuration snapshot.</returns>
    BackendConfigurationSnapshot Snapshot() => new(Type, Parameters());
}

/// <summary>求解器执行来源 / Solver execution provenance.</summary>
public sealed record SolverProvenance(
    SolverDescriptor Descriptor,
    string? NativeVersion = null,
    IReadOnlyDictionary<string, string>? EffectiveParameters = null,
    IReadOnlyDictionary<string, string>? IgnoredParameters = null,
    BackendConfigurationSnapshot? Configuration = null,
    int? ThreadCount = null,
    long? RandomSeed = null,
    bool? Deterministic = null,
    IReadOnlyDictionary<string, string>? EnvironmentSummary = null) {
    /// <summary>生效参数 / Effective parameters.</summary>
    public IReadOnlyDictionary<string, string> EffectiveParameters { get; init; } =
        EffectiveParameters ?? new Dictionary<string, string>();
    /// <summary>忽略参数 / Ignored parameters.</summary>
    public IReadOnlyDictionary<string, string> IgnoredParameters { get; init; } =
        IgnoredParameters ?? new Dictionary<string, string>();
    /// <summary>环境摘要 / Environment summary.</summary>
    public IReadOnlyDictionary<string, string> EnvironmentSummary { get; init; } =
        EnvironmentSummary ?? new Dictionary<string, string>();
}

/// <summary>求解统计 / Solve statistics.</summary>
public sealed record SolveStatistics(
    TimeSpan? SolveTime = null,
    ulong? Iterations = null,
    ulong? Nodes = null,
    Flt64? BestBound = null,
    Flt64? Gap = null);

/// <summary>求解解及解池 / Solve solution and pool.</summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed record SolveSolution<V>(
    Solution<V> Values,
    V? Objective = null,
    IReadOnlyList<Solution<V>>? Pool = null)
    where V : struct {
    /// <summary>解池 / Solution pool.</summary>
    public IReadOnlyList<Solution<V>> Pool { get; init; } = Pool ?? Array.Empty<Solution<V>>();
    /// <summary>解向量长度 / Solution vector length.</summary>
    public int Length => Values.Length;
    /// <summary>按索引访问 / Access by index.</summary>
    /// <param name="index">索引 / Index.</param>
    /// <returns>解值 / Solution value.</returns>
    public V this[int index] => Values[index];
}

/// <summary>求解证明 / Solve proof.</summary>
public sealed record SolveProof(
    ProofStatus Status,
    string? Kind = null,
    string? Reference = null);

/// <summary>求解诊断约束关系 / Solve diagnostic constraint relation.</summary>
public enum SolveConstraintRelation {
    /// <summary>小于等于 / Less than or equal.</summary>
    LessEqual,
    /// <summary>等于 / Equal.</summary>
    Equal,
    /// <summary>大于等于 / Greater than or equal.</summary>
    GreaterEqual,
    /// <summary>区间 / Range.</summary>
    Range,
}

/// <summary>约束求值 / Constraint evaluation.</summary>
public sealed record ConstraintEvaluation<V>(
    ConstraintId ConstraintId,
    V Lhs,
    V Rhs,
    SolveConstraintRelation Relation,
    V Slack,
    V Violation,
    V Tolerance,
    bool Satisfied,
    V? Dual = null)
    where V : struct;

/// <summary>变量边界求值 / Variable-bound evaluation.</summary>
public sealed record VariableBoundEvaluation<V>(
    VariableId VariableId,
    BoundSide Side,
    V Bound,
    V Value,
    V Slack,
    V Violation,
    V Tolerance,
    bool Satisfied)
    where V : struct;

/// <summary>不可行证据来源 / Infeasibility evidence source.</summary>
public enum InfeasibilityEvidenceSource {
    /// <summary>原生 IIS / Native IIS.</summary>
    NativeIis,
    /// <summary>Farkas 证书 / Farkas certificate.</summary>
    Farkas,
    /// <summary>约束冲突 / Constraint conflict.</summary>
    ConstraintConflict,
    /// <summary>弹性过滤 / Elastic filter.</summary>
    ElasticFilter,
    /// <summary>删除过滤 / Deletion filter.</summary>
    DeletionFilter,
    /// <summary>无证据 / No evidence.</summary>
    None,
}

/// <summary>证据有效性 / Evidence validity.</summary>
public enum EvidenceValidity {
    /// <summary>已复验 / Verified.</summary>
    Verified,
    /// <summary>启发式 / Heuristic.</summary>
    Heuristic,
    /// <summary>未知 / Unknown.</summary>
    Unknown,
}

/// <summary>证据最小性 / Evidence minimality.</summary>
public enum EvidenceMinimality {
    /// <summary>不可约 / Irreducible.</summary>
    Irreducible,
    /// <summary>部分 / Partial.</summary>
    Partial,
    /// <summary>未检查 / Not checked.</summary>
    NotChecked,
}

/// <summary>不可行证据精度 / Infeasibility evidence exactness.</summary>
public enum EvidenceExactness {
    /// <summary>精确 / Exact.</summary>
    Exact,
    /// <summary>不可约 / Irreducible.</summary>
    Irreducible,
    /// <summary>启发式 / Heuristic.</summary>
    Heuristic,
    /// <summary>未知 / Unknown.</summary>
    Unknown,
}

/// <summary>不可行证据完整度 / Infeasibility evidence completeness.</summary>
public enum EvidenceCompleteness {
    /// <summary>完整 / Complete.</summary>
    Complete,
    /// <summary>部分 / Partial.</summary>
    Partial,
    /// <summary>不可用 / Unavailable.</summary>
    Unavailable,
}

/// <summary>结构化不可行成员 / Structured infeasibility member.</summary>
public abstract record InfeasibilityMember {
    /// <summary>原始约束成员 / Original constraint member.</summary>
    /// <param name="Id">约束标识 / Constraint identifier.</param>
    public sealed record ConstraintMember(ConstraintId Id) : InfeasibilityMember;
    /// <summary>变量边界成员 / Variable-bound member.</summary>
    /// <param name="Ref">变量边界引用 / Variable-bound reference.</param>
    public sealed record VariableBoundMember(VariableBoundRef Ref) : InfeasibilityMember;
    /// <summary>变量值域成员 / Variable-domain member.</summary>
    /// <param name="Ref">变量值域引用 / Variable-domain reference.</param>
    public sealed record VariableDomainMember(VariableDomainRef Ref) : InfeasibilityMember;
}

/// <summary>不可行证据 / Infeasibility evidence.</summary>
public sealed record InfeasibilityEvidence {
    /// <summary>证据来源 / Evidence source.</summary>
    public required InfeasibilityEvidenceSource Source { get; init; }
    /// <summary>证据精度 / Evidence exactness.</summary>
    public EvidenceExactness Exactness { get; init; } = EvidenceExactness.Unknown;
    /// <summary>证据完整度 / Evidence completeness.</summary>
    public EvidenceCompleteness Completeness { get; init; } = EvidenceCompleteness.Unavailable;
    /// <summary>约束标识集合 / Constraint identifiers.</summary>
    public IReadOnlySet<ConstraintId> ConstraintIds { get; init; } = new HashSet<ConstraintId>();
    /// <summary>变量标识兼容字段 / Variable identifier compatibility field.</summary>
    public IReadOnlySet<VariableId> VariableBoundIds { get; init; } = new HashSet<VariableId>();
    /// <summary>诊断耗时 / Diagnostic elapsed time.</summary>
    public TimeSpan? Elapsed { get; init; }
    /// <summary>证据不可用原因 / Reason evidence is unavailable.</summary>
    public SolveIssue? UnavailableReason { get; init; }
    /// <summary>证据有效性 / Evidence validity.</summary>
    public EvidenceValidity Validity { get; init; } = EvidenceValidity.Unknown;
    /// <summary>证据最小性 / Evidence minimality.</summary>
    public EvidenceMinimality Minimality { get; init; } = EvidenceMinimality.NotChecked;
    /// <summary>变量边界引用 / Variable-bound references.</summary>
    public IReadOnlySet<VariableBoundRef> VariableBoundRefs { get; init; } = new HashSet<VariableBoundRef>();
    /// <summary>变量值域引用 / Variable-domain references.</summary>
    public IReadOnlySet<VariableDomainRef> VariableDomainRefs { get; init; } = new HashSet<VariableDomainRef>();
    /// <summary>结构化证据成员 / Structured evidence members.</summary>
    public IReadOnlySet<InfeasibilityMember> Members { get; init; } = new HashSet<InfeasibilityMember>();
    /// <summary>外部证据引用 / External evidence reference.</summary>
    public string? Reference { get; init; }
    /// <summary>后端假设成员 / Backend assumption members.</summary>
    public IReadOnlySet<VariableId> AssumptionIds { get; init; } = new HashSet<VariableId>();
    /// <summary>诊断复验次数 / Diagnostic verification checks.</summary>
    public ulong? VerificationChecks { get; init; }
    /// <summary>诊断复验终止原因 / Diagnostic verification termination reason.</summary>
    public TerminationReason? TerminationReason { get; init; }
}

/// <summary>求解诊断 / Solve diagnostics.</summary>
public sealed record SolveDiagnostics<V>(
    IReadOnlyList<ConstraintEvaluation<V>>? ConstraintEvaluations = null,
    IReadOnlyList<VariableBoundEvaluation<V>>? VariableBoundEvaluations = null,
    InfeasibilityEvidence? InfeasibilityEvidence = null,
    IReadOnlyList<SolveIssue>? Warnings = null,
    IReadOnlyList<SolveIssue>? Errors = null)
    where V : struct {
    /// <summary>约束求值 / Constraint evaluations.</summary>
    public IReadOnlyList<ConstraintEvaluation<V>> ConstraintEvaluations { get; init; } =
        ConstraintEvaluations ?? Array.Empty<ConstraintEvaluation<V>>();
    /// <summary>变量边界求值 / Variable-bound evaluations.</summary>
    public IReadOnlyList<VariableBoundEvaluation<V>> VariableBoundEvaluations { get; init; } =
        VariableBoundEvaluations ?? Array.Empty<VariableBoundEvaluation<V>>();
    /// <summary>警告 / Warnings.</summary>
    public IReadOnlyList<SolveIssue> Warnings { get; init; } =
        Warnings ?? Array.Empty<SolveIssue>();
    /// <summary>错误 / Errors.</summary>
    public IReadOnlyList<SolveIssue> Errors { get; init; } =
        Errors ?? Array.Empty<SolveIssue>();
}

/// <summary>带算法和 schema 版本的审计指纹 / Audit fingerprint with algorithm and schema version.</summary>
public sealed record AuditFingerprint(
    string SchemaVersion,
    string Algorithm,
    string Value);

/// <summary>模型指纹 / Model fingerprint.</summary>
public readonly record struct ModelFingerprint(AuditFingerprint Value);
/// <summary>配置指纹 / Configuration fingerprint.</summary>
public readonly record struct ConfigurationFingerprint(AuditFingerprint Value);
/// <summary>求解器指纹 / Solver fingerprint.</summary>
public readonly record struct SolverFingerprint(AuditFingerprint Value);

/// <summary>求解指纹集合 / Solve fingerprint set.</summary>
public sealed record SolveFingerprints(
    AuditFingerprint? Model = null,
    AuditFingerprint? Configuration = null,
    AuditFingerprint? Solver = null);

/// <summary>组合求解选择依据 / Combinatorial solve selection reason.</summary>
public enum SolveSelectionReason {
    /// <summary>第一个可行尝试 / First feasible attempt.</summary>
    FirstFeasible,
    /// <summary>最佳目标 / Best objective.</summary>
    BestObjective,
    /// <summary>最佳界 / Best bound.</summary>
    BestBound,
    /// <summary>首选后端 / Preferred backend.</summary>
    PreferredBackend,
    /// <summary>没有成功尝试 / No successful attempt.</summary>
    NoSuccessfulAttempt,
}

/// <summary>单次后端尝试轨迹 / Single backend attempt trace.</summary>
public sealed record SolveAttemptTrace<V>(
    SolveAttemptId AttemptId,
    string BackendId,
    SolveAttemptId? ParentAttemptId = null,
    SolveReport<V>? Report = null,
    TimeSpan? Elapsed = null,
    IReadOnlyList<SolveIssue>? Errors = null,
    string? CancellationReason = null)
    where V : struct {
    /// <summary>尝试错误 / Attempt errors.</summary>
    public IReadOnlyList<SolveIssue> Errors { get; init; } = Errors ?? Array.Empty<SolveIssue>();
}

/// <summary>统一求解报告 / Unified solve report.</summary>
/// <typeparam name="V">解与诊断数值类型 / Solution and diagnostic value type.</typeparam>
public sealed record SolveReport<V> : UnifiedSolverOutput, ILinearSolverOutputMarker, IQuadraticSolverOutputMarker
    where V : struct {
    /// <summary>当前报告 schema 版本 / Current report schema version.</summary>
    public const string CurrentSchemaVersion = "1.0";

    /// <summary>报告 schema 版本 / Report schema version.</summary>
    public string SchemaVersion { get; init; } = CurrentSchemaVersion;
    /// <summary>求解运行标识 / Solve run identifier.</summary>
    public SolveRunId? RunId { get; init; }
    /// <summary>问题结论 / Problem status.</summary>
    public required ProblemStatus ProblemStatus { get; init; }
    /// <summary>终止原因 / Termination reason.</summary>
    public required TerminationReason TerminationReason { get; init; }
    /// <summary>解存在性 / Solution presence.</summary>
    public required SolutionPresence SolutionPresence { get; init; }
    /// <summary>精确解与目标值 / Exact solution and objective value.</summary>
    public SolveSolution<V>? Solution { get; init; }
    /// <summary>证明状态 / Proof status.</summary>
    public SolveProof Proof { get; init; } = new(ProofStatus.None);
    /// <summary>求解统计 / Solve statistics.</summary>
    public SolveStatistics Statistics { get; init; } = new();
    /// <summary>求解诊断 / Solve diagnostics.</summary>
    public SolveDiagnostics<V> Diagnostics { get; init; } = new();
    /// <summary>求解器来源 / Solver provenance.</summary>
    public SolverProvenance? Provenance { get; init; }
    /// <summary>审计指纹 / Audit fingerprints.</summary>
    public SolveFingerprints Fingerprints { get; init; } = new();
    /// <summary>后端尝试轨迹 / Backend attempt traces.</summary>
    public IReadOnlyList<SolveAttemptTrace<V>> Attempts { get; init; } = Array.Empty<SolveAttemptTrace<V>>();
    /// <summary>选中的后端尝试 / Selected backend attempt.</summary>
    public SolveAttemptId? SelectedAttemptId { get; init; }
    /// <summary>选择依据 / Selection reason.</summary>
    public SolveSelectionReason? SelectionReason { get; init; }

    /// <summary>主解值；无 incumbent 时为空 / Main values; empty without an incumbent.</summary>
    public Solution<V> Values => Solution?.Values ?? new Solution<V>(Array.Empty<V>());
    /// <inheritdoc/>
    public new ulong? Iterations => Statistics.Iterations;
    /// <inheritdoc/>
    public new ulong? NodeCount => Statistics.Nodes;
    /// <inheritdoc/>
    public new Flt64? BestBound => Statistics.BestBound;
    /// <inheritdoc/>
    public new Flt64? MipGap => Statistics.Gap;
    /// <inheritdoc/>
    public new TimeSpan? SolveTime => Statistics.SolveTime;

    /// <summary>更新运行时元数据和解池 / Update runtime metadata and solution pool.</summary>
    /// <param name="runId">运行标识 / Run identifier.</param>
    /// <param name="provenance">求解器来源 / Solver provenance.</param>
    /// <param name="fingerprints">审计指纹 / Audit fingerprints.</param>
    /// <param name="solutionPool">解池 / Solution pool.</param>
    /// <returns>更新后的报告 / Updated report.</returns>
    public SolveReport<V> WithMetadata(
        SolveRunId? runId = null,
        SolverProvenance? provenance = null,
        SolveFingerprints? fingerprints = null,
        IReadOnlyList<Solution<V>>? solutionPool = null) {
        SolveSolution<V>? solution = Solution;
        if (solution is not null &&
            solutionPool is not null &&
            solutionPool.Count > 0) {
            solution = solution with { Pool = solutionPool };
        }
        return this with {
            RunId = runId ?? RunId,
            Provenance = provenance ?? Provenance,
            Fingerprints = fingerprints ?? Fingerprints,
            Solution = solution,
        };
    }
}

/// <summary>组合求解报告 / Combinatorial solve report.</summary>
public sealed record CombinatorialSolveReport<V>(
    SolveReport<V>? FinalReport,
    IReadOnlyList<SolveAttemptTrace<V>> Attempts,
    SolveAttemptId? SelectedAttemptId,
    SolveSelectionReason SelectionReason)
    where V : struct;
