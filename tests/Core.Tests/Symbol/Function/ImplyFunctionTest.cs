#nullable enable

using System;
using System.Collections.Generic;
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
namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

public class ImplyFunctionTest {
    [Theory]
    [InlineData(Comparison.GT)]
    [InlineData(Comparison.GE)]
    [InlineData(Comparison.LT)]
    [InlineData(Comparison.LE)]
    public async Task RowsAndMatrix_ShouldGateConsequentByAntecedent(Comparison relation) {
        RealVar x = Real("imply-x", -2, 2);
        RealVar y = Real("imply-y", -2, 2);
        ImplyFunction<Flt64> function = Create(x, y, relation);
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        Assert.Empty(adapter.Polynomial.Monomials);
        Assert.Equal(Flt64.One, adapter.Polynomial.Constant);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(y).IsOk);
        Assert.True(parent.Add(adapter).IsOk);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built = await LinearMechanismModel<Flt64>.InvokeAsync(parent);
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<Flt64> model = built.Value;
        using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
        foreach (double antecedent in new[] { -1.0, -0.5, -0.25, 0, 0.25, 0.5, 1.0 }) {
            foreach (double consequent in new[] { -1.0, -0.5, -0.25, 0, 0.25, 0.5, 1.0 }) {
                TruthValue first = ConditionClassification.Classify(
                    difference: new Flt64(antecedent),
                    relation: relation,
                    strictBoundary: new Flt64(0.5)
                ).Value;
                TruthValue second = ConditionClassification.Classify(
                    difference: new Flt64(consequent),
                    relation: relation,
                    strictBoundary: new Flt64(0.5)
                ).Value;
                TruthValue implication = first == TruthValue.False ? TruthValue.True :
                    first == TruthValue.Undefined ? TruthValue.Undefined : second;
                var inputs = new Dictionary<ISymbol, Flt64> { [x] = new(antecedent), [y] = new(consequent) };
                Assert.Equal(implication, function.Classify(inputs).Value);
                Assert.Equal(implication == TruthValue.Undefined ? null :
                    implication == TruthValue.True ? Flt64.One : Flt64.Zero, function.Evaluate(inputs));
                for (int indicator = 0; indicator <= 1; ++indicator) {
                    var assignment = new Dictionary<IVariableItem, Flt64> {
                        [x] = new(antecedent),
                        [y] = new(consequent),
                        [function.AntecedentEncoding.ResultVar] = new(indicator),
                        [function.AntecedentEncoding.FalseVar] = new(1 - indicator),
                        [function.AntecedentEncoding.ConditionVar] = new(antecedent),
                        [function.ConsequentEncoding.ConditionVar] = new(consequent),
                    };
                    bool feasible = implication == TruthValue.True &&
                        indicator == (first == TruthValue.True ? 1 : 0);
                    Assert.Equal(feasible, SatisfiedAmountResidualTest.ExactFeasible(model, assignment));
                    Assert.Equal(feasible, SatisfiedAmountResidualTest.ExactFeasible(matrix, assignment));
                }
            }
        }
    }

    [Fact]
    public void Evaluation_ShouldShortCircuitWithoutReadingConsequent() {
        RealVar x = Real("imply-short-x", -2, 2);
        RealVar y = Real("imply-short-y", -2, 2);
        ImplyFunction<Flt64> function = Create(x, y);
        Assert.True(function.Classify(new Dictionary<ISymbol, Flt64>()).IsFailed);
        Assert.Null(function.TryEvaluate(new Dictionary<ISymbol, Flt64>()).Value);
        var falseAntecedent = new Dictionary<ISymbol, Flt64> { [x] = Flt64.Zero, [y] = new(double.NaN) };
        Assert.Equal(TruthValue.True, function.Classify(falseAntecedent).Value);
        Assert.Equal(Flt64.One, function.TryEvaluate(falseAntecedent).Value);
        var gapAntecedent = new Dictionary<ISymbol, Flt64> { [x] = new(0.25), [y] = new(double.NaN) };
        Assert.Equal(TruthValue.Undefined, function.Classify(gapAntecedent).Value);
        Assert.Null(function.TryEvaluate(gapAntecedent).Value);
        Assert.True(function.Classify(new Dictionary<ISymbol, Flt64> { [x] = Flt64.One }).IsFailed);
        Assert.Null(function.TryEvaluate(new Dictionary<ISymbol, Flt64> { [x] = Flt64.One }).Value);
        Assert.True(function.TryEvaluate(new Dictionary<ISymbol, Flt64> { [x] = Flt64.One, [y] = new(double.NaN) }).IsFailed);
        Assert.Equal(Flt64.Zero, function.TryEvaluate(new Dictionary<ISymbol, Flt64> { [x] = Flt64.One, [y] = Flt64.Zero }).Value);
    }

    [Fact]
    public async Task Preparation_ShouldNotCacheAFalseImplicationAsItsConstantResult() {
        RealVar x = Real("imply-prepare-x", -2, 2);
        RealVar y = Real("imply-prepare-y", -2, 2);
        ImplyFunction<Flt64> function = Create(x, y);
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(y).IsOk);
        Assert.True(parent.Add(adapter).IsOk);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built = await LinearMechanismModel<Flt64>.InvokeAsync(
            metaModel: parent,
            fixedVariables: new Dictionary<IVariableItem, Flt64> { [x] = Flt64.One, [y] = Flt64.Zero }
        );
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<Flt64> model = built.Value;
        Assert.Null(model.Tokens.CachedValue(adapter, (IReadOnlyList<Flt64>?)null));
        var missing = new Dictionary<ISymbol, Flt64> { [x] = Flt64.Zero };
        Assert.Equal(Flt64.One, adapter.TryPrepare(missing, missing).Value);
    }

    [Fact]
    public async Task NestedConsequent_ShouldVerifyItsSourceOnlyWhenAntecedentIsTrue() {
        RealVar x = Real("imply-nested-x", -2, 2);
        RealVar y = Real("imply-nested-y", -2, 2);
        var inner = new LinearExpressionSymbol(
            polynomial: new MutableLinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, y) }, new Flt64(1e16)),
            name: "imply-nested-inner"
        );
        var consequent = new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, inner) }, new Flt64(-1e16));
        Result<ImplyFunction<Flt64>, ErrorCode, Error<ErrorCode>> created = ImplyFunction<Flt64>.Create(
            antecedent: Polynomial(x),
            consequent: consequent,
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: Flt64.One
        );
        Assert.True(created.IsOk, created.ToString());
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(created.Value);
        var inconsistent = new Dictionary<ISymbol, Flt64> { [x] = Flt64.One, [y] = Flt64.One, [inner] = new(1e16) };
        Assert.Equal(Flt64.Zero, adapter.TryEvaluate(inconsistent).Value);
        Assert.True(adapter.TryPrepare(inconsistent, inconsistent).IsFailed);
        inconsistent[x] = Flt64.Zero;
        inconsistent.Remove(y);
        Assert.Equal(Flt64.One, adapter.TryPrepare(inconsistent, inconsistent).Value);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(y).IsOk);
        Assert.True(parent.Add(inner).IsOk);
        Assert.True(parent.Add(adapter).IsOk);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built = await LinearMechanismModel<Flt64>.InvokeAsync(
            metaModel: parent,
            fixedVariables: new Dictionary<IVariableItem, Flt64> { [x] = Flt64.One, [y] = Flt64.One }
        );
        Assert.True(built.IsFailed);
        Assert.Null(parent.Tokens.CachedValue(adapter, (IReadOnlyList<Flt64>?)null));
    }

    [Fact]
    public async Task NestedResult_ShouldRequireRegisteredImplicationAndUseItsFeasibleConstant() {
        RealVar x = Real("imply-result-x", -2, 2);
        RealVar y = Real("imply-result-y", -2, 2);
        ImplyFunction<Flt64> function = Create(x, y);
        var inner = new LinearFunctionSymbolAdapter<Flt64>(function);
        IfFunction<Flt64> outer = IfFunction<Flt64>.Create(
            condition: Polynomial(inner),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: Flt64.One
        ).Value;
        var outerAdapter = new LinearFunctionSymbolAdapter<Flt64>(outer);
        using var missing = new LinearMetaModel<Flt64>();
        Assert.True(missing.Add(x).IsOk);
        Assert.True(missing.Add(y).IsOk);
        Assert.True(missing.Add(outerAdapter).IsOk);
        Assert.True((await LinearMechanismModel<Flt64>.InvokeAsync(missing)).IsFailed);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(y).IsOk);
        Assert.True(parent.Add(outerAdapter).IsOk);
        Assert.True(parent.Add(inner).IsOk);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built = await LinearMechanismModel<Flt64>.InvokeAsync(
            metaModel: parent,
            fixedVariables: new Dictionary<IVariableItem, Flt64> { [x] = Flt64.Zero, [y] = new Flt64(0.25) }
        );
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<Flt64> model = built.Value;
        Assert.Equal(Flt64.One, model.Tokens.CachedValue(inner, (IReadOnlyList<Flt64>?)null));
        Assert.Equal(Flt64.One, model.Tokens.CachedValue(outerAdapter, (IReadOnlyList<Flt64>?)null));
    }

    [Fact]
    public async Task ExplicitConsequentBounds_ShouldConstrainTheFalseAntecedentBranch() {
        RealVar x = Real("imply-bounds-x", -2, 2);
        RealVar y = Real("imply-bounds-y", -2, 2);
        ImplyFunction<Flt64> function = ImplyFunction<Flt64>.Create(
            antecedent: Polynomial(x),
            consequent: Polynomial(y),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5),
            consequentBounds: new LinearPolynomialBounds<Flt64>(new Flt64(-1), Flt64.One)
        ).Value;
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(y).IsOk);
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk);
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        var assignment = new Dictionary<IVariableItem, Flt64> {
            [x] = Flt64.Zero, [y] = new Flt64(2),
            [function.AntecedentEncoding.ResultVar] = Flt64.Zero,
            [function.AntecedentEncoding.FalseVar] = Flt64.One,
            [function.AntecedentEncoding.ConditionVar] = Flt64.Zero,
            [function.ConsequentEncoding.ConditionVar] = new Flt64(2),
        };
        Assert.Equal(Flt64.One, function.TryEvaluate(new Dictionary<ISymbol, Flt64> { [x] = Flt64.Zero }).Value);
        Assert.False(SatisfiedAmountResidualTest.ExactFeasible(model, assignment));
        y.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-3), new Flt64(3)).Value);
        Assert.True(model.ToLinearTriadModel().IsOk);
    }

    [Fact]
    public async Task MissingTokens_ShouldFailBeforeWritingAnyRows() {
        RealVar x = Real("imply-missing-x", -2, 2);
        RealVar y = Real("imply-missing-y", -2, 2);
        ImplyFunction<Flt64> function = Create(x, y);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(y).IsOk);
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        Assert.True(model.AddConstraint(
            relation: Polynomial(x).Le(Flt64.One),
            from: (IIntermediateSymbol?)null
        ).IsOk);
        Assert.True(function.RegisterConstraints(model).IsFailed);
        Assert.Single(((IMechanismModel<Flt64>)model).Constraints);
    }

    [Fact]
    public async Task AddConstraint_ShouldRegisterTheImplicationRows() {
        RealVar x = Real("imply-constraint-x", -2, 2);
        RealVar y = Real("imply-constraint-y", -2, 2);
        ImplyFunction<Flt64> function = Create(x, y);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(y).IsOk);
        Assert.True(parent.AddConstraint(
            constraint: new LinearFunctionSymbolAdapter<Flt64>(function),
            group: null
        ).IsOk);
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        var assignment = new Dictionary<IVariableItem, Flt64> {
            [x] = Flt64.One, [y] = Flt64.Zero,
            [function.AntecedentEncoding.ResultVar] = Flt64.One,
            [function.AntecedentEncoding.FalseVar] = Flt64.Zero,
            [function.AntecedentEncoding.ConditionVar] = Flt64.One,
            [function.ConsequentEncoding.ConditionVar] = Flt64.Zero,
        };
        Assert.False(SatisfiedAmountResidualTest.ExactFeasible(model, assignment));
    }

    [Fact]
    public void Creation_ShouldValidateConsequentBeforeFalseAntecedentShortCircuit() {
        RealVar y = Real("imply-invalid-y", -2, 2);
        var negative = new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(-1));
        Assert.True(ImplyFunction<Flt64>.Create(
            antecedent: negative,
            consequent: Polynomial(y),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: Flt64.One,
            consequentBounds: new LinearPolynomialBounds<Flt64>(Flt64.One, Flt64.Zero)
        ).IsFailed);
        Assert.True(ImplyFunction<Flt64>.Create(
            antecedent: negative,
            consequent: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(new(double.NaN), y) }, Flt64.Zero),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: Flt64.One
        ).IsFailed);
        Assert.True(ImplyFunction<Flt64>.Create(
            antecedent: negative,
            consequent: Polynomial(y),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: Flt64.One,
            relation: Comparison.EQ
        ).IsFailed);
        Assert.True(ImplyFunction<Flt64>.Create(
            antecedent: negative,
            consequent: Polynomial(y),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: Flt64.One,
            consequentBounds: new LinearPolynomialBounds<Flt64>(new Flt64(-double.MaxValue), Flt64.One)
        ).IsFailed);
    }

    [Theory]
    [InlineData(-1, -1, true)]
    [InlineData(-1, 1, true)]
    [InlineData(1, -1, false)]
    [InlineData(1, 1, true)]
    public async Task ConstantOperands_ShouldPreserveFeasibility(double antecedent, double consequent, bool feasible) {
        var first = new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(antecedent));
        var second = new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(consequent));
        ImplyFunction<Flt64> function = ImplyFunction<Flt64>.Create(
            antecedent: first,
            consequent: second,
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: Flt64.One
        ).Value;
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk);
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        var assignment = new Dictionary<IVariableItem, Flt64> {
            [function.AntecedentEncoding.ResultVar] = new(antecedent > 0 ? 1 : 0),
            [function.AntecedentEncoding.FalseVar] = new(antecedent > 0 ? 0 : 1),
            [function.AntecedentEncoding.ConditionVar] = new(antecedent),
            [function.ConsequentEncoding.ConditionVar] = new(consequent),
        };
        Assert.Equal(feasible, SatisfiedAmountResidualTest.ExactFeasible(model, assignment));
    }

    [Fact]
    public void InputSnapshots_ShouldNotFollowCallerListChanges() {
        RealVar x = Real("imply-snapshot-x", -2, 2);
        RealVar y = Real("imply-snapshot-y", -2, 2);
        var antecedent = new List<LinearMonomial<Flt64>> { new(Flt64.One, x) };
        var consequent = new List<LinearMonomial<Flt64>> { new(Flt64.One, y) };
        ImplyFunction<Flt64> function = ImplyFunction<Flt64>.Create(
            antecedent: new LinearPolynomial<Flt64>(antecedent, Flt64.Zero),
            consequent: new LinearPolynomial<Flt64>(consequent, Flt64.Zero),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5)
        ).Value;
        antecedent.Clear();
        consequent.Clear();
        Assert.Equal(Flt64.Zero, function.TryEvaluate(new Dictionary<ISymbol, Flt64> { [x] = Flt64.One, [y] = Flt64.Zero }).Value);
        Assert.Throws<NotSupportedException>(() => ((IList<LinearMonomial<Flt64>>)function.Antecedent.Monomials).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<LinearMonomial<Flt64>>)function.Consequent.Monomials).Clear());
    }

    [Fact]
    public async Task InferredBounds_ShouldInvalidateRegistrationAndMatrix() {
        RealVar x = Real("imply-stale-x", -2, 2);
        RealVar y = Real("imply-stale-y", -2, 2);
        ImplyFunction<Flt64> function = Create(x, y);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(y).IsOk);
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk);
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        Assert.True(model.ToLinearTriadModel().IsOk);
        y.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-3), new Flt64(3)).Value);
        Assert.True(model.ToLinearTriadModel().IsFailed);
        using var tokens = new ManualTokenList<Flt64>();
        Assert.True(function.RegisterAuxiliaryTokens(tokens).IsFailed);
        Assert.Empty(tokens.Tokens);
    }

    [Fact]
    public async Task RepresentativeNumericTypes_ShouldBuildAndEvaluate() {
        await VerifyNumeric(IIntoValue<Flt64>.Identity);
        await VerifyNumeric(new NumberConverter<Flt32>(value => value.ToFlt32()));
        await VerifyNumeric(new NumberConverter<Integer>(value => value.ToInt64()));
        await VerifyNumeric(new NumberConverter<FltX>(value => value.ToFltX()));
        await VerifyNumeric(new NumberConverter<RtnX>(value => new RtnX(value.ToDouble(), 1)));
    }

    private static async Task VerifyNumeric<V>(IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> {
        RealVar x = Real($"imply-{typeof(V).Name}-x", -2, 2);
        RealVar y = Real($"imply-{typeof(V).Name}-y", -2, 2);
        var antecedent = new LinearPolynomial<V>(new[] { new LinearMonomial<V>(converter.One, x) }, converter.Zero);
        var consequent = new LinearPolynomial<V>(new[] { new LinearMonomial<V>(converter.One, y) }, converter.Zero);
        Result<ImplyFunction<V>, ErrorCode, Error<ErrorCode>> created = ImplyFunction<V>.Create(
            antecedent: antecedent,
            consequent: consequent,
            converter: converter,
            strictBoundary: converter.One
        );
        Assert.True(created.IsOk, created.ToString());
        using var parent = new LinearMetaModel<V>(converter: converter);
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(y).IsOk);
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<V>(created.Value)).IsOk);
        Result<LinearMechanismModel<V>, ErrorCode, Error<ErrorCode>> built = await LinearMechanismModel<V>.InvokeAsync(parent);
        Assert.True(built.IsOk, built.ToString());
        built.Value.Dispose();
        Assert.True(created.Value.Evaluate(new Dictionary<ISymbol, V> { [x] = converter.Zero })!.Value.Eq(converter.One));
        Assert.True(created.Value.Evaluate(new Dictionary<ISymbol, V> { [x] = converter.One, [y] = converter.Zero })!.Value.Eq(converter.Zero));
    }

    private static RealVar Real(string name, double lower, double upper) {
        var variable = new RealVar(name);
        variable.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(lower), new Flt64(upper)).Value);
        return variable;
    }

    private static LinearPolynomial<Flt64> Polynomial(ISymbol symbol) => new(
        new[] { new LinearMonomial<Flt64>(Flt64.One, symbol) }, Flt64.Zero
    );

    private static ImplyFunction<Flt64> Create(ISymbol antecedent, ISymbol consequent, Comparison relation = Comparison.GT) =>
        ImplyFunction<Flt64>.Create(
            antecedent: Polynomial(antecedent),
            consequent: Polynomial(consequent),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5),
            relation: relation
        ).Value;

    private sealed class NumberConverter<V>(Func<Flt64, V> convert) : IIntoValue<V>
        where V : struct, IRealNumber<V> {
        public V Zero => convert(Flt64.Zero);
        public V One => convert(Flt64.One);
        public V IntoValue(Flt64 value) => convert(value);
        public Flt64 FromValue(V value) => value.ToFlt64();
    }
}
