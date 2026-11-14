#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Variable;
namespace Fuookami.Ospf.Core.Tests.Model.Intermediate;

public class LinearRelaxationSafetyTest {
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Relaxation_ShouldOwnItsRowsAndSurviveEitherDisposalOrder(bool disposeOriginalFirst) {
        using Fixture fixture = await Fixture.Create();
        LinearTriadModel original = fixture.Model.ToLinearTriadModel().Value;
        LinearTriadModel relaxed = original.RelaxToContinuous().Value;
        Assert.NotSame(original.Constraints, relaxed.Constraints);
        Assert.NotSame(((LinearConstraintBatch)original.Constraints).SparseLhs, ((LinearConstraintBatch)relaxed.Constraints).SparseLhs);
        Assert.Same(Continuous.Instance, relaxed.Variables[0].Type);
        Assert.Same(fixture.Variable, relaxed.Variables[0].Origin);
        Assert.True(fixture.Model.ValidateSolverTokenSnapshot(original, relaxed).IsOk);

        LinearTriadModel first = disposeOriginalFirst ? original : relaxed;
        LinearTriadModel second = disposeOriginalFirst ? relaxed : original;
        first.Dispose();
        Assert.Empty(first.Constraints.Names);
        Assert.Single(second.Constraints.Names);
        Assert.Single(second.Constraints.Lhs[0]);
        Assert.True(fixture.Model.ValidateSolverTokenSnapshot(original, relaxed).IsFailed);
        second.Dispose();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ChangedRows_ShouldRejectSolutionAndEvidence(bool changeOriginal, bool restore) {
        using Fixture fixture = await Fixture.Create();
        using LinearTriadModel original = fixture.Model.ToLinearTriadModel().Value;
        using LinearTriadModel relaxed = original.RelaxToContinuous().Value;
        SparseVector row = ((LinearConstraintBatch)(changeOriginal ? original : relaxed).Constraints).SparseLhs.Rows[0];
        SparseVectorEntry entry = row.Entries[0];
        row.Entries[0] = entry with { Value = new Flt64(2) };
        if (restore) {
            row.Entries[0] = entry;
        }

        Assert.True(fixture.Model.ValidateSolverTokenSnapshot(original, relaxed).IsFailed);
        Assert.True(fixture.Model.RestoreSolverSolution(
            matrix: original,
            solvedMatrix: relaxed,
            solution: [Flt64.One]
        ).IsFailed);
        Assert.True(fixture.Model.ApplySolverSolution(
            matrix: original,
            solvedMatrix: relaxed,
            solution: [Flt64.One]
        ).IsFailed);
        Assert.Equal(Flt64.Zero, fixture.Result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MutationDuringSolutionRead_ShouldNotWriteResults(bool changeOriginal) {
        using Fixture fixture = await Fixture.Create();
        using LinearTriadModel original = fixture.Model.ToLinearTriadModel().Value;
        using LinearTriadModel relaxed = original.RelaxToContinuous().Value;
        var solution = new CallbackSolution(() => {
            SparseVector row = ((LinearConstraintBatch)(changeOriginal ? original : relaxed).Constraints).SparseLhs.Rows[0];
            SparseVectorEntry entry = row.Entries[0];
            row.Entries[0] = entry with { Value = new Flt64(2) };
            row.Entries[0] = entry;
        });

        Assert.True(fixture.Model.ApplySolverSolution(
            matrix: original,
            solvedMatrix: relaxed,
            solution: solution
        ).IsFailed);
        Assert.Equal(Flt64.Zero, fixture.Result);
    }

    [Fact]
    public async Task UnrelatedOrUnchangedRelaxation_ShouldBeDistinguished() {
        using Fixture fixture = await Fixture.Create();
        using LinearTriadModel original = fixture.Model.ToLinearTriadModel().Value;
        using LinearTriadModel other = fixture.Model.ToLinearTriadModel().Value;
        using LinearTriadModel unrelated = other.RelaxToContinuous().Value;
        using LinearTriadModel relaxed = original.RelaxToContinuous().Value;

        Assert.True(fixture.Model.ValidateSolverTokenSnapshot(original, unrelated).IsFailed);
        Assert.True(fixture.Model.ValidateSolverTokenSnapshot(original, original).IsFailed);
        Assert.True(fixture.Model.ApplySolverSolution(
            matrix: original,
            solvedMatrix: relaxed,
            solution: [Flt64.One]
        ).IsOk);
        Assert.Equal(Flt64.One, fixture.Result);
    }

    [Fact]
    public async Task InvalidOrClosedMatrix_ShouldNotCreateRelaxation() {
        using Fixture fixture = await Fixture.Create();
        using LinearTriadModel invalid = fixture.Model.ToLinearTriadModel().Value;
        ((LinearConstraintBatch)invalid.Constraints).SparseLhs.Rows.Clear();
        Assert.True(invalid.RelaxToContinuous().IsFailed);
        using LinearTriadModel closed = fixture.Model.ToLinearTriadModel().Value;
        closed.Dispose();
        Assert.True(closed.RelaxToContinuous().IsFailed);
    }

    private sealed class Fixture : IDisposable {
        private readonly LinearMetaModel<Flt64> _parent;
        public LinearMechanismModel<Flt64> Model { get; }
        public BinVar Variable { get; }
        public Flt64? Result => _parent.Tokens.Find(Variable)!.Result;

        private Fixture(LinearMetaModel<Flt64> parent, LinearMechanismModel<Flt64> model, BinVar variable) {
            _parent = parent;
            Model = model;
            Variable = variable;
        }

        public static async Task<Fixture> Create() {
            var parent = new LinearMetaModel<Flt64>();
            var variable = new BinVar("relaxation-x");
            Assert.True(parent.Add(variable).IsOk);
            parent.Tokens.Find(variable)!.SetResult(Flt64.Zero);
            Assert.True(parent.AddConstraint(
                relation: new LinearInequality<Flt64>(
                    Lhs: new LinearPolynomial<Flt64>([new LinearMonomial<Flt64>(Flt64.One, variable)], Flt64.Zero),
                    Rhs: new LinearPolynomial<Flt64>([], Flt64.One),
                    Comparison: Comparison.LE
                ),
                group: null,
                name: "row"
            ).IsOk);
            LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
            return new Fixture(
                parent: parent,
                model: model,
                variable: variable
            );
        }

        public void Dispose() {
            Model.Dispose();
            _parent.Dispose();
        }
    }

    private sealed class CallbackSolution(Action callback) : IReadOnlyList<Flt64> {
        public int Count => 1;
        public Flt64 this[int index] {
            get {
                callback();
                return Flt64.One;
            }
        }
        public IEnumerator<Flt64> GetEnumerator() => ((IEnumerable<Flt64>)new[] { Flt64.One }).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
