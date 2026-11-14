#nullable enable

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Core.Solver.Output;

/// <summary>CP 冲突最小性 / CP conflict minimality.</summary>
public enum ConstraintProgrammingConflictMinimality {
    /// <summary>未执行必要性检查 / No irreducibility checks were performed.</summary>
    NotChecked,
    /// <summary>所有剩余候选均通过删除复验 / Every remaining candidate passed deletion checks.</summary>
    Irreducible,
    /// <summary>至少一次删除复验未能证明终态 / At least one deletion check lacked a proven terminal state.</summary>
    Partial,
}

/// <summary>后端无关的 CP 冲突 / Backend-neutral CP conflict.</summary>
public sealed record ConstraintProgrammingConflict {
    /// <summary>创建 CP 冲突 / Create a CP conflict.</summary>
    /// <param name="constraintIds">冲突约束 ID / Conflicting constraint IDs.</param>
    /// <param name="variableIds">冲突变量 ID / Conflicting variable IDs.</param>
    /// <param name="message">冲突说明 / Conflict message.</param>
    /// <param name="assumptions">参与冲突的假设 / Participating assumptions.</param>
    /// <param name="minimality">最小性状态 / Minimality status.</param>
    /// <param name="members">原始模型成员 / Original model members.</param>
    /// <param name="activationIds">稳定 activation ID / Stable activation IDs.</param>
    /// <param name="activationMembers">activation 到原始成员的映射 / Activation-to-member projection.</param>
    /// <param name="validity">冲突有效性 / Conflict validity.</param>
    /// <param name="verificationChecks">复验次数 / Verification-check count.</param>
    /// <param name="terminationReason">复验终止原因 / Verification termination reason.</param>
    public ConstraintProgrammingConflict(
        IEnumerable<ConstraintId>? constraintIds = null,
        IEnumerable<VariableId>? variableIds = null,
        string? message = null,
        IEnumerable<BooleanLiteral>? assumptions = null,
        ConstraintProgrammingConflictMinimality minimality =
            ConstraintProgrammingConflictMinimality.NotChecked,
        IEnumerable<InfeasibilityMember>? members = null,
        IEnumerable<string>? activationIds = null,
        IReadOnlyDictionary<string, InfeasibilityMember>? activationMembers = null,
        EvidenceValidity validity = EvidenceValidity.Verified,
        ulong? verificationChecks = null,
        TerminationReason? terminationReason = null) {
        ConstraintIds = (constraintIds ?? Array.Empty<ConstraintId>()).ToFrozenSet();
        VariableIds = (variableIds ?? Array.Empty<VariableId>()).ToFrozenSet();
        Message = message;
        Assumptions = (assumptions ?? Array.Empty<BooleanLiteral>()).ToList().AsReadOnly();
        Minimality = minimality;
        Members = (members ?? Array.Empty<InfeasibilityMember>()).ToFrozenSet();
        ActivationIds = (activationIds ?? Array.Empty<string>()).ToFrozenSet();
        ActivationMembers = activationMembers is null
            ? ReadOnlyDictionary<string, InfeasibilityMember>.Empty
            : new ReadOnlyDictionary<string, InfeasibilityMember>(
                activationMembers.ToDictionary(pair => pair.Key, pair => pair.Value));
        Validity = validity;
        VerificationChecks = verificationChecks;
        TerminationReason = terminationReason;
    }

    /// <summary>冲突约束 ID / Conflicting constraint IDs.</summary>
    public IReadOnlySet<ConstraintId> ConstraintIds { get; }
    /// <summary>冲突变量 ID / Conflicting variable IDs.</summary>
    public IReadOnlySet<VariableId> VariableIds { get; }
    /// <summary>冲突说明 / Conflict message.</summary>
    public string? Message { get; }
    /// <summary>参与冲突的假设 / Participating assumptions.</summary>
    public IReadOnlyList<BooleanLiteral> Assumptions { get; }
    /// <summary>最小性状态 / Minimality status.</summary>
    public ConstraintProgrammingConflictMinimality Minimality { get; }
    /// <summary>原始模型成员 / Original model members.</summary>
    public IReadOnlySet<InfeasibilityMember> Members { get; }
    /// <summary>稳定 activation ID / Stable activation IDs.</summary>
    public IReadOnlySet<string> ActivationIds { get; }
    /// <summary>activation 到原始成员的映射 / Activation-to-member projection.</summary>
    public IReadOnlyDictionary<string, InfeasibilityMember> ActivationMembers { get; }
    /// <summary>冲突有效性 / Conflict validity.</summary>
    public EvidenceValidity Validity { get; }
    /// <summary>复验次数 / Verification-check count.</summary>
    public ulong? VerificationChecks { get; }
    /// <summary>复验终止原因 / Verification termination reason.</summary>
    public TerminationReason? TerminationReason { get; }
}

/// <summary>精确整数 CP 解 / Exact-integer CP solution.</summary>
public sealed record ConstraintProgrammingSolution {
    private static readonly long MaxCompatibleDoubleInteger = 9_007_199_254_740_991L;

    /// <summary>创建 CP 解 / Create a CP solution.</summary>
    /// <param name="values">稳定变量 ID 到整数值的映射 / Stable variable ID to integer value mapping.</param>
    /// <param name="intervals">interval 解映射 / Interval solution mapping.</param>
    public ConstraintProgrammingSolution(
        IReadOnlyDictionary<VariableId, Int64>? values = null,
        IReadOnlyDictionary<IntervalId, IntervalValue>? intervals = null) {
        Values = values is null
            ? ReadOnlyDictionary<VariableId, Int64>.Empty
            : new ReadOnlyDictionary<VariableId, Int64>(
                values.ToDictionary(pair => pair.Key, pair => pair.Value));
        Intervals = intervals is null
            ? ReadOnlyDictionary<IntervalId, IntervalValue>.Empty
            : new ReadOnlyDictionary<IntervalId, IntervalValue>(
                intervals.ToDictionary(pair => pair.Key, pair => pair.Value));
    }

    /// <summary>变量解 / Variable values.</summary>
    public IReadOnlyDictionary<VariableId, Int64> Values { get; }

    /// <summary>interval 解 / Interval values.</summary>
    public IReadOnlyDictionary<IntervalId, IntervalValue> Intervals { get; }

    /// <summary>按稳定变量 ID 获取值 / Get a value by stable variable ID.</summary>
    /// <param name="id">稳定变量 ID / Stable variable ID.</param>
    /// <returns>整数值或缺少数据错误 / Integer value or a missing-data error.</returns>
    public Result<Int64, ErrorCode, Error<ErrorCode>> Value(VariableId id) =>
        Values.TryGetValue(id, out Int64 value)
            ? Results.Ok(value)
            : Failure(
                $"缺少 CP 解变量：{id.Value} / CP solution does not contain variable: {id.Value}");

    /// <summary>按 OSPF 变量获取值 / Get a value by an OSPF variable.</summary>
    /// <param name="variable">OSPF 变量 / OSPF variable.</param>
    /// <returns>整数值或缺少数据错误 / Integer value or a missing-data error.</returns>
    public Result<Int64, ErrorCode, Error<ErrorCode>> Value(IVariableItem variable) {
        ArgumentNullException.ThrowIfNull(variable);
        return Value(new VariableId($"{variable.Identifier}:{variable.Index}"));
    }

    /// <summary>获取二值变量值 / Get a binary-variable value.</summary>
    /// <param name="variable">二值变量 / Binary variable.</param>
    /// <returns>布尔值或缺少数据错误 / Boolean value or a missing-data error.</returns>
    public Result<bool, ErrorCode, Error<ErrorCode>> Boolean(BinVar variable) =>
        Value(variable).Map(value => value == Int64.One);

    /// <summary>获取 interval 解 / Get an interval solution.</summary>
    /// <param name="id">interval ID / Interval ID.</param>
    /// <returns>interval 值或缺少数据错误 / Interval value or a missing-data error.</returns>
    public Result<IntervalValue, ErrorCode, Error<ErrorCode>> Interval(IntervalId id) {
        if (!Intervals.TryGetValue(id, out IntervalValue? value) || value is null) {
            return Results.Failed<IntervalValue>(
                new Err<ErrorCode>(
                    code: ErrorCode.DataNotFound,
                    message: $"缺少 CP 解 interval：{id.Value} / CP solution does not contain interval: {id.Value}"));
        }
        return Results.Ok(value);
    }

    /// <summary>按请求顺序生成兼容解向量 / Build a compatible solution vector in request order.</summary>
    /// <param name="order">请求的变量顺序 / Requested variable order.</param>
    /// <returns>仅包含已有值的顺序向量 / Ordered values containing only present entries.</returns>
    public Solution<Int64> AsList(IEnumerable<VariableId> order) {
        ArgumentNullException.ThrowIfNull(order);
        return new Solution<Int64>(
            order
                .Where(Values.ContainsKey)
                .Select(id => Values[id])
                .ToList());
    }

    /// <summary>在不丢失精度时转换兼容浮点值 / Convert to a compatible floating value without precision loss.</summary>
    /// <param name="value">精确整数 / Exact integer.</param>
    /// <returns>兼容浮点值；超出精确范围时为空 / Compatible value, or null outside the lossless range.</returns>
    public static Flt64? ToCompatibilityFlt64(Int64 value) {
        long raw = value.ToLong();
        return raw >= -MaxCompatibleDoubleInteger && raw <= MaxCompatibleDoubleInteger
            ? value.ToFlt64()
            : null;
    }

    private static Result<Int64, ErrorCode, Error<ErrorCode>> Failure(string message) =>
        Results.Failed<Int64>(
            new Err<ErrorCode>(
                code: ErrorCode.DataNotFound,
                message: message));
}

/// <summary>CP 求解器输出 / CP solver output.</summary>
public abstract record ConstraintProgrammingSolverOutput : SolverOutput {
    /// <summary>初始化 CP 输出 / Initialize CP output.</summary>
    /// <param name="report">统一求解报告 / Unified solve report.</param>
    protected ConstraintProgrammingSolverOutput(SolveReport<Int64>? report = null) {
        Report = report;
    }

    /// <summary>统一求解报告 / Unified solve report.</summary>
    public SolveReport<Int64>? Report { get; }
}

/// <summary>CP 可行或最优输出 / CP feasible or optimal output.</summary>
public sealed record ConstraintProgrammingFeasibleOutput : ConstraintProgrammingSolverOutput {
    /// <summary>创建 CP 可行输出 / Create a CP feasible output.</summary>
    /// <param name="solution">CP 整数解 / CP integer solution.</param>
    /// <param name="objective">兼容浮点目标 / Compatibility floating objective.</param>
    /// <param name="bestBound">兼容浮点最佳界 / Compatibility floating best bound.</param>
    /// <param name="status">求解状态 / Solver status.</param>
    /// <param name="proofStatus">证明状态 / Proof status.</param>
    /// <param name="report">统一报告 / Unified report.</param>
    /// <param name="exactObjective">精确整数目标 / Exact integer objective.</param>
    public ConstraintProgrammingFeasibleOutput(
        ConstraintProgrammingSolution solution,
        Flt64? objective = null,
        Flt64? bestBound = null,
        SolverStatus status = SolverStatus.Optimal,
        ProofStatus proofStatus = ProofStatus.None,
        SolveReport<Int64>? report = null,
        Int64? exactObjective = null)
        : base(report) {
        ArgumentNullException.ThrowIfNull(solution);
        Solution = solution;
        Objective = objective;
        BestBound = bestBound;
        Status = status;
        ProofStatus = proofStatus;
        ExactObjective = exactObjective;
    }

    /// <summary>CP 整数解 / CP integer solution.</summary>
    public ConstraintProgrammingSolution Solution { get; }
    /// <summary>兼容浮点目标 / Compatibility floating objective.</summary>
    public Flt64? Objective { get; }
    /// <summary>兼容浮点最佳界 / Compatibility floating best bound.</summary>
    public Flt64? BestBound { get; }
    /// <summary>求解状态 / Solver status.</summary>
    public SolverStatus Status { get; }
    /// <summary>证明状态 / Proof status.</summary>
    public ProofStatus ProofStatus { get; }
    /// <summary>精确整数目标 / Exact integer objective.</summary>
    public Int64? ExactObjective { get; }
}

/// <summary>CP 已证明不可行输出 / CP proven-infeasible output.</summary>
public sealed record ConstraintProgrammingInfeasibleOutput : ConstraintProgrammingSolverOutput {
    /// <summary>创建 CP 不可行输出 / Create a CP infeasible output.</summary>
    /// <param name="conflict">冲突证据 / Conflict evidence.</param>
    /// <param name="proofStatus">证明状态 / Proof status.</param>
    /// <param name="report">统一报告 / Unified report.</param>
    public ConstraintProgrammingInfeasibleOutput(
        ConstraintProgrammingConflict? conflict = null,
        ProofStatus proofStatus = ProofStatus.Verified,
        SolveReport<Int64>? report = null)
        : base(report) {
        Conflict = conflict;
        ProofStatus = proofStatus;
    }

    /// <summary>冲突证据 / Conflict evidence.</summary>
    public ConstraintProgrammingConflict? Conflict { get; }
    /// <summary>证明状态 / Proof status.</summary>
    public ProofStatus ProofStatus { get; }
}

/// <summary>CP 未知或达到限制输出 / CP unknown or limit output.</summary>
public sealed record ConstraintProgrammingUnknownOutput : ConstraintProgrammingSolverOutput {
    /// <summary>创建 CP 未知输出 / Create a CP unknown output.</summary>
    /// <param name="terminationReason">终止原因 / Termination reason.</param>
    /// <param name="report">统一报告 / Unified report.</param>
    public ConstraintProgrammingUnknownOutput(
        TerminationReason terminationReason,
        SolveReport<Int64>? report = null)
        : base(report) {
        TerminationReason = terminationReason;
    }

    /// <summary>终止原因 / Termination reason.</summary>
    public TerminationReason TerminationReason { get; }
}
