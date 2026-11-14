#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Core.Tests.Model.ConstraintProgramming;

public class ConstraintProgrammingModelTest {
    [Fact]
    public void SnapshotShouldPreserveRegistrationOrderAndFreezeModelState() {
        var first = new IntVar("first");
        var second = new IntVar("second");
        using var model = new ConstraintProgrammingModel(
            name: "ordered",
            objectCategory: ObjectCategory.Minimum);
        VariableId firstId = model.RegisterVariable(first).Value;
        VariableId secondId = model.RegisterVariable(second).Value;
        ConstraintProgrammingExpression firstExpression =
            ConstraintProgrammingExpression.CreateVariable(first).Value;
        ConstraintProgrammingExpression secondExpression =
            ConstraintProgrammingExpression.CreateVariable(second).Value;
        model.AddConstraint(
            constraint: ConstraintProgrammingConstraint.CreateEqual(
                firstExpression,
                new Int64(1)).Value,
            name: "first is one").IsOk.Should().BeTrue();
        model.Minimize(secondExpression, name: "minimize second").IsOk.Should().BeTrue();

        ConstraintProgrammingModelSnapshot snapshot = model.Snapshot().Value;

        snapshot.Variables.Select(variable => variable.Id).Should().Equal(firstId, secondId);
        snapshot.Constraints.Select(constraint => constraint.Name)
            .Should().Equal("first is one");
        snapshot.Objectives.Select(objective => objective.Name)
            .Should().Equal("minimize second");

        model.AddConstraint(
            constraint: ConstraintProgrammingConstraint.CreateGreaterOrEqual(
                secondExpression,
                new Int64(0)).Value,
            name: "added later").IsOk.Should().BeTrue();

        snapshot.Constraints.Should().ContainSingle();
        var variables = (IList<ConstraintProgrammingVariableSnapshot>)snapshot.Variables;
        Action add = () => variables.Add(new ConstraintProgrammingVariableSnapshot(
            id: new VariableId("injected"),
            name: "injected",
            typeName: "IntVar",
            domain: snapshot.Variables[0].Domain));
        add.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void RegistrationShouldRejectDuplicateIdsAndMultipleBindingsForOneVariable() {
        var first = new IntVar("first");
        var second = new IntVar("second");
        var model = new ConstraintProgrammingModel();
        IntegerDomain domain = IntegerDomain.CreateInterval(
            Int64.Zero,
            new Int64(10)).Value;
        Continuation constraint = CreateContinuation(first);

        model.RegisterVariable(
            id: new VariableId("first"),
            variable: first,
            domain: domain).IsOk.Should().BeTrue();
        model.RegisterVariable(
            id: new VariableId("first"),
            variable: second,
            domain: domain).IsFailed.Should().BeTrue();
        model.RegisterVariable(
            id: new VariableId("second"),
            variable: first,
            domain: domain).IsFailed.Should().BeTrue();

        model.AddConstraint(constraint.Value, id: "constraint").IsOk.Should().BeTrue();
        model.AddConstraint(constraint.Value, id: "constraint").IsFailed.Should().BeTrue();
        model.Minimize(
            expression: ConstraintProgrammingExpression.CreateVariable(first).Value,
            id: new ObjectiveId("objective")).IsOk.Should().BeTrue();
        model.Minimize(
            expression: ConstraintProgrammingExpression.CreateVariable(second).Value,
            id: new ObjectiveId("objective")).IsFailed.Should().BeTrue();
    }

    [Fact]
    public void SnapshotShouldRejectIdsRepeatedAcrossElementKinds() {
        var variable = new IntVar("variable");
        var model = new ConstraintProgrammingModel();
        model.RegisterVariable(
            id: new VariableId("shared"),
            variable: variable,
            domain: IntegerDomain.Boolean).IsOk.Should().BeTrue();
        model.AddConstraint(
            CreateContinuation(variable).Value,
            id: "shared").IsOk.Should().BeTrue();

        Result<ConstraintProgrammingModelSnapshot, ErrorCode, Error<ErrorCode>> snapshot =
            model.Snapshot();

        snapshot.IsFailed.Should().BeTrue();
        snapshot.Should().BeOfType<
            Failed<ConstraintProgrammingModelSnapshot, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.IllegalArgument);
    }

    [Fact]
    public void ValidationShouldRejectUnregisteredVariableReferences() {
        var model = new ConstraintProgrammingModel();
        ConstraintProgrammingExpression missing =
            ConstraintProgrammingExpression.CreateVariable(new IntVar("missing")).Value;
        model.AddConstraint(CreateContinuation(missing).Value).IsOk.Should().BeTrue();

        model.Validate().IsFailed.Should().BeTrue();
        model.Snapshot().IsFailed.Should().BeTrue();
    }

    [Fact]
    public void IntervalRegistrationShouldBindStableIdsAndRejectDuplicateIds() {
        var start = new IntVar("start");
        var model = new ConstraintProgrammingModel();
        VariableId startId = model.RegisterVariable(start).Value;
        ConstraintProgrammingExpression startExpression =
            ConstraintProgrammingExpression.CreateVariable(start).Value;
        ConstraintProgrammingExpression endExpression =
            ConstraintProgrammingExpression.Add(
                startExpression,
                ConstraintProgrammingExpression.CreateConstant(3)).Value;
        IntervalVariable interval = IntervalVariable.CreateFixed(
            id: "work",
            start: startExpression,
            size: new Int64(3),
            end: endExpression).Value;

        model.RegisterInterval(interval).IsOk.Should().BeTrue();
        model.RegisterInterval(interval).Should()
            .BeOfType<Failed<IntervalId, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.IllegalArgument);
        ConstraintProgrammingModelSnapshot snapshot = model.Snapshot().Value;

        snapshot.Intervals.Should().ContainSingle();
        snapshot.Intervals[0].Variables.Should().Equal(startId);
        snapshot.Interval(new IntervalId("work")).Should().NotBeNull();
    }

    [Fact]
    public void IntervalValidationShouldRejectUnregisteredReferences() {
        var model = new ConstraintProgrammingModel();
        IntervalVariable interval = IntervalVariable.CreateFixed(
            id: "work",
            start: ConstraintProgrammingExpression.CreateVariable(new IntVar("missing")).Value,
            size: new Int64(3),
            end: ConstraintProgrammingExpression.CreateLinear(
                terms: new[] {
                    new ConstraintProgrammingExpression.Term(
                        variable: new IntVar("missing"),
                        coefficient: Int64.One),
                },
                constant: new Int64(3)).Value).Value;
        model.RegisterInterval(interval).IsOk.Should().BeTrue();

        model.Validate().IsFailed.Should().BeTrue();
        model.Snapshot().IsFailed.Should().BeTrue();
    }

    [Fact]
    public void CloseShouldBeIdempotentAndRejectAllSubsequentOperations() {
        var model = new ConstraintProgrammingModel();
        var timing = new IntVar("timing");
        ConstraintProgrammingExpression.Variable value =
            ConstraintProgrammingExpression.CreateVariable(new IntVar("value")).Value;
        model.RegisterVariable(value.VariableItem).IsOk.Should().BeTrue();
        model.RegisterVariable(timing).IsOk.Should().BeTrue();
        model.AddConstraint(CreateContinuation(value).Value).IsOk.Should().BeTrue();
        IntervalVariable interval = IntervalVariable.CreateFixed(
            id: "timing",
            start: ConstraintProgrammingExpression.CreateVariable(timing).Value,
            size: new Int64(2),
            end: ConstraintProgrammingExpression.Add(
                ConstraintProgrammingExpression.CreateVariable(timing).Value,
                ConstraintProgrammingExpression.CreateConstant(2)).Value).Value;
        model.RegisterInterval(interval).IsOk.Should().BeTrue();

        model.Close();
        model.Close();

        model.IsClosed.Should().BeTrue();
        model.VariableCount.Should().Be(0);
        model.IntervalCount.Should().Be(0);
        model.ConstraintCount.Should().Be(0);
        model.RegisterVariable(new IntVar("after-close")).Should()
            .BeOfType<Failed<VariableId, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.ApplicationStopped);
        model.AddConstraint(CreateContinuation(value).Value).Should()
            .BeOfType<Failed<ConstraintId, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.ApplicationStopped);
        model.Minimize(value).Should()
            .BeOfType<Failed<ObjectiveId, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.ApplicationStopped);
        model.RegisterExpression("value", value).Should()
            .BeOfType<Failed<string, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.ApplicationStopped);
        model.RegisterInterval(interval).Should()
            .BeOfType<Failed<IntervalId, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.ApplicationStopped);
        model.Validate().Should()
            .BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.ApplicationStopped);
        model.Snapshot().Should()
            .BeOfType<Failed<
                ConstraintProgrammingModelSnapshot,
                ErrorCode,
                Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.ApplicationStopped);
    }

    [Fact]
    public void ValidationShouldRejectMultipleObjectivesAndDirectionMismatch() {
        var value = new IntVar("value");
        ConstraintProgrammingExpression expression =
            ConstraintProgrammingExpression.CreateVariable(value).Value;
        var mismatched = new ConstraintProgrammingModel(
            objectCategory: ObjectCategory.Minimum);
        mismatched.RegisterVariable(value).IsOk.Should().BeTrue();
        mismatched.AddObjective(ObjectCategory.Maximum, expression).IsOk.Should().BeTrue();

        var multiple = new ConstraintProgrammingModel(
            objectCategory: ObjectCategory.Minimum);
        multiple.RegisterVariable(value).IsOk.Should().BeTrue();
        multiple.Minimize(expression).IsOk.Should().BeTrue();
        multiple.Minimize(expression).IsOk.Should().BeTrue();

        mismatched.Validate().IsFailed.Should().BeTrue();
        mismatched.Snapshot().IsFailed.Should().BeTrue();
        multiple.Validate().IsFailed.Should().BeTrue();
        multiple.Snapshot().IsFailed.Should().BeTrue();
    }

    [Fact]
    public void IdentityScopesShouldRejectAmbiguousOrReservedOrigins() {
        var model = new ConstraintProgrammingModel(
            identityNamespace: "org.example",
            identitySchemaVersion: "1.0");
        var stable = new IntVar("stable");
        var local = new IntVar("local");
        Result<VariableId, ErrorCode, Error<ErrorCode>> stableRegistration =
            model.RegisterVariable(
                id: new VariableId("source:stable"),
                variable: stable,
                domain: IntegerDomain.Boolean,
                scope: ModelElementScope.Stable,
                origin: "fixture:stable");
        Result<VariableId, ErrorCode, Error<ErrorCode>> stableWithoutOrigin =
            model.RegisterVariable(
                id: new VariableId("source:missing-origin"),
                variable: new IntVar("missing-origin"),
                domain: IntegerDomain.Boolean,
                scope: ModelElementScope.Stable);
        Result<VariableId, ErrorCode, Error<ErrorCode>> localWithOrigin =
            model.RegisterVariable(
                id: new VariableId("local"),
                variable: local,
                domain: IntegerDomain.Boolean,
                origin: "fixture:local");
        Result<VariableId, ErrorCode, Error<ErrorCode>> reserved =
            model.RegisterVariable(
                id: new VariableId("artifact:local"),
                variable: new IntVar("reserved"),
                domain: IntegerDomain.Boolean);
        Result<VariableId, ErrorCode, Error<ErrorCode>> stableLocalPrefix =
            model.RegisterVariable(
                id: new VariableId("model-local-stable"),
                variable: new IntVar("stable-local-prefix"),
                domain: IntegerDomain.Boolean,
                scope: ModelElementScope.Stable,
                origin: "fixture:stable-local-prefix");

        stableRegistration.IsOk.Should().BeTrue();
        stableWithoutOrigin.IsFailed.Should().BeTrue();
        localWithOrigin.IsFailed.Should().BeTrue();
        reserved.IsFailed.Should().BeTrue();
        stableLocalPrefix.IsFailed.Should().BeTrue();
        ConstraintProgrammingModelSnapshot snapshot = model.Snapshot().Value;
        snapshot.Variables.Should().ContainSingle();
        snapshot.Variables[0].IdentityProvenance.Should().Contain(
            new ModelElementOrigin("legacy-origin", "fixture:stable"));
    }

    private static Continuation CreateContinuation(
        IVariableItem variable) =>
        new(ConstraintProgrammingConstraint.CreateEqual(
            ConstraintProgrammingExpression.CreateVariable(variable).Value,
            new Int64(0)).Value);

    private static Continuation CreateContinuation(
        ConstraintProgrammingExpression expression) =>
        new(ConstraintProgrammingConstraint.CreateEqual(
            expression,
            new Int64(0)).Value);

    private sealed record Continuation(ConstraintProgrammingConstraint Value);
}
