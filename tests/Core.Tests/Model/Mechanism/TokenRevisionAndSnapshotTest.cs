#nullable enable

using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
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
namespace Fuookami.Ospf.Core.Tests.Model.Mechanism;

public class TokenRevisionAndSnapshotTest {
    [Fact]
    public void RevisionOwners_ShouldNotCollideAcrossNumericTypes() {
        using var floating = new LinearMetaModel<Flt64>();
        IFlt64ValueConverter<FltX> converter = FltX.Zero;
        using var decimalModel = new LinearMetaModel<FltX>(converter: converter);
        LinearModelRevision floatRevision = floating.State.Refresh().Value.Revision;
        LinearModelRevision decimalRevision = decimalModel.State.Refresh().Value.Revision;
        floating.State.RequireCurrent(decimalRevision).IsFailed.Should().BeTrue();
        decimalModel.State.RequireCurrent(floatRevision).IsFailed.Should().BeTrue();

        while (floatRevision.Owner < decimalRevision.Owner) {
            using var next = new LinearMetaModel<Flt64>();
            floatRevision = next.State.Refresh().Value.Revision;
        }
        while (decimalRevision.Owner < floatRevision.Owner) {
            using var next = new LinearMetaModel<FltX>(converter: converter);
            decimalRevision = next.State.Refresh().Value.Revision;
        }

        floatRevision.Owner.Should().NotBe(decimalRevision.Owner);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Revision_ShouldTrackColumnOrderButNotTokenIdentityOrSolution(bool manual) {
        using var model = new LinearMetaModel<Flt64>(
            configuration: new MetaModelConfiguration(ManualTokenAddition: manual)
        );
        var x = new RealVar("revision-x");
        var y = new RealVar("revision-y");
        var z = new RealVar("revision-z");
        model.Add(new IVariableItem[] { x, y, z }).IsOk.Should().BeTrue();
        LinearModelSnapshot<Flt64> initial = model.State.Refresh().Value;

        model.Tokens.SetSolverSolution(new[] { new Flt64(1), new Flt64(2), new Flt64(3) });
        model.State.RequireCurrent(initial.Revision).IsOk.Should().BeTrue();
        Token<Flt64> last = model.Tokens.Find(z)!;
        model.Add(z).IsOk.Should().BeTrue();
        model.Tokens.Find(z).Should().NotBeSameAs(last);
        model.State.Refresh().Value.Revision.Should().Be(initial.Revision);
        model.State.RequireCurrent(initial.Revision).IsOk.Should().BeTrue();

        model.Add(y).IsOk.Should().BeTrue();
        model.State.RequireCurrent(initial.Revision).IsFailed.Should().BeTrue();
        LinearModelSnapshot<Flt64> reordered = model.State.Refresh().Value;
        reordered.Revision.Sequence.Should().Be(initial.Revision.Sequence + 1);
        reordered.Variables.Select(variable => variable.Variable).Should().Equal(new IVariableItem[] { x, z, y });
        initial.Variables.Select(variable => variable.Variable).Should().Equal(new IVariableItem[] { x, y, z });
        model.State.RequireCurrent(reordered.Revision).IsOk.Should().BeTrue();

        using var other = new LinearMetaModel<Flt64>();
        other.Add(new IVariableItem[] { x, z, y }).IsOk.Should().BeTrue();
        other.State.RequireCurrent(reordered.Revision).IsFailed.Should().BeTrue();

        using var singleton = new LinearMetaModel<Flt64>();
        singleton.Add(x).IsOk.Should().BeTrue();
        LinearModelRevision singleRevision = singleton.State.Refresh().Value.Revision;
        singleton.Add(x).IsOk.Should().BeTrue();
        singleton.State.RequireCurrent(singleRevision).IsOk.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectedDuplicate_ShouldPreserveRevisionAndToken(bool manual) {
        using var model = new LinearMetaModel<Flt64>(
            configuration: new MetaModelConfiguration(
                ManualTokenAddition: manual,
                CheckTokenExists: true
            )
        );
        var x = new RealVar("checked-x");
        var y = new RealVar("checked-y");
        model.Add(new IVariableItem[] { x, y }).IsOk.Should().BeTrue();
        Token<Flt64> original = model.Tokens.Find(x)!;
        original.SetResult(new Flt64(5));
        LinearModelRevision revision = model.State.Refresh().Value.Revision;

        model.Add(x).Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.TokenExisted);
        model.Tokens.Find(x).Should().BeSameAs(original);
        original.Result.Should().Be(new Flt64(5));
        model.State.RequireCurrent(revision).IsOk.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LinearReplacement_ShouldKeepOldSnapshotAndMatrixSeparate(bool manual) {
        using var parent = new LinearMetaModel<Flt64>(
            configuration: new MetaModelConfiguration(ManualTokenAddition: manual)
        );
        var x = new RealVar("linear-x");
        var y = new RealVar("linear-y");
        var z = new RealVar("linear-z");
        parent.Add(new IVariableItem[] { x, y, z }).IsOk.Should().BeTrue();
        LinearMonomial<Flt64>[] terms = {
            new LinearMonomial<Flt64>(Flt64.One, x),
            new LinearMonomial<Flt64>(new Flt64(2), y),
            new LinearMonomial<Flt64>(new Flt64(4), z),
        };
        parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new SymbolFlatten.LinearFlattenData<Flt64>(terms, Flt64.Zero),
            name: "objective",
            displayName: null
        ).IsOk.Should().BeTrue();
        parent.AddConstraint(
            relation: new LinearInequality<Flt64>(
                Lhs: new LinearPolynomial<Flt64>(terms, Flt64.Zero),
                Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(50)),
                Comparison: Comparison.LE
            ),
            group: null
        ).IsOk.Should().BeTrue();
        parent.Tokens.SetSolverSolution(new[] { new Flt64(1), new Flt64(2), new Flt64(3) });
        Token<Flt64> original = parent.Tokens.Find(y)!;
        using LinearMechanismModel<Flt64> oldModel = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        using LinearTriadModel oldMatrix = oldModel.ToLinearTriadModel().Value;

        parent.Add(y).IsOk.Should().BeTrue();
        Token<Flt64> replacement = parent.Tokens.Find(y)!;
        replacement.Should().NotBeSameAs(original);
        original.RefreshCallbacks.Should().NotContainKey(parent.Tokens.TokenList);
        original.RefreshCallbacks.Should().ContainKey(oldModel.Tokens.TokenList);
        oldModel.Tokens.SetSolverSolution(new[] { new Flt64(11), new Flt64(22), new Flt64(33) });
        replacement.Result.Should().BeNull();
        replacement.SetResult(new Flt64(44));
        original.Result.Should().Be(new Flt64(22));
        using LinearMechanismModel<Flt64> newModel = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        using LinearTriadModel newMatrix = newModel.ToLinearTriadModel().Value;

        oldMatrix.Variables.Select(variable => variable.Name).Should().Equal(new[] { x.Name, y.Name, z.Name });
        oldMatrix.Variables.Select(variable => variable.InitialResult).Should().Equal(new Flt64?[] { new(1), new(2), new(3) });
        oldMatrix.Constraints.Lhs[0].Select(cell => (cell.ColIndex, cell.Coefficient)).Should().Equal(new[] {
            (0, Flt64.One), (1, new Flt64(2)), (2, new Flt64(4)),
        });
        newMatrix.Variables.Select(variable => variable.Name).Should().Equal(new[] { x.Name, z.Name, y.Name });
        newMatrix.Variables.Select(variable => variable.InitialResult).Should().Equal(new Flt64?[] { new(11), new(33), new(44) });
        newMatrix.Constraints.Lhs[0].Select(cell => (cell.ColIndex, cell.Coefficient)).Should().Equal(new[] {
            (0, Flt64.One), (1, new Flt64(4)), (2, new Flt64(2)),
        });
        oldModel.Tokens.Find(y).Should().BeSameAs(original);
        newModel.Tokens.Find(y).Should().BeSameAs(replacement);
        oldModel.Dispose();
        original.RefreshCallbacks.Should().NotContainKey(oldModel.Tokens.TokenList);
        replacement.RefreshCallbacks.Should().ContainKey(newModel.Tokens.TokenList);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QuadraticReplacement_ShouldKeepOldSnapshotAndMatrixSeparate(bool manual) {
        using var parent = new QuadraticMetaModel<Flt64>(
            configuration: new MetaModelConfiguration(ManualTokenAddition: manual)
        );
        var x = new RealVar("quadratic-x");
        var y = new RealVar("quadratic-y");
        var z = new RealVar("quadratic-z");
        parent.Add(new IVariableItem[] { x, y, z }).IsOk.Should().BeTrue();
        QuadraticMonomial<Flt64>[] terms = {
            new QuadraticMonomial<Flt64>(Flt64.One, x),
            new QuadraticMonomial<Flt64>(
                Coefficient: new Flt64(2),
                Symbol1: y,
                Symbol2: y
            ),
            new QuadraticMonomial<Flt64>(
                Coefficient: new Flt64(4),
                Symbol1: z,
                Symbol2: z
            ),
        };
        parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new SymbolFlatten.QuadraticFlattenData<Flt64>(terms, Flt64.Zero),
            name: "objective",
            displayName: null
        ).IsOk.Should().BeTrue();
        parent.AddConstraint(
            relation: new QuadraticInequalityOf<Flt64>(
                Lhs: new QuadraticPolynomial<Flt64>(terms, Flt64.Zero),
                Rhs: new QuadraticPolynomial<Flt64>(Array.Empty<QuadraticMonomial<Flt64>>(), new Flt64(50)),
                Comparison: Comparison.LE
            ),
            group: null
        ).IsOk.Should().BeTrue();
        parent.Tokens.SetSolverSolution(new[] { new Flt64(1), new Flt64(2), new Flt64(3) });
        Token<Flt64> original = parent.Tokens.Find(y)!;
        using QuadraticMechanismModel<Flt64> oldModel = (await QuadraticMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        using QuadraticTetradModel oldMatrix = oldModel.ToQuadraticTetradModel().Value;

        parent.Add(y).IsOk.Should().BeTrue();
        Token<Flt64> replacement = parent.Tokens.Find(y)!;
        replacement.Should().NotBeSameAs(original);
        original.RefreshCallbacks.Should().NotContainKey(parent.Tokens.TokenList);
        original.RefreshCallbacks.Should().ContainKey(oldModel.Tokens.TokenList);
        oldModel.Tokens.SetSolverSolution(new[] { new Flt64(11), new Flt64(22), new Flt64(33) });
        replacement.Result.Should().BeNull();
        replacement.SetResult(new Flt64(44));
        original.Result.Should().Be(new Flt64(22));
        using QuadraticMechanismModel<Flt64> newModel = (await QuadraticMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        using QuadraticTetradModel newMatrix = newModel.ToQuadraticTetradModel().Value;

        oldMatrix.Variables.Select(variable => variable.Name).Should().Equal(new[] { x.Name, y.Name, z.Name });
        oldMatrix.Variables.Select(variable => variable.InitialResult).Should().Equal(new Flt64?[] { new(1), new(2), new(3) });
        oldMatrix.Constraints.Lhs[0].Select(cell => (cell.Col1, cell.Col2, cell.Coefficient)).Should().Equal(new[] {
            (0, -1, Flt64.One), (1, 1, new Flt64(2)), (2, 2, new Flt64(4)),
        });
        newMatrix.Variables.Select(variable => variable.Name).Should().Equal(new[] { x.Name, z.Name, y.Name });
        newMatrix.Variables.Select(variable => variable.InitialResult).Should().Equal(new Flt64?[] { new(11), new(33), new(44) });
        newMatrix.Constraints.Lhs[0].Select(cell => (cell.Col1, cell.Col2, cell.Coefficient)).Should().Equal(new[] {
            (0, -1, Flt64.One), (1, 1, new Flt64(4)), (2, 2, new Flt64(2)),
        });
        oldModel.Tokens.Find(y).Should().BeSameAs(original);
        newModel.Tokens.Find(y).Should().BeSameAs(replacement);
        oldModel.Dispose();
        original.RefreshCallbacks.Should().NotContainKey(oldModel.Tokens.TokenList);
        replacement.RefreshCallbacks.Should().ContainKey(newModel.Tokens.TokenList);
    }
}
