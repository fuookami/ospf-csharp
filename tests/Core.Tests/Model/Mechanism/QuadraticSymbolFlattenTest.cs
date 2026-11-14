#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Integer = Fuookami.Ospf.Math.Algebra.Number.Int64;
using SymbolFlatten = Fuookami.Ospf.Core.Symbol.Flatten;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
namespace Fuookami.Ospf.Core.Tests.Model.Mechanism;

[Collection("LinearSymbolFlatten")]
public class QuadraticSymbolFlattenTest {
    [Fact]
    public async Task NestedFactorsAndPromotedLinear_ShouldReachRowsObjectivesAndMatrixAsSnapshots() {
        var x = new RealVar("quadratic-nested-x");
        var y = new RealVar("quadratic-nested-y");
        LinearExpressionSymbol first = LinearExpression(
            name: "first",
            constant: 3,
            terms: [(2, x)]
        );
        LinearExpressionSymbol second = LinearExpression(
            name: "second",
            constant: 4,
            terms: [(-1, y)]
        );
        var quadratic = new QuadraticExpressionSymbol(
            polynomial: new MutableQuadraticPolynomial<Flt64>(
                monomials: [new QuadraticMonomial<Flt64>(
                    Coefficient: Flt64.One,
                    Symbol1: x,
                    Symbol2: x
                )],
                linearMonomials: [new LinearMonomial<Flt64>(Flt64.One, first)],
                constant: Flt64.One),
            name: "nested-quadratic"
        );
        QuadraticMonomial<Flt64>[] left = [
            new(
                Coefficient: new Flt64(2),
                Symbol1: first,
                Symbol2: second
            ),
            new(new Flt64(3), quadratic),
        ];
        var relation = new QuadraticInequalityOf<Flt64>(
            Lhs: new QuadraticPolynomial<Flt64>(left, Flt64.Zero),
            Rhs: new QuadraticPolynomial<Flt64>([new QuadraticMonomial<Flt64>(new Flt64(4), first)], new Flt64(7)),
            Comparison: Comparison.LE,
            Name: "nested-row",
            DisplayName: "Nested Row"
        );
        var promoted = new LinearInequality<Flt64>(
            Lhs: LinearPolynomial(1, [(3, first)]),
            Rhs: LinearPolynomial(2, [(1, second)]),
            Comparison: Comparison.LE,
            Name: "promoted-row",
            DisplayName: "Promoted Row"
        );
        using var parent = new QuadraticMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(y).IsOk.Should().BeTrue();
        parent.AddConstraint(relation, null).IsOk.Should().BeTrue();
        parent.AddConstraint(
            relation: promoted,
            group: null,
            priority: 7
        ).IsOk.Should().BeTrue();
        parent.AddObject(
            category: ObjectCategory.Maximum,
            flattenData: new SymbolFlatten.QuadraticFlattenData<Flt64>(left, new Flt64(5)),
            name: "nested-objective",
            displayName: "Nested Objective"
        ).IsOk.Should().BeTrue();
        parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new SymbolFlatten.LinearFlattenData<Flt64>(
                [new LinearMonomial<Flt64>(new Flt64(2), first)], new Flt64(5)),
            name: "promoted-objective",
            displayName: "Promoted Objective"
        ).IsOk.Should().BeTrue();

        Result<QuadraticMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await QuadraticMechanismModel<Flt64>.InvokeAsync(parent);
        built.IsOk.Should().BeTrue();
        using QuadraticMechanismModel<Flt64> model = built.Value;
        IReadOnlyList<IConstraint<Flt64, QuadraticCategory>> rows = ((IAbstractQuadraticMechanismModel<Flt64>)model).Constraints;
        rows.Should().HaveCount(2);
        rows[0].Name.Should().Be("nested-row");
        rows[0].Origin.Should().BeSameAs(parent.RelationConstraints[0]);
        rows[0].Rhs.Should().Be(new Flt64(-17));
        rows[1].Name.Should().Be("promoted-row");
        rows[1].Origin.Should().BeSameAs(parent.RelationConstraints[1]);
        rows[1].Origin!.DisplayName.Should().Be("Promoted Row");
        rows[1].Origin!.Priority.Should().Be(7);
        rows[1].Rhs.Should().Be(new Flt64(-4));
        QuadraticSubObject<Flt64>[] objectives = model.ObjectFunction.GetSubObjects<Flt64>().OfType<QuadraticSubObject<Flt64>>().ToArray();
        objectives.Select(objective => objective.Name).Should().Equal("nested-objective", "promoted-objective");
        objectives[0].DisplayName.Should().Be("Nested Objective");
        objectives[0].Category.Should().Be(ObjectCategory.Maximum);
        objectives[1].DisplayName.Should().Be("Promoted Objective");
        objectives[1].Category.Should().Be(ObjectCategory.Minimum);
        var values = new Dictionary<IVariableItem, Flt64> { [x] = Flt64.One, [y] = new Flt64(2) };
        IReadOnlyList<Flt64> solution = model.Tokens.TokensInSolver.Select(token => values[token.Variable]).ToList();
        objectives[0].Evaluate(solution).Should().Be(new Flt64(46));
        objectives[1].Evaluate(solution).Should().Be(new Flt64(15));
        rows[0].IsTrue(solution).Should().BeFalse();

        using QuadraticTetradModel matrix = model.ToQuadraticTetradModel().Value;
        int xi = model.Tokens.IndexOf(x)!.Value;
        int yi = model.Tokens.IndexOf(y)!.Value;
        matrix.Constraints.Lhs[0].Single(cell => cell.Col1 == xi && cell.Col2 == xi).Coefficient.Should().Be(new Flt64(3));
        matrix.Constraints.Lhs[0].Single(cell => cell.Col1 == global::System.Math.Min(xi, yi) && cell.Col2 == global::System.Math.Max(xi, yi))
            .Coefficient.Should().Be(new Flt64(-4));
        matrix.Constraints.Lhs[0].Single(cell => cell.Col1 == xi && cell.Col2 == -1).Coefficient.Should().Be(new Flt64(14));
        matrix.Constraints.Lhs[0].Single(cell => cell.Col1 == yi && cell.Col2 == -1).Coefficient.Should().Be(new Flt64(-6));

        first.AsMutable().SetConstant(new Flt64(4));
        using QuadraticMechanismModel<Flt64> updated = (await QuadraticMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        updated.ObjectFunction.GetSubObjects<Flt64>().OfType<QuadraticSubObject<Flt64>>().First()
            .Evaluate(solution).Should().Be(new Flt64(53));
        objectives[0].Evaluate(solution).Should().Be(new Flt64(46));
        rows[0].Rhs.Should().Be(new Flt64(-17));
    }

    [Fact]
    public async Task NestedFunctionFactors_ShouldRequireRegistrationAndPreserveFunctionRows() {
        var x = new RealVar("quadratic-function-x");
        var absolute = new AbsFunction<Flt64>(
            polynomial: LinearPolynomial(0, [(1, x)]),
            converter: IIntoValue<Flt64>.Identity,
            bigM: new Flt64(10)
        );
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(absolute);
        var relation = new QuadraticInequalityOf<Flt64>(
            Lhs: new QuadraticPolynomial<Flt64>([new QuadraticMonomial<Flt64>(
                Coefficient: Flt64.One,
                Symbol1: x,
                Symbol2: adapter
            )], Flt64.Zero),
            Rhs: new QuadraticPolynomial<Flt64>([], new Flt64(3)),
            Comparison: Comparison.LE
        );
        using var missing = new QuadraticMetaModel<Flt64>();
        missing.Add(x).IsOk.Should().BeTrue();
        missing.Add(absolute.ResultVar).IsOk.Should().BeTrue();
        missing.AddConstraint(relation, null).IsOk.Should().BeTrue();
        int callbacks = missing.Tokens.Find(x)!.RefreshCallbacks.Count;
        (await QuadraticMechanismModel<Flt64>.InvokeAsync(missing)).IsFailed.Should().BeTrue();
        missing.Tokens.Find(x)!.RefreshCallbacks.Should().HaveCount(callbacks);

        using var parent = new QuadraticMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(adapter).IsOk.Should().BeTrue();
        parent.AddConstraint(relation, null).IsOk.Should().BeTrue();
        using QuadraticMechanismModel<Flt64> model = (await QuadraticMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        IReadOnlyList<IConstraint<Flt64, QuadraticCategory>> rows = ((IAbstractQuadraticMechanismModel<Flt64>)model).Constraints;
        rows.Should().HaveCount(5);
        rows[0].Lhs.Cast<IQuadraticCell<Flt64>>().Should().ContainSingle()
            .Which.Token2!.Variable.Should().BeSameAs(absolute.ResultVar);
        var values = new Dictionary<IVariableItem, Flt64> {
            [x] = new Flt64(-2),
            [absolute.ResultVar] = new Flt64(2),
            [absolute.PositiveVar] = Flt64.Zero,
            [absolute.NegativeVar] = new Flt64(2),
            [absolute.SignVar] = Flt64.Zero,
        };
        var solution = model.Tokens.TokensInSolver.Select(token => values[token.Variable]).ToList();
        rows.Should().OnlyContain(row => row.IsTrue(solution) == true);
        model.AddConstraint(relation, from: (IIntermediateSymbol?)null).IsOk.Should().BeTrue();
        rows.Should().HaveCount(6);
    }

    [Fact]
    public async Task CyclesHigherDegreesAndMissingVariables_ShouldFailWithoutRowsOrSnapshotLeaks() {
        var x = new RealVar("quadratic-invalid-x");
        var missing = new RealVar("quadratic-missing-x");
        var cycle = new QuadraticExpressionSymbol(new MutableQuadraticPolynomial<Flt64>(constant: Flt64.Zero), name: "cycle");
        cycle.AsMutable().AddMonomial(new QuadraticMonomial<Flt64>(Flt64.One, cycle));
        var square = new QuadraticExpressionSymbol(
            new MutableQuadraticPolynomial<Flt64>(
                monomials: [new QuadraticMonomial<Flt64>(
                    Coefficient: Flt64.One,
                    Symbol1: x,
                    Symbol2: x
                )],
                constant: Flt64.Zero),
            name: "square"
        );
        QuadraticMonomial<Flt64>[] invalid = [
            new(Flt64.One, cycle),
            new(
                Coefficient: Flt64.One,
                Symbol1: square,
                Symbol2: x
            ),
            new(
                Coefficient: Flt64.One,
                Symbol1: x,
                Symbol2: missing
            ),
        ];
        foreach (QuadraticMonomial<Flt64> term in invalid) {
            using var parent = new QuadraticMetaModel<Flt64>();
            parent.Add(x).IsOk.Should().BeTrue();
            using QuadraticMechanismModel<Flt64> existing = (await QuadraticMechanismModel<Flt64>.InvokeAsync(parent)).Value;
            var relation = new QuadraticInequalityOf<Flt64>(
                Lhs: new QuadraticPolynomial<Flt64>([term], Flt64.Zero),
                Rhs: new QuadraticPolynomial<Flt64>([], Flt64.Zero),
                Comparison: Comparison.LE
            );
            existing.AddConstraint(relation, from: (IIntermediateSymbol?)null).IsFailed.Should().BeTrue();
            ((IAbstractQuadraticMechanismModel<Flt64>)existing).Constraints.Should().BeEmpty();
            parent.AddConstraint(relation, null).IsOk.Should().BeTrue();
            int callbacks = parent.Tokens.Find(x)!.RefreshCallbacks.Count;
            (await QuadraticMechanismModel<Flt64>.InvokeAsync(parent)).IsFailed.Should().BeTrue();
            parent.Tokens.Find(x)!.RefreshCallbacks.Should().HaveCount(callbacks);
        }
    }

    [Fact]
    public async Task DeepAndRepeatedFactors_ShouldNotUseRecursiveCallStackOrMistakeSharingForCycle() {
        var x = new RealVar("quadratic-deep-x");
        ISymbol current = x;
        for (int index = 0; index < 2048; ++index) {
            current = new QuadraticExpressionSymbol(
                new MutableQuadraticPolynomial<Flt64>(
                    monomials: [new QuadraticMonomial<Flt64>(Flt64.One, current)],
                    constant: Flt64.Zero),
                name: $"depth-{index}"
            );
        }
        using var parent = new QuadraticMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new SymbolFlatten.QuadraticFlattenData<Flt64>(
                [new QuadraticMonomial<Flt64>(
                    Coefficient: Flt64.One,
                    Symbol1: current,
                    Symbol2: current
                )], Flt64.Zero),
            name: "deep",
            displayName: null
        ).IsOk.Should().BeTrue();
        using QuadraticMechanismModel<Flt64> model = (await QuadraticMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        model.ObjectFunction.GetSubObjects<Flt64>().OfType<QuadraticSubObject<Flt64>>().Single()
            .Evaluate([new Flt64(3)]).Should().Be(new Flt64(9));
    }

    [Fact]
    public async Task IntegerOverflowAndFloatUnderflow_ShouldFailSafely() {
        var x = new RealVar("quadratic-overflow-x");
        var inner = new LinearFunctionSymbolAdapter<Integer>(new StructuralFunction<Integer>(
            new LinearPolynomial<Integer>([new LinearMonomial<Integer>(new Integer(long.MaxValue), x)], Integer.Zero)
        ));
        using var table = new ConcurrentManualTokenTable<Integer>(
            category: QuadraticCategory.Instance,
            symbols: [inner]
        );
        table.Add(x).IsOk.Should().BeTrue();
        QuadraticSubObject<Integer>.Create(
            flattenData: new SymbolFlatten.QuadraticFlattenData<Integer>(
                [new QuadraticMonomial<Integer>(
                    Coefficient: new Integer(2),
                    Symbol1: inner,
                    Symbol2: x
                )], Integer.Zero),
            tokens: table,
            converter: new IntegerConverter(),
            category: ObjectCategory.Minimum
        ).IsFailed.Should().BeTrue();

        LinearExpressionSymbol tiny = LinearExpression(
            name: "tiny",
            constant: 0,
            terms: [(double.Epsilon, x)]
        );
        using var parent = new QuadraticMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new SymbolFlatten.QuadraticFlattenData<Flt64>(
                [new QuadraticMonomial<Flt64>(
                    Coefficient: new Flt64(0.5),
                    Symbol1: tiny,
                    Symbol2: x
                )], Flt64.Zero),
            name: "underflow",
            displayName: null
        ).IsOk.Should().BeTrue();
        (await QuadraticMechanismModel<Flt64>.InvokeAsync(parent)).IsFailed.Should().BeTrue();
    }

    [Fact]
    public void NestedConstantsAndSharedDependencies_ShouldKeepGroupingWithoutFalseCycles() {
        var x = new RealVar("quadratic-shared-x");
        LinearExpressionSymbol negative = LinearExpression(
            name: "negative",
            constant: -1e16,
            terms: []
        );
        LinearExpressionSymbol one = LinearExpression(
            name: "one",
            constant: 1,
            terms: []
        );
        LinearExpressionSymbol grouped = LinearExpression(
            name: "grouped",
            constant: 1e16,
            terms: [(1, negative), (1, one)]
        );
        Result<SymbolFlatten.QuadraticFlattenData<Flt64>, ErrorCode, Error<ErrorCode>> constants =
            new SymbolFlatten.QuadraticFlattenData<Flt64>(
                [new QuadraticMonomial<Flt64>(Flt64.One, grouped)], Flt64.Zero
            ).ExpandIntermediateSymbols();
        constants.IsOk.Should().BeTrue();
        constants.Value.Constant.Should().Be(Flt64.One);

        LinearExpressionSymbol first = LinearExpression(
            name: "shared",
            constant: 1,
            terms: [(1, x)]
        );
        LinearExpressionSymbol second = LinearExpression(
            name: "dependent",
            constant: 1,
            terms: [(1, first)]
        );
        using var table = new ConcurrentManualTokenTable<Flt64>(
            category: QuadraticCategory.Instance,
            symbols: []
        );
        table.Add(x).IsOk.Should().BeTrue();
        Result<QuadraticSubObject<Flt64>, ErrorCode, Error<ErrorCode>> objective = QuadraticSubObject<Flt64>.Create(
            flattenData: new SymbolFlatten.QuadraticFlattenData<Flt64>(
                [new QuadraticMonomial<Flt64>(
                    Coefficient: Flt64.One,
                    Symbol1: first,
                    Symbol2: second
                )], Flt64.Zero),
            tokens: table,
            converter: IIntoValue<Flt64>.Identity,
            category: ObjectCategory.Minimum
        );
        objective.IsOk.Should().BeTrue();
        objective.Value.Evaluate([new Flt64(2)]).Should().Be(new Flt64(12));
    }

    [Fact]
    public void DecimalAndRationalFactors_ShouldUseNativeCoefficients() {
        var x = new RealVar("quadratic-native-x");
        var decimalAdapter = new LinearFunctionSymbolAdapter<FltX>(new StructuralFunction<FltX>(
            new LinearPolynomial<FltX>(
                [new LinearMonomial<FltX>(new FltX(9007199254740993.1m), x)], new FltX(0.2m))
        ));
        using var decimalTable = new ConcurrentManualTokenTable<FltX>(
            category: QuadraticCategory.Instance,
            symbols: [decimalAdapter]
        );
        decimalTable.Add(x).IsOk.Should().BeTrue();
        Result<QuadraticSubObject<FltX>, ErrorCode, Error<ErrorCode>> decimalResult = QuadraticSubObject<FltX>.Create(
            flattenData: new SymbolFlatten.QuadraticFlattenData<FltX>(
                [new QuadraticMonomial<FltX>(
                    Coefficient: new FltX(2m),
                    Symbol1: decimalAdapter,
                    Symbol2: x
                )], FltX.Zero),
            tokens: decimalTable,
            converter: FltX.Zero,
            category: ObjectCategory.Maximum
        );
        decimalResult.IsOk.Should().BeTrue();
        decimalResult.Value.QuadraticTerms().Single(term => term.Symbol2 is not null).Coefficient
            .Eq(new FltX(18014398509481986.2m)).Should().BeTrue();
        decimalResult.Value.QuadraticTerms().Single(term => term.Symbol2 is null).Coefficient
            .Eq(new FltX(0.4m)).Should().BeTrue();

        var rationalAdapter = new LinearFunctionSymbolAdapter<RtnX>(new StructuralFunction<RtnX>(
            new LinearPolynomial<RtnX>(
                [new LinearMonomial<RtnX>(new RtnX(1, 3), x)], new RtnX(1, 4))
        ));
        using var rationalTable = new ConcurrentManualTokenTable<RtnX>(
            category: QuadraticCategory.Instance,
            symbols: [rationalAdapter]
        );
        rationalTable.Add(x).IsOk.Should().BeTrue();
        Result<QuadraticSubObject<RtnX>, ErrorCode, Error<ErrorCode>> rationalResult = QuadraticSubObject<RtnX>.Create(
            flattenData: new SymbolFlatten.QuadraticFlattenData<RtnX>(
                [new QuadraticMonomial<RtnX>(
                    Coefficient: new RtnX(3, 2),
                    Symbol1: rationalAdapter,
                    Symbol2: x
                )], RtnX.Zero),
            tokens: rationalTable,
            converter: RtnX.Zero,
            category: ObjectCategory.Minimum
        );
        rationalResult.IsOk.Should().BeTrue();
        rationalResult.Value.QuadraticTerms().Single(term => term.Symbol2 is not null).Coefficient
            .Eq(new RtnX(1, 2)).Should().BeTrue();
        rationalResult.Value.QuadraticTerms().Single(term => term.Symbol2 is null).Coefficient
            .Eq(new RtnX(3, 8)).Should().BeTrue();
    }

    [Fact]
    public void PublicConstraintFactory_ShouldExpandFactorsAndRejectInvalidConstant() {
        var x = new RealVar("quadratic-factory-x");
        LinearExpressionSymbol factor = LinearExpression(
            name: "factory-factor",
            constant: 3,
            terms: [(2, x)]
        );
        using var table = new ConcurrentManualTokenTable<Flt64>(
            category: QuadraticCategory.Instance,
            symbols: []
        );
        table.Add(x).IsOk.Should().BeTrue();
        Result<QuadraticConstraintImpl<Flt64>, ErrorCode, Error<ErrorCode>> row = QuadraticConstraintImpl<Flt64>.Create(
            relation: new QuadraticRelationImpl<Flt64>(
                new SymbolFlatten.QuadraticFlattenData<Flt64>(
                    [new QuadraticMonomial<Flt64>(
                        Coefficient: new Flt64(2),
                        Symbol1: factor,
                        Symbol2: x
                    )], Flt64.One),
                Comparison.LE),
            tokens: table,
            converter: IIntoValue<Flt64>.Identity,
            name: "factory"
        );
        row.IsOk.Should().BeTrue();
        row.Value.Name.Should().Be("factory");
        row.Value.Rhs.Should().Be(new Flt64(-1));
        row.Value.QuadraticCells.Single(cell => cell.Token2 is not null).Coefficient.Should().Be(new Flt64(4));
        row.Value.QuadraticCells.Single(cell => cell.Token2 is null).Coefficient.Should().Be(new Flt64(6));

        using var integerTable = new ConcurrentManualTokenTable<Integer>(
            category: QuadraticCategory.Instance,
            symbols: []
        );
        QuadraticConstraintImpl<Integer>.Create(
            relation: new QuadraticRelationImpl<Integer>(
                new SymbolFlatten.QuadraticFlattenData<Integer>([], new Integer(long.MinValue)), Comparison.LE),
            tokens: integerTable,
            converter: new IntegerConverter()
        ).IsFailed.Should().BeTrue();
    }

    [Fact]
    public void SymmetricTermsAndConstants_ShouldRejectOverflow() {
        var x = new RealVar("quadratic-overflow-a");
        var y = new RealVar("quadratic-overflow-b");
        using var table = new ConcurrentManualTokenTable<Integer>(
            category: QuadraticCategory.Instance,
            symbols: []
        );
        table.Add(x).IsOk.Should().BeTrue();
        table.Add(y).IsOk.Should().BeTrue();
        var zero = new QuadraticPolynomial<Integer>([], Integer.Zero);
        var relation = new QuadraticInequalityOf<Integer>(
            Lhs: new QuadraticPolynomial<Integer>([
                new QuadraticMonomial<Integer>(
                    Coefficient: new Integer(long.MaxValue),
                    Symbol1: x,
                    Symbol2: y
                ),
                new QuadraticMonomial<Integer>(
                    Coefficient: Integer.One,
                    Symbol1: y,
                    Symbol2: x
                ),
            ], Integer.Zero),
            Rhs: zero,
            Comparison: Comparison.LE
        );
        relation.ToQuadraticFlattenData(table).IsFailed.Should().BeTrue();
        (relation with {
            Lhs = new QuadraticPolynomial<Integer>([], new Integer(long.MinValue)),
        }).ToQuadraticFlattenData(table).IsFailed.Should().BeTrue();

        LinearExpressionSymbol infinite = LinearExpression(
            name: "invalid-constant",
            constant: double.PositiveInfinity,
            terms: []
        );
        new SymbolFlatten.QuadraticFlattenData<Flt64>(
            [new QuadraticMonomial<Flt64>(Flt64.One, infinite)], Flt64.Zero
        ).ExpandIntermediateSymbols().IsFailed.Should().BeTrue();
    }

    [Fact]
    public void ZeroFactor_ShouldReduceQuadraticProductsWithoutCreatingTerms() {
        var x = new RealVar("quadratic-zero-x");
        var square = new QuadraticExpressionSymbol(
            polynomial: new MutableQuadraticPolynomial<Flt64>(
                monomials: [new QuadraticMonomial<Flt64>(
                    Coefficient: Flt64.One,
                    Symbol1: x,
                    Symbol2: x
                )],
                constant: Flt64.One),
            name: "quadratic-zero-square"
        );
        LinearExpressionSymbol zero = LinearExpression(
            name: "quadratic-zero-factor",
            constant: 0,
            terms: []
        );
        foreach ((ISymbol first, ISymbol second) in new[] { ((ISymbol)square, (ISymbol)zero), (zero, square) }) {
            Result<SymbolFlatten.QuadraticFlattenData<Flt64>, ErrorCode, Error<ErrorCode>> result =
                new SymbolFlatten.QuadraticFlattenData<Flt64>([new QuadraticMonomial<Flt64>(
                    Coefficient: new Flt64(3),
                    Symbol1: first,
                    Symbol2: second
                )], new Flt64(2)).ExpandIntermediateSymbols();
            result.IsOk.Should().BeTrue();
            result.Value.Monomials.Should().BeEmpty();
            result.Value.Constant.Should().Be(new Flt64(2));
        }
        var invalid = new QuadraticExpressionSymbol(
            polynomial: new MutableQuadraticPolynomial<Flt64>(
                monomials: [new QuadraticMonomial<Flt64>(new Flt64(double.NaN), x)],
                constant: Flt64.Zero),
            name: "quadratic-invalid-factor"
        );
        new SymbolFlatten.QuadraticFlattenData<Flt64>([new QuadraticMonomial<Flt64>(
            Coefficient: Flt64.One,
            Symbol1: invalid,
            Symbol2: zero
        )], Flt64.Zero).ExpandIntermediateSymbols().IsFailed.Should().BeTrue();
    }

    [Fact]
    public async Task UnregisteredFunctionCancellation_ShouldFailBeforeMergingBothSides() {
        var x = new RealVar("quadratic-cancel-x");
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(new StructuralFunction<Flt64>(
            new LinearPolynomial<Flt64>([new LinearMonomial<Flt64>(Flt64.One, x)], Flt64.Zero)
        ));
        var same = new QuadraticPolynomial<Flt64>([new QuadraticMonomial<Flt64>(
            Coefficient: Flt64.One,
            Symbol1: adapter,
            Symbol2: x
        )], Flt64.Zero);
        var relation = new QuadraticInequalityOf<Flt64>(
            Lhs: same,
            Rhs: same,
            Comparison: Comparison.EQ
        );
        using var parent = new QuadraticMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        using QuadraticMechanismModel<Flt64> existing = (await QuadraticMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        existing.AddConstraint(relation, from: (IIntermediateSymbol?)null).IsFailed.Should().BeTrue();
        ((IAbstractQuadraticMechanismModel<Flt64>)existing).Constraints.Should().BeEmpty();
        parent.AddConstraint(relation, null).IsOk.Should().BeTrue();
        (await QuadraticMechanismModel<Flt64>.InvokeAsync(parent)).IsFailed.Should().BeTrue();
    }

    [Fact]
    public async Task DynamicRows_ShouldUseRelationNameUnlessExplicitlyOverridden() {
        var x = new RealVar("dynamic-name-x");
        var linear = new LinearInequality<Flt64>(
            Lhs: LinearPolynomial(0, [(1, x)]),
            Rhs: LinearPolynomial(1, []),
            Comparison: Comparison.LE,
            Name: "linear-source"
        );
        var quadratic = new QuadraticInequalityOf<Flt64>(
            Lhs: new QuadraticPolynomial<Flt64>([new QuadraticMonomial<Flt64>(
                Coefficient: Flt64.One,
                Symbol1: x,
                Symbol2: x
            )], Flt64.Zero),
            Rhs: new QuadraticPolynomial<Flt64>([], Flt64.One),
            Comparison: Comparison.LE,
            Name: "quadratic-source"
        );
        using var linearParent = new LinearMetaModel<Flt64>();
        linearParent.Add(x).IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> linearModel = (await LinearMechanismModel<Flt64>.InvokeAsync(linearParent)).Value;
        linearModel.AddConstraint(linear, from: (IIntermediateSymbol?)null).IsOk.Should().BeTrue();
        linearModel.AddConstraint(
            relation: linear,
            name: "linear-override",
            from: (IIntermediateSymbol?)null
        ).IsOk.Should().BeTrue();
        ((IMechanismModel<Flt64>)linearModel).Constraints.Select(row => row.Name)
            .Should().Equal("linear-source", "linear-override");

        using var quadraticParent = new QuadraticMetaModel<Flt64>();
        quadraticParent.Add(x).IsOk.Should().BeTrue();
        using QuadraticMechanismModel<Flt64> quadraticModel = (await QuadraticMechanismModel<Flt64>.InvokeAsync(quadraticParent)).Value;
        quadraticModel.AddConstraint(linear, from: (IIntermediateSymbol?)null).IsOk.Should().BeTrue();
        quadraticModel.AddConstraint(
            relation: linear,
            name: "promoted-override",
            from: (IIntermediateSymbol?)null
        ).IsOk.Should().BeTrue();
        quadraticModel.AddConstraint(quadratic, from: (IIntermediateSymbol?)null).IsOk.Should().BeTrue();
        quadraticModel.AddConstraint(
            relation: quadratic,
            name: "quadratic-override",
            from: (IIntermediateSymbol?)null
        ).IsOk.Should().BeTrue();
        ((IAbstractQuadraticMechanismModel<Flt64>)quadraticModel).Constraints.Select(row => row.Name)
            .Should().Equal("linear-source", "promoted-override", "quadratic-source", "quadratic-override");
    }

    private static LinearExpressionSymbol LinearExpression(string name, double constant, (double Coefficient, ISymbol Symbol)[] terms) =>
        new(
            polynomial: new MutableLinearPolynomial<Flt64>(
                terms.Select(term => new LinearMonomial<Flt64>(new Flt64(term.Coefficient), term.Symbol)),
                new Flt64(constant)),
            name: name
        );

    private static LinearPolynomial<Flt64> LinearPolynomial(double constant, (double Coefficient, ISymbol Symbol)[] terms) =>
        new(terms.Select(term => new LinearMonomial<Flt64>(new Flt64(term.Coefficient), term.Symbol)).ToList(), new Flt64(constant));

    private sealed class IntegerConverter : IFlt64ValueConverter<Integer> {
        public Integer Zero => Integer.Zero;
        public Integer One => Integer.One;
        public Integer IntoValue(Flt64 value) => value.ToInt64();
        public Flt64 FromValue(Integer value) => value.ToFlt64();
    }

    private sealed class StructuralFunction<V>(LinearPolynomial<V> polynomial) : IMathFunctionSymbol<V>, IHasResultPolynomial<V>
        where V : struct, IRealNumber<V>, INumberField<V> {
        public string Name { get; set; } = "structural";
        public string? DisplayName { get; set; }
        public IReadOnlyList<IVariableItem> HelperVariables => Array.Empty<IVariableItem>();
        public LinearPolynomial<V> ResultPolynomial => polynomial;
        public V? Evaluate(IReadOnlyDictionary<ISymbol, V> values) => null;
        public Try RegisterAuxiliaryTokens(IAddableTokenCollection<V> tokens) => Results.Ok(Results.SuccessInstance);
        public Try RegisterConstraints(IAbstractLinearMechanismModel<V> model) => Results.Ok(Results.SuccessInstance);
    }
}
