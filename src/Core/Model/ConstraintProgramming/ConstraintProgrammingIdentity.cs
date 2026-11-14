#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Model.ConstraintProgramming;

internal sealed record ConstraintProgrammingIdentityMetadata(
    ModelElementScope Scope,
    IReadOnlyList<ModelElementOrigin> Provenance);

internal static class ConstraintProgrammingIdentity {
    internal const string LegacyOriginKind = "legacy-origin";

    internal static Result<
        ConstraintProgrammingIdentityMetadata,
        ErrorCode,
        Error<ErrorCode>> Canonicalize(
        string id,
        ModelElementScope scope,
        string? origin,
        IReadOnlyList<ModelElementOrigin>? provenance,
        string identityNamespace,
        string identitySchemaVersion) {
        if (string.IsNullOrWhiteSpace(id)) {
            return Failure("CP 身份 ID 不能为空 / CP identity ID must not be blank");
        }

        IReadOnlyList<ModelElementOrigin> canonical = CanonicalProvenance(origin, provenance);
        string? message = Validate(
            id: id,
            scope: scope,
            origin: origin,
            provenance: canonical,
            identityNamespace: identityNamespace,
            identitySchemaVersion: identitySchemaVersion);
        return message is null
            ? Results.Ok(new ConstraintProgrammingIdentityMetadata(scope, canonical))
            : Failure(message);
    }

    internal static string? Validate(
        string id,
        ModelElementScope scope,
        string? origin,
        IReadOnlyList<ModelElementOrigin> provenance,
        string identityNamespace,
        string identitySchemaVersion) {
        if (provenance.Any(entry =>
                string.IsNullOrWhiteSpace(entry.Kind) ||
                string.IsNullOrWhiteSpace(entry.Key))) {
            return "CP 身份 provenance 不能为空 / CP identity provenance entries must not be blank";
        }
        if (origin is not null &&
            provenance.All(entry =>
                entry.Kind != LegacyOriginKind ||
                !string.Equals(entry.Key, origin, StringComparison.Ordinal))) {
            return "CP origin 未包含在规范 provenance 中 / CP origin is missing from canonical provenance";
        }

        return scope switch {
            ModelElementScope.Stable => ValidateStable(
                id: id,
                origin: origin,
                provenance: provenance,
                identityNamespace: identityNamespace,
                identitySchemaVersion: identitySchemaVersion),
            ModelElementScope.ModelLocal => ValidateModelLocal(
                id: id,
                origin: origin,
                provenance: provenance),
            _ => "CP 身份 scope 无效 / Invalid CP identity scope",
        };
    }

    internal static IReadOnlyList<ModelElementOrigin> CanonicalProvenance(
        string? origin,
        IReadOnlyList<ModelElementOrigin>? provenance) {
        IEnumerable<ModelElementOrigin> source = provenance ?? Array.Empty<ModelElementOrigin>();
        if (origin is not null) {
            source = source.Append(new ModelElementOrigin(LegacyOriginKind, origin));
        }
        return source
            .Distinct()
            .OrderBy(entry => entry.Kind, StringComparer.Ordinal)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .ToList();
    }

    private static string? ValidateStable(
        string id,
        string? origin,
        IReadOnlyList<ModelElementOrigin> provenance,
        string identityNamespace,
        string identitySchemaVersion) {
        if (id.StartsWith("model-local-", StringComparison.Ordinal)) {
            return "稳定 CP 身份不能使用 model-local ID / Stable CP identities must not use model-local IDs";
        }
        if (id.StartsWith("artifact:", StringComparison.Ordinal)) {
            return "CP 源身份不能使用保留的 artifact 前缀 / CP source identities must not use the reserved artifact prefix";
        }
        if (string.IsNullOrWhiteSpace(identityNamespace) ||
            string.IsNullOrWhiteSpace(identitySchemaVersion)) {
            return "稳定 CP 身份必须有 namespace/schema / Stable CP identities require namespace and schema";
        }
        return string.IsNullOrWhiteSpace(origin) && provenance.Count == 0
            ? "稳定 CP 身份必须提供 origin 或 provenance / Stable CP identities require an origin or provenance"
            : null;
    }

    private static string? ValidateModelLocal(
        string id,
        string? origin,
        IReadOnlyList<ModelElementOrigin> provenance) {
        if (id.StartsWith("artifact:", StringComparison.Ordinal)) {
            return "model-local CP 身份不能使用保留的 artifact 前缀 / Model-local CP identities must not use the reserved artifact prefix";
        }
        return origin is not null || provenance.Count != 0
            ? "model-local CP 身份不能携带稳定来源 / Model-local CP identities must not carry stable provenance"
            : null;
    }

    private static Result<
        ConstraintProgrammingIdentityMetadata,
        ErrorCode,
        Error<ErrorCode>> Failure(string message) =>
        Results.Failed<ConstraintProgrammingIdentityMetadata>(
            new Err<ErrorCode>(
                code: ErrorCode.IllegalArgument,
                message: message));
}

internal static class ConstraintProgrammingBindingExtensions {
    internal static ConstraintProgrammingExpression BindStableVariableIds(
        this ConstraintProgrammingExpression expression,
        IReadOnlyDictionary<IVariableItem, VariableId> bindings) {
        return expression switch {
            ConstraintProgrammingExpression.Constant constant => constant,
            ConstraintProgrammingExpression.Invalid invalid => invalid,
            ConstraintProgrammingExpression.Variable variable => new ConstraintProgrammingExpression.Variable(
                variable: variable.VariableItem,
                domain: variable.Domain,
                id: bindings.TryGetValue(variable.VariableItem, out VariableId id)
                    ? id
                    : variable.Id),
            ConstraintProgrammingExpression.Linear linear => new ConstraintProgrammingExpression.Linear(
                terms: linear.Terms.Select(term => new ConstraintProgrammingExpression.Term(
                    variable: term.Variable,
                    coefficient: term.Coefficient,
                    id: bindings.TryGetValue(term.Variable, out VariableId id)
                        ? id
                        : term.Id)),
                constant: linear.ConstantTerm),
            _ => expression,
        };
    }

    internal static BooleanLiteral BindStableVariableIds(
        this BooleanLiteral literal,
        IReadOnlyDictionary<IVariableItem, VariableId> bindings) {
        if (literal is not BooleanLiteral.VariableLiteral variable) {
            return literal;
        }
        return new BooleanLiteral.VariableLiteral(
            variable: variable.Variable,
            negated: variable.Negated,
            id: bindings.TryGetValue(variable.Variable, out VariableId id)
                ? id
                : variable.Id);
    }

    internal static IntervalVariable BindStableVariableIds(
        this IntervalVariable interval,
        IReadOnlyDictionary<IVariableItem, VariableId> bindings) =>
        interval.WithIdentity(
            scope: interval.Scope,
            provenance: interval.IdentityProvenance).WithExpressions(
                start: interval.Start.BindStableVariableIds(bindings),
                size: interval.Size.BindStableVariableIds(bindings),
                end: interval.End.BindStableVariableIds(bindings),
                presence: interval.Presence?.BindStableVariableIds(bindings));

    internal static ConstraintProgrammingConstraint BindStableVariableIds(
        this ConstraintProgrammingConstraint constraint,
        IReadOnlyDictionary<IVariableItem, VariableId> bindings) {
        return constraint switch {
            ConstraintProgrammingConstraint.IntegerComparison comparison =>
                new ConstraintProgrammingConstraint.IntegerComparison(
                    expression: comparison.Expression.BindStableVariableIds(bindings),
                    comparison: comparison.Comparison,
                    rhs: comparison.Rhs),
            ConstraintProgrammingConstraint.BoolAnd conjunction =>
                new ConstraintProgrammingConstraint.BoolAnd(
                    conjunction.Literals.Select(literal => literal.BindStableVariableIds(bindings))),
            ConstraintProgrammingConstraint.BoolOr disjunction =>
                new ConstraintProgrammingConstraint.BoolOr(
                    disjunction.Literals.Select(literal => literal.BindStableVariableIds(bindings))),
            ConstraintProgrammingConstraint.BoolXor exclusive =>
                new ConstraintProgrammingConstraint.BoolXor(
                    exclusive.Literals.Select(literal => literal.BindStableVariableIds(bindings))),
            ConstraintProgrammingConstraint.Literal literal =>
                new ConstraintProgrammingConstraint.Literal(
                    literal.LiteralValue.BindStableVariableIds(bindings)),
            ConstraintProgrammingConstraint.Implication implication =>
                new ConstraintProgrammingConstraint.Implication(
                    enforcement: implication.Enforcement.BindStableVariableIds(bindings),
                    constraint: implication.Constraint.BindStableVariableIds(bindings)),
            ConstraintProgrammingConstraint.Reified reified =>
                new ConstraintProgrammingConstraint.Reified(
                    literal: reified.LiteralValue.BindStableVariableIds(bindings),
                    constraint: reified.Constraint.BindStableVariableIds(bindings),
                    direction: reified.Direction),
            ConstraintProgrammingConstraint.AllDifferent allDifferent =>
                new ConstraintProgrammingConstraint.AllDifferent(
                    allDifferent.Expressions.Select(expression =>
                        expression.BindStableVariableIds(bindings))),
            ConstraintProgrammingConstraint.Element element =>
                new ConstraintProgrammingConstraint.Element(
                    index: element.Index.BindStableVariableIds(bindings),
                    values: element.Values.Select(expression =>
                        expression.BindStableVariableIds(bindings)),
                    target: element.Target.BindStableVariableIds(bindings)),
            ConstraintProgrammingConstraint.AllowedAssignments allowed =>
                new ConstraintProgrammingConstraint.AllowedAssignments(
                    expressions: allowed.Expressions.Select(expression =>
                        expression.BindStableVariableIds(bindings)),
                    tuples: allowed.Tuples),
            ConstraintProgrammingConstraint.ForbiddenAssignments forbidden =>
                new ConstraintProgrammingConstraint.ForbiddenAssignments(
                    expressions: forbidden.Expressions.Select(expression =>
                        expression.BindStableVariableIds(bindings)),
                    tuples: forbidden.Tuples),
            ConstraintProgrammingConstraint.Circuit circuit =>
                new ConstraintProgrammingConstraint.Circuit(
                    circuit.Successors.Select(expression =>
                        expression.BindStableVariableIds(bindings))),
            ConstraintProgrammingConstraint.Automaton automaton =>
                new ConstraintProgrammingConstraint.Automaton(
                    expressions: automaton.Expressions.Select(expression =>
                        expression.BindStableVariableIds(bindings)),
                    initialState: automaton.InitialState,
                    finalStates: automaton.FinalStates,
                    transitions: automaton.Transitions),
            ConstraintProgrammingConstraint.Reservoir reservoir =>
                new ConstraintProgrammingConstraint.Reservoir(
                    events: reservoir.Events.Select(evt =>
                        new ConstraintProgrammingConstraint.Reservoir.Event(
                            evt.Time.BindStableVariableIds(bindings),
                            evt.LevelChange.BindStableVariableIds(bindings))),
                    initialLevel: reservoir.InitialLevel,
                    minimumLevel: reservoir.MinimumLevel,
                    maximumLevel: reservoir.MaximumLevel),
            NoOverlap noOverlap =>
                NoOverlap.Create(noOverlap.Intervals.Select(interval =>
                    interval.BindStableVariableIds(bindings))).Value,
            Cumulative cumulative =>
                Cumulative.Create(
                    intervals: cumulative.Intervals.Select(interval =>
                        interval.BindStableVariableIds(bindings)),
                    demands: cumulative.Demands.Select(demand =>
                        demand.BindStableVariableIds(bindings)),
                    capacity: cumulative.Capacity.BindStableVariableIds(bindings)).Value,
            _ => constraint,
        };
    }
}
