#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Xunit;
using SymbolFlatten = Fuookami.Ospf.Core.Symbol.Flatten;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Tests.Model.Mechanism;

public class MetaModelWriteTransactionTest {
    [Fact]
    public async Task LinearRollback_ShouldRestoreTokensConstraintsObjectivesRowsAndIdentities() {
        var registry = new ModelElementIdentityRegistry();
        using var model = new LinearMetaModel<Flt64>(identityRegistry: registry);
        var x = new RealVar("transaction-x");
        var y = new RealVar("transaction-y");
        model.Add(x).IsOk.Should().BeTrue();
        model.AddConstraint(
            relation: Row(x, new Flt64(1)),
            group: null,
            name: "existing-row").IsOk.Should().BeTrue();
        model.AddObject(
            category: ObjectCategory.Minimum,
            polynomial: new LinearPolynomial<Flt64>(
                Monomials: new[] { new LinearMonomial<Flt64>(Flt64.One, x) },
                Constant: new Flt64(2)),
            name: "existing-objective",
            displayName: null).IsOk.Should().BeTrue();
        MathConstraint existingConstraint = model.Constraints.Single();
        MetaSubObject<Flt64> existingObjective = model.MetaSubObjects.Single();
        LinearModelSnapshot<Flt64> existingSnapshot = (await model.State.RefreshAsync()).Value;

        IMetaModelWriteTransaction transaction = model.BeginWrite().Value;
        model.Add(y).IsOk.Should().BeTrue();
        model.AddConstraint(
            relation: Row(y, new Flt64(2)),
            group: null,
            name: "temporary-row").IsOk.Should().BeTrue();
        model.State.RegisterRow(
            id: new ConstraintId("dynamic-row"),
            relation: Row(y, new Flt64(3))).IsOk.Should().BeTrue();
        model.RemoveObjectiveTerms(new HashSet<IVariableItem> { x }).IsOk.Should().BeTrue();
        model.AddObject(
            category: ObjectCategory.Minimum,
            polynomial: new LinearPolynomial<Flt64>(
                Monomials: new[] { new LinearMonomial<Flt64>(Flt64.One, y) },
                Constant: Flt64.Zero),
            name: "temporary-objective",
            displayName: null).IsOk.Should().BeTrue();

        transaction.Rollback().IsOk.Should().BeTrue();

        model.Tokens.Find(x).Should().NotBeNull();
        model.Tokens.Find(y).Should().BeNull();
        model.Constraints.Should().ContainSingle().Which.Should().BeSameAs(existingConstraint);
        model.MetaSubObjects.Should().ContainSingle().Which.Should().BeSameAs(existingObjective);
        registry.Entries().Should().NotContain(identity => identity.Id.Value == "dynamic-row");
        LinearModelSnapshot<Flt64> restored = (await model.State.RefreshAsync()).Value;
        restored.Revision.Should().Be(existingSnapshot.Revision);
        restored.Rows.Should().ContainSingle()
            .Which.SourceName.Should().Be("existing-row");
        restored.Variables.Should().ContainSingle()
            .Which.Variable.Should().BeSameAs(x);
        transaction.Commit().IsFailed.Should().BeTrue();
    }

    [Fact]
    public void LinearColumnLifecycle_ShouldRollbackRemovalAtomically() {
        using var model = new LinearMetaModel<Flt64>();
        var x = new RealVar("column-x");
        var y = new RealVar("column-y");
        model.Add(x).IsOk.Should().BeTrue();
        model.Add(y).IsOk.Should().BeTrue();
        model.AddObject(
            category: ObjectCategory.Minimum,
            polynomial: new LinearPolynomial<Flt64>(
                Monomials: new[] {
                    new LinearMonomial<Flt64>(Flt64.One, x),
                    new LinearMonomial<Flt64>(new Flt64(2), y),
                },
                Constant: Flt64.Zero),
            name: "columns",
            displayName: null).IsOk.Should().BeTrue();
        MetaSubObject<Flt64> originalObjective = model.MetaSubObjects.Single();

        IMetaModelWriteTransaction transaction = model.BeginWrite().Value;
        model.RemoveObjectiveTerms(new HashSet<IVariableItem> { y }).IsOk.Should().BeTrue();
        model.Remove(y);
        model.Tokens.Find(y).Should().BeNull();
        model.MetaSubObjects.Single().Polynomial.Monomials.Should().ContainSingle();

        transaction.Rollback().IsOk.Should().BeTrue();

        model.Tokens.Find(y).Should().NotBeNull();
        model.MetaSubObjects.Should().ContainSingle().Which.Should().BeSameAs(originalObjective);
        model.MetaSubObjects.Single().Polynomial.Monomials.Should().HaveCount(2);
    }

    [Fact]
    public void LinearColumnLifecycle_ShouldCommitAddAndRemove() {
        using var model = new LinearMetaModel<Flt64>();
        var x = new RealVar("committed-x");
        var y = new RealVar("committed-y");
        model.Add(x).IsOk.Should().BeTrue();

        IMetaModelWriteTransaction add = model.BeginWrite().Value;
        model.Add(y).IsOk.Should().BeTrue();
        model.AddObject(
            category: ObjectCategory.Minimum,
            polynomial: new LinearPolynomial<Flt64>(
                Monomials: new[] { new LinearMonomial<Flt64>(Flt64.One, y) },
                Constant: Flt64.Zero),
            name: "added-column",
            displayName: null).IsOk.Should().BeTrue();
        add.Commit().IsOk.Should().BeTrue();

        model.Tokens.Find(y).Should().NotBeNull();
        model.MetaSubObjects.Should().ContainSingle();

        IMetaModelWriteTransaction remove = model.BeginWrite().Value;
        model.RemoveObjectiveTerms(new HashSet<IVariableItem> { y }).IsOk.Should().BeTrue();
        model.Remove(y);
        remove.Commit().IsOk.Should().BeTrue();

        model.Tokens.Find(y).Should().BeNull();
        model.MetaSubObjects.Single().Polynomial.Monomials.Should().BeEmpty();
        model.MetaSubObjects.Single().Polynomial.Constant.Should().Be(Flt64.Zero);
    }

    [Fact]
    public async Task LinearRollback_ShouldRestoreRemovedDynamicRow() {
        using var model = new LinearMetaModel<Flt64>();
        var x = new RealVar("removed-row-x");
        model.Add(x).IsOk.Should().BeTrue();
        model.State.RegisterRow(
            id: new ConstraintId("retained"),
            relation: Row(x, new Flt64(1))).IsOk.Should().BeTrue();
        LinearModelSnapshot<Flt64> initial = (await model.State.RefreshAsync()).Value;

        IMetaModelWriteTransaction transaction = model.BeginWrite().Value;
        model.State.RemoveRow(new ConstraintId("retained")).IsOk.Should().BeTrue();
        model.Constraints.Should().BeEmpty();
        transaction.Rollback().IsOk.Should().BeTrue();

        LinearModelSnapshot<Flt64> restored = (await model.State.RefreshAsync()).Value;
        restored.Revision.Should().Be(initial.Revision);
        restored.Rows.Should().ContainSingle()
            .Which.Id.Should().Be("retained");
        model.Constraints.Should().ContainSingle();
    }

    [Fact]
    public void QuadraticRollback_ShouldRestoreFlattenedObjectives() {
        using var model = new QuadraticMetaModel<Flt64>();
        var x = new RealVar("quadratic-transaction-x");
        var y = new RealVar("quadratic-transaction-y");
        model.Add(x).IsOk.Should().BeTrue();
        model.Add(y).IsOk.Should().BeTrue();
        model.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new SymbolFlatten.QuadraticFlattenData<Flt64>(
                Monomials: new[] {
                    new QuadraticMonomial<Flt64>(Flt64.One, x, y),
                    new QuadraticMonomial<Flt64>(Flt64.One, x),
                },
                Constant: new Flt64(4)),
            name: "existing-quadratic",
            displayName: "Existing quadratic").IsOk.Should().BeTrue();
        QuadraticFlattenSubObject<Flt64> original = model.FlattenSubObjects.Single();

        IMetaModelWriteTransaction transaction = model.BeginWrite().Value;
        model.RemoveObjectiveTerms(new HashSet<IVariableItem> { x }).IsOk.Should().BeTrue();
        model.AddObject(
            category: ObjectCategory.Maximum,
            flattenData: new SymbolFlatten.QuadraticFlattenData<Flt64>(
                Monomials: new[] { new QuadraticMonomial<Flt64>(Flt64.One, y) },
                Constant: Flt64.Zero),
            name: "temporary-quadratic",
            displayName: null).IsOk.Should().BeTrue();

        transaction.Rollback().IsOk.Should().BeTrue();

        model.FlattenSubObjects.Should().ContainSingle().Which.Should().BeSameAs(original);
        model.FlattenSubObjects.Single().FlattenData.Monomials.Should().HaveCount(2);
    }

    [Fact]
    public void BeginWrite_ShouldRejectNestedTransactionsAndAllowRetryAfterRollback() {
        using var model = new LinearMetaModel<Flt64>();
        IMetaModelWriteTransaction transaction = model.BeginWrite().Value;

        Result<IMetaModelWriteTransaction, ErrorCode, Error<ErrorCode>> nested =
            model.BeginWrite();

        nested.Should()
            .BeOfType<Failed<IMetaModelWriteTransaction, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.IllegalArgument);
        transaction.Rollback().IsOk.Should().BeTrue();
        IMetaModelWriteTransaction retry = model.BeginWrite().Value;
        retry.Rollback().IsOk.Should().BeTrue();
    }

    [Fact]
    public void BeginWrite_WhenClosed_ShouldReturnApplicationStopped() {
        using var model = new LinearMetaModel<Flt64>();
        model.Close();

        Result<IMetaModelWriteTransaction, ErrorCode, Error<ErrorCode>> result =
            model.BeginWrite();

        result.Should()
            .BeOfType<Failed<IMetaModelWriteTransaction, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.ApplicationStopped);
    }

    [Fact]
    public void Commit_WhenModelCloses_ShouldRollBackMetadataAndTerminateTransaction() {
        using var model = new LinearMetaModel<Flt64>();
        var x = new RealVar("closed-commit-x");
        var y = new RealVar("closed-commit-y");
        model.Add(x).IsOk.Should().BeTrue();
        model.AddConstraint(
            relation: Row(x, Flt64.One),
            group: null,
            name: "retained").IsOk.Should().BeTrue();
        model.AddObject(
            category: ObjectCategory.Minimum,
            polynomial: new LinearPolynomial<Flt64>(
                Monomials: new[] { new LinearMonomial<Flt64>(Flt64.One, x) },
                Constant: Flt64.Zero),
            name: "retained-objective",
            displayName: null).IsOk.Should().BeTrue();
        MathConstraint originalConstraint = model.Constraints.Single();
        MetaSubObject<Flt64> originalObjective = model.MetaSubObjects.Single();
        IMetaModelWriteTransaction transaction = model.BeginWrite().Value;
        model.Add(y).IsOk.Should().BeTrue();
        model.AddConstraint(
            relation: Row(y, Flt64.One),
            group: null,
            name: "temporary").IsOk.Should().BeTrue();
        model.RemoveObjectiveTerms(new HashSet<IVariableItem> { x }).IsOk.Should().BeTrue();

        model.Close();
        Try result = transaction.Commit();

        Fatal<Success, ErrorCode, Error<ErrorCode>> fatal =
            result.Should().BeOfType<Fatal<Success, ErrorCode, Error<ErrorCode>>>().Which;
        fatal.Errors.Should().Contain(error => error.Code == ErrorCode.ApplicationStopped);
        fatal.Errors.Should().Contain(error => error.Code == ErrorCode.ApplicationError);
        model.Constraints.Should().ContainSingle().Which.Should().BeSameAs(originalConstraint);
        model.MetaSubObjects.Should().ContainSingle().Which.Should().BeSameAs(originalObjective);
        transaction.Commit().IsFailed.Should().BeTrue();
        transaction.Rollback().IsFailed.Should().BeTrue();
    }

    [Fact]
    public void Rollback_WhenTokenTableCloses_ShouldReportFailureAndTerminateTransaction() {
        using var model = new LinearMetaModel<Flt64>();
        var x = new RealVar("closed-rollback-x");
        model.Add(x).IsOk.Should().BeTrue();
        model.AddObject(
            category: ObjectCategory.Minimum,
            polynomial: new LinearPolynomial<Flt64>(
                Monomials: new[] { new LinearMonomial<Flt64>(Flt64.One, x) },
                Constant: Flt64.Zero),
            name: "retained-objective",
            displayName: null).IsOk.Should().BeTrue();
        MetaSubObject<Flt64> originalObjective = model.MetaSubObjects.Single();
        IMetaModelWriteTransaction transaction = model.BeginWrite().Value;
        model.RemoveObjectiveTerms(new HashSet<IVariableItem> { x }).IsOk.Should().BeTrue();

        model.Tokens.Dispose();
        Try result = transaction.Rollback();

        result.Should()
            .BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.ApplicationError);
        model.MetaSubObjects.Should().ContainSingle().Which.Should().BeSameAs(originalObjective);
        transaction.Rollback().IsFailed.Should().BeTrue();
        model.BeginWrite().Should()
            .BeOfType<Failed<IMetaModelWriteTransaction, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.ApplicationStopped);
    }

    [Fact]
    public void Rollback_AfterConstraintRemoval_ShouldRestoreGroupIndexes() {
        using var model = new LinearMetaModel<Flt64>();
        var group = new TestConstraintGroup("transaction-group");
        var x = new RealVar("transaction-group-x");
        model.Add(x).IsOk.Should().BeTrue();
        model.RegisterConstraintGroup(group);
        model.AddConstraint(Row(x, Flt64.One), group, name: "first").IsOk.Should().BeTrue();
        model.AddConstraint(Row(x, Flt64.One), group, name: "second").IsOk.Should().BeTrue();
        MathConstraint first = model.Constraints[0];
        MathConstraint second = model.Constraints[1];
        model.IndicesOfConstraintGroup(group).Should().NotBeNull();

        IMetaModelWriteTransaction transaction = model.BeginWrite().Value;
        model.RemoveConstraint(first).IsOk.Should().BeTrue();
        model.ConstraintsOfGroup(group).Should().ContainSingle().Which.Should().BeSameAs(second);

        transaction.Rollback().IsOk.Should().BeTrue();

        model.ConstraintsOfGroup(group).Should().HaveCount(2);
        model.ConstraintsOfGroup(group)[0].Should().BeSameAs(first);
        model.ConstraintsOfGroup(group)[1].Should().BeSameAs(second);
    }

    [Fact]
    public async Task RemovedDynamicRow_ShouldFreeItsIdentityForReuse() {
        var registry = new ModelElementIdentityRegistry();
        using var model = new LinearMetaModel<Flt64>(identityRegistry: registry);
        var x = new RealVar("reuse-row-x");
        model.Add(x).IsOk.Should().BeTrue();
        model.State.RegisterRow(
            id: new ConstraintId("reusable"),
            relation: Row(x, Flt64.One)).IsOk.Should().BeTrue();
        (await model.State.RefreshAsync()).IsOk.Should().BeTrue();

        model.State.RemoveRow(new ConstraintId("reusable")).IsOk.Should().BeTrue();
        registry.Entries().Should().NotContain(identity => identity.Id.Value == "reusable");
        model.State.RegisterRow(
            id: new ConstraintId("reusable"),
            relation: Row(x, new Flt64(2))).IsOk.Should().BeTrue();
        LinearModelSnapshot<Flt64> snapshot = (await model.State.RefreshAsync()).Value;
        snapshot.Rows.Should().ContainSingle()
            .Which.Id.Should().Be("reusable");
        registry.Entries().Should().ContainSingle()
            .Which.Id.Value.Should().Be("reusable");
    }

    [Fact]
    public async Task DirectlyRemovedDynamicRow_ShouldAlsoFreeItsIdentityForReuse() {
        using var model = new LinearMetaModel<Flt64>();
        var x = new RealVar("direct-remove-x");
        model.Add(x).IsOk.Should().BeTrue();
        model.State.RegisterRow(
            id: new ConstraintId("direct"),
            relation: Row(x, Flt64.One)).IsOk.Should().BeTrue();
        (await model.State.RefreshAsync()).IsOk.Should().BeTrue();

        MathConstraint origin = model.Constraints.Single();
        model.RemoveConstraint(origin).IsOk.Should().BeTrue();
        model.State.RegisterRow(
            id: new ConstraintId("direct"),
            relation: Row(x, new Flt64(2))).IsOk.Should().BeTrue();
        LinearModelSnapshot<Flt64> snapshot = (await model.State.RefreshAsync()).Value;
        snapshot.Rows.Should().ContainSingle()
            .Which.Id.Should().Be("direct");
    }

    [Fact]
    public void NonContiguousGroupReregistration_ShouldNotOverwriteExistingRange() {
        using var model = new LinearMetaModel<Flt64>();
        var group = new TestConstraintGroup("non-contiguous");
        var x = new RealVar("non-contiguous-x");
        model.Add(x).IsOk.Should().BeTrue();
        model.RegisterConstraintGroup(group);
        model.AddConstraint(Row(x, Flt64.One), group, name: "first").IsOk.Should().BeTrue();
        model.AddConstraint(Row(x, Flt64.One), null, name: "ungrouped").IsOk.Should().BeTrue();

        model.RegisterConstraintGroup(group);
        model.AddConstraint(Row(x, Flt64.One), group, name: "leaked").IsOk.Should().BeFalse();

        model.ConstraintsOfGroup(group).Select(constraint => constraint.Name)
            .Should().Equal("first");
        model.Constraints.Should().HaveCount(2);
    }

    [Fact]
    public void RemovedConstraint_ShouldFreeItsIdentityForReuse() {
        var registry = new ModelElementIdentityRegistry();
        using var model = new LinearMetaModel<Flt64>(identityRegistry: registry);
        var x = new RealVar("identity-reuse-x");
        model.Add(x).IsOk.Should().BeTrue();
        model.AddConstraint(Row(x, Flt64.One), null, name: "reusable").IsOk.Should().BeTrue();
        MathConstraint first = model.Constraints.Single();
        registry.RegisterConstraint(first, new ConstraintId("reusable")).IsOk.Should().BeTrue();

        model.RemoveConstraint(first).IsOk.Should().BeTrue();
        registry.Entries().Should().NotContain(identity => identity.Id.Value == "reusable");

        model.AddConstraint(Row(x, new Flt64(2)), null, name: "reusable").IsOk.Should().BeTrue();
        MathConstraint second = model.Constraints.Single();
        registry.RegisterConstraint(second, new ConstraintId("reusable")).IsOk.Should().BeTrue();
        registry.Entries().Should().ContainSingle()
            .Which.Id.Value.Should().Be("reusable");
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
