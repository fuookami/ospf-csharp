#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Operation;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Core.Tests.Solver.ConstraintProgramming;

public class ConstraintProgrammingLowererTest {
    [Fact]
    public void DomainBoundsShouldBeExactAndRejectValuesOutsideFlt64Integers() {
        using var model = new ConstraintProgrammingModel("lower-domain-bounds");
        var dense = new IntVar("dense");
        var singleton = new IntVar("singleton");
        VariableId denseId = model.RegisterVariable(
            dense,
            IntegerDomain.CreateInterval(
                lowerBound: new Int64(2),
                upperBound: new Int64(3)).Value).Value;
        VariableId singletonId = model.RegisterVariable(
            singleton,
            IntegerDomain.CreateSingleton(new Int64(7))).Value;

        using ConstraintProgrammingLoweredLinearModel lowered =
            Lower(model);

        lowered.Variables[denseId].LowerBound!.Value.Unwrap().ToDouble().Should().Be(2.0);
        lowered.Variables[denseId].UpperBound!.Value.Unwrap().ToDouble().Should().Be(3.0);
        lowered.Variables[singletonId].LowerBound!.Value.Unwrap().ToDouble().Should().Be(7.0);
        lowered.Variables[singletonId].UpperBound!.Value.Unwrap().ToDouble().Should().Be(7.0);

        using var largeModel = new ConstraintProgrammingModel("large-bound");
        var large = new IntVar("large");
        largeModel.RegisterVariable(
            large,
            IntegerDomain.CreateInterval(
                lowerBound: Int64.Zero,
                upperBound: new Int64(long.MaxValue)).Value).IsOk.Should().BeTrue();

        new ConstraintProgrammingToLinearModelLowerer()
            .Lower(largeModel)
            .IsFailed.Should().BeTrue();
    }

    [Fact]
    public void EmptyBooleanConstraintsShouldLowerToFalseRows() {
        using var model = new ConstraintProgrammingModel("empty-boolean-constraints");
        model.AddConstraint(
            ConstraintProgrammingConstraint.CreateBoolOr(
                Array.Empty<BooleanLiteral>()).Value).IsOk.Should().BeTrue();
        model.AddConstraint(
            ConstraintProgrammingConstraint.CreateBoolXor(
                Array.Empty<BooleanLiteral>()).Value).IsOk.Should().BeTrue();

        using ConstraintProgrammingLoweredLinearModel lowered =
            Lower(model);

        lowered.Model.RelationConstraints.Should().HaveCountGreaterThanOrEqualTo(2);
        lowered.Model.RelationConstraints.Should().OnlyContain(
            row => row.Inequality.IsSatisfied(
                new Dictionary<ISymbol, Flt64>()) == false);
    }

    [Fact]
    public void LogicAndReificationShouldMatchCpAcrossSmallDomains() {
        using var model = new ConstraintProgrammingModel(
            name: "lower-logic",
            objectCategory: ObjectCategory.Minimum);
        var x = new IntVar("x");
        var a = new BinVar("a");
        var b = new BinVar("b");
        ConstraintProgrammingExpression xExpression = Register(
            model,
            x,
            IntegerDomain.CreateInterval(
                lowerBound: Int64.Zero,
                upperBound: new Int64(4)).Value);
        Register(model, a, IntegerDomain.Boolean);
        Register(model, b, IntegerDomain.Boolean);
        BooleanLiteral aLiteral = new BooleanLiteral.VariableLiteral(a);
        BooleanLiteral bLiteral = new BooleanLiteral.VariableLiteral(b);
        ConstraintProgrammingConstraint comparison =
            ConstraintProgrammingConstraint.CreateGreaterOrEqual(
                xExpression,
                new Int64(2)).Value;
        model.AddConstraint(comparison).IsOk.Should().BeTrue();
        model.AddConstraint(
            ConstraintProgrammingConstraint.CreateBoolAnd(
                new[] { aLiteral, new BooleanLiteral.VariableLiteral(b, true) }).Value)
            .IsOk.Should().BeTrue();
        model.AddConstraint(
            ConstraintProgrammingConstraint.CreateBoolOr(
                new[] { aLiteral, bLiteral }).Value).IsOk.Should().BeTrue();
        model.AddConstraint(
            ConstraintProgrammingConstraint.CreateBoolXor(
                new[] { aLiteral, bLiteral }).Value).IsOk.Should().BeTrue();
        model.AddConstraint(
            ConstraintProgrammingConstraint.CreateImplication(
                enforcement: aLiteral,
                constraint: ConstraintProgrammingConstraint.CreateLessOrEqual(
                    xExpression,
                    new Int64(3)).Value).Value).IsOk.Should().BeTrue();
        model.AddConstraint(
            ConstraintProgrammingConstraint.CreateReified(
                literal: bLiteral,
                constraint: ConstraintProgrammingConstraint.CreateEqual(
                    xExpression,
                    new Int64(2)).Value,
                direction: ReificationDirection.Equivalent).Value).IsOk.Should().BeTrue();
        model.Minimize(xExpression).IsOk.Should().BeTrue();

        using ConstraintProgrammingLoweredLinearModel lowered =
            Lower(model);

        AssertLoweringMatchesModel(model, lowered);
        lowered.Variables.Should().HaveCount(3);
        string[] sourceIds = model.Snapshot().Value.Variables
            .Select(variable => variable.Id.Value)
            .ToArray();
        lowered.Artifacts.Values.Should().Contain(
            artifact => artifact.Role == "source-variable" &&
                        sourceIds.Contains(artifact.OriginId));
        lowered.Artifacts.Values.Should().Contain(
            artifact => artifact.Role == "auxiliary-variable");
        lowered.Artifacts.Values.Should().Contain(
            artifact => artifact.Role == "compiled-constraint");
        lowered.Artifacts.Keys.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void SparseGlobalTablesAndNoOverlapShouldMatchCpAcrossSmallDomains() {
        using var model = new ConstraintProgrammingModel(
            name: "lower-global",
            objectCategory: ObjectCategory.Minimum);
        var x = new IntVar("x");
        var y = new IntVar("y");
        var index = new IntVar("index");
        var target = new IntVar("target");
        ConstraintProgrammingExpression xExpression = Register(
            model,
            x,
            IntegerDomain.CreateValues(
                new[] { new Int64(1), new Int64(3), new Int64(5) }).Value);
        ConstraintProgrammingExpression yExpression = Register(
            model,
            y,
            IntegerDomain.CreateInterval(
                lowerBound: Int64.Zero,
                upperBound: new Int64(6)).Value);
        ConstraintProgrammingExpression indexExpression = Register(
            model,
            index,
            IntegerDomain.CreateInterval(
                lowerBound: Int64.Zero,
                upperBound: new Int64(2)).Value);
        ConstraintProgrammingExpression targetExpression = Register(
            model,
            target,
            IntegerDomain.CreateInterval(
                lowerBound: Int64.Zero,
                upperBound: new Int64(30)).Value);
        model.AddConstraint(
            ConstraintProgrammingConstraint.CreateAllDifferent(
                new[] { xExpression, yExpression }).Value).IsOk.Should().BeTrue();
        model.AddConstraint(
            ConstraintProgrammingConstraint.CreateElement(
                index: indexExpression,
                values: new[] {
                    ConstraintProgrammingExpression.CreateConstant(10),
                    ConstraintProgrammingExpression.CreateConstant(20),
                    ConstraintProgrammingExpression.CreateConstant(30),
                },
                target: targetExpression).Value).IsOk.Should().BeTrue();
        model.AddConstraint(
            ConstraintProgrammingConstraint.CreateAllowedAssignments(
                expressions: new[] { xExpression, yExpression },
                tuples: new[] {
                    new[] { new Int64(1), new Int64(2) },
                    new[] { new Int64(3), new Int64(4) },
                    new[] { new Int64(5), new Int64(6) },
                }).Value).IsOk.Should().BeTrue();
        model.AddConstraint(
            ConstraintProgrammingConstraint.CreateForbiddenAssignments(
                expressions: new[] { yExpression },
                tuples: new[] { new[] { Int64.Zero } }).Value).IsOk.Should().BeTrue();

        using ConstraintProgrammingLoweredLinearModel lowered =
            Lower(model);

        AssertLoweringMatchesModel(model, lowered);
        lowered.Variables.Should().HaveCount(4);
        lowered.Model.RelationConstraints.Should().HaveCountGreaterThanOrEqualTo(12);
    }

    [Fact]
    public void OptionalAndVariableDurationIntervalsShouldKeepHalfOpenLinkSemantics() {
        using var model = new ConstraintProgrammingModel("optional-interval");
        var firstStart = new IntVar("first-start");
        var firstEnd = new IntVar("first-end");
        var secondStart = new IntVar("second-start");
        var secondEnd = new IntVar("second-end");
        var present = new BinVar("present");
        ConstraintProgrammingExpression firstStartExpression = Register(
            model,
            firstStart,
            IntegerDomain.CreateInterval(
                lowerBound: new Int64(0),
                upperBound: new Int64(1)).Value);
        ConstraintProgrammingExpression firstEndExpression = Register(
            model,
            firstEnd,
            IntegerDomain.CreateInterval(
                lowerBound: new Int64(1),
                upperBound: new Int64(2)).Value);
        ConstraintProgrammingExpression secondStartExpression = Register(
            model,
            secondStart,
            IntegerDomain.CreateInterval(
                lowerBound: new Int64(0),
                upperBound: new Int64(1)).Value);
        ConstraintProgrammingExpression secondEndExpression = Register(
            model,
            secondEnd,
            IntegerDomain.CreateInterval(
                lowerBound: new Int64(1),
                upperBound: new Int64(2)).Value);
        Register(model, present, IntegerDomain.Boolean);
        IntervalVariable first = IntervalVariable.CreateFixed(
            id: "first",
            start: firstStartExpression,
            size: new Int64(1),
            end: firstEndExpression).Value;
        IntervalVariable second = IntervalVariable.CreateFixed(
            id: "second",
            start: secondStartExpression,
            size: new Int64(1),
            end: secondEndExpression,
            presence: new BooleanLiteral.VariableLiteral(present)).Value;
        model.RegisterInterval(first).IsOk.Should().BeTrue();
        model.RegisterInterval(second).IsOk.Should().BeTrue();
        model.AddConstraint(
            NoOverlap.Create(new[] { first, second }).Value).IsOk.Should().BeTrue();

        using ConstraintProgrammingLoweredLinearModel lowered =
            Lower(model);

        AssertLoweringMatchesModel(model, lowered);
        lowered.Model.RelationConstraints.Should().NotContain(
            row => row.Name == "cp-interval-optional-link");
        lowered.Model.RelationConstraints.Should().Contain(
            row => row.Name == "cp-interval-second-link-upper");

        using var variableDurationModel =
            new ConstraintProgrammingModel("variable-duration");
        var start = new IntVar("start");
        var size = new IntVar("size");
        var end = new IntVar("end");
        ConstraintProgrammingExpression startExpression = Register(
            variableDurationModel,
            start,
            IntegerDomain.CreateInterval(
                lowerBound: Int64.Zero,
                upperBound: new Int64(10)).Value);
        ConstraintProgrammingExpression sizeExpression = Register(
            variableDurationModel,
            size,
            IntegerDomain.CreateInterval(
                lowerBound: new Int64(1),
                upperBound: new Int64(4)).Value);
        ConstraintProgrammingExpression endExpression = Register(
            variableDurationModel,
            end,
            IntegerDomain.CreateInterval(
                lowerBound: Int64.Zero,
                upperBound: new Int64(14)).Value);
        IntervalVariable variableDuration = IntervalVariable.Create(
            id: "variable-duration",
            start: startExpression,
            size: sizeExpression,
            end: endExpression).Value;
        variableDurationModel.RegisterInterval(variableDuration).IsOk.Should().BeTrue();

        using ConstraintProgrammingLoweredLinearModel durationLowered =
            Lower(variableDurationModel);
        AssertLoweringMatchesModel(variableDurationModel, durationLowered);
    }

    [Fact]
    public void StableArtifactsShouldBeOrderIndependentAndPolicyAndFixedValuesShouldBeExact() {
        using ConstraintProgrammingModel first = StableProjectionModel(reverse: false);
        using ConstraintProgrammingModel rebuilt = StableProjectionModel(reverse: true);
        using ConstraintProgrammingLoweredLinearModel firstLowered =
            Lower(first);
        using ConstraintProgrammingLoweredLinearModel rebuiltLowered =
            Lower(rebuilt);
        string[] firstProjection = firstLowered.Artifacts.Values
            .Where(artifact => artifact.OriginId is not null)
            .Select(artifact => $"{artifact.Role}|{artifact.OriginId}")
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        string[] rebuiltProjection = rebuiltLowered.Artifacts.Values
            .Where(artifact => artifact.OriginId is not null)
            .Select(artifact => $"{artifact.Role}|{artifact.OriginId}")
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        firstProjection.Should().Equal(rebuiltProjection);
        firstProjection.Should().Contain("source-variable|stable:x");
        firstProjection.Should().Contain("source-variable|stable:y");
        firstProjection.Should().Contain("compiled-constraint|stable:x-lower");
        firstProjection.Should().Contain("compiled-constraint|stable:y-lower");

        using var sparseModel = new ConstraintProgrammingModel("sparse-limit");
        var sparseVariable = new IntVar("value");
        ConstraintProgrammingExpression sparseExpression = Register(
            sparseModel,
            sparseVariable,
            IntegerDomain.CreateValues(
                new[] { new Int64(0), new Int64(1), new Int64(2), new Int64(3) }).Value);
        sparseModel.AddConstraint(
            ConstraintProgrammingConstraint.CreateEqual(
                sparseExpression,
                Int64.Zero).Value).IsOk.Should().BeTrue();
        var policy = new ConstraintProgrammingLoweringPolicy(SparseDomainLimit: 2);
        new ConstraintProgrammingToLinearModelLowerer(policy)
            .Lower(sparseModel)
            .IsFailed.Should().BeTrue();

        using var fixedModel = new ConstraintProgrammingModel("fixed-value");
        var fixedVariable = new IntVar("fixed");
        VariableId fixedId = fixedModel.RegisterVariable(
            fixedVariable,
            IntegerDomain.CreateInterval(
                lowerBound: Int64.Zero,
                upperBound: new Int64(3)).Value).Value;
        using ConstraintProgrammingLoweredLinearModel fixedLowered = Lower(
            fixedModel,
            new Dictionary<VariableId, Int64> {
                [fixedId] = new Int64(2),
            });
        fixedLowered.Model.RelationConstraints.Should().Contain(
            row => row.Name == $"cp-fixed-{Sanitize(fixedId.Value)}");
    }

    private static ConstraintProgrammingLoweredLinearModel Lower(
        ConstraintProgrammingModel model,
        IReadOnlyDictionary<VariableId, Int64>? fixedValues = null,
        ConstraintProgrammingLoweringPolicy? policy = null) {
        Result<
            ConstraintProgrammingLoweredLinearModel,
            ErrorCode,
            Error<ErrorCode>> result =
            new ConstraintProgrammingToLinearModelLowerer(policy).Lower(
                model,
                fixedValues: fixedValues);
        result.IsOk.Should().BeTrue(
            result is Failed<
                ConstraintProgrammingLoweredLinearModel,
                ErrorCode,
                Error<ErrorCode>> failure
                ? failure.Error.Message
                : "CP lowering failed");
        return result.Value;
    }

    private static ConstraintProgrammingExpression Register(
        ConstraintProgrammingModel model,
        IVariableItem variable,
        IntegerDomain domain) {
        model.RegisterVariable(variable, domain).IsOk.Should().BeTrue();
        return ConstraintProgrammingExpression.CreateVariable(
            variable,
            domain).Value;
    }

    private static void AssertLoweringMatchesModel(
        ConstraintProgrammingModel model,
        ConstraintProgrammingLoweredLinearModel lowered) {
        ConstraintProgrammingModelSnapshot snapshot = model.Snapshot().Value;
        foreach (Dictionary<VariableId, Int64> assignment in
                 EnumerateAssignments(snapshot.Variables)) {
            bool expected = IsCpFeasible(snapshot, assignment);
            var fixedValues = new Dictionary<ISymbol, Flt64>();
            foreach (ConstraintProgrammingVariableSnapshot variable in
                     snapshot.Variables) {
                fixedValues[lowered.Variables[variable.Id]] =
                    new Flt64(assignment[variable.Id].ToLong());
            }

            HasFeasibleAuxiliaryAssignment(lowered, fixedValues)
                .Should()
                .Be(expected, string.Join(
                    ", ",
                    assignment.Select(pair => $"{pair.Key.Value}={pair.Value}")));
        }
    }

    private static IEnumerable<Dictionary<VariableId, Int64>> EnumerateAssignments(
        IReadOnlyList<ConstraintProgrammingVariableSnapshot> variables) =>
        EnumerateAssignments(
            variables,
            index: 0,
            assignment: new Dictionary<VariableId, Int64>());

    private static IEnumerable<Dictionary<VariableId, Int64>> EnumerateAssignments(
        IReadOnlyList<ConstraintProgrammingVariableSnapshot> variables,
        int index,
        Dictionary<VariableId, Int64> assignment) {
        if (index == variables.Count) {
            yield return new Dictionary<VariableId, Int64>(assignment);
            yield break;
        }

        ConstraintProgrammingVariableSnapshot variable = variables[index];
        long lower = variable.Domain.LowerBound.ToLong();
        long upper = variable.Domain.UpperBound.ToLong();
        for (long raw = lower; raw <= upper; ++raw) {
            var value = new Int64(raw);
            if (!variable.Domain.Contains(value)) {
                continue;
            }
            assignment[variable.Id] = value;
            foreach (Dictionary<VariableId, Int64> complete in
                     EnumerateAssignments(variables, index + 1, assignment)) {
                yield return complete;
            }
        }
    }

    private static bool IsCpFeasible(
        ConstraintProgrammingModelSnapshot snapshot,
        IReadOnlyDictionary<VariableId, Int64> assignment) {
        foreach (IntervalVariable interval in snapshot.Intervals) {
            if (interval.Evaluate(assignment).IsFailed) {
                return false;
            }
        }
        foreach (ConstraintProgrammingConstraintSnapshot constraint in
                 snapshot.Constraints) {
            Result<bool, ErrorCode, Error<ErrorCode>> satisfied =
                constraint.Constraint.IsSatisfied(assignment);
            if (satisfied.IsFailed || !satisfied.Value) {
                return false;
            }
        }
        return true;
    }

    private static bool HasFeasibleAuxiliaryAssignment(
        ConstraintProgrammingLoweredLinearModel lowered,
        IReadOnlyDictionary<ISymbol, Flt64> fixedValues) {
        IReadOnlyList<LinearInequalityConstraint<Flt64>> rows =
            lowered.Model.RelationConstraints;
        var symbols = rows
            .SelectMany(row =>
                row.Inequality.Lhs.Monomials.Select(monomial => monomial.Symbol)
                    .Concat(row.Inequality.Rhs.Monomials.Select(
                        monomial => monomial.Symbol)))
            .Distinct()
            .ToList();
        var auxiliaries = symbols
            .Where(symbol => !fixedValues.ContainsKey(symbol))
            .ToList();
        auxiliaries.Any(symbol => symbol is not BinVar).Should().BeFalse();
        long assignmentCount = 1L << auxiliaries.Count;
        for (long mask = 0; mask < assignmentCount; ++mask) {
            var values = new Dictionary<ISymbol, Flt64>(fixedValues);
            for (int index = 0; index < auxiliaries.Count; ++index) {
                values[auxiliaries[index]] =
                    new Flt64((mask & (1L << index)) == 0 ? 0.0 : 1.0);
            }
            if (rows.All(row => row.Inequality.IsSatisfied(values) == true)) {
                return true;
            }
        }
        return false;
    }

    private static ConstraintProgrammingModel StableProjectionModel(bool reverse) {
        var model = new ConstraintProgrammingModel(
            name: "stable-projection",
            objectCategory: ObjectCategory.Minimum);
        var x = new IntVar("duplicate-display");
        var y = new IntVar("duplicate-display");
        (VariableId Id, IntVar Variable)[] variables = new[] {
            (Id: new VariableId("stable:x"), Variable: x),
            (Id: new VariableId("stable:y"), Variable: y),
        };
        IEnumerable<(VariableId Id, IntVar Variable)> orderedVariables =
            reverse ? variables.Reverse() : variables;
        foreach ((VariableId id, IntVar variable) in orderedVariables) {
            model.RegisterVariable(
                id: id,
                variable: variable,
                domain: IntegerDomain.CreateInterval(
                    lowerBound: Int64.Zero,
                    upperBound: new Int64(1)).Value,
                scope: ModelElementScope.Stable,
                origin: $"fixture/{id.Value}").IsOk.Should().BeTrue();
        }

        (ConstraintId Id, VariableId VariableId, IntVar Variable)[] constraints = new[] {
            (
                Id: new ConstraintId("stable:x-lower"),
                VariableId: new VariableId("stable:x"),
                Variable: x
            ),
            (
                Id: new ConstraintId("stable:y-lower"),
                VariableId: new VariableId("stable:y"),
                Variable: y
            ),
        };
        IEnumerable<(
            ConstraintId Id,
            VariableId VariableId,
            IntVar Variable)> orderedConstraints =
            reverse ? constraints.Reverse() : constraints;
        foreach ((
                     ConstraintId id,
                     VariableId variableId,
                     IntVar variable) in orderedConstraints) {
            model.AddConstraint(
                constraint: ConstraintProgrammingConstraint.CreateGreaterOrEqual(
                    new ConstraintProgrammingExpression.Variable(
                        variable,
                        IntegerDomain.CreateInterval(
                            lowerBound: Int64.Zero,
                            upperBound: new Int64(1)).Value,
                        variableId),
                    Int64.Zero).Value,
                id: id,
                name: "duplicate-display",
                scope: ModelElementScope.Stable,
                origin: $"fixture/{id.Value}").IsOk.Should().BeTrue();
        }
        model.Minimize(
            expression: new ConstraintProgrammingExpression.Variable(
                x,
                IntegerDomain.CreateInterval(
                    lowerBound: Int64.Zero,
                    upperBound: new Int64(1)).Value,
                new VariableId("stable:x")),
            id: new ObjectiveId("stable:objective"),
            name: "duplicate-display",
            scope: ModelElementScope.Stable,
            origin: "fixture/stable:objective").IsOk.Should().BeTrue();
        return model;
    }

    private static string Sanitize(string value) =>
        new(value.Select(character =>
            char.IsLetterOrDigit(character) || character == '_' || character == '-'
                ? character
                : '_').ToArray());
}
