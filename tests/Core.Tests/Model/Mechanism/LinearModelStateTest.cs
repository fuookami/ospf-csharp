#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Model.Mechanism;

public class LinearModelStateTest {
    [Fact]
    public void DynamicRowsAreCanonicalIdempotentAndDetachedFromCallerExpressions() {
        var identities = new ModelElementIdentityRegistry();
        var model = new LinearMetaModel<Flt64>(
            name: "rows",
            identityRegistry: identities);
        var x = new BinVar("x");
        model.Add(x).IsOk.Should().BeTrue();

        var terms = new List<LinearMonomial<Flt64>>
        {
            new(Flt64.One, x),
        };
        var expression = new LinearPolynomial<Flt64>(terms, Flt64.Zero);
        LinearInequality<Flt64> relation = expression.Le(Flt64.One);

        model.State.RegisterRow(new ConstraintId("business"), relation).IsOk.Should().BeTrue();
        model.State.RegisterRow(new ConstraintId("business"), relation).IsOk.Should().BeTrue();
        model.Constraints.Should().HaveCount(1);
        identities.Identity(model.Constraints.Single())!.Id.Value.Should().Be("business");

        model.State.RegisterRow(
            new ConstraintId("business"),
            expression.Le(Flt64.Zero)).IsOk.Should().BeFalse();
        model.State.RegisterRow(
            new ConstraintId("unknown"),
            new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, new BinVar("unknown")) },
                Flt64.Zero).Le(Flt64.One)).IsOk.Should().BeFalse();
        model.State.RegisterRow(
            new ConstraintId("nan"),
            new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(new Flt64(double.NaN), x) },
                Flt64.Zero).Le(Flt64.One)).IsOk.Should().BeFalse();
        model.State.RegisterRow(
            new ConstraintId("model-local-bad"),
            relation).IsOk.Should().BeFalse();
        model.State.RegisterRow(
            new ConstraintId("strict"),
            expression.Lt(Flt64.One)).IsOk.Should().BeFalse();

        terms.Clear();
        model.Constraints.Should().HaveCount(1);
        LinearModelSnapshot<Flt64> snapshot = model.State.Refresh().Value;
        snapshot.Rows.Should().HaveCount(1);
        snapshot.Rows[0].Coefficients[x].Should().Be(Flt64.One);
        snapshot.Rows[0].Rhs.Should().Be(Flt64.One);
        model.State.RequireCurrent(snapshot.Revision).IsOk.Should().BeTrue();
    }

    [Fact]
    public void FailedIdentityRegistrationRollsBackTheDetachedRow() {
        var identities = new ModelElementIdentityRegistry();
        var model = new LinearMetaModel<Flt64>(
            name: "rollback",
            identityRegistry: identities);
        var x = new BinVar("x");
        model.Add(x).IsOk.Should().BeTrue();
        model.AddConstraint(
            new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, x) },
                Flt64.Zero).Le(Flt64.One),
            null).IsOk.Should().BeTrue();
        identities.RegisterConstraint(
            model.Constraints.Single(),
            new ConstraintId("occupied")).IsOk.Should().BeTrue();

        LinearInequality<Flt64> row = new LinearPolynomial<Flt64>(
            new[] { new LinearMonomial<Flt64>(Flt64.One, x) },
            Flt64.Zero).Ge(Flt64.Zero);

        model.State.RegisterRow(new ConstraintId("occupied"), row).IsOk.Should().BeFalse();
        model.Constraints.Should().HaveCount(1);
        model.State.RegisterRow(new ConstraintId("available"), row).IsOk.Should().BeTrue();
        model.Constraints.Should().HaveCount(2);
    }

    [Fact]
    public void RemoveRow_ShouldRemoveOnlyTheRequestedDynamicRowAndInvalidateSnapshots() {
        using var model = new LinearMetaModel<Flt64>(name: "row-removal");
        var x = new RealVar("row-removal-x");
        model.Add(x).IsOk.Should().BeTrue();
        model.State.RegisterRow(
            id: new ConstraintId("first"),
            relation: Row(x, new Flt64(1))).IsOk.Should().BeTrue();
        model.State.RegisterRow(
            id: new ConstraintId("second"),
            relation: Row(x, new Flt64(2))).IsOk.Should().BeTrue();
        LinearModelSnapshot<Flt64> initial = model.State.Refresh().Value;
        initial.Rows.Should().HaveCount(2);

        model.State.RemoveRow(new ConstraintId("first")).IsOk.Should().BeTrue();
        model.State.RemoveRow(new ConstraintId("unknown")).IsOk.Should().BeTrue();

        LinearModelSnapshot<Flt64> updated = model.State.Refresh().Value;
        updated.Rows.Should().ContainSingle()
            .Which.Id.Should().Be("second");
        updated.Revision.Should().NotBe(initial.Revision);
        model.State.RequireCurrent(initial.Revision).IsOk.Should().BeFalse();
        model.Constraints.Should().ContainSingle();
    }

    [Fact]
    public void RemoveRow_ShouldShiftLaterConstraintGroups() {
        using var model = new LinearMetaModel<Flt64>(name: "row-group-removal");
        var x = new RealVar("row-group-x");
        var firstGroup = new TestConstraintGroup("first");
        var secondGroup = new TestConstraintGroup("second");
        model.Add(x).IsOk.Should().BeTrue();
        model.RegisterConstraintGroup(firstGroup);
        model.AddConstraint(Row(x, Flt64.One), firstGroup, name: "first-1").IsOk.Should().BeTrue();
        model.AddConstraint(Row(x, Flt64.One), firstGroup, name: "first-2").IsOk.Should().BeTrue();
        model.IndicesOfConstraintGroup(firstGroup).Should().NotBeNull();

        model.State.RegisterRow(
            id: new ConstraintId("dynamic"),
            relation: Row(x, new Flt64(2))).IsOk.Should().BeTrue();
        model.RegisterConstraintGroup(secondGroup);
        model.AddConstraint(Row(x, Flt64.One), secondGroup, name: "second-1").IsOk.Should().BeTrue();
        model.AddConstraint(Row(x, Flt64.One), secondGroup, name: "second-2").IsOk.Should().BeTrue();
        model.IndicesOfConstraintGroup(secondGroup).Should().NotBeNull();

        model.State.RemoveRow(new ConstraintId("dynamic")).IsOk.Should().BeTrue();

        model.ConstraintsOfGroup(firstGroup).Select(constraint => constraint.Name)
            .Should().Equal("first-1", "first-2");
        model.ConstraintsOfGroup(secondGroup).Select(constraint => constraint.Name)
            .Should().Equal("second-1", "second-2");
        model.Constraints.Should().HaveCount(4);
    }

    [Fact]
    public void DirectRowObjectiveAndBoundEditsInvalidateRevisionsAndNamesAreNotIdentities() {
        var model = new LinearMetaModel<Flt64>(name: "revisions");
        var x = new URealVar("x");
        model.Add(x).IsOk.Should().BeTrue();
        model.AddConstraint(
            new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, x) },
                Flt64.Zero).Le(Flt64.One),
            null,
            name: "same").IsOk.Should().BeTrue();
        model.AddObject(
            ObjectCategory.Minimum,
            new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, x) },
                Flt64.Zero),
            "objective",
            null).IsOk.Should().BeTrue();

        LinearModelSnapshot<Flt64> initial = model.State.Refresh().Value;
        model.State.Refresh().Value.Should().Be(initial);

        model.AddConstraint(
            new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, x) },
                Flt64.Zero).Ge(Flt64.One),
            null,
            name: "same").IsOk.Should().BeTrue();
        model.State.RequireCurrent(initial.Revision).IsOk.Should().BeFalse();

        LinearModelSnapshot<Flt64> withRow = model.State.Refresh().Value;
        withRow.Rows.Select(row => row.Id).Distinct(StringComparer.Ordinal).Should().HaveCount(2);

        model.AddObject(
            ObjectCategory.Minimum,
            new LinearPolynomial<Flt64>(
                Array.Empty<LinearMonomial<Flt64>>(),
                new Flt64(7)),
            "constant",
            null).IsOk.Should().BeTrue();
        model.State.RequireCurrent(withRow.Revision).IsOk.Should().BeFalse();
        LinearModelSnapshot<Flt64> withObjective = model.State.Refresh().Value;
        withObjective.ObjectiveConstant.Should().Be(new Flt64(7));

        x.Range.Leq(new InvariantFlt64Wrapper(new Flt64(3))).Should().BeTrue();
        model.State.RequireCurrent(withObjective.Revision).IsOk.Should().BeFalse();

        var other = new LinearMetaModel<Flt64>(name: "foreign");
        other.State.RequireCurrent(initial.Revision).IsOk.Should().BeFalse();
    }

    [Fact]
    public void ClosedModelsRejectMutationAndDisposeRemainsIdempotent() {
        var model = new LinearMetaModel<Flt64>(name: "closed");
        var x = new RealVar("x");
        model.Add(x).IsOk.Should().BeTrue();

        model.Close();
        model.IsClosed.Should().BeTrue();
        model.Add(new RealVar("y")).IsOk.Should().BeFalse();
        model.AddConstraint(
            new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, x) },
                Flt64.Zero).Le(Flt64.One),
            null).IsOk.Should().BeFalse();
        model.AddObject(
            ObjectCategory.Minimum,
            new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, x) },
                Flt64.Zero),
            "objective",
            null).IsOk.Should().BeFalse();
        model.Invoking(value => value.Remove(x)).Should().NotThrow();
        model.Invoking(value => value.Dispose()).Should().NotThrow();
        model.IsClosed.Should().BeTrue();
    }

    private static LinearInequality<Flt64> Row(IVariableItem variable, Flt64 rhs) => new(
        Lhs: new LinearPolynomial<Flt64>(
            Monomials: new[] { new LinearMonomial<Flt64>(Flt64.One, variable) },
            Constant: Flt64.Zero),
        Rhs: new LinearPolynomial<Flt64>(
            Monomials: Array.Empty<LinearMonomial<Flt64>>(),
            Constant: rhs),
        Comparison: Comparison.LE);

    private sealed class TestConstraintGroup(string name) : IMetaConstraintGroup {
        public bool Lazy => false;
        public string Name { get; } = name;
    }
}
