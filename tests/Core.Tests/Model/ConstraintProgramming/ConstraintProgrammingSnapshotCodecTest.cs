#nullable enable

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Xunit;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Core.Tests.Model.ConstraintProgramming;

public class ConstraintProgrammingSnapshotCodecTest {
    [Fact]
    public void SnapshotShouldRoundTripCanonicalizeAndBeIdempotent() {
        SnapshotFixture fixture = BuildSnapshot(reverseRegistration: false);

        string encoded = ConstraintProgrammingSnapshotCodec.Encode(fixture.Snapshot).Value;
        ConstraintProgrammingSnapshotCodec.Validate(encoded).IsOk.Should().BeTrue();
        ConstraintProgrammingSnapshotCodec.Canonicalize(encoded).Value.Should().Be(encoded);

        using var document = JsonDocument.Parse(encoded);
        document.RootElement.GetProperty("variables")
            .EnumerateArray()
            .Select(variable => variable.GetProperty("id").GetString())
            .Should().Equal("source:a", "source:z");
        document.RootElement.GetProperty("expressions")
            .EnumerateArray()
            .Select(expression => expression.GetProperty("name").GetString())
            .Should().Equal("alpha", "zeta");
        document.RootElement.GetProperty("constraints")
            .EnumerateArray()
            .Select(constraint => constraint.GetProperty("id").GetString())
            .Should().Equal("constraint:a", "constraint:z");
        document.RootElement.GetProperty("intervals")
            .EnumerateArray()
            .Select(interval => interval.GetProperty("id").GetString())
            .Should().Equal("interval:work");

        ConstraintProgrammingModelSnapshot decoded =
            ConstraintProgrammingSnapshotCodec.Decode(
                encoded,
                fixture.VariableBindings).Value;
        string reencoded = ConstraintProgrammingSnapshotCodec.Encode(decoded).Value;

        reencoded.Should().Be(encoded);
        decoded.Variables.Select(variable => variable.Id.Value)
            .Should().Equal("source:a", "source:z");
        decoded.Variables.Single(variable => variable.Id.Value == "source:z")
            .Scope.Should().Be(ModelElementScope.Stable);
        decoded.Variables.Single(variable => variable.Id.Value == "source:z")
            .Origin.Should().Be("fixture/z");
    }

    [Fact]
    public void SnapshotEncodingShouldIgnoreRegistrationOrder() {
        SnapshotFixture forward = BuildSnapshot(reverseRegistration: false);
        SnapshotFixture reverse = BuildSnapshot(reverseRegistration: true);

        string first = ConstraintProgrammingSnapshotCodec.Encode(forward.Snapshot).Value;
        string second = ConstraintProgrammingSnapshotCodec.Encode(reverse.Snapshot).Value;

        second.Should().Be(first);
    }

    private static SnapshotFixture BuildSnapshot(bool reverseRegistration) {
        using var model = new ConstraintProgrammingModel(
            name: "canonical-snapshot",
            objectCategory: ObjectCategory.Minimum);
        var a = new IntVar("a");
        var z = new IntVar("z");
        var aId = new VariableId("source:a");
        var zId = new VariableId("source:z");
        IntegerDomain aDomain = IntegerDomain.Boolean;
        IntegerDomain zDomain = IntegerDomain.CreateInterval(
            Int64.Zero,
            new Int64(3)).Value;
        var aExpression = new ConstraintProgrammingExpression.Variable(
            a,
            aDomain,
            aId);
        var zExpression = new ConstraintProgrammingExpression.Variable(
            z,
            zDomain,
            zId);
        ConstraintProgrammingExpression.Linear zEnd =
            ConstraintProgrammingExpression.Add(
            zExpression,
            ConstraintProgrammingExpression.CreateConstant(1)).Value;

        RegisterVariables();
        RegisterExpressions();
        RegisterInterval();
        RegisterConstraints();
        model.Minimize(
            expression: zExpression,
            id: new ObjectiveId("objective:main")).IsOk.Should().BeTrue();

        return new SnapshotFixture(
            Snapshot: model.Snapshot().Value,
            VariableBindings: new Dictionary<VariableId, IVariableItem> {
                [aId] = a,
                [zId] = z,
            });

        void RegisterVariables() {
            if (reverseRegistration) {
                RegisterZ();
                RegisterA();
            }
            else {
                RegisterA();
                RegisterZ();
            }
        }

        void RegisterA() {
            model.RegisterVariable(
                id: aId,
                variable: a,
                domain: aDomain,
                scope: ModelElementScope.Stable,
                origin: "fixture/a").IsOk.Should().BeTrue();
        }

        void RegisterZ() {
            model.RegisterVariable(
                id: zId,
                variable: z,
                domain: zDomain,
                scope: ModelElementScope.Stable,
                origin: "fixture/z").IsOk.Should().BeTrue();
        }

        void RegisterExpressions() {
            if (reverseRegistration) {
                RegisterZeta();
                RegisterAlpha();
            }
            else {
                RegisterAlpha();
                RegisterZeta();
            }
        }

        void RegisterAlpha() {
            model.RegisterExpression("alpha", aExpression).IsOk.Should().BeTrue();
        }

        void RegisterZeta() {
            model.RegisterExpression("zeta", zExpression).IsOk.Should().BeTrue();
        }

        void RegisterInterval() {
            IntervalVariable interval = IntervalVariable.CreateFixed(
                id: "interval:work",
                start: zExpression,
                size: new Int64(1),
                end: zEnd).Value;
            model.RegisterInterval(interval).IsOk.Should().BeTrue();
        }

        void RegisterConstraints() {
            ConstraintProgrammingConstraint.IntegerComparison aConstraint =
                ConstraintProgrammingConstraint.CreateEqual(
                aExpression,
                Int64.Zero).Value;
            ConstraintProgrammingConstraint.IntegerComparison zConstraint =
                ConstraintProgrammingConstraint.CreateGreaterOrEqual(
                zExpression,
                Int64.One).Value;
            if (reverseRegistration) {
                model.AddConstraint(zConstraint, "constraint:z").IsOk.Should().BeTrue();
                model.AddConstraint(aConstraint, "constraint:a").IsOk.Should().BeTrue();
            }
            else {
                model.AddConstraint(aConstraint, "constraint:a").IsOk.Should().BeTrue();
                model.AddConstraint(zConstraint, "constraint:z").IsOk.Should().BeTrue();
            }
        }
    }

    private sealed record SnapshotFixture(
        ConstraintProgrammingModelSnapshot Snapshot,
        IReadOnlyDictionary<VariableId, IVariableItem> VariableBindings);
}
