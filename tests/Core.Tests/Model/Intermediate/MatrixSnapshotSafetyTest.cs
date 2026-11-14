#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using SymbolFlatten = Fuookami.Ospf.Core.Symbol.Flatten;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
namespace Fuookami.Ospf.Core.Tests.Model.Intermediate;

public class MatrixSnapshotSafetyTest {
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task AddedRows_ShouldInvalidateOldMatrixButNotFreshDump(bool quadratic, bool rollback) {
        using Fixture fixture = await Fixture.Create(quadratic);
        using IDisposable old = fixture.Dump();
        IConstraintWriteTransaction? transaction = rollback ? fixture.Model.BeginConstraintWrite().Value : null;
        fixture.AddRow().IsOk.Should().BeTrue();

        fixture.Validate(old).IsFailed.Should().BeTrue();
        fixture.Apply(old, new[] { new Flt64(5) }).IsFailed.Should().BeTrue();
        fixture.YResult.Should().Be(new Flt64(3));
        using IDisposable fresh = fixture.Dump();
        fixture.Validate(fresh).IsOk.Should().BeTrue();
        if (transaction is not null) {
            transaction.Rollback().IsOk.Should().BeTrue();
            fixture.Validate(old).IsOk.Should().BeTrue();
            fixture.Validate(fresh).IsFailed.Should().BeTrue();
        }
        else {
            fixture.Apply(fresh, new[] { new Flt64(5) }).IsOk.Should().BeTrue();
            fixture.YResult.Should().Be(new Flt64(5));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangedMechanismRowCells_ShouldInvalidateOldMatrix(bool quadratic) {
        using Fixture fixture = await Fixture.Create(quadratic);
        using IDisposable matrix = fixture.Dump();
        IReadOnlyList<ICell<Flt64>> cells = quadratic
            ? ((IAbstractQuadraticMechanismModel<Flt64>)fixture.Model).Constraints[0].Lhs
            : ((IMechanismModel<Flt64>)fixture.Model).Constraints[0].Lhs;
        ((IList<ICell<Flt64>>)cells).Clear();

        fixture.Validate(matrix).IsFailed.Should().BeTrue();
        fixture.Apply(matrix, new[] { new Flt64(5) }).IsFailed.Should().BeTrue();
        fixture.YResult.Should().Be(new Flt64(3));
        using IDisposable fresh = fixture.Dump();
        fixture.Validate(fresh).IsOk.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReplacedMechanismRow_ShouldBeComparedByIdentity(bool quadratic) {
        using Fixture fixture = await Fixture.Create(quadratic);
        using IDisposable matrix = fixture.Dump();
        if (quadratic) {
            var rows = (IList<QuadraticConstraintImpl<Flt64>>)((IAbstractQuadraticMechanismModel<Flt64>)fixture.Model).Constraints;
            QuadraticConstraintImpl<Flt64> original = rows[0];
            rows[0] = original with { Rhs = new Flt64(99) };
            fixture.Validate(matrix).IsFailed.Should().BeTrue();
            rows[0] = original;
        }
        else {
            var rows = (IList<LinearConstraintImpl<Flt64>>)((IMechanismModel<Flt64>)fixture.Model).Constraints;
            LinearConstraintImpl<Flt64> original = rows[0];
            rows[0] = original with { Rhs = new Flt64(99) };
            fixture.Validate(matrix).IsFailed.Should().BeTrue();
            rows[0] = original;
        }
        fixture.Validate(matrix).IsOk.Should().BeTrue();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ChangedCustomCell_ShouldInvalidateOldMatrix(bool quadratic, bool changeToken) {
        using Fixture fixture = await Fixture.Create(quadratic);
        IReadOnlyList<ICell<Flt64>> cells = quadratic
            ? ((IAbstractQuadraticMechanismModel<Flt64>)fixture.Model).Constraints[0].Lhs
            : ((IMechanismModel<Flt64>)fixture.Model).Constraints[0].Lhs;
        var mutable = (IList<ICell<Flt64>>)cells;
        if (quadratic) {
            var cell = new MutableQuadraticCell((IQuadraticCell<Flt64>)mutable[0]);
            mutable[0] = cell;
            using IDisposable matrix = fixture.Dump();
            if (changeToken) {
                cell.Token1 = fixture.FreeToken;
            }
            else {
                cell.Coefficient = new Flt64(2);
            }
            fixture.Validate(matrix).IsFailed.Should().BeTrue();
        }
        else {
            var cell = new MutableLinearCell((ILinearCell<Flt64>)mutable[0]);
            mutable[0] = cell;
            using IDisposable matrix = fixture.Dump();
            if (changeToken) {
                cell.Token = fixture.FreeToken;
            }
            else {
                cell.Coefficient = new Flt64(2);
            }
            fixture.Validate(matrix).IsFailed.Should().BeTrue();
        }
        using IDisposable fresh = fixture.Dump();
        fixture.Validate(fresh).IsOk.Should().BeTrue();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Dump_ShouldRejectUnreadableOrChangingCustomCell(bool quadratic, bool unreadable) {
        using Fixture fixture = await Fixture.Create(quadratic);
        IReadOnlyList<ICell<Flt64>> cells = quadratic
            ? ((IAbstractQuadraticMechanismModel<Flt64>)fixture.Model).Constraints[0].Lhs
            : ((IMechanismModel<Flt64>)fixture.Model).Constraints[0].Lhs;
        var mutable = (IList<ICell<Flt64>>)cells;
        if (quadratic) {
            mutable[0] = new MutableQuadraticCell((IQuadraticCell<Flt64>)mutable[0]) {
                ThrowOnRead = unreadable,
                FlipOnSecondRead = !unreadable,
            };
        }
        else {
            mutable[0] = new MutableLinearCell((ILinearCell<Flt64>)mutable[0]) {
                ThrowOnRead = unreadable,
                FlipOnSecondRead = !unreadable,
            };
        }

        fixture.DumpFailed().Should().BeTrue();
        fixture.YResult.Should().Be(new Flt64(3));
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(false, 4)]
    [InlineData(false, 5)]
    [InlineData(false, 6)]
    [InlineData(false, 7)]
    [InlineData(false, 8)]
    [InlineData(false, 9)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    [InlineData(true, 4)]
    [InlineData(true, 5)]
    [InlineData(true, 6)]
    [InlineData(true, 7)]
    [InlineData(true, 8)]
    [InlineData(true, 9)]
    public async Task ChangedMatrix_ShouldRejectSolutionAndNoIncumbentEvidence(bool quadratic, int change) {
        using Fixture fixture = await Fixture.Create(quadratic);
        using IDisposable matrix = fixture.Dump();
        fixture.Validate(matrix).IsOk.Should().BeTrue();
        if (change is 2 or >= 4 and <= 8) {
            Action attempt = () => Mutate(matrix, change);
            attempt.Should().Throw<NotSupportedException>();
            fixture.Validate(matrix).IsOk.Should().BeTrue();
            fixture.Apply(matrix, new[] { new Flt64(5) }).IsOk.Should().BeTrue();
            fixture.YResult.Should().Be(new Flt64(5));
            return;
        }
        Mutate(matrix, change);
        bool read = false;

        fixture.Validate(matrix).IsFailed.Should().BeTrue();
        fixture.Apply(matrix, new CallbackSolution(() => read = true)).IsFailed.Should().BeTrue();
        read.Should().BeFalse();
        fixture.YResult.Should().Be(new Flt64(3));
    }

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
    [InlineData(false, 11)]
    [InlineData(false, 12)]
    [InlineData(false, 13)]
    [InlineData(false, 14)]
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
    [InlineData(true, 11)]
    [InlineData(true, 12)]
    [InlineData(true, 13)]
    [InlineData(true, 14)]
    public async Task RestoredSparseChanges_ShouldStillInvalidateOldMatrix(bool quadratic, int change) {
        using Fixture fixture = await Fixture.Create(quadratic);
        using IDisposable matrix = fixture.Dump();
        fixture.Validate(matrix).IsOk.Should().BeTrue();

        ChangeAndRestoreSparse(matrix, change);

        fixture.Validate(matrix).IsFailed.Should().BeTrue();
        fixture.Apply(matrix, new[] { new Flt64(5) }).IsFailed.Should().BeTrue();
        fixture.YResult.Should().Be(new Flt64(3));
        using IDisposable fresh = fixture.Dump();
        fixture.Validate(fresh).IsOk.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RestoredSparseChangeDuringSolutionRead_ShouldNotWriteResults(bool quadratic) {
        using Fixture fixture = await Fixture.Create(quadratic);
        using IDisposable matrix = fixture.Dump();
        var solution = new CallbackSolution(() => ChangeAndRestoreSparse(matrix, 0));

        fixture.Apply(matrix, solution).IsFailed.Should().BeTrue();
        fixture.YResult.Should().Be(new Flt64(3));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ZeroSparseEntry_ShouldBeRetainedAndInvalidateMatrix(bool quadratic, bool negative) {
        using Fixture fixture = await Fixture.Create(quadratic);
        using IDisposable matrix = fixture.Dump();
        var value = new Flt64(negative ? -0.0 : 0.0);
        if (matrix is LinearTriadModel linear) {
            SparseVector row = ((LinearConstraintBatch)linear.Constraints).SparseLhs.Rows[0];
            int count = row.Count;
            row.Add(2, value);
            row.Count.Should().Be(count + 1);
            BitConverter.DoubleToInt64Bits(row.Entries[count].Value.ToDouble()).Should().Be(BitConverter.DoubleToInt64Bits(value.ToDouble()));
            LinearConstraintCell projected = linear.Constraints.Lhs[0][count];
            projected.ColIndex.Should().Be(2);
            BitConverter.DoubleToInt64Bits(projected.Coefficient.ToDouble()).Should().Be(BitConverter.DoubleToInt64Bits(value.ToDouble()));
        }
        else {
            SparseQuadraticVector row = ((QuadraticConstraintBatch)((QuadraticTetradModel)matrix).Constraints).SparseLhs.Rows[0];
            int count = row.Count;
            row.Add(
                col1: 2,
                col2: 2,
                value: value
            );
            row.Count.Should().Be(count + 1);
            BitConverter.DoubleToInt64Bits(row.Entries[count].Value.ToDouble()).Should().Be(BitConverter.DoubleToInt64Bits(value.ToDouble()));
            QuadraticConstraintCell projected = ((QuadraticTetradModel)matrix).Constraints.Lhs[0][count];
            projected.Col2.Should().Be(2);
            BitConverter.DoubleToInt64Bits(projected.Coefficient.ToDouble()).Should().Be(BitConverter.DoubleToInt64Bits(value.ToDouble()));
        }
        fixture.Validate(matrix).IsFailed.Should().BeTrue();
        fixture.Apply(matrix, new[] { new Flt64(5) }).IsFailed.Should().BeTrue();
        fixture.YResult.Should().Be(new Flt64(3));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectedSparseRemoval_ShouldNotInvalidateMatrix(bool quadratic) {
        using Fixture fixture = await Fixture.Create(quadratic);
        using IDisposable matrix = fixture.Dump();
        if (matrix is LinearTriadModel linear) {
            IList<SparseVectorEntry> entries = ((LinearConstraintBatch)linear.Constraints).SparseLhs.Rows[0].Entries;
            entries.Remove(new SparseVectorEntry(99, Flt64.One)).Should().BeFalse();
            Action remove = () => entries.RemoveAt(99);
            remove.Should().Throw<ArgumentOutOfRangeException>();
        }
        else {
            IList<SparseQuadraticEntry> entries = ((QuadraticConstraintBatch)((QuadraticTetradModel)matrix).Constraints).SparseLhs.Rows[0].Entries;
            entries.Remove(new SparseQuadraticEntry(
                Col1: 99,
                Col2: 99,
                Value: Flt64.One
            )).Should().BeFalse();
            Action remove = () => entries.RemoveAt(99);
            remove.Should().Throw<ArgumentOutOfRangeException>();
        }
        fixture.Validate(matrix).IsOk.Should().BeTrue();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task MutationDuringSolutionRead_ShouldNotWriteResults(bool quadratic, bool matrixChange) {
        using Fixture fixture = await Fixture.Create(quadratic);
        using IDisposable matrix = fixture.Dump();
        var solution = new CallbackSolution(() => {
            if (matrixChange) {
                Mutate(matrix, 0);
            }
            else {
                fixture.AddRow().IsOk.Should().BeTrue();
            }
        });

        fixture.Apply(matrix, solution).IsFailed.Should().BeTrue();
        fixture.YResult.Should().Be(new Flt64(3));
    }

    [Theory]
    [InlineData(false, 2)]
    [InlineData(false, 4)]
    [InlineData(false, 5)]
    [InlineData(false, 6)]
    [InlineData(false, 7)]
    [InlineData(false, 8)]
    [InlineData(true, 2)]
    [InlineData(true, 4)]
    [InlineData(true, 5)]
    [InlineData(true, 6)]
    [InlineData(true, 7)]
    [InlineData(true, 8)]
    public async Task ReadOnlyMutationDuringSolutionRead_ShouldNotWriteResults(bool quadratic, int change) {
        using Fixture fixture = await Fixture.Create(quadratic);
        using IDisposable matrix = fixture.Dump();
        var solution = new CallbackSolution(() => Mutate(matrix, change));

        fixture.Apply(matrix, solution).IsFailed.Should().BeTrue();
        fixture.YResult.Should().Be(new Flt64(3));
        fixture.Validate(matrix).IsOk.Should().BeTrue();
        fixture.Apply(matrix, new[] { new Flt64(5) }).IsOk.Should().BeTrue();
        fixture.YResult.Should().Be(new Flt64(5));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MatrixRhs_ShouldDetectSignedZeroChanges(bool quadratic) {
        using Fixture fixture = await Fixture.Create(quadratic);
        using IDisposable matrix = fixture.Dump(new Flt64(10));
        IReadOnlyList<Flt64> rhs = matrix is LinearTriadModel linear ? linear.Constraints.Rhs : ((QuadraticTetradModel)matrix).Constraints.Rhs;
        BitConverter.DoubleToInt64Bits(rhs[0].ToDouble()).Should().Be(0);
        Action change = () => ((IList<Flt64>)rhs)[0] = new Flt64(-0.0);
        change.Should().Throw<NotSupportedException>();
        fixture.Validate(matrix).IsOk.Should().BeTrue();
        if (matrix is LinearTriadModel linearModel) {
            var batch = (LinearConstraintBatch)linearModel.Constraints;
            using var changed = new LinearTriadModel(
                variables: linearModel.Variables,
                constraints: new LinearConstraintBatch(
                    lhs: batch.SparseLhs,
                    signs: batch.Signs,
                    rhs: new[] { new Flt64(-0.0) },
                    names: batch.Names,
                    sources: batch.Sources
                ),
                objective: linearModel.Objective,
                name: linearModel.Name
            );
            MechanismMatrixSnapshot.Capture(batch, linearModel.Objective).Matches(changed).Should().BeFalse();
        }
        else {
            var quadraticMatrix = (QuadraticTetradModel)matrix;
            var batch = (QuadraticConstraintBatch)quadraticMatrix.Constraints;
            using var changed = new QuadraticTetradModel(
                variables: quadraticMatrix.Variables,
                constraints: new QuadraticConstraintBatch(
                    lhs: batch.SparseLhs,
                    signs: batch.Signs,
                    rhs: new[] { new Flt64(-0.0) },
                    names: batch.Names,
                    sources: batch.Sources
                ),
                objective: quadraticMatrix.Objective,
                name: quadraticMatrix.Name
            );
            MechanismMatrixSnapshot.Capture(batch, quadraticMatrix.Objective).Matches(changed).Should().BeFalse();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyMatrix_ShouldRejectDisposal(bool quadratic) {
        using AbstractMetaModel<Flt64> parent = quadratic ? new QuadraticMetaModel<Flt64>() : new LinearMetaModel<Flt64>();
        if (parent is LinearMetaModel<Flt64> linear) {
            using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(linear)).Value;
            LinearTriadModel matrix = model.ToLinearTriadModel().Value;
            model.ValidateSolverTokenSnapshot(matrix).IsOk.Should().BeTrue();
            matrix.Dispose();
            model.ValidateSolverTokenSnapshot(matrix).IsFailed.Should().BeTrue();
        }
        else {
            using QuadraticMechanismModel<Flt64> model = (await QuadraticMechanismModel<Flt64>.InvokeAsync((QuadraticMetaModel<Flt64>)parent)).Value;
            QuadraticTetradModel matrix = model.ToQuadraticTetradModel().Value;
            model.ValidateSolverTokenSnapshot(matrix).IsOk.Should().BeTrue();
            matrix.Dispose();
            model.ValidateSolverTokenSnapshot(matrix).IsFailed.Should().BeTrue();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DumpedRows_ShouldNotKeepSourceOrTokensAlive(bool quadratic) {
        (IDisposable matrix, WeakReference[] witnesses) = OrphanedMatrix(quadratic);
        using (matrix) {
            for (int i = 0; i < 3; i++) {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            foreach (WeakReference witness in witnesses) {
                witness.IsAlive.Should().BeFalse();
            }
            GC.KeepAlive(matrix);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (IDisposable Matrix, WeakReference[] Witnesses) OrphanedMatrix(bool quadratic) {
        Fixture fixture = Task.Run(() => Fixture.Create(quadratic)).GetAwaiter().GetResult();
        IDisposable matrix = fixture.Dump();
        return (matrix, fixture.Witnesses(quadratic));
    }

    private static void Mutate(IDisposable matrix, int change) {
        if (matrix is LinearTriadModel linear) {
            var batch = (LinearConstraintBatch)linear.Constraints;
            switch (change) {
                case 0: batch.SparseLhs.Rows[0].Entries.Clear(); break;
                case 1: batch.SparseLhs.Rows.Clear(); break;
                case 2: ((IList<LinearConstraintCell>)batch.Lhs[0]).Clear(); break;
                case 4: ((IList<ConstraintRelation>)batch.Signs)[0] = ConstraintRelation.Equal; break;
                case 5: ((IList<Flt64>)batch.Rhs)[0] = new Flt64(99); break;
                case 6: ((IList<string>)batch.Names)[0] = "changed"; break;
                case 7: ((IList<ConstraintSource>)batch.Sources)[0] = ConstraintSource.Dual; break;
                case 8: ((IList<LinearObjectiveCell>)linear.Objective.Cells).Clear(); break;
                default: linear.Dispose(); break;
            }
        }
        else {
            var quadratic = (QuadraticTetradModel)matrix;
            var batch = (QuadraticConstraintBatch)quadratic.Constraints;
            switch (change) {
                case 0: batch.SparseLhs.Rows[0].Entries.Clear(); break;
                case 1: batch.SparseLhs.Rows.Clear(); break;
                case 2: ((IList<QuadraticConstraintCell>)batch.Lhs[0]).Clear(); break;
                case 4: ((IList<ConstraintRelation>)batch.Signs)[0] = ConstraintRelation.Equal; break;
                case 5: ((IList<Flt64>)batch.Rhs)[0] = new Flt64(99); break;
                case 6: ((IList<string>)batch.Names)[0] = "changed"; break;
                case 7: ((IList<ConstraintSource>)batch.Sources)[0] = ConstraintSource.Dual; break;
                case 8: ((IList<QuadraticObjectiveCell>)quadratic.Objective.Cells).Clear(); break;
                default: quadratic.Dispose(); break;
            }
        }
    }

    private static void ChangeAndRestoreSparse(IDisposable matrix, int change) {
        if (matrix is LinearTriadModel linear) {
            SparseMatrix sparse = ((LinearConstraintBatch)linear.Constraints).SparseLhs;
            if (change < 4 || change is >= 9 and <= 11) {
                ChangeAndRestore(
                    values: sparse.Rows[0].Entries,
                    replacement: new SparseVectorEntry(0, new Flt64(2)),
                    change: change < 4 ? change : change - 5
                );
            }
            else if (change < 8 || change >= 12) {
                ChangeAndRestore(
                    values: sparse.Rows,
                    replacement: new SparseVector(),
                    change: change < 8 ? change - 4 : change - 8
                );
            }
            else {
                sparse.ScaleInPlace(new Flt64(2));
                sparse.ScaleInPlace(new Flt64(0.5));
            }
        }
        else {
            SparseQuadraticMatrix sparse = ((QuadraticConstraintBatch)((QuadraticTetradModel)matrix).Constraints).SparseLhs;
            if (change < 4 || change is >= 9 and <= 11) {
                ChangeAndRestore(
                    values: sparse.Rows[0].Entries,
                    replacement: new SparseQuadraticEntry(
                        Col1: 0,
                        Col2: 0,
                        Value: new Flt64(2)
                    ),
                    change: change < 4 ? change : change - 5
                );
            }
            else if (change < 8 || change >= 12) {
                ChangeAndRestore(
                    values: sparse.Rows,
                    replacement: new SparseQuadraticVector(),
                    change: change < 8 ? change - 4 : change - 8
                );
            }
            else {
                sparse.ScaleInPlace(new Flt64(2));
                sparse.ScaleInPlace(new Flt64(0.5));
            }
        }
    }

    private static void ChangeAndRestore<T>(IList<T> values, T replacement, int change) {
        T original = values[0];
        switch (change) {
            case 0:
                values[0] = replacement;
                values[0] = original;
                break;
            case 1:
                values.RemoveAt(0);
                values.Insert(0, original);
                break;
            case 2:
                values.Clear();
                values.Add(original);
                break;
            case 3:
                values.Insert(0, replacement);
                values.RemoveAt(0);
                break;
            case 4:
                values.Remove(original);
                values.Insert(0, original);
                break;
            case 5:
                ((IList)values)[0] = replacement;
                ((IList)values)[0] = original;
                break;
            default:
                values[0] = original;
                break;
        }
    }

    private sealed class Fixture : IDisposable {
        private readonly AbstractMetaModel<Flt64> _parent;
        private readonly RealVar _x;
        private readonly RealVar _y;
        public IAbstractLinearMechanismModel<Flt64> Model { get; }
        public Flt64? YResult => _parent.Tokens.Find(_y)!.Result;
        public Token<Flt64> FreeToken => Model.Tokens.Find(_y)!;

        private Fixture(AbstractMetaModel<Flt64> parent, IAbstractLinearMechanismModel<Flt64> model, RealVar x, RealVar y) {
            _parent = parent;
            Model = model;
            _x = x;
            _y = y;
        }

        public static async Task<Fixture> Create(bool quadratic) {
            AbstractMetaModel<Flt64> parent = quadratic ? new QuadraticMetaModel<Flt64>() : new LinearMetaModel<Flt64>();
            var x = new RealVar("matrix-fixed-x");
            var y = new RealVar("matrix-free-y");
            parent.Add(new IVariableItem[] { x, y }).IsOk.Should().BeTrue();
            parent.Tokens.SetSolverSolution(new[] { new Flt64(2), new Flt64(3) });
            IAbstractLinearMechanismModel<Flt64> model;
            if (parent is LinearMetaModel<Flt64> linear) {
                linear.AddObject(
                    category: ObjectCategory.Minimum,
                    flattenData: new SymbolFlatten.LinearFlattenData<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, y) }, Flt64.Zero),
                    name: "objective",
                    displayName: null
                ).IsOk.Should().BeTrue();
                model = (await LinearMechanismModel<Flt64>.InvokeAsync(linear)).Value;
            }
            else {
                var quadraticParent = (QuadraticMetaModel<Flt64>)parent;
                quadraticParent.AddObject(
                    category: ObjectCategory.Minimum,
                    flattenData: new SymbolFlatten.QuadraticFlattenData<Flt64>(new[] { new QuadraticMonomial<Flt64>(Flt64.One, y) }, Flt64.Zero),
                    name: "objective",
                    displayName: null
                ).IsOk.Should().BeTrue();
                model = (await QuadraticMechanismModel<Flt64>.InvokeAsync(quadraticParent)).Value;
            }
            var fixture = new Fixture(
                parent: parent,
                model: model,
                x: x,
                y: y
            );
            fixture.AddRow().IsOk.Should().BeTrue();
            return fixture;
        }

        public Try AddRow() => Model.AddConstraint(
            relation: new LinearInequality<Flt64>(
                Lhs: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, _x), new LinearMonomial<Flt64>(Flt64.One, _y) }, Flt64.Zero),
                Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(10)),
                Comparison: Comparison.LE
            ),
            name: "row",
            from: (Fuookami.Ospf.Core.Symbol.IIntermediateSymbol?)null
        );

        public IDisposable Dump(Flt64? fixedValue = null) {
            var fixedValues = new Dictionary<IVariableItem, Flt64> { [_x] = fixedValue ?? Flt64.One };
            return Model switch {
                LinearMechanismModel<Flt64> linear => linear.ToLinearTriadModel(fixedValues).Value,
                QuadraticMechanismModel<Flt64> quadratic => quadratic.ToQuadraticTetradModel(fixedValues).Value,
                _ => throw new InvalidOperationException(),
            };
        }

        public bool DumpFailed() {
            return Model switch {
                LinearMechanismModel<Flt64> linear => linear.ToLinearTriadModel().IsFailed,
                QuadraticMechanismModel<Flt64> quadratic => quadratic.ToQuadraticTetradModel().IsFailed,
                _ => throw new InvalidOperationException(),
            };
        }

        public Try Validate(IDisposable matrix) => (Model, matrix) switch {
            (LinearMechanismModel<Flt64> linear, LinearTriadModel triad) => linear.ValidateSolverTokenSnapshot(triad),
            (QuadraticMechanismModel<Flt64> quadratic, QuadraticTetradModel tetrad) => quadratic.ValidateSolverTokenSnapshot(tetrad),
            _ => throw new InvalidOperationException(),
        };

        public Try Apply(IDisposable matrix, IReadOnlyList<Flt64> solution) => (Model, matrix) switch {
            (LinearMechanismModel<Flt64> linear, LinearTriadModel triad) => linear.ApplySolverSolution(
                matrix: triad,
                solution: solution
            ),
            (QuadraticMechanismModel<Flt64> quadratic, QuadraticTetradModel tetrad) => quadratic.ApplySolverSolution(
                matrix: tetrad,
                solution: solution
            ),
            _ => throw new InvalidOperationException(),
        };

        public WeakReference[] Witnesses(bool quadratic) {
            IReadOnlyList<ICell<Flt64>> cells = quadratic
                ? ((IAbstractQuadraticMechanismModel<Flt64>)Model).Constraints[0].Lhs
                : ((IMechanismModel<Flt64>)Model).Constraints[0].Lhs;
            object row = quadratic
                ? ((IAbstractQuadraticMechanismModel<Flt64>)Model).Constraints[0]
                : ((IMechanismModel<Flt64>)Model).Constraints[0];
            return new[] {
                new WeakReference(Model), new WeakReference(_parent), new WeakReference(FreeToken),
                new WeakReference(row), new WeakReference(cells[0]),
            };
        }

        public void Dispose() {
            Model.Dispose();
            _parent.Dispose();
        }
    }

    private sealed class MutableLinearCell(ILinearCell<Flt64> source) : ILinearCell<Flt64> {
        private Flt64 _coefficient = source.Coefficient;
        private int _reads;
        public bool ThrowOnRead { get; init; }
        public bool FlipOnSecondRead { get; init; }
        public Flt64 Coefficient {
            get {
                if (ThrowOnRead) {
                    throw new InvalidOperationException("unreadable");
                }
                if (FlipOnSecondRead && ++_reads == 2) {
                    _coefficient = new Flt64(2);
                }
                return _coefficient;
            }
            set => _coefficient = value;
        }
        public Token<Flt64> Token { get; set; } = source.Token;
        public Flt64? Evaluate() => source.Evaluate();
        public Flt64? Evaluate(IReadOnlyList<Flt64> solution) => source.Evaluate(solution);
        public Flt64? Evaluate(IReadOnlyDictionary<VariableItemKey, Flt64> solution) => source.Evaluate(solution);
    }

    private sealed class MutableQuadraticCell(IQuadraticCell<Flt64> source) : IQuadraticCell<Flt64> {
        private Flt64 _coefficient = source.Coefficient;
        private int _reads;
        public bool ThrowOnRead { get; init; }
        public bool FlipOnSecondRead { get; init; }
        public Flt64 Coefficient {
            get {
                if (ThrowOnRead) {
                    throw new InvalidOperationException("unreadable");
                }
                if (FlipOnSecondRead && ++_reads == 2) {
                    _coefficient = new Flt64(2);
                }
                return _coefficient;
            }
            set => _coefficient = value;
        }
        public Token<Flt64> Token1 { get; set; } = source.Token1;
        public Token<Flt64>? Token2 { get; } = source.Token2;
        public Flt64? Evaluate() => source.Evaluate();
        public Flt64? Evaluate(IReadOnlyList<Flt64> solution) => source.Evaluate(solution);
        public Flt64? Evaluate(IReadOnlyDictionary<VariableItemKey, Flt64> solution) => source.Evaluate(solution);
    }

    private sealed class CallbackSolution(Action callback) : IReadOnlyList<Flt64> {
        public int Count => 1;
        public Flt64 this[int index] {
            get {
                callback();
                return new Flt64(5);
            }
        }
        public IEnumerator<Flt64> GetEnumerator() => ((IEnumerable<Flt64>)new[] { new Flt64(5) }).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
