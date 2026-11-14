#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Utils.Concept;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Core.Solver.Report;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
namespace Fuookami.Ospf.Core.Model.Basic;

/// <summary>模型视图身份校验 / Model-view identity validation.</summary>
public static class ModelViewIdentityExtensions {
    /// <summary>校验行形状和完整模型元素身份 / Validate row shape and all model-element identities.</summary>
    /// <typeparam name="C">约束单元格类型 / Constraint cell type.</typeparam>
    /// <typeparam name="O">目标单元格类型 / Objective cell type.</typeparam>
    /// <param name="model">模型视图 / Model view.</param>
    /// <returns>成功或校验错误 / Success or a validation error.</returns>
    public static Try ValidateIdentity<C, O>(this IModelView<C, O>? model)
        where C : IConstraintCell<C>, ICopyable<C>
        where O : IModelCell<O>, ICopyable<O> {
        if (model is null) {
            return Failure("模型为空 / Model is null");
        }
        try {
            Try shape = model.Constraints.ValidateShape();
            if (!shape.IsOk) {
                return shape;
            }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var namespaces = new HashSet<string>(StringComparer.Ordinal);
            var schemas = new HashSet<string>(StringComparer.Ordinal);
            foreach (ModelViewVariable variable in model.Variables) {
                if (variable is null) {
                    return Failure("变量为空 / Variable is null");
                }
                Try result = Check(
                    identity: variable.Identity,
                    kind: ModelElementKind.Variable,
                    identityNamespace: variable.IdentityNamespace,
                    schema: variable.IdentitySchemaVersion,
                    ids: ids,
                    namespaces: namespaces,
                    schemas: schemas
                );
                if (!result.IsOk) {
                    return result;
                }
            }
            ModelConstraint<C> constraints = model.Constraints;
            AddMetadata(constraints.IdentityNamespace, namespaces);
            AddMetadata(constraints.IdentitySchemaVersion, schemas);
            foreach (ModelElementIdentity identity in constraints.Identities) {
                Try result = Check(
                    identity: identity,
                    kind: ModelElementKind.Constraint,
                    identityNamespace: constraints.IdentityNamespace,
                    schema: constraints.IdentitySchemaVersion,
                    ids: ids,
                    namespaces: namespaces,
                    schemas: schemas
                );
                if (identity is null || !result.IsOk) {
                    return identity is null ? Failure("约束身份为空 / Constraint identity is null") : result;
                }
            }
            Try objective = Check(
                identity: model.Objective.Identity,
                kind: ModelElementKind.Objective,
                identityNamespace: model.Objective.IdentityNamespace,
                schema: model.Objective.IdentitySchemaVersion,
                ids: ids,
                namespaces: namespaces,
                schemas: schemas
            );
            if (!objective.IsOk) {
                return objective;
            }
            return namespaces.Count > 1 || schemas.Count > 1
                ? Failure("模型身份 namespace/schema 不一致 / Model identity namespace/schema disagree")
                : Results.OkInstance;
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            return Failure($"无法校验模型身份：{error.Message} / Cannot validate model identities: {error.Message}");
        }
    }

    private static Try Check(
        ModelElementIdentity? identity,
        ModelElementKind kind,
        string? identityNamespace,
        string? schema,
        HashSet<string> ids,
        HashSet<string> namespaces,
        HashSet<string> schemas) {
        AddMetadata(identityNamespace, namespaces);
        AddMetadata(schema, schemas);
        if (identity is null) {
            return Results.OkInstance;
        }
        string? id = identity.Id.Value;
        if (identity.Kind != kind || string.IsNullOrWhiteSpace(id) || !DerivedModelElementIdentity.IsWellFormedUtf16(id)
            || (identity.Scope != ModelElementScope.Stable && identity.Scope != ModelElementScope.ModelLocal)) {
            return Failure("模型元素身份类别、ID 或作用域无效 / Invalid model-element identity kind, ID or scope");
        }
        if (id.StartsWith("artifact:", StringComparison.Ordinal)
            && !DerivedModelElementIdentity.IsGeneratedArtifactModelElementId(id, kind)) {
            return Failure("模型元素 artifact ID 无效 / Invalid model-element artifact ID");
        }
        if (identity.Scope == ModelElementScope.Stable) {
            if (id.StartsWith("model-local-", StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(identityNamespace) || string.IsNullOrWhiteSpace(schema)
                || identity.Provenance.Count == 0
                || identity.Provenance.Any(origin => origin is null
                    || string.IsNullOrWhiteSpace(origin.Kind) || string.IsNullOrWhiteSpace(origin.Key))
                || (identity.Origin is not null && !identity.Provenance.Contains(identity.Origin))) {
                return Failure("稳定身份缺少有效来源或元数据 / Stable identity lacks valid provenance or metadata");
            }
        }
        else if (identity.Origin is not null || identity.Provenance.Count != 0) {
            return Failure("模型内身份不能携带稳定来源 / Model-local identities cannot carry stable provenance");
        }
        return ids.Add(id) ? Results.OkInstance : Failure($"模型元素 ID 重复：{id} / Duplicate model-element ID: {id}");
    }

    private static void AddMetadata(string? value, HashSet<string> values) {
        if (!string.IsNullOrWhiteSpace(value)) {
            values.Add(value);
        }
    }

    private static Try Failure(string message) =>
        Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.IllegalArgument, message));
}
