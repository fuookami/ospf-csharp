#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using static Fuookami.Ospf.Core.Model.ConstraintProgramming.ConstraintProgrammingSnapshotCodecHelpers;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Model.ConstraintProgramming;

/// <summary>
/// CP snapshot 的带版本规范 JSON 编解码器。
/// Versioned canonical JSON codec for CP snapshots.
/// </summary>
public static class ConstraintProgrammingSnapshotCodec {
    internal const int CurrentSchema = 1;

    internal static readonly JsonSerializerOptions JsonOptions = new() {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = false,
    };

    /// <summary>编码不可变 CP snapshot / Encode an immutable CP snapshot.</summary>
    /// <param name="snapshot">CP snapshot / CP snapshot.</param>
    /// <returns>规范 JSON 或结构化错误 / Canonical JSON or a structured error.</returns>
    public static Result<string, ErrorCode, Error<ErrorCode>> Encode(
        ConstraintProgrammingModelSnapshot snapshot) {
        ArgumentNullException.ThrowIfNull(snapshot);
        string? validation = ValidateSnapshotSemantics(snapshot);
        if (validation is not null) {
            return Failure<string>(ErrorCode.IllegalArgument, validation);
        }
        try {
            return Results.Ok(JsonSerializer.Serialize(
                ToPayload(snapshot).Canonicalized(),
                JsonOptions));
        }
        catch (Exception error) {
            return Failure<string>(
                ErrorCode.Other,
                $"CP snapshot 序列化失败：{error.Message} / CP snapshot serialization failed: {error.Message}");
        }
    }

    /// <summary>
    /// 使用调用方提供的稳定变量绑定解码 snapshot。
    /// Decode a snapshot with caller-provided stable variable bindings.
    /// </summary>
    /// <param name="encoded">规范 snapshot JSON / Canonical snapshot JSON.</param>
    /// <param name="variables">稳定变量 ID 到源变量的绑定 / Stable-ID-to-source-variable bindings.</param>
    /// <returns>解码后的 snapshot 或结构化错误 / Decoded snapshot or a structured error.</returns>
    public static Result<
        ConstraintProgrammingModelSnapshot,
        ErrorCode,
        Error<ErrorCode>> Decode(
        string encoded,
        IReadOnlyDictionary<VariableId, IVariableItem> variables) {
        ArgumentNullException.ThrowIfNull(encoded);
        ArgumentNullException.ThrowIfNull(variables);
        try {
            SnapshotPayload? payload = JsonSerializer.Deserialize<SnapshotPayload>(
                encoded,
                JsonOptions);
            if (payload is null) {
                return Failure<ConstraintProgrammingModelSnapshot>(
                    ErrorCode.IllegalArgument,
                    "CP snapshot JSON 为空 / CP snapshot JSON is empty");
            }
            if (payload.Schema != CurrentSchema) {
                return Failure<ConstraintProgrammingModelSnapshot>(
                    ErrorCode.Other,
                    $"不支持的 CP snapshot schema：{payload.Schema} / " +
                    $"Unsupported CP snapshot schema: {payload.Schema}");
            }
            string? validation = payload.Validate();
            if (validation is not null) {
                return Failure<ConstraintProgrammingModelSnapshot>(
                    ErrorCode.IllegalArgument,
                    validation);
            }
            return payload.Canonicalized().ToSnapshot(variables);
        }
        catch (Exception error) {
            return Failure<ConstraintProgrammingModelSnapshot>(
                ErrorCode.IllegalArgument,
                $"CP snapshot 反序列化失败：{error.Message} / " +
                $"CP snapshot deserialization failed: {error.Message}");
        }
    }

    /// <summary>只校验 snapshot 传输结构 / Validate snapshot transport structure.</summary>
    /// <param name="encoded">snapshot JSON / Snapshot JSON.</param>
    /// <returns>成功或结构化错误 / Success or a structured error.</returns>
    public static Try Validate(string encoded) {
        ArgumentNullException.ThrowIfNull(encoded);
        try {
            SnapshotPayload? payload = JsonSerializer.Deserialize<SnapshotPayload>(
                encoded,
                JsonOptions);
            if (payload is null) {
                return Failure<Success>(
                    ErrorCode.IllegalArgument,
                    "CP snapshot JSON 为空 / CP snapshot JSON is empty");
            }
            if (payload.Schema != CurrentSchema) {
                return Failure<Success>(
                    ErrorCode.Other,
                    $"不支持的 CP snapshot schema：{payload.Schema} / " +
                    $"Unsupported CP snapshot schema: {payload.Schema}");
            }
            string? validation = payload.Validate();
            return validation is null
                ? Results.OkInstance
                : Failure<Success>(ErrorCode.IllegalArgument, validation);
        }
        catch (Exception error) {
            return Failure<Success>(
                ErrorCode.IllegalArgument,
                $"CP snapshot 结构校验失败：{error.Message} / " +
                $"CP snapshot validation failed: {error.Message}");
        }
    }

    /// <summary>
    /// 将兼容 snapshot 迁移为唯一规范文本。
    /// Migrate a compatible snapshot to its unique canonical text.
    /// </summary>
    /// <param name="encoded">snapshot JSON / Snapshot JSON.</param>
    /// <returns>规范 snapshot JSON 或结构化错误 / Canonical snapshot JSON or a structured error.</returns>
    public static Result<string, ErrorCode, Error<ErrorCode>> Canonicalize(string encoded) {
        ArgumentNullException.ThrowIfNull(encoded);
        try {
            SnapshotPayload? payload = JsonSerializer.Deserialize<SnapshotPayload>(
                encoded,
                JsonOptions);
            if (payload is null) {
                return Failure<string>(
                    ErrorCode.IllegalArgument,
                    "CP snapshot JSON 为空 / CP snapshot JSON is empty");
            }
            if (payload.Schema != CurrentSchema) {
                return Failure<string>(
                    ErrorCode.Other,
                    $"不支持的 CP snapshot schema：{payload.Schema} / " +
                    $"Unsupported CP snapshot schema: {payload.Schema}");
            }
            string? validation = payload.Validate();
            return validation is null
                ? Results.Ok(JsonSerializer.Serialize(payload.Canonicalized(), JsonOptions))
                : Failure<string>(ErrorCode.IllegalArgument, validation);
        }
        catch (Exception error) {
            return Failure<string>(
                ErrorCode.IllegalArgument,
                $"CP snapshot 规范化失败：{error.Message} / " +
                $"CP snapshot canonicalization failed: {error.Message}");
        }
    }

    private static string? ValidateSnapshotSemantics(ConstraintProgrammingModelSnapshot snapshot) {
        if (string.IsNullOrWhiteSpace(snapshot.IdentitySchemaVersion) ||
            string.IsNullOrWhiteSpace(snapshot.IdentityNamespace)) {
            return "CP snapshot identity schema/namespace 不能为空 / " +
                "CP snapshot identity schema and namespace must not be blank";
        }
        if (snapshot.Objectives.Count > 1) {
            return "CP snapshot 只支持单一目标 / CP snapshot supports at most one objective";
        }
        if (snapshot.Objectives.Count == 1 &&
            snapshot.Objectives[0].Category != snapshot.ObjectCategory) {
            return "CP snapshot 目标方向与模型方向不一致 / " +
                "CP snapshot objective category disagrees with model category";
        }
        return ToPayload(snapshot).ValidateIdentity();
    }

    private static SnapshotPayload ToPayload(ConstraintProgrammingModelSnapshot snapshot) =>
        new(
            Schema: CurrentSchema,
            Name: snapshot.Name,
            ObjectCategory: snapshot.ObjectCategory.ToString(),
            Variables: snapshot.Variables
                .OrderBy(variable => variable.Id.Value, StringComparer.Ordinal)
                .Select(variable => new VariablePayload(
                    Id: variable.Id.Value,
                    Name: variable.Name,
                    TypeName: variable.TypeName,
                    Domain: variable.Domain.ToPayload(),
                    Scope: CanonicalScope(variable.Scope),
                    Origin: variable.Origin,
                    IdentityProvenance: variable.IdentityProvenance.ToPayload(variable.Origin)))
                .ToList(),
            Intervals: snapshot.Intervals
                .OrderBy(interval => interval.Id.Value, StringComparer.Ordinal)
                .Select(interval => interval.ToPayload())
                .ToList(),
            Expressions: snapshot.Expressions
                .OrderBy(expression => expression.Name, StringComparer.Ordinal)
                .Select(expression => new NamedExpressionPayload(
                    Name: expression.Name,
                    Expression: expression.Expression.ToPayload()))
                .ToList(),
            Constraints: snapshot.Constraints
                .OrderBy(constraint => constraint.Id.Value, StringComparer.Ordinal)
                .Select(constraint => new ConstraintEntryPayload(
                    Id: constraint.Id.Value,
                    Name: constraint.Name,
                    GroupName: constraint.GroupName,
                    Constraint: constraint.Constraint.ToPayload(),
                    Scope: CanonicalScope(constraint.Scope),
                    Origin: constraint.Origin,
                    IdentityProvenance: constraint.IdentityProvenance.ToPayload(constraint.Origin)))
                .ToList(),
            Objectives: snapshot.Objectives
                .OrderBy(objective => objective.Id.Value, StringComparer.Ordinal)
                .Select(objective => new ObjectivePayload(
                    Id: objective.Id.Value,
                    Category: objective.Category.ToString(),
                    Name: objective.Name,
                    Expression: objective.Expression.ToPayload(),
                    Scope: CanonicalScope(objective.Scope),
                    Origin: objective.Origin,
                    IdentityProvenance: objective.IdentityProvenance.ToPayload(objective.Origin)))
                .ToList(),
            ConstraintGroups: snapshot.ConstraintGroups
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList(),
            IdentitySchemaVersion: snapshot.IdentitySchemaVersion,
            IdentityNamespace: snapshot.IdentityNamespace);

    private static string CanonicalScope(ModelElementScope scope) =>
        scope == ModelElementScope.Stable ? "stable" : "model-local";

    private static string? CanonicalScope(string? scope) => scope switch {
        "stable" or "Stable" => "stable",
        "model-local" or "modelLocal" or "ModelLocal" => "model-local",
        _ => scope,
    };

    private static Result<T, ErrorCode, Error<ErrorCode>> Failure<T>(
        ErrorCode code,
        string message) =>
        Results.Failed<T>(new Err<ErrorCode>(code, message));
}

internal sealed record SnapshotPayload(
    [property: JsonPropertyName("schema"), JsonPropertyOrder(0)] int Schema,
    [property: JsonPropertyName("name"), JsonPropertyOrder(1)] string Name,
    [property: JsonPropertyName("objectCategory"), JsonPropertyOrder(2)] string ObjectCategory,
    [property: JsonPropertyName("variables"), JsonPropertyOrder(3)] List<VariablePayload> Variables,
    [property: JsonPropertyName("intervals"), JsonPropertyOrder(4)] List<IntervalPayload> Intervals,
    [property: JsonPropertyName("expressions"), JsonPropertyOrder(5)] List<NamedExpressionPayload> Expressions,
    [property: JsonPropertyName("constraints"), JsonPropertyOrder(6)] List<ConstraintEntryPayload> Constraints,
    [property: JsonPropertyName("objectives"), JsonPropertyOrder(7)] List<ObjectivePayload> Objectives,
    [property: JsonPropertyName("constraintGroups"), JsonPropertyOrder(8)] List<string> ConstraintGroups,
    [property: JsonPropertyName("identitySchemaVersion"), JsonPropertyOrder(9)] string IdentitySchemaVersion = "1.0",
    [property: JsonPropertyName("identityNamespace"), JsonPropertyOrder(10)] string IdentityNamespace = "model-local") {
    internal string? Validate() {
        if (string.IsNullOrWhiteSpace(IdentitySchemaVersion) ||
            string.IsNullOrWhiteSpace(IdentityNamespace)) {
            return "CP snapshot identity schema/namespace 不能为空 / " +
                "CP snapshot identity schema and namespace must not be blank";
        }
        if (!Enum.TryParse(ObjectCategory, ignoreCase: false, out ObjectCategory category)) {
            return "CP snapshot 目标方向无效 / CP snapshot objective category is invalid";
        }
        if (Objectives.Count > 1) {
            return "CP snapshot 只支持单一目标 / CP snapshot supports at most one objective";
        }
        if (Objectives.Count == 1 &&
            !string.Equals(
                Objectives[0].Category,
                category.ToString(),
                StringComparison.Ordinal)) {
            return "CP snapshot 目标方向与模型方向不一致 / " +
                "CP snapshot objective category disagrees with model category";
        }
        return ValidateIdentity();
    }

    internal string? ValidateIdentity() {
        if (string.IsNullOrWhiteSpace(IdentitySchemaVersion) ||
            string.IsNullOrWhiteSpace(IdentityNamespace)) {
            return "CP snapshot identity schema/namespace 不能为空 / " +
                "CP snapshot identity schema and namespace must not be blank";
        }
        var identities = new List<(
            string Id,
            string Scope,
            string? Origin,
            List<IdentityOriginPayload> Provenance)>();
        identities.AddRange(Variables.Select(variable => (
            variable.Id,
            variable.Scope,
            variable.Origin,
            variable.Provenance)));
        identities.AddRange(Intervals.Select(interval => (
            interval.Id,
            interval.Scope,
            interval.Origin,
            interval.Provenance)));
        identities.AddRange(Constraints.Select(constraint => (
            constraint.Id,
            constraint.Scope,
            constraint.Origin,
            constraint.Provenance)));
        identities.AddRange(Objectives.Select(objective => (
            objective.Id,
            objective.Scope,
            objective.Origin,
            objective.Provenance)));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach ((string id, string scope, string? origin, List<IdentityOriginPayload> provenance) in identities) {
            if (string.IsNullOrWhiteSpace(id)) {
                return "CP snapshot identity 包含空 ID / CP snapshot identity contains a blank ID";
            }
            if (!seen.Add(id)) {
                return "CP snapshot 存在重复稳定 ID / CP snapshot contains duplicate element IDs";
            }
            ModelElementScope? parsedScope = CanonicalScope(scope) switch {
                "stable" => ModelElementScope.Stable,
                "model-local" => ModelElementScope.ModelLocal,
                _ => null,
            };
            if (parsedScope is null) {
                return "CP snapshot identity scope 无效 / CP snapshot identity scope is invalid";
            }
            IReadOnlyList<ModelElementOrigin> canonical =
                ConstraintProgrammingIdentity.CanonicalProvenance(
                    origin: origin,
                    provenance: provenance.ToModelOrigins());
            string? validation = ConstraintProgrammingIdentity.Validate(
                id: id,
                scope: parsedScope.Value,
                origin: origin,
                provenance: canonical,
                identityNamespace: IdentityNamespace,
                identitySchemaVersion: IdentitySchemaVersion);
            if (validation is not null) {
                return validation;
            }
        }
        return null;
    }

    internal SnapshotPayload Canonicalized() => this with {
        Variables = Variables
            .Select(variable => variable.Canonicalized())
            .OrderBy(variable => variable.Id, StringComparer.Ordinal)
            .ToList(),
        Intervals = Intervals
            .Select(interval => interval.Canonicalized())
            .OrderBy(interval => interval.Id, StringComparer.Ordinal)
            .ToList(),
        Expressions = Expressions
            .Select(expression => expression with {
                Expression = expression.Expression.Canonicalized(),
            })
            .OrderBy(expression => expression.Name, StringComparer.Ordinal)
            .ToList(),
        Constraints = Constraints
            .Select(constraint => constraint.Canonicalized())
            .OrderBy(constraint => constraint.Id, StringComparer.Ordinal)
            .ToList(),
        Objectives = Objectives
            .Select(objective => objective.Canonicalized())
            .OrderBy(objective => objective.Id, StringComparer.Ordinal)
            .ToList(),
        ConstraintGroups = ConstraintGroups
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList(),
    };

    internal Result<
        ConstraintProgrammingModelSnapshot,
        ErrorCode,
        Error<ErrorCode>> ToSnapshot(
        IReadOnlyDictionary<VariableId, IVariableItem> variables) {
        var variableSnapshots = new List<ConstraintProgrammingVariableSnapshot>();
        foreach (VariablePayload variable in Variables) {
            Result<IntegerDomain, ErrorCode, Error<ErrorCode>> domain =
                variable.Domain.ToDomain();
            if (domain.IsFailed) {
                return domain.Map<ConstraintProgrammingModelSnapshot>(_ => default!);
            }
            variableSnapshots.Add(new ConstraintProgrammingVariableSnapshot(
                id: new VariableId(variable.Id),
                name: variable.Name,
                typeName: variable.TypeName,
                domain: domain.Value,
                scope: ParseScope(variable.Scope),
                origin: variable.Origin,
                identityProvenance: variable.Provenance.ToModelOrigins()));
        }

        var intervals = new List<IntervalVariable>();
        foreach (IntervalPayload interval in Intervals) {
            Result<IntervalVariable, ErrorCode, Error<ErrorCode>> decoded =
                interval.ToInterval(variables);
            if (decoded.IsFailed) {
                return decoded.Map<ConstraintProgrammingModelSnapshot>(_ => default!);
            }
            intervals.Add(decoded.Value);
        }

        var expressions = new List<ConstraintProgrammingExpressionSnapshot>();
        foreach (NamedExpressionPayload expression in Expressions) {
            Result<
                ConstraintProgrammingExpression,
                ErrorCode,
                Error<ErrorCode>> decoded =
                expression.Expression.ToExpression(variables);
            if (decoded.IsFailed) {
                return decoded.Map<ConstraintProgrammingModelSnapshot>(_ => default!);
            }
            expressions.Add(new ConstraintProgrammingExpressionSnapshot(
                name: expression.Name,
                expression: decoded.Value));
        }

        var constraints = new List<ConstraintProgrammingConstraintSnapshot>();
        foreach (ConstraintEntryPayload constraint in Constraints) {
            Result<
                ConstraintProgrammingConstraint,
                ErrorCode,
                Error<ErrorCode>> decoded =
                constraint.Constraint.ToConstraint(variables);
            if (decoded.IsFailed) {
                return decoded.Map<ConstraintProgrammingModelSnapshot>(_ => default!);
            }
            constraints.Add(new ConstraintProgrammingConstraintSnapshot(
                id: new ConstraintId(constraint.Id),
                name: constraint.Name,
                groupName: constraint.GroupName,
                constraint: decoded.Value,
                scope: ParseScope(constraint.Scope),
                origin: constraint.Origin,
                identityProvenance: constraint.Provenance.ToModelOrigins()));
        }

        var objectives = new List<ConstraintProgrammingObjectiveSnapshot>();
        foreach (ObjectivePayload objective in Objectives) {
            if (!Enum.TryParse(objective.Category, ignoreCase: false, out ObjectCategory category)) {
                return Failure<ConstraintProgrammingModelSnapshot>(
                    ErrorCode.IllegalArgument,
                    "未知目标类别 / Unknown objective category");
            }
            Result<
                ConstraintProgrammingExpression,
                ErrorCode,
                Error<ErrorCode>> expression =
                objective.Expression.ToExpression(variables);
            if (expression.IsFailed) {
                return expression.Map<ConstraintProgrammingModelSnapshot>(_ => default!);
            }
            objectives.Add(new ConstraintProgrammingObjectiveSnapshot(
                id: new ObjectiveId(objective.Id),
                category: category,
                name: objective.Name,
                expression: expression.Value,
                scope: ParseScope(objective.Scope),
                origin: objective.Origin,
                identityProvenance: objective.Provenance.ToModelOrigins()));
        }

        if (!Enum.TryParse(ObjectCategory, ignoreCase: false, out ObjectCategory modelCategory)) {
            return Failure<ConstraintProgrammingModelSnapshot>(
                ErrorCode.IllegalArgument,
                "未知模型目标类别 / Unknown model objective category");
        }
        return ConstraintProgrammingModelSnapshot.Create(
            name: Name,
            objectCategory: modelCategory,
            variables: variableSnapshots,
            intervals: intervals,
            expressions: expressions,
            constraints: constraints,
            objectives: objectives,
            constraintGroups: ConstraintGroups,
            identitySchemaVersion: IdentitySchemaVersion,
            identityNamespace: IdentityNamespace);
    }

    private static ModelElementScope ParseScope(string scope) =>
        CanonicalScope(scope) == "stable"
            ? ModelElementScope.Stable
            : ModelElementScope.ModelLocal;
}

internal sealed record VariablePayload(
    [property: JsonPropertyName("id"), JsonPropertyOrder(0)] string Id,
    [property: JsonPropertyName("name"), JsonPropertyOrder(1)] string Name,
    [property: JsonPropertyName("typeName"), JsonPropertyOrder(2)] string TypeName,
    [property: JsonPropertyName("domain"), JsonPropertyOrder(3)] DomainPayload Domain,
    [property: JsonPropertyName("scope"), JsonPropertyOrder(4)] string Scope = "model-local",
    [property: JsonPropertyName("origin"), JsonPropertyOrder(5)] string? Origin = null,
    [property: JsonPropertyName("identityProvenance"), JsonPropertyOrder(6)] List<IdentityOriginPayload>? IdentityProvenance = null) {
    [JsonIgnore]
    internal List<IdentityOriginPayload> Provenance => IdentityProvenance ?? new List<IdentityOriginPayload>();

    internal VariablePayload Canonicalized() => this with {
        Domain = Domain.Canonicalized(),
        Scope = CanonicalScope(Scope),
        IdentityProvenance = Provenance.Canonicalized(Origin),
    };
}

internal sealed record IdentityOriginPayload(
    [property: JsonPropertyName("kind"), JsonPropertyOrder(0)] string Kind,
    [property: JsonPropertyName("key"), JsonPropertyOrder(1)] string Key);

internal sealed record DomainPayload(
    [property: JsonPropertyName("kind"), JsonPropertyOrder(0)] string Kind,
    [property: JsonPropertyName("lower"), JsonPropertyOrder(1)] long? Lower = null,
    [property: JsonPropertyName("upper"), JsonPropertyOrder(2)] long? Upper = null,
    [property: JsonPropertyName("values"), JsonPropertyOrder(3)] List<long>? Values = null) {
    [JsonIgnore]
    internal List<long> SparseValues => Values ?? new List<long>();

    internal DomainPayload Canonicalized() => Kind == "values"
        ? this with { Values = SparseValues.OrderBy(value => value).ToList() }
        : this;

    internal Result<IntegerDomain, ErrorCode, Error<ErrorCode>> ToDomain() {
        return Kind switch {
            "interval" when Lower is { } lower && Upper is { } upper =>
                IntegerDomain.CreateInterval(new Int64(lower), new Int64(upper))
                    .Map(domain => (IntegerDomain)domain),
            "values" => IntegerDomain
                .CreateValues(SparseValues.Select(value => new Int64(value)))
                .Map(domain => (IntegerDomain)domain),
            "interval" => Failure<IntegerDomain>(
                ErrorCode.IllegalArgument,
                "CP interval domain 缺少边界 / CP interval domain is missing bounds"),
            _ => Failure<IntegerDomain>(
                ErrorCode.IllegalArgument,
                $"未知 CP domain 类型：{Kind} / Unknown CP domain kind: {Kind}"),
        };
    }
}

internal sealed record NamedExpressionPayload(
    [property: JsonPropertyName("name"), JsonPropertyOrder(0)] string Name,
    [property: JsonPropertyName("expression"), JsonPropertyOrder(1)] ExpressionPayload Expression);

internal sealed record ExpressionPayload(
    [property: JsonPropertyName("kind"), JsonPropertyOrder(0)] string Kind,
    [property: JsonPropertyName("value"), JsonPropertyOrder(1)] long? Value = null,
    [property: JsonPropertyName("message"), JsonPropertyOrder(2)] string? Message = null,
    [property: JsonPropertyName("variableId"), JsonPropertyOrder(3)] string? VariableId = null,
    [property: JsonPropertyName("domain"), JsonPropertyOrder(4)] DomainPayload? Domain = null,
    [property: JsonPropertyName("terms"), JsonPropertyOrder(5)] List<TermPayload>? Terms = null,
    [property: JsonPropertyName("constant"), JsonPropertyOrder(6)] long Constant = 0L) {
    [JsonIgnore]
    internal List<TermPayload> LinearTerms => Terms ?? new List<TermPayload>();

    internal ExpressionPayload Canonicalized() => this with {
        Domain = Domain?.Canonicalized(),
        Terms = LinearTerms
            .OrderBy(term => term.VariableId, StringComparer.Ordinal)
            .ThenBy(term => term.Coefficient)
            .ToList(),
    };

    internal Result<
        ConstraintProgrammingExpression,
        ErrorCode,
        Error<ErrorCode>> ToExpression(
        IReadOnlyDictionary<VariableId, IVariableItem> variables) {
        switch (Kind) {
            case "constant":
                return Value is { } value
                    ? Results.Ok<ConstraintProgrammingExpression>(
                        ConstraintProgrammingExpression.CreateConstant(value))
                    : Failure<ConstraintProgrammingExpression>(
                        ErrorCode.IllegalArgument,
                        "CP constant 缺少值 / CP constant is missing a value");
            case "invalid":
                return Results.Ok<ConstraintProgrammingExpression>(
                    new ConstraintProgrammingExpression.Invalid(Message ?? string.Empty));
            case "variable": {
                if (VariableId is null) {
                    return Failure<ConstraintProgrammingExpression>(
                        ErrorCode.IllegalArgument,
                        "CP variable expression 缺少 ID / CP variable expression is missing an ID");
                }
                var id = new VariableId(VariableId);
                if (!variables.TryGetValue(id, out IVariableItem? variable)) {
                    return Failure<ConstraintProgrammingExpression>(
                        ErrorCode.DataNotFound,
                        $"缺少 CP 变量绑定：{id.Value} / Missing CP variable binding: {id.Value}");
                }
                Result<IntegerDomain, ErrorCode, Error<ErrorCode>> domain =
                    Domain is null
                        ? Results.Ok(ConstraintProgrammingExpression.CreateVariable(variable).Value.Domain)
                        : Domain.ToDomain();
                if (domain.IsFailed) {
                    return domain.Map<ConstraintProgrammingExpression>(_ => default!);
                }
                Result<
                    ConstraintProgrammingExpression.Variable,
                    ErrorCode,
                    Error<ErrorCode>> validated =
                    ConstraintProgrammingExpression.CreateVariable(variable, domain.Value);
                return validated.IsOk
                    ? Results.Ok<ConstraintProgrammingExpression>(new ConstraintProgrammingExpression.Variable(
                        variable: variable,
                        domain: validated.Value.Domain,
                        id: id))
                    : validated.Map<ConstraintProgrammingExpression>(_ => default!);
            }
            case "linear": {
                var terms = new List<ConstraintProgrammingExpression.Term>();
                foreach (TermPayload term in LinearTerms) {
                    var id = new VariableId(term.VariableId);
                    if (!variables.TryGetValue(id, out IVariableItem? variable)) {
                        return Failure<ConstraintProgrammingExpression>(
                            ErrorCode.DataNotFound,
                            $"缺少 CP 变量绑定：{id.Value} / Missing CP variable binding: {id.Value}");
                    }
                    if (!variable.TypeKind.IsIntegerType) {
                        return Failure<ConstraintProgrammingExpression>(
                            ErrorCode.IllegalArgument,
                            $"CP 线性项只支持整数变量：{variable.Name} / " +
                            $"CP linear terms only support integer variables: {variable.Name}");
                    }
                    terms.Add(new ConstraintProgrammingExpression.Term(
                        variable: variable,
                        coefficient: new Int64(term.Coefficient),
                        id: id));
                }
                return Results.Ok<ConstraintProgrammingExpression>(
                    new ConstraintProgrammingExpression.Linear(
                        terms: terms,
                        constant: new Int64(Constant)));
            }
            default:
                return Failure<ConstraintProgrammingExpression>(
                    ErrorCode.IllegalArgument,
                    $"未知 CP expression 类型：{Kind} / Unknown CP expression kind: {Kind}");
        }
    }
}

internal sealed record TermPayload(
    [property: JsonPropertyName("variableId"), JsonPropertyOrder(0)] string VariableId,
    [property: JsonPropertyName("coefficient"), JsonPropertyOrder(1)] long Coefficient);

internal sealed record LiteralPayload(
    [property: JsonPropertyName("variableId"), JsonPropertyOrder(0)] string? VariableId = null,
    [property: JsonPropertyName("negated"), JsonPropertyOrder(1)] bool Negated = false,
    [property: JsonPropertyName("constant"), JsonPropertyOrder(2)] bool? Constant = null) {
    internal Result<BooleanLiteral, ErrorCode, Error<ErrorCode>> ToLiteral(
        IReadOnlyDictionary<VariableId, IVariableItem> variables) {
        if (Constant is { } constant) {
            return Results.Ok<BooleanLiteral>(new BooleanLiteral.ConstantLiteral(constant));
        }
        if (VariableId is null) {
            return Failure<BooleanLiteral>(
                ErrorCode.IllegalArgument,
                "CP literal 缺少变量 ID / CP literal is missing a variable ID");
        }
        var id = new VariableId(VariableId);
        if (!variables.TryGetValue(id, out IVariableItem? variable)) {
            return Failure<BooleanLiteral>(
                ErrorCode.DataNotFound,
                $"缺少 CP 布尔变量绑定：{id.Value} / Missing CP Boolean variable binding: {id.Value}");
        }
        return variable is BinVar binary
            ? Results.Ok<BooleanLiteral>(new BooleanLiteral.VariableLiteral(
                variable: binary,
                negated: Negated,
                id: id))
            : Failure<BooleanLiteral>(
                ErrorCode.IllegalArgument,
                "CP literal 绑定变量不是二值变量 / CP literal binding is not binary");
    }
}

internal sealed record IntervalPayload(
    [property: JsonPropertyName("id"), JsonPropertyOrder(0)] string Id,
    [property: JsonPropertyName("start"), JsonPropertyOrder(1)] ExpressionPayload Start,
    [property: JsonPropertyName("size"), JsonPropertyOrder(2)] ExpressionPayload Size,
    [property: JsonPropertyName("end"), JsonPropertyOrder(3)] ExpressionPayload End,
    [property: JsonPropertyName("presence"), JsonPropertyOrder(4)] LiteralPayload? Presence = null,
    [property: JsonPropertyName("scope"), JsonPropertyOrder(5)] string Scope = "model-local",
    [property: JsonPropertyName("origin"), JsonPropertyOrder(6)] string? Origin = null,
    [property: JsonPropertyName("identityProvenance"), JsonPropertyOrder(7)] List<IdentityOriginPayload>? IdentityProvenance = null) {
    [JsonIgnore]
    internal List<IdentityOriginPayload> Provenance => IdentityProvenance ?? new List<IdentityOriginPayload>();

    internal IntervalPayload Canonicalized() => this with {
        Start = Start.Canonicalized(),
        Size = Size.Canonicalized(),
        End = End.Canonicalized(),
        Presence = Presence,
        Scope = CanonicalScope(Scope),
        IdentityProvenance = Provenance.Canonicalized(Origin),
    };

    internal Result<IntervalVariable, ErrorCode, Error<ErrorCode>> ToInterval(
        IReadOnlyDictionary<VariableId, IVariableItem> variables) {
        Result<
            ConstraintProgrammingExpression,
            ErrorCode,
            Error<ErrorCode>> start = Start.ToExpression(variables);
        if (start.IsFailed) {
            return start.Map<IntervalVariable>(_ => default!);
        }
        Result<
            ConstraintProgrammingExpression,
            ErrorCode,
            Error<ErrorCode>> size = Size.ToExpression(variables);
        if (size.IsFailed) {
            return size.Map<IntervalVariable>(_ => default!);
        }
        Result<
            ConstraintProgrammingExpression,
            ErrorCode,
            Error<ErrorCode>> end = End.ToExpression(variables);
        if (end.IsFailed) {
            return end.Map<IntervalVariable>(_ => default!);
        }
        BooleanLiteral? presence = null;
        if (Presence is not null) {
            Result<BooleanLiteral, ErrorCode, Error<ErrorCode>> decoded =
                Presence.ToLiteral(variables);
            if (decoded.IsFailed) {
                return decoded.Map<IntervalVariable>(_ => default!);
            }
            presence = decoded.Value;
        }
        return IntervalVariable.Create(
            id: Id,
            start: start.Value,
            size: size.Value,
            end: end.Value,
            presence: presence,
            scope: ParseScope(Scope),
            origin: Origin,
            identityProvenance: Provenance.ToModelOrigins());
    }
}

internal sealed record ConstraintEntryPayload(
    [property: JsonPropertyName("id"), JsonPropertyOrder(0)] string Id,
    [property: JsonPropertyName("name"), JsonPropertyOrder(1)] string Name,
    [property: JsonPropertyName("groupName"), JsonPropertyOrder(2)] string? GroupName,
    [property: JsonPropertyName("constraint"), JsonPropertyOrder(3)] ConstraintPayload Constraint,
    [property: JsonPropertyName("scope"), JsonPropertyOrder(4)] string Scope = "model-local",
    [property: JsonPropertyName("origin"), JsonPropertyOrder(5)] string? Origin = null,
    [property: JsonPropertyName("identityProvenance"), JsonPropertyOrder(6)] List<IdentityOriginPayload>? IdentityProvenance = null) {
    [JsonIgnore]
    internal List<IdentityOriginPayload> Provenance => IdentityProvenance ?? new List<IdentityOriginPayload>();

    internal ConstraintEntryPayload Canonicalized() => this with {
        Constraint = Constraint.Canonicalized(),
        Scope = CanonicalScope(Scope),
        IdentityProvenance = Provenance.Canonicalized(Origin),
    };
}

internal sealed record ConstraintPayload(
    [property: JsonPropertyName("kind"), JsonPropertyOrder(0)] string Kind,
    [property: JsonPropertyName("expression"), JsonPropertyOrder(1)] ExpressionPayload? Expression = null,
    [property: JsonPropertyName("comparison"), JsonPropertyOrder(2)] string? Comparison = null,
    [property: JsonPropertyName("rhs"), JsonPropertyOrder(3)] long? Rhs = null,
    [property: JsonPropertyName("literals"), JsonPropertyOrder(4)] List<LiteralPayload>? Literals = null,
    [property: JsonPropertyName("enforcement"), JsonPropertyOrder(5)] LiteralPayload? Enforcement = null,
    [property: JsonPropertyName("child"), JsonPropertyOrder(6)] ConstraintPayload? Child = null,
    [property: JsonPropertyName("reifiedDirection"), JsonPropertyOrder(7)] string? ReifiedDirection = null,
    [property: JsonPropertyName("reifiedLiteral"), JsonPropertyOrder(8)] LiteralPayload? ReifiedLiteral = null,
    [property: JsonPropertyName("expressions"), JsonPropertyOrder(9)] List<ExpressionPayload>? Expressions = null,
    [property: JsonPropertyName("index"), JsonPropertyOrder(10)] ExpressionPayload? Index = null,
    [property: JsonPropertyName("values"), JsonPropertyOrder(11)] List<ValuePayload>? Values = null,
    [property: JsonPropertyName("target"), JsonPropertyOrder(12)] ExpressionPayload? Target = null,
    [property: JsonPropertyName("tuples"), JsonPropertyOrder(13)] List<List<long>>? Tuples = null,
    [property: JsonPropertyName("intervals"), JsonPropertyOrder(14)] List<IntervalPayload>? Intervals = null,
    [property: JsonPropertyName("demands"), JsonPropertyOrder(15)] List<ExpressionPayload>? Demands = null,
    [property: JsonPropertyName("capacity"), JsonPropertyOrder(16)] ExpressionPayload? Capacity = null,
    [property: JsonPropertyName("successors"), JsonPropertyOrder(17)] List<ExpressionPayload>? Successors = null,
    [property: JsonPropertyName("initialState"), JsonPropertyOrder(18)] int? InitialState = null,
    [property: JsonPropertyName("finalStates"), JsonPropertyOrder(19)] List<int>? FinalStates = null,
    [property: JsonPropertyName("transitions"), JsonPropertyOrder(20)] List<TransitionPayload>? Transitions = null,
    [property: JsonPropertyName("events"), JsonPropertyOrder(21)] List<EventPayload>? Events = null,
    [property: JsonPropertyName("initialLevel"), JsonPropertyOrder(22)] long? InitialLevel = null,
    [property: JsonPropertyName("minimumLevel"), JsonPropertyOrder(23)] long? MinimumLevel = null,
    [property: JsonPropertyName("maximumLevel"), JsonPropertyOrder(24)] long? MaximumLevel = null) {
    [JsonIgnore] internal List<LiteralPayload> ChildLiterals => Literals ?? new List<LiteralPayload>();
    [JsonIgnore] internal List<ExpressionPayload> ChildExpressions => Expressions ?? new List<ExpressionPayload>();
    [JsonIgnore] internal List<ValuePayload> ElementValues => Values ?? new List<ValuePayload>();
    [JsonIgnore] internal List<List<long>> AssignmentTuples => Tuples ?? new List<List<long>>();
    [JsonIgnore] internal List<IntervalPayload> ChildIntervals => Intervals ?? new List<IntervalPayload>();
    [JsonIgnore] internal List<ExpressionPayload> ChildDemands => Demands ?? new List<ExpressionPayload>();
    [JsonIgnore] internal List<ExpressionPayload> ChildSuccessors => Successors ?? new List<ExpressionPayload>();
    [JsonIgnore] internal List<int> AcceptingStates => FinalStates ?? new List<int>();
    [JsonIgnore] internal List<TransitionPayload> AutomatonTransitions => Transitions ?? new List<TransitionPayload>();
    [JsonIgnore] internal List<EventPayload> ReservoirEvents => Events ?? new List<EventPayload>();

    internal ConstraintPayload Canonicalized() {
        var expressions = ChildExpressions
            .Select(expression => expression.Canonicalized())
            .ToList();
        var intervals = ChildIntervals
            .Select(interval => interval.Canonicalized())
            .ToList();
        var demands = ChildDemands
            .Select(demand => demand.Canonicalized())
            .ToList();
        List<(IntervalPayload Interval, ExpressionPayload Demand)> intervalDemandPairs =
            intervals.Count == demands.Count
                ? intervals
                    .Zip(demands)
                    .OrderBy(pair => pair.First.CanonicalKey(), StringComparer.Ordinal)
                    .ThenBy(pair => pair.Second.CanonicalKey(), StringComparer.Ordinal)
                    .ToList()
                : new List<(IntervalPayload Interval, ExpressionPayload Demand)>();
        List<LiteralPayload> literals = Kind is "bool-and" or "bool-or" or "bool-xor"
            ? ChildLiterals
                .OrderBy(literal => literal.VariableId ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(literal => literal.Constant ?? false)
                .ThenBy(literal => literal.Negated)
                .ToList()
            : ChildLiterals;
        List<IntervalPayload> orderedIntervals = Kind switch {
            "no-overlap" => intervals
                .OrderBy(interval => interval.CanonicalKey(), StringComparer.Ordinal)
                .ToList(),
            "cumulative" when intervalDemandPairs.Count > 0 =>
                intervalDemandPairs.Select(pair => pair.Interval).ToList(),
            _ => intervals,
        };
        List<ExpressionPayload> orderedDemands =
            Kind == "cumulative" && intervalDemandPairs.Count > 0
                ? intervalDemandPairs.Select(pair => pair.Demand).ToList()
                : demands;
        return this with {
            Expression = Expression?.Canonicalized(),
            Literals = literals,
            Child = Child?.Canonicalized(),
            ReifiedLiteral = ReifiedLiteral,
            Expressions = Kind == "all-different"
                ? expressions
                    .OrderBy(expression => expression.CanonicalKey(), StringComparer.Ordinal)
                    .ToList()
                : expressions,
            Index = Index?.Canonicalized(),
            Values = ElementValues
                .Select(value => value.Canonicalized())
                .ToList(),
            Target = Target?.Canonicalized(),
            Tuples = AssignmentTuples
                .OrderBy(tuple => tuple, Comparer<List<long>>.Create(CompareIntLists))
                .ToList(),
            Intervals = orderedIntervals,
            Demands = orderedDemands,
            Capacity = Capacity?.Canonicalized(),
            Successors = ChildSuccessors
                .Select(successor => successor.Canonicalized())
                .ToList(),
            FinalStates = AcceptingStates
                .OrderBy(state => state)
                .ToList(),
            Transitions = AutomatonTransitions
                .OrderBy(transition => transition.FromState)
                .ThenBy(transition => transition.Value)
                .ThenBy(transition => transition.ToState)
                .ToList(),
            Events = Kind == "reservoir"
                ? ReservoirEvents
                    .Select(evt => evt.Canonicalized())
                    .OrderBy(evt => evt.CanonicalKey(), StringComparer.Ordinal)
                    .ToList()
                : ReservoirEvents,
        };
    }

    internal Result<
        ConstraintProgrammingConstraint,
        ErrorCode,
        Error<ErrorCode>> ToConstraint(
        IReadOnlyDictionary<VariableId, IVariableItem> variables) {
        Result<
            ConstraintProgrammingExpression,
            ErrorCode,
            Error<ErrorCode>> RequiredExpression(ExpressionPayload? payload) =>
            payload is null
                ? Failure<ConstraintProgrammingExpression>(
                    ErrorCode.IllegalArgument,
                    "CP constraint 缺少表达式 / CP constraint is missing an expression")
                : payload.ToExpression(variables);
        Result<List<BooleanLiteral>, ErrorCode, Error<ErrorCode>> DecodeLiterals() {
            var decoded = new List<BooleanLiteral>();
            foreach (LiteralPayload literal in ChildLiterals) {
                Result<BooleanLiteral, ErrorCode, Error<ErrorCode>> value =
                    literal.ToLiteral(variables);
                if (value.IsFailed) {
                    return value.Map<List<BooleanLiteral>>(_ => default!);
                }
                decoded.Add(value.Value);
            }
            return Results.Ok(decoded);
        }
        Result<List<ConstraintProgrammingExpression>, ErrorCode, Error<ErrorCode>>
            DecodeExpressions() {
                var decoded = new List<ConstraintProgrammingExpression>();
                foreach (ExpressionPayload expression in ChildExpressions) {
                    Result<
                        ConstraintProgrammingExpression,
                        ErrorCode,
                        Error<ErrorCode>> value =
                        expression.ToExpression(variables);
                    if (value.IsFailed) {
                        return value.Map<List<ConstraintProgrammingExpression>>(_ => default!);
                    }
                    decoded.Add(value.Value);
                }
                return Results.Ok(decoded);
            }
        Result<List<IntervalVariable>, ErrorCode, Error<ErrorCode>> DecodeIntervals() {
            var decoded = new List<IntervalVariable>();
            foreach (IntervalPayload interval in ChildIntervals) {
                Result<IntervalVariable, ErrorCode, Error<ErrorCode>> value =
                    interval.ToInterval(variables);
                if (value.IsFailed) {
                    return value.Map<List<IntervalVariable>>(_ => default!);
                }
                decoded.Add(value.Value);
            }
            return Results.Ok(decoded);
        }

        switch (Kind) {
            case "integer-comparison": {
                Result<
                    ConstraintProgrammingExpression,
                    ErrorCode,
                    Error<ErrorCode>> expression = RequiredExpression(Expression);
                if (expression.IsFailed) {
                    return expression.Map<ConstraintProgrammingConstraint>(_ => default!);
                }
                if (Comparison is null ||
                    !Enum.TryParse(Comparison, ignoreCase: false, out ConstraintProgrammingComparison comparison)) {
                    return Failure<ConstraintProgrammingConstraint>(
                        ErrorCode.IllegalArgument,
                        "未知 CP comparison / Unknown CP comparison");
                }
                if (Rhs is not { } rhs) {
                    return Failure<ConstraintProgrammingConstraint>(
                        ErrorCode.IllegalArgument,
                        "CP comparison 缺少 rhs / CP comparison is missing rhs");
                }
                return Results.Ok<ConstraintProgrammingConstraint>(
                    new ConstraintProgrammingConstraint.IntegerComparison(
                        expression: expression.Value,
                        comparison: comparison,
                        rhs: new Int64(rhs)));
            }
            case "bool-and":
            case "bool-or":
            case "bool-xor": {
                Result<List<BooleanLiteral>, ErrorCode, Error<ErrorCode>> literals =
                    DecodeLiterals();
                if (literals.IsFailed) {
                    return literals.Map<ConstraintProgrammingConstraint>(_ => default!);
                }
                return Results.Ok<ConstraintProgrammingConstraint>(Kind switch {
                    "bool-and" => new ConstraintProgrammingConstraint.BoolAnd(literals.Value),
                    "bool-or" => new ConstraintProgrammingConstraint.BoolOr(literals.Value),
                    _ => new ConstraintProgrammingConstraint.BoolXor(literals.Value),
                });
            }
            case "literal": {
                if (ChildLiterals.Count != 1) {
                    return Failure<ConstraintProgrammingConstraint>(
                        ErrorCode.IllegalArgument,
                        "CP literal 数量无效 / Invalid CP literal arity");
                }
                Result<BooleanLiteral, ErrorCode, Error<ErrorCode>> literal =
                    ChildLiterals[0].ToLiteral(variables);
                return literal.IsOk
                    ? Results.Ok<ConstraintProgrammingConstraint>(
                        new ConstraintProgrammingConstraint.Literal(literal.Value))
                    : literal.Map<ConstraintProgrammingConstraint>(_ => default!);
            }
            case "implication": {
                if (Enforcement is null || Child is null) {
                    return Failure<ConstraintProgrammingConstraint>(
                        ErrorCode.IllegalArgument,
                        "CP implication 缺少 enforcement 或 child / CP implication is missing enforcement or child");
                }
                Result<BooleanLiteral, ErrorCode, Error<ErrorCode>> enforcement =
                    Enforcement.ToLiteral(variables);
                if (enforcement.IsFailed) {
                    return enforcement.Map<ConstraintProgrammingConstraint>(_ => default!);
                }
                Result<
                    ConstraintProgrammingConstraint,
                    ErrorCode,
                    Error<ErrorCode>> child = Child.ToConstraint(variables);
                return child.IsOk
                    ? Results.Ok<ConstraintProgrammingConstraint>(
                        new ConstraintProgrammingConstraint.Implication(
                            enforcement: enforcement.Value,
                            constraint: child.Value))
                    : child.Map<ConstraintProgrammingConstraint>(_ => default!);
            }
            case "reified": {
                if (ReifiedLiteral is null ||
                    Child is null ||
                    ReifiedDirection is null ||
                    !Enum.TryParse(
                        ReifiedDirection,
                        ignoreCase: false,
                        out ReificationDirection direction)) {
                    return Failure<ConstraintProgrammingConstraint>(
                        ErrorCode.IllegalArgument,
                        "CP reified 形状无效 / CP reified shape is invalid");
                }
                Result<BooleanLiteral, ErrorCode, Error<ErrorCode>> literal =
                    ReifiedLiteral.ToLiteral(variables);
                if (literal.IsFailed) {
                    return literal.Map<ConstraintProgrammingConstraint>(_ => default!);
                }
                Result<
                    ConstraintProgrammingConstraint,
                    ErrorCode,
                    Error<ErrorCode>> child = Child.ToConstraint(variables);
                return child.IsOk
                    ? Results.Ok<ConstraintProgrammingConstraint>(
                        new ConstraintProgrammingConstraint.Reified(
                            literal: literal.Value,
                            constraint: child.Value,
                            direction: direction))
                    : child.Map<ConstraintProgrammingConstraint>(_ => default!);
            }
            case "all-different": {
                Result<
                    List<ConstraintProgrammingExpression>,
                    ErrorCode,
                    Error<ErrorCode>> expressions = DecodeExpressions();
                if (expressions.IsFailed) {
                    return expressions.Map<ConstraintProgrammingConstraint>(_ => default!);
                }
                Result<
                    ConstraintProgrammingConstraint.AllDifferent,
                    ErrorCode,
                    Error<ErrorCode>> constraint =
                    ConstraintProgrammingConstraint.CreateAllDifferent(expressions.Value);
                return constraint.IsOk
                    ? Results.Ok<ConstraintProgrammingConstraint>(constraint.Value)
                    : constraint.Map<ConstraintProgrammingConstraint>(_ => default!);
            }
            case "element": {
                Result<
                    ConstraintProgrammingExpression,
                    ErrorCode,
                    Error<ErrorCode>> index = RequiredExpression(Index);
                Result<
                    ConstraintProgrammingExpression,
                    ErrorCode,
                    Error<ErrorCode>> target = RequiredExpression(Target);
                if (index.IsFailed) {
                    return index.Map<ConstraintProgrammingConstraint>(_ => default!);
                }
                if (target.IsFailed) {
                    return target.Map<ConstraintProgrammingConstraint>(_ => default!);
                }
                var values = new List<ConstraintProgrammingExpression>();
                foreach (ValuePayload value in ElementValues) {
                    if (value.Expression is not null) {
                        Result<
                            ConstraintProgrammingExpression,
                            ErrorCode,
                            Error<ErrorCode>> expression =
                            value.Expression.ToExpression(variables);
                        if (expression.IsFailed) {
                            return expression.Map<ConstraintProgrammingConstraint>(_ => default!);
                        }
                        values.Add(expression.Value);
                    }
                    else if (value.Integer is { } integer) {
                        values.Add(ConstraintProgrammingExpression.CreateConstant(integer));
                    }
                    else {
                        return Failure<ConstraintProgrammingConstraint>(
                            ErrorCode.IllegalArgument,
                            "Element 值为空 / Element value is empty");
                    }
                }
                Result<
                    ConstraintProgrammingConstraint.Element,
                    ErrorCode,
                    Error<ErrorCode>> constraint =
                    ConstraintProgrammingConstraint.CreateElement(
                        index: index.Value,
                        values: values,
                        target: target.Value);
                return constraint.IsOk
                    ? Results.Ok<ConstraintProgrammingConstraint>(constraint.Value)
                    : constraint.Map<ConstraintProgrammingConstraint>(_ => default!);
            }
            case "allowed-assignments":
            case "forbidden-assignments": {
                Result<
                    List<ConstraintProgrammingExpression>,
                    ErrorCode,
                    Error<ErrorCode>> expressions = DecodeExpressions();
                if (expressions.IsFailed) {
                    return expressions.Map<ConstraintProgrammingConstraint>(_ => default!);
                }
                IEnumerable<IEnumerable<Int64>> tuples = AssignmentTuples.Select(
                    tuple => tuple.Select(value => new Int64(value)));
                if (Kind == "allowed-assignments") {
                    Result<
                        ConstraintProgrammingConstraint.AllowedAssignments,
                        ErrorCode,
                        Error<ErrorCode>> constraint =
                        ConstraintProgrammingConstraint.CreateAllowedAssignments(
                            expressions.Value,
                            tuples);
                    return constraint.IsOk
                        ? Results.Ok<ConstraintProgrammingConstraint>(constraint.Value)
                        : constraint.Map<ConstraintProgrammingConstraint>(_ => default!);
                }
                Result<
                    ConstraintProgrammingConstraint.ForbiddenAssignments,
                    ErrorCode,
                    Error<ErrorCode>> forbidden =
                    ConstraintProgrammingConstraint.CreateForbiddenAssignments(
                        expressions.Value,
                        tuples);
                return forbidden.IsOk
                    ? Results.Ok<ConstraintProgrammingConstraint>(forbidden.Value)
                    : forbidden.Map<ConstraintProgrammingConstraint>(_ => default!);
            }
            case "circuit": {
                Result<
                    List<ConstraintProgrammingExpression>,
                    ErrorCode,
                    Error<ErrorCode>> successors =
                    DecodeExpressionList(ChildSuccessors, variables);
                if (successors.IsFailed) {
                    return successors.Map<ConstraintProgrammingConstraint>(_ => default!);
                }
                Result<
                    ConstraintProgrammingConstraint.Circuit,
                    ErrorCode,
                    Error<ErrorCode>> constraint =
                    ConstraintProgrammingConstraint.CreateCircuit(successors.Value);
                return constraint.IsOk
                    ? Results.Ok<ConstraintProgrammingConstraint>(constraint.Value)
                    : constraint.Map<ConstraintProgrammingConstraint>(_ => default!);
            }
            case "automaton": {
                Result<
                    List<ConstraintProgrammingExpression>,
                    ErrorCode,
                    Error<ErrorCode>> expressions = DecodeExpressions();
                if (expressions.IsFailed) {
                    return expressions.Map<ConstraintProgrammingConstraint>(_ => default!);
                }
                Result<
                    ConstraintProgrammingConstraint.Automaton,
                    ErrorCode,
                    Error<ErrorCode>> constraint =
                    ConstraintProgrammingConstraint.CreateAutomaton(
                        expressions: expressions.Value,
                        initialState: InitialState ?? 0,
                        finalStates: AcceptingStates,
                        transitions: AutomatonTransitions.Select(transition =>
                            new ConstraintProgrammingConstraint.AutomatonTransition(
                                fromState: transition.FromState,
                                value: new Int64(transition.Value),
                                toState: transition.ToState)));
                return constraint.IsOk
                    ? Results.Ok<ConstraintProgrammingConstraint>(constraint.Value)
                    : constraint.Map<ConstraintProgrammingConstraint>(_ => default!);
            }
            case "reservoir": {
                var events = new List<ConstraintProgrammingConstraint.Reservoir.Event>();
                foreach (EventPayload evt in ReservoirEvents) {
                    Result<
                        ConstraintProgrammingExpression,
                        ErrorCode,
                        Error<ErrorCode>> time =
                        evt.Time.ToExpression(variables);
                    if (time.IsFailed) {
                        return time.Map<ConstraintProgrammingConstraint>(_ => default!);
                    }
                    Result<
                        ConstraintProgrammingExpression,
                        ErrorCode,
                        Error<ErrorCode>> change =
                        evt.LevelChange.ToExpression(variables);
                    if (change.IsFailed) {
                        return change.Map<ConstraintProgrammingConstraint>(_ => default!);
                    }
                    events.Add(new ConstraintProgrammingConstraint.Reservoir.Event(
                        Time: time.Value,
                        LevelChange: change.Value));
                }
                if (InitialLevel is not { } initial ||
                    MinimumLevel is not { } minimum ||
                    MaximumLevel is not { } maximum) {
                    return Failure<ConstraintProgrammingConstraint>(
                        ErrorCode.IllegalArgument,
                        "Reservoir 缺少液位边界 / Reservoir is missing level bounds");
                }
                Result<
                    ConstraintProgrammingConstraint.Reservoir,
                    ErrorCode,
                    Error<ErrorCode>> constraint =
                    ConstraintProgrammingConstraint.CreateReservoir(
                        events: events,
                        initialLevel: new Int64(initial),
                        minimumLevel: new Int64(minimum),
                        maximumLevel: new Int64(maximum));
                return constraint.IsOk
                    ? Results.Ok<ConstraintProgrammingConstraint>(constraint.Value)
                    : constraint.Map<ConstraintProgrammingConstraint>(_ => default!);
            }
            case "no-overlap":
            case "cumulative": {
                Result<List<IntervalVariable>, ErrorCode, Error<ErrorCode>> intervals =
                    DecodeIntervals();
                if (intervals.IsFailed) {
                    return intervals.Map<ConstraintProgrammingConstraint>(_ => default!);
                }
                if (Kind == "no-overlap") {
                    Result<
                        NoOverlap,
                        ErrorCode,
                        Error<ErrorCode>> noOverlap = NoOverlap.Create(intervals.Value);
                    return noOverlap.IsOk
                        ? Results.Ok<ConstraintProgrammingConstraint>(noOverlap.Value)
                        : noOverlap.Map<ConstraintProgrammingConstraint>(_ => default!);
                }
                Result<
                    List<ConstraintProgrammingExpression>,
                    ErrorCode,
                    Error<ErrorCode>> demands =
                    DecodeExpressionList(ChildDemands, variables);
                Result<
                    ConstraintProgrammingExpression,
                    ErrorCode,
                    Error<ErrorCode>> capacity = RequiredExpression(Capacity);
                if (demands.IsFailed) {
                    return demands.Map<ConstraintProgrammingConstraint>(_ => default!);
                }
                if (capacity.IsFailed) {
                    return capacity.Map<ConstraintProgrammingConstraint>(_ => default!);
                }
                Result<Cumulative, ErrorCode, Error<ErrorCode>> cumulative =
                    Cumulative.Create(
                        intervals: intervals.Value,
                        demands: demands.Value,
                        capacity: capacity.Value);
                return cumulative.IsOk
                    ? Results.Ok<ConstraintProgrammingConstraint>(cumulative.Value)
                    : cumulative.Map<ConstraintProgrammingConstraint>(_ => default!);
            }
            default:
                return Failure<ConstraintProgrammingConstraint>(
                    ErrorCode.IllegalArgument,
                    $"未知 CP constraint 类型：{Kind} / Unknown CP constraint kind: {Kind}");
        }
    }
}

internal sealed record ValuePayload(
    [property: JsonPropertyName("integer"), JsonPropertyOrder(0)] long? Integer = null,
    [property: JsonPropertyName("expression"), JsonPropertyOrder(1)] ExpressionPayload? Expression = null) {
    internal ValuePayload Canonicalized() => this with {
        Expression = Expression?.Canonicalized(),
    };
}

internal sealed record TransitionPayload(
    [property: JsonPropertyName("fromState"), JsonPropertyOrder(0)] int FromState,
    [property: JsonPropertyName("value"), JsonPropertyOrder(1)] long Value,
    [property: JsonPropertyName("toState"), JsonPropertyOrder(2)] int ToState);

internal sealed record EventPayload(
    [property: JsonPropertyName("time"), JsonPropertyOrder(0)] ExpressionPayload Time,
    [property: JsonPropertyName("levelChange"), JsonPropertyOrder(1)] ExpressionPayload LevelChange) {
    internal EventPayload Canonicalized() => this with {
        Time = Time.Canonicalized(),
        LevelChange = LevelChange.Canonicalized(),
    };

    internal string CanonicalKey() => JsonSerializer.Serialize(this, ConstraintProgrammingSnapshotCodec.JsonOptions);
}

internal sealed record ObjectivePayload(
    [property: JsonPropertyName("id"), JsonPropertyOrder(0)] string Id,
    [property: JsonPropertyName("category"), JsonPropertyOrder(1)] string Category,
    [property: JsonPropertyName("name"), JsonPropertyOrder(2)] string Name,
    [property: JsonPropertyName("expression"), JsonPropertyOrder(3)] ExpressionPayload Expression,
    [property: JsonPropertyName("scope"), JsonPropertyOrder(4)] string Scope = "model-local",
    [property: JsonPropertyName("origin"), JsonPropertyOrder(5)] string? Origin = null,
    [property: JsonPropertyName("identityProvenance"), JsonPropertyOrder(6)] List<IdentityOriginPayload>? IdentityProvenance = null) {
    [JsonIgnore]
    internal List<IdentityOriginPayload> Provenance => IdentityProvenance ?? new List<IdentityOriginPayload>();

    internal ObjectivePayload Canonicalized() => this with {
        Expression = Expression.Canonicalized(),
        Scope = CanonicalScope(Scope),
        IdentityProvenance = Provenance.Canonicalized(Origin),
    };
}

internal static class ConstraintProgrammingSnapshotCodecHelpers {
    internal static DomainPayload ToPayload(this IntegerDomain domain) => domain switch {
        IntegerDomain.Interval interval => new DomainPayload(
            Kind: "interval",
            Lower: interval.LowerBound.ToLong(),
            Upper: interval.UpperBound.ToLong(),
            Values: null),
        IntegerDomain.Values values => new DomainPayload(
            Kind: "values",
            Lower: null,
            Upper: null,
            Values: values.Items.Select(value => value.ToLong()).ToList()),
        _ => new DomainPayload(
            Kind: "unknown",
            Lower: domain.LowerBound.ToLong(),
            Upper: domain.UpperBound.ToLong(),
            Values: null),
    };

    internal static IntervalPayload ToPayload(this IntervalVariable interval) => new(
        Id: interval.Id.Value,
        Start: interval.Start.ToPayload(),
        Size: interval.Size.ToPayload(),
        End: interval.End.ToPayload(),
        Presence: interval.Presence?.ToPayload(),
        Scope: CanonicalScope(interval.Scope),
        Origin: interval.Origin,
        IdentityProvenance: interval.IdentityProvenance.ToPayload(interval.Origin));

    internal static ExpressionPayload ToPayload(
        this ConstraintProgrammingExpression expression) => expression switch {
            ConstraintProgrammingExpression.Constant constant => new ExpressionPayload(
                Kind: "constant",
                Value: constant.Value.ToLong()),
            ConstraintProgrammingExpression.Invalid invalid => new ExpressionPayload(
                Kind: "invalid",
                Message: invalid.Message),
            ConstraintProgrammingExpression.Variable variable => new ExpressionPayload(
                Kind: "variable",
                VariableId: variable.VariableId.Value,
                Domain: variable.Domain.ToPayload()),
            ConstraintProgrammingExpression.Linear linear => new ExpressionPayload(
                Kind: "linear",
                Terms: linear.Terms
                    .Select(term => new TermPayload(
                        VariableId: term.VariableId.Value,
                        Coefficient: term.Coefficient.ToLong()))
                    .ToList(),
                Constant: linear.ConstantTerm.ToLong()),
            _ => new ExpressionPayload(
                Kind: "invalid",
                Message: "未知 CP 表达式节点 / Unknown CP expression node"),
        };

    internal static LiteralPayload ToPayload(this BooleanLiteral literal) => new(
        VariableId: literal.VariableId?.Value,
        Negated: literal.Negated,
        Constant: literal.Constant);

    internal static ConstraintPayload ToPayload(
        this ConstraintProgrammingConstraint constraint) => constraint switch {
            ConstraintProgrammingConstraint.IntegerComparison comparison => new ConstraintPayload(
                Kind: "integer-comparison",
                Expression: comparison.Expression.ToPayload(),
                Comparison: comparison.Comparison.ToString(),
                Rhs: comparison.Rhs.ToLong()),
            ConstraintProgrammingConstraint.BoolAnd conjunction => new ConstraintPayload(
                Kind: "bool-and",
                Literals: conjunction.Literals.Select(literal => literal.ToPayload()).ToList()),
            ConstraintProgrammingConstraint.BoolOr disjunction => new ConstraintPayload(
                Kind: "bool-or",
                Literals: disjunction.Literals.Select(literal => literal.ToPayload()).ToList()),
            ConstraintProgrammingConstraint.BoolXor exclusive => new ConstraintPayload(
                Kind: "bool-xor",
                Literals: exclusive.Literals.Select(literal => literal.ToPayload()).ToList()),
            ConstraintProgrammingConstraint.Literal literal => new ConstraintPayload(
                Kind: "literal",
                Literals: new List<LiteralPayload> { literal.LiteralValue.ToPayload() }),
            ConstraintProgrammingConstraint.Implication implication => new ConstraintPayload(
                Kind: "implication",
                Enforcement: implication.Enforcement.ToPayload(),
                Child: implication.Constraint.ToPayload()),
            ConstraintProgrammingConstraint.Reified reified => new ConstraintPayload(
                Kind: "reified",
                Child: reified.Constraint.ToPayload(),
                ReifiedDirection: reified.Direction.ToString(),
                ReifiedLiteral: reified.LiteralValue.ToPayload()),
            ConstraintProgrammingConstraint.AllDifferent allDifferent => new ConstraintPayload(
                Kind: "all-different",
                Expressions: allDifferent.Expressions
                    .Select(expression => expression.ToPayload())
                    .ToList()),
            ConstraintProgrammingConstraint.Element element => new ConstraintPayload(
                Kind: "element",
                Index: element.Index.ToPayload(),
                Values: element.Values
                    .Select(expression => new ValuePayload(Expression: expression.ToPayload()))
                    .ToList(),
                Target: element.Target.ToPayload()),
            ConstraintProgrammingConstraint.AllowedAssignments allowed => new ConstraintPayload(
                Kind: "allowed-assignments",
                Expressions: allowed.Expressions
                    .Select(expression => expression.ToPayload())
                    .ToList(),
                Tuples: allowed.Tuples
                    .Select(tuple => tuple.Select(value => value.ToLong()).ToList())
                    .ToList()),
            ConstraintProgrammingConstraint.ForbiddenAssignments forbidden => new ConstraintPayload(
                Kind: "forbidden-assignments",
                Expressions: forbidden.Expressions
                    .Select(expression => expression.ToPayload())
                    .ToList(),
                Tuples: forbidden.Tuples
                    .Select(tuple => tuple.Select(value => value.ToLong()).ToList())
                    .ToList()),
            ConstraintProgrammingConstraint.Circuit circuit => new ConstraintPayload(
                Kind: "circuit",
                Successors: circuit.Successors
                    .Select(expression => expression.ToPayload())
                    .ToList()),
            ConstraintProgrammingConstraint.Automaton automaton => new ConstraintPayload(
                Kind: "automaton",
                Expressions: automaton.Expressions
                    .Select(expression => expression.ToPayload())
                    .ToList(),
                InitialState: automaton.InitialState,
                FinalStates: automaton.FinalStates.ToList(),
                Transitions: automaton.Transitions
                    .Select(transition => new TransitionPayload(
                        FromState: transition.FromState,
                        Value: transition.Value.ToLong(),
                        ToState: transition.ToState))
                    .ToList()),
            ConstraintProgrammingConstraint.Reservoir reservoir => new ConstraintPayload(
                Kind: "reservoir",
                Events: reservoir.Events
                    .Select(evt => new EventPayload(
                        Time: evt.Time.ToPayload(),
                        LevelChange: evt.LevelChange.ToPayload()))
                    .ToList(),
                InitialLevel: reservoir.InitialLevel.ToLong(),
                MinimumLevel: reservoir.MinimumLevel.ToLong(),
                MaximumLevel: reservoir.MaximumLevel.ToLong()),
            NoOverlap noOverlap => new ConstraintPayload(
                Kind: "no-overlap",
                Intervals: noOverlap.Intervals
                    .Select(interval => interval.ToPayload())
                    .ToList()),
            Cumulative cumulative => new ConstraintPayload(
                Kind: "cumulative",
                Intervals: cumulative.Intervals
                    .Select(interval => interval.ToPayload())
                    .ToList(),
                Demands: cumulative.Demands
                    .Select(demand => demand.ToPayload())
                    .ToList(),
                Capacity: cumulative.Capacity.ToPayload()),
            _ => new ConstraintPayload(
                Kind: "invalid",
                Expression: new ExpressionPayload(
                    Kind: "invalid",
                    Message: "未知 CP 约束节点 / Unknown CP constraint node")),
        };

    internal static List<IdentityOriginPayload> ToPayload(
        this IReadOnlyList<ModelElementOrigin> provenance,
        string? origin) =>
        ConstraintProgrammingIdentity
            .CanonicalProvenance(origin: origin, provenance: provenance)
            .Select(entry => new IdentityOriginPayload(Kind: entry.Kind, Key: entry.Key))
            .ToList();

    internal static List<IdentityOriginPayload> Canonicalized(
        this IReadOnlyList<IdentityOriginPayload> provenance,
        string? origin) =>
        ConstraintProgrammingIdentity
            .CanonicalProvenance(origin: origin, provenance: provenance.ToModelOrigins())
            .Select(entry => new IdentityOriginPayload(Kind: entry.Kind, Key: entry.Key))
            .ToList();

    internal static List<ModelElementOrigin> ToModelOrigins(
        this IReadOnlyList<IdentityOriginPayload> provenance) =>
        provenance
            .Select(entry => new ModelElementOrigin(Kind: entry.Kind, Key: entry.Key))
            .ToList();

    internal static string CanonicalKey(this ExpressionPayload expression) =>
        JsonSerializer.Serialize(expression, ConstraintProgrammingSnapshotCodec.JsonOptions);

    internal static string CanonicalKey(this IntervalPayload interval) =>
        JsonSerializer.Serialize(interval, ConstraintProgrammingSnapshotCodec.JsonOptions);

    internal static string CanonicalScope(ModelElementScope scope) =>
        scope == ModelElementScope.Stable ? "stable" : "model-local";

    internal static string CanonicalScope(string scope) => scope switch {
        "stable" or "Stable" => "stable",
        "model-local" or "modelLocal" or "ModelLocal" => "model-local",
        _ => scope,
    };

    internal static ModelElementScope ParseScope(string scope) =>
        CanonicalScope(scope) == "stable"
            ? ModelElementScope.Stable
            : ModelElementScope.ModelLocal;

    internal static int CompareIntLists(IReadOnlyList<long>? left, IReadOnlyList<long>? right) {
        left ??= Array.Empty<long>();
        right ??= Array.Empty<long>();
        for (int index = 0; index < System.Math.Min(left.Count, right.Count); ++index) {
            int comparison = left[index].CompareTo(right[index]);
            if (comparison != 0) {
                return comparison;
            }
        }
        return left.Count.CompareTo(right.Count);
    }

    internal static Result<
        List<ConstraintProgrammingExpression>,
        ErrorCode,
        Error<ErrorCode>> DecodeExpressionList(
        IReadOnlyList<ExpressionPayload> expressions,
        IReadOnlyDictionary<VariableId, IVariableItem> variables) {
        var result = new List<ConstraintProgrammingExpression>();
        foreach (ExpressionPayload expression in expressions) {
            Result<
                ConstraintProgrammingExpression,
                ErrorCode,
                Error<ErrorCode>> decoded =
                expression.ToExpression(variables);
            if (decoded.IsFailed) {
                return decoded.Map<List<ConstraintProgrammingExpression>>(_ => default!);
            }
            result.Add(decoded.Value);
        }
        return Results.Ok(result);
    }

    internal static Result<T, ErrorCode, Error<ErrorCode>> Failure<T>(
        ErrorCode code,
        string message) =>
        Results.Failed<T>(new Err<ErrorCode>(code, message));

}
