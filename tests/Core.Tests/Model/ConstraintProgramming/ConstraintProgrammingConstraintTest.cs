#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Xunit;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Core.Tests.Model.ConstraintProgramming;

public class ConstraintProgrammingConstraintTest {
    [Fact]
    public void IntegerAndBooleanConstraintsShouldEvaluateExactAssignments() {
        var amount = new IntVar("amount");
        ConstraintProgrammingExpression.Variable left =
            ConstraintProgrammingExpression.CreateVariable(amount).Value;
        ConstraintProgrammingConstraint.IntegerComparison comparison =
            ConstraintProgrammingConstraint.CreateGreaterOrEqual(left, new Int64(3)).Value;

        comparison.IsSatisfied(Values((left.VariableId, 4))).Value.Should().BeTrue();
        comparison.IsSatisfied(Values((left.VariableId, 2))).Value.Should().BeFalse();

        var first = new BooleanLiteral.VariableLiteral(new BinVar("first"));
        var second = new BooleanLiteral.VariableLiteral(new BinVar("second"));
        ConstraintProgrammingConstraint.BoolXor xor =
            ConstraintProgrammingConstraint.CreateBoolXor(new[] { first, second }).Value;

        xor.IsSatisfied(Values((first.VariableId!.Value, 1), (second.VariableId!.Value, 0)))
            .Value.Should().BeTrue();
        xor.IsSatisfied(Values((first.VariableId!.Value, 1), (second.VariableId!.Value, 1)))
            .Value.Should().BeFalse();
    }

    [Fact]
    public void BooleanConstraintsShouldShortCircuitAndRejectNonBinaryValues() {
        var first = new BooleanLiteral.VariableLiteral(new BinVar("first"));
        var missing = new BooleanLiteral.VariableLiteral(new BinVar("missing"));
        ConstraintProgrammingConstraint.BoolOr or =
            ConstraintProgrammingConstraint.CreateBoolOr(new[] { first, missing }).Value;
        ConstraintProgrammingConstraint.Literal nonBinary =
            ConstraintProgrammingConstraint.CreateLiteral(first).Value;

        or.IsSatisfied(Values((first.VariableId!.Value, 1))).Value.Should().BeTrue();
        nonBinary.IsSatisfied(Values((first.VariableId!.Value, 2))).IsFailed.Should().BeTrue();
    }

    [Fact]
    public void ImplicationAndReificationShouldRespectDirectionAndEnforcement() {
        var enforcement = new BooleanLiteral.VariableLiteral(new BinVar("enforcement"));
        ConstraintProgrammingExpression.Variable value =
            ConstraintProgrammingExpression.CreateVariable(new IntVar("value")).Value;
        ConstraintProgrammingConstraint.IntegerComparison inner =
            ConstraintProgrammingConstraint.CreateEqual(value, new Int64(7)).Value;
        ConstraintProgrammingConstraint.Implication implication = ConstraintProgrammingConstraint
            .CreateImplication(enforcement, inner).Value;
        ConstraintProgrammingConstraint.Reified equivalent = ConstraintProgrammingConstraint
            .CreateReified(
                literal: enforcement,
                constraint: inner,
                direction: ReificationDirection.Equivalent).Value;
        ConstraintProgrammingConstraint.Reified impliedBy = ConstraintProgrammingConstraint
            .CreateReified(
                literal: enforcement,
                constraint: inner,
                direction: ReificationDirection.ImpliedBy).Value;

        implication.IsSatisfied(Values((enforcement.VariableId!.Value, 0))).Value.Should().BeTrue();
        implication.IsSatisfied(
            Values(
                (enforcement.VariableId!.Value, 1),
                (value.VariableId, 6))).Value.Should().BeFalse();
        implication.IsSatisfied(
            Values(
                (enforcement.VariableId!.Value, 1),
                (value.VariableId, 7))).Value.Should().BeTrue();
        equivalent.IsSatisfied(
            Values(
                (enforcement.VariableId!.Value, 0),
                (value.VariableId, 7))).Value.Should().BeFalse();
        impliedBy.IsSatisfied(
            Values(
                (enforcement.VariableId!.Value, 0),
                (value.VariableId, 6))).Value.Should().BeTrue();
    }

    [Fact]
    public void AllDifferentElementAndTablesShouldValidateShapesAndAssignments() {
        ConstraintProgrammingExpression.Variable value =
            ConstraintProgrammingExpression.CreateVariable(new IntVar("value")).Value;
        ConstraintProgrammingExpression.Variable other =
            ConstraintProgrammingExpression.CreateVariable(new IntVar("other")).Value;
        ConstraintProgrammingConstraint.AllDifferent allDifferent = ConstraintProgrammingConstraint
            .CreateAllDifferent(new[] { value, other }).Value;
        ConstraintProgrammingExpression.Variable index =
            ConstraintProgrammingExpression.CreateVariable(new IntVar("index")).Value;
        ConstraintProgrammingExpression.Variable target =
            ConstraintProgrammingExpression.CreateVariable(new IntVar("target")).Value;
        ConstraintProgrammingConstraint.Element element = ConstraintProgrammingConstraint.CreateElement(
            index: index,
            values: new[] {
                ConstraintProgrammingExpression.CreateConstant(10),
                ConstraintProgrammingExpression.CreateConstant(20),
                ConstraintProgrammingExpression.CreateConstant(30),
            },
            target: target).Value;
        ConstraintProgrammingConstraint.AllowedAssignments allowed =
            ConstraintProgrammingConstraint.CreateAllowedAssignments(
            expressions: new[] { value, other },
            tuples: new[] {
                new[] { new Int64(0), new Int64(1) },
                new[] { new Int64(1), new Int64(0) },
            }).Value;
        ConstraintProgrammingConstraint.ForbiddenAssignments forbidden =
            ConstraintProgrammingConstraint.CreateForbiddenAssignments(
            expressions: new[] { value },
            tuples: new[] { new[] { new Int64(9) } }).Value;

        allDifferent.IsSatisfied(
            Values((value.VariableId, 1), (other.VariableId, 1))).Value.Should().BeFalse();
        element.IsSatisfied(
            Values((index.VariableId, 1), (target.VariableId, 20))).Value.Should().BeTrue();
        element.IsSatisfied(
            Values((index.VariableId, 3), (target.VariableId, 20))).Value.Should().BeFalse();
        allowed.IsSatisfied(Values((value.VariableId, 0), (other.VariableId, 1)))
            .Value.Should().BeTrue();
        forbidden.IsSatisfied(Values((value.VariableId, 9))).Value.Should().BeFalse();
        ConstraintProgrammingConstraint.CreateAllDifferent(Array.Empty<ConstraintProgrammingExpression>())
            .IsFailed.Should().BeTrue();
        ConstraintProgrammingConstraint.CreateElement(
            index: index,
            values: Array.Empty<ConstraintProgrammingExpression>(),
            target: target).IsFailed.Should().BeTrue();
        ConstraintProgrammingConstraint.CreateAllowedAssignments(
            expressions: new[] { value, other },
            tuples: new[] { new[] { new Int64(1) } }).IsFailed.Should().BeTrue();
    }

    [Fact]
    public void CircuitAutomatonAndReservoirShouldEvaluateExactBoundaries() {
        IntVar[] successors = new[] {
            new IntVar("next-0"),
            new IntVar("next-1"),
            new IntVar("next-2"),
        };
        IntegerDomain.Interval domain = IntegerDomain.CreateInterval(new Int64(0), new Int64(2)).Value;
        ConstraintProgrammingConstraint.Circuit circuit = ConstraintProgrammingConstraint.CreateCircuit(
            successors.Select(variable => ConstraintProgrammingExpression.CreateVariable(
                variable: variable,
                domain: domain).Value)).Value;
        IReadOnlyDictionary<VariableId, Int64> circuitValues = Values(
            (Id(successors[0]), 1),
            (Id(successors[1]), 2),
            (Id(successors[2]), 0));

        circuit.IsSatisfied(circuitValues).Value.Should().BeTrue();
        circuit.IsSatisfied(Values(
            (Id(successors[0]), 1),
            (Id(successors[1]), 2),
            (Id(successors[2]), 1))).Value.Should().BeFalse();

        ConstraintProgrammingConstraint.Automaton automaton =
            ConstraintProgrammingConstraint.CreateAutomaton(
            expressions: new[] {
                ConstraintProgrammingExpression.CreateConstant(0),
                ConstraintProgrammingExpression.CreateConstant(1),
            },
            initialState: 0,
            finalStates: new[] { 2 },
            transitions: new[] {
                new ConstraintProgrammingConstraint.AutomatonTransition(
                    fromState: 0,
                    value: new Int64(0),
                    toState: 1),
                new ConstraintProgrammingConstraint.AutomatonTransition(
                    fromState: 1,
                    value: new Int64(1),
                    toState: 2),
            }).Value;
        automaton.IsSatisfied(Values()).Value.Should().BeTrue();
        ConstraintProgrammingConstraint.CreateAutomaton(
            expressions: new[] { ConstraintProgrammingExpression.CreateConstant(0) },
            initialState: 0,
            finalStates: new[] { 1 },
            transitions: new[] {
                new ConstraintProgrammingConstraint.AutomatonTransition(
                    fromState: 0,
                    value: new Int64(0),
                    toState: 1),
                new ConstraintProgrammingConstraint.AutomatonTransition(
                    fromState: 0,
                    value: new Int64(0),
                    toState: 2),
            }).IsFailed.Should().BeTrue();

        ConstraintProgrammingConstraint.Reservoir reservoir =
            ConstraintProgrammingConstraint.CreateReservoir(
            events: new[] {
                new ConstraintProgrammingConstraint.Reservoir.Event(
                    ConstraintProgrammingExpression.CreateConstant(3),
                    ConstraintProgrammingExpression.CreateConstant(-1)),
                new ConstraintProgrammingConstraint.Reservoir.Event(
                    ConstraintProgrammingExpression.CreateConstant(3),
                    ConstraintProgrammingExpression.CreateConstant(1)),
            },
            initialLevel: Int64.Zero,
            minimumLevel: Int64.Zero,
            maximumLevel: new Int64(1)).Value;

        reservoir.IsSatisfied(Values()).Value.Should().BeTrue();
        ConstraintProgrammingConstraint.CreateReservoir(
            events: new[] {
                new ConstraintProgrammingConstraint.Reservoir.Event(
                    ConstraintProgrammingExpression.CreateConstant(1),
                    ConstraintProgrammingExpression.CreateConstant(1)),
            },
            initialLevel: new Int64(2),
            minimumLevel: Int64.Zero,
            maximumLevel: new Int64(1)).IsFailed.Should().BeTrue();
    }

    [Fact]
    public void CollectionInputsShouldBeFrozenAtConstruction() {
        List<ConstraintProgrammingExpression> values = new() {
            ConstraintProgrammingExpression.CreateConstant(1),
        };
        ConstraintProgrammingConstraint.AllDifferent allDifferent =
            ConstraintProgrammingConstraint.CreateAllDifferent(values).Value;

        values.Clear();

        allDifferent.Expressions.Should().HaveCount(1);
        allDifferent.IsSatisfied(Values()).Value.Should().BeTrue();
    }

    private static IReadOnlyDictionary<VariableId, Int64> Values(
        params (VariableId Id, long Value)[] values) =>
        values.ToDictionary(pair => pair.Id, pair => new Int64(pair.Value));

    private static VariableId Id(IVariableItem variable) =>
        new($"{variable.Identifier}:{variable.Index}");
}
