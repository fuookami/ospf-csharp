#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using Xunit;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Variable;
namespace Fuookami.Ospf.Core.Tests.Model.Intermediate;

public class MatrixCollectionOwnershipTest {
    [Fact]
    public void CustomObjectiveCells_AreNotDeepCopied() {
        var cell = new MutableCell();
        var objective = new Objective<MutableCell>(ObjectCategory.Minimum, [cell]);
        Objective<MutableCell> copy = objective.Copy();
        cell.Value = 3;

        Assert.Same(cell, Assert.Single(objective.Cells));
        Assert.Same(cell, Assert.Single(copy.Cells));
        Assert.Equal(3, copy.Cells[0].Value);
        AssertReadOnly(objective.Cells, new MutableCell());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ObjectiveCells_ShouldOwnInputAndRejectWrites(bool quadratic) {
        if (quadratic) {
            var cell = new QuadraticObjectiveCell(
                Col1: 0,
                Col2: -1,
                Coefficient: Flt64.One
            );
            var cells = new List<QuadraticObjectiveCell> { cell };
            var objective = new Objective<QuadraticObjectiveCell>(ObjectCategory.Minimum, cells);
            cells.Clear();
            Assert.Same(cell, Assert.Single(objective.Cells));
            AssertReadOnly(objective.Cells, cell.Copy());
            QuadraticObjectiveCell[] array = [cell];
            var arrayObjective = new Objective<QuadraticObjectiveCell>(ObjectCategory.Minimum, array);
            array[0] = cell.Copy();
            Assert.Same(cell, Assert.Single(arrayObjective.Cells));
        }
        else {
            var cell = new LinearObjectiveCell(0, Flt64.One);
            var cells = new List<LinearObjectiveCell> { cell };
            var objective = new Objective<LinearObjectiveCell>(ObjectCategory.Minimum, cells);
            cells.Clear();
            Assert.Same(cell, Assert.Single(objective.Cells));
            AssertReadOnly(objective.Cells, cell.Copy());
            LinearObjectiveCell[] array = [cell];
            var arrayObjective = new Objective<LinearObjectiveCell>(ObjectCategory.Minimum, array);
            array[0] = cell.Copy();
            Assert.Same(cell, Assert.Single(arrayObjective.Cells));
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CopiedMetadata_ShouldSurviveEitherDisposalOrder(bool quadratic, bool copyFirst) {
        if (quadratic) {
            using var constraints = new QuadraticConstraintBatch(
                lhs: new SparseQuadraticMatrix([new SparseQuadraticVector()]),
                signs: [ConstraintRelation.LessEqual],
                rhs: [Flt64.One],
                names: ["row"],
                sources: [ConstraintSource.Origin]
            );
            AssertDisposalOrder(constraints, copyFirst);
        }
        else {
            using var constraints = new LinearConstraintBatch(
                lhs: new SparseMatrix([new SparseVector()]),
                signs: [ConstraintRelation.LessEqual],
                rhs: [Flt64.One],
                names: ["row"],
                sources: [ConstraintSource.Origin]
            );
            AssertDisposalOrder(constraints, copyFirst);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConstraintMetadata_ShouldOwnInputsAndRejectWrites(bool quadratic) {
        var signs = new List<ConstraintRelation> { ConstraintRelation.LessEqual };
        var rhs = new List<Flt64> { new(-0.0) };
        var names = new List<string> { "row" };
        var sources = new List<ConstraintSource> { ConstraintSource.Origin };
        if (quadratic) {
            using var constraints = new QuadraticConstraintBatch(
                lhs: new SparseQuadraticMatrix([new SparseQuadraticVector()]),
                signs: signs,
                rhs: rhs,
                names: names,
                sources: sources
            );
            AssertMetadata(
                constraints: constraints,
                signs: signs,
                rhs: rhs,
                names: names,
                sources: sources
            );
        }
        else {
            using var constraints = new LinearConstraintBatch(
                lhs: new SparseMatrix([new SparseVector()]),
                signs: signs,
                rhs: rhs,
                names: names,
                sources: sources
            );
            AssertMetadata(
                constraints: constraints,
                signs: signs,
                rhs: rhs,
                names: names,
                sources: sources
            );
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MatrixCollections_ShouldOwnInputsAndRejectWrites(bool quadratic) {
        var variable = new ModelViewVariable(
            index: 0,
            lowerBound: Flt64.Zero,
            upperBound: Flt64.One,
            type: Continuous.Instance,
            name: "x"
        );
        var variables = new List<ModelViewVariable> { variable };
        var signs = new List<ConstraintRelation> { ConstraintRelation.LessEqual };
        var rhs = new List<Flt64> { new(-0.0) };
        var names = new List<string> { "row" };
        var sources = new List<ConstraintSource> { ConstraintSource.Origin };

        if (quadratic) {
            var row = new SparseQuadraticVector();
            row.Add(
                col1: 0,
                col2: 0,
                value: Flt64.One
            );
            var constraints = new QuadraticConstraintBatch(
                lhs: new SparseQuadraticMatrix([row]),
                signs: signs,
                rhs: rhs,
                names: names,
                sources: sources
            );
            var cell = new QuadraticObjectiveCell(
                Col1: 0,
                Col2: 0,
                Coefficient: Flt64.One
            );
            var cells = new List<QuadraticObjectiveCell> { cell };
            var objective = new Objective<QuadraticObjectiveCell>(ObjectCategory.Minimum, cells);
            using var model = new QuadraticTetradModel(
                variables: variables,
                constraints: constraints,
                objective: objective,
                name: "quadratic"
            );

            variables.Clear();
            cells.Clear();
            Assert.Same(variable, Assert.Single(model.Variables));
            Assert.Same(cell, Assert.Single(model.Objective.Cells));
            AssertReadOnly(model.Variables, variable.Copy());
            AssertReadOnly(model.Objective.Cells, cell.Copy());
            AssertMetadata(
                constraints: constraints,
                signs: signs,
                rhs: rhs,
                names: names,
                sources: sources
            );
        }
        else {
            var row = new SparseVector();
            row.Add(0, Flt64.One);
            var constraints = new LinearConstraintBatch(
                lhs: new SparseMatrix([row]),
                signs: signs,
                rhs: rhs,
                names: names,
                sources: sources
            );
            var cell = new LinearObjectiveCell(0, Flt64.One);
            var cells = new List<LinearObjectiveCell> { cell };
            var objective = new Objective<LinearObjectiveCell>(ObjectCategory.Minimum, cells);
            using var model = new LinearTriadModel(
                variables: variables,
                constraints: constraints,
                objective: objective,
                name: "linear"
            );

            variables.Clear();
            cells.Clear();
            Assert.Same(variable, Assert.Single(model.Variables));
            Assert.Same(cell, Assert.Single(model.Objective.Cells));
            AssertReadOnly(model.Variables, variable.Copy());
            AssertReadOnly(model.Objective.Cells, cell.Copy());
            AssertMetadata(
                constraints: constraints,
                signs: signs,
                rhs: rhs,
                names: names,
                sources: sources
            );
        }
    }

    private static void AssertMetadata<C>(
        ModelConstraint<C> constraints,
        List<ConstraintRelation> signs,
        List<Flt64> rhs,
        List<string> names,
        List<ConstraintSource> sources)
        where C : IConstraintCell<C>, Fuookami.Ospf.Utils.Concept.ICopyable<C> {
        signs.Clear();
        rhs.Clear();
        names.Clear();
        sources.Clear();
        Assert.Equal(ConstraintRelation.LessEqual, Assert.Single(constraints.Signs));
        Assert.Equal(long.MinValue, BitConverter.DoubleToInt64Bits(Assert.Single(constraints.Rhs).ToDouble()));
        Assert.Equal("row", Assert.Single(constraints.Names));
        Assert.Equal(ConstraintSource.Origin, Assert.Single(constraints.Sources));
        AssertReadOnly(constraints.Signs, ConstraintRelation.Equal);
        AssertReadOnly(constraints.Rhs, Flt64.One);
        AssertReadOnly(constraints.Names, "changed");
        AssertReadOnly(constraints.Sources, ConstraintSource.Dual);
    }

    private static void AssertReadOnly<T>(IReadOnlyList<T> values, T replacement) {
        IList<T> generic = Assert.IsAssignableFrom<IList<T>>(values);
        IList nongeneric = Assert.IsAssignableFrom<IList>(values);
        Assert.True(generic.IsReadOnly);
        Assert.True(nongeneric.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => generic[0] = replacement);
        Assert.Throws<NotSupportedException>(nongeneric.Clear);
        Assert.Single(values);
    }

    private static void AssertDisposalOrder<C>(ModelConstraint<C> original, bool copyFirst)
        where C : IConstraintCell<C>, Fuookami.Ospf.Utils.Concept.ICopyable<C> {
        using ModelConstraint<C> copy = original.Copy();
        ModelConstraint<C> first = copyFirst ? copy : original;
        ModelConstraint<C> second = copyFirst ? original : copy;
        IReadOnlyList<string> firstNames = first.Names;
        IReadOnlyList<string> secondNames = second.Names;

        first.Dispose();
        first.Dispose();
        Assert.Empty(firstNames);
        Assert.Equal("row", Assert.Single(secondNames));
        AssertReadOnly(secondNames, "changed");
        second.Dispose();
        Assert.Empty(secondNames);
    }

    private sealed class MutableCell : Fuookami.Ospf.Utils.Concept.ICopyable<MutableCell> {
        public int Value { get; set; }
        public MutableCell Copy() => new() { Value = Value };
    }
}
