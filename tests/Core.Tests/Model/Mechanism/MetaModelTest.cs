#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using SymbolFlatten = Fuookami.Ospf.Core.Symbol.Flatten;

namespace Fuookami.Ospf.Core.Tests.Model.Mechanism;

public class MetaModelTest {
    [Fact]
    public void LinearMetaModel_AddVariable_ShouldSucceed() {
        var model = new LinearMetaModel<Flt64>("test", ObjectCategory.Minimum);
        var variable = new RealVar("x");

        Result<Success, ErrorCode, Error<ErrorCode>> result = model.Add(variable);

        result.Should().NotBeNull();
        model.Tokens.Find(variable).Should().NotBeNull();
    }

    [Fact]
    public void LinearMetaModel_RemoveVariable_ShouldSucceed() {
        var model = new LinearMetaModel<Flt64>("test", ObjectCategory.Minimum);
        var variable = new RealVar("x");
        model.Add(variable);

        model.Remove(variable);

        model.Tokens.Find(variable).Should().BeNull();
    }

    [Fact]
    public void LinearMetaModel_AddConstraint_ShouldAddToConstraints() {
        var model = new LinearMetaModel<Flt64>("test", ObjectCategory.Minimum);
        var variable = new RealVar("x");
        model.Add(variable);

        var lhs = new LinearPolynomial<Flt64>(
            new List<LinearMonomial<Flt64>> { new(Flt64.One, variable) },
            Flt64.Zero);
        var rhs = new LinearPolynomial<Flt64>(
            Array.Empty<LinearMonomial<Flt64>>(),
            Flt64.One);
        var inequality = new LinearInequality<Flt64>(lhs, rhs, Comparison.LE);

        Result<Success, ErrorCode, Error<ErrorCode>> result = model.AddConstraint(inequality, null, name: "c1");

        result.Should().NotBeNull();
        model.Constraints.Should().HaveCount(1);
    }

    [Fact]
    public void LinearMetaModel_AddObject_ShouldSucceed() {
        var model = new LinearMetaModel<Flt64>("test", ObjectCategory.Minimum);
        var variable = new RealVar("x");
        model.Add(variable);

        var polynomial = new LinearPolynomial<Flt64>(
            new List<LinearMonomial<Flt64>> { new(Flt64.One, variable) },
            Flt64.Zero);

        Result<Success, ErrorCode, Error<ErrorCode>> result = model.AddObject(ObjectCategory.Minimum, polynomial, "obj", null);

        result.Should().NotBeNull();
    }

    [Fact]
    public void QuadraticMetaModel_AddQuadraticConstraint_ShouldSucceed() {
        var model = new QuadraticMetaModel<Flt64>("test", ObjectCategory.Minimum);
        var variable = new RealVar("x");
        model.Add(variable);

        var qLhs = new QuadraticPolynomial<Flt64>(
            new List<QuadraticMonomial<Flt64>> { new(Flt64.One, variable, variable) },
            Flt64.Zero);
        var qRhs = new QuadraticPolynomial<Flt64>(
            Array.Empty<QuadraticMonomial<Flt64>>(),
            Flt64.One);
        var inequality = new QuadraticInequalityOf<Flt64>(qLhs, qRhs, Comparison.LE);

        Result<Success, ErrorCode, Error<ErrorCode>> result = model.AddConstraint(inequality, null, name: "q1");

        result.Should().NotBeNull();
        model.Constraints.Should().HaveCount(1);
    }

    [Fact]
    public void LinearMetaModel_RegisterConstraintGroup_ShouldTrackIndices() {
        var model = new LinearMetaModel<Flt64>("test", ObjectCategory.Minimum);
        var group = new TestConstraintGroup("group1");
        var variable = new RealVar("x");
        model.Add(variable);

        model.RegisterConstraintGroup(group);
        var lhs = new LinearPolynomial<Flt64>(
            new List<LinearMonomial<Flt64>> { new(Flt64.One, variable) },
            Flt64.Zero);
        var rhs = new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.One);
        model.AddConstraint(new LinearInequality<Flt64>(lhs, rhs, Comparison.LE), group, name: "c1");
        model.AddConstraint(new LinearInequality<Flt64>(lhs, rhs, Comparison.LE), group, name: "c2");

        Range? indices = model.IndicesOfConstraintGroup(group);
        indices.Should().NotBeNull();
        model.ConstraintsOfGroup(group).Should().HaveCount(2);
    }

    [Fact]
    public void LinearMetaModel_RollbackConstraintsTo_ShouldTrimConstraintsAndGroupIndexes() {
        var model = new LinearMetaModel<Flt64>("rollback", ObjectCategory.Minimum);
        var firstGroup = new TestConstraintGroup("first");
        var secondGroup = new TestConstraintGroup("second");
        var variable = new RealVar("x");
        model.Add(variable).IsOk.Should().BeTrue();
        var lhs = new LinearPolynomial<Flt64>(
            new List<LinearMonomial<Flt64>> { new(Flt64.One, variable) },
            Flt64.Zero);
        var rhs = new LinearPolynomial<Flt64>(
            Array.Empty<LinearMonomial<Flt64>>(),
            Flt64.One);
        var relation = new LinearInequality<Flt64>(lhs, rhs, Comparison.LE);

        model.RegisterConstraintGroup(firstGroup);
        model.AddConstraint(relation, firstGroup, name: "first-1").IsOk.Should().BeTrue();
        model.AddConstraint(relation, firstGroup, name: "first-2").IsOk.Should().BeTrue();
        model.RegisterConstraintGroup(secondGroup);
        model.AddConstraint(relation, secondGroup, name: "second-1").IsOk.Should().BeTrue();
        model.AddConstraint(relation, secondGroup, name: "second-2").IsOk.Should().BeTrue();

        model.RollbackConstraintsTo(3).IsOk.Should().BeTrue();
        model.Constraints.Should().HaveCount(3);
        model.RollbackConstraintsTo(1).IsOk.Should().BeTrue();
        model.Constraints.Should().HaveCount(1);
        model.ConstraintsOfGroup(firstGroup).Should().ContainSingle()
            .Which.Name.Should().Be("first-1");
        model.ConstraintsOfGroup(secondGroup).Should().BeEmpty();

        Result<Success, ErrorCode, Error<ErrorCode>> invalid =
            model.RollbackConstraintsTo(2);
        invalid.Should()
            .BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.IllegalArgument);
        model.Constraints.Should().HaveCount(1);
    }

    [Fact]
    public void LinearMetaModel_RemoveConstraint_ShouldShiftCompletedGroupIndexes() {
        using var model = new LinearMetaModel<Flt64>();
        var firstGroup = new TestConstraintGroup("first");
        var secondGroup = new TestConstraintGroup("second");
        var x = new RealVar("group-shift-x");
        model.Add(x).IsOk.Should().BeTrue();
        LinearInequality<Flt64> relation = Row(x, Flt64.One);

        model.RegisterConstraintGroup(firstGroup);
        model.AddConstraint(relation, firstGroup, name: "first-1").IsOk.Should().BeTrue();
        model.AddConstraint(relation, firstGroup, name: "first-2").IsOk.Should().BeTrue();
        model.RegisterConstraintGroup(secondGroup);
        model.AddConstraint(relation, secondGroup, name: "second-1").IsOk.Should().BeTrue();
        model.AddConstraint(relation, secondGroup, name: "second-2").IsOk.Should().BeTrue();
        MathConstraint first = model.Constraints[0];
        MathConstraint secondGroupFirst = model.Constraints[2];

        model.RemoveConstraint(first).IsOk.Should().BeTrue();

        model.ConstraintsOfGroup(firstGroup).Should().ContainSingle()
            .Which.Name.Should().Be("first-2");
        model.ConstraintsOfGroup(secondGroup).Should().HaveCount(2);

        model.RemoveConstraint(secondGroupFirst).IsOk.Should().BeTrue();

        model.ConstraintsOfGroup(firstGroup).Should().ContainSingle();
        model.ConstraintsOfGroup(secondGroup).Should().ContainSingle()
            .Which.Name.Should().Be("second-2");
        model.Constraints.Should().HaveCount(2);
    }

    [Fact]
    public void QuadraticMetaModel_RemoveConstraint_ShouldShiftGroupIndexesAndBeIdempotent() {
        using var model = new QuadraticMetaModel<Flt64>();
        var group = new TestConstraintGroup("quadratic");
        var x = new RealVar("quadratic-remove-x");
        model.Add(x).IsOk.Should().BeTrue();
        LinearInequality<Flt64> relation = Row(x, Flt64.One);
        model.RegisterConstraintGroup(group);
        model.AddConstraint(relation, group, name: "first").IsOk.Should().BeTrue();
        model.AddConstraint(relation, group, name: "second").IsOk.Should().BeTrue();
        MathConstraint first = model.Constraints[0];
        MathConstraint second = model.Constraints[1];

        model.RemoveConstraint(first).IsOk.Should().BeTrue();
        model.RemoveConstraint(first).IsOk.Should().BeTrue();
        model.RemoveConstraint(new LinearInequalityConstraint<Flt64>(
            Row(x, Flt64.One), null, false, null, 0, "unknown", null)).IsOk.Should().BeTrue();

        model.Constraints.Should().ContainSingle().Which.Should().BeSameAs(second);
        model.ConstraintsOfGroup(group).Should().ContainSingle().Which.Should().BeSameAs(second);
        model.RemoveConstraint(null!).Should()
            .BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.IllegalArgument);
    }

    [Fact]
    public void ConstraintGroups_ShouldStopAtUngroupedRowsAndRejectNonContiguousReuse() {
        using var model = new LinearMetaModel<Flt64>();
        var group = new TestConstraintGroup("contiguous");
        var x = new RealVar("contiguous-x");
        model.Add(x).IsOk.Should().BeTrue();
        model.RegisterConstraintGroup(group);
        model.AddConstraint(Row(x, Flt64.One), group, name: "first").IsOk.Should().BeTrue();
        model.IndicesOfConstraintGroup(group).Should().NotBeNull();
        model.AddConstraint(Row(x, Flt64.One), group, name: "second").IsOk.Should().BeTrue();
        model.AddConstraint(Row(x, Flt64.One), null, name: "ungrouped").IsOk.Should().BeTrue();

        model.ConstraintsOfGroup(group).Select(constraint => constraint.Name)
            .Should().Equal("first", "second");
        model.AddConstraint(Row(x, Flt64.One), group, name: "noncontiguous").Should()
            .BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.IllegalArgument);
        model.Constraints.Should().HaveCount(3);
    }

    [Fact]
    public async Task LinearMetaModel_RemoveObjectiveTerms_ShouldPreserveMetadataAndInvalidateSnapshot() {
        using var model = new LinearMetaModel<Flt64>("objectives", ObjectCategory.Minimum);
        var x = new RealVar("x");
        var y = new RealVar("y");
        model.Add(x).IsOk.Should().BeTrue();
        model.Add(y).IsOk.Should().BeTrue();
        model.AddObject(
            category: ObjectCategory.Maximum,
            polynomial: new LinearPolynomial<Flt64>(
                Monomials: new[] {
                    new LinearMonomial<Flt64>(new Flt64(2), x),
                    new LinearMonomial<Flt64>(new Flt64(3), y),
                },
                Constant: new Flt64(5)),
            name: "first",
            displayName: "First objective").IsOk.Should().BeTrue();
        model.AddObject(
            category: ObjectCategory.Minimum,
            polynomial: new LinearPolynomial<Flt64>(
                Monomials: new[] {
                    new LinearMonomial<Flt64>(new Flt64(-1), x),
                    new LinearMonomial<Flt64>(new Flt64(4), y),
                },
                Constant: new Flt64(7)),
            name: "second",
            displayName: null).IsOk.Should().BeTrue();
        LinearModelSnapshot<Flt64> before = (await model.State.RefreshAsync()).Value;

        model.RemoveObjectiveTerms(new HashSet<IVariableItem> { x }).IsOk.Should().BeTrue();

        model.MetaSubObjects.Select(objective => objective.Name)
            .Should().Equal("first", "second");
        model.MetaSubObjects.Select(objective => objective.Category)
            .Should().Equal(ObjectCategory.Maximum, ObjectCategory.Minimum);
        model.MetaSubObjects.Select(objective => objective.DisplayName)
            .Should().Equal("First objective", null);
        model.MetaSubObjects[0].Polynomial.Monomials.Should().ContainSingle()
            .Which.Symbol.Should().BeSameAs(y);
        model.MetaSubObjects[0].Polynomial.Monomials[0].Coefficient.Should().Be(new Flt64(3));
        model.MetaSubObjects[0].Polynomial.Constant.Should().Be(new Flt64(5));
        model.MetaSubObjects[1].Polynomial.Monomials.Should().ContainSingle()
            .Which.Symbol.Should().BeSameAs(y);
        model.MetaSubObjects[1].Polynomial.Monomials[0].Coefficient.Should().Be(new Flt64(4));
        model.MetaSubObjects[1].Polynomial.Constant.Should().Be(new Flt64(7));

        LinearModelSnapshot<Flt64> after = (await model.State.RefreshAsync()).Value;
        after.Revision.Should().NotBe(before.Revision);
        after.Objective.Should().NotContainKey(x);
        after.Objective[y].Should().Be(new Flt64(1));
        (await model.State.RequireCurrentAsync(before.Revision)).IsFailed.Should().BeTrue();
    }

    [Fact]
    public void LinearMetaModel_RemoveObjectiveTerms_ShouldIgnoreEmptyAndUnknownVariables() {
        using var model = new LinearMetaModel<Flt64>();
        var x = new RealVar("x");
        model.Add(x).IsOk.Should().BeTrue();
        model.AddObject(
            category: ObjectCategory.Minimum,
            polynomial: new LinearPolynomial<Flt64>(
                Monomials: new[] { new LinearMonomial<Flt64>(Flt64.One, x) },
                Constant: Flt64.Zero),
            name: "objective",
            displayName: null).IsOk.Should().BeTrue();
        MetaSubObject<Flt64> original = model.MetaSubObjects.Single();

        model.RemoveObjectiveTerms(new HashSet<IVariableItem>()).IsOk.Should().BeTrue();
        model.RemoveObjectiveTerms(
            new HashSet<IVariableItem> { new RealVar("unknown") }).IsOk.Should().BeTrue();

        model.MetaSubObjects.Should().ContainSingle().Which.Should().BeSameAs(original);
        model.MetaSubObjects.Single().Polynomial.Monomials.Should().ContainSingle();
    }

    [Fact]
    public void LinearMetaModel_RemoveObjectiveTerms_ShouldRejectClosedModel() {
        using var model = new LinearMetaModel<Flt64>();
        var x = new RealVar("x");
        model.Close();

        Result<Success, ErrorCode, Error<ErrorCode>> result =
            model.RemoveObjectiveTerms(new HashSet<IVariableItem> { x });

        result.Should()
            .BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.ApplicationStopped);
    }

    [Fact]
    public void QuadraticMetaModel_RemoveObjectiveTerms_ShouldRemoveBothFactorsAndPreserveConstant() {
        using var model = new QuadraticMetaModel<Flt64>();
        var x = new RealVar("x");
        var y = new RealVar("y");
        model.Add(x).IsOk.Should().BeTrue();
        model.Add(y).IsOk.Should().BeTrue();
        model.AddObject(
            category: ObjectCategory.Maximum,
            flattenData: new SymbolFlatten.QuadraticFlattenData<Flt64>(
                Monomials: new[] {
                    new QuadraticMonomial<Flt64>(new Flt64(2), x, y),
                    new QuadraticMonomial<Flt64>(new Flt64(3), x),
                    new QuadraticMonomial<Flt64>(new Flt64(4), y),
                    new QuadraticMonomial<Flt64>(new Flt64(5), y, y),
                },
                Constant: new Flt64(11)),
            name: "quadratic",
            displayName: "Quadratic objective").IsOk.Should().BeTrue();

        model.RemoveObjectiveTerms(new HashSet<IVariableItem> { x }).IsOk.Should().BeTrue();

        QuadraticFlattenSubObject<Flt64> objective = model.FlattenSubObjects.Single();
        objective.Category.Should().Be(ObjectCategory.Maximum);
        objective.Name.Should().Be("quadratic");
        objective.DisplayName.Should().Be("Quadratic objective");
        objective.Constant.Should().Be(new Flt64(11));
        objective.FlattenData.Monomials.Should().HaveCount(2);
        objective.FlattenData.Monomials.Should().OnlyContain(term =>
            term.Symbol1 == y && (term.Symbol2 == null || term.Symbol2 == y));
    }

    [Fact]
    public void QuadraticMetaModel_RemoveObjectiveTerms_ShouldIgnoreEmptyUnknownAndRejectClosed() {
        using var model = new QuadraticMetaModel<Flt64>();
        var x = new RealVar("quadratic-edge-x");
        model.Add(x).IsOk.Should().BeTrue();
        model.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new SymbolFlatten.QuadraticFlattenData<Flt64>(
                Monomials: new[] { new QuadraticMonomial<Flt64>(Flt64.One, x) },
                Constant: Flt64.One),
            name: "quadratic-edge",
            displayName: null).IsOk.Should().BeTrue();
        QuadraticFlattenSubObject<Flt64> original = model.FlattenSubObjects.Single();

        model.RemoveObjectiveTerms(new HashSet<IVariableItem>()).IsOk.Should().BeTrue();
        model.RemoveObjectiveTerms(
            new HashSet<IVariableItem> { new RealVar("quadratic-unknown") }).IsOk.Should().BeTrue();
        model.FlattenSubObjects.Should().ContainSingle().Which.Should().BeSameAs(original);

        model.Close();
        model.RemoveObjectiveTerms(new HashSet<IVariableItem> { x }).Should()
            .BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.ApplicationStopped);
    }

    [Fact]
    public void LinearMetaModel_Flush_ShouldNotThrow() {
        var model = new LinearMetaModel<Flt64>("test", ObjectCategory.Minimum);

        model.Invoking(m => m.Flush()).Should().NotThrow();
        model.Invoking(m => m.Flush(force: true)).Should().NotThrow();
    }

    [Fact]
    public void LinearMetaModel_Dispose_ShouldNotThrow() {
        var model = new LinearMetaModel<Flt64>("test", ObjectCategory.Minimum);

        model.Invoking(m => m.Dispose()).Should().NotThrow();
    }

    private static LinearInequality<Flt64> Row(IVariableItem variable, Flt64 rhs) => new(
        Lhs: new LinearPolynomial<Flt64>(
            Monomials: new[] { new LinearMonomial<Flt64>(Flt64.One, variable) },
            Constant: Flt64.Zero),
        Rhs: new LinearPolynomial<Flt64>(
            Monomials: Array.Empty<LinearMonomial<Flt64>>(),
            Constant: rhs),
        Comparison: Comparison.LE);

    private class TestConstraintGroup : IMetaConstraintGroup {
        public bool Lazy => false;
        public string Name { get; }
        public TestConstraintGroup(string name) => Name = name;
    }
}
