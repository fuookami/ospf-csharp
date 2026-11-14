#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Solver.Report;
namespace Fuookami.Ospf.Core.Model.Intermediate;

internal static class MatrixIdentitySupport {
    internal delegate Result<T, ErrorCode, Error<ErrorCode>> CreateMatrix<T>(
        IReadOnlyList<ModelViewVariable> variables,
        IReadOnlyList<ModelElementIdentity>? rows,
        ModelElementIdentity? objective,
        object? revision);

    internal static Result<T, ErrorCode, Error<ErrorCode>> Materialize<T>(
        ModelElementIdentityRegistry? registry,
        IReadOnlyList<ModelViewVariable> variables,
        IReadOnlyList<object> rows,
        IReadOnlyList<object?> objectives,
        object objectiveFallback,
        CreateMatrix<T> create) {
        if (registry is null) {
            return create(
                variables: variables,
                rows: null,
                objective: null,
                revision: null
            );
        }
        return registry.ResolveAtomically(() => {
            var resolvedVariables = new List<ModelViewVariable>(variables.Count);
            foreach (ModelViewVariable variable in variables) {
                if (variable.Origin is null) {
                    return Results.Failed<T>(new Err<ErrorCode>(
                        ErrorCode.IllegalArgument, "变量来源为空 / Variable origin is null"));
                }
                ModelElementIdentity identity = Resolve(
                    registry: registry,
                    element: variable.Origin,
                    kind: ModelElementKind.Variable,
                    index: variable.Index
                );
                resolvedVariables.Add(variable.WithIdentity(
                    identity: identity,
                    identityNamespace: registry.Namespace,
                    schema: registry.SchemaVersion
                ));
            }
            var resolvedRows = new List<ModelElementIdentity>(rows.Count);
            for (int i = 0; i < rows.Count; i++) {
                resolvedRows.Add(Resolve(
                    registry: registry,
                    element: rows[i],
                    kind: ModelElementKind.Constraint,
                    index: i
                ));
            }
            ModelElementIdentity objective;
            if (objectives.Count > 1) {
                objective = Aggregate(registry, objectives) ?? new ModelElementIdentity(
                    Kind: ModelElementKind.Objective,
                    Id: new ModelElementId("model-local-objective:0"),
                    Scope: ModelElementScope.ModelLocal
                );
            }
            else {
                objective = Resolve(
                    registry: registry,
                    element: objectives.Count == 1 ? objectives[0] ?? objectiveFallback : objectiveFallback,
                    kind: ModelElementKind.Objective,
                    index: 0
                );
            }
            return create(
                variables: resolvedVariables,
                rows: resolvedRows,
                objective: objective,
                revision: registry.Revision
            );
        });
    }

    private static ModelElementIdentity Resolve(
        ModelElementIdentityRegistry registry,
        object element,
        ModelElementKind kind,
        int index) {
        ModelElementIdentity? identity = registry.Identity(element);
        if (identity?.Kind == kind) {
            return identity;
        }
        string id = kind switch {
            ModelElementKind.Variable => registry.VariableId(element, index).Value,
            ModelElementKind.Constraint => registry.ConstraintId(element, index).Value,
            _ => registry.ObjectiveId(element, index).Value,
        };
        return new ModelElementIdentity(
            Kind: kind,
            Id: new ModelElementId(id),
            Scope: ModelElementScope.ModelLocal
        );
    }

    private static ModelElementIdentity? Aggregate(ModelElementIdentityRegistry registry, IReadOnlyList<object?> objectives) {
        var identities = new List<ModelElementIdentity>(objectives.Count);
        foreach (object? origin in objectives) {
            ModelElementIdentity? identity = origin is null ? null : registry.Identity(origin);
            if (identity is null || identity.Kind != ModelElementKind.Objective
                || identity.Scope != ModelElementScope.Stable || string.IsNullOrWhiteSpace(identity.Id.Value)
                || identity.Id.Value.StartsWith("model-local-", StringComparison.Ordinal)
                || (identity.Id.Value.StartsWith("artifact:", StringComparison.Ordinal)
                    && !DerivedModelElementIdentity.IsGeneratedArtifactModelElementId(identity.Id.Value, ModelElementKind.Objective))
                || identity.Provenance.Count == 0) {
                return null;
            }
            identities.Add(identity);
        }
        var provenance = identities.SelectMany(identity => identity.Provenance)
            .Distinct()
            .OrderBy(origin => origin.Kind, StringComparer.Ordinal)
            .ThenBy(origin => origin.Key, StringComparer.Ordinal)
            .ToList();
        if (provenance.Any(origin => string.IsNullOrWhiteSpace(origin.Kind) || string.IsNullOrWhiteSpace(origin.Key))) {
            return null;
        }
        string sourceKey = string.Concat(identities.Select(identity => identity.Id.Value)
            .OrderBy(id => id, StringComparer.Ordinal)
            .Select(DerivedModelElementIdentity.EncodeIdentityPart));
        return DerivedModelElementIdentity.Create(
            kind: ModelElementKind.Objective,
            role: "aggregate-objective",
            sourceId: $"aggregate:{sourceKey}",
            sourceScope: ModelElementScope.Stable,
            sourceOrigin: null,
            sourceProvenance: provenance,
            identityNamespace: registry.Namespace,
            identitySchemaVersion: registry.SchemaVersion
        );
    }
}
