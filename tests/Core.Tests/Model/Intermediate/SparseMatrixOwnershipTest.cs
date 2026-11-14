#nullable enable

using System.Collections.Generic;
using FluentAssertions;
using Xunit;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
namespace Fuookami.Ospf.Core.Tests.Model.Intermediate;

public class SparseMatrixOwnershipTest {
    [Fact]
    public void LinearConstructors_ShouldCopyListsButRetainSharedRows() {
        var entries = new List<SparseVectorEntry> { new(0, Flt64.One) };
        var row = new SparseVector(entries);
        var rows = new List<SparseVector> { row };
        var first = new SparseMatrix(rows);
        var second = new SparseMatrix(new List<SparseVector> { row });
        var copiedRow = new SparseVector(row.Entries);
        var copiedMatrix = new SparseMatrix(first.Rows);

        entries.Clear();
        rows.Clear();
        first.RowCount.Should().Be(1);
        row.Count.Should().Be(1);
        row.Add(1, new Flt64(2));
        first.Rows[0].Entries.Should().Equal(second.Rows[0].Entries);
        first.Rows[0].Should().BeSameAs(copiedMatrix.Rows[0]);
        copiedRow.Count.Should().Be(1);
        first.Rows[0].Count.Should().Be(2);
    }

    [Fact]
    public void QuadraticConstructors_ShouldCopyListsButRetainSharedRows() {
        var entries = new List<SparseQuadraticEntry> { new(0, 0, Flt64.One) };
        var row = new SparseQuadraticVector(entries);
        var rows = new List<SparseQuadraticVector> { row };
        var first = new SparseQuadraticMatrix(rows);
        var second = new SparseQuadraticMatrix(new List<SparseQuadraticVector> { row });
        var copiedRow = new SparseQuadraticVector(row.Entries);
        var copiedMatrix = new SparseQuadraticMatrix(first.Rows);

        entries.Clear();
        rows.Clear();
        first.RowCount.Should().Be(1);
        row.Count.Should().Be(1);
        row.Add(
            col1: 1,
            col2: 1,
            value: new Flt64(2)
        );
        first.Rows[0].Entries.Should().Equal(second.Rows[0].Entries);
        first.Rows[0].Should().BeSameAs(copiedMatrix.Rows[0]);
        copiedRow.Count.Should().Be(1);
        first.Rows[0].Count.Should().Be(2);
    }

    [Fact]
    public void SharedLinearRowChange_ShouldInvalidateBothSnapshots() {
        var row = new SparseVector(new List<SparseVectorEntry> { new(0, Flt64.One) });
        using LinearTriadModel first = LinearModel(row);
        using LinearTriadModel second = LinearModel(row);
        var firstSnapshot = MechanismMatrixSnapshot.Capture((LinearConstraintBatch)first.Constraints, first.Objective);
        var secondSnapshot = MechanismMatrixSnapshot.Capture((LinearConstraintBatch)second.Constraints, second.Objective);
        firstSnapshot.Matches(first).Should().BeTrue();
        secondSnapshot.Matches(second).Should().BeTrue();

        SparseVectorEntry original = row.Entries[0];
        row.Entries[0] = new SparseVectorEntry(0, new Flt64(2));
        row.Entries[0] = original;

        firstSnapshot.Matches(first).Should().BeFalse();
        secondSnapshot.Matches(second).Should().BeFalse();
        MechanismMatrixSnapshot.Capture((LinearConstraintBatch)first.Constraints, first.Objective).Matches(first).Should().BeTrue();
        MechanismMatrixSnapshot.Capture((LinearConstraintBatch)second.Constraints, second.Objective).Matches(second).Should().BeTrue();
    }

    [Fact]
    public void SharedQuadraticRowChange_ShouldInvalidateBothSnapshots() {
        var row = new SparseQuadraticVector(new List<SparseQuadraticEntry> { new(0, 0, Flt64.One) });
        using QuadraticTetradModel first = QuadraticModel(row);
        using QuadraticTetradModel second = QuadraticModel(row);
        var firstSnapshot = MechanismMatrixSnapshot.Capture((QuadraticConstraintBatch)first.Constraints, first.Objective);
        var secondSnapshot = MechanismMatrixSnapshot.Capture((QuadraticConstraintBatch)second.Constraints, second.Objective);
        firstSnapshot.Matches(first).Should().BeTrue();
        secondSnapshot.Matches(second).Should().BeTrue();

        SparseQuadraticEntry original = row.Entries[0];
        row.Entries[0] = new SparseQuadraticEntry(
            Col1: 0,
            Col2: 0,
            Value: new Flt64(2)
        );
        row.Entries[0] = original;

        firstSnapshot.Matches(first).Should().BeFalse();
        secondSnapshot.Matches(second).Should().BeFalse();
        MechanismMatrixSnapshot.Capture((QuadraticConstraintBatch)first.Constraints, first.Objective).Matches(first).Should().BeTrue();
        MechanismMatrixSnapshot.Capture((QuadraticConstraintBatch)second.Constraints, second.Objective).Matches(second).Should().BeTrue();
    }

    private static LinearTriadModel LinearModel(SparseVector row) {
        var constraints = new LinearConstraintBatch(
            lhs: new SparseMatrix(new List<SparseVector> { row }),
            signs: new[] { ConstraintRelation.LessEqual },
            rhs: new[] { Flt64.One },
            names: new[] { "row" },
            sources: new[] { ConstraintSource.Origin }
        );
        return new LinearTriadModel(
            variables: new List<ModelViewVariable>(),
            constraints: constraints,
            objective: new Objective<LinearObjectiveCell>(ObjectCategory.Minimum, new List<LinearObjectiveCell>()),
            name: "linear"
        );
    }

    private static QuadraticTetradModel QuadraticModel(SparseQuadraticVector row) {
        var constraints = new QuadraticConstraintBatch(
            lhs: new SparseQuadraticMatrix(new List<SparseQuadraticVector> { row }),
            signs: new[] { ConstraintRelation.LessEqual },
            rhs: new[] { Flt64.One },
            names: new[] { "row" },
            sources: new[] { ConstraintSource.Origin }
        );
        return new QuadraticTetradModel(
            variables: new List<ModelViewVariable>(),
            constraints: constraints,
            objective: new Objective<QuadraticObjectiveCell>(ObjectCategory.Minimum, new List<QuadraticObjectiveCell>()),
            name: "quadratic"
        );
    }
}
