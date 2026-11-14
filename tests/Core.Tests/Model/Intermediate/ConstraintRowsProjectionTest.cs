#nullable enable

using System;
using System.Collections.Generic;
using Xunit;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
namespace Fuookami.Ospf.Core.Tests.Model.Intermediate;

public class ConstraintRowsProjectionTest {
    [Fact]
    public void LinearRows_ShouldProjectCurrentSparseRowsAndKeepHeldRowsReadOnly() {
        var row = new SparseVector([new SparseVectorEntry(0, Flt64.One)]);
        var sparse = new SparseMatrix([row]);
        using var batch = new LinearConstraintBatch(
            lhs: sparse,
            signs: [ConstraintRelation.LessEqual],
            rhs: [Flt64.One],
            names: ["row"],
            sources: [ConstraintSource.Origin]
        );
        IReadOnlyList<IReadOnlyList<LinearConstraintCell>> view = batch.Lhs;
        IReadOnlyList<LinearConstraintCell> held = view[0];

        row.Entries[0] = new SparseVectorEntry(1, new Flt64(-0.0));
        row.Add(2, new Flt64(3));
        sparse.Rows.Insert(0, row);

        Assert.Equal(2, view.Count);
        Assert.False(view is IList<IReadOnlyList<LinearConstraintCell>>);
        Assert.Throws<NotSupportedException>(() => ((IList<LinearConstraintCell>)held).Clear());
        Assert.Equal(new LinearConstraintCell(0, 0, Flt64.One), Assert.Single(held));
        Assert.Equal(new[] { 0, 0 }, new[] { view[0][0].RowIndex, view[0][1].RowIndex });
        Assert.Equal(new[] { 1, 1 }, new[] { view[1][0].RowIndex, view[1][1].RowIndex });
        Assert.Equal(long.MinValue, BitConverter.DoubleToInt64Bits(view[0][0].Coefficient.ToDouble()));

        using var copy = (LinearConstraintBatch)batch.Copy();
        row.Entries.Clear();
        sparse.Rows.RemoveAt(0);
        sparse.Rows[0] = new SparseVector([new SparseVectorEntry(3, new Flt64(4))]);
        Assert.Equal(3, Assert.Single(view[0]).ColIndex);
        Assert.Equal(2, copy.Lhs.Count);
        Assert.Equal(new[] { 1, 2 }, new[] { copy.Lhs[0][0].ColIndex, copy.Lhs[0][1].ColIndex });
    }

    [Fact]
    public void QuadraticRows_ShouldProjectCurrentSparseRowsAndKeepHeldRowsReadOnly() {
        var row = new SparseQuadraticVector([new SparseQuadraticEntry(0, 0, Flt64.One)]);
        var sparse = new SparseQuadraticMatrix([row]);
        using var batch = new QuadraticConstraintBatch(
            lhs: sparse,
            signs: [ConstraintRelation.LessEqual],
            rhs: [Flt64.One],
            names: ["row"],
            sources: [ConstraintSource.Origin]
        );
        IReadOnlyList<IReadOnlyList<QuadraticConstraintCell>> view = batch.Lhs;
        IReadOnlyList<QuadraticConstraintCell> held = view[0];

        row.Entries[0] = new SparseQuadraticEntry(
            Col1: 1,
            Col2: -1,
            Value: new Flt64(-0.0)
        );
        row.Add(
            col1: 2,
            col2: 2,
            value: new Flt64(3)
        );
        sparse.Rows.Insert(0, row);

        Assert.Equal(2, view.Count);
        Assert.False(view is IList<IReadOnlyList<QuadraticConstraintCell>>);
        Assert.Throws<NotSupportedException>(() => ((IList<QuadraticConstraintCell>)held).Clear());
        Assert.Equal(new QuadraticConstraintCell(0, 0, 0, Flt64.One), Assert.Single(held));
        Assert.Equal(new[] { 0, 0 }, new[] { view[0][0].RowIndex, view[0][1].RowIndex });
        Assert.Equal(new[] { 1, 1 }, new[] { view[1][0].RowIndex, view[1][1].RowIndex });
        Assert.Equal(-1, view[0][0].Col2);
        Assert.Equal(long.MinValue, BitConverter.DoubleToInt64Bits(view[0][0].Coefficient.ToDouble()));

        using var copy = (QuadraticConstraintBatch)batch.Copy();
        row.Entries.Clear();
        sparse.Rows.RemoveAt(0);
        sparse.Rows[0] = new SparseQuadraticVector([new SparseQuadraticEntry(
            Col1: 3,
            Col2: -1,
            Value: new Flt64(4)
        )]);
        Assert.Equal(3, Assert.Single(view[0]).Col1);
        Assert.Equal(2, copy.Lhs.Count);
        Assert.Equal(new[] { -1, 2 }, new[] { copy.Lhs[0][0].Col2, copy.Lhs[0][1].Col2 });
    }
}
