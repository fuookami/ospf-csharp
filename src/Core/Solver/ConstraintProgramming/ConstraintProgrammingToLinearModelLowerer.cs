#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
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

namespace Fuookami.Ospf.Core.Solver.ConstraintProgramming;

/// <summary>降阶 artifact 与源 CP 元素的可回查映射 / Reverse mapping from a lowered artifact to its CP source.</summary>
/// <param name="ArtifactId">artifact 稳定标识 / Stable artifact identifier.</param>
/// <param name="Role">artifact 角色 / Artifact role.</param>
/// <param name="OriginId">源 CP 元素 ID；无法唯一归属时为空 / Source CP element ID, or null.</param>
public sealed record ConstraintProgrammingLoweredArtifact(
    string ArtifactId,
    string Role,
    string? OriginId = null);

/// <summary>
/// 降阶后的线性模型及 CP 元素映射；辅助变量不会暴露为 CP 解成员。
/// Lowered linear model and CP-element mapping; auxiliaries are not exposed as CP solution members.
/// </summary>
public sealed class ConstraintProgrammingLoweredLinearModel : IDisposable {
    internal ConstraintProgrammingLoweredLinearModel(
        LinearMetaModel<Flt64> model,
        IReadOnlyDictionary<VariableId, IVariableItem> variables,
        IReadOnlyList<VariableId> variableIds,
        IReadOnlyDictionary<IntervalId, IntervalVariable> intervals,
        IReadOnlyDictionary<VariableId, IntegerDomain> domains,
        IReadOnlyDictionary<string, ConstraintProgrammingLoweredArtifact> artifacts,
        IReadOnlyDictionary<MathConstraint, ConstraintId> constraintProvenance) {
        Model = model;
        Variables = variables;
        VariableIds = variableIds;
        Intervals = intervals;
        Domains = domains;
        Artifacts = artifacts;
        ConstraintProvenance = constraintProvenance;
    }

    /// <summary>降阶后的线性模型 / Lowered linear model.</summary>
    public LinearMetaModel<Flt64> Model { get; }

    /// <summary>原 CP 标量变量到线性变量的映射 / Source CP variable mapping.</summary>
    public IReadOnlyDictionary<VariableId, IVariableItem> Variables { get; }

    /// <summary>原 CP 变量 ID 顺序 / Original CP variable-ID order.</summary>
    public IReadOnlyList<VariableId> VariableIds { get; }

    /// <summary>原 CP interval 映射 / Source interval mapping.</summary>
    public IReadOnlyDictionary<IntervalId, IntervalVariable> Intervals { get; }

    /// <summary>原 CP 标量值域 / Original CP scalar domains.</summary>
    public IReadOnlyDictionary<VariableId, IntegerDomain> Domains { get; }

    /// <summary>降阶 artifact 与源 CP 元素映射 / Lowered artifacts and their source mappings.</summary>
    public IReadOnlyDictionary<string, ConstraintProgrammingLoweredArtifact> Artifacts { get; }

    /// <summary>生成线性约束的显式来源 / Explicit source for each generated linear constraint.</summary>
    public IReadOnlyDictionary<MathConstraint, ConstraintId> ConstraintProvenance { get; }

    /// <inheritdoc/>
    public void Dispose() => Model.Dispose();
}

/// <summary>
/// 将 CP 模型精确降为 OSPF 线性元模型。
/// Lower a CP model exactly to an OSPF linear meta model.
/// </summary>
/// <remarks>
/// 仅接受有限整数表达式和可由 IEEE double 精确表示的系数、边界与右端项。
/// 近似、弱化或未证明等价的转换返回结构化失败。
/// Only finite integer expressions whose coefficients, bounds, and right-hand sides are exactly
/// representable by IEEE doubles are accepted. Approximate, weakened, or unproved transformations
/// return a structured failure.
/// </remarks>
public sealed class ConstraintProgrammingToLinearModelLowerer {
    /// <summary>创建精确降阶器 / Create an exact lowerer.</summary>
    /// <param name="policy">精确降阶策略 / Exact lowering policy.</param>
    public ConstraintProgrammingToLinearModelLowerer(
        ConstraintProgrammingLoweringPolicy? policy = null) {
        Policy = policy ?? ConstraintProgrammingLoweringPolicy.Strict;
    }

    /// <summary>精确降阶策略 / Exact lowering policy.</summary>
    public ConstraintProgrammingLoweringPolicy Policy { get; }

    /// <summary>从可变 CP 模型生成线性模型 / Lower a mutable CP model.</summary>
    /// <param name="model">可变 CP 模型 / Mutable CP model.</param>
    /// <param name="assumptions">激活文字 / Assumption literals.</param>
    /// <param name="fixedValues">固定变量值 / Fixed variable values.</param>
    /// <returns>降阶模型或结构化错误 / Lowered model or a structured error.</returns>
    public Result<
        ConstraintProgrammingLoweredLinearModel,
        ErrorCode,
        Error<ErrorCode>> Lower(
        ConstraintProgrammingModel model,
        IReadOnlyList<BooleanLiteral>? assumptions = null,
        IReadOnlyDictionary<VariableId, Int64>? fixedValues = null) {
        ArgumentNullException.ThrowIfNull(model);
        Result<
            ConstraintProgrammingModelSnapshot,
            ErrorCode,
            Error<ErrorCode>> snapshot = model.Snapshot();
        return snapshot is Failed<
                ConstraintProgrammingModelSnapshot,
                ErrorCode,
                Error<ErrorCode>> failed
            ? Results.Failed<ConstraintProgrammingLoweredLinearModel>(failed.Error)
            : Lower(
                snapshot.Value,
                assumptions,
                fixedValues);
    }

    /// <summary>从不可变 CP 快照生成线性模型 / Lower an immutable CP snapshot.</summary>
    /// <param name="snapshot">CP 模型快照 / CP model snapshot.</param>
    /// <param name="assumptions">激活文字 / Assumption literals.</param>
    /// <param name="fixedValues">固定变量值 / Fixed variable values.</param>
    /// <returns>降阶模型或结构化错误 / Lowered model or a structured error.</returns>
    public Result<
        ConstraintProgrammingLoweredLinearModel,
        ErrorCode,
        Error<ErrorCode>> Lower(
        ConstraintProgrammingModelSnapshot snapshot,
        IReadOnlyList<BooleanLiteral>? assumptions = null,
        IReadOnlyDictionary<VariableId, Int64>? fixedValues = null) {
        ArgumentNullException.ThrowIfNull(snapshot);
        Try policy = Policy.Validate();
        if (policy is Failed<Success, ErrorCode, Error<ErrorCode>> policyFailure) {
            return Results.Failed<ConstraintProgrammingLoweredLinearModel>(
                policyFailure.Error);
        }

        var identityRegistry = new ModelElementIdentityRegistry(
            @namespace: snapshot.IdentityNamespace,
            schemaVersion: snapshot.IdentitySchemaVersion);
        var linear = new LinearMetaModel<Flt64>(
            name: $"{snapshot.Name}-mip",
            objectCategory: snapshot.ObjectCategory,
            configuration: new MetaModelConfiguration(
                Concurrent: false,
                DumpBlocking: true),
            identityRegistry: identityRegistry);
        var compiler = new Compiler(
            linear: linear,
            snapshot: snapshot,
            policy: Policy);
        Try compiled = compiler.Compile(
            assumptions ?? Array.Empty<BooleanLiteral>(),
            fixedValues ?? new Dictionary<VariableId, Int64>());
        if (compiled is Failed<Success, ErrorCode, Error<ErrorCode>> failed) {
            linear.Dispose();
            return Results.Failed<ConstraintProgrammingLoweredLinearModel>(
                failed.Error);
        }

        return Results.Ok(
            new ConstraintProgrammingLoweredLinearModel(
                model: linear,
                variables: new ReadOnlyDictionary<VariableId, IVariableItem>(
                    compiler.Variables),
                variableIds: snapshot.Variables
                    .Select(variable => variable.Id)
                    .ToList()
                    .AsReadOnly(),
                intervals: new ReadOnlyDictionary<IntervalId, IntervalVariable>(
                    compiler.SourceIntervals),
                domains: new ReadOnlyDictionary<VariableId, IntegerDomain>(
                    snapshot.Variables.ToDictionary(
                        variable => variable.Id,
                        variable => variable.Domain)),
                artifacts: new ReadOnlyDictionary<
                    string,
                    ConstraintProgrammingLoweredArtifact>(compiler.Artifacts),
                constraintProvenance: new ReadOnlyDictionary<
                    MathConstraint,
                    ConstraintId>(compiler.ConstraintProvenance)));
    }
}

internal sealed class Compiler {
    private readonly LinearMetaModel<Flt64> _linear;
    private readonly ConstraintProgrammingModelSnapshot _snapshot;
    private readonly ConstraintProgrammingLoweringPolicy _policy;
    private readonly Dictionary<IVariableItem, (BigInteger Lower, BigInteger Upper)> _domains =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<IntervalId, LoweredInterval> _intervals = new();
    private int _auxiliaryVariables;
    private ConstraintId? _activeSourceConstraintId;

    internal Compiler(
        LinearMetaModel<Flt64> linear,
        ConstraintProgrammingModelSnapshot snapshot,
        ConstraintProgrammingLoweringPolicy policy) {
        _linear = linear;
        _snapshot = snapshot;
        _policy = policy;
    }

    internal Dictionary<VariableId, IVariableItem> Variables { get; } = new();

    internal Dictionary<IntervalId, IntervalVariable> SourceIntervals { get; } = new();

    internal Dictionary<string, ConstraintProgrammingLoweredArtifact> Artifacts { get; } =
        new(StringComparer.Ordinal);

    internal Dictionary<MathConstraint, ConstraintId> ConstraintProvenance { get; } =
        new(ReferenceEqualityComparer.Instance);

    internal Try Compile(
        IReadOnlyList<BooleanLiteral> assumptions,
        IReadOnlyDictionary<VariableId, Int64> fixedValues) {
        Try variables = CompileVariables();
        if (variables is Failed<Success, ErrorCode, Error<ErrorCode>> variableFailure) {
            return variableFailure;
        }

        Try intervals = CompileIntervals();
        if (intervals is Failed<Success, ErrorCode, Error<ErrorCode>> intervalFailure) {
            return intervalFailure;
        }

        foreach (ConstraintProgrammingConstraintSnapshot entry in _snapshot.Constraints) {
            _activeSourceConstraintId = entry.Id;
            Try constraint = CompileConstraint(entry.Constraint, entry.Id.Value);
            _activeSourceConstraintId = null;
            if (constraint is Failed<Success, ErrorCode, Error<ErrorCode>> constraintFailure) {
                return constraintFailure;
            }
        }

        for (int index = 0; index < assumptions.Count; ++index) {
            Result<LinearForm, ErrorCode, Error<ErrorCode>> form =
                LiteralForm(assumptions[index]);
            if (form is Failed<LinearForm, ErrorCode, Error<ErrorCode>> formFailure) {
                return Results.Failed<Success>(formFailure.Error);
            }
            Try added = AddConstraint(
                form: form.Value,
                comparison: Comparison.EQ,
                rhs: BigInteger.One,
                name: $"cp-assumption-{index}");
            if (added is Failed<Success, ErrorCode, Error<ErrorCode>> addedFailure) {
                return addedFailure;
            }
        }

        foreach ((VariableId id, Int64 value) in fixedValues) {
            ConstraintProgrammingVariableSnapshot? definition = _snapshot.Variable(id);
            if (definition is null) {
                return Failure(
                    $"固定值引用了未注册 CP 变量：{id.Value} / " +
                    $"Fixed value references an unregistered CP variable: {id.Value}",
                    ErrorCode.DataNotFound);
            }
            if (!definition.Domain.Contains(value)) {
                return Failure(
                    $"固定值超出 CP 变量值域：{id.Value}={value} / " +
                    $"Fixed value is outside the CP variable domain: {id.Value}={value}",
                    ErrorCode.ORSolutionInvalid);
            }
            if (!Variables.TryGetValue(id, out IVariableItem? variable)) {
                return Failure(
                    $"固定值缺少降阶变量：{id.Value} / " +
                    $"Lowered model misses fixed CP variable: {id.Value}",
                    ErrorCode.DataNotFound);
            }
            Try added = AddConstraint(
                form: Form(variable),
                comparison: Comparison.EQ,
                rhs: new BigInteger(value.ToLong()),
                name: $"cp-fixed-{Sanitize(id.Value)}");
            if (added is Failed<Success, ErrorCode, Error<ErrorCode>> addedFailure) {
                return addedFailure;
            }
        }

        Try objectives = CompileObjectives();
        if (objectives is Failed<Success, ErrorCode, Error<ErrorCode>> objectiveFailure) {
            return objectiveFailure;
        }

        Try identity = _linear.IdentityRegistry!.Validate();
        return identity is Failed<Success, ErrorCode, Error<ErrorCode>> identityFailure
            ? identityFailure
            : Results.Ok(Results.SuccessInstance);
    }

    private Try CompileVariables() {
        foreach (ConstraintProgrammingVariableSnapshot definition in _snapshot.Variables) {
            IVariableItem variable = IsBooleanDomain(definition.Domain)
                ? new BinVar($"cp-{Sanitize(definition.Id.Value)}")
                : new IntVar($"cp-{Sanitize(definition.Id.Value)}");
            BigInteger lower = new(definition.Domain.LowerBound.ToLong());
            BigInteger upper = new(definition.Domain.UpperBound.ToLong());
            Result<Flt64, ErrorCode, Error<ErrorCode>> exactLower =
                SafeFlt64(lower, "CP variable lower bound");
            if (exactLower is Failed<Flt64, ErrorCode, Error<ErrorCode>> lowerFailure) {
                return Results.Failed<Success>(lowerFailure.Error);
            }
            Result<Flt64, ErrorCode, Error<ErrorCode>> exactUpper =
                SafeFlt64(upper, "CP variable upper bound");
            if (exactUpper is Failed<Flt64, ErrorCode, Error<ErrorCode>> upperFailure) {
                return Results.Failed<Success>(upperFailure.Error);
            }
            if (variable is IntVar intVariable &&
                !intVariable.Range.IntersectWith(
                    definition.Domain.LowerBound,
                    definition.Domain.UpperBound)) {
                return Failure(
                    $"CP 变量值域为空：{definition.Id.Value} / " +
                    $"CP variable domain is empty: {definition.Id.Value}");
            }

            Try added = _linear.Add(variable);
            if (added is Failed<Success, ErrorCode, Error<ErrorCode>> addFailure) {
                return addFailure;
            }

            if (definition.Scope == ModelElementScope.Stable &&
                definition.IdentityProvenance.Count > 0) {
                Try registered = _linear.IdentityRegistry!.RegisterVariable(
                    element: variable,
                    id: definition.Id,
                    origin: definition.IdentityProvenance[0],
                    provenance: definition.IdentityProvenance);
                if (registered is Failed<
                        Success,
                        ErrorCode,
                        Error<ErrorCode>> registrationFailure) {
                    return registrationFailure;
                }
            }

            Variables[definition.Id] = variable;
            RegisterArtifact(
                artifactId: $"variable:{definition.Id.Value}",
                role: "source-variable",
                originId: definition.Id.Value);
            _domains[variable] = (lower, upper);

            if (definition.Domain is IntegerDomain.Values sparse &&
                !IsBooleanDomain(definition.Domain)) {
                if (sparse.Items.Count > _policy.SparseDomainLimit) {
                    return Unsupported(
                        "稀疏值域超出降阶规模上限 / " +
                        "Sparse domain exceeds the lowering limit");
                }
                Try compiled = CompileSparseDomain(
                    variable,
                    sparse.Items,
                    definition.Id.Value);
                if (compiled is Failed<
                        Success,
                        ErrorCode,
                        Error<ErrorCode>> sparseFailure) {
                    return sparseFailure;
                }
            }
        }
        return Results.Ok(Results.SuccessInstance);
    }

    private Try CompileSparseDomain(
        IVariableItem variable,
        IReadOnlyList<Int64> values,
        string name) {
        var selectors = new List<IVariableItem>(values.Count);
        for (int index = 0; index < values.Count; ++index) {
            Result<IVariableItem, ErrorCode, Error<ErrorCode>> selector =
                AuxiliaryBinary($"domain-{Sanitize(name)}-{index}");
            if (selector is Failed<
                    IVariableItem,
                    ErrorCode,
                    Error<ErrorCode>> selectorFailure) {
                return Results.Failed<Success>(selectorFailure.Error);
            }
            selectors.Add(selector.Value);
        }

        Try one = AddConstraint(
            form: Form(selectors, BigInteger.One),
            comparison: Comparison.EQ,
            rhs: BigInteger.One,
            name: $"cp-domain-{Sanitize(name)}-one");
        if (one is Failed<Success, ErrorCode, Error<ErrorCode>> oneFailure) {
            return oneFailure;
        }

        Dictionary<IVariableItem, BigInteger> terms = NewTerms();
        terms[variable] = BigInteger.One;
        for (int index = 0; index < values.Count; ++index) {
            terms[selectors[index]] = -new BigInteger(values[index].ToLong());
        }
        return AddConstraint(
            form: Form(terms, BigInteger.Zero),
            comparison: Comparison.EQ,
            rhs: BigInteger.Zero,
            name: $"cp-domain-{Sanitize(name)}-link");
    }

    private Try CompileIntervals() {
        foreach (IntervalVariable interval in _snapshot.Intervals) {
            Result<LinearForm, ErrorCode, Error<ErrorCode>> start =
                ExpressionForm(interval.Start);
            if (start is Failed<LinearForm, ErrorCode, Error<ErrorCode>> startFailure) {
                return Results.Failed<Success>(startFailure.Error);
            }
            Result<LinearForm, ErrorCode, Error<ErrorCode>> end =
                ExpressionForm(interval.End);
            if (end is Failed<LinearForm, ErrorCode, Error<ErrorCode>> endFailure) {
                return Results.Failed<Success>(endFailure.Error);
            }
            Result<LinearForm, ErrorCode, Error<ErrorCode>> size =
                ExpressionForm(interval.Size);
            if (size is Failed<LinearForm, ErrorCode, Error<ErrorCode>> sizeFailure) {
                return Results.Failed<Success>(sizeFailure.Error);
            }
            (BigInteger Lower, BigInteger Upper)? sizeBounds = FormBounds(size.Value);
            if (sizeBounds is null) {
                return Unsupported(
                    "变量 duration 需要有限值域 / " +
                    "Variable duration requires finite bounds");
            }

            LinearForm? presence = null;
            if (interval.Presence is not null) {
                Result<LinearForm, ErrorCode, Error<ErrorCode>> presenceResult =
                    LiteralForm(interval.Presence);
                if (presenceResult is Failed<
                        LinearForm,
                        ErrorCode,
                        Error<ErrorCode>> presenceFailure) {
                    return Results.Failed<Success>(presenceFailure.Error);
                }
                presence = presenceResult.Value;
            }

            string intervalName = Sanitize(interval.Id.Value);
            LinearForm link = Plus(
                Combine(end.Value, start.Value, BigInteger.One, -BigInteger.One),
                size.Value.Negate());
            (BigInteger Lower, BigInteger Upper)? linkBounds = FormBounds(link);
            if (linkBounds is null) {
                return Unsupported(
                    "interval link 需要有限值域 / Interval link requires finite bounds");
            }

            if (presence is null) {
                Try nonNegative = AddConstraint(
                    form: size.Value,
                    comparison: Comparison.GE,
                    rhs: BigInteger.Zero,
                    name: $"cp-interval-{intervalName}-non-negative");
                if (nonNegative is Failed<
                        Success,
                        ErrorCode,
                        Error<ErrorCode>> nonNegativeFailure) {
                    return nonNegativeFailure;
                }
                Try linkResult = AddConstraint(
                    form: link,
                    comparison: Comparison.EQ,
                    rhs: BigInteger.Zero,
                    name: $"cp-interval-{intervalName}-link");
                if (linkResult is Failed<
                        Success,
                        ErrorCode,
                        Error<ErrorCode>> linkFailure) {
                    return linkFailure;
                }
            }
            else {
                Try nonNegative = AddConstraint(
                    form: Combine(
                        size.Value,
                        presence,
                        BigInteger.One,
                        sizeBounds.Value.Lower),
                    comparison: Comparison.GE,
                    rhs: sizeBounds.Value.Lower,
                    name: $"cp-interval-{intervalName}-non-negative-gated");
                if (nonNegative is Failed<
                        Success,
                        ErrorCode,
                        Error<ErrorCode>> nonNegativeFailure) {
                    return nonNegativeFailure;
                }
                Try upper = AddConstraint(
                    form: Combine(
                        link,
                        presence,
                        BigInteger.One,
                        linkBounds.Value.Upper),
                    comparison: Comparison.LE,
                    rhs: linkBounds.Value.Upper,
                    name: $"cp-interval-{intervalName}-link-upper");
                if (upper is Failed<Success, ErrorCode, Error<ErrorCode>> upperFailure) {
                    return upperFailure;
                }
                Try lower = AddConstraint(
                    form: Combine(
                        link,
                        presence,
                        BigInteger.One,
                        linkBounds.Value.Lower),
                    comparison: Comparison.GE,
                    rhs: linkBounds.Value.Lower,
                    name: $"cp-interval-{intervalName}-link-lower");
                if (lower is Failed<Success, ErrorCode, Error<ErrorCode>> lowerFailure) {
                    return lowerFailure;
                }
            }

            Result<IVariableItem, ErrorCode, Error<ErrorCode>> startVariable =
                AsVariable(interval.Start);
            if (startVariable is Failed<
                    IVariableItem,
                    ErrorCode,
                    Error<ErrorCode>> startVariableFailure) {
                return Results.Failed<Success>(startVariableFailure.Error);
            }
            Result<IVariableItem, ErrorCode, Error<ErrorCode>> endVariable =
                AsVariable(interval.End);
            if (endVariable is Failed<
                    IVariableItem,
                    ErrorCode,
                    Error<ErrorCode>> endVariableFailure) {
                return Results.Failed<Success>(endVariableFailure.Error);
            }
            _intervals[interval.Id] = new LoweredInterval(
                Start: startVariable.Value,
                End: endVariable.Value,
                Size: size.Value,
                Presence: presence);
            SourceIntervals[interval.Id] = interval;
            RegisterArtifact(
                artifactId: $"interval:{interval.Id.Value}",
                role: "source-interval",
                originId: interval.Id.Value);
        }
        return Results.Ok(Results.SuccessInstance);
    }

    private Try CompileConstraint(
        ConstraintProgrammingConstraint constraint,
        string name) {
        switch (constraint) {
            case ConstraintProgrammingConstraint.IntegerComparison comparison: {
                Result<LinearForm, ErrorCode, Error<ErrorCode>> form =
                    ExpressionForm(comparison.Expression);
                if (form is Failed<LinearForm, ErrorCode, Error<ErrorCode>> failure) {
                    return Results.Failed<Success>(failure.Error);
                }
                return AddConstraint(
                    form: form.Value,
                    comparison: ToComparison(comparison.Comparison),
                    rhs: new BigInteger(comparison.Rhs.ToLong()),
                    name: name);
            }
            case ConstraintProgrammingConstraint.Literal literal: {
                Result<LinearForm, ErrorCode, Error<ErrorCode>> form =
                    LiteralForm(literal.LiteralValue);
                return form is Failed<LinearForm, ErrorCode, Error<ErrorCode>> failure
                    ? Results.Failed<Success>(failure.Error)
                    : AddConstraint(
                        form: form.Value,
                        comparison: Comparison.EQ,
                        rhs: BigInteger.One,
                        name: name);
            }
            case ConstraintProgrammingConstraint.BoolAnd conjunction: {
                for (int index = 0; index < conjunction.Literals.Count; ++index) {
                    Result<LinearForm, ErrorCode, Error<ErrorCode>> form =
                        LiteralForm(conjunction.Literals[index]);
                    if (form is Failed<
                            LinearForm,
                            ErrorCode,
                            Error<ErrorCode>> failure) {
                        return Results.Failed<Success>(failure.Error);
                    }
                    Try added = AddConstraint(
                        form: form.Value,
                        comparison: Comparison.EQ,
                        rhs: BigInteger.One,
                        name: $"{name}-and-{index}");
                    if (added is Failed<
                            Success,
                            ErrorCode,
                            Error<ErrorCode>> addedFailure) {
                        return addedFailure;
                    }
                }
                return Results.Ok(Results.SuccessInstance);
            }
            case ConstraintProgrammingConstraint.BoolOr disjunction: {
                Result<LinearForm, ErrorCode, Error<ErrorCode>> form =
                    LiteralSum(disjunction.Literals);
                return form is Failed<LinearForm, ErrorCode, Error<ErrorCode>> failure
                    ? Results.Failed<Success>(failure.Error)
                    : AddConstraint(
                        form: form.Value,
                        comparison: Comparison.GE,
                        rhs: BigInteger.One,
                        name: name);
            }
            case ConstraintProgrammingConstraint.BoolXor exclusive: {
                Result<LinearForm, ErrorCode, Error<ErrorCode>> form =
                    LiteralSum(exclusive.Literals);
                return form is Failed<LinearForm, ErrorCode, Error<ErrorCode>> failure
                    ? Results.Failed<Success>(failure.Error)
                    : AddConstraint(
                        form: form.Value,
                        comparison: Comparison.EQ,
                        rhs: BigInteger.One,
                        name: name);
            }
            case ConstraintProgrammingConstraint.Implication implication:
                return CompileImplication(implication, name);
            case ConstraintProgrammingConstraint.Reified reified:
                return CompileReified(reified, name);
            case ConstraintProgrammingConstraint.AllDifferent allDifferent:
                return CompileAllDifferent(allDifferent, name);
            case ConstraintProgrammingConstraint.Element element:
                return CompileElement(element, name);
            case ConstraintProgrammingConstraint.AllowedAssignments allowed:
                return CompileAllowedAssignments(allowed, name);
            case ConstraintProgrammingConstraint.ForbiddenAssignments forbidden:
                return CompileForbiddenAssignments(forbidden, name);
            case NoOverlap noOverlap:
                return CompileNoOverlap(noOverlap, name);
            case Cumulative cumulative:
                return _policy.AllowCumulative
                    ? CompileCumulative(cumulative, name)
                    : Unsupported(
                        "MIP-backed CP 首版不支持 cumulative / " +
                        "Cumulative is unsupported by the first MIP-backed CP compiler");
            case ConstraintProgrammingConstraint.Circuit:
                return Unsupported(
                    "MIP-backed CP 首版不支持 Circuit / " +
                    "Circuit is unsupported by the first MIP-backed CP compiler");
            case ConstraintProgrammingConstraint.Automaton:
                return Unsupported(
                    "MIP-backed CP 首版不支持 Automaton / " +
                    "Automaton is unsupported by the first MIP-backed CP compiler");
            case ConstraintProgrammingConstraint.Reservoir:
                return Unsupported(
                    "MIP-backed CP 首版不支持 Reservoir / " +
                    "Reservoir is unsupported by the first MIP-backed CP compiler");
            default:
                return Unsupported(
                    "未知 CP 约束类型 / Unknown CP constraint type");
        }
    }

    private Try CompileImplication(
        ConstraintProgrammingConstraint.Implication implication,
        string name) {
        Result<LinearForm, ErrorCode, Error<ErrorCode>> enforcement =
            LiteralForm(implication.Enforcement);
        if (enforcement is Failed<
                LinearForm,
                ErrorCode,
                Error<ErrorCode>> enforcementFailure) {
            return Results.Failed<Success>(enforcementFailure.Error);
        }

        switch (implication.Constraint) {
            case ConstraintProgrammingConstraint.IntegerComparison comparison:
                return CompileGatedComparison(
                    enforcement: enforcement.Value,
                    expression: comparison.Expression,
                    comparison: comparison.Comparison,
                    rhs: new BigInteger(comparison.Rhs.ToLong()),
                    name: name);
            case ConstraintProgrammingConstraint.Literal literal: {
                Result<LinearForm, ErrorCode, Error<ErrorCode>> consequence =
                    LiteralForm(literal.LiteralValue);
                return consequence is Failed<
                    LinearForm,
                    ErrorCode,
                    Error<ErrorCode>> consequenceFailure
                    ? Results.Failed<Success>(consequenceFailure.Error)
                    : AddConstraint(
                        form: Combine(
                            consequence.Value,
                            enforcement.Value,
                            BigInteger.One,
                            -BigInteger.One),
                        comparison: Comparison.GE,
                        rhs: BigInteger.Zero,
                        name: name);
            }
            default:
                return Unsupported(
                    "复杂逻辑 implication 不支持精确降阶 / " +
                    "Complex logical implications are unsupported by exact lowering");
        }
    }

    private Try CompileReified(
        ConstraintProgrammingConstraint.Reified reified,
        string name) {
        Result<LinearForm, ErrorCode, Error<ErrorCode>> literal =
            LiteralForm(reified.LiteralValue);
        if (literal is Failed<LinearForm, ErrorCode, Error<ErrorCode>> literalFailure) {
            return Results.Failed<Success>(literalFailure.Error);
        }

        switch (reified.Constraint) {
            case ConstraintProgrammingConstraint.IntegerComparison comparison: {
                Result<LinearForm, ErrorCode, Error<ErrorCode>> expression =
                    ExpressionForm(comparison.Expression);
                if (expression is Failed<
                        LinearForm,
                        ErrorCode,
                        Error<ErrorCode>> expressionFailure) {
                    return Results.Failed<Success>(expressionFailure.Error);
                }
                BigInteger rhs = new(comparison.Rhs.ToLong());
                return reified.Direction switch {
                    ReificationDirection.Implies => CompileGatedComparison(
                        enforcement: literal.Value,
                        form: expression.Value,
                        comparison: comparison.Comparison,
                        rhs: rhs,
                        name: name),
                    ReificationDirection.ImpliedBy => CompileNegatedReification(
                        literal: reified.LiteralValue,
                        form: expression.Value,
                        comparison: comparison.Comparison,
                        rhs: rhs,
                        name: name),
                    ReificationDirection.Equivalent => CompileEquivalentReification(
                        literal: reified.LiteralValue,
                        enforcement: literal.Value,
                        form: expression.Value,
                        comparison: comparison.Comparison,
                        rhs: rhs,
                        name: name),
                    _ => Unsupported(
                        "未知 reification 方向 / Unknown reification direction"),
                };
            }
            case ConstraintProgrammingConstraint.Literal child:
                return CompileReifiedLiteral(
                    literal: literal.Value,
                    child: child.LiteralValue,
                    direction: reified.Direction,
                    name: name);
            default:
                return Unsupported(
                    "仅整数比较和文字支持精确 reification / " +
                    "Exact reification supports integer comparisons and literals only");
        }
    }

    private Try CompileNegatedReification(
        BooleanLiteral literal,
        LinearForm form,
        ConstraintProgrammingComparison comparison,
        BigInteger rhs,
        string name) {
        Result<LinearForm, ErrorCode, Error<ErrorCode>> negated =
            LiteralForm(literal.Negate());
        return negated is Failed<LinearForm, ErrorCode, Error<ErrorCode>> failure
            ? Results.Failed<Success>(failure.Error)
            : CompileGatedNegatedComparison(
                enforcement: negated.Value,
                form: form,
                comparison: comparison,
                rhs: rhs,
                name: name);
    }

    private Try CompileEquivalentReification(
        BooleanLiteral literal,
        LinearForm enforcement,
        LinearForm form,
        ConstraintProgrammingComparison comparison,
        BigInteger rhs,
        string name) {
        Try forward = CompileGatedComparison(
            enforcement: enforcement,
            form: form,
            comparison: comparison,
            rhs: rhs,
            name: $"{name}-forward");
        return forward is Failed<Success, ErrorCode, Error<ErrorCode>> forwardFailure
            ? forwardFailure
            : CompileNegatedReification(
                literal: literal,
                form: form,
                comparison: comparison,
                rhs: rhs,
                name: $"{name}-reverse");
    }

    private Try CompileReifiedLiteral(
        LinearForm literal,
        BooleanLiteral child,
        ReificationDirection direction,
        string name) {
        Result<LinearForm, ErrorCode, Error<ErrorCode>> consequence =
            LiteralForm(child);
        if (consequence is Failed<
                LinearForm,
                ErrorCode,
                Error<ErrorCode>> consequenceFailure) {
            return Results.Failed<Success>(consequenceFailure.Error);
        }

        return direction switch {
            ReificationDirection.Implies => AddConstraint(
                form: Combine(
                    consequence.Value,
                    literal,
                    BigInteger.One,
                    -BigInteger.One),
                comparison: Comparison.GE,
                rhs: BigInteger.Zero,
                name: name),
            ReificationDirection.ImpliedBy => AddConstraint(
                form: Combine(
                    literal,
                    consequence.Value,
                    BigInteger.One,
                    -BigInteger.One),
                comparison: Comparison.GE,
                rhs: BigInteger.Zero,
                name: name),
            ReificationDirection.Equivalent => CompileEquivalentLiteralConstraints(
                literal: literal,
                consequence: consequence.Value,
                name: name),
            _ => Unsupported(
                "未知 reification 方向 / Unknown reification direction"),
        };
    }

    private Try CompileEquivalentLiteralConstraints(
        LinearForm literal,
        LinearForm consequence,
        string name) {
        Try forward = AddConstraint(
            form: Combine(
                consequence,
                literal,
                BigInteger.One,
                -BigInteger.One),
            comparison: Comparison.EQ,
            rhs: BigInteger.Zero,
            name: $"{name}-forward");
        return forward is Failed<Success, ErrorCode, Error<ErrorCode>> forwardFailure
            ? forwardFailure
            : AddConstraint(
                form: Combine(
                    literal,
                    consequence,
                    BigInteger.One,
                    -BigInteger.One),
                comparison: Comparison.EQ,
                rhs: BigInteger.Zero,
                name: $"{name}-reverse");
    }

    private Try CompileGatedComparison(
        LinearForm enforcement,
        ConstraintProgrammingExpression expression,
        ConstraintProgrammingComparison comparison,
        BigInteger rhs,
        string name) {
        Result<LinearForm, ErrorCode, Error<ErrorCode>> form =
            ExpressionForm(expression);
        return form is Failed<LinearForm, ErrorCode, Error<ErrorCode>> failure
            ? Results.Failed<Success>(failure.Error)
            : CompileGatedComparison(
                enforcement: enforcement,
                form: form.Value,
                comparison: comparison,
                rhs: rhs,
                name: name);
    }

    private Try CompileGatedComparison(
        LinearForm enforcement,
        LinearForm form,
        ConstraintProgrammingComparison comparison,
        BigInteger rhs,
        string name) {
        (BigInteger Lower, BigInteger Upper)? bounds = FormBounds(form);
        if (bounds is null) {
            return Unsupported(
                "indicator/reification 需要有限表达式界 / " +
                "Indicators and reification require finite expression bounds");
        }

        switch (comparison) {
            case ConstraintProgrammingComparison.LessOrEqual: {
                var m = BigInteger.Max(
                    BigInteger.One,
                    bounds.Value.Upper - rhs);
                return AddConstraint(
                    form: Combine(
                        form,
                        enforcement,
                        BigInteger.One,
                        m),
                    comparison: Comparison.LE,
                    rhs: rhs + m,
                    name: name);
            }
            case ConstraintProgrammingComparison.GreaterOrEqual: {
                var m = BigInteger.Max(
                    BigInteger.One,
                    rhs - bounds.Value.Lower);
                return AddConstraint(
                    form: Combine(
                        form,
                        enforcement,
                        BigInteger.One,
                        -m),
                    comparison: Comparison.GE,
                    rhs: rhs - m,
                    name: name);
            }
            case ConstraintProgrammingComparison.Equal: {
                Try upper = CompileGatedComparison(
                    enforcement: enforcement,
                    form: form,
                    comparison: ConstraintProgrammingComparison.LessOrEqual,
                    rhs: rhs,
                    name: $"{name}-le");
                return upper is Failed<
                    Success,
                    ErrorCode,
                    Error<ErrorCode>> upperFailure
                    ? upperFailure
                    : CompileGatedComparison(
                        enforcement: enforcement,
                        form: form,
                        comparison: ConstraintProgrammingComparison.GreaterOrEqual,
                        rhs: rhs,
                        name: $"{name}-ge");
            }
            default:
                return Unsupported(
                    "未知 CP 比较关系 / Unknown CP comparison");
        }
    }

    private Try CompileGatedNegatedComparison(
        LinearForm enforcement,
        LinearForm form,
        ConstraintProgrammingComparison comparison,
        BigInteger rhs,
        string name) => comparison switch {
            ConstraintProgrammingComparison.LessOrEqual => CompileGatedComparison(
                enforcement: enforcement,
                form: form,
                comparison: ConstraintProgrammingComparison.GreaterOrEqual,
                rhs: rhs + BigInteger.One,
                name: name),
            ConstraintProgrammingComparison.GreaterOrEqual => CompileGatedComparison(
                enforcement: enforcement,
                form: form,
                comparison: ConstraintProgrammingComparison.LessOrEqual,
                rhs: rhs - BigInteger.One,
                name: name),
            ConstraintProgrammingComparison.Equal => CompileGatedNotEqual(
                enforcement: enforcement,
                form: form,
                rhs: rhs,
                name: name),
            _ => Unsupported(
                "未知 CP 比较关系 / Unknown CP comparison"),
        };

    private Try CompileGatedNotEqual(
        LinearForm enforcement,
        LinearForm form,
        BigInteger rhs,
        string name) {
        (BigInteger Lower, BigInteger Upper)? bounds = FormBounds(form);
        if (bounds is null) {
            return Unsupported(
                "不等式 reification 需要有限表达式界 / " +
                "Not-equal reification requires finite expression bounds");
        }
        Result<IVariableItem, ErrorCode, Error<ErrorCode>> left =
            AuxiliaryBinary($"{name}-left");
        if (left is Failed<
                IVariableItem,
                ErrorCode,
                Error<ErrorCode>> leftFailure) {
            return Results.Failed<Success>(leftFailure.Error);
        }
        Result<IVariableItem, ErrorCode, Error<ErrorCode>> right =
            AuxiliaryBinary($"{name}-right");
        if (right is Failed<
                IVariableItem,
                ErrorCode,
                Error<ErrorCode>> rightFailure) {
            return Results.Failed<Success>(rightFailure.Error);
        }

        Try branch = AddConstraint(
            form: Combine(
                Form(left.Value, right.Value),
                enforcement,
                BigInteger.One,
                -BigInteger.One),
            comparison: Comparison.GE,
            rhs: BigInteger.Zero,
            name: $"{name}-branch");
        if (branch is Failed<Success, ErrorCode, Error<ErrorCode>> branchFailure) {
            return branchFailure;
        }

        var leftM = BigInteger.Max(
            BigInteger.One,
            bounds.Value.Upper - (rhs - BigInteger.One));
        Try leftLink = AddConstraint(
            form: Combine(
                form,
                Form(left.Value),
                BigInteger.One,
                leftM),
            comparison: Comparison.LE,
            rhs: rhs - BigInteger.One + leftM,
            name: $"{name}-left-link");
        if (leftLink is Failed<
                Success,
                ErrorCode,
                Error<ErrorCode>> leftLinkFailure) {
            return leftLinkFailure;
        }

        var rightM = BigInteger.Max(
            BigInteger.One,
            rhs + BigInteger.One - bounds.Value.Lower);
        return AddConstraint(
            form: Combine(
                form,
                Form(right.Value),
                BigInteger.One,
                -rightM),
            comparison: Comparison.GE,
            rhs: rhs + BigInteger.One - rightM,
            name: $"{name}-right-link");
    }

    private Try CompileAllDifferent(
        ConstraintProgrammingConstraint.AllDifferent constraint,
        string name) {
        if (constraint.Expressions.Count > _policy.DecompositionLimit) {
            return Unsupported(
                "AllDifferent 分解规模超限 / " +
                "AllDifferent decomposition exceeds the limit");
        }

        for (int leftIndex = 0;
             leftIndex < constraint.Expressions.Count;
             ++leftIndex) {
            for (int rightIndex = leftIndex + 1;
                 rightIndex < constraint.Expressions.Count;
                 ++rightIndex) {
                Result<LinearForm, ErrorCode, Error<ErrorCode>> left =
                    ExpressionForm(constraint.Expressions[leftIndex]);
                if (left is Failed<
                        LinearForm,
                        ErrorCode,
                        Error<ErrorCode>> leftFailure) {
                    return Results.Failed<Success>(leftFailure.Error);
                }
                Result<LinearForm, ErrorCode, Error<ErrorCode>> right =
                    ExpressionForm(constraint.Expressions[rightIndex]);
                if (right is Failed<
                        LinearForm,
                        ErrorCode,
                        Error<ErrorCode>> rightFailure) {
                    return Results.Failed<Success>(rightFailure.Error);
                }

                LinearForm difference = Combine(
                    left.Value,
                    right.Value,
                    BigInteger.One,
                    -BigInteger.One);
                (BigInteger Lower, BigInteger Upper)? bounds = FormBounds(difference);
                if (bounds is null) {
                    return Unsupported(
                        "AllDifferent 需要有限表达式界 / " +
                        "AllDifferent requires finite expression bounds");
                }
                BigInteger m = BigInteger.Max(
                    BigInteger.Abs(bounds.Value.Lower),
                    BigInteger.Abs(bounds.Value.Upper)) + BigInteger.One;
                Result<IVariableItem, ErrorCode, Error<ErrorCode>> order =
                    AuxiliaryBinary(
                        $"{name}-order-{leftIndex}-{rightIndex}");
                if (order is Failed<
                        IVariableItem,
                        ErrorCode,
                        Error<ErrorCode>> orderFailure) {
                    return Results.Failed<Success>(orderFailure.Error);
                }

                Try upper = AddConstraint(
                    form: Combine(
                        difference,
                        Form(order.Value),
                        BigInteger.One,
                        m),
                    comparison: Comparison.LE,
                    rhs: m - BigInteger.One,
                    name: $"{name}-upper-{leftIndex}-{rightIndex}");
                if (upper is Failed<
                        Success,
                        ErrorCode,
                        Error<ErrorCode>> upperFailure) {
                    return upperFailure;
                }
                Try lower = AddConstraint(
                    form: Combine(
                        difference.Negate(),
                        Form(order.Value),
                        BigInteger.One,
                        -m),
                    comparison: Comparison.LE,
                    rhs: -BigInteger.One,
                    name: $"{name}-lower-{leftIndex}-{rightIndex}");
                if (lower is Failed<
                        Success,
                        ErrorCode,
                        Error<ErrorCode>> lowerFailure) {
                    return lowerFailure;
                }
            }
        }
        return Results.Ok(Results.SuccessInstance);
    }

    private Try CompileElement(
        ConstraintProgrammingConstraint.Element constraint,
        string name) {
        if (constraint.Values.Count > _policy.DecompositionLimit) {
            return Unsupported(
                "Element 分解规模超限 / Element decomposition exceeds the limit");
        }
        var values = new List<BigInteger>(constraint.Values.Count);
        foreach (ConstraintProgrammingExpression expression in constraint.Values) {
            if (expression is not ConstraintProgrammingExpression.Constant constant) {
                return Unsupported(
                    "Element 首版只支持常量数组 / " +
                    "The first Element lowerer supports constant arrays only");
            }
            values.Add(new BigInteger(constant.Value.ToLong()));
        }

        var selectors = new List<IVariableItem>(values.Count);
        for (int index = 0; index < values.Count; ++index) {
            Result<IVariableItem, ErrorCode, Error<ErrorCode>> selector =
                AuxiliaryBinary($"{name}-select-{index}");
            if (selector is Failed<
                    IVariableItem,
                    ErrorCode,
                    Error<ErrorCode>> selectorFailure) {
                return Results.Failed<Success>(selectorFailure.Error);
            }
            selectors.Add(selector.Value);
        }
        Try one = AddConstraint(
            form: Form(selectors, BigInteger.One),
            comparison: Comparison.EQ,
            rhs: BigInteger.One,
            name: $"{name}-one");
        if (one is Failed<Success, ErrorCode, Error<ErrorCode>> oneFailure) {
            return oneFailure;
        }

        Result<LinearForm, ErrorCode, Error<ErrorCode>> indexForm =
            ExpressionForm(constraint.Index);
        if (indexForm is Failed<
                LinearForm,
                ErrorCode,
                Error<ErrorCode>> indexFailure) {
            return Results.Failed<Success>(indexFailure.Error);
        }
        Result<LinearForm, ErrorCode, Error<ErrorCode>> targetForm =
            ExpressionForm(constraint.Target);
        if (targetForm is Failed<
                LinearForm,
                ErrorCode,
                Error<ErrorCode>> targetFailure) {
            return Results.Failed<Success>(targetFailure.Error);
        }

        Dictionary<IVariableItem, BigInteger> indexTerms =
            NewTerms(indexForm.Value.Terms);
        Dictionary<IVariableItem, BigInteger> targetTerms =
            NewTerms(targetForm.Value.Terms);
        for (int position = 0; position < selectors.Count; ++position) {
            indexTerms[selectors[position]] =
                (indexTerms.GetValueOrDefault(selectors[position]) -
                 new BigInteger(position));
            targetTerms[selectors[position]] =
                (targetTerms.GetValueOrDefault(selectors[position]) -
                 values[position]);
        }
        Try indexLink = AddConstraint(
            form: Form(indexTerms, indexForm.Value.Constant),
            comparison: Comparison.EQ,
            rhs: BigInteger.Zero,
            name: $"{name}-index");
        return indexLink is Failed<
            Success,
            ErrorCode,
            Error<ErrorCode>> indexLinkFailure
            ? indexLinkFailure
            : AddConstraint(
                form: Form(targetTerms, targetForm.Value.Constant),
                comparison: Comparison.EQ,
                rhs: BigInteger.Zero,
                name: $"{name}-target");
    }

    private Try CompileAllowedAssignments(
        ConstraintProgrammingConstraint.AllowedAssignments constraint,
        string name) {
        if (constraint.Tuples.Count > _policy.DecompositionLimit) {
            return Unsupported(
                "Allowed table 分解规模超限 / " +
                "Allowed table decomposition exceeds the limit");
        }
        if (constraint.Tuples.Any(tuple =>
                tuple.Count != constraint.Expressions.Count)) {
            return Failure(
                "Allowed table 元组列数不一致 / " +
                "Allowed table tuples have inconsistent arity");
        }

        var selectors = new List<IVariableItem>(constraint.Tuples.Count);
        for (int index = 0; index < constraint.Tuples.Count; ++index) {
            Result<IVariableItem, ErrorCode, Error<ErrorCode>> selector =
                AuxiliaryBinary($"{name}-tuple-{index}");
            if (selector is Failed<
                    IVariableItem,
                    ErrorCode,
                    Error<ErrorCode>> selectorFailure) {
                return Results.Failed<Success>(selectorFailure.Error);
            }
            selectors.Add(selector.Value);
        }
        Try one = AddConstraint(
            form: Form(selectors, BigInteger.One),
            comparison: Comparison.EQ,
            rhs: BigInteger.One,
            name: $"{name}-one");
        if (one is Failed<Success, ErrorCode, Error<ErrorCode>> oneFailure) {
            return oneFailure;
        }

        for (int column = 0; column < constraint.Expressions.Count; ++column) {
            Result<LinearForm, ErrorCode, Error<ErrorCode>> expression =
                ExpressionForm(constraint.Expressions[column]);
            if (expression is Failed<
                    LinearForm,
                    ErrorCode,
                    Error<ErrorCode>> expressionFailure) {
                return Results.Failed<Success>(expressionFailure.Error);
            }
            Dictionary<IVariableItem, BigInteger> terms =
                NewTerms(expression.Value.Terms);
            for (int row = 0; row < constraint.Tuples.Count; ++row) {
                terms[selectors[row]] =
                    (terms.GetValueOrDefault(selectors[row]) -
                     new BigInteger(constraint.Tuples[row][column].ToLong()));
            }
            Try link = AddConstraint(
                form: Form(terms, expression.Value.Constant),
                comparison: Comparison.EQ,
                rhs: BigInteger.Zero,
                name: $"{name}-link-{column}");
            if (link is Failed<
                    Success,
                    ErrorCode,
                    Error<ErrorCode>> linkFailure) {
                return linkFailure;
            }
        }
        return Results.Ok(Results.SuccessInstance);
    }

    private Try CompileForbiddenAssignments(
        ConstraintProgrammingConstraint.ForbiddenAssignments constraint,
        string name) {
        if (!_policy.AllowForbiddenAssignments) {
            return Unsupported(
                "策略禁止 forbidden table / The policy disables forbidden tables");
        }
        if (constraint.Tuples.Count > _policy.DecompositionLimit) {
            return Unsupported(
                "Forbidden table 分解规模超限 / " +
                "Forbidden table decomposition exceeds the limit");
        }
        if (constraint.Tuples.Any(tuple =>
                tuple.Count != constraint.Expressions.Count)) {
            return Failure(
                "Forbidden table 元组列数不一致 / " +
                "Forbidden table tuples have inconsistent arity");
        }

        for (int row = 0; row < constraint.Tuples.Count; ++row) {
            var differences = new List<IVariableItem>();
            for (int column = 0; column < constraint.Expressions.Count; ++column) {
                Result<LinearForm, ErrorCode, Error<ErrorCode>> expression =
                    ExpressionForm(constraint.Expressions[column]);
                if (expression is Failed<
                        LinearForm,
                        ErrorCode,
                        Error<ErrorCode>> expressionFailure) {
                    return Results.Failed<Success>(expressionFailure.Error);
                }
                (BigInteger Lower, BigInteger Upper)? bounds =
                    FormBounds(expression.Value);
                if (bounds is null) {
                    return Unsupported(
                        "Forbidden table 需要有限表达式界 / " +
                        "Forbidden table requires finite expression bounds");
                }
                BigInteger value = new(constraint.Tuples[row][column].ToLong());
                Result<IVariableItem, ErrorCode, Error<ErrorCode>> left =
                    AuxiliaryBinary($"{name}-{row}-{column}-left");
                if (left is Failed<
                        IVariableItem,
                        ErrorCode,
                        Error<ErrorCode>> leftFailure) {
                    return Results.Failed<Success>(leftFailure.Error);
                }
                Result<IVariableItem, ErrorCode, Error<ErrorCode>> right =
                    AuxiliaryBinary($"{name}-{row}-{column}-right");
                if (right is Failed<
                        IVariableItem,
                        ErrorCode,
                        Error<ErrorCode>> rightFailure) {
                    return Results.Failed<Success>(rightFailure.Error);
                }
                differences.Add(left.Value);
                differences.Add(right.Value);

                var leftM = BigInteger.Max(
                    BigInteger.One,
                    bounds.Value.Upper - (value - BigInteger.One));
                Try leftLink = AddConstraint(
                    form: Combine(
                        expression.Value,
                        Form(left.Value),
                        BigInteger.One,
                        leftM),
                    comparison: Comparison.LE,
                    rhs: value - BigInteger.One + leftM,
                    name: $"{name}-{row}-{column}-left-link");
                if (leftLink is Failed<
                        Success,
                        ErrorCode,
                        Error<ErrorCode>> leftLinkFailure) {
                    return leftLinkFailure;
                }
                var rightM = BigInteger.Max(
                    BigInteger.One,
                    value + BigInteger.One - bounds.Value.Lower);
                Try rightLink = AddConstraint(
                    form: Combine(
                        expression.Value,
                        Form(right.Value),
                        BigInteger.One,
                        -rightM),
                    comparison: Comparison.GE,
                    rhs: value + BigInteger.One - rightM,
                    name: $"{name}-{row}-{column}-right-link");
                if (rightLink is Failed<
                        Success,
                        ErrorCode,
                        Error<ErrorCode>> rightLinkFailure) {
                    return rightLinkFailure;
                }
            }
            Try difference = AddConstraint(
                form: Form(differences, BigInteger.One),
                comparison: Comparison.GE,
                rhs: BigInteger.One,
                name: $"{name}-{row}-difference");
            if (difference is Failed<
                    Success,
                    ErrorCode,
                    Error<ErrorCode>> differenceFailure) {
                return differenceFailure;
            }
        }
        return Results.Ok(Results.SuccessInstance);
    }

    private Try CompileNoOverlap(NoOverlap noOverlap, string name) {
        if (noOverlap.Intervals.Count > _policy.DecompositionLimit) {
            return Unsupported(
                "NoOverlap 分解规模超限 / " +
                "NoOverlap decomposition exceeds the limit");
        }

        for (int firstIndex = 0;
             firstIndex < noOverlap.Intervals.Count;
             ++firstIndex) {
            for (int secondIndex = firstIndex + 1;
                 secondIndex < noOverlap.Intervals.Count;
                 ++secondIndex) {
                if (!_intervals.TryGetValue(
                        noOverlap.Intervals[firstIndex].Id,
                        out LoweredInterval? first)) {
                    return Failure(
                        "NoOverlap 引用了未注册 interval / " +
                        "NoOverlap references an unknown interval");
                }
                if (!_intervals.TryGetValue(
                        noOverlap.Intervals[secondIndex].Id,
                        out LoweredInterval? second)) {
                    return Failure(
                        "NoOverlap 引用了未注册 interval / " +
                        "NoOverlap references an unknown interval");
                }

                LinearForm firstEnd = Plus(Form(first.Start), first.Size);
                LinearForm secondEnd = Plus(Form(second.Start), second.Size);
                (BigInteger Lower, BigInteger Upper)? firstEndBounds =
                    FormBounds(firstEnd);
                (BigInteger Lower, BigInteger Upper)? secondStartBounds =
                    FormBounds(Form(second.Start));
                (BigInteger Lower, BigInteger Upper)? secondEndBounds =
                    FormBounds(secondEnd);
                (BigInteger Lower, BigInteger Upper)? firstStartBounds =
                    FormBounds(Form(first.Start));
                if (firstEndBounds is null ||
                    secondStartBounds is null ||
                    secondEndBounds is null ||
                    firstStartBounds is null) {
                    return Unsupported(
                        "NoOverlap 需要有限 start/duration 值域 / " +
                        "NoOverlap requires finite start and duration bounds");
                }
                var firstM = BigInteger.Max(
                    BigInteger.One,
                    firstEndBounds.Value.Upper - secondStartBounds.Value.Lower);
                var secondM = BigInteger.Max(
                    BigInteger.One,
                    secondEndBounds.Value.Upper - firstStartBounds.Value.Lower);
                Result<IVariableItem, ErrorCode, Error<ErrorCode>> order =
                    AuxiliaryBinary($"{name}-order-{firstIndex}-{secondIndex}");
                if (order is Failed<
                        IVariableItem,
                        ErrorCode,
                        Error<ErrorCode>> orderFailure) {
                    return Results.Failed<Success>(orderFailure.Error);
                }

                LinearForm firstPresence =
                    first.Presence ?? Form(BigInteger.One);
                LinearForm secondPresence =
                    second.Presence ?? Form(BigInteger.One);
                LinearForm firstBefore = Combine(
                    Combine(
                        firstEnd,
                        Form(second.Start),
                        BigInteger.One,
                        -BigInteger.One),
                    Form(order.Value),
                    BigInteger.One,
                    -firstM);
                firstBefore = Combine(
                    firstBefore,
                    firstPresence,
                    BigInteger.One,
                    firstM);
                firstBefore = Combine(
                    firstBefore,
                    secondPresence,
                    BigInteger.One,
                    firstM);
                firstBefore = firstBefore.PlusConstant(-firstM * 2);
                Try firstConstraint = AddConstraint(
                    form: firstBefore,
                    comparison: Comparison.LE,
                    rhs: BigInteger.Zero,
                    name: $"{name}-first-before-{firstIndex}-{secondIndex}");
                if (firstConstraint is Failed<
                        Success,
                        ErrorCode,
                        Error<ErrorCode>> firstConstraintFailure) {
                    return firstConstraintFailure;
                }

                LinearForm secondBefore = Combine(
                    Combine(
                        secondEnd,
                        Form(first.Start),
                        BigInteger.One,
                        -BigInteger.One),
                    Form(order.Value),
                    BigInteger.One,
                    secondM);
                secondBefore = Combine(
                    secondBefore,
                    firstPresence,
                    BigInteger.One,
                    secondM);
                secondBefore = Combine(
                    secondBefore,
                    secondPresence,
                    BigInteger.One,
                    secondM);
                secondBefore = secondBefore.PlusConstant(-secondM * 3);
                Try secondConstraint = AddConstraint(
                    form: secondBefore,
                    comparison: Comparison.LE,
                    rhs: BigInteger.Zero,
                    name: $"{name}-second-before-{firstIndex}-{secondIndex}");
                if (secondConstraint is Failed<
                        Success,
                        ErrorCode,
                        Error<ErrorCode>> secondConstraintFailure) {
                    return secondConstraintFailure;
                }
            }
        }
        return Results.Ok(Results.SuccessInstance);
    }

    private Try CompileCumulative(Cumulative cumulative, string name) {
        if (cumulative.Intervals.Count == 0 ||
            cumulative.Intervals.Count != cumulative.Demands.Count) {
            return Failure(
                "Cumulative interval 与 demand 数量必须一致且非空 / " +
                "Cumulative interval and demand counts must match and be non-empty");
        }

        var demandValues = new List<BigInteger>(cumulative.Demands.Count);
        foreach (ConstraintProgrammingExpression demand in cumulative.Demands) {
            if (demand is not ConstraintProgrammingExpression.Constant constant) {
                return Unsupported(
                    "Cumulative MIP lowering 仅支持常量 demand / " +
                    "Cumulative MIP lowering supports constant demands only");
            }
            BigInteger value = new(constant.Value.ToLong());
            if (value.Sign < 0) {
                return Failure(
                    "Cumulative demand 不得为负 / Cumulative demand must not be negative");
            }
            demandValues.Add(value);
        }
        if (cumulative.Capacity is not ConstraintProgrammingExpression.Constant capacityConstant) {
            return Unsupported(
                "Cumulative MIP lowering 仅支持常量 capacity / " +
                "Cumulative MIP lowering supports constant capacity only");
        }
        BigInteger capacity = new(capacityConstant.Value.ToLong());
        if (capacity.Sign < 0) {
            return Failure(
                "Cumulative capacity 不得为负 / Cumulative capacity must not be negative");
        }

        var activeIntervals = new List<(LoweredInterval Interval, BigInteger Demand)>(
            cumulative.Intervals.Count);
        for (int index = 0; index < cumulative.Intervals.Count; ++index) {
            if (!_intervals.TryGetValue(
                    cumulative.Intervals[index].Id,
                    out LoweredInterval? interval)) {
                return Failure(
                    "Cumulative 引用了未注册 interval / " +
                    "Cumulative references an unknown interval");
            }
            BigInteger demand = demandValues[index];
            if (demand.IsZero) {
                continue;
            }
            (BigInteger Lower, BigInteger Upper)? durationBounds =
                FormBounds(interval.Size);
            if (durationBounds is null) {
                return Unsupported(
                    "Cumulative 需要有限整数 duration 值域 / " +
                    "Cumulative requires finite integer duration bounds");
            }
            if (durationBounds.Value.Lower.IsZero &&
                durationBounds.Value.Upper.IsZero) {
                continue;
            }
            activeIntervals.Add((interval, demand));
        }
        if (activeIntervals.Count == 0) {
            return Results.Ok(Results.SuccessInstance);
        }

        BigInteger? horizonLower = null;
        BigInteger? horizonUpper = null;
        var intervalBounds = new List<(
            (BigInteger Lower, BigInteger Upper) Start,
            (BigInteger Lower, BigInteger Upper) End)>(activeIntervals.Count);
        foreach ((LoweredInterval interval, BigInteger _) in activeIntervals) {
            (BigInteger Lower, BigInteger Upper)? startBounds =
                FormBounds(Form(interval.Start));
            (BigInteger Lower, BigInteger Upper)? endBounds =
                FormBounds(Form(interval.End));
            if (startBounds is null || endBounds is null) {
                return Unsupported(
                    "Cumulative 需要有限整数 start/end 值域 / " +
                    "Cumulative requires finite integer start and end bounds");
            }
            intervalBounds.Add((startBounds.Value, endBounds.Value));
            horizonLower = horizonLower is null
                ? startBounds.Value.Lower
                : BigInteger.Min(horizonLower.Value, startBounds.Value.Lower);
            horizonUpper = horizonUpper is null
                ? endBounds.Value.Upper
                : BigInteger.Max(horizonUpper.Value, endBounds.Value.Upper);
        }
        if (horizonLower is null || horizonUpper is null) {
            return Results.Ok(Results.SuccessInstance);
        }

        var timeSlots = BigInteger.Max(
            BigInteger.Zero,
            horizonUpper.Value - horizonLower.Value);
        if (timeSlots > new BigInteger(_policy.MaxCumulativeTimeSlots)) {
            return Unsupported(
                "Cumulative 整数时隙数超出策略上限 / " +
                "Cumulative time-slot count exceeds the configured limit");
        }
        BigInteger work = timeSlots * new BigInteger(activeIntervals.Count);
        if (work > new BigInteger(_policy.MaxCumulativeWork)) {
            return Unsupported(
                "Cumulative interval-slot 工作规模超出策略上限 / " +
                "Cumulative interval-slot work exceeds the configured limit");
        }
        if (timeSlots.IsZero) {
            return Results.Ok(Results.SuccessInstance);
        }

        for (BigInteger time = horizonLower.Value;
             time < horizonUpper.Value;
             time += BigInteger.One) {
            LinearForm load = Form(BigInteger.Zero);
            for (int index = 0; index < activeIntervals.Count; ++index) {
                (LoweredInterval interval, BigInteger demand) =
                    activeIntervals[index];
                ((BigInteger Lower, BigInteger Upper) Start,
                    (BigInteger Lower, BigInteger Upper) End) bounds =
                    intervalBounds[index];
                Result<LinearForm, ErrorCode, Error<ErrorCode>> startBefore =
                    ExactLessOrEqualPredicate(
                        value: Form(interval.Start),
                        bounds: bounds.Start,
                        threshold: time,
                        name: $"{name}-start-{index}-{time}");
                if (startBefore is Failed<
                        LinearForm,
                        ErrorCode,
                        Error<ErrorCode>> startFailure) {
                    return Results.Failed<Success>(startFailure.Error);
                }
                Result<LinearForm, ErrorCode, Error<ErrorCode>> endBefore =
                    ExactLessOrEqualPredicate(
                        value: Form(interval.End),
                        bounds: bounds.End,
                        threshold: time,
                        name: $"{name}-end-{index}-{time}");
                if (endBefore is Failed<
                        LinearForm,
                        ErrorCode,
                        Error<ErrorCode>> endFailure) {
                    return Results.Failed<Success>(endFailure.Error);
                }

                var conditions = new List<LinearForm> {
                    startBefore.Value,
                    BooleanNot(endBefore.Value),
                };
                if (interval.Presence is not null) {
                    conditions.Add(interval.Presence);
                }
                Result<LinearForm, ErrorCode, Error<ErrorCode>> active =
                    ExactConjunction(
                        conditions,
                        $"{name}-active-{index}-{time}");
                if (active is Failed<
                        LinearForm,
                        ErrorCode,
                        Error<ErrorCode>> activeFailure) {
                    return Results.Failed<Success>(activeFailure.Error);
                }
                load = Plus(
                    load,
                    Combine(
                        active.Value,
                        Form(BigInteger.Zero),
                        demand,
                        BigInteger.Zero));
            }

            Try slot = AddConstraint(
                form: load,
                comparison: Comparison.LE,
                rhs: capacity,
                name: $"{name}-slot-{time}");
            if (slot is Failed<
                    Success,
                    ErrorCode,
                    Error<ErrorCode>> slotFailure) {
                return slotFailure;
            }
        }
        return Results.Ok(Results.SuccessInstance);
    }

    private Result<LinearForm, ErrorCode, Error<ErrorCode>>
        ExactLessOrEqualPredicate(
            LinearForm value,
            (BigInteger Lower, BigInteger Upper) bounds,
            BigInteger threshold,
            string name) {
        if (bounds.Upper <= threshold) {
            return Results.Ok(Form(BigInteger.One));
        }
        if (bounds.Lower > threshold) {
            return Results.Ok(Form(BigInteger.Zero));
        }

        Result<IVariableItem, ErrorCode, Error<ErrorCode>> indicator =
            AuxiliaryBinary(name);
        if (indicator is Failed<
                IVariableItem,
                ErrorCode,
                Error<ErrorCode>> indicatorFailure) {
            return Results.Failed<LinearForm>(indicatorFailure.Error);
        }
        LinearForm indicatorForm = Form(indicator.Value);
        Try upper = AddConstraint(
            form: Combine(
                value,
                indicatorForm,
                BigInteger.One,
                bounds.Upper - threshold),
            comparison: Comparison.LE,
            rhs: bounds.Upper,
            name: $"{name}-upper");
        if (upper is Failed<Success, ErrorCode, Error<ErrorCode>> upperFailure) {
            return Results.Failed<LinearForm>(upperFailure.Error);
        }
        Try lower = AddConstraint(
            form: Combine(
                value,
                indicatorForm,
                BigInteger.One,
                threshold + BigInteger.One - bounds.Lower),
            comparison: Comparison.GE,
            rhs: threshold + BigInteger.One,
            name: $"{name}-lower");
        return lower is Failed<
            Success,
            ErrorCode,
            Error<ErrorCode>> lowerFailure
            ? Results.Failed<LinearForm>(lowerFailure.Error)
            : Results.Ok(indicatorForm);
    }

    private Result<LinearForm, ErrorCode, Error<ErrorCode>> ExactConjunction(
        IReadOnlyList<LinearForm> conditions,
        string name) {
        var nonConstant = new List<LinearForm>(conditions.Count);
        foreach (LinearForm condition in conditions) {
            if (condition.Terms.Count == 0) {
                if (condition.Constant.IsZero) {
                    return Results.Ok(Form(BigInteger.Zero));
                }
                if (condition.Constant == BigInteger.One) {
                    continue;
                }
                return Unsupported<LinearForm>(
                    "Cumulative 布尔指示条件不是 0/1 / " +
                    "Cumulative boolean condition is not 0/1");
            }
            nonConstant.Add(condition);
        }
        if (nonConstant.Count == 0) {
            return Results.Ok(Form(BigInteger.One));
        }
        if (nonConstant.Count == 1) {
            return Results.Ok(nonConstant[0]);
        }

        Result<IVariableItem, ErrorCode, Error<ErrorCode>> indicator =
            AuxiliaryBinary(name);
        if (indicator is Failed<
                IVariableItem,
                ErrorCode,
                Error<ErrorCode>> indicatorFailure) {
            return Results.Failed<LinearForm>(indicatorFailure.Error);
        }
        LinearForm result = Form(indicator.Value);
        for (int index = 0; index < nonConstant.Count; ++index) {
            Try upper = AddConstraint(
                form: Combine(
                    result,
                    nonConstant[index],
                    BigInteger.One,
                    -BigInteger.One),
                comparison: Comparison.LE,
                rhs: BigInteger.Zero,
                name: $"{name}-upper-{index}");
            if (upper is Failed<
                    Success,
                    ErrorCode,
                    Error<ErrorCode>> upperFailure) {
                return Results.Failed<LinearForm>(upperFailure.Error);
            }
        }

        LinearForm lower = result;
        foreach (LinearForm condition in nonConstant) {
            lower = Combine(
                lower,
                condition,
                BigInteger.One,
                -BigInteger.One);
        }
        Try lowerConstraint = AddConstraint(
            form: lower,
            comparison: Comparison.GE,
            rhs: -new BigInteger(nonConstant.Count - 1),
            name: $"{name}-lower");
        return lowerConstraint is Failed<
            Success,
            ErrorCode,
            Error<ErrorCode>> lowerFailure
            ? Results.Failed<LinearForm>(lowerFailure.Error)
            : Results.Ok(result);
    }

    private static LinearForm BooleanNot(LinearForm condition) =>
        CombineStatic(
            Form(BigInteger.One),
            condition,
            BigInteger.One,
            -BigInteger.One);

    private Try CompileObjectives() {
        if (_snapshot.Objectives.Count > 1) {
            return Unsupported(
                "MIP-backed CP 首版只支持单一目标 / " +
                "The first MIP-backed CP lowerer supports one objective");
        }
        if (_snapshot.Objectives.Count == 0) {
            return Results.Ok(Results.SuccessInstance);
        }
        ConstraintProgrammingObjectiveSnapshot objective = _snapshot.Objectives[0];
        if (objective.Category != _snapshot.ObjectCategory) {
            return Unsupported(
                "目标方向与模型方向不一致 / " +
                "Objective category does not match the model category");
        }
        Result<LinearForm, ErrorCode, Error<ErrorCode>> expression =
            ExpressionForm(objective.Expression);
        if (expression is Failed<
                LinearForm,
                ErrorCode,
                Error<ErrorCode>> expressionFailure) {
            return Results.Failed<Success>(expressionFailure.Error);
        }
        Result<LinearPolynomial<Flt64>, ErrorCode, Error<ErrorCode>> polynomial =
            ToPolynomial(expression.Value);
        return polynomial is Failed<
            LinearPolynomial<Flt64>,
            ErrorCode,
            Error<ErrorCode>> polynomialFailure
            ? Results.Failed<Success>(polynomialFailure.Error)
            : _linear.AddObject(
                category: objective.Category,
                polynomial: polynomial.Value,
                name: objective.Name,
                displayName: null);
    }

    private Result<LinearForm, ErrorCode, Error<ErrorCode>> ExpressionForm(
        ConstraintProgrammingExpression expression) {
        switch (expression) {
            case ConstraintProgrammingExpression.Constant constant:
                return Results.Ok(Form(new BigInteger(constant.Value.ToLong())));
            case ConstraintProgrammingExpression.Invalid invalid:
                return Failure<LinearForm>(
                    invalid.Message,
                    ErrorCode.IllegalArgument);
            case ConstraintProgrammingExpression.Variable variable:
                return Variables.TryGetValue(variable.VariableId, out IVariableItem? item)
                    ? Results.Ok(Form(item))
                    : Failure<LinearForm>(
                        "CP 表达式变量未注册 / " +
                        "CP expression variable is not registered",
                        ErrorCode.IllegalArgument);
            case ConstraintProgrammingExpression.Linear linear: {
                Dictionary<IVariableItem, BigInteger> terms = NewTerms();
                foreach (ConstraintProgrammingExpression.Term term in linear.Terms) {
                    if (!Variables.TryGetValue(
                            term.VariableId,
                            out IVariableItem? linearItem)) {
                        return Failure<LinearForm>(
                            "CP 线性项变量未注册 / " +
                            "CP linear-term variable is not registered",
                            ErrorCode.IllegalArgument);
                    }
                    terms[linearItem] =
                        terms.GetValueOrDefault(linearItem) +
                        new BigInteger(term.Coefficient.ToLong());
                }
                return Results.Ok(Form(
                    terms,
                    new BigInteger(linear.ConstantTerm.ToLong())));
            }
            default:
                return Unsupported<LinearForm>(
                    "未知 CP 表达式节点 / Unknown CP expression node");
        }
    }

    private Result<LinearForm, ErrorCode, Error<ErrorCode>> LiteralForm(
        BooleanLiteral literal) {
        if (literal.Constant is { } constant) {
            return Results.Ok(Form(constant ? BigInteger.One : BigInteger.Zero));
        }
        if (literal.VariableId is not { } id ||
            !Variables.TryGetValue(id, out IVariableItem? variable)) {
            return Failure<LinearForm>(
                "布尔文字变量未注册 / Boolean literal variable is not registered",
                ErrorCode.IllegalArgument);
        }
        return Results.Ok(
            literal.Negated
                ? Combine(
                    Form(BigInteger.One),
                    Form(variable),
                    BigInteger.One,
                    -BigInteger.One)
                : Form(variable));
    }

    private Result<LinearForm, ErrorCode, Error<ErrorCode>> LiteralSum(
        IReadOnlyList<BooleanLiteral> literals) {
        LinearForm result = Form(BigInteger.Zero);
        foreach (BooleanLiteral literal in literals) {
            Result<LinearForm, ErrorCode, Error<ErrorCode>> form =
                LiteralForm(literal);
            if (form is Failed<LinearForm, ErrorCode, Error<ErrorCode>> failure) {
                return Results.Failed<LinearForm>(failure.Error);
            }
            result = Plus(result, form.Value);
        }
        return Results.Ok(result);
    }

    private Result<IVariableItem, ErrorCode, Error<ErrorCode>> AuxiliaryBinary(
        string name) {
        ++_auxiliaryVariables;
        if (_auxiliaryVariables > _policy.AuxiliaryVariableLimit) {
            return Unsupported<IVariableItem>(
                "辅助变量数量超出降阶规模上限 / " +
                "Auxiliary-variable count exceeds the lowering limit");
        }
        var variable = new BinVar(
            $"cp-aux-{Sanitize(name)}-{_auxiliaryVariables}");
        Try added = _linear.Add(variable);
        if (added is Failed<Success, ErrorCode, Error<ErrorCode>> addFailure) {
            return Results.Failed<IVariableItem>(addFailure.Error);
        }
        _domains[variable] = (BigInteger.Zero, BigInteger.One);
        RegisterArtifact(
            artifactId: $"variable:{variable.Identifier}:{variable.Index}",
            role: "auxiliary-variable",
            originId: _activeSourceConstraintId?.Value);
        return Results.Ok<IVariableItem>(variable);
    }

    private void RegisterArtifact(
        string artifactId,
        string role,
        string? originId) {
        string resolvedId = artifactId;
        int collision = 1;
        while (Artifacts.ContainsKey(resolvedId)) {
            resolvedId = $"{artifactId}#{collision}";
            ++collision;
        }
        Artifacts[resolvedId] = new ConstraintProgrammingLoweredArtifact(
            ArtifactId: resolvedId,
            Role: role,
            OriginId: originId);
    }

    private static Dictionary<IVariableItem, BigInteger> NewTerms() =>
        new(ReferenceEqualityComparer.Instance);

    private static Dictionary<IVariableItem, BigInteger> NewTerms(
        IReadOnlyDictionary<IVariableItem, BigInteger> source) {
        Dictionary<IVariableItem, BigInteger> result = NewTerms();
        foreach ((IVariableItem variable, BigInteger coefficient) in source) {
            result[variable] = coefficient;
        }
        return result;
    }

    private static LinearForm Form(BigInteger constant) =>
        new(NewTerms(), constant);

    private static LinearForm Form(IVariableItem variable) =>
        Form(new Dictionary<IVariableItem, BigInteger>(
            ReferenceEqualityComparer.Instance) {
            [variable] = BigInteger.One,
        }, BigInteger.Zero);

    private static LinearForm Form(
        IVariableItem left,
        IVariableItem right) =>
        Form(new Dictionary<IVariableItem, BigInteger>(
            ReferenceEqualityComparer.Instance) {
            [left] = BigInteger.One,
            [right] = BigInteger.One,
        }, BigInteger.Zero);

    private static LinearForm Form(
        IEnumerable<IVariableItem> variables,
        BigInteger coefficient) {
        Dictionary<IVariableItem, BigInteger> terms = NewTerms();
        foreach (IVariableItem variable in variables) {
            terms[variable] = coefficient;
        }
        return Form(terms, BigInteger.Zero);
    }

    private static LinearForm Form(
        IReadOnlyDictionary<IVariableItem, BigInteger> terms,
        BigInteger constant) {
        Dictionary<IVariableItem, BigInteger> normalized = NewTerms();
        foreach ((IVariableItem variable, BigInteger coefficient) in terms) {
            if (!coefficient.IsZero) {
                normalized[variable] = coefficient;
            }
        }
        return new LinearForm(normalized, constant);
    }

    private static LinearForm Combine(
        LinearForm left,
        LinearForm right,
        BigInteger leftScale,
        BigInteger rightScale) {
        Dictionary<IVariableItem, BigInteger> terms = NewTerms();
        foreach ((IVariableItem variable, BigInteger coefficient) in left.Terms) {
            terms[variable] = coefficient * leftScale;
        }
        foreach ((IVariableItem variable, BigInteger coefficient) in right.Terms) {
            terms[variable] =
                terms.GetValueOrDefault(variable) + coefficient * rightScale;
        }
        return Form(
            terms,
            left.Constant * leftScale + right.Constant * rightScale);
    }

    private static LinearForm CombineStatic(
        LinearForm left,
        LinearForm right,
        BigInteger leftScale,
        BigInteger rightScale) =>
        Combine(left, right, leftScale, rightScale);

    private static LinearForm Plus(LinearForm left, LinearForm right) =>
        Combine(left, right, BigInteger.One, BigInteger.One);

    private Try AddConstraint(
        LinearForm form,
        Comparison comparison,
        BigInteger rhs,
        string name) {
        Result<LinearPolynomial<Flt64>, ErrorCode, Error<ErrorCode>> polynomial =
            ToPolynomial(form);
        if (polynomial is Failed<
                LinearPolynomial<Flt64>,
                ErrorCode,
                Error<ErrorCode>> polynomialFailure) {
            return Results.Failed<Success>(polynomialFailure.Error);
        }
        Result<Flt64, ErrorCode, Error<ErrorCode>> right =
            SafeFlt64(rhs, "CP rhs");
        if (right is Failed<Flt64, ErrorCode, Error<ErrorCode>> rhsFailure) {
            return Results.Failed<Success>(rhsFailure.Error);
        }

        try {
            var rhsPolynomial = new LinearPolynomial<Flt64>(
                Array.Empty<LinearMonomial<Flt64>>(),
                right.Value);
            Try added = _linear.AddConstraint(
                relation: new LinearInequality<Flt64>(
                    polynomial.Value,
                    rhsPolynomial,
                    comparison),
                group: null,
                name: name);
            if (added is Failed<
                    Success,
                    ErrorCode,
                    Error<ErrorCode>> addFailure) {
                return addFailure;
            }
            RegisterArtifact(
                artifactId: $"constraint:{name}",
                role: "compiled-constraint",
                originId: _activeSourceConstraintId?.Value);
            if (_activeSourceConstraintId is { } sourceId &&
                _linear.RelationConstraints.Count > 0) {
                ConstraintProvenance[_linear.RelationConstraints[^1]] = sourceId;
            }
            return added;
        }
        catch (Exception error) {
            return Failure(
                "CP 线性约束注册失败：" +
                $"{error.Message} / CP linear constraint registration failed",
                ErrorCode.OREngineModelingException);
        }
    }

    private Result<LinearPolynomial<Flt64>, ErrorCode, Error<ErrorCode>>
        ToPolynomial(LinearForm form) {
        var monomials = new List<LinearMonomial<Flt64>>(form.Terms.Count);
        foreach ((IVariableItem variable, BigInteger coefficient) in
                 form.Terms.OrderBy(pair => pair.Key.Identifier)
                     .ThenBy(pair => pair.Key.Index)) {
            Result<Flt64, ErrorCode, Error<ErrorCode>> converted =
                SafeFlt64(coefficient, "CP coefficient");
            if (converted is Failed<
                    Flt64,
                    ErrorCode,
                    Error<ErrorCode>> coefficientFailure) {
                return Results.Failed<LinearPolynomial<Flt64>>(
                    coefficientFailure.Error);
            }
            monomials.Add(new LinearMonomial<Flt64>(
                converted.Value,
                variable));
        }
        Result<Flt64, ErrorCode, Error<ErrorCode>> constant =
            SafeFlt64(form.Constant, "CP constant");
        return constant is Failed<Flt64, ErrorCode, Error<ErrorCode>> constantFailure
            ? Results.Failed<LinearPolynomial<Flt64>>(constantFailure.Error)
            : Results.Ok(new LinearPolynomial<Flt64>(
                monomials,
                constant.Value));
    }

    private (BigInteger Lower, BigInteger Upper)? FormBounds(LinearForm form) {
        BigInteger lower = form.Constant;
        BigInteger upper = form.Constant;
        foreach ((IVariableItem variable, BigInteger coefficient) in form.Terms) {
            if (!_domains.TryGetValue(
                    variable,
                    out (BigInteger Lower, BigInteger Upper) bounds)) {
                return null;
            }
            if (coefficient.Sign >= 0) {
                lower += coefficient * bounds.Lower;
                upper += coefficient * bounds.Upper;
            }
            else {
                lower += coefficient * bounds.Upper;
                upper += coefficient * bounds.Lower;
            }
        }
        return (lower, upper);
    }

    private Result<IVariableItem, ErrorCode, Error<ErrorCode>> AsVariable(
        ConstraintProgrammingExpression expression) {
        if (expression is ConstraintProgrammingExpression.Variable variable &&
            Variables.TryGetValue(variable.VariableId, out IVariableItem? item)) {
            return Results.Ok(item);
        }
        return Unsupported<IVariableItem>(
            "interval start/end 必须是标量变量 / " +
            "Interval start/end must be scalar variables");
    }

    private static Comparison ToComparison(
        ConstraintProgrammingComparison comparison) => comparison switch {
            ConstraintProgrammingComparison.Equal => Comparison.EQ,
            ConstraintProgrammingComparison.LessOrEqual => Comparison.LE,
            ConstraintProgrammingComparison.GreaterOrEqual => Comparison.GE,
            _ => Comparison.EQ,
        };

    private static bool IsBooleanDomain(IntegerDomain domain) =>
        domain.LowerBound == Int64.Zero &&
        domain.UpperBound == Int64.One &&
        domain.Cardinality == 2 &&
        domain.Contains(Int64.Zero) &&
        domain.Contains(Int64.One);

    private static string Sanitize(string value) {
        char[] chars = value.ToCharArray();
        for (int index = 0; index < chars.Length; ++index) {
            if (!char.IsLetterOrDigit(chars[index]) &&
                chars[index] != '_' &&
                chars[index] != '-') {
                chars[index] = '_';
            }
        }
        return new string(chars);
    }

    private static Result<Flt64, ErrorCode, Error<ErrorCode>> SafeFlt64(
        BigInteger value,
        string context) {
        if (BigInteger.Abs(value) > MaxExactDouble) {
            return Failure<Flt64>(
                $"{context} 超出 Flt64 精确整数范围 / " +
                $"{context} exceeds the exact Flt64 integer range",
                ErrorCode.Other);
        }
        return Results.Ok(new Flt64((double)value));
    }

    private static Try Failure(
        string message,
        ErrorCode code = ErrorCode.IllegalArgument) =>
        Results.Failed<Success>(new Err<ErrorCode>(code, message));

    private static Result<T, ErrorCode, Error<ErrorCode>> Failure<T>(
        string message,
        ErrorCode code = ErrorCode.IllegalArgument) =>
        Results.Failed<T>(new Err<ErrorCode>(code, message));

    private static Try Unsupported(string message) =>
        Failure($"Unsupported: {message}", ErrorCode.Other);

    private static Result<T, ErrorCode, Error<ErrorCode>> Unsupported<T>(
        string message) =>
        Failure<T>($"Unsupported: {message}", ErrorCode.Other);

    private sealed record LoweredInterval(
        IVariableItem Start,
        IVariableItem End,
        LinearForm Size,
        LinearForm? Presence);

    private sealed record LinearForm(
        IReadOnlyDictionary<IVariableItem, BigInteger> Terms,
        BigInteger Constant) {
        internal LinearForm Negate() {
            Dictionary<IVariableItem, BigInteger> terms = NewTerms();
            foreach ((IVariableItem variable, BigInteger coefficient) in Terms) {
                terms[variable] = -coefficient;
            }
            return Compiler.Form(terms, -Constant);
        }

        internal LinearForm PlusConstant(BigInteger value) =>
            new(Terms, Constant + value);
    }

    private static readonly BigInteger MaxExactDouble =
        new(9_007_199_254_740_991L);
}
