#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using SymbolFlatten = Fuookami.Ospf.Core.Symbol.Flatten;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
namespace Fuookami.Ospf.Core.Tests.Model.Mechanism;

public class ConstraintResolutionTest {
    [Fact]
    public void LinearFactory_ShouldRejectMissingAndNonVariableTermsButIgnoreZero() {
        using var table = new ConcurrentManualTokenTable<Flt64>(LinearCategory.Instance, new List<Fuookami.Ospf.Core.Symbol.IIntermediateSymbol>());
        var x = new RealVar("registered");
        var missing = new RealVar("missing");
        var symbol = new TestSymbol("unexpanded");
        table.Add(x).IsOk.Should().BeTrue();
        LinearMonomial<Flt64>[] terms = new[] {
            new LinearMonomial<Flt64>(Flt64.One, x),
            new LinearMonomial<Flt64>(Flt64.Zero, missing),
            new LinearMonomial<Flt64>(Flt64.Zero, symbol),
        };
        Result<LinearConstraintImpl<Flt64>, ErrorCode, Error<ErrorCode>> valid = LinearConstraintImpl<Flt64>.Create(
            relation: new LinearRelationImpl<Flt64>(new SymbolFlatten.LinearFlattenData<Flt64>(terms, Flt64.Zero), Comparison.LE),
            tokens: table,
            converter: IIntoValue<Flt64>.Identity
        );
        valid.IsOk.Should().BeTrue();
        valid.Value.LinearCells.Should().ContainSingle().Which.Token.Variable.Should().BeSameAs(x);
        foreach (ISymbol invalid in new ISymbol[] { missing, symbol }) {
            Result<LinearConstraintImpl<Flt64>, ErrorCode, Error<ErrorCode>> result = LinearConstraintImpl<Flt64>.Create(
                relation: new LinearRelationImpl<Flt64>(
                    new SymbolFlatten.LinearFlattenData<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, invalid) }, Flt64.Zero),
                    Comparison.LE
                ),
                tokens: table,
                converter: IIntoValue<Flt64>.Identity
            );
            result.Should().BeOfType<Failed<LinearConstraintImpl<Flt64>, ErrorCode, Error<ErrorCode>>>()
                .Which.Code.Should().Be(ErrorCode.IllegalArgument);
        }
    }

    [Fact]
    public void QuadraticFactory_ShouldValidateBothSymbolsAndIgnoreZero() {
        using var table = new ConcurrentManualTokenTable<Flt64>(QuadraticCategory.Instance, new List<Fuookami.Ospf.Core.Symbol.IIntermediateSymbol>());
        var x = new RealVar("registered");
        var missing = new RealVar("missing");
        var symbol = new TestSymbol("unexpanded");
        table.Add(x).IsOk.Should().BeTrue();
        QuadraticMonomial<Flt64>[] validTerms = new[] {
            QuadraticTerm(x, x),
            new QuadraticMonomial<Flt64>(Flt64.One, x),
            QuadraticTerm(missing, symbol) with { Coefficient = Flt64.Zero },
        };
        Result<QuadraticConstraintImpl<Flt64>, ErrorCode, Error<ErrorCode>> valid = QuadraticConstraintImpl<Flt64>.Create(
            relation: new QuadraticRelationImpl<Flt64>(new SymbolFlatten.QuadraticFlattenData<Flt64>(validTerms, Flt64.Zero), Comparison.LE),
            tokens: table,
            converter: IIntoValue<Flt64>.Identity
        );
        valid.IsOk.Should().BeTrue();
        valid.Value.QuadraticCells.Should().HaveCount(2);

        QuadraticMonomial<Flt64>[] invalidTerms = new[] {
            QuadraticTerm(missing, x),
            QuadraticTerm(x, missing),
            QuadraticTerm(symbol, x),
            QuadraticTerm(x, symbol),
        };
        foreach (QuadraticMonomial<Flt64> invalid in invalidTerms) {
            Result<QuadraticConstraintImpl<Flt64>, ErrorCode, Error<ErrorCode>> result = QuadraticConstraintImpl<Flt64>.Create(
                relation: new QuadraticRelationImpl<Flt64>(
                    new SymbolFlatten.QuadraticFlattenData<Flt64>(new[] { invalid }, Flt64.Zero),
                    Comparison.LE
                ),
                tokens: table,
                converter: IIntoValue<Flt64>.Identity
            );
            result.Should().BeOfType<Failed<QuadraticConstraintImpl<Flt64>, ErrorCode, Error<ErrorCode>>>()
                .Which.Code.Should().Be(ErrorCode.IllegalArgument);
        }
    }

    [Fact]
    public async Task LinearModel_ShouldRejectMissingConstraintWithoutAddingARowOrLeakingSnapshot() {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new RealVar("registered");
        var missing = new RealVar("missing");
        parent.Add(x).IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        model.AddConstraint(LinearRow(missing), from: (Fuookami.Ospf.Core.Symbol.IIntermediateSymbol?)null).IsFailed.Should().BeTrue();
        ((IMechanismModel<Flt64>)model).Constraints.Should().BeEmpty();
        parent.AddConstraint(LinearRow(missing), null).IsOk.Should().BeTrue();
        Token<Flt64> token = parent.Tokens.TokenList.Find(x)!;
        int before = token.RefreshCallbacks.Count;
        (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).IsFailed.Should().BeTrue();
        token.RefreshCallbacks.Should().HaveCount(before);
    }

    [Fact]
    public async Task QuadraticModel_ShouldRejectMissingConstraintWithoutAddingARowOrLeakingSnapshot() {
        using var parent = new QuadraticMetaModel<Flt64>();
        var x = new RealVar("registered");
        var missing = new RealVar("missing");
        parent.Add(x).IsOk.Should().BeTrue();
        using QuadraticMechanismModel<Flt64> model = (await QuadraticMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        model.AddConstraint(QuadraticRow(x, missing), from: (Fuookami.Ospf.Core.Symbol.IIntermediateSymbol?)null).IsFailed.Should().BeTrue();
        model.AddConstraint(LinearRow(missing), from: (Fuookami.Ospf.Core.Symbol.IIntermediateSymbol?)null).IsFailed.Should().BeTrue();
        ((IAbstractQuadraticMechanismModel<Flt64>)model).Constraints.Should().BeEmpty();
        parent.AddConstraint(QuadraticRow(x, missing), null).IsOk.Should().BeTrue();
        Token<Flt64> token = parent.Tokens.TokenList.Find(x)!;
        int before = token.RefreshCallbacks.Count;
        (await QuadraticMechanismModel<Flt64>.InvokeAsync(parent)).IsFailed.Should().BeTrue();
        token.RefreshCallbacks.Should().HaveCount(before);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LinearObjectives_ShouldRejectMissingTermsAndPreserveValidSnapshot(bool flattened) {
        var x = new RealVar("registered");
        var missing = new RealVar("missing");
        using var invalid = new LinearMetaModel<Flt64>();
        invalid.Add(x).IsOk.Should().BeTrue();
        var invalidTerms = new List<LinearMonomial<Flt64>> { new(Flt64.One, missing) };
        AddLinearObjective(
            model: invalid,
            terms: invalidTerms,
            flattened: flattened
        );
        Token<Flt64> token = invalid.Tokens.TokenList.Find(x)!;
        int before = token.RefreshCallbacks.Count;
        (await LinearMechanismModel<Flt64>.InvokeAsync(invalid)).IsFailed.Should().BeTrue();
        token.RefreshCallbacks.Should().HaveCount(before);

        using var unexpanded = new LinearMetaModel<Flt64>();
        AddLinearObjective(
            model: unexpanded,
            terms: new List<LinearMonomial<Flt64>> { new(Flt64.One, new TestSymbol("unexpanded")) },
            flattened: flattened
        );
        (await LinearMechanismModel<Flt64>.InvokeAsync(unexpanded)).IsFailed.Should().BeTrue();

        using var valid = new LinearMetaModel<Flt64>();
        valid.Add(x).IsOk.Should().BeTrue();
        var validTerms = new List<LinearMonomial<Flt64>> {
            new(Flt64.One, x),
            new(Flt64.Zero, missing),
            new(Flt64.Zero, new TestSymbol("zero-unexpanded")),
        };
        AddLinearObjective(
            model: valid,
            terms: validTerms,
            flattened: flattened
        );
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(valid)).Value;
        (await valid.State.RefreshAsync()).IsOk.Should().BeTrue();
        validTerms.Clear();
        model.ObjectFunction.GetSubObjects<Flt64>().OfType<LinearSubObject<Flt64>>().Single()
            .LinearTerms().Should().ContainSingle().Which.Symbol.Should().BeSameAs(x);
    }

    [Fact]
    public async Task QuadraticObjectives_ShouldValidateBothSymbolsAndPreserveSnapshot() {
        var x = new RealVar("registered");
        var missing = new RealVar("missing");
        var symbol = new TestSymbol("unexpanded");
        foreach (QuadraticMonomial<Flt64> term in new[] {
            QuadraticTerm(missing, x),
            QuadraticTerm(x, missing),
            QuadraticTerm(symbol, x),
            QuadraticTerm(x, symbol),
        }) {
            using var invalid = new QuadraticMetaModel<Flt64>();
            invalid.Add(x).IsOk.Should().BeTrue();
            AddQuadraticObjective(invalid, new List<QuadraticMonomial<Flt64>> { term });
            Token<Flt64> token = invalid.Tokens.TokenList.Find(x)!;
            int before = token.RefreshCallbacks.Count;
            (await QuadraticMechanismModel<Flt64>.InvokeAsync(invalid)).IsFailed.Should().BeTrue();
            token.RefreshCallbacks.Should().HaveCount(before);
        }
        using var valid = new QuadraticMetaModel<Flt64>();
        valid.Add(x).IsOk.Should().BeTrue();
        var terms = new List<QuadraticMonomial<Flt64>> {
            QuadraticTerm(x, x),
            QuadraticTerm(missing, symbol) with { Coefficient = Flt64.Zero },
        };
        AddQuadraticObjective(valid, terms);
        using QuadraticMechanismModel<Flt64> model = (await QuadraticMechanismModel<Flt64>.InvokeAsync(valid)).Value;
        terms.Clear();
        model.ObjectFunction.GetSubObjects<Flt64>().OfType<QuadraticSubObject<Flt64>>().Single()
            .QuadraticTerms().Should().ContainSingle().Which.Symbol2.Should().BeSameAs(x);
    }

    [Fact]
    public async Task QuadraticModel_ShouldValidatePromotedLinearObjectives() {
        var missing = new RealVar("missing");
        using var model = new QuadraticMetaModel<Flt64>();
        model.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new SymbolFlatten.LinearFlattenData<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, missing) }, Flt64.Zero),
            name: "promoted",
            displayName: null
        ).IsOk.Should().BeTrue();
        (await QuadraticMechanismModel<Flt64>.InvokeAsync(model)).IsFailed.Should().BeTrue();
    }

    [Fact]
    public async Task ZeroNonVariableConstraintTerms_ShouldBeIgnored() {
        using var parent = new LinearMetaModel<Flt64>();
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        var zero = new LinearPolynomial<Flt64>(
            new[] { new LinearMonomial<Flt64>(Flt64.Zero, new TestSymbol("unused")) },
            Flt64.Zero
        );
        var relation = new LinearInequality<Flt64>(
            Lhs: zero,
            Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero),
            Comparison: Comparison.LE
        );
        model.AddConstraint(relation, from: (Fuookami.Ospf.Core.Symbol.IIntermediateSymbol?)null).IsOk.Should().BeTrue();
        ((IMechanismModel<Flt64>)model).Constraints.Should().ContainSingle().Which.Lhs.Should().BeEmpty();
    }

    [Theory]
    [InlineData(Comparison.NE)]
    [InlineData((Comparison)99)]
    public async Task UnsupportedLinearRelation_ShouldReturnFailureWithoutLeakingSnapshot(Comparison comparison) {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new RealVar("linear-sign");
        parent.Add(x).IsOk.Should().BeTrue();
        var relation = new LinearRelationImpl<Flt64>(
            new SymbolFlatten.LinearFlattenData<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, Flt64.Zero),
            comparison
        );
        relation.ConstraintRelation().IsFailed.Should().BeTrue();
        LinearConstraintImpl<Flt64>.Create(
            relation: relation,
            tokens: parent.Tokens,
            converter: IIntoValue<Flt64>.Identity
        ).Should().BeOfType<Failed<LinearConstraintImpl<Flt64>, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.IllegalArgument);
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        LinearInequality<Flt64> row = LinearRow(x) with { Comparison = comparison };
        model.AddConstraint(row, from: (Fuookami.Ospf.Core.Symbol.IIntermediateSymbol?)null).IsFailed.Should().BeTrue();
        ((IMechanismModel<Flt64>)model).Constraints.Should().BeEmpty();
        parent.AddConstraint(row, null).IsOk.Should().BeTrue();
        Token<Flt64> token = parent.Tokens.TokenList.Find(x)!;
        int before = token.RefreshCallbacks.Count;
        (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).IsFailed.Should().BeTrue();
        token.RefreshCallbacks.Should().HaveCount(before);
    }

    [Theory]
    [InlineData(Comparison.NE)]
    [InlineData((Comparison)99)]
    public async Task UnsupportedQuadraticRelation_ShouldReturnFailureWithoutLeakingSnapshot(Comparison comparison) {
        using var parent = new QuadraticMetaModel<Flt64>();
        var x = new RealVar("quadratic-sign");
        parent.Add(x).IsOk.Should().BeTrue();
        var relation = new QuadraticRelationImpl<Flt64>(
            new SymbolFlatten.QuadraticFlattenData<Flt64>(new[] { QuadraticTerm(x, x) }, Flt64.Zero),
            comparison
        );
        relation.ConstraintRelation().IsFailed.Should().BeTrue();
        QuadraticConstraintImpl<Flt64>.Create(
            relation: relation,
            tokens: parent.Tokens,
            converter: IIntoValue<Flt64>.Identity
        ).Should().BeOfType<Failed<QuadraticConstraintImpl<Flt64>, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.IllegalArgument);
        using QuadraticMechanismModel<Flt64> model = (await QuadraticMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        QuadraticInequalityOf<Flt64> row = QuadraticRow(x, x) with { Comparison = comparison };
        model.AddConstraint(row, from: (Fuookami.Ospf.Core.Symbol.IIntermediateSymbol?)null).IsFailed.Should().BeTrue();
        ((IAbstractQuadraticMechanismModel<Flt64>)model).Constraints.Should().BeEmpty();
        parent.AddConstraint(row, null).IsOk.Should().BeTrue();
        Token<Flt64> token = parent.Tokens.TokenList.Find(x)!;
        int before = token.RefreshCallbacks.Count;
        (await QuadraticMechanismModel<Flt64>.InvokeAsync(parent)).IsFailed.Should().BeTrue();
        token.RefreshCallbacks.Should().HaveCount(before);
    }

    [Fact]
    public void ConstraintFactories_ShouldPreserveFatalRelationErrors() {
        using var table = new ConcurrentManualTokenTable<Flt64>(LinearCategory.Instance, new List<Fuookami.Ospf.Core.Symbol.IIntermediateSymbol>());
        LinearConstraintImpl<Flt64>.Create(
            relation: new FatalLinearRelation(),
            tokens: table,
            converter: IIntoValue<Flt64>.Identity
        ).Should().BeOfType<Fatal<LinearConstraintImpl<Flt64>, ErrorCode, Error<ErrorCode>>>()
            .Which.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCode.ApplicationError);
        QuadraticConstraintImpl<Flt64>.Create(
            relation: new FatalQuadraticRelation(),
            tokens: table,
            converter: IIntoValue<Flt64>.Identity
        ).Should().BeOfType<Fatal<QuadraticConstraintImpl<Flt64>, ErrorCode, Error<ErrorCode>>>()
            .Which.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCode.ApplicationError);
    }

    [Theory]
    [InlineData(Comparison.LT)]
    [InlineData(Comparison.GT)]
    public void StrictRelations_ShouldRetainBaselineNonStrictBoundary(Comparison comparison) {
        using var table = new ConcurrentManualTokenTable<Flt64>(LinearCategory.Instance, new List<Fuookami.Ospf.Core.Symbol.IIntermediateSymbol>());
        var x = new RealVar("strict-boundary");
        table.Add(x).IsOk.Should().BeTrue();
        var linear = new LinearRelationImpl<Flt64>(
            new SymbolFlatten.LinearFlattenData<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, Flt64.Zero),
            comparison
        );
        var quadratic = new QuadraticRelationImpl<Flt64>(
            new SymbolFlatten.QuadraticFlattenData<Flt64>(new[] { QuadraticTerm(x, x) }, Flt64.Zero),
            comparison
        );
        LinearConstraintImpl<Flt64>.Create(
            relation: linear,
            tokens: table,
            converter: IIntoValue<Flt64>.Identity
        ).Value.IsTrue(new[] { Flt64.Zero }).Should().BeTrue();
        QuadraticConstraintImpl<Flt64>.Create(
            relation: quadratic,
            tokens: table,
            converter: IIntoValue<Flt64>.Identity
        ).Value.IsTrue(new[] { Flt64.Zero }).Should().BeTrue();
    }

    private static LinearInequality<Flt64> LinearRow(ISymbol symbol) => new(
        Lhs: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, symbol) }, Flt64.Zero),
        Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero),
        Comparison: Comparison.LE
    );

    private static QuadraticInequalityOf<Flt64> QuadraticRow(ISymbol first, ISymbol second) => new(
        Lhs: new QuadraticPolynomial<Flt64>(new[] { QuadraticTerm(first, second) }, Flt64.Zero),
        Rhs: new QuadraticPolynomial<Flt64>(Array.Empty<QuadraticMonomial<Flt64>>(), Flt64.Zero),
        Comparison: Comparison.LE
    );

    private static QuadraticMonomial<Flt64> QuadraticTerm(ISymbol first, ISymbol second) => new(
        Coefficient: Flt64.One,
        Symbol1: first,
        Symbol2: second
    );

    private static void AddLinearObjective(LinearMetaModel<Flt64> model, List<LinearMonomial<Flt64>> terms, bool flattened) {
        if (flattened) {
            model.AddObject(
                category: ObjectCategory.Minimum,
                flattenData: new SymbolFlatten.LinearFlattenData<Flt64>(terms, Flt64.Zero),
                name: "objective",
                displayName: null
            ).IsOk.Should().BeTrue();
        }
        else {
            model.AddObject(
                category: ObjectCategory.Minimum,
                polynomial: new LinearPolynomial<Flt64>(terms, Flt64.Zero),
                name: "objective",
                displayName: null
            ).IsOk.Should().BeTrue();
        }
    }

    private static void AddQuadraticObjective(QuadraticMetaModel<Flt64> model, List<QuadraticMonomial<Flt64>> terms) {
        model.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new SymbolFlatten.QuadraticFlattenData<Flt64>(terms, Flt64.Zero),
            name: "objective",
            displayName: null
        ).IsOk.Should().BeTrue();
    }

    private sealed record TestSymbol(string Name) : ISymbol {
        public string? DisplayName => null;
    }

    private sealed class FatalLinearRelation : ILinearRelation<Flt64> {
        public SymbolFlatten.LinearFlattenData<Flt64> FlattenData => new(Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero);
        public Comparison Sign => Comparison.LE;
        public string Name => "fatal-linear";
        public string? DisplayName => null;
        public ConstraintRelation? ConstraintRelationOrNull => null;
        public Result<ConstraintRelation, ErrorCode, Error<ErrorCode>> ConstraintRelation() =>
            new Fatal<ConstraintRelation, ErrorCode, Error<ErrorCode>>(ErrorCode.ApplicationError);
        public ILinearRelation<Flt64> Normalize() => this;
    }

    private sealed class FatalQuadraticRelation : IQuadraticRelation<Flt64> {
        public SymbolFlatten.QuadraticFlattenData<Flt64> FlattenData => new(Array.Empty<QuadraticMonomial<Flt64>>(), Flt64.Zero);
        public Comparison Sign => Comparison.LE;
        public string Name => "fatal-quadratic";
        public string? DisplayName => null;
        public ConstraintRelation? ConstraintRelationOrNull => null;
        public Result<ConstraintRelation, ErrorCode, Error<ErrorCode>> ConstraintRelation() =>
            new Fatal<ConstraintRelation, ErrorCode, Error<ErrorCode>>(ErrorCode.ApplicationError);
        public IQuadraticRelation<Flt64> Normalize() => this;
    }
}
