#nullable enable

using System;
using System.Collections.Generic;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
namespace Fuookami.Ospf.Core.Tests.Model.Intermediate;

public class ConstraintShapeTest {
    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(false, 3)]
    [InlineData(false, 4)]
    [InlineData(false, 5)]
    [InlineData(false, 6)]
    [InlineData(false, 7)]
    [InlineData(false, 8)]
    [InlineData(false, 9)]
    [InlineData(false, 10)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    [InlineData(true, 3)]
    [InlineData(true, 4)]
    [InlineData(true, 5)]
    [InlineData(true, 6)]
    [InlineData(true, 7)]
    [InlineData(true, 8)]
    [InlineData(true, 9)]
    [InlineData(true, 10)]
    public void ValidateShape_ShouldCheckRowsAndEachMetadataList(bool quadratic, int change) {
        ConstraintRelation[] signs = change switch {
            1 => [],
            2 => [ConstraintRelation.LessEqual, ConstraintRelation.Equal],
            _ => [ConstraintRelation.LessEqual],
        };
        Flt64[] rhs = change switch {
            3 => [],
            4 => [Flt64.One, Flt64.One],
            _ => [Flt64.One],
        };
        string[] names = change switch {
            5 => [],
            6 => ["row", "extra"],
            _ => ["row"],
        };
        ConstraintSource[] sources = change switch {
            7 => [],
            8 => [ConstraintSource.Origin, ConstraintSource.Dual],
            _ => [ConstraintSource.Origin],
        };
        if (quadratic) {
            var sparse = new SparseQuadraticMatrix([new SparseQuadraticVector()]);
            using var batch = new QuadraticConstraintBatch(
                lhs: sparse,
                signs: signs,
                rhs: rhs,
                names: names,
                sources: sources
            );
            if (change == 9) { sparse.Rows.Add(new SparseQuadraticVector()); }
            if (change == 10) { sparse.Rows.Clear(); }
            AssertResult(batch.ValidateShape(), change == 0);
            using ModelConstraint<QuadraticConstraintCell> copy = batch.Copy();
            AssertResult(copy.ValidateShape(), change == 0);
        }
        else {
            var sparse = new SparseMatrix([new SparseVector()]);
            using var batch = new LinearConstraintBatch(
                lhs: sparse,
                signs: signs,
                rhs: rhs,
                names: names,
                sources: sources
            );
            if (change == 9) { sparse.Rows.Add(new SparseVector()); }
            if (change == 10) { sparse.Rows.Clear(); }
            AssertResult(batch.ValidateShape(), change == 0);
            using ModelConstraint<LinearConstraintCell> copy = batch.Copy();
            AssertResult(copy.ValidateShape(), change == 0);
        }
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    public void ValidateShape_ShouldCheckOptionalPriorities(int count, bool valid) {
        using var batch = new PriorityConstraint(new int[count]);
        AssertResult(batch.ValidateShape(), valid);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ValidateShape_ShouldAcceptEmptyAndRejectDisposedBatch(bool quadratic) {
        if (quadratic) {
            var batch = new QuadraticConstraintBatch(
                lhs: new SparseQuadraticMatrix(),
                signs: [],
                rhs: [],
                names: [],
                sources: []
            );
            Assert.True(batch.ValidateShape().IsOk);
            batch.Dispose();
            Assert.Equal(ErrorCode.ApplicationStopped, Assert.IsType<Failed<Success, ErrorCode, Error<ErrorCode>>>(batch.ValidateShape()).Code);
        }
        else {
            var batch = new LinearConstraintBatch(
                lhs: new SparseMatrix(),
                signs: [],
                rhs: [],
                names: [],
                sources: []
            );
            Assert.True(batch.ValidateShape().IsOk);
            batch.Dispose();
            Assert.Equal(ErrorCode.ApplicationStopped, Assert.IsType<Failed<Success, ErrorCode, Error<ErrorCode>>>(batch.ValidateShape()).Code);
        }
    }

    private static void AssertResult(Result<Success, ErrorCode, Error<ErrorCode>> result, bool valid) {
        if (valid) {
            Assert.True(result.IsOk);
        }
        else {
            Assert.Equal(ErrorCode.IllegalArgument, Assert.IsType<Failed<Success, ErrorCode, Error<ErrorCode>>>(result).Code);
        }
    }

    private sealed class PriorityConstraint : ModelConstraint<LinearConstraintCell> {
        private readonly IReadOnlyList<int> _priorities;

        internal PriorityConstraint(IReadOnlyList<int> priorities)
            : base(
                constraintCount: 1,
                signs: [ConstraintRelation.Equal],
                rhs: [Flt64.Zero],
                names: ["row"],
                sources: [ConstraintSource.Origin]
            ) {
            _priorities = priorities;
        }

        public override IReadOnlyList<IReadOnlyList<LinearConstraintCell>> Lhs => [Array.Empty<LinearConstraintCell>()];
        public override IReadOnlyList<int>? Priorities => _priorities;
        public override ModelConstraint<LinearConstraintCell> Copy() => new PriorityConstraint(_priorities);
    }
}
