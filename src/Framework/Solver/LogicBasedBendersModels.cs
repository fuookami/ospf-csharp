#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Solver;

/// <summary>
/// 逻辑 Benders 证明模式 / Logic-based Benders proof mode.
/// </summary>
public enum BendersProofMode {
    /// <summary>只接受已验证的终态证书 / Only accept verified terminal certificates.</summary>
    Exact,

    /// <summary>允许使用未完成全局最优证明的可行解 / Allow feasible incumbents without a global optimality proof.</summary>
    Heuristic,
}

/// <summary>主问题割类别 / Master-cut category.</summary>
public enum BendersCutKind {
    /// <summary>可行性割 / Feasibility cut.</summary>
    Feasibility,

    /// <summary>冲突割 / Conflict cut.</summary>
    Conflict,

    /// <summary>No-good 割 / No-good cut.</summary>
    NoGood,

    /// <summary>最优性割 / Optimality cut.</summary>
    Optimality,

    /// <summary>启发式割 / Heuristic cut.</summary>
    Heuristic,
}

/// <summary>割的有效性范围 / Cut validity scope.</summary>
public enum BendersCutValidity {
    /// <summary>对整个主问题可行域有效 / Valid for the complete master feasible region.</summary>
    Global,

    /// <summary>仅对当前赋值有效 / Valid only at the current assignment.</summary>
    Assignment,

    /// <summary>启发式有效 / Heuristic validity.</summary>
    Heuristic,
}

/// <summary>CP 子问题轨迹状态 / CP-subproblem trace status.</summary>
public enum BendersSubproblemStatus {
    /// <summary>可行 / Feasible.</summary>
    Feasible,

    /// <summary>不可行 / Infeasible.</summary>
    Infeasible,

    /// <summary>未知 / Unknown.</summary>
    Unknown,
}

/// <summary>按稳定领域键读取主问题值的值源 / Stable master-value source keyed by domain identifiers.</summary>
public interface IConstraintProgrammingValueSource {
    /// <summary>按稳定键读取值 / Read a value by stable key.</summary>
    /// <param name="key">稳定领域键 / Stable domain key.</param>
    /// <returns>值或缺少数据错误 / Value or a missing-data error.</returns>
    Result<Flt64, ErrorCode, Error<ErrorCode>> Value(string key);

    /// <summary>获取全部值的只读快照 / Get a read-only snapshot of all values.</summary>
    IReadOnlyDictionary<string, Flt64> Values { get; }

    /// <summary>按 OSPF 变量读取值 / Read a value by an OSPF variable.</summary>
    /// <param name="variable">OSPF 变量 / OSPF variable.</param>
    /// <returns>值或缺少数据错误 / Value or a missing-data error.</returns>
    Result<Flt64, ErrorCode, Error<ErrorCode>> Value(IVariableItem variable) =>
        Value(BendersStableKey(variable));

    /// <summary>生成 OSPF 变量的稳定键 / Build the stable key of an OSPF variable.</summary>
    /// <param name="variable">OSPF 变量 / OSPF variable.</param>
    /// <returns>稳定键 / Stable key.</returns>
    static string BendersStableKey(IVariableItem variable) =>
        $"{variable.Identifier}:{variable.Index}";
}

/// <summary>基于映射的稳定值源 / Map-backed stable value source.</summary>
public sealed class MapConstraintProgrammingValueSource : IConstraintProgrammingValueSource {
    /// <summary>创建映射值源 / Create a map-backed value source.</summary>
    /// <param name="values">按稳定键索引的值 / Values keyed by stable identifiers.</param>
    public MapConstraintProgrammingValueSource(IReadOnlyDictionary<string, Flt64> values) {
        ArgumentNullException.ThrowIfNull(values);
        Values = new ReadOnlyDictionary<string, Flt64>(
            new Dictionary<string, Flt64>(values, StringComparer.Ordinal));
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, Flt64> Values { get; }

    /// <inheritdoc/>
    public Result<Flt64, ErrorCode, Error<ErrorCode>> Value(string key) {
        return Values.TryGetValue(key, out Flt64 value)
            ? Results.Ok(value)
            : Results.Failed<Flt64>(new Err<ErrorCode>(
                code: ErrorCode.DataNotFound,
                message: $"缺少主问题变量值：{key} / Missing master variable value: {key}"));
    }

    /// <summary>创建映射值源 / Create a map-backed value source.</summary>
    /// <param name="values">按稳定键索引的值 / Values keyed by stable identifiers.</param>
    /// <returns>映射值源 / Map-backed value source.</returns>
    public static MapConstraintProgrammingValueSource Of(
        IReadOnlyDictionary<string, Flt64> values) => new(values);
}

/// <summary>主问题值及其 CP 投影 / Master value and its CP projection.</summary>
/// <param name="Key">稳定绑定键 / Stable binding key.</param>
/// <param name="Variable">主问题变量 / Master variable.</param>
/// <param name="Value">主问题值 / Master value.</param>
public sealed record BendersMasterAssignment(
    string Key,
    IVariableItem Variable,
    Flt64 Value);

/// <summary>一轮 CP 子问题使用的主问题赋值 / Master assignment used for one CP-subproblem solve.</summary>
public sealed class BendersSubproblemAssignment {
    /// <summary>创建子问题赋值 / Create a subproblem assignment.</summary>
    /// <param name="masterValues">按绑定键索引的主问题值 / Master values keyed by binding key.</param>
    /// <param name="masterVariables">按绑定键索引的主问题变量 / Master variables keyed by binding key.</param>
    /// <param name="fixedValues">本轮固定的 CP 值 / CP values fixed for this solve.</param>
    /// <param name="assumptions">传给 CP 求解器的假设 / Assumptions passed to the CP solver.</param>
    /// <param name="assumptionToMaster">CP 变量到主问题键的反向映射 / Reverse CP-to-master mapping.</param>
    public BendersSubproblemAssignment(
        IReadOnlyDictionary<string, Flt64> masterValues,
        IReadOnlyDictionary<string, IVariableItem> masterVariables,
        IReadOnlyDictionary<VariableId, Int64> fixedValues,
        IReadOnlyList<BooleanLiteral> assumptions,
        IReadOnlyDictionary<string, string>? assumptionToMaster = null) {
        ArgumentNullException.ThrowIfNull(masterValues);
        ArgumentNullException.ThrowIfNull(masterVariables);
        ArgumentNullException.ThrowIfNull(fixedValues);
        ArgumentNullException.ThrowIfNull(assumptions);
        MasterValues = new ReadOnlyDictionary<string, Flt64>(
            new Dictionary<string, Flt64>(masterValues, StringComparer.Ordinal));
        MasterVariables = new ReadOnlyDictionary<string, IVariableItem>(
            new Dictionary<string, IVariableItem>(masterVariables, StringComparer.Ordinal));
        FixedValues = new ReadOnlyDictionary<VariableId, Int64>(
            new Dictionary<VariableId, Int64>(fixedValues));
        Assumptions = Array.AsReadOnly(assumptions.ToArray());
        AssumptionToMaster = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(
                assumptionToMaster ?? new Dictionary<string, string>(),
                StringComparer.Ordinal));
    }

    /// <summary>按绑定键索引的主问题值 / Master values keyed by binding key.</summary>
    public IReadOnlyDictionary<string, Flt64> MasterValues { get; }

    /// <summary>按绑定键索引的主问题变量 / Master variables keyed by binding key.</summary>
    public IReadOnlyDictionary<string, IVariableItem> MasterVariables { get; }

    /// <summary>本轮固定的 CP 值 / CP values fixed for this solve.</summary>
    public IReadOnlyDictionary<VariableId, Int64> FixedValues { get; }

    /// <summary>传给 CP 求解器的假设 / Assumptions passed to the CP solver.</summary>
    public IReadOnlyList<BooleanLiteral> Assumptions { get; }

    /// <summary>CP 变量到主问题键的反向映射 / Reverse CP-to-master mapping.</summary>
    public IReadOnlyDictionary<string, string> AssumptionToMaster { get; }

    /// <summary>主问题赋值兼容别名 / Compatibility alias for the master assignment.</summary>
    public IReadOnlyDictionary<string, Flt64> MasterAssignment => MasterValues;

    /// <summary>CP 固定值兼容别名 / Compatibility alias for CP fixed values.</summary>
    public IReadOnlyDictionary<VariableId, Int64> CpValues => FixedValues;

    /// <summary>假设来源兼容别名 / Compatibility alias for assumption origins.</summary>
    public IReadOnlyDictionary<string, string> AssumptionOrigins => AssumptionToMaster;
}

/// <summary>显式主问题到 CP 的绑定契约 / Explicit master-to-CP binding contract.</summary>
public interface IBendersVariableBinding {
    /// <summary>将一轮主问题解绑定到静态 CP 模型 / Bind one master solution to a static CP model.</summary>
    /// <param name="masterSolution">待绑定的主问题值 / Master values to bind.</param>
    /// <param name="subproblem">接收绑定的 CP 模型 / CP model receiving the binding.</param>
    /// <returns>绑定赋值或结构化错误 / Binding assignment or a structured error.</returns>
    Result<BendersSubproblemAssignment, ErrorCode, Error<ErrorCode>> Bind(
        IConstraintProgrammingValueSource masterSolution,
        ConstraintProgrammingModel subproblem);
}

/// <summary>二值主问题变量到 CP 变量的映射 / Binary master-to-CP variable mapping.</summary>
/// <param name="Key">稳定绑定键 / Stable binding key.</param>
/// <param name="MasterVariable">主问题二值变量 / Binary master variable.</param>
/// <param name="SubproblemVariable">CP 二值变量 / Binary CP variable.</param>
/// <param name="SubproblemVariableId">显式 CP 稳定 ID；为空时使用模型局部 ID / Explicit stable CP ID, or the model-local ID.</param>
public sealed record BinaryBendersVariable(
    string Key,
    IVariableItem MasterVariable,
    BinVar SubproblemVariable,
    VariableId? SubproblemVariableId = null);

/// <summary>有界整数主问题变量到 CP 变量的映射 / Bounded integer master-to-CP variable mapping.</summary>
/// <param name="Key">稳定绑定键 / Stable binding key.</param>
/// <param name="MasterVariable">主问题整数变量 / Integer master variable.</param>
/// <param name="SubproblemVariable">CP 整数变量 / Integer CP variable.</param>
/// <param name="Domain">主问题赋值值域 / Master assignment domain.</param>
/// <param name="SubproblemVariableId">显式 CP 稳定 ID；为空时使用模型局部 ID / Explicit stable CP ID, or the model-local ID.</param>
public sealed record IntegerBendersVariable(
    string Key,
    IVariableItem MasterVariable,
    IVariableItem SubproblemVariable,
    IntegerDomain Domain,
    VariableId? SubproblemVariableId = null);

/// <summary>二值主问题赋值的默认绑定 / Default binding for binary master assignments.</summary>
public sealed class BinaryBendersVariableBinding : IBendersVariableBinding {
    private readonly IReadOnlyList<BinaryBendersVariable> _variables;

    /// <summary>创建二值绑定 / Create a binary binding.</summary>
    /// <param name="variables">二值绑定集合 / Binary bindings.</param>
    public BinaryBendersVariableBinding(IEnumerable<BinaryBendersVariable> variables) {
        ArgumentNullException.ThrowIfNull(variables);
        _variables = Array.AsReadOnly(variables.ToArray());
    }

    /// <inheritdoc/>
    public Result<BendersSubproblemAssignment, ErrorCode, Error<ErrorCode>> Bind(
        IConstraintProgrammingValueSource masterSolution,
        ConstraintProgrammingModel subproblem) {
        ArgumentNullException.ThrowIfNull(masterSolution);
        ArgumentNullException.ThrowIfNull(subproblem);
        Result<ConstraintProgrammingModelSnapshot, ErrorCode, Error<ErrorCode>> snapshotResult =
            subproblem.Snapshot();
        if (snapshotResult is not Ok<
                ConstraintProgrammingModelSnapshot,
                ErrorCode,
                Error<ErrorCode>> snapshotOk) {
            return Propagate<BendersSubproblemAssignment, ConstraintProgrammingModelSnapshot>(
                snapshotResult);
        }

        ConstraintProgrammingModelSnapshot snapshot = snapshotOk.Value;
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var cpIds = new HashSet<VariableId>();
        var masterValues = new Dictionary<string, Flt64>(StringComparer.Ordinal);
        var masterVariables = new Dictionary<string, IVariableItem>(StringComparer.Ordinal);
        var fixedValues = new Dictionary<VariableId, Int64>();
        var assumptions = new List<BooleanLiteral>();
        var assumptionToMaster = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (BinaryBendersVariable binding in _variables) {
            if (string.IsNullOrWhiteSpace(binding.Key) || !keys.Add(binding.Key)) {
                return Failure<BendersSubproblemAssignment>(
                    ErrorCode.IllegalArgument,
                    $"Benders 绑定键为空或重复：{binding.Key} / " +
                    $"Benders binding key is blank or duplicated: {binding.Key}");
            }
            if (!binding.MasterVariable.TypeKind.IsBinaryType ||
                !((IVariableItem)binding.SubproblemVariable).TypeKind.IsBinaryType) {
                return Failure<BendersSubproblemAssignment>(
                    ErrorCode.IllegalArgument,
                    $"Benders 绑定要求主问题和 CP 变量均为二值：{binding.Key} / " +
                    $"Benders binding requires binary master and CP variables: {binding.Key}");
            }
            VariableId cpId = binding.SubproblemVariableId
                ?? new VariableId(
                    $"{binding.SubproblemVariable.Identifier}:{binding.SubproblemVariable.Index}");
            if (string.IsNullOrWhiteSpace(cpId.Value) || !cpIds.Add(cpId)) {
                return Failure<BendersSubproblemAssignment>(
                    ErrorCode.IllegalArgument,
                    $"Benders CP 变量稳定 ID 为空或重复：{cpId.Value} / " +
                    $"Benders CP variable stable ID is blank or duplicated: {cpId.Value}");
            }
            Result<Flt64, ErrorCode, Error<ErrorCode>> valueResult =
                masterSolution.Value(binding.Key);
            if (valueResult is not Ok<Flt64, ErrorCode, Error<ErrorCode>> valueOk) {
                return Propagate<BendersSubproblemAssignment, Flt64>(valueResult);
            }
            Flt64 raw = valueOk.Value;
            if (raw != Flt64.Zero && raw != Flt64.One) {
                return Failure<BendersSubproblemAssignment>(
                    ErrorCode.IllegalArgument,
                    $"Benders 主问题变量必须为 0 或 1：{binding.Key}={raw} / " +
                    $"Benders master variable must be 0 or 1: {binding.Key}={raw}");
            }
            ConstraintProgrammingVariableSnapshot? definition = snapshot.Variable(cpId);
            if (definition is null) {
                return Failure<BendersSubproblemAssignment>(
                    ErrorCode.DataNotFound,
                    $"CP 子问题缺少绑定变量：{cpId.Value} / " +
                    $"CP subproblem is missing bound variable: {cpId.Value}");
            }
            Int64 integer = raw == Flt64.One ? Int64.One : Int64.Zero;
            if (!definition.Domain.Contains(integer)) {
                return Failure<BendersSubproblemAssignment>(
                    ErrorCode.ORSolutionInvalid,
                    $"绑定值超出 CP 子问题值域：{cpId.Value}={integer} / " +
                    $"Binding value is outside the CP subproblem domain: {cpId.Value}={integer}");
            }
            assumptions.Add(new BooleanLiteral.VariableLiteral(
                variable: binding.SubproblemVariable,
                negated: integer == Int64.Zero,
                id: cpId));
            masterValues[binding.Key] = raw;
            masterVariables[binding.Key] = binding.MasterVariable;
            fixedValues[cpId] = integer;
            assumptionToMaster[cpId.Value] = binding.Key;
        }

        return Results.Ok(new BendersSubproblemAssignment(
            masterValues: masterValues,
            masterVariables: masterVariables,
            fixedValues: fixedValues,
            assumptions: assumptions,
            assumptionToMaster: assumptionToMaster));
    }

    private static Result<T, ErrorCode, Error<ErrorCode>> Propagate<T, S>(
        Result<S, ErrorCode, Error<ErrorCode>> result) {
        return result switch {
            Failed<S, ErrorCode, Error<ErrorCode>> failed =>
                Results.Failed<T>(failed.Error),
            Fatal<S, ErrorCode, Error<ErrorCode>> fatal =>
                new Fatal<T, ErrorCode, Error<ErrorCode>>(fatal.Errors),
            _ => Failure<T>(
                ErrorCode.ApplicationError,
                "Benders 绑定结果状态无效 / Invalid Benders binding result state"),
        };
    }

    private static Result<T, ErrorCode, Error<ErrorCode>> Failure<T>(
        ErrorCode code,
        string message) =>
        Results.Failed<T>(new Err<ErrorCode>(code: code, message: message));
}

/// <summary>有界整数主问题赋值的默认绑定 / Default binding for bounded integer master assignments.</summary>
public sealed class IntegerBendersVariableBinding : IBendersVariableBinding {
    private readonly IReadOnlyList<IntegerBendersVariable> _variables;

    /// <summary>创建整数绑定 / Create an integer binding.</summary>
    /// <param name="variables">整数绑定集合 / Integer bindings.</param>
    public IntegerBendersVariableBinding(IEnumerable<IntegerBendersVariable> variables) {
        ArgumentNullException.ThrowIfNull(variables);
        _variables = Array.AsReadOnly(variables.ToArray());
    }

    /// <inheritdoc/>
    public Result<BendersSubproblemAssignment, ErrorCode, Error<ErrorCode>> Bind(
        IConstraintProgrammingValueSource masterSolution,
        ConstraintProgrammingModel subproblem) {
        ArgumentNullException.ThrowIfNull(masterSolution);
        ArgumentNullException.ThrowIfNull(subproblem);
        Result<ConstraintProgrammingModelSnapshot, ErrorCode, Error<ErrorCode>> snapshotResult =
            subproblem.Snapshot();
        if (snapshotResult is not Ok<
                ConstraintProgrammingModelSnapshot,
                ErrorCode,
                Error<ErrorCode>> snapshotOk) {
            return Propagate<BendersSubproblemAssignment, ConstraintProgrammingModelSnapshot>(
                snapshotResult);
        }

        ConstraintProgrammingModelSnapshot snapshot = snapshotOk.Value;
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var cpIds = new HashSet<VariableId>();
        var masterValues = new Dictionary<string, Flt64>(StringComparer.Ordinal);
        var masterVariables = new Dictionary<string, IVariableItem>(StringComparer.Ordinal);
        var fixedValues = new Dictionary<VariableId, Int64>();

        foreach (IntegerBendersVariable binding in _variables) {
            if (string.IsNullOrWhiteSpace(binding.Key) || !keys.Add(binding.Key)) {
                return Failure<BendersSubproblemAssignment>(
                    ErrorCode.IllegalArgument,
                    $"Benders 绑定键为空或重复：{binding.Key} / " +
                    $"Benders binding key is blank or duplicated: {binding.Key}");
            }
            if (!binding.MasterVariable.TypeKind.IsIntegerType ||
                !binding.SubproblemVariable.TypeKind.IsIntegerType) {
                return Failure<BendersSubproblemAssignment>(
                    ErrorCode.IllegalArgument,
                    $"Benders 绑定要求主问题和 CP 变量均为整数：{binding.Key} / " +
                    $"Benders binding requires integer master and CP variables: {binding.Key}");
            }
            VariableId cpId = binding.SubproblemVariableId
                ?? new VariableId(
                    $"{binding.SubproblemVariable.Identifier}:{binding.SubproblemVariable.Index}");
            if (string.IsNullOrWhiteSpace(cpId.Value) || !cpIds.Add(cpId)) {
                return Failure<BendersSubproblemAssignment>(
                    ErrorCode.IllegalArgument,
                    $"Benders CP 变量稳定 ID 为空或重复：{cpId.Value} / " +
                    $"Benders CP variable stable ID is blank or duplicated: {cpId.Value}");
            }
            Result<Flt64, ErrorCode, Error<ErrorCode>> valueResult =
                masterSolution.Value(binding.Key);
            if (valueResult is not Ok<Flt64, ErrorCode, Error<ErrorCode>> valueOk) {
                return Propagate<BendersSubproblemAssignment, Flt64>(valueResult);
            }
            if (!BendersNumeric.TryExactInt64(valueOk.Value, out Int64 integer)) {
                return Failure<BendersSubproblemAssignment>(
                    ErrorCode.IllegalArgument,
                    $"Benders 主问题变量必须为精确整数：{binding.Key}={valueOk.Value} / " +
                    $"Benders master variable must have an exact integral value: {binding.Key}={valueOk.Value}");
            }
            if (!binding.Domain.Contains(integer)) {
                return Failure<BendersSubproblemAssignment>(
                    ErrorCode.ORSolutionInvalid,
                    $"Benders 主问题赋值超出声明值域：{binding.Key}={integer} / " +
                    $"Benders master assignment is outside the declared domain: {binding.Key}={integer}");
            }
            ConstraintProgrammingVariableSnapshot? definition = snapshot.Variable(cpId);
            if (definition is null) {
                return Failure<BendersSubproblemAssignment>(
                    ErrorCode.DataNotFound,
                    $"CP 子问题缺少绑定变量：{cpId.Value} / " +
                    $"CP subproblem is missing bound variable: {cpId.Value}");
            }
            if (!definition.Domain.Contains(integer)) {
                return Failure<BendersSubproblemAssignment>(
                    ErrorCode.ORSolutionInvalid,
                    $"绑定值超出 CP 子问题值域：{cpId.Value}={integer} / " +
                    $"Binding value is outside the CP subproblem domain: {cpId.Value}={integer}");
            }
            masterValues[binding.Key] = valueOk.Value;
            masterVariables[binding.Key] = binding.MasterVariable;
            fixedValues[cpId] = integer;
        }

        return Results.Ok(new BendersSubproblemAssignment(
            masterValues: masterValues,
            masterVariables: masterVariables,
            fixedValues: fixedValues,
            assumptions: Array.Empty<BooleanLiteral>(),
            assumptionToMaster: new Dictionary<string, string>()));
    }

    private static Result<T, ErrorCode, Error<ErrorCode>> Propagate<T, S>(
        Result<S, ErrorCode, Error<ErrorCode>> result) {
        return result switch {
            Failed<S, ErrorCode, Error<ErrorCode>> failed =>
                Results.Failed<T>(failed.Error),
            Fatal<S, ErrorCode, Error<ErrorCode>> fatal =>
                new Fatal<T, ErrorCode, Error<ErrorCode>>(fatal.Errors),
            _ => Failure<T>(
                ErrorCode.ApplicationError,
                "Benders 绑定结果状态无效 / Invalid Benders binding result state"),
        };
    }

    private static Result<T, ErrorCode, Error<ErrorCode>> Failure<T>(
        ErrorCode code,
        string message) =>
        Results.Failed<T>(new Err<ErrorCode>(code: code, message: message));
}

/// <summary>子问题结果契约 / Subproblem-result contract.</summary>
public abstract record LogicBasedBendersSubproblemResult(
    BendersSubproblemAssignment Assignment);

/// <summary>已证明或已接受的 CP 可行结果 / Proven or accepted feasible CP result.</summary>
/// <param name="Assignment">本次求解使用的主问题赋值 / Master assignment used for the solve.</param>
/// <param name="Output">CP 可行输出 / Feasible CP output.</param>
public sealed record FeasibleSubproblemResult(
    BendersSubproblemAssignment Assignment,
    ConstraintProgrammingFeasibleOutput Output)
    : LogicBasedBendersSubproblemResult(Assignment);

/// <summary>已证明的 CP 不可行结果 / Proven CP-infeasibility result.</summary>
/// <param name="Assignment">本次求解使用的主问题赋值 / Master assignment used for the solve.</param>
/// <param name="Conflict">可选冲突证据 / Optional conflict evidence.</param>
/// <param name="ProofStatus">不可行证明状态 / Infeasibility proof status.</param>
public sealed record InfeasibleSubproblemResult(
    BendersSubproblemAssignment Assignment,
    ConstraintProgrammingConflict? Conflict,
    ProofStatus ProofStatus = ProofStatus.Verified)
    : LogicBasedBendersSubproblemResult(Assignment);

/// <summary>未知、受限或取消的 CP 结果 / Unknown, limited, or cancelled CP result.</summary>
/// <param name="Assignment">本次求解使用的主问题赋值 / Master assignment used for the solve.</param>
/// <param name="TerminationReason">CP 求解停止原因 / CP solve termination reason.</param>
public sealed record UnknownSubproblemResult(
    BendersSubproblemAssignment Assignment,
    TerminationReason TerminationReason)
    : LogicBasedBendersSubproblemResult(Assignment);

/// <summary>割预言器上下文 / Cut-oracle context.</summary>
/// <param name="Master">可变主问题模型 / Mutable master model.</param>
/// <param name="Subproblem">静态 CP 子问题 / Static CP subproblem.</param>
/// <param name="Assignment">当前主问题到 CP 的赋值 / Current master-to-CP assignment.</param>
/// <param name="Iteration">当前 Benders 迭代编号 / Current Benders iteration.</param>
/// <param name="ProofMode">当前证明模式 / Active proof mode.</param>
public sealed record BendersCutContext(
    LinearMetaModel<Flt64> Master,
    ConstraintProgrammingModel Subproblem,
    BendersSubproblemAssignment Assignment,
    int Iteration,
    BendersProofMode ProofMode);

/// <summary>主问题割契约 / Master-cut contract.</summary>
public sealed record BendersMasterCut {
    /// <summary>创建主问题割 / Create a master cut.</summary>
    /// <param name="inequality">主线性不等式 / Primary linear inequality.</param>
    /// <param name="kind">割类别 / Cut category.</param>
    /// <param name="validity">有效性范围 / Validity scope.</param>
    /// <param name="proofStatus">支持该割的证据状态 / Evidence status supporting the cut.</param>
    /// <param name="source">稳定来源标识 / Stable source identifier.</param>
    /// <param name="name">可选模型名称 / Optional model name.</param>
    /// <param name="additionalInequalities">同一割中的附加不等式 / Additional inequalities in the same cut.</param>
    /// <param name="auxiliaryVariables">割所需的辅助变量 / Auxiliary variables required by the cut.</param>
    public BendersMasterCut(
        LinearInequality<Flt64> inequality,
        BendersCutKind kind,
        BendersCutValidity validity,
        ProofStatus proofStatus,
        string source,
        string? name = null,
        IEnumerable<LinearInequality<Flt64>>? additionalInequalities = null,
        IEnumerable<BinVar>? auxiliaryVariables = null) {
        ArgumentNullException.ThrowIfNull(inequality);
        Inequality = inequality;
        Kind = kind;
        Validity = validity;
        ProofStatus = proofStatus;
        Source = source;
        Name = name;
        AdditionalInequalities = Array.AsReadOnly(
            (additionalInequalities ?? Array.Empty<LinearInequality<Flt64>>()).ToArray());
        AuxiliaryVariables = Array.AsReadOnly(
            (auxiliaryVariables ?? Array.Empty<BinVar>()).ToArray());
    }

    /// <summary>主线性不等式 / Primary linear inequality.</summary>
    public LinearInequality<Flt64> Inequality { get; }

    /// <summary>割类别 / Cut category.</summary>
    public BendersCutKind Kind { get; }

    /// <summary>有效性范围 / Validity scope.</summary>
    public BendersCutValidity Validity { get; }

    /// <summary>证据状态 / Evidence status.</summary>
    public ProofStatus ProofStatus { get; }

    /// <summary>稳定来源标识 / Stable source identifier.</summary>
    public string Source { get; }

    /// <summary>可选模型名称 / Optional model name.</summary>
    public string? Name { get; }

    /// <summary>附加不等式 / Additional inequalities.</summary>
    public IReadOnlyList<LinearInequality<Flt64>> AdditionalInequalities { get; }

    /// <summary>辅助变量 / Auxiliary variables.</summary>
    public IReadOnlyList<BinVar> AuxiliaryVariables { get; }

    /// <summary>稳定去重键 / Stable deduplication key.</summary>
    public string Key => BendersCutSignature.For(this);
}

/// <summary>领域割预言器 / Domain cut oracle.</summary>
public interface IBendersCutOracle {
    /// <summary>生成可行性或冲突割 / Generate feasibility or conflict cuts.</summary>
    /// <param name="result">已证明不可行的子问题结果 / Proven-infeasible subproblem result.</param>
    /// <param name="context">当前主问题、子问题和迭代上下文 / Current master, subproblem, and iteration context.</param>
    /// <returns>候选割或生成错误 / Candidate cuts or a generation error.</returns>
    Result<List<BendersMasterCut>, ErrorCode, Error<ErrorCode>> FeasibilityCuts(
        InfeasibleSubproblemResult result,
        BendersCutContext context);

    /// <summary>生成可选最优性割 / Generate optional optimality cuts.</summary>
    /// <param name="result">CP 可行子问题结果 / Feasible CP subproblem result.</param>
    /// <param name="context">当前主问题、子问题和迭代上下文 / Current master, subproblem, and iteration context.</param>
    /// <returns>候选割或生成错误 / Candidate cuts or a generation error.</returns>
    Result<List<BendersMasterCut>, ErrorCode, Error<ErrorCode>> OptimalityCuts(
        FeasibleSubproblemResult result,
        BendersCutContext context);
}

/// <summary>默认二值 no-good/conflict 割预言器 / Default binary no-good/conflict cut oracle.</summary>
public sealed class BinaryNoGoodCutOracle : IBendersCutOracle {
    /// <inheritdoc/>
    public Result<List<BendersMasterCut>, ErrorCode, Error<ErrorCode>> FeasibilityCuts(
        InfeasibleSubproblemResult result,
        BendersCutContext context) {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(context);
        var candidateKeys = new HashSet<string>(StringComparer.Ordinal);
        ConstraintProgrammingConflict? conflict = result.Conflict;
        bool useVerifiedCore = conflict?.Validity == EvidenceValidity.Verified;
        if (conflict is not null && useVerifiedCore) {
            foreach (BooleanLiteral assumption in conflict.Assumptions) {
                if (assumption.VariableId is { } id &&
                    result.Assignment.AssumptionToMaster.TryGetValue(id.Value, out string? masterKey)) {
                    candidateKeys.Add(masterKey);
                }
            }
            foreach (VariableId id in conflict.VariableIds) {
                if (result.Assignment.AssumptionToMaster.TryGetValue(id.Value, out string? masterKey)) {
                    candidateKeys.Add(masterKey);
                }
            }
        }
        if (candidateKeys.Count == 0) {
            candidateKeys.UnionWith(result.Assignment.MasterValues.Keys);
        }
        if (candidateKeys.Count == 0) {
            return Failure<List<BendersMasterCut>>(
                ErrorCode.IllegalArgument,
                "无法为无变量赋值生成 no-good cut / Cannot generate a no-good cut without an assignment");
        }
        return BuildNoGoodCut(
            assignment: result.Assignment,
            keys: candidateKeys,
            conflict: conflict is not null && useVerifiedCore);
    }

    /// <inheritdoc/>
    public Result<List<BendersMasterCut>, ErrorCode, Error<ErrorCode>> OptimalityCuts(
        FeasibleSubproblemResult result,
        BendersCutContext context) {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(context);
        // 没有领域证明时，CP 点目标不能升级为全局最优性割。
        // A CP point objective cannot become a globally valid optimality cut without a domain proof.
        return Results.Ok(new List<BendersMasterCut>());
    }

    private static Result<List<BendersMasterCut>, ErrorCode, Error<ErrorCode>> BuildNoGoodCut(
        BendersSubproblemAssignment assignment,
        IEnumerable<string> keys,
        bool conflict) {
        var terms = new List<LinearMonomial<Flt64>>();
        Flt64 constant = Flt64.Zero;
        foreach (string key in keys) {
            if (!assignment.MasterVariables.TryGetValue(key, out IVariableItem? variable)) {
                return Failure<List<BendersMasterCut>>(
                    ErrorCode.DataNotFound,
                    $"no-good cut 缺少主问题变量：{key} / No-good cut is missing master variable: {key}");
            }
            if (!assignment.MasterValues.TryGetValue(key, out Flt64 value)) {
                return Failure<List<BendersMasterCut>>(
                    ErrorCode.DataNotFound,
                    $"no-good cut 缺少主问题赋值：{key} / No-good cut is missing master assignment: {key}");
            }
            if (value != Flt64.Zero && value != Flt64.One) {
                return Failure<List<BendersMasterCut>>(
                    ErrorCode.IllegalArgument,
                    $"no-good cut 只支持二值赋值：{key}={value} / " +
                    $"No-good cut only supports binary assignments: {key}={value}");
            }
            if (value == Flt64.One) {
                terms.Add(new LinearMonomial<Flt64>(-Flt64.One, variable));
                constant += Flt64.One;
            }
            else {
                terms.Add(new LinearMonomial<Flt64>(Flt64.One, variable));
            }
        }
        LinearPolynomial<Flt64> lhs = new(terms, constant);
        LinearPolynomial<Flt64> rhs = new(Array.Empty<LinearMonomial<Flt64>>(), Flt64.One);
        return Results.Ok(new List<BendersMasterCut> {
            new(
                inequality: new LinearInequality<Flt64>(lhs, rhs, Comparison.GE),
                kind: conflict ? BendersCutKind.Conflict : BendersCutKind.NoGood,
                validity: BendersCutValidity.Global,
                proofStatus: ProofStatus.Verified,
                source: conflict ? "binary-conflict-core" : "binary-no-good"),
        });
    }

    private static Result<T, ErrorCode, Error<ErrorCode>> Failure<T>(
        ErrorCode code,
        string message) =>
        Results.Failed<T>(new Err<ErrorCode>(code: code, message: message));
}

/// <summary>一轮 Benders 轨迹 / One Benders iteration trace.</summary>
/// <param name="Iteration">迭代编号 / Iteration number.</param>
/// <param name="MasterObjective">主问题 incumbent 目标 / Master incumbent objective.</param>
/// <param name="MasterBestBound">主问题最佳界 / Master best bound.</param>
/// <param name="MasterStatus">主问题求解状态 / Master solver status.</param>
/// <param name="SubproblemStatus">CP 子问题状态 / CP subproblem status.</param>
/// <param name="SubproblemObjective">当前赋值下的 CP 目标 / CP objective at the current assignment.</param>
/// <param name="CutCount">新接受的割数量 / Newly accepted cut count.</param>
/// <param name="ConflictCoreSize">冲突核心大小 / Conflict-core size.</param>
/// <param name="Elapsed">引擎耗时 / Engine elapsed time.</param>
/// <param name="ProofMode">本次迭代使用的证明模式 / Proof mode used for the iteration.</param>
/// <param name="ConvergenceGap">主问题 incumbent 与最佳界间隙 / Master incumbent/bound gap.</param>
public sealed record BendersIterationTrace(
    int Iteration,
    Flt64? MasterObjective,
    Flt64? MasterBestBound,
    SolverStatus? MasterStatus,
    BendersSubproblemStatus SubproblemStatus,
    Flt64? SubproblemObjective,
    int CutCount,
    int ConflictCoreSize,
    TimeSpan Elapsed,
    BendersProofMode ProofMode,
    Flt64? ConvergenceGap);

/// <summary>计算完整 Benders incumbent 目标的函数 / Evaluator for the complete Benders incumbent objective.</summary>
/// <param name="masterOutput">当前主问题可行输出 / Current master feasible output.</param>
/// <param name="assignment">主问题到 CP 的赋值 / Master-to-CP assignment.</param>
/// <param name="subproblemOutput">CP 可行输出 / Feasible CP output.</param>
/// <returns>完整 incumbent 目标或求值错误 / Complete incumbent objective or an evaluation error.</returns>
public delegate Result<Flt64, ErrorCode, Error<ErrorCode>> BendersCompleteObjectiveEvaluator(
    SolveReport<Flt64> masterOutput,
    BendersSubproblemAssignment assignment,
    ConstraintProgrammingFeasibleOutput subproblemOutput);

/// <summary>逻辑 Benders 引擎选项 / Logic-based Benders engine options.</summary>
public sealed record LogicBasedBendersOptions {
    /// <summary>证明模式 / Proof mode.</summary>
    public BendersProofMode ProofMode { get; init; } = BendersProofMode.Exact;

    /// <summary>最大迭代次数 / Maximum iteration count.</summary>
    public int MaxIterations { get; init; } = 100;

    /// <summary>连续停滞迭代上限 / Consecutive stalled-iteration limit.</summary>
    public int StallIterationLimit { get; init; } = 1;

    /// <summary>目标与收敛绝对容差 / Absolute objective and convergence tolerance.</summary>
    public Flt64 OptimalityTolerance { get; init; } = Flt64.Zero;

    /// <summary>完整 incumbent 目标求值器 / Complete incumbent-objective evaluator.</summary>
    public BendersCompleteObjectiveEvaluator? CompleteObjectiveEvaluator { get; init; }

    /// <summary>用于补充稳定值查找的主问题变量 / Optional master variables for stable value lookup.</summary>
    public IReadOnlyList<IVariableItem> MasterVariables { get; init; } = Array.Empty<IVariableItem>();

    /// <summary>自定义主问题输出值源 / Custom master-output value source.</summary>
    public Func<
        SolveReport<Flt64>,
        Result<IConstraintProgrammingValueSource, ErrorCode, Error<ErrorCode>>>?
        MasterSolutionSource { get; init; }

    /// <summary>CP 求解器选项 / CP solver options.</summary>
    public ConstraintProgrammingSolveOptions ConstraintProgrammingOptions { get; init; } = new();

    /// <summary>调用方取消令牌 / Caller cancellation token.</summary>
    public CancellationToken CancellationToken { get; init; } = default;

    /// <summary>迭代进度回调 / Iteration-progress callback.</summary>
    public Func<BendersIterationTrace, Try>? ProgressReporter { get; init; }

    /// <summary>已校验的可移植恢复状态 / Validated portable resume state.</summary>
    public BendersResumeState? ResumeState { get; init; }

    /// <summary>应用持久化主问题状态的适配器 / Adapter that applies persisted master state.</summary>
    public BendersMasterResumeApplier? ResumeStateApplier { get; init; }
}

/// <summary>逻辑 Benders 最终报告 / Final logic-based Benders report.</summary>
public sealed record LogicBasedBendersReport {
    /// <summary>创建最终报告 / Create a final report.</summary>
    /// <param name="problemStatus">最终问题结论 / Final problem status.</param>
    /// <param name="terminationReason">最终终止原因 / Final termination reason.</param>
    /// <param name="proof">最终证明 / Final proof.</param>
    /// <param name="masterOutput">最后一次主问题输出 / Last master output.</param>
    /// <param name="assignment">最后一次主问题到 CP 的赋值 / Last master-to-CP assignment.</param>
    /// <param name="subproblemResult">最后一次 CP 结果 / Last CP result.</param>
    /// <param name="cuts">全部已接受割 / All accepted cuts.</param>
    /// <param name="iterations">迭代轨迹 / Iteration traces.</param>
    /// <param name="diagnostics">结构化诊断 / Structured diagnostics.</param>
    public LogicBasedBendersReport(
        ProblemStatus problemStatus,
        TerminationReason terminationReason,
        SolveProof proof,
        SolveReport<Flt64>? masterOutput = null,
        BendersSubproblemAssignment? assignment = null,
        LogicBasedBendersSubproblemResult? subproblemResult = null,
        IEnumerable<BendersMasterCut>? cuts = null,
        IEnumerable<BendersIterationTrace>? iterations = null,
        IEnumerable<SolveIssue>? diagnostics = null) {
        ProblemStatus = problemStatus;
        TerminationReason = terminationReason;
        Proof = proof;
        MasterOutput = masterOutput;
        Assignment = assignment;
        SubproblemResult = subproblemResult;
        Cuts = Array.AsReadOnly((cuts ?? Array.Empty<BendersMasterCut>()).ToArray());
        Iterations = Array.AsReadOnly((iterations ?? Array.Empty<BendersIterationTrace>()).ToArray());
        Diagnostics = Array.AsReadOnly((diagnostics ?? Array.Empty<SolveIssue>()).ToArray());
    }

    /// <summary>最终问题结论 / Final problem status.</summary>
    public ProblemStatus ProblemStatus { get; }

    /// <summary>最终终止原因 / Final termination reason.</summary>
    public TerminationReason TerminationReason { get; }

    /// <summary>最终证明 / Final proof.</summary>
    public SolveProof Proof { get; }

    /// <summary>最后一次主问题输出 / Last master output.</summary>
    public SolveReport<Flt64>? MasterOutput { get; }

    /// <summary>最后一次主问题到 CP 的赋值 / Last master-to-CP assignment.</summary>
    public BendersSubproblemAssignment? Assignment { get; }

    /// <summary>最后一次 CP 结果 / Last CP result.</summary>
    public LogicBasedBendersSubproblemResult? SubproblemResult { get; }

    /// <summary>全部已接受割 / All accepted cuts.</summary>
    public IReadOnlyList<BendersMasterCut> Cuts { get; }

    /// <summary>迭代轨迹 / Iteration traces.</summary>
    public IReadOnlyList<BendersIterationTrace> Iterations { get; }

    /// <summary>结构化诊断 / Structured diagnostics.</summary>
    public IReadOnlyList<SolveIssue> Diagnostics { get; }

    /// <summary>问题结论兼容别名 / Compatibility alias for the problem status.</summary>
    public ProblemStatus Status => ProblemStatus;

    /// <summary>转换为 Core 统一报告 / Convert to the Core unified report.</summary>
    /// <returns>统一求解报告 / Unified solve report.</returns>
    public SolveReport<Flt64> ToSolveReport() {
        SolveReport<Flt64>? output = MasterOutput;
        SolutionPresence presence = output?.Solution is null
            ? SolutionPresence.None
            : Proof.Status == ProofStatus.Verified && ProblemStatus == ProblemStatus.Feasible
                ? SolutionPresence.Optimal
                : SolutionPresence.Incumbent;
        return new SolveReport<Flt64> {
            ProblemStatus = ProblemStatus,
            TerminationReason = TerminationReason,
            SolutionPresence = presence,
            Solution = output?.Solution,
            Proof = Proof,
            Statistics = new SolveStatistics(
                SolveTime: output?.Statistics.SolveTime,
                Iterations: (ulong)Iterations.Count,
                BestBound: output?.Statistics.BestBound,
                Gap: output?.Statistics.Gap),
            Diagnostics = new SolveDiagnostics<Flt64>(Errors: Diagnostics),
            Provenance = output?.Provenance,
            Fingerprints = output?.Fingerprints ?? new SolveFingerprints(),
        };
    }
}

/// <summary>引擎使用的主问题求解适配器 / Master-solve adapter used by the engine.</summary>
/// <param name="master">当前可变主问题模型 / Current mutable master model.</param>
/// <returns>主问题统一报告或结构化错误 / Master unified report or a structured error.</returns>
public delegate Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>>
    BendersMasterProblemSolver(LinearMetaModel<Flt64> master);

/// <summary>稳定 cut 去重签名 / Stable cut deduplication signature.</summary>
internal static class BendersCutSignature {
    /// <summary>生成 cut 去重键 / Build a cut deduplication key.</summary>
    /// <param name="cut">待签名的割 / Cut to sign.</param>
    /// <returns>稳定键 / Stable key.</returns>
    public static string For(BendersMasterCut cut) {
        var parts = new List<string> {
            cut.Source,
            cut.Kind.ToString(),
            Inequality(cut.Inequality),
        };
        parts.AddRange(cut.AdditionalInequalities.Select(Inequality));
        parts.AddRange(cut.AuxiliaryVariables.Select(variable =>
            $"aux:{variable.Identifier}:{variable.Index}"));
        return string.Join("|", parts);
    }

    private static string Inequality(LinearInequality<Flt64> inequality) {
        var terms = new Dictionary<string, Flt64>(StringComparer.Ordinal);
        AddTerms(terms, inequality.Lhs.Monomials, coefficientSign: 1.0);
        AddTerms(terms, inequality.Rhs.Monomials, coefficientSign: -1.0);
        Flt64 constant = inequality.Lhs.Constant - inequality.Rhs.Constant;
        double scale = System.Math.Max(
            terms.Values.Select(value => System.Math.Abs(value.ToDouble())).DefaultIfEmpty(0.0).Max(),
            System.Math.Abs(constant.ToDouble()));
        if (!double.IsFinite(scale) || scale <= 0) {
            scale = 1.0;
        }
        string canonicalTerms = string.Join(
            ";",
            terms
                .Where(pair => pair.Value != Flt64.Zero)
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair =>
                    $"{pair.Key}={Format(pair.Value.ToDouble() / scale)}"));
        return $"{inequality.Comparison}|{Format(constant.ToDouble() / scale)}|{canonicalTerms}";
    }

    private static void AddTerms(
        IDictionary<string, Flt64> terms,
        IEnumerable<LinearMonomial<Flt64>> monomials,
        double coefficientSign) {
        foreach (LinearMonomial<Flt64> monomial in monomials) {
            string key = SymbolKey(monomial.Symbol);
            Flt64 coefficient = monomial.Coefficient * new Flt64(coefficientSign);
            terms[key] = terms.TryGetValue(key, out Flt64 existing)
                ? existing + coefficient
                : coefficient;
        }
    }

    private static string SymbolKey(ISymbol symbol) {
        return symbol is IVariableItem variable
            ? $"{variable.Identifier}:{variable.Index}"
            : symbol.ToString() ?? string.Empty;
    }

    private static string Format(double value) =>
        value.ToString("R", CultureInfo.InvariantCulture);
}

/// <summary>将主问题统一报告转换为稳定值源 / Convert a master unified report into a stable value source.</summary>
internal static class BendersMasterSolutionValueSource {
    /// <summary>创建默认值源 / Create the default value source.</summary>
    /// <param name="master">当前主问题 / Current master.</param>
    /// <param name="report">主问题统一报告 / Master unified report.</param>
    /// <param name="variables">可选补充变量 / Optional additional variables.</param>
    /// <returns>值源或结构化错误 / Value source or a structured error.</returns>
    public static Result<IConstraintProgrammingValueSource, ErrorCode, Error<ErrorCode>> Create(
        LinearMetaModel<Flt64> master,
        SolveReport<Flt64> report,
        IReadOnlyList<IVariableItem>? variables = null) {
        IReadOnlyList<IVariableItem> tokens = master.Tokens.TokensInSolver
            .Select(token => token.Variable)
            .ToList();
        IReadOnlyList<Flt64> values = report.Solution?.Values.Values ?? Array.Empty<Flt64>();
        if (tokens.Count != values.Count) {
            return Failure(
                $"主问题解长度不匹配 / Master solution length mismatch: {values.Count} != {tokens.Count}");
        }
        var result = new Dictionary<string, Flt64>(StringComparer.Ordinal);
        for (int index = 0; index < values.Count; index++) {
            if (!double.IsFinite(values[index].ToDouble())) {
                return Failure("主问题解包含非有限值 / Master solution contains a non-finite value");
            }
            result[$"index:{index}"] = values[index];
            result[index.ToString(CultureInfo.InvariantCulture)] = values[index];
            result[$"{tokens[index].Identifier}:{tokens[index].Index}"] = values[index];
        }
        foreach (IVariableItem variable in variables ?? Array.Empty<IVariableItem>()) {
            int tokenIndex = -1;
            for (int index = 0; index < tokens.Count; index++) {
                if (ReferenceEquals(tokens[index], variable)) {
                    tokenIndex = index;
                    break;
                }
            }
            if (tokenIndex < 0) {
                return Failure(
                    $"补充主问题变量不在求解器 token 中：{variable.Identifier}:{variable.Index} / " +
                    $"Additional master variable is not in the solver tokens: " +
                    $"{variable.Identifier}:{variable.Index}");
            }
            result[$"{variable.Identifier}:{variable.Index}"] = values[tokenIndex];
        }
        return Results.Ok<IConstraintProgrammingValueSource>(
            new MapConstraintProgrammingValueSource(result));
    }

    private static Result<IConstraintProgrammingValueSource, ErrorCode, Error<ErrorCode>> Failure(
        string message) =>
        Results.Failed<IConstraintProgrammingValueSource>(
            new Err<ErrorCode>(
                code: ErrorCode.ORSolutionInvalid,
                message: message));
}
