#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Model.ConstraintProgramming;

/// <summary>
/// 独立 CP 模型注册器与生命周期，不继承线性或二次 MetaModel。
/// Independent CP model registry and lifecycle, intentionally not a linear or quadratic MetaModel.
/// </summary>
public sealed class ConstraintProgrammingModel : IDisposable {
    private sealed record VariableEntry(
        IVariableItem Variable,
        IntegerDomain Domain,
        ModelElementScope Scope,
        string? Origin,
        IReadOnlyList<ModelElementOrigin> Provenance);

    private sealed record ConstraintEntry(
        ConstraintId Id,
        string Name,
        string? GroupName,
        ConstraintProgrammingConstraint Constraint,
        ModelElementScope Scope,
        string? Origin,
        IReadOnlyList<ModelElementOrigin> Provenance);

    private sealed record ObjectiveEntry(
        ObjectiveId Id,
        ObjectCategory Category,
        string Name,
        ConstraintProgrammingExpression Expression,
        ModelElementScope Scope,
        string? Origin,
        IReadOnlyList<ModelElementOrigin> Provenance);

    private readonly List<VariableEntry> _variables = new();
    private readonly List<IntervalVariable> _intervals = new();
    private readonly List<ConstraintEntry> _constraints = new();
    private readonly List<ObjectiveEntry> _objectives = new();
    private readonly List<(string Name, ConstraintProgrammingExpression Expression)> _expressions = new();
    private readonly List<string> _groups = new();
    private readonly Dictionary<VariableId, VariableEntry> _variablesById = new();
    private readonly Dictionary<IntervalId, IntervalVariable> _intervalsById = new();
    private readonly Dictionary<ConstraintId, ConstraintEntry> _constraintsById = new();
    private readonly Dictionary<ObjectiveId, ObjectiveEntry> _objectivesById = new();
    private readonly Dictionary<IVariableItem, VariableId> _variableIdsByIdentity =
        new(ReferenceEqualityComparer.Instance);
    private string? _currentGroupName;
    private bool _closed;

    /// <summary>创建 CP 模型 / Create a CP model.</summary>
    /// <param name="name">模型名称 / Model name.</param>
    /// <param name="objectCategory">模型优化方向 / Model optimization direction.</param>
    /// <param name="identityNamespace">身份命名空间 / Identity namespace.</param>
    /// <param name="identitySchemaVersion">身份 schema 版本 / Identity schema version.</param>
    public ConstraintProgrammingModel(
        string name = "constraint-programming-model",
        ObjectCategory objectCategory = ObjectCategory.Minimum,
        string identityNamespace = "model-local",
        string identitySchemaVersion = "1.0") {
        Name = name;
        ObjectCategory = objectCategory;
        IdentityNamespace = identityNamespace;
        IdentitySchemaVersion = identitySchemaVersion;
    }

    /// <summary>模型名称 / Model name.</summary>
    public string Name { get; }

    /// <summary>模型优化方向 / Model optimization direction.</summary>
    public ObjectCategory ObjectCategory { get; }

    /// <summary>身份命名空间 / Identity namespace.</summary>
    public string IdentityNamespace { get; }

    /// <summary>身份 schema 版本 / Identity schema version.</summary>
    public string IdentitySchemaVersion { get; }

    /// <summary>模型是否已关闭 / Whether the model is closed.</summary>
    public bool IsClosed => _closed;

    /// <summary>已注册标量变量数量 / Number of registered scalar variables.</summary>
    public int VariableCount => _variables.Count;

    /// <summary>已注册约束数量 / Number of registered constraints.</summary>
    public int ConstraintCount => _constraints.Count;

    /// <summary>已注册 interval 数量 / Number of registered intervals.</summary>
    public int IntervalCount => _intervals.Count;

    /// <summary>注册现有 OSPF 整数变量 / Register an existing OSPF integer variable.</summary>
    /// <param name="variable">OSPF 标量变量 / OSPF scalar variable.</param>
    /// <param name="domain">CP 值域；为空时按变量类型推导 / CP domain, inferred from type when null.</param>
    /// <param name="scope">身份作用域 / Identity scope.</param>
    /// <param name="origin">兼容来源字段 / Compatibility origin field.</param>
    /// <param name="identityProvenance">完整身份来源 / Complete identity provenance.</param>
    /// <returns>稳定变量 ID 或结构化错误 / Stable variable ID or a structured error.</returns>
    public Result<VariableId, ErrorCode, Error<ErrorCode>> RegisterVariable(
        IVariableItem variable,
        IntegerDomain? domain = null,
        ModelElementScope scope = ModelElementScope.ModelLocal,
        string? origin = null,
        IReadOnlyList<ModelElementOrigin>? identityProvenance = null) {
        if (_closed) {
            return Closed<VariableId>();
        }
        ArgumentNullException.ThrowIfNull(variable);
        return RegisterVariableCore(
            id: VariableIdOf(variable),
            variable: variable,
            domain: domain,
            scope: scope,
            origin: origin,
            identityProvenance: identityProvenance);
    }

    /// <summary>使用显式稳定 ID 注册变量 / Register a variable with an explicit stable ID.</summary>
    /// <param name="id">稳定变量 ID / Stable variable ID.</param>
    /// <param name="variable">OSPF 标量变量 / OSPF scalar variable.</param>
    /// <param name="domain">CP 值域 / CP domain.</param>
    /// <param name="scope">身份作用域 / Identity scope.</param>
    /// <param name="origin">兼容来源字段 / Compatibility origin field.</param>
    /// <param name="identityProvenance">完整身份来源 / Complete identity provenance.</param>
    /// <returns>稳定变量 ID 或结构化错误 / Stable variable ID or a structured error.</returns>
    public Result<VariableId, ErrorCode, Error<ErrorCode>> RegisterVariable(
        VariableId id,
        IVariableItem variable,
        IntegerDomain domain,
        ModelElementScope scope = ModelElementScope.ModelLocal,
        string? origin = null,
        IReadOnlyList<ModelElementOrigin>? identityProvenance = null) {
        if (_closed) {
            return Closed<VariableId>();
        }
        ArgumentNullException.ThrowIfNull(variable);
        ArgumentNullException.ThrowIfNull(domain);
        return RegisterVariableCore(
            id: id,
            variable: variable,
            domain: domain,
            scope: scope,
            origin: origin,
            identityProvenance: identityProvenance);
    }

    /// <summary>注册 interval 结构变量 / Register an interval structural variable.</summary>
    /// <param name="interval">interval 定义 / Interval definition.</param>
    /// <returns>稳定 interval ID 或结构化错误 / Stable interval ID or a structured error.</returns>
    public Result<IntervalId, ErrorCode, Error<ErrorCode>> RegisterInterval(
        IntervalVariable interval) {
        if (_closed) {
            return Closed<IntervalId>();
        }
        ArgumentNullException.ThrowIfNull(interval);
        Result<ConstraintProgrammingIdentityMetadata, ErrorCode, Error<ErrorCode>> identity =
            ConstraintProgrammingIdentity.Canonicalize(
                id: interval.Id.Value,
                scope: interval.Scope,
                origin: interval.Origin,
                provenance: interval.IdentityProvenance,
                identityNamespace: IdentityNamespace,
                identitySchemaVersion: IdentitySchemaVersion);
        if (identity.IsFailed) {
            return identity.Map(_ => default(IntervalId)!);
        }
        if (_intervalsById.ContainsKey(interval.Id)) {
            return Failure<IntervalId>(
                code: ErrorCode.IllegalArgument,
                message: $"CP interval ID 重复：{interval.Id.Value} / Duplicate CP interval ID: {interval.Id.Value}");
        }
        IntervalVariable registered = interval.WithIdentity(
            scope: identity.Value.Scope,
            provenance: identity.Value.Provenance);
        _intervals.Add(registered);
        _intervalsById[interval.Id] = registered;
        return Results.Ok(interval.Id);
    }

    /// <summary>注册具名表达式 / Register a named expression.</summary>
    /// <param name="name">表达式名称 / Expression name.</param>
    /// <param name="expression">表达式 AST / Expression AST.</param>
    /// <returns>注册名称或结构化错误 / Registered name or a structured error.</returns>
    public Result<string, ErrorCode, Error<ErrorCode>> RegisterExpression(
        string name,
        ConstraintProgrammingExpression expression) {
        if (_closed) {
            return Closed<string>();
        }
        ArgumentNullException.ThrowIfNull(expression);
        if (string.IsNullOrWhiteSpace(name)) {
            return Failure<string>(
                code: ErrorCode.IllegalArgument,
                message: "CP 表达式名称不能为空 / CP expression name must not be blank");
        }
        if (_expressions.Any(entry => string.Equals(entry.Name, name, StringComparison.Ordinal))) {
            return Failure<string>(
                code: ErrorCode.IllegalArgument,
                message: $"CP 表达式名称重复：{name} / Duplicate CP expression name: {name}");
        }
        _expressions.Add((name, expression));
        return Results.Ok(name);
    }

    /// <summary>注册约束 / Register a constraint.</summary>
    /// <param name="constraint">约束 AST / Constraint AST.</param>
    /// <param name="id">可选稳定约束 ID / Optional stable constraint ID.</param>
    /// <param name="name">展示名称 / Display name.</param>
    /// <param name="group">约束组 / Constraint group.</param>
    /// <param name="scope">身份作用域 / Identity scope.</param>
    /// <param name="origin">兼容来源字段 / Compatibility origin field.</param>
    /// <param name="identityProvenance">完整身份来源 / Complete identity provenance.</param>
    /// <returns>稳定约束 ID 或结构化错误 / Stable constraint ID or a structured error.</returns>
    public Result<ConstraintId, ErrorCode, Error<ErrorCode>> AddConstraint(
        ConstraintProgrammingConstraint constraint,
        ConstraintId? id = null,
        string name = "",
        IMetaConstraintGroup? group = null,
        ModelElementScope scope = ModelElementScope.ModelLocal,
        string? origin = null,
        IReadOnlyList<ModelElementOrigin>? identityProvenance = null) {
        if (_closed) {
            return Closed<ConstraintId>();
        }
        ArgumentNullException.ThrowIfNull(constraint);
        ConstraintId resolvedId = id ?? new ConstraintId($"constraint-{_constraints.Count}");
        Result<ConstraintProgrammingIdentityMetadata, ErrorCode, Error<ErrorCode>> identity =
            ConstraintProgrammingIdentity.Canonicalize(
                id: resolvedId.Value,
                scope: scope,
                origin: origin,
                provenance: identityProvenance,
                identityNamespace: IdentityNamespace,
                identitySchemaVersion: IdentitySchemaVersion);
        if (identity.IsFailed) {
            return identity.Map(_ => default(ConstraintId)!);
        }
        if (_constraintsById.ContainsKey(resolvedId)) {
            return Failure<ConstraintId>(
                code: ErrorCode.IllegalArgument,
                message: $"CP 约束 ID 重复：{resolvedId.Value} / Duplicate CP constraint ID: {resolvedId.Value}");
        }
        if (group is not null) {
            Try registered = RegisterConstraintGroup(group);
            if (registered.IsFailed) {
                return Propagate<ConstraintId>(registered);
            }
        }

        var entry = new ConstraintEntry(
            Id: resolvedId,
            Name: string.IsNullOrWhiteSpace(name) ? resolvedId.Value : name,
            GroupName: group?.Name ?? _currentGroupName,
            Constraint: constraint,
            Scope: identity.Value.Scope,
            Origin: origin,
            Provenance: identity.Value.Provenance);
        _constraints.Add(entry);
        _constraintsById[resolvedId] = entry;
        return Results.Ok(resolvedId);
    }

    /// <summary>使用字符串 ID 注册约束 / Register a constraint with a string ID.</summary>
    /// <param name="constraint">约束 AST / Constraint AST.</param>
    /// <param name="id">稳定约束 ID 字符串 / Stable constraint ID string.</param>
    /// <param name="name">展示名称 / Display name.</param>
    /// <param name="group">约束组 / Constraint group.</param>
    /// <param name="scope">身份作用域 / Identity scope.</param>
    /// <param name="origin">兼容来源字段 / Compatibility origin field.</param>
    /// <param name="identityProvenance">完整身份来源 / Complete identity provenance.</param>
    /// <returns>稳定约束 ID 或结构化错误 / Stable constraint ID or a structured error.</returns>
    public Result<ConstraintId, ErrorCode, Error<ErrorCode>> AddConstraint(
        ConstraintProgrammingConstraint constraint,
        string id,
        string name = "",
        IMetaConstraintGroup? group = null,
        ModelElementScope scope = ModelElementScope.ModelLocal,
        string? origin = null,
        IReadOnlyList<ModelElementOrigin>? identityProvenance = null) =>
        AddConstraint(
            constraint: constraint,
            id: new ConstraintId(id),
            name: name,
            group: group,
            scope: scope,
            origin: origin,
            identityProvenance: identityProvenance);

    /// <summary>注册目标 / Register an objective.</summary>
    /// <param name="category">优化方向 / Optimization direction.</param>
    /// <param name="expression">目标表达式 / Objective expression.</param>
    /// <param name="id">可选稳定目标 ID / Optional stable objective ID.</param>
    /// <param name="name">展示名称 / Display name.</param>
    /// <param name="scope">身份作用域 / Identity scope.</param>
    /// <param name="origin">兼容来源字段 / Compatibility origin field.</param>
    /// <param name="identityProvenance">完整身份来源 / Complete identity provenance.</param>
    /// <returns>稳定目标 ID 或结构化错误 / Stable objective ID or a structured error.</returns>
    public Result<ObjectiveId, ErrorCode, Error<ErrorCode>> AddObjective(
        ObjectCategory category,
        ConstraintProgrammingExpression expression,
        ObjectiveId? id = null,
        string name = "",
        ModelElementScope scope = ModelElementScope.ModelLocal,
        string? origin = null,
        IReadOnlyList<ModelElementOrigin>? identityProvenance = null) {
        if (_closed) {
            return Closed<ObjectiveId>();
        }
        ArgumentNullException.ThrowIfNull(expression);
        ObjectiveId resolvedId = id ?? new ObjectiveId($"objective-{_objectives.Count}");
        Result<ConstraintProgrammingIdentityMetadata, ErrorCode, Error<ErrorCode>> identity =
            ConstraintProgrammingIdentity.Canonicalize(
                id: resolvedId.Value,
                scope: scope,
                origin: origin,
                provenance: identityProvenance,
                identityNamespace: IdentityNamespace,
                identitySchemaVersion: IdentitySchemaVersion);
        if (identity.IsFailed) {
            return identity.Map(_ => default(ObjectiveId)!);
        }
        if (_objectivesById.ContainsKey(resolvedId)) {
            return Failure<ObjectiveId>(
                code: ErrorCode.IllegalArgument,
                message: $"CP 目标 ID 重复：{resolvedId.Value} / Duplicate CP objective ID: {resolvedId.Value}");
        }

        var entry = new ObjectiveEntry(
            Id: resolvedId,
            Category: category,
            Name: string.IsNullOrWhiteSpace(name) ? resolvedId.Value : name,
            Expression: expression,
            Scope: identity.Value.Scope,
            Origin: origin,
            Provenance: identity.Value.Provenance);
        _objectives.Add(entry);
        _objectivesById[resolvedId] = entry;
        return Results.Ok(resolvedId);
    }

    /// <summary>注册最小化目标 / Register a minimization objective.</summary>
    /// <param name="expression">目标表达式 / Objective expression.</param>
    /// <param name="id">可选稳定目标 ID / Optional stable objective ID.</param>
    /// <param name="name">展示名称 / Display name.</param>
    /// <param name="scope">身份作用域 / Identity scope.</param>
    /// <param name="origin">兼容来源字段 / Compatibility origin field.</param>
    /// <param name="identityProvenance">完整身份来源 / Complete identity provenance.</param>
    /// <returns>稳定目标 ID 或结构化错误 / Stable objective ID or a structured error.</returns>
    public Result<ObjectiveId, ErrorCode, Error<ErrorCode>> Minimize(
        ConstraintProgrammingExpression expression,
        ObjectiveId? id = null,
        string name = "",
        ModelElementScope scope = ModelElementScope.ModelLocal,
        string? origin = null,
        IReadOnlyList<ModelElementOrigin>? identityProvenance = null) =>
        AddObjective(
            category: ObjectCategory.Minimum,
            expression: expression,
            id: id,
            name: name,
            scope: scope,
            origin: origin,
            identityProvenance: identityProvenance);

    /// <summary>注册最大化目标 / Register a maximization objective.</summary>
    /// <param name="expression">目标表达式 / Objective expression.</param>
    /// <param name="id">可选稳定目标 ID / Optional stable objective ID.</param>
    /// <param name="name">展示名称 / Display name.</param>
    /// <param name="scope">身份作用域 / Identity scope.</param>
    /// <param name="origin">兼容来源字段 / Compatibility origin field.</param>
    /// <param name="identityProvenance">完整身份来源 / Complete identity provenance.</param>
    /// <returns>稳定目标 ID 或结构化错误 / Stable objective ID or a structured error.</returns>
    public Result<ObjectiveId, ErrorCode, Error<ErrorCode>> Maximize(
        ConstraintProgrammingExpression expression,
        ObjectiveId? id = null,
        string name = "",
        ModelElementScope scope = ModelElementScope.ModelLocal,
        string? origin = null,
        IReadOnlyList<ModelElementOrigin>? identityProvenance = null) =>
        AddObjective(
            category: ObjectCategory.Maximum,
            expression: expression,
            id: id,
            name: name,
            scope: scope,
            origin: origin,
            identityProvenance: identityProvenance);

    /// <summary>注册约束组 / Register a constraint group.</summary>
    /// <param name="group">约束组 / Constraint group.</param>
    /// <returns>注册结果 / Registration result.</returns>
    public Try RegisterConstraintGroup(IMetaConstraintGroup group) {
        if (_closed) {
            return Closed<Success>();
        }
        ArgumentNullException.ThrowIfNull(group);
        if (string.IsNullOrWhiteSpace(group.Name)) {
            return Failure<Success>(
                code: ErrorCode.IllegalArgument,
                message: "CP 约束组名称不能为空 / CP constraint group name must not be blank");
        }
        if (!_groups.Contains(group.Name, StringComparer.Ordinal)) {
            _groups.Add(group.Name);
        }
        _currentGroupName = group.Name;
        return Results.OkInstance;
    }

    /// <summary>获取约束组中的约束 / Get constraints belonging to a group.</summary>
    /// <param name="group">约束组 / Constraint group.</param>
    /// <returns>该组中的约束 / Constraints in the group.</returns>
    public IReadOnlyList<ConstraintProgrammingConstraint> ConstraintsOfGroup(IMetaConstraintGroup group) {
        ArgumentNullException.ThrowIfNull(group);
        return _constraints
            .Where(entry => string.Equals(entry.GroupName, group.Name, StringComparison.Ordinal))
            .Select(entry => entry.Constraint)
            .ToList();
    }

    /// <summary>校验当前模型的所有引用与目标语义 / Validate all references and objective semantics.</summary>
    /// <returns>成功或第一个结构化错误 / Success or the first structured error.</returns>
    public Try Validate() {
        if (_closed) {
            return Closed<Success>();
        }
        Try objective = ValidateObjectives();
        if (objective.IsFailed) {
            return objective;
        }

        var knownVariables = _variableIdsByIdentity.Values.ToHashSet();
        foreach ((string expressionName, ConstraintProgrammingExpression expression) in _expressions) {
            Try reference = ValidateReferences(
                owner: $"表达式 {expressionName}",
                variables: expression.BindStableVariableIds(_variableIdsByIdentity).Variables,
                knownVariables: knownVariables);
            if (reference.IsFailed) {
                return reference;
            }
        }
        foreach (IntervalVariable interval in _intervals) {
            Try reference = ValidateReferences(
                owner: $"interval {interval.Id.Value}",
                variables: interval.BindStableVariableIds(_variableIdsByIdentity).Variables,
                knownVariables: knownVariables);
            if (reference.IsFailed) {
                return reference;
            }
        }
        foreach (ConstraintEntry entry in _constraints) {
            Try reference = ValidateReferences(
                owner: $"约束 {entry.Id.Value}",
                variables: entry.Constraint.BindStableVariableIds(_variableIdsByIdentity).Variables,
                knownVariables: knownVariables);
            if (reference.IsFailed) {
                return reference;
            }
        }
        foreach (ObjectiveEntry entry in _objectives) {
            Try reference = ValidateReferences(
                owner: $"目标 {entry.Id.Value}",
                variables: entry.Expression.BindStableVariableIds(_variableIdsByIdentity).Variables,
                knownVariables: knownVariables);
            if (reference.IsFailed) {
                return reference;
            }
        }
        return Results.OkInstance;
    }

    /// <summary>生成可重复编译的不可变快照 / Build an immutable, repeatably compilable snapshot.</summary>
    /// <returns>不可变快照或结构化错误 / Immutable snapshot or a structured error.</returns>
    public Result<
        ConstraintProgrammingModelSnapshot,
        ErrorCode,
        Error<ErrorCode>> Snapshot() {
        if (_closed) {
            return Closed<ConstraintProgrammingModelSnapshot>();
        }
        Try valid = Validate();
        if (valid.IsFailed) {
            return Propagate<ConstraintProgrammingModelSnapshot>(valid);
        }

        return ConstraintProgrammingModelSnapshot.Create(
            name: Name,
            objectCategory: ObjectCategory,
            variables: _variables.Select(entry => new ConstraintProgrammingVariableSnapshot(
                id: _variableIdsByIdentity[entry.Variable],
                name: entry.Variable.Name,
                typeName: entry.Variable.TypeKind.ToString() ?? "unknown",
                domain: entry.Domain,
                scope: entry.Scope,
                origin: entry.Origin,
                identityProvenance: entry.Provenance)),
            intervals: _intervals.Select(interval =>
                interval.BindStableVariableIds(_variableIdsByIdentity)),
            expressions: _expressions.Select(entry => new ConstraintProgrammingExpressionSnapshot(
                name: entry.Name,
                expression: entry.Expression.BindStableVariableIds(_variableIdsByIdentity))),
            constraints: _constraints.Select(entry => new ConstraintProgrammingConstraintSnapshot(
                id: entry.Id,
                name: entry.Name,
                groupName: entry.GroupName,
                constraint: entry.Constraint.BindStableVariableIds(_variableIdsByIdentity),
                scope: entry.Scope,
                origin: entry.Origin,
                identityProvenance: entry.Provenance)),
            objectives: _objectives.Select(entry => new ConstraintProgrammingObjectiveSnapshot(
                id: entry.Id,
                category: entry.Category,
                name: entry.Name,
                expression: entry.Expression.BindStableVariableIds(_variableIdsByIdentity),
                scope: entry.Scope,
                origin: entry.Origin,
                identityProvenance: entry.Provenance)),
            constraintGroups: _groups,
            identitySchemaVersion: IdentitySchemaVersion,
            identityNamespace: IdentityNamespace);
    }

    /// <summary>关闭并释放注册表 / Close and release the registries.</summary>
    public void Close() {
        if (_closed) {
            return;
        }
        _closed = true;
        _variables.Clear();
        _intervals.Clear();
        _constraints.Clear();
        _objectives.Clear();
        _expressions.Clear();
        _groups.Clear();
        _variablesById.Clear();
        _intervalsById.Clear();
        _constraintsById.Clear();
        _objectivesById.Clear();
        _variableIdsByIdentity.Clear();
        _currentGroupName = null;
    }

    /// <inheritdoc/>
    public void Dispose() => Close();

    private Result<VariableId, ErrorCode, Error<ErrorCode>> RegisterVariableCore(
        VariableId id,
        IVariableItem variable,
        IntegerDomain? domain,
        ModelElementScope scope,
        string? origin,
        IReadOnlyList<ModelElementOrigin>? identityProvenance) {
        Result<ConstraintProgrammingIdentityMetadata, ErrorCode, Error<ErrorCode>> identity =
            ConstraintProgrammingIdentity.Canonicalize(
                id: id.Value,
                scope: scope,
                origin: origin,
                provenance: identityProvenance,
                identityNamespace: IdentityNamespace,
                identitySchemaVersion: IdentitySchemaVersion);
        if (identity.IsFailed) {
            return identity.Map(_ => default(VariableId)!);
        }
        Result<
            ConstraintProgrammingExpression.Variable,
            ErrorCode,
            Error<ErrorCode>> reference =
            ConstraintProgrammingExpression.CreateVariable(
                variable: variable,
                domain: domain);
        if (reference.IsFailed) {
            return reference.Map(_ => default(VariableId)!);
        }
        if (_variablesById.ContainsKey(id)) {
            return Failure<VariableId>(
                code: ErrorCode.IllegalArgument,
                message: $"CP 变量 ID 重复：{id.Value} / Duplicate CP variable ID: {id.Value}");
        }
        if (_variableIdsByIdentity.TryGetValue(variable, out VariableId existingId) &&
            existingId != id) {
            return Failure<VariableId>(
                code: ErrorCode.IllegalArgument,
                message: "同一 OSPF 变量不能绑定多个 CP ID / " +
                    "The same OSPF variable cannot be bound to multiple CP IDs");
        }

        var entry = new VariableEntry(
            Variable: variable,
            Domain: reference.Value.Domain,
            Scope: identity.Value.Scope,
            Origin: origin,
            Provenance: identity.Value.Provenance);
        _variables.Add(entry);
        _variablesById[id] = entry;
        _variableIdsByIdentity[variable] = id;
        return Results.Ok(id);
    }

    private Try ValidateObjectives() {
        if (_objectives.Count > 1) {
            return Failure<Success>(
                code: ErrorCode.IllegalArgument,
                message: "CP 模型只支持单一目标 / CP models support at most one objective");
        }
        return _objectives.Count == 1 && _objectives[0].Category != ObjectCategory
            ? Failure<Success>(
                code: ErrorCode.IllegalArgument,
                message: "CP 目标方向与模型方向不一致 / CP objective category disagrees with the model category")
            : Results.OkInstance;
    }

    private static Try ValidateReferences(
        string owner,
        IReadOnlySet<VariableId> variables,
        IReadOnlySet<VariableId> knownVariables) {
        string? missing = variables
            .Where(variable => !knownVariables.Contains(variable))
            .Select(variable => variable.Value)
            .OrderBy(value => value, StringComparer.Ordinal)
            .FirstOrDefault();
        return missing is null
            ? Results.OkInstance
            : Failure<Success>(
                code: ErrorCode.IllegalArgument,
                message: $"{owner} 引用了未注册变量：{missing} / " +
                    $"{owner} references an unregistered variable: {missing}");
    }

    private static VariableId VariableIdOf(IVariableItem variable) =>
        new($"{variable.Identifier}:{variable.Index}");

    private static Result<T, ErrorCode, Error<ErrorCode>> Closed<T>() =>
        Failure<T>(
            code: ErrorCode.ApplicationStopped,
            message: "CP 模型已关闭 / CP model is closed");

    private static Result<T, ErrorCode, Error<ErrorCode>> Failure<T>(
        ErrorCode code,
        string message) =>
        Results.Failed<T>(
            new Err<ErrorCode>(
                code: code,
                message: message));

    private static Result<T, ErrorCode, Error<ErrorCode>> Propagate<T>(
        Result<T, ErrorCode, Error<ErrorCode>> result) =>
        result switch {
            Failed<T, ErrorCode, Error<ErrorCode>> failed =>
                Results.Failed<T>(failed.Error),
            Fatal<T, ErrorCode, Error<ErrorCode>> fatal =>
                new Fatal<T, ErrorCode, Error<ErrorCode>>(fatal.Errors),
            _ => Results.Ok(result.Value),
        };

    private static Result<T, ErrorCode, Error<ErrorCode>> Propagate<T>(
        Try result) =>
        result switch {
            Failed<Success, ErrorCode, Error<ErrorCode>> failed =>
                Results.Failed<T>(failed.Error),
            Fatal<Success, ErrorCode, Error<ErrorCode>> fatal =>
                new Fatal<T, ErrorCode, Error<ErrorCode>>(fatal.Errors),
            _ => Failure<T>(
                code: ErrorCode.ApplicationError,
                message: "CP 模型结果状态无效 / Invalid CP model result state"),
        };

}
