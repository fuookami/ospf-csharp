#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Algebra.ValueRange;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Integer = Fuookami.Ospf.Math.Algebra.Number.Int64;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

public class IfNestedFunctionTest {
    [Theory]
    [InlineData(Comparison.GT)]
    [InlineData(Comparison.GE)]
    [InlineData(Comparison.LT)]
    [InlineData(Comparison.LE)]
    public async Task NestedExpressions_ShouldMatchEvaluationAndMatrix(Comparison relation) {
        var x = new RealVar("nested-if-x");
        var y = new RealVar("nested-if-y");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);
        y.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);
        LinearExpressionSymbol inner = Expression(
            name: "inner",
            constant: 1,
            terms: [(2, x)]
        );
        LinearExpressionSymbol outer = Expression(
            name: "outer",
            constant: -1,
            terms: [(2, inner), (1, y)]
        );
        Result<IfFunction<Flt64>, ErrorCode, Error<ErrorCode>> created = IfFunction<Flt64>.Create(
            condition: Polynomial(-1, (1, outer)),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5),
            relation: relation
        );
        Assert.True(created.IsOk, created.ToString());
        IfFunction<Flt64> function = created.Value;
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(y).IsOk);
        Assert.True(parent.Add(adapter).IsOk);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Flt64>.InvokeAsync(parent);
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<Flt64> model = built.Value;
        using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
        foreach (double a in new[] { -1.0, 0.0, 1.0 }) {
            foreach (double b in new[] { -1.0, 0.0, 1.0 }) {
                double difference = 4 * a + b;
                TruthValue expected = ConditionClassification.Classify(
                    difference: new Flt64(difference),
                    relation: relation,
                    strictBoundary: new Flt64(0.5)
                ).Value;
                Assert.Equal(expected == TruthValue.True ? Flt64.One : Flt64.Zero,
                    function.Evaluate(new Dictionary<ISymbol, Flt64> { [outer] = new Flt64(difference + 1) }));
                for (int result = 0; result <= 1; ++result) {
                    var values = new Dictionary<IVariableItem, Flt64> {
                        [x] = new Flt64(a), [y] = new Flt64(b),
                        [function.ConditionVar] = new Flt64(difference),
                        [function.ResultVar] = new Flt64(result),
                        [function.FalseVar] = new Flt64(1 - result),
                    };
                    bool feasible = result == (expected == TruthValue.True ? 1 : 0);
                    Assert.Equal(feasible, SatisfiedAmountResidualTest.ExactFeasible(model, values));
                    Assert.Equal(feasible, SatisfiedAmountResidualTest.ExactFeasible(matrix, values));
                }
            }
        }
    }

    [Fact]
    public async Task NestedFunction_ShouldRequireRegistrationAndPrepareFixedValues() {
        var x = new RealVar("nested-if-abs-x");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);
        var abs = new AbsFunction<Flt64>(Polynomial(0, (1, x)), IIntoValue<Flt64>.Identity);
        var absAdapter = new LinearFunctionSymbolAdapter<Flt64>(abs);
        LinearExpressionSymbol outer = Expression(
            name: "nested-abs",
            constant: -1,
            terms: [(2, absAdapter), (1, x)]
        );
        IfFunction<Flt64> function = IfFunction<Flt64>.Create(
            condition: Polynomial(0, (1, outer)),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5)
        ).Value;
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        using var missing = new LinearMetaModel<Flt64>();
        Assert.True(missing.Add(x).IsOk);
        Assert.True(missing.Add(adapter).IsOk);
        Assert.True((await LinearMechanismModel<Flt64>.InvokeAsync(missing)).IsFailed);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(adapter).IsOk);
        Assert.True(parent.Add(absAdapter).IsOk);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Flt64>.InvokeAsync(
                metaModel: parent,
                fixedVariables: new Dictionary<IVariableItem, Flt64> { [x] = new Flt64(-2) }
            );
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<Flt64> model = built.Value;
        Assert.Equal(Flt64.One, model.Tokens.CachedValue(adapter, (IReadOnlyList<Flt64>?)null));
        var assignment = new Dictionary<IVariableItem, Flt64> {
            [x] = new Flt64(-2), [abs.ResultVar] = new Flt64(2),
            [abs.PositiveVar] = Flt64.Zero, [abs.NegativeVar] = new Flt64(2), [abs.SignVar] = Flt64.Zero,
            [function.ConditionVar] = Flt64.One, [function.ResultVar] = Flt64.One, [function.FalseVar] = Flt64.Zero,
        };
        Assert.True(SatisfiedAmountResidualTest.ExactFeasible(model, assignment));
        assignment[function.ResultVar] = Flt64.Zero;
        assignment[function.FalseVar] = Flt64.One;
        Assert.False(SatisfiedAmountResidualTest.ExactFeasible(model, assignment));
    }

    [Fact]
    public async Task ChangedNestedExpression_ShouldFailBeforeRegistrationOrBuild() {
        var x = new RealVar("nested-if-mutation");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);
        LinearExpressionSymbol inner = Expression(
            name: "mutable-inner",
            constant: 0,
            terms: [(1, x)]
        );
        LinearExpressionSymbol outer = Expression(
            name: "mutable-outer",
            constant: 0,
            terms: [(1, inner)]
        );
        IfFunction<Flt64> function = IfFunction<Flt64>.Create(
            condition: Polynomial(0, (1, outer)),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: Flt64.One
        ).Value;
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(adapter).IsOk);
        inner.AsMutable().SetConstant(Flt64.One);
        Assert.True((await LinearMechanismModel<Flt64>.InvokeAsync(parent)).IsFailed);
        using var other = new LinearMetaModel<Flt64>();
        Assert.True(other.Add(x).IsOk);
        Assert.True(other.Add(adapter).IsFailed);
        Assert.Empty(other.Tokens.Symbols);
        Assert.All(function.HelperVariables, variable => Assert.Null(other.Tokens.Find(variable)));
    }

    [Fact]
    public async Task ChangedNestedConstantHiddenByRounding_ShouldStillFailBuild() {
        var x = new RealVar("nested-if-hidden-change");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);
        LinearExpressionSymbol inner = Expression(
            name: "hidden-change",
            constant: 0,
            terms: [(1, x)]
        );
        Result<IfFunction<Flt64>, ErrorCode, Error<ErrorCode>> created = IfFunction<Flt64>.Create(
            condition: Polynomial(9007199254740992.0, (1, inner)),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: Flt64.One,
            bounds: new LinearPolynomialBounds<Flt64>(new Flt64(-1e16), new Flt64(1e16))
        );
        Assert.True(created.IsOk, created.ToString());
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<Flt64>(created.Value)).IsOk);
        inner.AsMutable().SetConstant(Flt64.One);
        Assert.True((await LinearMechanismModel<Flt64>.InvokeAsync(parent)).IsFailed);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public async Task ConverterFailureDuringRevalidation_ShouldReturnFailureWithoutChanges(
        int failureKind,
        bool registerConstraints) {
        var converter = new SwitchableConverter(failureKind);
        var x = new RealVar("nested-if-converter");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);
        LinearExpressionSymbol inner = Expression(
            name: "converter-inner",
            constant: 0,
            terms: [(1, x)]
        );
        Result<IfFunction<Flt64>, ErrorCode, Error<ErrorCode>> created = IfFunction<Flt64>.Create(
            condition: Polynomial(0, (1, inner)),
            converter: converter,
            strictBoundary: Flt64.One
        );
        Assert.True(created.IsOk, created.ToString());
        IfFunction<Flt64> function = created.Value;
        using var parent = new LinearMetaModel<Flt64>(converter: converter);
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Flt64>.InvokeAsync(parent);
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<Flt64> model = built.Value;
        int rows = ((IMechanismModel<Flt64>)model).Constraints.Count;
        converter.Fails = true;
        if (registerConstraints) {
            Assert.True(function.RegisterConstraints(model).IsFailed);
            Assert.Equal(rows, ((IMechanismModel<Flt64>)model).Constraints.Count);
        }
        else {
            using var tokens = new ManualTokenList<Flt64>();
            Assert.True(function.RegisterAuxiliaryTokens(tokens).IsFailed);
            Assert.Empty(tokens.Tokens);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ConverterFailureDuringCreation_ShouldReturnFailure(int failureKind) {
        var converter = new SwitchableConverter(failureKind) { Fails = true };
        var x = new RealVar("nested-if-create-converter");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);
        Assert.True(IfFunction<Flt64>.Create(
            condition: Polynomial(0, (1, x)),
            converter: converter,
            strictBoundary: Flt64.One
        ).IsFailed);
    }

    [Fact]
    public async Task WriterWithoutTransaction_ShouldFailBeforeWriting() {
        var x = new RealVar("nested-if-throwing-writer");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);
        LinearExpressionSymbol inner = Expression(
            name: "writer-inner",
            constant: 0,
            terms: [(1, x)]
        );
        IfFunction<Flt64> function = IfFunction<Flt64>.Create(
            condition: Polynomial(0, (1, inner)),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: Flt64.One
        ).Value;
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        foreach (IVariableItem helper in function.HelperVariables) {
            Assert.True(parent.Add(helper).IsOk);
        }
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        using var writer = new ThrowingWriter(model);
        Assert.True(function.RegisterConstraints(writer).IsFailed);
        Assert.Empty(((IMechanismModel<Flt64>)model).Constraints);
    }

    [Fact]
    public async Task NestedBounds_ShouldInvalidateLinearAndQuadraticDumps() {
        var x = new RealVar("nested-if-range");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);
        LinearExpressionSymbol expression = Expression(
            name: "nested-range",
            constant: 1,
            terms: [(2, x)]
        );
        IfFunction<Flt64> function = IfFunction<Flt64>.Create(
            condition: Polynomial(0, (1, expression)),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: Flt64.One
        ).Value;
        using var linearParent = new LinearMetaModel<Flt64>();
        using var quadraticParent = new QuadraticMetaModel<Flt64>();
        Assert.True(linearParent.Add(x).IsOk);
        Assert.True(linearParent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk);
        Assert.True(quadraticParent.Add(x).IsOk);
        Assert.True(quadraticParent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> linearResult =
            await LinearMechanismModel<Flt64>.InvokeAsync(linearParent);
        Result<QuadraticMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> quadraticResult =
            await QuadraticMechanismModel<Flt64>.InvokeAsync(quadraticParent);
        Assert.True(linearResult.IsOk, linearResult.ToString());
        Assert.True(quadraticResult.IsOk, quadraticResult.ToString());
        using LinearMechanismModel<Flt64> linear = linearResult.Value;
        using QuadraticMechanismModel<Flt64> quadratic = quadraticResult.Value;
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-3), new Flt64(3)).Value);
        Assert.True(linear.ToLinearTriadModel().IsFailed);
        Assert.True(quadratic.ToQuadraticTetradModel().IsFailed);
    }

    [Theory]
    [InlineData(Comparison.GT, 0.5, 0.25)]
    [InlineData(Comparison.GE, -0.5, -0.25)]
    [InlineData(Comparison.LT, -0.5, -0.25)]
    [InlineData(Comparison.LE, 0.5, 0.25)]
    public async Task NestedIntegerLattice_ShouldProveOffsetsBeforeWritingRows(
        Comparison relation,
        double safeOffset,
        double unsafeOffset) {
        var x = new IntVar("nested-if-integer");
        x.Range.SetRange(ValueRange<Integer>.Of(new Integer(-2), new Integer(2)).Value);
        LinearExpressionSymbol safe = Expression(
            name: "safe-lattice",
            constant: safeOffset,
            terms: [(2, x)]
        );
        LinearExpressionSymbol unsafeExpression = Expression(
            name: "unsafe-lattice",
            constant: unsafeOffset,
            terms: [(2, x)]
        );
        Assert.True(IfFunction<Flt64>.Create(
            condition: Polynomial(0, (1, unsafeExpression)),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5),
            relation: relation
        ).IsFailed);
        Result<IfFunction<Flt64>, ErrorCode, Error<ErrorCode>> created = IfFunction<Flt64>.Create(
            condition: Polynomial(0, (1, safe)),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5),
            relation: relation
        );
        Assert.True(created.IsOk, created.ToString());
        IfFunction<Flt64> function = created.Value;
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Flt64>.InvokeAsync(parent);
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<Flt64> model = built.Value;
        using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
        for (int input = -2; input <= 2; ++input) {
            double difference = 2 * input + safeOffset;
            TruthValue expected = ConditionClassification.Classify(
                difference: new Flt64(difference),
                relation: relation,
                strictBoundary: new Flt64(0.5)
            ).Value;
            Assert.NotEqual(TruthValue.Undefined, expected);
            for (int result = 0; result <= 1; ++result) {
                var values = new Dictionary<IVariableItem, Flt64> {
                    [x] = new Flt64(input), [function.ConditionVar] = new Flt64(difference),
                    [function.ResultVar] = new Flt64(result), [function.FalseVar] = new Flt64(1 - result),
                };
                bool feasible = result == (expected == TruthValue.True ? 1 : 0);
                Assert.Equal(feasible, SatisfiedAmountResidualTest.ExactFeasible(model, values));
                Assert.Equal(feasible, SatisfiedAmountResidualTest.ExactFeasible(matrix, values));
            }
        }
    }

    [Fact]
    public void NestedExpansion_ShouldRejectRoundingCyclesAndUnknownFunctions() {
        var x = new RealVar("nested-if-numeric");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);
        LinearExpressionSymbol roundedCoefficient = Expression(
            name: "rounded-coefficient",
            constant: 0,
            terms: [(0.1, x)]
        );
        LinearExpressionSymbol roundedConstant = Expression("rounded-constant", 1);
        LinearExpressionSymbol cycle = Expression("cycle", 0);
        cycle.AsMutable().AddMonomial(new LinearMonomial<Flt64>(Flt64.One, cycle));
        var unknown = new LinearFunctionSymbolAdapter<Flt64>(new UnknownFunctionStub());
        foreach (LinearPolynomial<Flt64> condition in new[] {
            Polynomial(0, (0.1, roundedCoefficient)),
            Polynomial(9007199254740992.0, (1, roundedConstant)),
            Polynomial(0, (1, cycle)),
            Polynomial(0, (1, unknown)),
        }) {
            Assert.True(IfFunction<Flt64>.Create(
                condition: condition,
                converter: IIntoValue<Flt64>.Identity,
                strictBoundary: Flt64.One,
                bounds: new LinearPolynomialBounds<Flt64>(new Flt64(-10), new Flt64(10))
            ).IsFailed);
        }
    }

    [Fact]
    public void DeepAndSharedExpressions_ShouldNotUseRecursiveTraversal() {
        var x = new RealVar("nested-if-deep");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-1), new Flt64(1)).Value);
        ISymbol symbol = x;
        for (int index = 0; index < 1024; ++index) {
            symbol = Expression(
                name: $"depth-{index}",
                constant: 0,
                terms: [(1, symbol)]
            );
        }
        LinearExpressionSymbol shared = Expression(
            name: "shared",
            constant: 0,
            terms: [(1, symbol), (-1, symbol), (1, x)]
        );
        Assert.True(IfFunction<Flt64>.Create(
            condition: Polynomial(0, (1, shared)),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: Flt64.One
        ).IsOk);
    }

    [Fact]
    public async Task NestedStructuralResults_ShouldSupportRepresentativeNumericTypes() {
        await VerifyNumeric(IIntoValue<Flt64>.Identity);
        await VerifyNumeric(new TestNumberConverter<Flt32>(value => value.ToFlt32()));
        await VerifyNumeric(new TestNumberConverter<Integer>(value => value.ToInt64()));
        await VerifyNumeric(new TestNumberConverter<FltX>(value => value.ToFltX()));
        await VerifyNumeric(new TestNumberConverter<RtnX>(value => new RtnX(value.ToDouble(), 1)));
    }

    private static async Task VerifyNumeric<V>(IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> {
        var x = new RealVar($"nested-if-{typeof(V).Name}-x");
        var y = new RealVar($"nested-if-{typeof(V).Name}-y");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);
        y.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);
        V two = converter.IntoValue(new Flt64(2));
        var nested = new LinearFunctionSymbolAdapter<V>(new StructuralFunction<V>(new LinearPolynomial<V>(
            new[] { new LinearMonomial<V>(two, x), new LinearMonomial<V>(converter.One, y) }, converter.One
        )));
        var condition = new LinearPolynomial<V>(
            new[] { new LinearMonomial<V>(two, nested) }, converter.IntoValue(new Flt64(-1))
        );
        Result<IfFunction<V>, ErrorCode, Error<ErrorCode>> created = IfFunction<V>.Create(
            condition: condition,
            converter: converter,
            strictBoundary: converter.One
        );
        Assert.True(created.IsOk, created.ToString());
        var outer = new LinearFunctionSymbolAdapter<V>(created.Value);
        using var parent = new LinearMetaModel<V>(converter: converter);
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(y).IsOk);
        Assert.True(parent.Add(nested).IsOk);
        Assert.True(parent.Add(outer).IsOk);
        V negativeOne = converter.IntoValue(new Flt64(-1));
        Result<LinearMechanismModel<V>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<V>.InvokeAsync(
                metaModel: parent,
                fixedVariables: new Dictionary<IVariableItem, V> { [x] = negativeOne, [y] = converter.One }
            );
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<V> model = built.Value;
        Assert.True(model.Tokens.CachedValue(outer, (IReadOnlyList<V>?)null)!.Value.Eq(converter.Zero));
        var values = new Dictionary<IVariableItem, V> {
            [x] = negativeOne, [y] = converter.One, [created.Value.ConditionVar] = negativeOne,
            [created.Value.ResultVar] = converter.Zero, [created.Value.FalseVar] = converter.One,
        };
        var solution = model.Tokens.TokensInSolver.Select(token => values[token.Variable]).ToList();
        Assert.All(((IMechanismModel<V>)model).Constraints, row => Assert.True(row.IsTrue(solution) == true));
        values[created.Value.ResultVar] = converter.One;
        values[created.Value.FalseVar] = converter.Zero;
        solution = model.Tokens.TokensInSolver.Select(token => values[token.Variable]).ToList();
        Assert.Contains(((IMechanismModel<V>)model).Constraints, row => row.IsTrue(solution) == false);
    }

    private static LinearExpressionSymbol Expression(string name, double constant, params (double Coefficient, ISymbol Symbol)[] terms) =>
        new(new MutableLinearPolynomial<Flt64>(
            terms.Select(term => new LinearMonomial<Flt64>(new Flt64(term.Coefficient), term.Symbol)),
            new Flt64(constant)
        ), name: name);

    private static LinearPolynomial<Flt64> Polynomial(double constant, params (double Coefficient, ISymbol Symbol)[] terms) =>
        new(terms.Select(term => new LinearMonomial<Flt64>(new Flt64(term.Coefficient), term.Symbol)).ToArray(),
            new Flt64(constant));

    private sealed class StructuralFunction<V>(LinearPolynomial<V> polynomial) : IMathFunctionSymbol<V>, IHasResultPolynomial<V>
        where V : struct, IRealNumber<V>, INumberField<V> {
        public string Name { get; set; } = "structural";
        public string? DisplayName { get; set; }
        public IReadOnlyList<IVariableItem> HelperVariables => Array.Empty<IVariableItem>();
        public IReadOnlyCollection<ISymbol> InputSymbols => polynomial.Monomials.Select(term => term.Symbol).ToArray();
        public LinearPolynomial<V> ResultPolynomial => polynomial;
        public V? Evaluate(IReadOnlyDictionary<ISymbol, V> values) => TryEvaluate(values).Value;
        public Result<V?, ErrorCode, Error<ErrorCode>> TryEvaluate(IReadOnlyDictionary<ISymbol, V> values) =>
            FunctionEvaluation.Polynomial(polynomial, values);
        public Try RegisterAuxiliaryTokens(IAddableTokenCollection<V> tokens) => Results.OkInstance;
        public Try RegisterConstraints(IAbstractLinearMechanismModel<V> model) => Results.OkInstance;
    }

    private sealed class SwitchableConverter(int failureKind) : IIntoValue<Flt64> {
        public bool Fails { get; set; }
        public Flt64 Zero => Flt64.Zero;
        public Flt64 One => Flt64.One;
        public Flt64 IntoValue(Flt64 value) => value;
        public Flt64 FromValue(Flt64 value) {
            if (Fails && failureKind == 1) {
                throw new InvalidOperationException("invalid converter");
            }
            if (Fails && failureKind == 2) {
                throw new ArgumentException("invalid converter");
            }
            if (Fails) {
                throw new OverflowException("invalid converter");
            }
            return value;
        }
    }

    private sealed class ThrowingWriter(LinearMechanismModel<Flt64> model) : IAbstractLinearMechanismModel<Flt64> {
        private int _writes;
        public string Name => model.Name;
        public IReadOnlyList<IConstraint<Flt64, LinearCategory>> Constraints => ((IMechanismModel<Flt64>)model).Constraints;
        public IObject ObjectFunction => ((IMechanismModel<Flt64>)model).ObjectFunction;
        public IAbstractTokenTable<Flt64> Tokens => model.Tokens;
        public void Dispose() { }

        public Try AddConstraint(
            LinearInequality<Flt64> relation,
            string? name = null,
            (IIntermediateSymbol Symbol, bool Direction)? from = null) {
            if (++_writes == 2) {
                throw new ArgumentException("writer failed");
            }
            return model.AddConstraint(
                relation: relation,
                name: name,
                from: from
            );
        }

        public Try AddConstraint(
            LinearInequality<Flt64> relation,
            string? name = null,
            IIntermediateSymbol? from = null) => throw new NotSupportedException();
    }
}
