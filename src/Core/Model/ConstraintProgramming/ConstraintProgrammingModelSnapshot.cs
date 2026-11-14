#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Model.ConstraintProgramming;

/// <summary>快照中的标量变量定义 / Scalar-variable definition in a snapshot.</summary>
public sealed record ConstraintProgrammingVariableSnapshot {
    /// <summary>创建变量快照 / Create a variable snapshot.</summary>
    /// <param name="id">稳定变量 ID / Stable variable ID.</param>
    /// <param name="name">展示名称 / Display name.</param>
    /// <param name="typeName">变量类型名称 / Variable type name.</param>
    /// <param name="domain">整数值域 / Integer domain.</param>
    /// <param name="scope">身份作用域 / Identity scope.</param>
    /// <param name="origin">兼容来源字段 / Compatibility origin field.</param>
    /// <param name="identityProvenance">完整身份来源 / Complete identity provenance.</param>
    public ConstraintProgrammingVariableSnapshot(
        VariableId id,
        string name,
        string typeName,
        IntegerDomain domain,
        ModelElementScope scope = ModelElementScope.ModelLocal,
        string? origin = null,
        IReadOnlyList<ModelElementOrigin>? identityProvenance = null) {
        ArgumentNullException.ThrowIfNull(domain);
        Id = id;
        Name = name;
        TypeName = typeName;
        Domain = domain;
        Scope = scope;
        Origin = origin;
        IdentityProvenance = ConstraintProgrammingIdentity.CanonicalProvenance(
            origin: origin,
            provenance: identityProvenance);
    }

    /// <summary>稳定变量 ID / Stable variable ID.</summary>
    public VariableId Id { get; }

    /// <summary>展示名称 / Display name.</summary>
    public string Name { get; }

    /// <summary>变量类型名称 / Variable type name.</summary>
    public string TypeName { get; }

    /// <summary>整数值域 / Integer domain.</summary>
    public IntegerDomain Domain { get; }

    /// <summary>身份作用域 / Identity scope.</summary>
    public ModelElementScope Scope { get; }

    /// <summary>兼容来源字段 / Compatibility origin field.</summary>
    public string? Origin { get; }

    /// <summary>完整身份来源 / Complete identity provenance.</summary>
    public IReadOnlyList<ModelElementOrigin> IdentityProvenance { get; }
}

/// <summary>快照中的具名表达式 / Named expression in a snapshot.</summary>
public sealed record ConstraintProgrammingExpressionSnapshot {
    /// <summary>创建具名表达式快照 / Create a named-expression snapshot.</summary>
    /// <param name="name">表达式名称 / Expression name.</param>
    /// <param name="expression">表达式 AST / Expression AST.</param>
    public ConstraintProgrammingExpressionSnapshot(
        string name,
        ConstraintProgrammingExpression expression) {
        ArgumentNullException.ThrowIfNull(expression);
        Name = name;
        Expression = expression;
    }

    /// <summary>表达式名称 / Expression name.</summary>
    public string Name { get; }

    /// <summary>表达式 AST / Expression AST.</summary>
    public ConstraintProgrammingExpression Expression { get; }
}

/// <summary>快照中的约束定义 / Constraint definition in a snapshot.</summary>
public sealed record ConstraintProgrammingConstraintSnapshot {
    /// <summary>创建约束快照 / Create a constraint snapshot.</summary>
    /// <param name="id">稳定约束 ID / Stable constraint ID.</param>
    /// <param name="name">展示名称 / Display name.</param>
    /// <param name="groupName">约束组名称 / Constraint group name.</param>
    /// <param name="constraint">约束 AST / Constraint AST.</param>
    /// <param name="scope">身份作用域 / Identity scope.</param>
    /// <param name="origin">兼容来源字段 / Compatibility origin field.</param>
    /// <param name="identityProvenance">完整身份来源 / Complete identity provenance.</param>
    public ConstraintProgrammingConstraintSnapshot(
        ConstraintId id,
        string name,
        string? groupName,
        ConstraintProgrammingConstraint constraint,
        ModelElementScope scope = ModelElementScope.ModelLocal,
        string? origin = null,
        IReadOnlyList<ModelElementOrigin>? identityProvenance = null) {
        ArgumentNullException.ThrowIfNull(constraint);
        Id = id;
        Name = name;
        GroupName = groupName;
        Constraint = constraint;
        Scope = scope;
        Origin = origin;
        IdentityProvenance = ConstraintProgrammingIdentity.CanonicalProvenance(
            origin: origin,
            provenance: identityProvenance);
    }

    /// <summary>稳定约束 ID / Stable constraint ID.</summary>
    public ConstraintId Id { get; }

    /// <summary>展示名称 / Display name.</summary>
    public string Name { get; }

    /// <summary>约束组名称 / Constraint group name.</summary>
    public string? GroupName { get; }

    /// <summary>约束 AST / Constraint AST.</summary>
    public ConstraintProgrammingConstraint Constraint { get; }

    /// <summary>身份作用域 / Identity scope.</summary>
    public ModelElementScope Scope { get; }

    /// <summary>兼容来源字段 / Compatibility origin field.</summary>
    public string? Origin { get; }

    /// <summary>完整身份来源 / Complete identity provenance.</summary>
    public IReadOnlyList<ModelElementOrigin> IdentityProvenance { get; }
}

/// <summary>快照中的目标定义 / Objective definition in a snapshot.</summary>
public sealed record ConstraintProgrammingObjectiveSnapshot {
    /// <summary>创建目标快照 / Create an objective snapshot.</summary>
    /// <param name="id">稳定目标 ID / Stable objective ID.</param>
    /// <param name="category">优化方向 / Optimization direction.</param>
    /// <param name="name">展示名称 / Display name.</param>
    /// <param name="expression">目标表达式 / Objective expression.</param>
    /// <param name="scope">身份作用域 / Identity scope.</param>
    /// <param name="origin">兼容来源字段 / Compatibility origin field.</param>
    /// <param name="identityProvenance">完整身份来源 / Complete identity provenance.</param>
    public ConstraintProgrammingObjectiveSnapshot(
        ObjectiveId id,
        ObjectCategory category,
        string name,
        ConstraintProgrammingExpression expression,
        ModelElementScope scope = ModelElementScope.ModelLocal,
        string? origin = null,
        IReadOnlyList<ModelElementOrigin>? identityProvenance = null) {
        ArgumentNullException.ThrowIfNull(expression);
        Id = id;
        Category = category;
        Name = name;
        Expression = expression;
        Scope = scope;
        Origin = origin;
        IdentityProvenance = ConstraintProgrammingIdentity.CanonicalProvenance(
            origin: origin,
            provenance: identityProvenance);
    }

    /// <summary>稳定目标 ID / Stable objective ID.</summary>
    public ObjectiveId Id { get; }

    /// <summary>优化方向 / Optimization direction.</summary>
    public ObjectCategory Category { get; }

    /// <summary>展示名称 / Display name.</summary>
    public string Name { get; }

    /// <summary>目标表达式 / Objective expression.</summary>
    public ConstraintProgrammingExpression Expression { get; }

    /// <summary>身份作用域 / Identity scope.</summary>
    public ModelElementScope Scope { get; }

    /// <summary>兼容来源字段 / Compatibility origin field.</summary>
    public string? Origin { get; }

    /// <summary>完整身份来源 / Complete identity provenance.</summary>
    public IReadOnlyList<ModelElementOrigin> IdentityProvenance { get; }
}

/// <summary>
/// CP 模型的不可变快照；所有集合按注册顺序冻结，不持有可变模型或求解器句柄。
/// Immutable CP model snapshot with frozen registration order and no mutable model or solver handle.
/// </summary>
public sealed class ConstraintProgrammingModelSnapshot {
    /// <summary>
    /// 派生一份去掉全部约束的快照 / Derive a snapshot with every constraint removed.
    /// </summary>
    /// <returns>只剩变量、表达式与目标的快照 / A snapshot with only the variables, expressions and objectives left.</returns>
    /// <remarks>
    /// **assumption 派生模型要这个**：它把每条约束换成一个 guard 版本，
    /// 而不是"在原始约束旁边再加一条"——加在旁边的话原始约束仍无条件生效，**假设求解永远发现不了冲突。**
    ///
    /// **The assumption model needs this**: it replaces each constraint with a guarded version rather than adding one beside it, and beside it the original stays
    /// unconditionally active — **the assumption solve would never find a conflict.**
    /// </remarks>
    public ConstraintProgrammingModelSnapshot WithoutConstraints() =>
        new(
            Name,
            ObjectCategory,
            Variables,
            Intervals,
            Expressions,
            [],
            Objectives,
            ConstraintGroups,
            IdentitySchemaVersion,
            IdentityNamespace);

    /// <summary>
    /// 派生一份去掉一条约束的快照 / Derive a snapshot with one constraint removed.
    /// </summary>
    /// <param name="id">要去掉的约束 / The constraint to remove.</param>
    /// <param name="derived">派生结果 / The derived snapshot.</param>
    /// <returns>该约束是否存在 / Whether the constraint exists.</returns>
    /// <remarks>
    /// **找不到就返回 `false`，不返回原快照。** 删除测试的意义全在"模型真的少了这条约束"上：
    /// 给一份没少的快照，求出来的目标改善是 0，**报告会写"删除无效"——而这条约束可能极其关键。**
    ///
    /// **A miss returns `false` rather than the original snapshot.** The entire point of a removal test is that the model really lost that constraint: with an unchanged snapshot
    /// the objective improvement comes out zero and **the report says "removal ineffective" — about a constraint that may be critical.**
    /// </remarks>
    public bool TryWithoutConstraint(ConstraintId id, out ConstraintProgrammingModelSnapshot derived) {
        if (!Constraints.Any(entry => entry.Id == id)) {
            derived = this;
            return false;
        }
        derived = new ConstraintProgrammingModelSnapshot(
            Name,
            ObjectCategory,
            Variables,
            Intervals,
            Expressions,
            Constraints.Where(entry => entry.Id != id).ToArray(),
            Objectives,
            ConstraintGroups,
            IdentitySchemaVersion,
            IdentityNamespace);
        return true;
    }

    /// <summary>
    /// 派生一份只改了一条约束的快照 / Derive a snapshot with exactly one constraint replaced.
    /// </summary>
    /// <param name="id">要替换的约束 / The constraint to replace.</param>
    /// <param name="constraint">新的约束体 / The new constraint body.</param>
    /// <param name="derived">派生结果 / The derived snapshot.</param>
    /// <returns>该约束是否存在 / Whether the constraint exists.</returns>
    /// <remarks>
    /// **B993 补这个方法，是因为扰动与冲突分析都需要"改一条约束、别的一模一样"的快照**，
    /// 而 C# 的快照是 sealed class + 私有构造器，**没有 Kotlin `copy` 那样的派生手段。**
    ///
    /// **找不到就返回 `false`，不返回原快照**：把"没改"当成"改好了"，会让后续求解解的是一份没被扰动的模型，
    /// **而报告会照常写上一个改善量。**
    ///
    /// **B993 adds this because perturbation and conflict analysis both need "one constraint changed, everything else identical"**, and the C# snapshot is a sealed class
    /// with a private constructor — **there is no `copy`-style derivation the way Kotlin has one.** **A miss returns `false` rather than the original snapshot**: treating "nothing
    /// changed" as "changed" would solve an unperturbed model, **and the report would still print an improvement number.**
    /// </remarks>
    public bool TryWithConstraint(
        ConstraintId id,
        ConstraintProgrammingConstraint constraint,
        out ConstraintProgrammingModelSnapshot derived) {
        ArgumentNullException.ThrowIfNull(constraint);

        if (!Constraints.Any(entry => entry.Id == id)) {
            derived = this;
            return false;
        }
        derived = new ConstraintProgrammingModelSnapshot(
            Name,
            ObjectCategory,
            Variables,
            Intervals,
            Expressions,
            Constraints.Select(entry => entry.Id == id
                ? new ConstraintProgrammingConstraintSnapshot(
                    entry.Id, entry.Name, entry.GroupName, constraint, entry.Scope, entry.Origin, entry.IdentityProvenance)
                : entry).ToArray(),
            Objectives,
            ConstraintGroups,
            IdentitySchemaVersion,
            IdentityNamespace);
        return true;
    }

    private ConstraintProgrammingModelSnapshot(
        string name,
        ObjectCategory objectCategory,
        IReadOnlyList<ConstraintProgrammingVariableSnapshot> variables,
        IReadOnlyList<IntervalVariable> intervals,
        IReadOnlyList<ConstraintProgrammingExpressionSnapshot> expressions,
        IReadOnlyList<ConstraintProgrammingConstraintSnapshot> constraints,
        IReadOnlyList<ConstraintProgrammingObjectiveSnapshot> objectives,
        IReadOnlyList<string> constraintGroups,
        string identitySchemaVersion,
        string identityNamespace) {
        Name = name;
        ObjectCategory = objectCategory;
        Variables = Freeze(variables);
        Intervals = Freeze(intervals);
        Expressions = Freeze(expressions);
        Constraints = Freeze(constraints);
        Objectives = Freeze(objectives);
        ConstraintGroups = Freeze(constraintGroups);
        IdentitySchemaVersion = identitySchemaVersion;
        IdentityNamespace = identityNamespace;
    }

    /// <summary>模型名称 / Model name.</summary>
    public string Name { get; }

    /// <summary>模型优化方向 / Model optimization direction.</summary>
    public ObjectCategory ObjectCategory { get; }

    /// <summary>标量变量 / Scalar variables.</summary>
    public IReadOnlyList<ConstraintProgrammingVariableSnapshot> Variables { get; }

    /// <summary>interval 变量 / Interval variables.</summary>
    public IReadOnlyList<IntervalVariable> Intervals { get; }

    /// <summary>具名表达式 / Named expressions.</summary>
    public IReadOnlyList<ConstraintProgrammingExpressionSnapshot> Expressions { get; }

    /// <summary>约束 / Constraints.</summary>
    public IReadOnlyList<ConstraintProgrammingConstraintSnapshot> Constraints { get; }

    /// <summary>目标 / Objectives.</summary>
    public IReadOnlyList<ConstraintProgrammingObjectiveSnapshot> Objectives { get; }

    /// <summary>约束组名称 / Constraint group names.</summary>
    public IReadOnlyList<string> ConstraintGroups { get; }

    /// <summary>身份 schema 版本 / Identity schema version.</summary>
    public string IdentitySchemaVersion { get; }

    /// <summary>身份命名空间 / Identity namespace.</summary>
    public string IdentityNamespace { get; }

    /// <summary>创建并校验不可变快照 / Create and validate an immutable snapshot.</summary>
    /// <param name="name">模型名称 / Model name.</param>
    /// <param name="objectCategory">模型优化方向 / Model optimization direction.</param>
    /// <param name="variables">标量变量 / Scalar variables.</param>
    /// <param name="intervals">interval 变量 / Interval variables.</param>
    /// <param name="expressions">具名表达式 / Named expressions.</param>
    /// <param name="constraints">约束 / Constraints.</param>
    /// <param name="objectives">目标 / Objectives.</param>
    /// <param name="constraintGroups">约束组名称 / Constraint group names.</param>
    /// <param name="identitySchemaVersion">身份 schema 版本 / Identity schema version.</param>
    /// <param name="identityNamespace">身份命名空间 / Identity namespace.</param>
    /// <returns>有效快照或结构化错误 / Valid snapshot or a structured error.</returns>
    public static Result<
        ConstraintProgrammingModelSnapshot,
        ErrorCode,
        Error<ErrorCode>> Create(
        string name,
        ObjectCategory objectCategory,
        IEnumerable<ConstraintProgrammingVariableSnapshot> variables,
        IEnumerable<IntervalVariable> intervals,
        IEnumerable<ConstraintProgrammingExpressionSnapshot> expressions,
        IEnumerable<ConstraintProgrammingConstraintSnapshot> constraints,
        IEnumerable<ConstraintProgrammingObjectiveSnapshot> objectives,
        IEnumerable<string> constraintGroups,
        string identitySchemaVersion = "1.0",
        string identityNamespace = "model-local") {
        ArgumentNullException.ThrowIfNull(variables);
        ArgumentNullException.ThrowIfNull(intervals);
        ArgumentNullException.ThrowIfNull(expressions);
        ArgumentNullException.ThrowIfNull(constraints);
        ArgumentNullException.ThrowIfNull(objectives);
        ArgumentNullException.ThrowIfNull(constraintGroups);
        if (string.IsNullOrWhiteSpace(name)) {
            return Failure(
                code: ErrorCode.IllegalArgument,
                message: "CP 模型名称不能为空 / CP model name must not be blank");
        }

        var snapshot = new ConstraintProgrammingModelSnapshot(
            name: name,
            objectCategory: objectCategory,
            variables: variables.ToList(),
            intervals: intervals.ToList(),
            expressions: expressions.ToList(),
            constraints: constraints.ToList(),
            objectives: objectives.ToList(),
            constraintGroups: constraintGroups.ToList(),
            identitySchemaVersion: identitySchemaVersion,
            identityNamespace: identityNamespace);
        Try valid = snapshot.Validate();
        return valid.IsOk
            ? Results.Ok(snapshot)
            : Results.Failed<ConstraintProgrammingModelSnapshot>(
                valid is Failed<Success, ErrorCode, Error<ErrorCode>> failed
                    ? failed.Error
                    : new Err<ErrorCode>(
                        code: ErrorCode.IllegalArgument,
                        message: "CP snapshot 校验失败 / CP snapshot validation failed"));
    }

    /// <summary>按 ID 查找标量变量 / Find a scalar variable by ID.</summary>
    /// <param name="id">稳定变量 ID / Stable variable ID.</param>
    /// <returns>匹配变量；不存在时为空 / Matching variable, or null.</returns>
    public ConstraintProgrammingVariableSnapshot? Variable(VariableId id) =>
        Variables.FirstOrDefault(variable => variable.Id == id);

    /// <summary>按 ID 查找 interval / Find an interval by ID.</summary>
    /// <param name="id">稳定 interval ID / Stable interval ID.</param>
    /// <returns>匹配 interval；不存在时为空 / Matching interval, or null.</returns>
    public IntervalVariable? Interval(IntervalId id) =>
        Intervals.FirstOrDefault(interval => interval.Id == id);

    /// <summary>按 ID 查找约束 / Find a constraint by ID.</summary>
    /// <param name="id">稳定约束 ID / Stable constraint ID.</param>
    /// <returns>匹配约束；不存在时为空 / Matching constraint, or null.</returns>
    public ConstraintProgrammingConstraintSnapshot? Constraint(ConstraintId id) =>
        Constraints.FirstOrDefault(constraint => constraint.Id == id);

    /// <summary>按 ID 查找目标 / Find an objective by ID.</summary>
    /// <param name="id">稳定目标 ID / Stable objective ID.</param>
    /// <returns>匹配目标；不存在时为空 / Matching objective, or null.</returns>
    public ConstraintProgrammingObjectiveSnapshot? Objective(ObjectiveId id) =>
        Objectives.FirstOrDefault(objective => objective.Id == id);

    /// <summary>校验身份清单、引用与目标语义 / Validate identities, references, and objective semantics.</summary>
    /// <returns>校验结果 / Validation result.</returns>
    public Try Validate() {
        if (string.IsNullOrWhiteSpace(IdentitySchemaVersion) ||
            string.IsNullOrWhiteSpace(IdentityNamespace)) {
            return Failure();
        }
        if (Objectives.Count > 1) {
            return Failure("CP 模型只支持单一目标 / CP models support at most one objective");
        }
        if (Objectives.Count == 1 && Objectives[0].Category != ObjectCategory) {
            return Failure(
                "CP 目标方向与模型方向不一致 / CP objective category disagrees with the model category");
        }

        var identities = new List<(string Id, ModelElementScope Scope, string? Origin, IReadOnlyList<ModelElementOrigin> Provenance)>();
        identities.AddRange(Variables.Select(variable => (
            variable.Id.Value,
            variable.Scope,
            variable.Origin,
            variable.IdentityProvenance)));
        identities.AddRange(Intervals.Select(interval => (
            interval.Id.Value,
            interval.Scope,
            interval.Origin,
            interval.IdentityProvenance)));
        identities.AddRange(Constraints.Select(constraint => (
            constraint.Id.Value,
            constraint.Scope,
            constraint.Origin,
            constraint.IdentityProvenance)));
        identities.AddRange(Objectives.Select(objective => (
            objective.Id.Value,
            objective.Scope,
            objective.Origin,
            objective.IdentityProvenance)));

        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach ((string id, ModelElementScope scope, string? origin, IReadOnlyList<ModelElementOrigin> provenance) in identities) {
            if (!seenIds.Add(id)) {
                return Failure($"CP 身份 ID 重复：{id} / Duplicate CP identity ID: {id}");
            }
            string? identityError = ConstraintProgrammingIdentity.Validate(
                id: id,
                scope: scope,
                origin: origin,
                provenance: provenance,
                identityNamespace: IdentityNamespace,
                identitySchemaVersion: IdentitySchemaVersion);
            if (identityError is not null) {
                return Failure(identityError);
            }
        }

        var knownVariables = Variables.Select(variable => variable.Id).ToHashSet();
        foreach (ConstraintProgrammingExpressionSnapshot expression in Expressions) {
            Try reference = ValidateReferences(
                owner: $"表达式 {expression.Name}",
                variables: expression.Expression.Variables,
                knownVariables: knownVariables);
            if (reference.IsFailed) {
                return reference;
            }
        }
        foreach (IntervalVariable interval in Intervals) {
            Try reference = ValidateReferences(
                owner: $"interval {interval.Id.Value}",
                variables: interval.Variables,
                knownVariables: knownVariables);
            if (reference.IsFailed) {
                return reference;
            }
        }
        foreach (ConstraintProgrammingConstraintSnapshot constraint in Constraints) {
            Try reference = ValidateReferences(
                owner: $"约束 {constraint.Id.Value}",
                variables: constraint.Constraint.Variables,
                knownVariables: knownVariables);
            if (reference.IsFailed) {
                return reference;
            }
        }
        foreach (ConstraintProgrammingObjectiveSnapshot objective in Objectives) {
            Try reference = ValidateReferences(
                owner: $"目标 {objective.Id.Value}",
                variables: objective.Expression.Variables,
                knownVariables: knownVariables);
            if (reference.IsFailed) {
                return reference;
            }
        }
        return Results.OkInstance;
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
            : Failure(
                $"{owner} 引用了未注册变量：{missing} / " +
                $"{owner} references an unregistered variable: {missing}");
    }

    private static IReadOnlyList<T> Freeze<T>(IEnumerable<T> values) =>
        new ReadOnlyCollection<T>(values.ToList());

    private static Try Failure(
        string message = "CP snapshot 身份清单无效 / CP snapshot identity manifest is invalid") =>
        Results.Failed<Success>(
            new Err<ErrorCode>(
                code: ErrorCode.IllegalArgument,
                message: message));

    private static Result<ConstraintProgrammingModelSnapshot, ErrorCode, Error<ErrorCode>> Failure(
        ErrorCode code,
        string message) =>
        Results.Failed<ConstraintProgrammingModelSnapshot>(
            new Err<ErrorCode>(
                code: code,
                message: message));
}
